using System.Text;
using System.Text.Json;

namespace Companion.Core;

/// <summary>UI callbacks. May be invoked from background threads; the UI must marshal.</summary>
public interface IBrainListener
{
    void OnThinking();
    void OnFirstText();
    void OnText(string delta);
    void OnStatus(string status);

    /// <summary>Any streamed event arrived (possibly very often, from a background thread). Must be cheap.</summary>
    void OnActivity();
}

public enum BrainOutcome { Ok, Refused, CutOff, CapReached, Error }

public sealed record BrainReply(BrainOutcome Outcome, string Text, string? Notice);

/// <summary>Orchestrates one question: cap check → conversation → Claude → tools → spend → history.</summary>
public sealed class Brain(
    IClaudeClient client,
    Conversation conversation,
    Func<string> systemPrompt,
    Func<int> systemPromptVersion,
    ToolRegistry tools,
    SpendTracker spend,
    SettingsStore settings,
    ChatLog? chatLog,
    ILog log)
{
    public const string ThinkHarderPrompt = "Think this through more carefully and give me your best answer.";

    public const string NoAccessMessage = "This API key can't use " + AnthropicClaudeClient.Model + " (check your console plan/workspace).";

    public bool Busy { get; private set; }

    public UsageTotals LastUsage { get; private set; }

    public Task<BrainReply> AskAsync(string text, IBrainListener listener, CancellationToken ct = default) =>
        RunAsync(text, settings.Current.EverydayEffort, listener, ct);

    public Task<BrainReply> ThinkHarderAsync(IBrainListener listener, CancellationToken ct = default) =>
        RunAsync(ThinkHarderPrompt, settings.Current.ThinkHarderEffort, listener, ct);

    public void NewChat() => conversation.Reset();

    private async Task<BrainReply> RunAsync(string text, EffortLevel effort, IBrainListener listener, CancellationToken ct)
    {
        if (Busy) return new BrainReply(BrainOutcome.Error, "", "Still working on the last one…");
        if (spend.CapReached)
        {
            return new BrainReply(BrainOutcome.CapReached, "",
                $"I've reached this month's ${spend.Cap:0.##} spending cap. You can raise it in Settings.");
        }

        Busy = true;
        try
        {
            conversation.BeginTurn(systemPrompt, systemPromptVersion());
            var userTurn = conversation.FormatUserTurn(text);
            var messages = new List<HistoryMessage>();
            foreach (var exchange in conversation.Exchanges)
            {
                messages.Add(new HistoryMessage(true, exchange.User));
                messages.Add(new HistoryMessage(false, exchange.Assistant));
            }
            messages.Add(new HistoryMessage(true, userTurn));

            var handler = new TurnHandler(listener, tools);
            listener.OnThinking();
            TurnResult result;
            try
            {
                result = await client.RunTurnAsync(
                    new TurnRequest(conversation.SystemPrompt!, messages, effort, tools.Specs, WebSearch: true), handler, ct);
            }
            catch (ClaudeException ex)
            {
                log.Error($"Claude call failed ({ex.Kind}).", ex);
                return new BrainReply(BrainOutcome.Error, handler.Text, FriendlyError(ex.Kind));
            }
            catch (OperationCanceledException)
            {
                return new BrainReply(BrainOutcome.Error, handler.Text, "Okay, stopped.");
            }

            LastUsage = result.Usage;
            spend.Record(result.Usage);
            var answer = result.Text.Trim();
            if (result.Stop == TurnStop.Refusal)
            {
                return new BrainReply(BrainOutcome.Refused, answer, "Sorry, I can't help with that one.");
            }

            var outcome = BrainOutcome.Ok;
            if (result.Stop is TurnStop.MaxTokens or TurnStop.ToolLoopLimit)
            {
                answer += " …(cut off)";
                outcome = BrainOutcome.CutOff;
            }

            if (answer.Length == 0) answer = "(no answer)";
            conversation.Add(userTurn, answer);
            chatLog?.Append(text, answer);
            var notice = spend.TakeWarning() ? $"Heads up: that's 80% of this month's ${spend.Cap:0.##} budget." : null;
            return new BrainReply(outcome, answer, notice);
        }
        finally
        {
            Busy = false;
        }
    }

    private static string FriendlyError(ClaudeErrorKind kind) => kind switch
    {
        ClaudeErrorKind.Network => "I can't reach the internet right now.",
        ClaudeErrorKind.Unauthorized => "My API key doesn't work. Check it in Settings.",
        ClaudeErrorKind.NoAccess => NoAccessMessage,
        ClaudeErrorKind.Billing => "Out of API credit. Top up at console.anthropic.com.",
        ClaudeErrorKind.RateLimited => "Claude is busy right now. Try again in a minute.",
        ClaudeErrorKind.Server => "Claude is having trouble right now. Try again in a bit.",
        _ => "Something went wrong (details are in data\\logs).",
    };

    private sealed class TurnHandler(IBrainListener listener, ToolRegistry tools) : ITurnHandler
    {
        private readonly StringBuilder _text = new();
        private bool _gotText;

        public string Text => _text.ToString();

        public void OnTextDelta(string text)
        {
            if (!_gotText)
            {
                _gotText = true;
                listener.OnFirstText();
            }
            _text.Append(text);
            listener.OnText(text);
        }

        public void OnWebSearch() => listener.OnStatus("searching the web…");

        public void OnActivity() => listener.OnActivity();

        public ToolOutcome ExecuteTool(string name, IReadOnlyDictionary<string, JsonElement> input) => tools.Execute(name, input);
    }
}
