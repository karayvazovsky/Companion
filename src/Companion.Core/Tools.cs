using System.Text.Json;

namespace Companion.Core;

public sealed record ToolSpec(string Name, string Description, IReadOnlyDictionary<string, JsonElement> Properties, IReadOnlyList<string> Required);

public sealed record ToolOutcome(string Content, bool IsError);

public interface ITool
{
    ToolSpec Spec { get; }
    ToolOutcome Execute(IReadOnlyDictionary<string, JsonElement> input);
}

/// <summary>Client-side tools Claude may call. Inputs are validated here (all v1 inputs are required strings).</summary>
public sealed class ToolRegistry
{
    private readonly List<ITool> _tools = new();

    public IReadOnlyList<ToolSpec> Specs => _tools.Select(t => t.Spec).ToList();

    public void Register(ITool tool) => _tools.Add(tool);

    public ToolOutcome Execute(string name, IReadOnlyDictionary<string, JsonElement> input)
    {
        var tool = _tools.FirstOrDefault(t => t.Spec.Name == name);
        if (tool is null) return new ToolOutcome($"Unknown tool '{name}'.", true);
        foreach (var required in tool.Spec.Required)
        {
            if (!input.TryGetValue(required, out var value)
                || value.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(value.GetString()))
            {
                return new ToolOutcome($"'{required}' is required and must be a non-empty string.", true);
            }
        }
        try
        {
            return tool.Execute(input);
        }
        catch (Exception ex)
        {
            return new ToolOutcome($"Tool failed: {ex.Message}", true);
        }
    }

    internal static IReadOnlyDictionary<string, JsonElement> StringProperty(string name, string description) =>
        new Dictionary<string, JsonElement>
        {
            [name] = JsonSerializer.SerializeToElement(new { type = "string", description }),
        };
}

public sealed class RememberTool(MemoryStore memory) : ITool
{
    public ToolSpec Spec { get; } = new(
        "remember",
        "Save one short fact about the user to long-term memory: preferences, projects, tools they use, people and things that matter to them. " +
        "Use it when they share something worth keeping or ask you to remember something. One fact per call, third person, e.g. \"Works in Cinema 4D\".",
        ToolRegistry.StringProperty("fact", "The fact to remember, one short sentence."),
        ["fact"]);

    public ToolOutcome Execute(IReadOnlyDictionary<string, JsonElement> input)
    {
        var result = memory.Add(input["fact"].GetString()!);
        return new ToolOutcome(result.Message, !result.Ok);
    }
}

public sealed class ForgetTool(MemoryStore memory) : ITool
{
    public ToolSpec Spec { get; } = new(
        "forget",
        "Delete a remembered fact by its id (the [f_NN] shown in your instructions). Use it when the user asks you to forget something or a fact is outdated.",
        ToolRegistry.StringProperty("fact_id", "Id of the fact, e.g. f_03."),
        ["fact_id"]);

    public ToolOutcome Execute(IReadOnlyDictionary<string, JsonElement> input)
    {
        var id = input["fact_id"].GetString()!.Trim();
        return memory.Remove(id)
            ? new ToolOutcome($"Forgot {id}.", false)
            : new ToolOutcome($"No fact with id {id}.", true);
    }
}
