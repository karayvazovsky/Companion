using System.IO;
using System.Text.Json;
using Companion.Core;
using Xunit;

namespace Companion.Tests;

public class ClaudeCodeTests
{
    private sealed class RecordingHandler : ITurnHandler
    {
        public List<string> Texts { get; } = new();
        public int Searches { get; private set; }
        public int Activity { get; private set; }
        public void OnTextDelta(string text) => Texts.Add(text);
        public void OnWebSearch() => Searches++;
        public void OnActivity() => Activity++;
        public ToolOutcome ExecuteTool(string name, IReadOnlyDictionary<string, JsonElement> input) => new("unused", true);
    }

    private static string Event(object ev) => JsonSerializer.Serialize(new { type = "stream_event", @event = ev });
    private static string TextDelta(string text) => Event(new { type = "content_block_delta", index = 1, delta = new { type = "text_delta", text } });
    private static string MessageStart() => Event(new { type = "message_start", message = new { id = "m" } });
    private static string Result(bool isError = false, string stop = "end_turn", string result = "ok") =>
        JsonSerializer.Serialize(new { type = "result", subtype = isError ? "error" : "success", is_error = isError, stop_reason = stop, result });

    [Fact]
    public void Locator_prefers_override_then_path_exe_then_npm_package()
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            @"C:\tools\claude.exe",
            @"C:\npm\claude.cmd",
            @"C:\npm\node_modules\@anthropic-ai\claude-code\bin\claude.exe",
            @"C:\Users\me\.local\bin\claude.exe",
        };
        bool Exists(string p) => files.Contains(p);

        Assert.Equal(@"C:\tools\claude.exe", ClaudeCodeLocator.Find(@"C:\tools\claude.exe", Exists, "", @"C:\Users\me", @"C:\AppData"));
        Assert.Equal(@"C:\npm\node_modules\@anthropic-ai\claude-code\bin\claude.exe",
            ClaudeCodeLocator.Find(null, Exists, @"C:\Windows;C:\npm", @"C:\Users\me", @"C:\AppData"));
        Assert.Equal(@"C:\Users\me\.local\bin\claude.exe", ClaudeCodeLocator.Find(@"C:\missing.exe", Exists, @"C:\Windows", @"C:\Users\me", @"C:\AppData"));
        Assert.Null(ClaudeCodeLocator.Find(null, _ => false, @"C:\Windows", @"C:\Users\me", @"C:\AppData"));
    }

    [Fact]
    public void Memory_server_locator_checks_app_folder_then_dev_build()
    {
        var paths = new AppPaths(@"D:\Claude\Companion");
        var dev = @"D:\Claude\Companion\src\Companion.Memory\bin\Debug\net10.0-windows\Companion.Memory.exe";
        Assert.Equal(@"D:\app\Companion.Memory.exe", MemoryServerLocator.Find(paths, @"D:\app", p => p == @"D:\app\Companion.Memory.exe"));
        Assert.Equal(dev, MemoryServerLocator.Find(paths, @"D:\elsewhere", p => p == dev));
        Assert.Null(MemoryServerLocator.Find(paths, @"D:\elsewhere", _ => false));
    }

    [Fact]
    public void Parser_streams_text_joins_messages_and_reports_search_and_activity()
    {
        var handler = new RecordingHandler();
        var parser = new ClaudeCodeStreamParser(handler);

        parser.Feed(JsonSerializer.Serialize(new { type = "system", subtype = "init" }));
        parser.Feed(MessageStart());
        parser.Feed(Event(new { type = "content_block_start", index = 1, content_block = new { type = "tool_use", name = "WebSearch", id = "t1" } }));
        parser.Feed(TextDelta("Let me check."));
        parser.Feed(MessageStart());
        parser.Feed(TextDelta("It is "));
        parser.Feed(TextDelta("2026."));
        parser.Feed("not json at all");
        parser.Feed("");
        parser.Feed(Result());

        Assert.Equal("Let me check.\n\nIt is 2026.", parser.Text);
        Assert.Equal(new[] { "Let me check.", "\n\nIt is ", "2026." }, handler.Texts);
        Assert.Equal(1, handler.Searches);
        Assert.Equal(8, handler.Activity);
        Assert.True(parser.Finished);
        Assert.False(parser.IsError);
        Assert.Equal(TurnStop.EndTurn, parser.Stop);
    }

    [Fact]
    public void Parser_maps_stop_reasons()
    {
        var parser = new ClaudeCodeStreamParser(new RecordingHandler());
        parser.Feed(Result(stop: "max_tokens"));
        Assert.Equal(TurnStop.MaxTokens, parser.Stop);
    }

    [Fact]
    public void Plan_limit_failure_mentions_the_reset_time()
    {
        var parser = new ClaudeCodeStreamParser(new RecordingHandler());
        parser.Feed(JsonSerializer.Serialize(new { type = "rate_limit_event", rate_limit_info = new { status = "rejected", resetsAt = 1790281800L } }));
        parser.Feed(Result(isError: true, result: "Claude usage limit reached"));

        var failure = parser.ToFailure("");

        Assert.Equal(ClaudeErrorKind.PlanLimit, failure.Kind);
        Assert.Contains("resets at", failure.UserMessage);
    }

    [Theory]
    [InlineData("Not logged in · Please run /login", ClaudeErrorKind.NotLoggedIn)]
    [InlineData("API Error: Connection error.", ClaudeErrorKind.Network)]
    [InlineData("API Error: 529 overloaded", ClaudeErrorKind.Server)]
    [InlineData("API Error: 503 Service Unavailable", ClaudeErrorKind.Server)]
    [InlineData("Overloaded", ClaudeErrorKind.Server)]
    [InlineData("You've hit your limit · resets 5pm", ClaudeErrorKind.PlanLimit)]
    [InlineData("Claude AI usage limit reached|1790281800", ClaudeErrorKind.PlanLimit)]
    [InlineData("5-hour limit reached ∙ resets 5pm", ClaudeErrorKind.PlanLimit)]
    [InlineData("Rate limit exceeded", ClaudeErrorKind.PlanLimit)]
    [InlineData("resets 5pm", ClaudeErrorKind.Unknown)]
    [InlineData("tried 5 times", ClaudeErrorKind.Unknown)]
    [InlineData("something odd", ClaudeErrorKind.Unknown)]
    public void Failures_are_classified(string result, ClaudeErrorKind expected)
    {
        var parser = new ClaudeCodeStreamParser(new RecordingHandler());
        parser.Feed(Result(isError: true, result: result));
        Assert.Equal(expected, parser.ToFailure("").Kind);
    }

    [Theory]
    [InlineData("There's an issue with the selected model (claude-opus-5-5). It may not exist or you may not have access to it. Run /model to pick a different model.", "")]
    [InlineData("API Error: 404 {\"type\":\"error\",\"error\":{\"type\":\"not_found_error\",\"message\":\"model: claude-opus-5-5\"}}", "")]
    [InlineData("something odd", "[claude-code:unrecognized_model] {\"model\":\"claude-opus-5-5\",\"query_source\":\"sdk\"}")]
    public void An_unusable_model_asks_the_user_to_pick_another(string result, string stderr)
    {
        var parser = new ClaudeCodeStreamParser(new RecordingHandler());
        parser.Feed(Result(isError: true, result: result));

        var failure = parser.ToFailure(stderr, "claude-opus-5-5");

        Assert.Equal(ClaudeErrorKind.BadRequest, failure.Kind);
        Assert.Equal("I can't use the model claude-opus-5-5. Pick another in Settings.", failure.UserMessage);
    }

    [Fact]
    public void An_expired_login_wins_over_an_unrecognised_model_note()
    {
        // Claude Code 2.1.252, verbatim: an expired OAuth login with an unknown model id.
        var parser = new ClaudeCodeStreamParser(new RecordingHandler());
        parser.Feed(Result(isError: true, result: "Failed to authenticate: OAuth session expired and could not be refreshed"));

        var failure = parser.ToFailure("[claude-code:unrecognized_model] {\"model\":\"claude-opus-5-5\",\"query_source\":\"sdk\"}", "claude-opus-5-5");

        Assert.Equal(ClaudeErrorKind.NotLoggedIn, failure.Kind);
    }

    [Fact]
    public void A_retried_401_that_then_succeeds_is_not_a_failure()
    {
        var parser = new ClaudeCodeStreamParser(new RecordingHandler());
        parser.Feed(JsonSerializer.Serialize(new { type = "system", subtype = "api_retry", error_status = 401, error = "authentication_failed" }));
        parser.Feed(TextDelta("hi"));
        parser.Feed(Result());
        Assert.True(parser.Finished);
        Assert.False(parser.IsError);
    }

    [Fact]
    public void Missing_result_with_stderr_is_a_failure()
    {
        var parser = new ClaudeCodeStreamParser(new RecordingHandler());
        Assert.False(parser.Finished);
        Assert.Contains("boom", parser.ToFailure("boom").Message);
    }

    [Fact]
    public void Arguments_pick_model_by_effort_and_wire_tools_and_memory_server()
    {
        var options = new ClaudeCodeOptions(@"C:\c\claude.exe", "claude-sonnet-5", "claude-opus-5-5", @"D:\app\Companion.Memory.exe", @"D:\data\memory.json", @"D:\ws");
        var everyday = new TurnRequest("You are Pip.", [new HistoryMessage(true, "hi")], EffortLevel.Low, [], WebSearch: true);

        var args = ClaudeCodeClient.BuildArguments(everyday, options);

        Assert.Equal("-p", args[0]);
        Assert.Equal("claude-sonnet-5", args[args.ToList().IndexOf("--model") + 1]);
        Assert.Equal("low", args[args.ToList().IndexOf("--effort") + 1]);
        Assert.Equal("You are Pip.", args[args.ToList().IndexOf("--system-prompt") + 1]);
        Assert.Equal("", args[args.ToList().IndexOf("--setting-sources") + 1]);
        Assert.Contains("--no-session-persistence", args);
        Assert.DoesNotContain("--bare", args);
        var mcp = JsonDocument.Parse(args[args.ToList().IndexOf("--mcp-config") + 1]).RootElement.GetProperty("mcpServers").GetProperty("companion");
        Assert.Equal(@"D:\app\Companion.Memory.exe", mcp.GetProperty("command").GetString());
        Assert.Equal(@"D:\data\memory.json", mcp.GetProperty("args")[1].GetString());
        var allowed = args.ToList().IndexOf("--allowedTools");
        Assert.Equal(new[] { "WebSearch", "mcp__companion__remember", "mcp__companion__forget" }, args.Skip(allowed + 1));

        var harder = ClaudeCodeClient.BuildArguments(everyday with { Effort = EffortLevel.High, WebSearch = false }, options with { MemoryServerExe = null });
        Assert.Equal("claude-opus-5-5", harder[harder.ToList().IndexOf("--model") + 1]);
        Assert.Equal("", harder[harder.ToList().IndexOf("--tools") + 1]);
        Assert.DoesNotContain("--mcp-config", harder);
        Assert.DoesNotContain("--allowedTools", harder);
    }

    [Fact]
    public void Prompt_is_the_message_alone_or_a_transcript()
    {
        Assert.Equal("hi", ClaudeCodeClient.BuildPrompt([new HistoryMessage(true, "hi")]));
        var prompt = ClaudeCodeClient.BuildPrompt([new(true, "one"), new(false, "uno"), new(true, "two")]);
        Assert.StartsWith("Our conversation so far", prompt);
        Assert.Contains("User: one", prompt);
        Assert.Contains("You: uno", prompt);
        Assert.EndsWith("The user's new message:\ntwo", prompt);
    }
}
