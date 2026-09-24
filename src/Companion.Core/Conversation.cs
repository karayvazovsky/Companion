using System.Globalization;

namespace Companion.Core;

public sealed record Exchange(string User, string Assistant);

/// <summary>
/// Text-only history of the current chat. The system prompt is frozen for the whole conversation
/// so the cached prefix stays byte-identical between turns.
/// </summary>
public sealed class Conversation(IClock clock)
{
    public const int MaxExchanges = 10;
    public static readonly TimeSpan ResetAfter = TimeSpan.FromMinutes(30);

    private readonly List<Exchange> _exchanges = new();
    private DateTimeOffset _lastActivity;

    public string? SystemPrompt { get; private set; }

    public IReadOnlyList<Exchange> Exchanges => _exchanges;

    public void BeginTurn(Func<string> buildSystemPrompt)
    {
        var now = clock.Now;
        if (SystemPrompt is not null && now - _lastActivity >= ResetAfter) Reset();
        SystemPrompt ??= buildSystemPrompt();
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
