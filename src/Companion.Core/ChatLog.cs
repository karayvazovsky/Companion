using System.Globalization;
using System.Text.Json;

namespace Companion.Core;

/// <summary>Local-only chat history: data\logs\chat-YYYY-MM.jsonl.</summary>
public sealed class ChatLog(string logsDirectory, IClock clock)
{
    private readonly object _gate = new();

    public void Append(string user, string assistant)
    {
        var now = clock.Now;
        var line = JsonSerializer.Serialize(new { time = now, user, assistant });
        var path = Path.Combine(logsDirectory, $"chat-{now.ToString("yyyy-MM", CultureInfo.InvariantCulture)}.jsonl");
        lock (_gate)
        {
            Directory.CreateDirectory(logsDirectory);
            File.AppendAllText(path, line + Environment.NewLine);
        }
    }
}
