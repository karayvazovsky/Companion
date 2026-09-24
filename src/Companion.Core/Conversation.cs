using System.Globalization;

namespace Companion.Core;

public sealed record Exchange(string User, string Assistant);

/// <summary>
/// Text-only history of the current chat. The system prompt stays frozen between turns so the cached
/// prefix is byte-identical, unless its inputs (the remembered facts) changed.
/// </summary>
public sealed class Conversation(IClock clock)
{
    public const int MaxExchanges = 10;
    public static readonly TimeSpan ResetAfter = TimeSpan.FromMinutes(30);

    private readonly List<Exchange> _exchanges = new();
    private DateTimeOffset _lastActivity;
    private int _promptVersion;

    public string? SystemPrompt { get; private set; }

    public IReadOnlyList<Exchange> Exchanges => _exchanges;

    /// <summary>
    /// Starts a turn. The prompt is rebuilt only when the chat is new or <paramref name="promptVersion"/>
    /// (e.g. the memory version) differs from the one the current prompt was built with; history is kept.
    /// </summary>
    public void BeginTurn(Func<string> buildSystemPrompt, int promptVersion)
    {
        var now = clock.Now;
        if (SystemPrompt is not null && now - _lastActivity >= ResetAfter) Reset();
        if (SystemPrompt is null || promptVersion != _promptVersion)
        {
            SystemPrompt = buildSystemPrompt();
            _promptVersion = promptVersion;
        }
        _lastActivity = now;
    }

    public string FormatUserTurn(string text) =>
        $"[{clock.Now.ToString("yyyy-MM-dd HH:mm, dddd", CultureInfo.InvariantCulture)}]\n{text}";

    public void Add(string userTurn, string assistant)
    {
        _exchanges.Add(new Exchange(userTurn, assistant));
        while (_exchanges.Count > MaxExchanges) _exchanges.RemoveAt(0);
        _lastActivity = clock.Now;
    }

    public void Reset()
    {
        _exchanges.Clear();
        SystemPrompt = null;
    }
}
