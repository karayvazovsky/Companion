using Companion.Core;
using Xunit;

namespace Companion.Tests;

public class BrainTests
{
    private sealed class FakeClaude : IClaudeClient
    {
        public List<TurnRequest> Requests { get; } = new();
        public Exception? Throw { get; set; }
        public Func<TurnRequest, ITurnHandler, TurnResult> Behavior { get; set; } = (_, handler) =>
        {
            handler.OnTextDelta("Hi ");
            handler.OnTextDelta("there!");
            return new TurnResult("Hi there!", TurnStop.EndTurn, new UsageTotals(1000, 100, 0, 0, 0));
        };

        public Task<TurnResult> RunTurnAsync(TurnRequest request, ITurnHandler handler, CancellationToken ct)
        {
            Requests.Add(request);
            if (Throw is not null) throw Throw;
            return Task.FromResult(Behavior(request, handler));
        }

        public Task ValidateKeyAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class Listener : IBrainListener
    {
        public List<string> Events { get; } = new();
        public void OnThinking() => Events.Add("thinking");
        public void OnFirstText() => Events.Add("first");
        public void OnText(string delta) => Events.Add("text:" + delta);
        public void OnStatus(string status) => Events.Add("status:" + status);
    }

    private sealed class Rig : IDisposable
    {
        public TempDir Tmp { get; } = new();
        public FakeClock Clock { get; } = FakeClock.At(2026, 9, 24, 14);
        public FakeClaude Claude { get; } = new();
        public Listener Listener { get; } = new();
        public SettingsStore Settings { get; }
        public MemoryStore Memory { get; }
        public SpendTracker Spend { get; }
        public Brain Brain { get; }

        public Rig()
        {
            Settings = new SettingsStore(Tmp.PathOf("settings.json"), Clock, NullLog.Instance);
            Memory = new MemoryStore(Tmp.PathOf("memory.json"), Clock, NullLog.Instance);
            Spend = new SpendTracker(Settings, Clock);
            var tools = new ToolRegistry();
            tools.Register(new RememberTool(Memory));
            tools.Register(new ForgetTool(Memory));
            Brain = new Brain(Claude, new Conversation(Clock), () => SystemPrompt.Build("Pip", "Karco", Memory.Facts),
                tools, Spend, Settings, new ChatLog(Tmp.PathOf("logs"), Clock), NullLog.Instance);
        }

        public void Dispose() => Tmp.Dispose();
    }

    [Fact]
    public async Task Ask_streams_text_and_sends_a_well_formed_request()
    {
        using var rig = new Rig();

        var reply = await rig.Brain.AskAsync("hello", rig.Listener);

        Assert.Equal(BrainOutcome.Ok, reply.Outcome);
        Assert.Equal("Hi there!", reply.Text);
        Assert.Equal(new[] { "thinking", "first", "text:Hi ", "text:there!" }, rig.Listener.Events);
        var request = Assert.Single(rig.Claude.Requests);
        Assert.Equal(EffortLevel.Low, request.Effort);
        Assert.True(request.WebSearch);
        Assert.Equal(new[] { "remember", "forget" }, request.Tools.Select(t => t.Name));
        Assert.Contains("You are Pip", request.SystemPrompt);
        var last = Assert.Single(request.Messages);
        Assert.True(last.FromUser);
        Assert.Equal("[2026-09-24 14:00, Thursday]\nhello", last.Text);
        Assert.False(rig.Brain.Busy);
    }

    [Fact]
    public async Task Previous_exchanges_are_replayed_as_text()
    {
        using var rig = new Rig();
        await rig.Brain.AskAsync("one", rig.Listener);
        rig.Clock.Advance(TimeSpan.FromMinutes(1));

        await rig.Brain.AskAsync("two", rig.Listener);

        var messages = rig.Claude.Requests[1].Messages;
        Assert.Equal(3, messages.Count);
        Assert.EndsWith("one", messages[0].Text);
        Assert.False(messages[1].FromUser);
        Assert.Equal("Hi there!", messages[1].Text);
        Assert.EndsWith("two", messages[2].Text);
        Assert.Equal(rig.Claude.Requests[0].SystemPrompt, rig.Claude.Requests[1].SystemPrompt);
    }

    [Fact]
    public async Task Think_harder_appends_a_turn_with_high_effort()
    {
        using var rig = new Rig();
        await rig.Brain.AskAsync("tricky question", rig.Listener);

        await rig.Brain.ThinkHarderAsync(rig.Listener);

        var request = rig.Claude.Requests[1];
        Assert.Equal(EffortLevel.High, request.Effort);
        Assert.EndsWith(Brain.ThinkHarderPrompt, request.Messages[^1].Text);
        Assert.Equal(3, request.Messages.Count);
    }

