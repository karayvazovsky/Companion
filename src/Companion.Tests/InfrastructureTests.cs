using System.IO;
using Companion.Core;
using Xunit;

namespace Companion.Tests;

public class JsonFileTests
{
    private sealed class Sample
    {
        public string Name { get; set; } = "default";
        public int Count { get; set; }
    }

    [Fact]
    public void Load_returns_fallback_when_file_is_missing()
    {
        using var tmp = new TempDir();
        var value = JsonFile.Load(tmp.PathOf("missing.json"), () => new Sample(), NullLog.Instance, FakeClock.At(2026, 9, 24, 12));
        Assert.Equal("default", value.Name);
    }

    [Fact]
    public void Save_then_Load_round_trips_and_leaves_no_temp_file()
    {
        using var tmp = new TempDir();
        var path = tmp.PathOf("sample.json");

        JsonFile.Save(path, new Sample { Name = "pip", Count = 3 });
        var loaded = JsonFile.Load(path, () => new Sample(), NullLog.Instance, FakeClock.At(2026, 9, 24, 12));

        Assert.Equal("pip", loaded.Name);
        Assert.Equal(3, loaded.Count);
        Assert.False(File.Exists(path + ".tmp"));
        Assert.Contains("\"name\"", File.ReadAllText(path));
    }

    [Fact]
    public void Load_moves_a_corrupt_file_aside_and_returns_fallback()
    {
        using var tmp = new TempDir();
        var path = tmp.PathOf("broken.json");
        File.WriteAllText(path, "{ not json");

        var value = JsonFile.Load(path, () => new Sample(), NullLog.Instance, FakeClock.At(2026, 9, 24, 12));

        Assert.Equal("default", value.Name);
        Assert.False(File.Exists(path));
        Assert.True(File.Exists(path + ".bad-20260924120000"));
    }
}

public class FileLogTests
{
    [Fact]
    public void Writes_daily_file_and_deletes_files_older_than_keep_days()
    {
        using var tmp = new TempDir();
        File.WriteAllText(tmp.PathOf("app-2026-09-10.log"), "old");
        File.WriteAllText(tmp.PathOf("app-2026-09-20.log"), "recent");
        var clock = FakeClock.At(2026, 9, 24, 8);

        var log = new FileLog(tmp.Root, clock);
        log.Info("hello");
        log.Error("boom", new InvalidOperationException("bad"));

        Assert.False(File.Exists(tmp.PathOf("app-2026-09-10.log")));
        Assert.True(File.Exists(tmp.PathOf("app-2026-09-20.log")));
        var today = File.ReadAllText(tmp.PathOf("app-2026-09-24.log"));
        Assert.Contains("INFO hello", today);
        Assert.Contains("ERROR boom", today);
        Assert.Contains("InvalidOperationException", today);
    }

    [Fact]
    public void Constructor_tolerates_locked_old_log_files()
    {
        using var tmp = new TempDir();
        var oldPath = tmp.PathOf("app-2026-09-10.log");
        File.WriteAllText(oldPath, "old");

        using (var locked = new FileStream(oldPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var clock = FakeClock.At(2026, 9, 24, 8);
            var log = new FileLog(tmp.Root, clock);
            log.Info("ok");

            var today = File.ReadAllText(tmp.PathOf("app-2026-09-24.log"));
            Assert.Contains("INFO ok", today);
        }
    }
}
