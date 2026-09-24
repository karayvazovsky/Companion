using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Beta.Messages;

namespace Companion.Core;

/// <summary>
/// Streams one turn from claude-opus-5-5 through the beta Messages endpoint, running client tool rounds.
/// Completed exchanges arrive as plain text; thinking/tool blocks are echoed only within this turn.
/// </summary>
public sealed class AnthropicClaudeClient : IClaudeClient
{
    public const string Model = "claude-opus-5-5";
    public const int MaxTokens = 8000;
    public const int MaxRounds = 5;
    private const string BindingBeta = "thinking-binding-controls-2026-08-01";

    private readonly AnthropicClient _client;

    public AnthropicClaudeClient(string apiKey) => _client = new AnthropicClient { ApiKey = apiKey };

    /// <summary>Free check: retrieving model metadata fails with 401 on a bad key.</summary>
    public async Task ValidateKeyAsync(CancellationToken ct)
    {
        try { await _client.Models.Retrieve(Model, cancellationToken: ct); }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested) { throw Map(ex); }
    }

    public async Task<TurnResult> RunTurnAsync(TurnRequest request, ITurnHandler handler, CancellationToken ct)
    {
        var messages = request.Messages
            .Select(m => new BetaMessageParam { Role = m.FromUser ? Role.User : Role.Assistant, Content = m.Text })
            .ToList();
        var text = new StringBuilder();
        var usage = new UsageTotals();

        for (var round = 0; round < MaxRounds; round++)
        {
            BetaMessage message;
            try
            {
                message = await Tap(_client.Beta.Messages.CreateStreaming(BuildParams(request, messages), ct), handler, text).Aggregate();
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                throw Map(ex);
            }

            usage += ToUsage(message);
            string stop = message.StopReason?.Raw() ?? "end_turn";
            if (stop == "refusal") return new TurnResult(text.ToString(), TurnStop.Refusal, usage);
            if (stop == "max_tokens") return new TurnResult(text.ToString(), TurnStop.MaxTokens, usage);
            if (stop != "tool_use" && stop != "pause_turn") return new TurnResult(text.ToString(), TurnStop.EndTurn, usage);

            var (echo, results) = BuildToolRound(message, handler);
            messages.Add(new BetaMessageParam { Role = Role.Assistant, Content = echo });
            if (results.Count > 0) messages.Add(new BetaMessageParam { Role = Role.User, Content = results });
        }
        return new TurnResult(text.ToString(), TurnStop.ToolLoopLimit, usage);
    }

    private static MessageCreateParams BuildParams(TurnRequest request, List<BetaMessageParam> messages)
    {
        var tools = new List<BetaToolUnion>();
        if (request.WebSearch) tools.Add(new BetaToolUnion(new BetaWebSearchTool20260209 { MaxUses = 3 }));
        foreach (var spec in request.Tools)
        {
            tools.Add(new BetaTool
            {
                Name = spec.Name,
                Description = spec.Description,
                InputSchema = new() { Properties = spec.Properties.ToDictionary(), Required = [.. spec.Required] },
            });
        }

        return new MessageCreateParams
        {
            Model = Model,
            MaxTokens = MaxTokens,
            Betas = [BindingBeta],
            Thinking = new BetaThinkingConfigAdaptive
            {
                BlockBinding = new BetaThinkingBlockBinding { PrefixMismatchBehavior = BetaThinkingPrefixMismatchBehavior.DropBlock },
            },
            OutputConfig = new BetaOutputConfig
            {
                Effort = request.Effort switch { EffortLevel.Low => Effort.Low, EffortLevel.Medium => Effort.Medium, _ => Effort.High },
            },
            System = new List<BetaTextBlockParam> { new() { Text = request.SystemPrompt, CacheControl = new BetaCacheControlEphemeral() } },
            Tools = tools,
            Messages = messages,
        };
    }

    /// <summary>Forwards text deltas to the UI while the SDK aggregates the full message.</summary>
    private static async IAsyncEnumerable<BetaRawMessageStreamEvent> Tap(
        IAsyncEnumerable<BetaRawMessageStreamEvent> source, ITurnHandler handler, StringBuilder text,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var firstTextThisRound = true;
        await foreach (var ev in source.WithCancellation(ct))
        {
            if (ev.TryPickContentBlockDelta(out var delta) && delta.Delta.TryPickText(out var t))
            {
                var piece = t.Text;
                if (firstTextThisRound && text.Length > 0 && text[^1] != '\n') piece = "\n\n" + piece;
                firstTextThisRound = false;
                text.Append(piece);
                handler.OnTextDelta(piece);
            }
            else if (ev.TryPickContentBlockStart(out var start) && start.ContentBlock.TryPickBetaServerToolUse(out _))
            {
                handler.OnWebSearch();
            }
            yield return ev;
        }
    }

    private static (List<BetaContentBlockParam> Echo, List<BetaContentBlockParam> Results) BuildToolRound(BetaMessage message, ITurnHandler handler)
    {
        var echo = new List<BetaContentBlockParam>();
        var results = new List<BetaContentBlockParam>();
        foreach (var block in message.Content)
        {
            if (block.TryPickText(out var text)) echo.Add(new BetaTextBlockParam { Text = text.Text });
            else if (block.TryPickThinking(out var thinking)) echo.Add(new BetaThinkingBlockParam { Thinking = thinking.Thinking, Signature = thinking.Signature });
            else if (block.TryPickRedactedThinking(out var redacted)) echo.Add(new BetaRedactedThinkingBlockParam { Data = redacted.Data });
            else if (block.TryPickServerToolUse(out var serverUse)) echo.Add(Convert<BetaServerToolUseBlockParam>(serverUse));
            else if (block.TryPickWebSearchToolResult(out var searchResult)) echo.Add(Convert<BetaWebSearchToolResultBlockParam>(searchResult));
            else if (block.TryPickToolUse(out var toolUse))
            {
                echo.Add(new BetaToolUseBlockParam { ID = toolUse.ID, Name = toolUse.Name, Input = toolUse.Input });
                var outcome = handler.ExecuteTool(toolUse.Name, toolUse.Input);
                results.Add(new BetaToolResultBlockParam { ToolUseID = toolUse.ID, Content = outcome.Content, IsError = outcome.IsError });
            }
        }
        return (echo, results);
    }

    // Response blocks and request params share the same wire shape; round-trip through JSON.
    private static T Convert<T>(object block) =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(block, block.GetType()))
        ?? throw new InvalidOperationException("Could not convert " + block.GetType().Name);

    private static UsageTotals ToUsage(BetaMessage message) => new(
        message.Usage.InputTokens,
        message.Usage.OutputTokens,
        message.Usage.CacheReadInputTokens ?? 0,
        message.Usage.CacheCreationInputTokens ?? 0,
        message.Usage.ServerToolUse?.WebSearchRequests ?? 0);

    internal static ClaudeException Map(Exception ex) => ex switch
    {
        ClaudeException ce => ce,
        AnthropicUnauthorizedException => new(ClaudeErrorKind.Unauthorized, ex.Message, ex),
        AnthropicForbiddenException or AnthropicNotFoundException => new(ClaudeErrorKind.NoAccess, ex.Message, ex),
        AnthropicRateLimitException => new(ClaudeErrorKind.RateLimited, ex.Message, ex),
        Anthropic5xxException or AnthropicSseException => new(ClaudeErrorKind.Server, ex.Message, ex),
        AnthropicBadRequestException when ex.Message.Contains("credit balance", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("billing", StringComparison.OrdinalIgnoreCase)
            => new(ClaudeErrorKind.Billing, ex.Message, ex),
        AnthropicBadRequestException => new(ClaudeErrorKind.BadRequest, ex.Message, ex),
        AnthropicIOException or HttpRequestException or OperationCanceledException => new(ClaudeErrorKind.Network, ex.Message, ex),
        AnthropicApiException => new(ClaudeErrorKind.Unknown, ex.Message, ex),
        _ => new(ClaudeErrorKind.Unknown, ex.Message, ex),
    };
}
