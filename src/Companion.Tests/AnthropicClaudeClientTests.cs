using System.Net;
using System.Text.Json;
using Anthropic.Exceptions;
using Anthropic.Models.Beta.Messages;
using Companion.Core;
using Xunit;

namespace Companion.Tests;

public class AnthropicClaudeClientToolRoundTests
{
    private sealed class Handler : ITurnHandler
    {
        public List<string> Calls { get; } = new();
        public void OnTextDelta(string text) { }
        public void OnWebSearch() { }

        public ToolOutcome ExecuteTool(string name, IReadOnlyDictionary<string, JsonElement> input)
        {
            Calls.Add($"{name}:{input["fact"].GetString()}");
            return new ToolOutcome("Saved as f_01.", false);
        }
    }

    // A tool_use round from web_search_20260209: dynamic filtering runs server-side code execution.
    private const string ToolRoundMessage = """
        {
          "id": "msg_01", "type": "message", "role": "assistant", "model": "claude-opus-5-5",
          "stop_reason": "tool_use", "stop_sequence": null,
          "usage": { "input_tokens": 10, "output_tokens": 20 },
          "content": [
            { "type": "thinking", "thinking": "Let me look this up.", "signature": "sig-abc" },
            { "type": "text", "text": "Checking...", "citations": null },
            { "type": "server_tool_use", "id": "srvtoolu_01", "name": "web_search", "input": { "query": "date in Tokyo today" } },
            { "type": "web_search_tool_result", "tool_use_id": "srvtoolu_01",
              "content": [ { "type": "web_search_result", "url": "https://example.com/tokyo", "title": "Tokyo time",
                             "encrypted_content": "enc-1", "page_age": null } ] },
            { "type": "server_tool_use", "id": "srvtoolu_02", "name": "code_execution", "input": { "code": "print(1)" } },
            { "type": "code_execution_tool_result", "tool_use_id": "srvtoolu_02",
              "content": { "type": "code_execution_result", "stdout": "1\n", "stderr": "", "return_code": 0, "content": [] } },
            { "type": "tool_use", "id": "toolu_01", "name": "remember", "input": { "fact": "Asked about Tokyo" } }
          ]
        }
        """;

    private static JsonElement Wire(object block) => JsonSerializer.SerializeToElement(block, block.GetType());

    [Fact]
    public void Echoes_every_block_in_order_and_answers_each_client_tool_once()
    {
        var message = JsonSerializer.Deserialize<BetaMessage>(ToolRoundMessage)!;
        var handler = new Handler();

        var (echo, results) = AnthropicClaudeClient.BuildToolRound(message, handler);

        var wire = echo.Select(Wire).ToList();
        Assert.Equal(
            ["thinking", "text", "server_tool_use", "web_search_tool_result", "server_tool_use", "code_execution_tool_result", "tool_use"],
            wire.Select(b => b.GetProperty("type").GetString()));
        Assert.Equal("sig-abc", wire[0].GetProperty("signature").GetString());
        Assert.Equal("Checking...", wire[1].GetProperty("text").GetString());
        Assert.Equal("srvtoolu_01", wire[2].GetProperty("id").GetString());
        Assert.Equal("web_search", wire[2].GetProperty("name").GetString());
        Assert.Equal("srvtoolu_01", wire[3].GetProperty("tool_use_id").GetString());
        Assert.Equal("enc-1", wire[3].GetProperty("content")[0].GetProperty("encrypted_content").GetString());
        Assert.Equal("srvtoolu_02", wire[4].GetProperty("id").GetString());
        Assert.Equal("code_execution", wire[4].GetProperty("name").GetString());
        Assert.Equal("srvtoolu_02", wire[5].GetProperty("tool_use_id").GetString());
        Assert.Equal("1\n", wire[5].GetProperty("content").GetProperty("stdout").GetString());
        Assert.Equal("toolu_01", wire[6].GetProperty("id").GetString());

        var result = Wire(Assert.Single(results));
        Assert.Equal("tool_result", result.GetProperty("type").GetString());
        Assert.Equal("toolu_01", result.GetProperty("tool_use_id").GetString());
        Assert.Equal(["remember:Asked about Tokyo"], handler.Calls);
    }

    [Fact]
    public void Continuation_rounds_reuse_the_container()
    {
        var request = new TurnRequest("system", [new HistoryMessage(true, "hi")], EffortLevel.Low, [], WebSearch: true);

        var first = AnthropicClaudeClient.BuildParams(request, [], containerId: null);
        var next = AnthropicClaudeClient.BuildParams(request, [], containerId: "container_01");

        Assert.Null(first.Container);
        Assert.Equal("\"container_01\"", JsonSerializer.Serialize(next.Container));
        Assert.Equal(AnthropicClaudeClient.Model, next.Model.Raw());
        Assert.Equal(AnthropicClaudeClient.MaxTokens, next.MaxTokens);
        Assert.Single(next.Tools!); // web_search survives the copy
    }

    [Fact]
    public void Bash_code_execution_results_are_echoed_too()
    {
        var message = JsonSerializer.Deserialize<BetaMessage>("""
            {
              "id": "msg_02", "type": "message", "role": "assistant", "model": "claude-opus-5-5",
              "stop_reason": "pause_turn", "stop_sequence": null,
              "usage": { "input_tokens": 10, "output_tokens": 20 },
              "content": [
                { "type": "server_tool_use", "id": "srvtoolu_03", "name": "bash_code_execution", "input": { "command": "echo hi" } },
                { "type": "bash_code_execution_tool_result", "tool_use_id": "srvtoolu_03",
                  "content": { "type": "bash_code_execution_result", "stdout": "hi\n", "stderr": "", "return_code": 0, "content": [] } }
              ]
            }
            """)!;

        var (echo, results) = AnthropicClaudeClient.BuildToolRound(message, new Handler());

        var wire = echo.Select(Wire).ToList();
        Assert.Equal(["server_tool_use", "bash_code_execution_tool_result"], wire.Select(b => b.GetProperty("type").GetString()));
        Assert.Equal("srvtoolu_03", wire[1].GetProperty("tool_use_id").GetString());
        Assert.Empty(results);
    }
}

public class AnthropicClaudeClientMapTests
{
    [Fact]
    public void Mid_stream_errors_map_to_server()
    {
        Assert.Equal(ClaudeErrorKind.Server, AnthropicClaudeClient.Map(new AnthropicSseException("overloaded_error")).Kind);
    }

    [Fact]
    public void Forbidden_and_not_found_map_to_no_access()
    {
        Assert.Equal(ClaudeErrorKind.NoAccess, AnthropicClaudeClient.Map(new AnthropicForbiddenException { StatusCode = HttpStatusCode.Forbidden, ResponseBody = "{}" }).Kind);
        Assert.Equal(ClaudeErrorKind.NoAccess, AnthropicClaudeClient.Map(new AnthropicNotFoundException { StatusCode = HttpStatusCode.NotFound, ResponseBody = "{}" }).Kind);
    }

    [Theory]
    [InlineData("Your credit balance is too low to access the Anthropic API.")]
    [InlineData("This organization has been disabled due to a billing issue.")]
    public void Billing_bad_requests_map_to_billing(string message)
    {
        Assert.Equal(ClaudeErrorKind.Billing, AnthropicClaudeClient.Map(new AnthropicBadRequestException { StatusCode = HttpStatusCode.BadRequest, ResponseBody = message }).Kind);
    }
}
