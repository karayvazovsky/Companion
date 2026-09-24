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
