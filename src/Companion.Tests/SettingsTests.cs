using Companion.Core;
using Xunit;

namespace Companion.Tests;

public class SettingsStoreTests
{
    [Fact]
    public void New_store_has_spec_defaults()
    {
        using var tmp = new TempDir();
        var store = new SettingsStore(tmp.PathOf("settings.json"), FakeClock.At(2026, 9, 24, 12), NullLog.Instance);

        Assert.Equal("Ctrl+Alt+Space", store.Current.Hotkey);
        Assert.Equal(EffortLevel.Low, store.Current.EverydayEffort);
        Assert.Equal(EffortLevel.High, store.Current.ThinkHarderEffort);
        Assert.Equal(30m, store.Current.MonthlyCapUsd);
        Assert.Equal(4, store.Current.PetScale);
        Assert.False(store.Current.StartWithWindows);
    }

    [Fact]
    public void Save_persists_and_writes_enums_as_text()
    {
        using var tmp = new TempDir();
        var path = tmp.PathOf("settings.json");
        var store = new SettingsStore(path, FakeClock.At(2026, 9, 24, 12), NullLog.Instance);
        var changed = 0;
        store.Changed += () => changed++;

        store.Current.Hotkey = "Ctrl+Shift+K";
        store.Current.EverydayEffort = EffortLevel.Medium;
        store.Current.SpendByMonth["2026-09"] = 1.25m;
        store.Save();

        var reloaded = new SettingsStore(path, FakeClock.At(2026, 9, 24, 12), NullLog.Instance);
        Assert.Equal("Ctrl+Shift+K", reloaded.Current.Hotkey);
        Assert.Equal(EffortLevel.Medium, reloaded.Current.EverydayEffort);
        Assert.Equal(1.25m, reloaded.Current.SpendByMonth["2026-09"]);
        Assert.Contains("\"medium\"", System.IO.File.ReadAllText(path));
        Assert.Equal(1, changed);
    }

    [Fact]
    public void Invalid_pet_scale_is_normalized_to_4()
    {
        using var tmp = new TempDir();
        var path = tmp.PathOf("settings.json");
        System.IO.File.WriteAllText(path, """{ "petScale": 5 }""");

        var store = new SettingsStore(path, FakeClock.At(2026, 9, 24, 12), NullLog.Instance);

        Assert.Equal(4, store.Current.PetScale);
    }
}

public class SecretStoreTests
{
    [Fact]
    public void Api_key_round_trips_and_is_not_stored_in_plain_text()
    {
        using var tmp = new TempDir();
        var path = tmp.PathOf("secrets.bin");
        var store = new SecretStore(path, new DpapiProtector(), NullLog.Instance);
        Assert.False(store.HasKey);

        store.SaveApiKey("  sk-ant-test-123  ");

        Assert.True(store.HasKey);
        Assert.Equal("sk-ant-test-123", new SecretStore(path, new DpapiProtector(), NullLog.Instance).LoadApiKey());
        Assert.DoesNotContain("sk-ant-test-123", System.Text.Encoding.UTF8.GetString(System.IO.File.ReadAllBytes(path)));
    }

    [Fact]
    public void Garbage_file_loads_as_null()
    {
        using var tmp = new TempDir();
        var path = tmp.PathOf("secrets.bin");
        System.IO.File.WriteAllBytes(path, [1, 2, 3, 4]);

        Assert.Null(new SecretStore(path, new DpapiProtector(), NullLog.Instance).LoadApiKey());
    }

    [Fact]
    public void Clear_removes_the_key()
    {
        using var tmp = new TempDir();
        var store = new SecretStore(tmp.PathOf("secrets.bin"), new DpapiProtector(), NullLog.Instance);
        store.SaveApiKey("sk-ant-x");

        store.Clear();

        Assert.False(store.HasKey);
        Assert.Null(store.LoadApiKey());
    }
}
