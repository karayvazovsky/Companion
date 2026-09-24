using System.Text.Json;
using System.Text.Json.Nodes;

namespace Companion.Core;

/// <summary>
/// Minimal MCP server over stdio (newline-delimited JSON-RPC 2.0): initialize, tools/list, tools/call, ping.
/// Exposes the ToolRegistry so Claude Code can call remember/forget.
/// </summary>
public sealed class McpStdioServer(ToolRegistry tools, string serverName = "companion")
{
    public async Task RunAsync(TextReader input, TextWriter output, CancellationToken ct = default)
    {
        while (await input.ReadLineAsync(ct) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var reply = Handle(line);
            if (reply is null) continue;
            await output.WriteAsync(reply + "\n");
            await output.FlushAsync(ct);
        }
    }

    internal string? Handle(string line)
    {
        JsonNode? request;
        try { request = JsonNode.Parse(line); }
        catch (JsonException) { return Error(null, -32700, "Parse error"); }

        var id = request?["id"]?.DeepClone();
        if (id is null) return null;   // notification
        var method = request?["method"]?.GetValue<string>();
        try
        {
            return method switch
            {
                "initialize" => Result(id, new JsonObject
                {
                    ["protocolVersion"] = request?["params"]?["protocolVersion"]?.GetValue<string>() ?? "2025-06-18",
                    ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
                    ["serverInfo"] = new JsonObject { ["name"] = serverName, ["version"] = "1.0.0" },
                }),
                "ping" => Result(id, new JsonObject()),
                "tools/list" => Result(id, new JsonObject { ["tools"] = ToolsJson() }),
                "tools/call" => Result(id, Call(request?["params"])),
                _ => Error(id, -32601, $"Method not found: {method}"),
            };
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException or JsonException)
        {
            return Error(id, -32602, ex.Message);
        }
    }

    private JsonArray ToolsJson()
    {
        var array = new JsonArray();
        foreach (var spec in tools.Specs)
        {
            var properties = new JsonObject();
            foreach (var (name, schema) in spec.Properties) properties[name] = JsonNode.Parse(schema.GetRawText());
            var required = new JsonArray();
            foreach (var name in spec.Required) required.Add(name);
            array.Add(new JsonObject
            {
                ["name"] = spec.Name,
                ["description"] = spec.Description,
                ["inputSchema"] = new JsonObject { ["type"] = "object", ["properties"] = properties, ["required"] = required },
            });
        }
        return array;
    }

    private JsonObject Call(JsonNode? parameters)
    {
        var name = parameters?["name"]?.GetValue<string>() ?? "";
        var arguments = new Dictionary<string, JsonElement>();
        if (parameters?["arguments"] is JsonObject args)
        {
            foreach (var property in JsonSerializer.SerializeToElement(args).EnumerateObject())
                arguments[property.Name] = property.Value.Clone();
        }
        var outcome = tools.Execute(name, arguments);
        return new JsonObject
        {
            ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = outcome.Content }),
            ["isError"] = outcome.IsError,
        };
    }

    private static string Result(JsonNode id, JsonNode result) =>
        new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result }.ToJsonString();

    private static string Error(JsonNode? id, int code, string message) =>
        new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["error"] = new JsonObject { ["code"] = code, ["message"] = message } }.ToJsonString();
}
