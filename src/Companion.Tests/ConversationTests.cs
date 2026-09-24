using System.IO;
using System.Text.Json;
using Companion.Core;
using Xunit;

namespace Companion.Tests;

public class ConversationTests
{
    [Fact]
    public void System_prompt_is_built_once_per_conversation()
    {
        var clock = FakeClock.At(2026, 9, 24, 14);
        var conversation = new Conversation(clock);
        var builds = 0;
        string Build() { builds++; return $"prompt {builds}"; }

        conversation.BeginTurn(Build, promptVersion: 3);
        conversation.Add("u1", "a1");
        clock.Advance(TimeSpan.FromMinutes(5));
        conversation.BeginTurn(Build, promptVersion: 3);

        Assert.Equal(1, builds);
        Assert.Equal("prompt 1", conversation.SystemPrompt);
    }

    [Fact]
    public void System_prompt_is_rebuilt_when_its_version_changes_and_history_is_kept()
    {
        var clock = FakeClock.At(2026, 9, 24, 14);
        var conversation = new Conversation(clock);
        var builds = 0;
        conversation.BeginTurn(() => $"prompt {++builds}", promptVersion: 1);
        conversation.Add("u1", "a1");

        conversation.BeginTurn(() => $"prompt {++builds}", promptVersion: 2);

        Assert.Equal("prompt 2", conversation.SystemPrompt);
        Assert.Equal("u1", Assert.Single(conversation.Exchanges).User);
    }

    [Fact]
    public void Resets_after_thirty_quiet_minutes()
    {
        var clock = FakeClock.At(2026, 9, 24, 14);
        var conversation = new Conversation(clock);
        var builds = 0;
        conversation.BeginTurn(() => $"prompt {++builds}", promptVersion: 0);
        conversation.Add("u1", "a1");

        clock.Advance(TimeSpan.FromMinutes(30));
        conversation.BeginTurn(() => $"prompt {++builds}", promptVersion: 0);

        Assert.Empty(conversation.Exchanges);
        Assert.Equal("prompt 2", conversation.SystemPrompt);
    }

    [Fact]
    public void Keeps_only_the_last_ten_exchanges()
    {
        var conversation = new Conversation(FakeClock.At(2026, 9, 24, 14));
        for (var i = 1; i <= 12; i++) conversation.Add($"u{i}", $"a{i}");

        Assert.Equal(10, conversation.Exchanges.Count);
        Assert.Equal("u3", conversation.Exchanges[0].User);
        Assert.Equal("a12", conversation.Exchanges[^1].Assistant);
    }

    [Fact]
    public void User_turn_starts_with_local_date_and_time()
    {
        var conversation = new Conversation(FakeClock.At(2026, 9, 24, 14, 5));
        Assert.Equal("[2026-09-24 14:05, Thursday]\nhello", conversation.FormatUserTurn("hello"));
    }

    [Fact]
    public void Reset_clears_everything()
    {
        var conversation = new Conversation(FakeClock.At(2026, 9, 24, 14));
        conversation.BeginTurn(() => "p", promptVersion: 0);
        conversation.Add("u", "a");

        conversation.Reset();

        Assert.Empty(conversation.Exchanges);
        Assert.Null(conversation.SystemPrompt);
    }
}

public class SystemPromptTests
{
    [Fact]
    public void Includes_names_personality_and_facts_with_ids()
    {
        var facts = new List<Fact> { new("f_01", "Works in Cinema 4D", DateTimeOffset.UnixEpoch) };

        var prompt = SystemPrompt.Build("Pip", "Karco", facts);

        Assert.Contains("You are Pip", prompt);
        Assert.Contains("Karco", prompt);
        Assert.Contains("- [f_01] Works in Cinema 4D", prompt);
        Assert.Contains("remember", prompt);
        Assert.DoesNotContain("(nothing yet)", prompt);
    }

    [Fact]
    public void Says_nothing_yet_without_facts()
    {
        Assert.Contains("(nothing yet)", SystemPrompt.Build("Pip", "Karco", []));
    }
}

public class ChatLogTests
{
    [Fact]
    public void Appends_one_json_line_per_exchange_in_a_monthly_file()
    {
        using var tmp = new TempDir();
        var log = new ChatLog(tmp.Root, FakeClock.At(2026, 9, 24, 14));

        log.Append("hi", "hello!");
        log.Append("bye", "see ya");

        var lines = File.ReadAllLines(tmp.PathOf("chat-2026-09.jsonl"));
        Assert.Equal(2, lines.Length);
        using var first = JsonDocument.Parse(lines[0]);
        Assert.Equal("hi", first.RootElement.GetProperty("user").GetString());
        Assert.Equal("hello!", first.RootElement.GetProperty("assistant").GetString());
    }
}
