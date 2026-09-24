using Companion.Core;
using Xunit;

namespace Companion.Tests;

public class MemoryStoreTests
{
    private static MemoryStore NewStore(TempDir tmp) =>
        new(tmp.PathOf("memory.json"), FakeClock.At(2026, 9, 24, 12), NullLog.Instance);

    [Fact]
    public void Add_assigns_sequential_ids_and_persists()
    {
        using var tmp = new TempDir();
        var store = NewStore(tmp);

        var first = store.Add("Works in Cinema 4D");
        var second = store.Add("Prefers short answers");

        Assert.True(first.Ok);
        Assert.Equal("f_01", first.Fact!.Id);
        Assert.Equal("f_02", second.Fact!.Id);
        var reloaded = NewStore(tmp);
        Assert.Equal(new[] { "Works in Cinema 4D", "Prefers short answers" }, reloaded.Facts.Select(f => f.Text));
    }

    [Fact]
    public void Add_of_a_duplicate_returns_the_existing_fact()
    {
        using var tmp = new TempDir();
        var store = NewStore(tmp);
        store.Add("Likes teal");

        var again = store.Add("  likes TEAL ");

        Assert.True(again.Ok);
        Assert.Equal("f_01", again.Fact!.Id);
        Assert.Single(store.Facts);
    }

    [Fact]
    public void Add_rejects_empty_text_and_respects_the_soft_limit()
    {
        using var tmp = new TempDir();
        var store = NewStore(tmp);
        Assert.False(store.Add("   ").Ok);

        for (var i = 0; i < MemoryStore.SoftLimit; i++) Assert.True(store.Add($"fact {i}").Ok);
        var overflow = store.Add("one too many");

        Assert.False(overflow.Ok);
        Assert.Contains("full", overflow.Message);
        Assert.Equal(MemoryStore.SoftLimit, store.Facts.Count);
    }

    [Fact]
    public void Remove_and_Update_work_by_id_and_raise_Changed()
    {
        using var tmp = new TempDir();
        var store = NewStore(tmp);
        store.Add("a");
        store.Add("b");
        var changes = 0;
        store.Changed += () => changes++;

        Assert.True(store.Update("f_02", "b2"));
        Assert.True(store.Remove("f_01"));
        Assert.False(store.Remove("f_99"));

        Assert.Equal(new[] { "b2" }, NewStore(tmp).Facts.Select(f => f.Text));
        Assert.Equal(2, changes);
        Assert.Equal("f_03", store.Add("c").Fact!.Id);
    }

    [Fact]
    public void Ids_never_repeat_after_deleting_the_highest_fact_even_across_reload()
    {
        using var tmp = new TempDir();
        var store = NewStore(tmp);
        store.Add("a");
        store.Add("b");

        Assert.True(store.Remove("f_02"));
        Assert.Equal("f_03", store.Add("c").Fact!.Id);
        Assert.True(store.Remove("f_03"));

        var reloaded = NewStore(tmp);
        Assert.Equal("f_04", reloaded.Add("d").Fact!.Id);
    }

    [Fact]
    public void A_file_without_a_counter_continues_after_the_highest_id()
    {
        using var tmp = new TempDir();
        System.IO.File.WriteAllText(tmp.PathOf("memory.json"),
            """{ "facts": [ { "id": "f_01", "text": "a", "created": "2026-09-01T00:00:00+00:00" }, { "id": "f_05", "text": "b", "created": "2026-09-01T00:00:00+00:00" } ] }""");

        var store = NewStore(tmp);

        Assert.Equal("f_06", store.Add("c").Fact!.Id);
        Assert.Contains("\"nextId\": 7", System.IO.File.ReadAllText(tmp.PathOf("memory.json")));
    }

    [Fact]
    public void Version_changes_only_on_successful_edits()
    {
        using var tmp = new TempDir();
        var store = NewStore(tmp);
        var v0 = store.Version;

        store.Add("a");
        var v1 = store.Version;
        store.Add("A");        // duplicate: no change
        store.Remove("f_99");  // missing: no change
        store.Update("f_99", "x");
        Assert.Equal(v1, store.Version);

        store.Update("f_01", "a2");
        var v2 = store.Version;
        store.Remove("f_01");

        Assert.True(v0 < v1 && v1 < v2 && v2 < store.Version);
    }

    [Fact]
    public void Reload_picks_up_changes_made_by_another_process()
    {
        using var tmp = new TempDir();
        var app = NewStore(tmp);
        var server = NewStore(tmp);
        var changed = 0;
        app.Changed += () => changed++;
        var before = app.Version;

        app.Reload();
        Assert.Equal(before, app.Version);

        server.Add("Likes teal");
        app.Reload();
        Assert.Equal("Likes teal", Assert.Single(app.Facts).Text);
        Assert.Equal(before + 1, app.Version);
        Assert.Equal(1, changed);
        Assert.Equal("f_02", app.Add("Works in C4D").Fact!.Id);
    }
}

public class PetStateStoreTests
{
    [Fact]
    public void Defaults_round_trip_and_reset()
    {
        using var tmp = new TempDir();
        var path = tmp.PathOf("pet.json");
        var store = new PetStateStore(path, FakeClock.At(2026, 9, 24, 12), NullLog.Instance);
        Assert.False(store.Current.Hatched);
        Assert.Equal(80, store.Current.Attention);

        store.Current.PetName = "Pip";
        store.Current.Hatched = true;
        store.Current.PositionX = 512;
        store.Save();
        var reloaded = new PetStateStore(path, FakeClock.At(2026, 9, 24, 12), NullLog.Instance);
        Assert.Equal("Pip", reloaded.Current.PetName);
        Assert.Equal(512, reloaded.Current.PositionX);

        var sameInstance = reloaded.Current;
        reloaded.Reset();
        Assert.Same(sameInstance, reloaded.Current);
        Assert.Null(reloaded.Current.PetName);
        Assert.False(reloaded.Current.Hatched);
        Assert.Null(reloaded.Current.PositionX);
    }
}
