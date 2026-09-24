using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Companion.Core;

public static class JsonFile
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>Loads a JSON file. Missing → fallback. Corrupt → renamed to *.bad-yyyyMMddHHmmss, logged, fallback.</summary>
    public static T Load<T>(string path, Func<T> fallback, ILog log, IClock clock)
    {
        if (!File.Exists(path)) return fallback();
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options)
                ?? throw new JsonException("Document was null.");
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            var bad = $"{path}.bad-{clock.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture)}";
            File.Move(path, bad, overwrite: true);
            log.Error($"Corrupt file {path}; moved to {bad}", ex);
            return fallback();
        }
    }

    /// <summary>Writes to a temp file then renames, so a crash never leaves a half-written file.</summary>
    public static void Save<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(value, Options));
        File.Move(temp, path, overwrite: true);
    }
}
