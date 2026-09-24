using System.Text.Json;

namespace Companion.Core;

public sealed record HistoryMessage(bool FromUser, string Text);

public sealed record TurnRequest(
    string SystemPrompt,
    IReadOnlyList<HistoryMessage> Messages,
    EffortLevel Effort,
    IReadOnlyList<ToolSpec> Tools,
    bool WebSearch);

/// <summary>Callbacks from the client during one turn. May be invoked on a background thread.</summary>
public interface ITurnHandler
{
    void OnTextDelta(string text);
    void OnWebSearch();
    ToolOutcome ExecuteTool(string name, IReadOnlyDictionary<string, JsonElement> input);
}

public enum TurnStop { EndTurn, Refusal, MaxTokens, ToolLoopLimit }

public sealed record TurnResult(string Text, TurnStop Stop, UsageTotals Usage);

/// <summary>One user turn against Claude, including any tool rounds, streamed.</summary>
public interface IClaudeClient
{
    Task<TurnResult> RunTurnAsync(TurnRequest request, ITurnHandler handler, CancellationToken ct);
    Task ValidateKeyAsync(CancellationToken ct);
}

public enum ClaudeErrorKind { Network, Unauthorized, Billing, RateLimited, Server, BadRequest, Unknown }

public sealed class ClaudeException(ClaudeErrorKind kind, string message, Exception? inner = null) : Exception(message, inner)
{
    public ClaudeErrorKind Kind { get; } = kind;
}
