using System.IO;
using System.Text.Json;
using Companion.Core;
using Xunit;

namespace Companion.Tests;

public class McpTests
{
    private static (McpStdioServer Server, MemoryStore Memory, TempDir Tmp) NewServer()
    {
        var tmp = new TempDir();
        var memory = new MemoryStore(tmp.PathOf("memory.json"), FakeClock.At(2026, 9, 24, 12), NullLog.Instance);
        var tools = new ToolRegistry();
        tools.Register(new RememberTool(memory));
        tools.Register(new ForgetTool(memory));
        return (new McpStdioServer(tools), memory, tmp);
    }

    private static JsonElement Parse(string? json) => JsonDocument.Parse(json!).RootElement;

    [Fact]
    public void Initialize_echoes_protocol_version_and_advertises_tools()
    {
        var (server, _, tmp) = NewServer();
        using var cleanup = tmp;
        var reply = Parse(server.Handle("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"claude-code"}}}"""));
        Assert.Equal(1, reply.GetProperty("id").GetInt32());
        Assert.Equal("2025-06-18", reply.GetProperty("result").GetProperty("protocolVersion").GetString());
        Assert.True(reply.GetProperty("result").GetProperty("capabilities").TryGetProperty("tools", out _));
        Assert.Equal("companion", reply.GetProperty("result").GetProperty("serverInfo").GetProperty("name").GetString());
    }

    [Fact]
    public void Notifications_get_no_reply_and_unknown_methods_get_an_error()
    {
        var (server, _, tmp) = NewServer();
        using var cleanup = tmp;
        Assert.Null(server.Handle("""{"jsonrpc":"2.0","method":"notifications/initialized"}"""));
        Assert.Equal(-32601, Parse(server.Handle("""{"jsonrpc":"2.0","id":"x","method":"resources/list"}""")).GetProperty("error").GetProperty("code").GetInt32());
        Assert.Equal(-32700, Parse(server.Handle("{ nope")).GetProperty("error").GetProperty("code").GetInt32());
    }

    [Fact]
    public void Tools_list_exposes_remember_and_forget_schemas()
    {
        var (server, _, tmp) = NewServer();
        using var cleanup = tmp;
        var tools = Parse(server.Handle("""{"jsonrpc":"2.0","id":2,"method":"tools/list"}""")).GetProperty("result").GetProperty("tools");
        Assert.Equal(new[] { "remember", "forget" }, tools.EnumerateArray().Select(t => t.GetProperty("name").GetString()));
        var schema = tools[0].GetProperty("inputSchema");
        Assert.Equal("object", schema.GetProperty("type").GetString());
        Assert.Equal("string", schema.GetProperty("properties").GetProperty("fact").GetProperty("type").GetString());
        Assert.Equal("fact", schema.GetProperty("required")[0].GetString());
    }

    [Fact]
    public async Task Tools_call_runs_the_tool_over_stdio()
    {
        var (server, memory, tmp) = NewServer();
        using var cleanup = tmp;
        var input = new StringReader(
            """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18"}}""" + "\n" +
            """{"jsonrpc":"2.0","method":"notifications/initialized"}""" + "\n" +
            """{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"remember","arguments":{"fact":"Likes teal"}}}""" + "\n" +
            """{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"forget","arguments":{"fact_id":"f_99"}}}""" + "\n");
        var output = new StringWriter();

        await server.RunAsync(input, output);

        var lines = output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, lines.Length);
        var saved = Parse(lines[1]).GetProperty("result");
        Assert.False(saved.GetProperty("isError").GetBoolean());
        Assert.Contains("f_01", saved.GetProperty("content")[0].GetProperty("text").GetString());
        Assert.True(Parse(lines[2]).GetProperty("result").GetProperty("isError").GetBoolean());
        Assert.Equal("Likes teal", Assert.Single(memory.Facts).Text);
    }
}
