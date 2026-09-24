using System.Globalization;

namespace Companion.Core;

public interface ILog
{
    void Info(string message);
    void Error(string message, Exception? exception = null);
}

public sealed class NullLog : ILog
{
    public static readonly NullLog Instance = new();
    public void Info(string message) { }
    public void Error(string message, Exception? exception = null) { }
}

/// <summary>Writes data\logs\app-YYYY-MM-DD.log and keeps the last <c>keepDays</c> days.</summary>
public sealed class FileLog : ILog
{
    private readonly string _directory;
    private readonly IClock _clock;
    private readonly object _gate = new();

    public FileLog(string directory, IClock clock, int keepDays = 7)
    {
        _directory = directory;
        _clock = clock;
        Directory.CreateDirectory(directory);

        var cutoff = clock.Now.Date.AddDays(-keepDays);
        foreach (var file in Directory.GetFiles(directory, "app-*.log"))
        {
            var stamp = Path.GetFileNameWithoutExtension(file)[4..];
            if (DateTime.TryParseExact(stamp, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) && day < cutoff)
            {
                try { File.Delete(file); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
    }

    public void Info(string message) => Write("INFO", message);

    public void Error(string message, Exception? exception = null) =>
        Write("ERROR", exception is null ? message : $"{message}{Environment.NewLine}{exception}");

    private void Write(string level, string message)
    {
        var now = _clock.Now;
        var path = Path.Combine(_directory, $"app-{now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.log");
        var line = $"{now.ToString("HH:mm:ss", CultureInfo.InvariantCulture)} {level} {message}{Environment.NewLine}";
        lock (_gate)
        {
            try { File.AppendAllText(path, line); } catch (IOException) { }
        }
    }
}