    [Fact]
    public async Task Tool_calls_reach_memory_and_web_search_shows_status()
    {
        using var rig = new Rig();
        rig.Claude.Behavior = (_, handler) =>
        {
            handler.OnWebSearch();
            var outcome = handler.ExecuteTool("remember", ToolInput.From(new { fact = "Likes teal" }));
            Assert.False(outcome.IsError);
            handler.OnTextDelta("Got it!");
            return new TurnResult("Got it!", TurnStop.EndTurn, new UsageTotals(10, 10, 0, 0, 1));
        };

        await rig.Brain.AskAsync("remember I like teal", rig.Listener);

        Assert.Equal("Likes teal", Assert.Single(rig.Memory.Facts).Text);
        Assert.Contains("status:searching the web…", rig.Listener.Events);
    }

    [Fact]
    public async Task Spend_is_recorded_and_the_cap_blocks_requests()
    {
        using var rig = new Rig();
        await rig.Brain.AskAsync("hi", rig.Listener);
        Assert.Equal(0.006m, rig.Spend.ThisMonth);
        Assert.Equal(new UsageTotals(1000, 100, 0, 0, 0), rig.Brain.LastUsage);

        rig.Settings.Current.MonthlyCapUsd = 0.005m;
        var blocked = await rig.Brain.AskAsync("again", rig.Listener);

        Assert.Equal(BrainOutcome.CapReached, blocked.Outcome);
        Assert.Contains("cap", blocked.Notice);
        Assert.Single(rig.Claude.Requests);
    }

    [Fact]
    public async Task Warns_once_when_crossing_eighty_percent()
    {
        using var rig = new Rig();
        rig.Settings.Current.MonthlyCapUsd = 0.0075m;

        var reply = await rig.Brain.AskAsync("hi", rig.Listener);

        Assert.Equal(BrainOutcome.Ok, reply.Outcome);
        Assert.Contains("80%", reply.Notice);
    }

    [Fact]
    public async Task Refusals_are_not_kept_in_history()
    {
        using var rig = new Rig();
        rig.Claude.Behavior = (_, _) => new TurnResult("", TurnStop.Refusal, new UsageTotals(10, 0, 0, 0, 0));

        var refused = await rig.Brain.AskAsync("something", rig.Listener);
        rig.Claude.Behavior = (_, _) => new TurnResult("ok", TurnStop.EndTurn, new UsageTotals(10, 1, 0, 0, 0));
        await rig.Brain.AskAsync("next", rig.Listener);

        Assert.Equal(BrainOutcome.Refused, refused.Outcome);
        Assert.Single(rig.Claude.Requests[1].Messages);
    }

    [Fact]
    public async Task Max_tokens_marks_the_answer_as_cut_off()
    {
        using var rig = new Rig();
        rig.Claude.Behavior = (_, _) => new TurnResult("long answer", TurnStop.MaxTokens, new UsageTotals(10, 8000, 0, 0, 0));

        var reply = await rig.Brain.AskAsync("essay please", rig.Listener);

        Assert.Equal(BrainOutcome.CutOff, reply.Outcome);
        Assert.EndsWith("…(cut off)", reply.Text);
    }

    [Fact]
    public async Task Empty_answers_are_stored_as_a_placeholder()
    {
        using var rig = new Rig();
        rig.Claude.Behavior = (_, _) => new TurnResult("   ", TurnStop.EndTurn, new UsageTotals(10, 1, 0, 0, 0));
        var reply = await rig.Brain.AskAsync("one", rig.Listener);
        rig.Claude.Behavior = (_, _) => new TurnResult("ok", TurnStop.EndTurn, new UsageTotals(10, 1, 0, 0, 0));

        await rig.Brain.AskAsync("two", rig.Listener);

        Assert.Equal("(no answer)", reply.Text);
        Assert.Equal("(no answer)", rig.Claude.Requests[1].Messages[1].Text);
    }

    [Fact]
    public async Task New_chat_clears_history()
    {
        using var rig = new Rig();
        await rig.Brain.AskAsync("one", rig.Listener);

        rig.Brain.NewChat();
        await rig.Brain.AskAsync("two", rig.Listener);

        Assert.Single(rig.Claude.Requests[1].Messages);
    }

    [Theory]
    [InlineData(ClaudeErrorKind.Network, "internet")]
    [InlineData(ClaudeErrorKind.Unauthorized, "API key")]
    [InlineData(ClaudeErrorKind.Billing, "credit")]
    [InlineData(ClaudeErrorKind.RateLimited, "busy")]
    [InlineData(ClaudeErrorKind.Server, "trouble")]
    [InlineData(ClaudeErrorKind.NoAccess, "This API key can't use claude-opus-5-5 (check your console plan/workspace).")]
    public async Task Errors_become_friendly_notices(ClaudeErrorKind kind, string expected)
    {
        using var rig = new Rig();
        rig.Claude.Throw = new ClaudeException(kind, "raw detail");

        var reply = await rig.Brain.AskAsync("hi", rig.Listener);

        Assert.Equal(BrainOutcome.Error, reply.Outcome);
        Assert.Contains(expected, reply.Notice);
        Assert.False(rig.Brain.Busy);
    }
}
