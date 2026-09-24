using System.Text.Json;
using Companion.Core;
using Xunit;

namespace Companion.Tests;

public static class ToolInput
{
    public static IReadOnlyDictionary<string, JsonElement> From(object value) =>
        JsonSerializer.SerializeToElement(value).EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
}

public class ToolsTests
{
    private static (ToolRegistry Registry, MemoryStore Memory, TempDir Tmp) NewRegistry()
    {
        var tmp = new TempDir();
        var memory = new MemoryStore(tmp.PathOf("memory.json"), FakeClock.At(2026, 9, 24, 12), NullLog.Instance);
        var registry = new ToolRegistry();
        registry.Register(new RememberTool(memory));
        registry.Register(new ForgetTool(memory));
        return (registry, memory, tmp);
    }

    [Fact]
    public void Specs_describe_both_tools_with_required_string_inputs()
    {
        var (registry, _, tmp) = NewRegistry();
        using var cleanup = tmp;

        Assert.Equal(new[] { "remember", "forget" }, registry.Specs.Select(s => s.Name));
        var remember = registry.Specs[0];
        Assert.Equal(new[] { "fact" }, remember.Required);
        Assert.Equal("string", remember.Properties["fact"].GetProperty("type").GetString());
    }

    [Fact]
    public void Remember_then_forget_changes_memory()
    {
        var (registry, memory, tmp) = NewRegistry();
        using var cleanup = tmp;

        var saved = registry.Execute("remember", ToolInput.From(new { fact = "Likes teal" }));
        Assert.False(saved.IsError);
        Assert.Contains("f_01", saved.Content);
        Assert.Equal("Likes teal", Assert.Single(memory.Facts).Text);

        var forgotten = registry.Execute("forget", ToolInput.From(new { fact_id = "f_01" }));
        Assert.False(forgotten.IsError);
        Assert.Empty(memory.Facts);

        Assert.True(registry.Execute("forget", ToolInput.From(new { fact_id = "f_01" })).IsError);
    }

    [Fact]
    public void Unknown_tools_and_bad_inputs_become_error_outcomes()
    {
        var (registry, _, tmp) = NewRegistry();
        using var cleanup = tmp;

        Assert.True(registry.Execute("launch_rockets", ToolInput.From(new { })).IsError);
        Assert.True(registry.Execute("remember", ToolInput.From(new { })).IsError);
        Assert.True(registry.Execute("remember", ToolInput.From(new { fact = 42 })).IsError);
        Assert.True(registry.Execute("remember", ToolInput.From(new { fact = "  " })).IsError);
    }
}
