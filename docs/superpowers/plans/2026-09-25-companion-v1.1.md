# Companion v1.1 Implementation Plan — subscription brain + desktop movement

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Answer through the user's Claude subscription (installed Claude Code) instead of the paid API by default, and let the pet live anywhere on the desktop: floor, window tops, walls, ceiling, free spots, on any monitor, with dangling/pendulum dragging and teleporting.

**Architecture:** A new `ClaudeCodeClient : IClaudeClient` runs `claude.exe -p --output-format stream-json …` per turn and parses its JSON lines; remember/forget reach Claude Code through a tiny MCP stdio server (`Companion.Memory.exe`). Movement logic (perch planning, pendulum) is pure Core code with tests; the WPF side scans monitors/windows via Win32 and renders tilt with transforms.

**Tech Stack:** as v1 (.NET 10, WPF + WinForms tray, xUnit) + Claude Code 2.1.x CLI.

**Specs:** `docs/superpowers/specs/2026-09-25-companion-v1.1-subscription-and-movement-design.md` (binding; extends the v1 spec).

## Global Constraints

- Work on branch `feature/companion-v1` in `D:\Claude\Companion`. All v1 global constraints still apply (net10.0-windows, Anthropic 12.50.0, data under `<root>\data`, DPAPI key, no AppData, footprint < 60 MB / < 1 % CPU idle, commit trailer `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`).
- `claude.exe` is started directly with `ProcessStartInfo.ArgumentList` — never via `cmd.exe`, `claude.cmd` or a shell. Never pass `--bare` (it disables subscription login).
- Default brain: `BrainKind.Subscription`; everyday model `claude-sonnet-5`, think-harder model `claude-opus-5-5`.
- Live checks through the **subscription** are allowed but must stay tiny (model `claude-haiku-4-5`, ≤ 5 short prompts per task). Never call the paid API; never read/type an API key; never touch files under `data\` except what the app itself writes.
- In `UseWPF` projects (src/Companion, src/Companion.Tests, tools/SkinGen) add `using System.IO;` where Path/File/Directory/IOException are used.
- Snap distance 24 DIP; pet window = 2× sprite width × 1.5× sprite height; teleport every 5–15 min; motion timer 33 ms only while held/falling/hanging from the ceiling; window follow 250 ms only on WindowTop.

## File Map (new or changed)

```
src/Companion.Core/
  Settings.cs            + BrainKind, Brain, SubscriptionEverydayModel, SubscriptionThinkHarderModel, ClaudeCodePath, AutoTeleport
  Spend.cs               cap/warning only for ApiKey brain
  ClaudeContracts.cs     + NotInstalled, NotLoggedIn, PlanLimit; ClaudeException.UserMessage
  Brain.cs               uses UserMessage; friendly texts for new kinds
  Onboarding.cs          subscription path skips the key step
  Memory.cs              + Reload()
  PetState.cs            + Perch, PositionY
  ClaudeCode.cs          ClaudeCodeLocator, MemoryServerLocator, ClaudeCodeStreamParser, ClaudeCodeOptions, ClaudeCodeClient
  McpStdioServer.cs      minimal MCP stdio JSON-RPC server over ToolRegistry
  Desktop.cs             RectD, ScreenArea, DesktopWindow, PerchKind, Perch, PerchPlanner
  Pendulum.cs            damped swing
src/Companion.Memory/    new console exe: MCP memory server
src/Companion/
  NativeMethods.cs       + EnumWindows, DWM, RECT
  DesktopScanner.cs      monitors + top-level windows in DIPs
  PetWindow.xaml(.cs)    padded stage, tilt, 2-D drag events
  PetController.cs       rewritten around perches
  ChatBubble / LineBubble  monitor-aware placement
  ChatController.cs      no-brain message func, TurnFinished event
  SettingsWindow.xaml(.cs) Brain, models, auto-teleport
  AppHost.cs             brain selection, memory reload, wiring
tools/SkinGen/Program.cs + hang, poof frames (sheet 4×10)
skins/default/sheet.png, sheet.json
tools/Smoke/Program.cs   + --subscription mode
tools/publish.ps1        also publishes Companion.Memory
docs/manual-checklist.md + v1.1 checks
```

---

### Task 1: Core — brain setting, subscription-aware spend/onboarding, new error kinds, memory reload

**Files:** Modify `src/Companion.Core/Settings.cs`, `Spend.cs`, `ClaudeContracts.cs`, `Brain.cs`, `Onboarding.cs`, `Memory.cs`; tests `src/Companion.Tests/SettingsTests.cs`, `SpendTests.cs`, `BrainTests.cs`, `OnboardingTests.cs`, `MemoryTests.cs`.

**Interfaces produced:** `enum BrainKind { Subscription, ApiKey }`; `AppSettings.Brain` (default Subscription), `SubscriptionEverydayModel` ("claude-sonnet-5"), `SubscriptionThinkHarderModel` ("claude-opus-5-5"), `ClaudeCodePath` (string?), `AutoTeleport` (bool, default true); `ClaudeErrorKind.NotInstalled|NotLoggedIn|PlanLimit`; `ClaudeException(kind, message, inner = null, userMessage = null)` with `string? UserMessage`; `OnboardingFlow(pet, secrets, validateKey, hotkey, Func<BrainKind>? brain = null)`, `static IsComplete(pet, secrets, BrainKind brain = BrainKind.ApiKey)`; `MemoryStore.Reload()`.

- [ ] **Step 1: Failing tests.** Add:

```csharp
// SettingsTests.cs (in SettingsStoreTests)
[Fact]
public void Brain_defaults_to_subscription_with_sonnet_and_opus()
{
    using var tmp = new TempDir();
    var store = new SettingsStore(tmp.PathOf("settings.json"), FakeClock.At(2026, 9, 24, 12), NullLog.Instance);
    Assert.Equal(BrainKind.Subscription, store.Current.Brain);
    Assert.Equal("claude-sonnet-5", store.Current.SubscriptionEverydayModel);
    Assert.Equal("claude-opus-5-5", store.Current.SubscriptionThinkHarderModel);
    Assert.True(store.Current.AutoTeleport);
}

[Fact]
public void Blank_subscription_models_fall_back_to_defaults()
{
    using var tmp = new TempDir();
    var path = tmp.PathOf("settings.json");
    File.WriteAllText(path, """{ "subscriptionEverydayModel": " ", "subscriptionThinkHarderModel": "" }""");
    var store = new SettingsStore(path, FakeClock.At(2026, 9, 24, 12), NullLog.Instance);
    Assert.Equal("claude-sonnet-5", store.Current.SubscriptionEverydayModel);
    Assert.Equal("claude-opus-5-5", store.Current.SubscriptionThinkHarderModel);
}

// SpendTests.cs
[Fact]
public void Subscription_brain_never_hits_the_cap_or_warns()
{
    using var tmp = new TempDir();
    var clock = FakeClock.At(2026, 9, 24, 12);
    var settings = new SettingsStore(tmp.PathOf("settings.json"), clock, NullLog.Instance);
    settings.Current.MonthlyCapUsd = 1m;
    settings.Current.SpendByMonth["2026-09"] = 5m;
    var spend = new SpendTracker(settings, clock);
    Assert.False(spend.CapReached);
    Assert.False(spend.TakeWarning());
    settings.Current.Brain = BrainKind.ApiKey;
    Assert.True(spend.CapReached);
}

// MemoryTests.cs (in MemoryStoreTests)
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

// OnboardingTests.cs — the Rig gets a brain field; existing tests keep ApiKey
//   public BrainKind Brain { get; set; } = BrainKind.ApiKey;
//   Flow(): new(Pet, Secrets, validate, "Ctrl+Alt+Space", () => Brain)
[Fact]
public async Task Subscription_brain_skips_the_key_step()
{
    using var rig = new Rig { Brain = BrainKind.Subscription };
    var flow = rig.Flow();
    Assert.Null(await flow.SubmitAsync("Pip", CancellationToken.None));
    Assert.Null(await flow.SubmitAsync("Karco", CancellationToken.None));
    Assert.Equal(OnboardingStep.Done, flow.Step);
    Assert.Contains("subscription", flow.Prompt);
    Assert.False(rig.Secrets.HasKey);
    Assert.True(OnboardingFlow.IsComplete(rig.Pet, rig.Secrets, BrainKind.Subscription));
    Assert.False(OnboardingFlow.IsComplete(rig.Pet, rig.Secrets, BrainKind.ApiKey));
}

// BrainTests.cs
[Theory]
[InlineData(ClaudeErrorKind.NotInstalled, "Claude Code")]
[InlineData(ClaudeErrorKind.NotLoggedIn, "log in")]
[InlineData(ClaudeErrorKind.PlanLimit, "limit")]
public async Task Subscription_errors_become_friendly_notices(ClaudeErrorKind kind, string expected)
{
    using var rig = new Rig();
    rig.Claude.Throw = new ClaudeException(kind, "raw");
    var reply = await rig.Brain.AskAsync("hi", rig.Listener);
    Assert.Contains(expected, reply.Notice);
}

[Fact]
public async Task A_user_message_on_the_exception_wins()
{
    using var rig = new Rig();
    rig.Claude.Throw = new ClaudeException(ClaudeErrorKind.PlanLimit, "raw", userMessage: "Resets at 18:30.");
    var reply = await rig.Brain.AskAsync("hi", rig.Listener);
    Assert.Equal("Resets at 18:30.", reply.Notice);
}
```

Also update the existing tests that rely on the cap/warning (`SpendTests.Cap_and_one_time_warning_at_80_percent`, `SpendTests.Record_*` if it checks the cap, `BrainTests.Spend_is_recorded_and_the_cap_blocks_requests`, `BrainTests.Warns_once_when_crossing_eighty_percent`) by setting `settings.Current.Brain = BrainKind.ApiKey;` (or `rig.Settings.Current.Brain = BrainKind.ApiKey;`) at their start.

- [ ] **Step 2: Run, see RED** (`dotnet test src/Companion.Tests`): compile errors for `BrainKind`, `Reload`, new kinds.

- [ ] **Step 3: Implement.**

`Settings.cs` — add the enum and properties, and extend `Normalize()`:

```csharp
public enum BrainKind { Subscription, ApiKey }

// in AppSettings:
    /// <summary>Where answers come from: the user's Claude subscription via Claude Code (default) or a paid API key.</summary>
    public BrainKind Brain { get; set; } = BrainKind.Subscription;
    public string SubscriptionEverydayModel { get; set; } = "claude-sonnet-5";
    public string SubscriptionThinkHarderModel { get; set; } = "claude-opus-5-5";
    /// <summary>Optional full path to claude.exe; empty means auto-detect.</summary>
    public string? ClaudeCodePath { get; set; }
    public bool AutoTeleport { get; set; } = true;

// in SettingsStore.Normalize():
        if (string.IsNullOrWhiteSpace(Current.SubscriptionEverydayModel)) Current.SubscriptionEverydayModel = "claude-sonnet-5";
        if (string.IsNullOrWhiteSpace(Current.SubscriptionThinkHarderModel)) Current.SubscriptionThinkHarderModel = "claude-opus-5-5";
        Current.SubscriptionEverydayModel = Current.SubscriptionEverydayModel.Trim();
        Current.SubscriptionThinkHarderModel = Current.SubscriptionThinkHarderModel.Trim();
```

`Spend.cs`:

```csharp
    /// <summary>The cap only guards pay-per-use API spend; the subscription brain has no per-question cost.</summary>
    public bool CapReached => settings.Current.Brain == BrainKind.ApiKey && ThisMonth >= Cap;

    public bool TakeWarning()
    {
        if (settings.Current.Brain != BrainKind.ApiKey) return false;
        // ...existing body unchanged...
    }
```

`ClaudeContracts.cs`:

```csharp
public enum ClaudeErrorKind { Network, Unauthorized, NoAccess, Billing, RateLimited, Server, BadRequest, NotInstalled, NotLoggedIn, PlanLimit, Unknown }

public sealed class ClaudeException(ClaudeErrorKind kind, string message, Exception? inner = null, string? userMessage = null)
    : Exception(message, inner)
{
    public ClaudeErrorKind Kind { get; } = kind;

    /// <summary>Optional ready-to-show text (e.g. with a reset time) that overrides the generic message for the kind.</summary>
    public string? UserMessage { get; } = userMessage;
}
```

`Brain.cs` — in the `catch (ClaudeException ex)` use `ex.UserMessage ?? FriendlyError(ex.Kind)`, and add to `FriendlyError`:

```csharp
        ClaudeErrorKind.NotInstalled => "I can't find Claude Code on this PC. Install it, or switch my Brain to API key in Settings.",
        ClaudeErrorKind.NotLoggedIn => "Claude Code isn't logged in. Open a terminal, run `claude` and log in, then ask me again.",
        ClaudeErrorKind.PlanLimit => "You've reached your Claude plan's usage limit. Try again a bit later.",
```

`Onboarding.cs` — add a `Func<BrainKind> _brain` (default `() => BrainKind.ApiKey`) as an optional last constructor parameter; `IsComplete(PetStateStore pet, SecretStore secrets, BrainKind brain = BrainKind.ApiKey) => names set && (brain == BrainKind.Subscription || secrets.HasKey)`; `NextStep()` returns `ApiKey` only when `_brain() == BrainKind.ApiKey && !_secrets.HasKey`; the Done prompt in subscription mode:

```csharp
        _ when _brain() == BrainKind.Subscription =>
            $"All set! I'll think with your Claude subscription (through Claude Code). Double-click me or press **{_hotkey}** whenever you need me. *wags*",
        _ => $"All set! Double-click me or press **{_hotkey}** whenever you need me. *wags*",
```

`Memory.cs` — keep the `ILog` in a field and add:

```csharp
    /// <summary>Re-reads memory.json; another process (the MCP memory server) may have changed it.</summary>
    public void Reload()
    {
        bool changed;
        lock (_gate)
        {
            var fresh = JsonFile.Load(_path, () => new Document(), _log, _clock);
            changed = !fresh.Facts.SequenceEqual(_doc.Facts);
            if (changed)
            {
                _doc.Facts = fresh.Facts;
                _doc.NextId = Math.Max(Math.Max(_doc.NextId, fresh.NextId), MaxExistingId() + 1);
                Interlocked.Increment(ref _version);
            }
        }
        if (changed) Changed?.Invoke();
    }
```

(`_doc` must allow `Facts` reassignment — it already has a public setter.)

- [ ] **Step 4: GREEN.** `dotnet test src/Companion.Tests` → all pass.
- [ ] **Step 5: Commit** `feat(core): subscription brain setting, error kinds and memory reload`.

---

### Task 2: Core — Claude Code locator, stream parser and client

**Files:** Create `src/Companion.Core/ClaudeCode.cs`; test `src/Companion.Tests/ClaudeCodeTests.cs`.

**Interfaces produced:** `ClaudeCodeLocator.Find(string? overridePath)` (+ internal testable overload); `MemoryServerLocator.Find(AppPaths paths, string baseDirectory)` (+ internal overload with `exists`); `ClaudeCodeStreamParser(ITurnHandler)` with `Feed(string)`, `Text`, `Finished`, `IsError`, `Stop`, `ToFailure(string stderr)`; `record ClaudeCodeOptions(string ExePath, string EverydayModel, string ThinkHarderModel, string? MemoryServerExe, string MemoryFile, string WorkingDirectory)`; `ClaudeCodeClient(ClaudeCodeOptions) : IClaudeClient` with internal static `BuildArguments(TurnRequest, ClaudeCodeOptions)` and `BuildPrompt(IReadOnlyList<HistoryMessage>)`.

- [ ] **Step 1: Failing tests** — create `src/Companion.Tests/ClaudeCodeTests.cs`:

```csharp
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
    [InlineData("something odd", ClaudeErrorKind.Unknown)]
    public void Failures_are_classified(string result, ClaudeErrorKind expected)
    {
        var parser = new ClaudeCodeStreamParser(new RecordingHandler());
        parser.Feed(Result(isError: true, result: result));
        Assert.Equal(expected, parser.ToFailure("").Kind);
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
```

- [ ] **Step 2: RED** (compile errors).

- [ ] **Step 3: Implement** `src/Companion.Core/ClaudeCode.cs`:

```csharp
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Companion.Core;

/// <summary>Finds claude.exe itself: starting claude.cmd would route arguments through cmd.exe, which mangles them.</summary>
public static class ClaudeCodeLocator
{
    private const string PackageExe = @"node_modules\@anthropic-ai\claude-code\bin\claude.exe";

    public static string? Find(string? overridePath) => Find(
        overridePath,
        File.Exists,
        Environment.GetEnvironmentVariable("PATH") ?? "",
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));

    internal static string? Find(string? overridePath, Func<string, bool> exists, string pathVariable, string userProfile, string appData)
    {
        if (!string.IsNullOrWhiteSpace(overridePath) && exists(overridePath)) return overridePath;
        foreach (var dir in pathVariable.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var exe = Path.Combine(dir, "claude.exe");
            if (exists(exe)) return exe;
            if (exists(Path.Combine(dir, "claude.cmd")))
            {
                var packaged = Path.Combine(dir, PackageExe);
                if (exists(packaged)) return packaged;
            }
        }
        foreach (var candidate in new[] { Path.Combine(userProfile, ".local", "bin", "claude.exe"), Path.Combine(appData, "npm", PackageExe) })
        {
            if (exists(candidate)) return candidate;
        }
        return null;
    }
}

/// <summary>Finds Companion.Memory.exe: next to the app when published, or in its dev build folder.</summary>
public static class MemoryServerLocator
{
    public const string ExeName = "Companion.Memory.exe";

    public static string? Find(AppPaths paths, string baseDirectory) => Find(paths, baseDirectory, File.Exists);

    internal static string? Find(AppPaths paths, string baseDirectory, Func<string, bool> exists)
    {
        var candidates = new[]
        {
            Path.Combine(baseDirectory, ExeName),
            Path.Combine(paths.Root, "src", "Companion.Memory", "bin", "Debug", "net10.0-windows", ExeName),
            Path.Combine(paths.Root, "src", "Companion.Memory", "bin", "Release", "net10.0-windows", ExeName),
        };
        return candidates.FirstOrDefault(exists);
    }
}

/// <summary>Reads Claude Code's <c>--output-format stream-json</c> lines for one turn.</summary>
public sealed class ClaudeCodeStreamParser(ITurnHandler handler)
{
    private readonly StringBuilder _text = new();
    private bool _firstTextThisMessage = true;

    public string Text => _text.ToString();
    public bool Finished { get; private set; }
    public bool IsError { get; private set; }
    public string? ResultText { get; private set; }
    public TurnStop Stop { get; private set; } = TurnStop.EndTurn;
    public string? LastApiError { get; private set; }
    public int? LastApiErrorStatus { get; private set; }
    public bool LimitRejected { get; private set; }
    public DateTimeOffset? LimitResetsAt { get; private set; }

    public void Feed(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        JsonDocument document;
        try { document = JsonDocument.Parse(line); }
        catch (JsonException) { return; }
        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return;
            handler.OnActivity();
            switch (Str(root, "type"))
            {
                case "stream_event" when root.TryGetProperty("event", out var ev):
                    OnStreamEvent(ev);
                    break;
                case "system" when Str(root, "subtype") == "api_retry":
                    LastApiError = Str(root, "error");
                    LastApiErrorStatus = root.TryGetProperty("error_status", out var status) && status.ValueKind == JsonValueKind.Number ? status.GetInt32() : null;
                    break;
                case "rate_limit_event" when root.TryGetProperty("rate_limit_info", out var info):
                    if (Str(info, "status") == "rejected")
                    {
                        LimitRejected = true;
                        if (info.TryGetProperty("resetsAt", out var resets) && resets.ValueKind == JsonValueKind.Number)
                            LimitResetsAt = DateTimeOffset.FromUnixTimeSeconds(resets.GetInt64());
                    }
                    break;
                case "result":
                    Finished = true;
                    IsError = root.TryGetProperty("is_error", out var isError) && isError.ValueKind == JsonValueKind.True;
                    ResultText = Str(root, "result");
                    Stop = Str(root, "stop_reason") switch
                    {
                        "max_tokens" => TurnStop.MaxTokens,
                        "refusal" => TurnStop.Refusal,
                        _ => TurnStop.EndTurn,
                    };
                    break;
            }
        }
    }

    /// <summary>Turns a failed or unfinished run into a ClaudeException with a user-facing message.</summary>
    public ClaudeException ToFailure(string stderr)
    {
        var detail = string.Join(" | ", new[] { ResultText, LastApiError, stderr.Trim() }.Where(s => !string.IsNullOrWhiteSpace(s)));
        if (detail.Length == 0) detail = "Claude Code exited without an answer.";
        if (LimitRejected || LastApiErrorStatus == 429 || Has(detail, "usage limit") || Has(detail, "rate limit"))
        {
            var when = LimitResetsAt is { } at ? $" It resets at {at.ToLocalTime():HH:mm}." : "";
            return new ClaudeException(ClaudeErrorKind.PlanLimit, detail, userMessage: $"You've reached your Claude plan's usage limit.{when}");
        }
        if (Has(detail, "/login") || Has(detail, "not logged in") || Has(detail, "authentication") || (!Finished && LastApiErrorStatus == 401))
            return new ClaudeException(ClaudeErrorKind.NotLoggedIn, detail);
        if (Has(detail, "connection error") || Has(detail, "ENOTFOUND") || Has(detail, "ECONNREFUSED") || Has(detail, "network"))
            return new ClaudeException(ClaudeErrorKind.Network, detail);
        if (LastApiErrorStatus >= 500 || Has(detail, "overloaded") || Has(detail, " 5"))
            return new ClaudeException(ClaudeErrorKind.Server, detail);
        return new ClaudeException(ClaudeErrorKind.Unknown, detail);
    }

    private void OnStreamEvent(JsonElement ev)
    {
        switch (Str(ev, "type"))
        {
            case "message_start":
                _firstTextThisMessage = true;
                break;
            case "content_block_start" when ev.TryGetProperty("content_block", out var block):
                if (Str(block, "type") == "tool_use" && Str(block, "name") == "WebSearch") handler.OnWebSearch();
                break;
            case "content_block_delta" when ev.TryGetProperty("delta", out var delta) && Str(delta, "type") == "text_delta":
                var piece = Str(delta, "text") ?? "";
                if (piece.Length == 0) return;
                if (_firstTextThisMessage && _text.Length > 0 && _text[^1] != '\n') piece = "\n\n" + piece;
                _firstTextThisMessage = false;
                _text.Append(piece);
                handler.OnTextDelta(piece);
                break;
        }
    }

    private static string? Str(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool Has(string text, string part) => text.Contains(part, StringComparison.OrdinalIgnoreCase);
}

public sealed record ClaudeCodeOptions(
    string ExePath,
    string EverydayModel,
    string ThinkHarderModel,
    string? MemoryServerExe,
    string MemoryFile,
    string WorkingDirectory);

/// <summary>Answers through the user's installed Claude Code (their subscription): one claude.exe process per turn.</summary>
public sealed class ClaudeCodeClient(ClaudeCodeOptions options) : IClaudeClient
{
    /// <summary>Nothing to validate: Claude Code uses its own login.</summary>
    public Task ValidateKeyAsync(CancellationToken ct) => Task.CompletedTask;

    public async Task<TurnResult> RunTurnAsync(TurnRequest request, ITurnHandler handler, CancellationToken ct)
    {
        Directory.CreateDirectory(options.WorkingDirectory);
        var start = new ProcessStartInfo(options.ExePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = options.WorkingDirectory,
        };
        foreach (var argument in BuildArguments(request, options)) start.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = start };
        try
        {
            process.Start();
        }
        catch (Win32Exception ex)
        {
            throw new ClaudeException(ClaudeErrorKind.NotInstalled, ex.Message, ex);
        }

        var stderrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);
        var parser = new ClaudeCodeStreamParser(handler);
        using var registration = ct.Register(() => Kill(process));
        try
        {
            await process.StandardInput.WriteAsync(BuildPrompt(request.Messages).AsMemory(), ct);
            process.StandardInput.Close();
            while (await process.StandardOutput.ReadLineAsync(ct) is { } line) parser.Feed(line);
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            Kill(process);
            throw;
        }
        catch (IOException ex) when (!ct.IsCancellationRequested)
        {
            Kill(process);
            throw new ClaudeException(ClaudeErrorKind.Unknown, ex.Message, ex);
        }

        var stderr = await stderrTask;
        if (!parser.Finished || parser.IsError) throw parser.ToFailure(stderr);
        return new TurnResult(parser.Text, parser.Stop, new UsageTotals());
    }

    internal static IReadOnlyList<string> BuildArguments(TurnRequest request, ClaudeCodeOptions options)
    {
        var model = request.Effort == EffortLevel.High ? options.ThinkHarderModel : options.EverydayModel;
        var args = new List<string>
        {
            "-p",
            "--output-format", "stream-json",
            "--verbose",
            "--include-partial-messages",
            "--model", model,
            "--effort", request.Effort.ToString().ToLowerInvariant(),
            "--system-prompt", request.SystemPrompt,
            "--tools", request.WebSearch ? "WebSearch" : "",
            "--permission-mode", "dontAsk",
            "--settings", """{"disableAllHooks":true}""",
            "--setting-sources", "",
            "--no-session-persistence",
            "--strict-mcp-config",
        };
        var allowed = new List<string>();
        if (request.WebSearch) allowed.Add("WebSearch");
        if (options.MemoryServerExe is { } server)
        {
            args.Add("--mcp-config");
            args.Add(JsonSerializer.Serialize(new
            {
                mcpServers = new
                {
                    companion = new { type = "stdio", command = server, args = new[] { "--memory", options.MemoryFile } },
                },
            }));
            allowed.Add("mcp__companion__remember");
            allowed.Add("mcp__companion__forget");
        }
        if (allowed.Count > 0)
        {
            args.Add("--allowedTools");   // variadic: keep it last
            args.AddRange(allowed);
        }
        return args;
    }

    internal static string BuildPrompt(IReadOnlyList<HistoryMessage> messages)
    {
        if (messages.Count == 1) return messages[0].Text;
        var sb = new StringBuilder("Our conversation so far (you are the assistant):\n\n");
        for (var i = 0; i < messages.Count - 1; i++)
        {
            sb.Append(messages[i].FromUser ? "User: " : "You: ").Append(messages[i].Text).Append("\n\n");
        }
        sb.Append("---\nThe user's new message:\n").Append(messages[^1].Text);
        return sb.ToString();
    }

    private static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) { }
        catch (Win32Exception) { }
    }
}
```

Note: the `" 5"` Server heuristic matches "API Error: 529 …"; if it misclassifies a test case, prefer a regex `API Error: 5\d\d` and keep the tests.

- [ ] **Step 4: GREEN**, full suite.
- [ ] **Step 5: Live check (subscription, tiny).** Add a temporary xUnit-free check: `dotnet run` is not needed — instead, from PowerShell, run the real client through a throwaway console snippet is overkill; simply verify the CLI accepts the argument list by running `claude.exe` with the exact flags from `BuildArguments` for model `claude-haiku-4-5`, `--tools ""`, no MCP, prompt "Reply with: ok" (use `Start-Process`-free invocation via .NET or the smoke tool in Task 4). If `--setting-sources ""` or `--permission-mode dontAsk` is rejected by the installed CLI, adjust `BuildArguments` minimally (e.g. drop the flag) and update the test; record the exact CLI error in the report.
- [ ] **Step 6: Commit** `feat(core): Claude Code client with stream-json parsing`.

---

### Task 3: MCP memory server (Core protocol + Companion.Memory.exe)

**Files:** Create `src/Companion.Core/McpStdioServer.cs`, `src/Companion.Memory/Companion.Memory.csproj`, `src/Companion.Memory/Program.cs`; modify `Companion.sln`, `src/Companion/Companion.csproj` (build-order reference); test `src/Companion.Tests/McpTests.cs`.

**Interfaces produced:** `McpStdioServer(ToolRegistry tools, string serverName = "companion")` with `Task RunAsync(TextReader input, TextWriter output, CancellationToken ct = default)` and internal `string? Handle(string line)`; exe usage `Companion.Memory.exe --memory <memory.json>`.

- [ ] **Step 1: Failing tests** — `src/Companion.Tests/McpTests.cs`:

```csharp
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
```

- [ ] **Step 2: RED.**

- [ ] **Step 3: Implement** `src/Companion.Core/McpStdioServer.cs`:

```csharp
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
```

`src/Companion.Memory/Companion.Memory.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <AssemblyName>Companion.Memory</AssemblyName>
    <ServerGarbageCollection>false</ServerGarbageCollection>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Companion.Core\Companion.Core.csproj" />
  </ItemGroup>
</Project>
```

`src/Companion.Memory/Program.cs`:

```csharp
using System.Text;
using Companion.Core;

var index = Array.IndexOf(args, "--memory");
if (index < 0 || index + 1 >= args.Length)
{
    Console.Error.WriteLine("usage: Companion.Memory --memory <memory.json>");
    return 2;
}

var memory = new MemoryStore(args[index + 1], new SystemClock(), NullLog.Instance);
var tools = new ToolRegistry();
tools.Register(new RememberTool(memory));
tools.Register(new ForgetTool(memory));

using var input = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
using var output = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true };
await new McpStdioServer(tools).RunAsync(input, output);
return 0;
```

Add to the solution (`dotnet sln add src/Companion.Memory/Companion.Memory.csproj`) and, so a normal app build also builds the server, add to `src/Companion/Companion.csproj`:

```xml
    <ProjectReference Include="..\Companion.Memory\Companion.Memory.csproj" ReferenceOutputAssembly="false" />
```

- [ ] **Step 4: GREEN** + `dotnet build Companion.sln`.
- [ ] **Step 5: Live check (free, no Claude):** pipe three JSON-RPC lines (initialize, tools/list, a remember call) into the built `src\Companion.Memory\bin\Debug\net10.0-windows\Companion.Memory.exe --memory <temp dir>\memory.json` and confirm three JSON replies and the fact in the temp file. Then one tiny subscription check: run `claude.exe -p --model claude-haiku-4-5 --output-format stream-json --verbose --tools "" --strict-mcp-config --mcp-config <json pointing at the built exe and a temp memory file> --settings {"disableAllHooks":true} --permission-mode dontAsk --allowedTools mcp__companion__remember` with stdin "Use the remember tool to save the fact 'Likes teal', then reply ok." (invoke via a small .NET/PowerShell `ProcessStartInfo` with `ArgumentList` so empty args survive) and confirm the temp memory file contains the fact. Report the exact commands and outcomes.
- [ ] **Step 6: Commit** `feat: MCP memory server for Claude Code`.

---

### Task 4: App — choose the brain, reload memory, settings, smoke, publish

**Files:** Modify `src/Companion/AppHost.cs`, `ChatController.cs`, `SettingsWindow.xaml(.cs)`, `tools/Smoke/Program.cs`, `tools/publish.ps1`.

**Interfaces:** `ChatController(ChatBubble, PetController, Func<Brain?>, Func<string> petName, Func<string> noBrainMessage, ILog)`; `event Action? ChatController.TurnFinished` (raised on the UI thread after every Brain turn, success or not).

- [ ] **Step 1: ChatController** — add the `Func<string> noBrainMessage` constructor parameter (after `petName`), show `noBrainMessage()` instead of the hard-coded "I need an API key…" text, and raise `TurnFinished?.Invoke()` at the end of `RunAsync` after the reply is rendered.

- [ ] **Step 2: AppHost** — keep `AppPaths` in a `_paths` field; add `private string _noBrainMessage = "";`; replace the body of `RebuildBrain()` after the busy guard with:

```csharp
        _rebuildPending = false;
        IClaudeClient? client;
        if (_settings.Current.Brain == BrainKind.Subscription)
        {
            var exe = ClaudeCodeLocator.Find(_settings.Current.ClaudeCodePath);
            var memoryServer = MemoryServerLocator.Find(_paths, AppContext.BaseDirectory);
            if (memoryServer is null) _log.Info("Companion.Memory.exe not found; remember/forget are unavailable in subscription mode.");
            _noBrainMessage = "I can't find Claude Code on this PC. Install it, or switch my **Brain** to API key in Settings.";
            client = exe is null
                ? null
                : new ClaudeCodeClient(new ClaudeCodeOptions(
                    exe,
                    _settings.Current.SubscriptionEverydayModel,
                    _settings.Current.SubscriptionThinkHarderModel,
                    memoryServer,
                    _paths.MemoryFile,
                    _paths.Workspace));
        }
        else
        {
            _noBrainMessage = "I need an API key before I can think. Right-click me → **Settings**.";
            client = _secrets.LoadApiKey() is { } key ? new AnthropicClaudeClient(key) : null;
        }
        _brain = client is null
            ? null
            : new Brain(client, _conversation,
                () => SystemPrompt.Build(PetName, _petStore.Current.UserName ?? "my human", _memory.Facts),
                () => _memory.Version,
                _tools, _spend, _settings, _chatLog, _log);
```

Pass `() => _noBrainMessage` to the ChatController; subscribe `_chat.TurnFinished += () => { if (_settings.Current.Brain == BrainKind.Subscription) _memory.Reload(); UpdateTooltip(); };`. Update every `OnboardingFlow.IsComplete(_petStore, _secrets)` call to pass `_settings.Current.Brain`, and `CreateOnboarding()` to pass `() => _settings.Current.Brain` as the last argument. Tooltip:

```csharp
    private void UpdateTooltip()
    {
        var cost = _settings.Current.Brain == BrainKind.Subscription ? "Claude subscription" : $"${_spend.ThisMonth:0.00} this month";
        _tray.SetTooltip($"{PetName} · {_pet.Life.State.ToString().ToLowerInvariant()} · {cost}");
    }
```

- [ ] **Step 3: SettingsWindow** — add, above the hotkey row, a "Brain" `ComboBox` (`BrainBox`, items `Enum.GetValues<BrainKind>()`), and two editable `ComboBox`es "Everyday model" (`EverydayModelBox`) and "Think-harder model" (`HarderModelBox`) with items `claude-sonnet-5`, `claude-opus-5-5`, `claude-haiku-4-5` and `IsEditable="True"`; a muted hint line under Brain: "Subscription = uses your Claude plan through Claude Code (no extra cost). ApiKey = pay-per-use API credit (the monthly cap applies)." Load/save them with the other settings (`settings.Brain`, `SubscriptionEverydayModel = EverydayModelBox.Text.Trim()`, `SubscriptionThinkHarderModel = HarderModelBox.Text.Trim()`). Grow the Grid's RowDefinitions accordingly.

- [ ] **Step 4: Smoke `--subscription` mode** — in `tools/Smoke/Program.cs`, when `args` contains `--subscription`, build the client as

```csharp
    var exe = ClaudeCodeLocator.Find(null) ?? throw new InvalidOperationException("Claude Code not found.");
    var server = MemoryServerLocator.Find(paths, AppContext.BaseDirectory);
    client = new ClaudeCodeClient(new ClaudeCodeOptions(exe, "claude-haiku-4-5", "claude-sonnet-5", server,
        Path.Combine(temp, "memory.json"), Path.Combine(temp, "workspace")));
```

(skip key validation), create the `MemoryStore` on that same temp `memory.json`, and call `memory.Reload()` after each question before printing. Keep the API path unchanged when the flag is absent.

- [ ] **Step 5: publish.ps1** — after the app publish, also run:

```powershell
dotnet publish "$root\src\Companion.Memory\Companion.Memory.csproj" -c Release -r win-x64 --self-contained false -o "$root\app"
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish (memory server) failed.' }
```

- [ ] **Step 6: Build, full tests, then live subscription smoke** (allowed, tiny): `dotnet build Companion.sln`, `dotnet test src/Companion.Tests`, then `dotnet run --project tools/Smoke -- --subscription`. Expected: 4 answers streamed, `Facts:` shows the teal fact, a "searching the web…" status on the search questions. Record output. Then a launch-smoke of the app (start, ~10 s alive, no ERROR in the newest `data\logs\app-*.log`, Stop-Process). Do not ask the pet anything through the UI.
- [ ] **Step 7: Commit** `feat(app): answer through the Claude subscription by default`.

---

### Task 5: Core — perches, desktop geometry and pendulum

**Files:** Create `src/Companion.Core/Desktop.cs`, `src/Companion.Core/Pendulum.cs`; modify `src/Companion.Core/PetState.cs`; tests `src/Companion.Tests/DesktopTests.cs`, `MemoryTests.cs` (PetStateStore reset).

**Interfaces produced:** `readonly record struct RectD(double X, double Y, double Width, double Height)` (+ Left/Top/Right/Bottom/CenterX/CenterY); `record ScreenArea(RectD Bounds, RectD WorkArea)`; `record DesktopWindow(long Handle, RectD Bounds)`; `enum PerchKind { Floor, WindowTop, WallLeft, WallRight, Ceiling, Free }`; `record Perch(PerchKind Kind, double X, double Y, long WindowHandle = 0, double OffsetX = 0)` with `CanWalk`; `static PerchPlanner` (`SnapDistance = 24`, `ScreenFor`, `ForDrop`, `Random`, `Follow`, `Fall`, `WalkRange`); `Pendulum` (`MaxAngle = 35`, `Angle`, `Step(dt, pivotVelocityX, restAngle = 0)`, `Reset()`, `static IdleSwing(seconds)`); `PetState.Perch` (default Floor), `PetState.PositionY` (double?).

- [ ] **Step 1: Failing tests** — `src/Companion.Tests/DesktopTests.cs`:

```csharp
using Companion.Core;
using Xunit;

namespace Companion.Tests;

public class DesktopTests
{
    private const double Size = 64;
    private static readonly ScreenArea Primary = new(new RectD(0, 0, 1920, 1080), new RectD(0, 0, 1920, 1040));
    private static readonly ScreenArea Second = new(new RectD(1920, 0, 1280, 1024), new RectD(1920, 0, 1280, 1024));
    private static readonly ScreenArea[] Screens = [Primary, Second];
    private static readonly DesktopWindow Editor = new(42, new RectD(500, 300, 800, 600));

    [Fact]
    public void Drop_near_edges_snaps_to_floor_walls_and_ceiling()
    {
        Assert.Equal(new Perch(PerchKind.Floor, 900, 976), PerchPlanner.ForDrop(900, 1040 - 64 - 10, Size, Screens, []));
        Assert.Equal(new Perch(PerchKind.WallLeft, 0, 500), PerchPlanner.ForDrop(10, 500, Size, Screens, []));
        Assert.Equal(new Perch(PerchKind.WallRight, 3136, 400), PerchPlanner.ForDrop(3200 - 64 - 5, 400, Size, Screens, []));
        Assert.Equal(new Perch(PerchKind.Ceiling, 700, 0), PerchPlanner.ForDrop(700, 8, Size, Screens, []));
    }

    [Fact]
    public void Drop_on_a_window_top_perches_there_with_an_offset()
    {
        var perch = PerchPlanner.ForDrop(700, 300 - 64 + 10, Size, Screens, [Editor]);
        Assert.Equal(new Perch(PerchKind.WindowTop, 700, 236, 42, 200), perch);
        Assert.True(perch.CanWalk);
    }

    [Fact]
    public void Drop_in_open_space_stays_free_and_clamped()
    {
        Assert.Equal(new Perch(PerchKind.Free, 900, 500), PerchPlanner.ForDrop(900, 500, Size, Screens, [Editor]));
        Assert.Equal(new Perch(PerchKind.WallLeft, 0, 500), PerchPlanner.ForDrop(-5000, 500, Size, Screens, []));
        Assert.False(new Perch(PerchKind.Free, 0, 0).CanWalk);
    }

    [Fact]
    public void A_window_touching_the_top_of_the_screen_is_not_perchable()
    {
        var maximized = new DesktopWindow(7, new RectD(0, 0, 1920, 1040));
        Assert.Equal(PerchKind.Ceiling, PerchPlanner.ForDrop(700, 10, Size, Screens, [maximized]).Kind);
    }

    [Fact]
    public void Follow_tracks_the_window_and_gives_up_when_it_is_gone()
    {
        var perch = new Perch(PerchKind.WindowTop, 700, 236, 42, 200);
        var moved = new DesktopWindow(42, new RectD(600, 400, 800, 600));
        Assert.Equal(new Perch(PerchKind.WindowTop, 800, 336, 42, 200), PerchPlanner.Follow(perch, Size, Screens, [moved]));
        Assert.Null(PerchPlanner.Follow(perch, Size, Screens, []));
        Assert.Null(PerchPlanner.Follow(perch, Size, Screens, [new DesktopWindow(42, new RectD(600, 400, 60, 600))]));
        var floor = new Perch(PerchKind.Floor, 10, 976);
        Assert.Same(floor, PerchPlanner.Follow(floor, Size, Screens, []));
    }

    [Fact]
    public void Fall_lands_on_the_floor_of_the_screen_below()
    {
        Assert.Equal(new Perch(PerchKind.Floor, 2500, 960), PerchPlanner.Fall(2500, Size, 100, Screens));
        Assert.Equal(new Perch(PerchKind.Floor, 1856, 976), PerchPlanner.Fall(1900, Size, 100, [Primary]));
    }

    [Fact]
    public void Walk_range_follows_the_perch()
    {
        Assert.Equal((0d, 1856d), PerchPlanner.WalkRange(new Perch(PerchKind.Floor, 5, 976), Size, Screens, []));
        Assert.Equal((500d, 1236d), PerchPlanner.WalkRange(new Perch(PerchKind.WindowTop, 700, 236, 42, 200), Size, Screens, [Editor]));
    }

    [Fact]
    public void Random_perches_stay_on_screen_and_use_every_kind()
    {
        var rng = new Random(3);
        var kinds = new HashSet<PerchKind>();
        for (var i = 0; i < 400; i++)
        {
            var perch = PerchPlanner.Random(rng, Size, Screens, [Editor]);
            kinds.Add(perch.Kind);
            var wa = PerchPlanner.ScreenFor(perch.X + Size / 2, perch.Y + Size / 2, Screens).WorkArea;
            Assert.InRange(perch.X, wa.Left - 0.001, wa.Right - Size + 0.001);
            Assert.InRange(perch.Y, wa.Top - 0.001, wa.Bottom - Size + 0.001);
        }
        Assert.Equal(new[] { PerchKind.Floor, PerchKind.WindowTop, PerchKind.WallLeft, PerchKind.WallRight, PerchKind.Ceiling }.ToHashSet(), kinds);
        for (var i = 0; i < 100; i++) Assert.NotEqual(PerchKind.WindowTop, PerchPlanner.Random(rng, Size, Screens, []).Kind);
    }

    [Fact]
    public void Pendulum_lags_behind_motion_clamps_and_settles()
    {
        var pendulum = new Pendulum();
        for (var i = 0; i < 30; i++) pendulum.Step(1 / 30.0, 1000);
        Assert.True(pendulum.Angle > 5);
        for (var i = 0; i < 30; i++) pendulum.Step(1 / 30.0, 1_000_000);
        Assert.True(pendulum.Angle <= Pendulum.MaxAngle);
        for (var i = 0; i < 120; i++) pendulum.Step(1 / 30.0, 0);
        Assert.InRange(pendulum.Angle, -0.5, 0.5);
        for (var i = 0; i < 30; i++) pendulum.Step(1 / 30.0, -1000);
        Assert.True(pendulum.Angle < -5);
        pendulum.Reset();
        Assert.Equal(0, pendulum.Angle);
        Assert.Equal(6, Pendulum.IdleSwing(0.5), 3);
    }
}
```

Add to `PetStateStoreTests.Defaults_round_trip_and_reset`: set `Current.Perch = PerchKind.Ceiling; Current.PositionY = 3;` before saving, assert they round-trip, and after `Reset()` assert `Perch == PerchKind.Floor` and `PositionY == null`.

- [ ] **Step 2: RED.**

- [ ] **Step 3: Implement.** `src/Companion.Core/Desktop.cs`:

```csharp
namespace Companion.Core;

public readonly record struct RectD(double X, double Y, double Width, double Height)
{
    public double Left => X;
    public double Top => Y;
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public double CenterX => X + Width / 2;
    public double CenterY => Y + Height / 2;

    public bool Contains(double px, double py) => px >= Left && px < Right && py >= Top && py < Bottom;
}

public sealed record ScreenArea(RectD Bounds, RectD WorkArea);

public sealed record DesktopWindow(long Handle, RectD Bounds);

public enum PerchKind { Floor, WindowTop, WallLeft, WallRight, Ceiling, Free }

/// <summary>Where the pet is. X/Y = top-left of the sprite square in DIPs; OffsetX = distance from the window's left edge (WindowTop).</summary>
public sealed record Perch(PerchKind Kind, double X, double Y, long WindowHandle = 0, double OffsetX = 0)
{
    public bool CanWalk => Kind is PerchKind.Floor or PerchKind.WindowTop;
}

/// <summary>Pure placement rules: snapping a drop, teleport targets, following windows, falling, walking ranges.</summary>
public static class PerchPlanner
{
    public const double SnapDistance = 24;
    private const double MinWindowWidthInPets = 1.5;

    public static ScreenArea ScreenFor(double x, double y, IReadOnlyList<ScreenArea> screens)
    {
        foreach (var screen in screens)
            if (screen.Bounds.Contains(x, y)) return screen;
        return screens.MinBy(s => Distance(s.Bounds, x, y))!;
    }

    public static Perch ForDrop(double x, double y, double size, IReadOnlyList<ScreenArea> screens, IReadOnlyList<DesktopWindow> windows)
    {
        var wa = ScreenFor(x + size / 2, y + size / 2, screens).WorkArea;
        x = Math.Clamp(x, wa.Left, Math.Max(wa.Left, wa.Right - size));
        y = Math.Clamp(y, wa.Top, Math.Max(wa.Top, wa.Bottom - size));
        var best = new Perch(PerchKind.Free, x, y);
        var bestDistance = double.MaxValue;

        void Consider(double distance, Perch perch)
        {
            if (distance <= SnapDistance && distance < bestDistance)
            {
                bestDistance = distance;
                best = perch;
            }
        }

        var center = x + size / 2;
        foreach (var window in windows)
        {
            var b = window.Bounds;
            if (!Perchable(window, wa, size) || center < b.Left || center > b.Right) continue;
            var px = Math.Clamp(x, b.Left, b.Right - size);
            Consider(Math.Abs(y + size - b.Top), new Perch(PerchKind.WindowTop, px, b.Top - size, window.Handle, px - b.Left));
            break;   // windows arrive topmost first: only the frontmost window under the pet counts
        }
        Consider(wa.Bottom - (y + size), new Perch(PerchKind.Floor, x, wa.Bottom - size));
        Consider(x - wa.Left, new Perch(PerchKind.WallLeft, wa.Left, y));
        Consider(wa.Right - (x + size), new Perch(PerchKind.WallRight, wa.Right - size, y));
        Consider(y - wa.Top, new Perch(PerchKind.Ceiling, x, wa.Top));
        return best;
    }

    public static Perch Random(Random rng, double size, IReadOnlyList<ScreenArea> screens, IReadOnlyList<DesktopWindow> windows)
    {
        var wa = screens[rng.Next(screens.Count)].WorkArea;
        double RandomX() => wa.Left + rng.NextDouble() * Math.Max(0, wa.Width - size);
        double RandomY() => wa.Top + rng.NextDouble() * Math.Max(0, wa.Height - size);
        var tops = windows.Where(w => Perchable(w, wa, size) && w.Bounds.Left >= wa.Left - 1 && w.Bounds.Right <= wa.Right + 1).ToList();

        var roll = rng.NextDouble();
        if (roll < 0.25 && tops.Count > 0)
        {
            var w = tops[rng.Next(tops.Count)];
            var px = w.Bounds.Left + rng.NextDouble() * (w.Bounds.Width - size);
            return new Perch(PerchKind.WindowTop, px, w.Bounds.Top - size, w.Handle, px - w.Bounds.Left);
        }
        if (roll < 0.40) return new Perch(PerchKind.Ceiling, RandomX(), wa.Top);
        if (roll < 0.50) return new Perch(PerchKind.WallLeft, wa.Left, RandomY());
        if (roll < 0.60) return new Perch(PerchKind.WallRight, wa.Right - size, RandomY());
        return new Perch(PerchKind.Floor, RandomX(), wa.Bottom - size);
    }

    /// <summary>Updated WindowTop perch, the same perch for other kinds, or null when the window is gone or unusable.</summary>
    public static Perch? Follow(Perch perch, double size, IReadOnlyList<ScreenArea> screens, IReadOnlyList<DesktopWindow> windows)
    {
        if (perch.Kind != PerchKind.WindowTop) return perch;
        var window = windows.FirstOrDefault(w => w.Handle == perch.WindowHandle);
        if (window is null) return null;
        var b = window.Bounds;
        if (!Perchable(window, ScreenFor(b.CenterX, b.Top, screens).WorkArea, size)) return null;
        var px = Math.Clamp(b.Left + perch.OffsetX, b.Left, b.Right - size);
        return perch with { X = px, Y = b.Top - size, OffsetX = px - b.Left };
    }

    public static Perch Fall(double x, double size, double fromY, IReadOnlyList<ScreenArea> screens)
    {
        var wa = ScreenFor(x + size / 2, fromY + size / 2, screens).WorkArea;
        return new Perch(PerchKind.Floor, Math.Clamp(x, wa.Left, Math.Max(wa.Left, wa.Right - size)), wa.Bottom - size);
    }

    public static (double Min, double Max) WalkRange(Perch perch, double size, IReadOnlyList<ScreenArea> screens, IReadOnlyList<DesktopWindow> windows)
    {
        if (perch.Kind == PerchKind.WindowTop && windows.FirstOrDefault(w => w.Handle == perch.WindowHandle) is { } window)
            return (window.Bounds.Left, window.Bounds.Right - size);
        var wa = ScreenFor(perch.X + size / 2, perch.Y + size / 2, screens).WorkArea;
        return (wa.Left, wa.Right - size);
    }

    private static bool Perchable(DesktopWindow window, RectD workArea, double size) =>
        window.Bounds.Width >= size * MinWindowWidthInPets
        && window.Bounds.Top - size >= workArea.Top
        && window.Bounds.Top <= workArea.Bottom;

    private static double Distance(RectD r, double x, double y)
    {
        var dx = Math.Max(Math.Max(r.Left - x, 0), x - r.Right);
        var dy = Math.Max(Math.Max(r.Top - y, 0), y - r.Bottom);
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
```

`src/Companion.Core/Pendulum.cs`:

```csharp
namespace Companion.Core;

/// <summary>Damped swing in degrees (+ = clockwise, i.e. the body swings left). The body lags behind the grip's motion.</summary>
public sealed class Pendulum
{
    public const double MaxAngle = 35;
    private const double Stiffness = 60;
    private const double Damping = 9;
    private const double DegreesPerDipPerSecond = 0.03;
    private double _velocity;

    public double Angle { get; private set; }

    public void Step(double seconds, double pivotVelocityX, double restAngle = 0)
    {
        var dt = Math.Clamp(seconds, 0, 0.1);
        var target = Math.Clamp(restAngle + pivotVelocityX * DegreesPerDipPerSecond, -MaxAngle, MaxAngle);
        _velocity += (Stiffness * (target - Angle) - Damping * _velocity) * dt;
        Angle = Math.Clamp(Angle + _velocity * dt, -MaxAngle, MaxAngle);
    }

    public void Reset()
    {
        Angle = 0;
        _velocity = 0;
    }

    /// <summary>Gentle idle swing: ±6°, one full swing every 2 s.</summary>
    public static double IdleSwing(double seconds) => 6 * Math.Sin(seconds * Math.PI);
}
```

`PetState.cs`: add `public PerchKind Perch { get; set; } = PerchKind.Floor;` and `public double? PositionY { get; set; }`, and reset both in `PetStateStore.Reset()`.

- [ ] **Step 4: GREEN** (if a hand-computed expectation in the tests is off by rounding, fix the expectation only after re-deriving it from the spec rules; report it).
- [ ] **Step 5: Commit** `feat(core): perch planning, pendulum and saved perch`.

---

### Task 6: Hanging and poof art

**Files:** Modify `tools/SkinGen/Program.cs`, `skins/default/sheet.json`, `src/Companion.Tests/DefaultSkinTests.cs`; regenerate `skins/default/sheet.png`.

- [ ] **Step 1: Test first** — in `DefaultSkinTests`, expect height **160**, and add `"hang"` and `"poof"` to the required animation names. Run → FAIL.
- [ ] **Step 2: sheet.json** — add:

```json
    "hang":     { "fps": 2, "loop": true,  "frames": [32, 32, 32, 33] },
    "poof":     { "fps": 8, "loop": false, "frames": [34, 35, 36] }
```

- [ ] **Step 3: SkinGen** — grow `Build()` to 40 frames and add palette colours `['W'] = 0xFFF2F2F2`, `['V'] = 0xFFC9C9C9`:

```csharp
        // in Build(): var f = new Grid[40]; ...existing 0..31... then:
        f[32] = Hang(blink: false);
        f[33] = Hang(blink: true);
        f[34] = new Grid().Stamp(Poof0, 0, 0);
        f[35] = new Grid().Stamp(Poof1, 0, 0);
        f[36] = new Grid().Stamp(Poof2, 0, 0);
        f[37] = f[32];
        f[38] = f[33];
        f[39] = f[36];

    /// <summary>Hanging by both arms from an edge above; rotated by the app for walls.</summary>
    private static Grid Hang(bool blink)
    {
        var g = new Grid();
        foreach (var x in new[] { 3, 12 }) { g.Set(x, 0, 'O'); g.Set(x, 1, 'O'); }
        for (var y = 2; y <= 8; y++) g.Row(y, 3, 12, 'O');
        g.Row(9, 3, 12, 'D');
        foreach (var x in new[] { 5, 10 })
        {
            if (blink) { g.Set(x - 1, 5, 'K'); g.Set(x, 5, 'K'); }
            else { g.Set(x, 4, 'K'); g.Set(x, 5, 'K'); }
        }
        for (var y = 10; y <= 12; y++)
            foreach (var x in new[] { 4, 6, 9, 11 })
                g.Set(x, y, 'O');
        return g;
    }

    private static readonly string[] Poof0 =
    [
        "", "", "", "", "",
        "......WWW",
        ".....WWWWW",
        "....WWWVWWW",
        "....WWWWWWW",
        ".....WWVWW",
        "......WWW",
    ];

    private static readonly string[] Poof1 =
    [
        "", "",
        "....WW...WW",
        "...WWWW.WWWW",
        "..WWWWWWWWWWW",
        ".WWWWWVWWWWWWW",
        ".WWWWWWWWWVWWW",
        "..WWWVWWWWWWW",
        "..WWWWWWWWWWW",
        "...WWWW.WWWW",
        "....WW...WW",
    ];

    private static readonly string[] Poof2 =
    [
        "",
        "..W.......W",
        "",
        ".....W.....W",
        "W",
        "...........W",
        "..W.....W",
        "",
        "......W......W",
        "",
        ".W........W",
    ];
```

(`Stamp` already skips '.' and tolerates short rows.) Regenerate: `dotnet run --project tools/SkinGen -- skins/default/sheet.png`. Make an 8× preview in the SDD scratch folder and look at rows 9–10: a creature hanging by two raised arms (open eyes, blink) and three white puff frames.
- [ ] **Step 4: GREEN**, commit `feat(skin): hanging and poof frames`.

---

### Task 7: The pet moves anywhere (scanner, padded window, perch-driven controller, monitor-aware bubbles, teleport)

**Files:** Modify `src/Companion/NativeMethods.cs`, `PetWindow.xaml(.cs)`, `PetController.cs`, `ChatBubble.xaml.cs`, `LineBubble.xaml.cs`, `ChatController.cs`, `AppHost.cs`, `SettingsWindow.xaml(.cs)`; create `src/Companion/DesktopScanner.cs`.

**Interfaces:** `DesktopScanner(Window reference)` with `IgnoreWindow(Window)`, `Screens()` (primary first), `Windows()`; `PetWindow` with `SpriteSize`, `SpritePosition` (sprite top-left, screen DIPs), `SetSize`, `SetFrame`, `SetFacing`, `SetTilt(double angle, bool pivotTop)`, events `Clicked`, `DoubleClicked`, `DragStarted(Point)`, `DragMoved(Point)`, `DragEnded(Point)`; `PetController(PetWindow, PetLife, SpriteSheet, IUserActivity, SettingsStore, PetStateStore, Func<bool> chatBusy)` with `Bounds` (sprite rect), `WorkArea` (rect of the pet's monitor), `Teleport()`; `ChatBubble.ShowAt(Rect anchor, Rect area)`, `LineBubble.Say(string, Rect anchor, Rect area)`.

- [ ] **Step 1: NativeMethods** — add:

```csharp
    internal const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    internal const int DWMWA_CLOAKED = 14;

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT { public int Left, Top, Right, Bottom; }

    internal delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] internal static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetWindowTextLength(IntPtr hWnd);
    [DllImport("dwmapi.dll")] internal static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out RECT value, int size);
    [DllImport("dwmapi.dll")] internal static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);
```

- [ ] **Step 2: DesktopScanner** — create `src/Companion/DesktopScanner.cs`:

```csharp
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Companion.Core;
using Forms = System.Windows.Forms;

namespace Companion;

/// <summary>Monitors and visible top-level windows, in WPF device-independent pixels.</summary>
public sealed class DesktopScanner(Window reference)
{
    private readonly HashSet<IntPtr> _ignored = new();
    private Matrix _fromDevice = Matrix.Identity;

    public void IgnoreWindow(Window window) => _ignored.Add(new WindowInteropHelper(window).EnsureHandle());

    public IReadOnlyList<ScreenArea> Screens()
    {
        var m = FromDevice();
        return Forms.Screen.AllScreens
            .OrderBy(s => s.Primary ? 0 : 1)
            .Select(s => new ScreenArea(ToDip(s.Bounds, m), ToDip(s.WorkingArea, m)))
            .ToList();
    }

    /// <summary>Visible, non-minimised, titled application windows, topmost first.</summary>
    public IReadOnlyList<DesktopWindow> Windows()
    {
        var m = FromDevice();
        var result = new List<DesktopWindow>();
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (_ignored.Contains(hwnd) || !NativeMethods.IsWindowVisible(hwnd) || NativeMethods.IsIconic(hwnd)) return true;
            if (NativeMethods.GetWindowTextLength(hwnd) == 0) return true;
            if ((NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE) & NativeMethods.WS_EX_TOOLWINDOW) != 0) return true;
            if (NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0) return true;
            if (NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS, out NativeMethods.RECT r, Marshal.SizeOf<NativeMethods.RECT>()) != 0) return true;
            if (r.Right - r.Left < 120 || r.Bottom - r.Top < 80) return true;
            result.Add(new DesktopWindow(hwnd.ToInt64(), ToDip(new Rect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top), m)));
            return true;
        }, IntPtr.Zero);
        return result;
    }

    private Matrix FromDevice()
    {
        if (PresentationSource.FromVisual(reference)?.CompositionTarget is { } target) _fromDevice = target.TransformFromDevice;
        return _fromDevice;
    }

    private static RectD ToDip(System.Drawing.Rectangle r, Matrix m) => ToDip(new Rect(r.X, r.Y, r.Width, r.Height), m);

    private static RectD ToDip(Rect r, Matrix m)
    {
        var topLeft = m.Transform(r.TopLeft);
        var bottomRight = m.Transform(r.BottomRight);
        return new RectD(topLeft.X, topLeft.Y, bottomRight.X - topLeft.X, bottomRight.Y - topLeft.Y);
    }
}
```

- [ ] **Step 3: PetWindow** — replace `PetWindow.xaml` content with a padded stage and a flip+tilt transform group:

```xml
<Window x:Class="Companion.PetWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Companion" WindowStyle="None" AllowsTransparency="True" Background="Transparent"
        Topmost="True" ShowInTaskbar="False" ShowActivated="False" ResizeMode="NoResize">
    <Canvas>
        <Image x:Name="Sprite" Stretch="Fill" RenderOptions.BitmapScalingMode="NearestNeighbor">
            <Image.RenderTransform>
                <TransformGroup>
                    <ScaleTransform x:Name="Flip" ScaleX="1" />
                    <RotateTransform x:Name="Tilt" Angle="0" />
                </TransformGroup>
            </Image.RenderTransform>
        </Image>
    </Canvas>
</Window>
```

and `PetWindow.xaml.cs`:

```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Companion;

/// <summary>
/// Transparent, non-activating sprite window. The window is 2× the sprite wide and 1.5× tall so tilted poses aren't
/// clipped; the padding is fully transparent and therefore click-through.
/// </summary>
public partial class PetWindow : Window
{
    private Point _pressPoint;
    private bool _pressed;
    private bool _dragging;

    public PetWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => WindowStyles.MakeToolWindow(this, clickThrough: false);
    }

    public event Action? Clicked;
    public event Action? DoubleClicked;
    public event Action<Point>? DragStarted;
    public event Action<Point>? DragMoved;
    public event Action<Point>? DragEnded;

    public double SpriteSize { get; private set; }

    /// <summary>Top-left of the sprite square in screen DIPs.</summary>
    public Point SpritePosition
    {
        get => new(Left + SpriteSize / 2, Top);
        set
        {
            Left = value.X - SpriteSize / 2;
            Top = value.Y;
        }
    }

    public void SetFrame(ImageSource frame) => Sprite.Source = frame;

    public void SetSize(double size)
    {
        var position = SpritePosition;
        SpriteSize = size;
        Width = size * 2;
        Height = size * 1.5;
        Sprite.Width = size;
        Sprite.Height = size;
        Canvas.SetLeft(Sprite, size / 2);
        Canvas.SetTop(Sprite, 0);
        Flip.CenterX = size / 2;
        Tilt.CenterX = size / 2;
        SpritePosition = position;
    }

    public void SetFacing(int direction) => Flip.ScaleX = direction < 0 ? -1 : 1;

    /// <summary>Rotates the sprite around its top-centre (hanging/swinging) or its centre (walls).</summary>
    public void SetTilt(double angle, bool pivotTop)
    {
        Tilt.CenterY = pivotTop ? 0 : SpriteSize / 2;
        Tilt.Angle = angle;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (e.ClickCount >= 2)
        {
            _pressed = false;
            DoubleClicked?.Invoke();
            return;
        }
        _pressed = true;
        _dragging = false;
        _pressPoint = ScreenPoint(e);
        CaptureMouse();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!_pressed || e.LeftButton != MouseButtonState.Pressed) return;
        var point = ScreenPoint(e);
        if (!_dragging)
        {
            if (Math.Abs(point.X - _pressPoint.X) < 4 && Math.Abs(point.Y - _pressPoint.Y) < 4) return;
            _dragging = true;
            DragStarted?.Invoke(point);
        }
        DragMoved?.Invoke(point);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (!_pressed) return;
        e.Handled = true;
        _pressed = false;
        ReleaseMouseCapture();
        if (_dragging) DragEnded?.Invoke(ScreenPoint(e));
        else Clicked?.Invoke();
        _dragging = false;
    }

    private Point ScreenPoint(MouseEventArgs e)
    {
        var device = PointToScreen(e.GetPosition(this));
        return PresentationSource.FromVisual(this)?.CompositionTarget is { } target
            ? target.TransformFromDevice.Transform(device)
            : device;
    }
}
```

- [ ] **Step 4: Bubbles** — `ChatBubble.ShowAt(Rect anchor, Rect area)` stores both; `Reposition()` uses the stored area (fallback `SystemParameters.WorkArea` when empty) and opens **below** the anchor when there is no room above:

```csharp
        var area = _area.Width > 0 ? _area : SystemParameters.WorkArea;
        Left = Math.Clamp(_anchor.Left + _anchor.Width / 2 - ActualWidth / 2, area.Left, Math.Max(area.Left, area.Right - ActualWidth));
        var above = _anchor.Top - ActualHeight + 8;
        Top = above >= area.Top ? above : Math.Clamp(_anchor.Bottom - 8, area.Top, Math.Max(area.Top, area.Bottom - ActualHeight));
```

`LineBubble.Say(string text, Rect anchor, Rect area)` gets the same area/below logic. Update callers: `ChatController.Open()` → `_bubble.ShowAt(_pet.Bounds, _pet.WorkArea)`; AppHost `LineReady` → `_line.Say(text, _pet.Bounds, _pet.WorkArea)`.

- [ ] **Step 5: PetController** — replace `src/Companion/PetController.cs` with:

```csharp
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Companion.Core;

namespace Companion;

/// <summary>
/// Drives PetLife once per second and keeps the pet on a perch (floor, window top, wall, ceiling or a free spot) on any
/// monitor: dragging (dangle + pendulum), dropping (snap), following windows, falling, walking and teleporting.
/// </summary>
public sealed class PetController : IDisposable
{
    private const double WalkSpeedDipPerSecond = 24;
    private const double FallSpeedDipPerSecond = 900;
    private static readonly TimeSpan PoofDuration = TimeSpan.FromMilliseconds(400);

    private readonly PetWindow _window;
    private readonly PetLife _life;
    private readonly SpriteSheet _sheet;
    private readonly AnimationPlayer _player;
    private readonly IUserActivity _activity;
    private readonly SettingsStore _settings;
    private readonly PetStateStore _petStore;
    private readonly Func<bool> _chatBusy;
    private readonly DesktopScanner _desktop;
    private readonly Random _rng = new();
    private readonly Pendulum _pendulum = new();
    private readonly DispatcherTimer _lifeTimer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _animationTimer = new(DispatcherPriority.Render);
    private readonly DispatcherTimer _motionTimer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly DispatcherTimer _followTimer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly Stopwatch _time = Stopwatch.StartNew();
    private IReadOnlyList<ScreenArea> _screens = [];
    private IReadOnlyList<DesktopWindow> _windows = [];
    private Perch _perch = new(PerchKind.Floor, 0, 0);
    private TimeSpan _lastAnimationTick;
    private TimeSpan _lastMotionTick;
    private TimeSpan _lastDragTick;
    private Point _lastCursor;
    private double _cursorVelocityX;
    private double? _fallTargetY;
    private string? _overrideAnimation;
    private int _shownFrame = -1;
    private bool _suspended;
    private bool _held;
    private DateTime _hiddenUntil = DateTime.MinValue;
    private DateTime _nextTeleport;
    private long _ticks;

    public PetController(PetWindow window, PetLife life, SpriteSheet sheet, IUserActivity activity, SettingsStore settings, PetStateStore petStore, Func<bool> chatBusy)
    {
        _window = window;
        _life = life;
        _sheet = sheet;
        _activity = activity;
        _settings = settings;
        _petStore = petStore;
        _chatBusy = chatBusy;
        _desktop = new DesktopScanner(window);
        _player = new AnimationPlayer(sheet.Definition);
        _nextTeleport = DateTime.Now + NextTeleportDelay();

        _lifeTimer.Tick += (_, _) => Refresh();
        _animationTimer.Tick += (_, _) => OnAnimationTick();
        _motionTimer.Tick += (_, _) => OnMotionTick();
        _followTimer.Tick += (_, _) => OnFollowTick();
        _window.Clicked += OnClicked;
        _window.DoubleClicked += () => ChatRequested?.Invoke();
        _window.DragStarted += OnDragStarted;
        _window.DragMoved += OnDragMoved;
        _window.DragEnded += OnDragEnded;
        _window.ContextMenu = BuildMenu();
        _window.SetSize(Size);
    }

    public event Action? ChatRequested;
    public event Action? EggClicked;
    public event Action? SettingsRequested;
    public event Action? QuitRequested;
    public event Action<string>? LineReady;

    public PetLife Life => _life;

    public Window Window => _window;

    /// <summary>The sprite square on screen (DIPs).</summary>
    public Rect Bounds
    {
        get
        {
            var p = _window.SpritePosition;
            return new Rect(p.X, p.Y, Size, Size);
        }
    }

    /// <summary>Work area of the monitor the pet is on (DIPs).</summary>
    public Rect WorkArea
    {
        get
        {
            var b = Bounds;
            var wa = PerchPlanner.ScreenFor(b.X + Size / 2, b.Y + Size / 2, Screens()).WorkArea;
            return new Rect(wa.X, wa.Y, wa.Width, wa.Height);
        }
    }

    private double Size => _sheet.Definition.FrameSize * _settings.Current.PetScale;

    public void Start()
    {
        _window.Show();
        _desktop.IgnoreWindow(_window);
        _screens = _desktop.Screens();
        RestorePerch();
        Refresh();
        _lifeTimer.Start();
    }

    public void Refresh()
    {
        _ticks++;
        var activity = _activity.Sample();
        var tick = _life.Tick(activity);
        var hide = tick.Suspended || DateTime.Now < _hiddenUntil;
        if (hide != _suspended) SetSuspended(hide);
        if (_ticks % 60 == 0) _petStore.Save();
        if (hide) return;

        if (_ticks % 5 == 0)
        {
            _screens = _desktop.Screens();
            _window.Topmost = false;   // re-assert z-order; the taskbar sometimes covers topmost windows
            _window.Topmost = true;
            if (_perch.Kind == PerchKind.Floor && !_held && _fallTargetY is null)
                SetPerch(PerchPlanner.Fall(_perch.X, Size, _perch.Y, _screens));   // taskbar moved or resolution changed
        }
        Play(AnimationFor(tick.State));
        if (!_held) _window.SetFacing(tick.State == PetStateKind.Walk && _perch.CanWalk ? _life.WalkDirection : 1);
        if (tick.Line is { } line) LineReady?.Invoke(line);
        MaybeAutoTeleport(tick.State, activity);
    }

    public void ApplyScale()
    {
        _window.SetSize(Size);
        if (_window.IsVisible) SetPerch(PerchPlanner.Fall(_perch.X, Size, _perch.Y, Screens()));
    }

    public void HideFor(TimeSpan duration)
    {
        _hiddenUntil = DateTime.Now + duration;
        Refresh();
    }

    public void ToggleVisible()
    {
        _hiddenUntil = DateTime.Now < _hiddenUntil ? DateTime.MinValue : DateTime.MaxValue;
        Refresh();
    }

    /// <summary>Poof out, reappear at a random perch on a random monitor, poof in.</summary>
    public void Teleport()
    {
        if (_held || _overrideAnimation is not null || !_petStore.Current.Hatched || _suspended) return;
        _nextTeleport = DateTime.Now + NextTeleportDelay();
        _overrideAnimation = "poof";
        Play("poof", restart: true);
        After(PoofDuration, () =>
        {
            _screens = _desktop.Screens();
            _windows = _desktop.Windows();
            SetPerch(PerchPlanner.Random(_rng, Size, _screens, _windows));
            Play("poof", restart: true);
            After(PoofDuration, () =>
            {
                _overrideAnimation = null;
                Refresh();
            });
        });
    }

    public void Dispose()
    {
        _lifeTimer.Stop();
        _animationTimer.Stop();
        _motionTimer.Stop();
        _followTimer.Stop();
        _petStore.Save();
        _window.Close();
    }

    private IReadOnlyList<ScreenArea> Screens()
    {
        if (_screens.Count == 0) _screens = _desktop.Screens();
        return _screens;
    }

    private void RestorePerch()
    {
        var state = _petStore.Current;
        var screens = Screens();
        if (state.PositionX is not { } x)
        {
            var wa = screens[0].WorkArea;
            SetPerch(new Perch(PerchKind.Floor, wa.Right - Size - 160, wa.Bottom - Size));
            return;
        }
        var y = state.PositionY ?? 0;
        SetPerch(state.Perch is PerchKind.Floor or PerchKind.WindowTop
            ? PerchPlanner.Fall(x, Size, y, screens)
            : PerchPlanner.ForDrop(x, y, Size, screens, []));
    }

    private void SetPerch(Perch perch)
    {
        _perch = perch;
        _fallTargetY = null;
        _window.SpritePosition = new Point(perch.X, perch.Y);
        _petStore.Current.Perch = perch.Kind;
        _petStore.Current.PositionX = perch.X;
        _petStore.Current.PositionY = perch.Y;
        if (perch.Kind != PerchKind.Ceiling) _pendulum.Reset();
        ApplyPose();
        UpdateTimers();
    }

    private void ApplyPose()
    {
        if (_held)
        {
            _window.SetTilt(_pendulum.Angle, pivotTop: true);
            return;
        }
        switch (_perch.Kind)
        {
            case PerchKind.WallLeft: _window.SetTilt(-90, pivotTop: false); break;
            case PerchKind.WallRight: _window.SetTilt(90, pivotTop: false); break;
            case PerchKind.Ceiling: _window.SetTilt(_pendulum.Angle, pivotTop: true); break;
            default: _window.SetTilt(0, pivotTop: false); break;
        }
    }

    private void UpdateTimers()
    {
        var visible = !_suspended;
        var followNeeded = visible && !_held && _perch.Kind == PerchKind.WindowTop && _fallTargetY is null;
        if (followNeeded && !_followTimer.IsEnabled) _followTimer.Start();
        if (!followNeeded) _followTimer.Stop();
        var motionNeeded = visible && (_held || _fallTargetY is not null || _perch.Kind == PerchKind.Ceiling);
        if (motionNeeded && !_motionTimer.IsEnabled)
        {
            _lastMotionTick = _time.Elapsed;
            _motionTimer.Start();
        }
        if (!motionNeeded) _motionTimer.Stop();
    }

    private void SetSuspended(bool suspended)
    {
        _suspended = suspended;
        if (suspended)
        {
            _animationTimer.Stop();
            _window.Hide();
        }
        else
        {
            _window.Show();
            _window.SpritePosition = new Point(_perch.X, _perch.Y);
        }
        UpdateTimers();
    }

    private string AnimationFor(PetStateKind state)
    {
        if (_overrideAnimation is { } forced) return forced;
        if (state == PetStateKind.Egg) return "egg";
        if (_held || _perch.Kind is PerchKind.Ceiling or PerchKind.WallLeft or PerchKind.WallRight) return "hang";
        return state switch
        {
            PetStateKind.Walk => _perch.CanWalk ? "walk" : "idle",
            PetStateKind.Happy => "happy",
            PetStateKind.Bored => "bored",
            PetStateKind.Sleepy => "sleepy",
            PetStateKind.Sleep => "sleep",
            PetStateKind.Thinking => "thinking",
            PetStateKind.Talking => "talking",
            _ => "idle",
        };
    }

    private void Play(string animation, bool restart = false)
    {
        if (!restart && _player.Current == animation && _animationTimer.IsEnabled) return;
        _player.Play(animation, _time.Elapsed);
        _animationTimer.Interval = _player.FrameInterval;
        _shownFrame = -1;
        _lastAnimationTick = _time.Elapsed;
        _animationTimer.Start();
        OnAnimationTick();
    }

    private void OnAnimationTick()
    {
        var now = _time.Elapsed;
        var frame = _player.FrameAt(now);
        if (frame != _shownFrame)
        {
            _shownFrame = frame;
            _window.SetFrame(_sheet.Frame(frame));
        }
        if (_life.State == PetStateKind.Walk && _perch.CanWalk && !_held && _fallTargetY is null && _overrideAnimation is null)
            Walk(now - _lastAnimationTick);
        _lastAnimationTick = now;
    }

    private void Walk(TimeSpan elapsed)
    {
        var (min, max) = PerchPlanner.WalkRange(_perch, Size, Screens(), _windows);
        var x = _perch.X + _life.WalkDirection * WalkSpeedDipPerSecond * elapsed.TotalSeconds;
        if (x <= min || x >= max)
        {
            x = Math.Clamp(x, min, Math.Max(min, max));
            _life.ReverseWalk();
            _window.SetFacing(_life.WalkDirection);
        }
        var offset = _perch.Kind == PerchKind.WindowTop ? _perch.OffsetX + (x - _perch.X) : 0;
        _perch = _perch with { X = x, OffsetX = offset };
        _window.SpritePosition = new Point(_perch.X, _perch.Y);
        _petStore.Current.PositionX = x;
    }

    private void OnFollowTick()
    {
        if (_held || _perch.Kind != PerchKind.WindowTop)
        {
            _followTimer.Stop();
            return;
        }
        _windows = _desktop.Windows();
        var next = PerchPlanner.Follow(_perch, Size, Screens(), _windows);
        if (next is null)
        {
            StartFall();
            return;
        }
        if (next == _perch) return;
        _perch = next;
        _window.SpritePosition = new Point(next.X, next.Y);
        _petStore.Current.PositionX = next.X;
        _petStore.Current.PositionY = next.Y;
    }

    private void StartFall()
    {
        var landing = PerchPlanner.Fall(_perch.X, Size, _perch.Y, Screens());
        _perch = landing with { Y = Math.Min(_perch.Y, landing.Y) };
        _fallTargetY = landing.Y;
        ApplyPose();
        UpdateTimers();
    }

    private void OnMotionTick()
    {
        var now = _time.Elapsed;
        var dt = (now - _lastMotionTick).TotalSeconds;
        _lastMotionTick = now;

        if (_held)
        {
            _cursorVelocityX *= Math.Pow(0.02, dt);   // the swing dies down when the mouse stops
            _pendulum.Step(dt, _cursorVelocityX);
            _window.SetTilt(_pendulum.Angle, pivotTop: true);
            return;
        }
        if (_fallTargetY is { } target)
        {
            var y = Math.Min(target, _perch.Y + FallSpeedDipPerSecond * dt);
            _perch = _perch with { Y = y };
            _window.SpritePosition = new Point(_perch.X, y);
            if (y >= target) SetPerch(_perch);
            return;
        }
        if (_perch.Kind == PerchKind.Ceiling)
        {
            _pendulum.Step(dt, 0, Pendulum.IdleSwing(now.TotalSeconds));
            _window.SetTilt(_pendulum.Angle, pivotTop: true);
            return;
        }
        _motionTimer.Stop();
    }

    private void OnClicked()
    {
        if (!_petStore.Current.Hatched)
        {
            EggClicked?.Invoke();
            return;
        }
        _life.OnClick();
        Refresh();
    }

    private void OnDragStarted(Point cursor)
    {
        _held = true;
        _fallTargetY = null;
        _pendulum.Reset();
        _lastCursor = cursor;
        _lastDragTick = _time.Elapsed;
        _cursorVelocityX = 0;
        MoveHeld(cursor);
        _window.SetFacing(1);
        Play(AnimationFor(_life.State), restart: true);
        ApplyPose();
        UpdateTimers();
    }

    private void OnDragMoved(Point cursor)
    {
        var now = _time.Elapsed;
        var dt = Math.Max(0.001, (now - _lastDragTick).TotalSeconds);
        _lastDragTick = now;
        _cursorVelocityX = _cursorVelocityX * 0.6 + (cursor.X - _lastCursor.X) / dt * 0.4;
        _lastCursor = cursor;
        MoveHeld(cursor);
    }

    /// <summary>While held, the pet hangs from the cursor by its hands (top-centre of the sprite).</summary>
    private void MoveHeld(Point cursor) => _window.SpritePosition = new Point(cursor.X - Size / 2, cursor.Y - Size * 0.1);

    private void OnDragEnded(Point cursor)
    {
        _held = false;
        _pendulum.Reset();
        var p = _window.SpritePosition;
        _screens = _desktop.Screens();
        _windows = _desktop.Windows();
        SetPerch(PerchPlanner.ForDrop(p.X, p.Y, Size, _screens, _windows));
        Refresh();
    }

    private void MaybeAutoTeleport(PetStateKind state, ActivitySnapshot activity)
    {
        if (!_settings.Current.AutoTeleport || DateTime.Now < _nextTeleport) return;
        if (_held || _chatBusy() || activity.UserIdle >= TimeSpan.FromMinutes(1)) return;
        if (state is not (PetStateKind.Idle or PetStateKind.Bored or PetStateKind.Walk)) return;
        Teleport();
    }

    private TimeSpan NextTeleportDelay() => TimeSpan.FromMinutes(5 + _rng.NextDouble() * 10);

    private static void After(TimeSpan delay, Action action)
    {
        var timer = new DispatcherTimer { Interval = delay };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            action();
        };
        timer.Start();
    }

    private ContextMenu BuildMenu()
    {
        var menu = new ContextMenu();
        menu.Items.Add(Item("Chat", () => ChatRequested?.Invoke()));
        menu.Items.Add(Item("Teleport", Teleport));
        menu.Items.Add(Item("Sleep", () => { _life.ForceSleep(); Refresh(); }));
        menu.Items.Add(Item("Hide for 1 hour", () => HideFor(TimeSpan.FromHours(1))));
        menu.Items.Add(Item("Settings", () => SettingsRequested?.Invoke()));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Quit", () => QuitRequested?.Invoke()));
        return menu;
    }

    private static MenuItem Item(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }
}
```

- [ ] **Step 6: AppHost + Settings** — construct the controller with `() => _chat.IsOpen || _brain?.Busy == true` as the last argument (the lambda runs after `_chat` exists); add a tray menu item **Teleport** wired to `_pet.Teleport` in `TrayIcon` (new `event Action? TeleportClicked`, item placed after "Chat"); in SettingsWindow add a "Teleport on its own" CheckBox (`AutoTeleportBox`) bound to `settings.AutoTeleport` on load/save.

- [ ] **Step 7: Build, full tests, launch-smoke.** `dotnet build Companion.sln` (0 warnings), `dotnet test src/Companion.Tests`, then start the app, wait ~10 s, confirm alive and no ERROR in the newest app log, Stop-Process. Report the manual checks left for the user: drag and dangle/swing, drop on each edge and on a window top, window follow and fall, walls/ceiling look, teleport from the menu and on its own, second monitor, bubble placement near ceiling.
- [ ] **Step 8: Commit** `feat(app): perches, dangling drag and teleport across monitors`.

---

### Task 8: Publish, footprint, checklist

**Files:** Modify `docs/manual-checklist.md`.

- [ ] **Step 1:** Append to `docs/manual-checklist.md`:

```markdown
## v1.1 — Subscription brain
- [ ] Settings → Brain = Subscription (default); a question answers without any API key; tooltip says "Claude subscription".
- [ ] "Remember that …" then Settings shows the fact; "forget …" removes it (via the MCP memory server).
- [ ] Claude Code logged out → the bubble says to run `claude` and log in. Plan limit → message with reset time.
- [ ] Brain = ApiKey still works with a key and the cap applies.

## v1.1 — Movement
- [ ] Drag: the pet dangles from the cursor and swings with motion; stops swinging when the mouse stops.
- [ ] Drop near floor / left wall / right wall / ceiling / a window's top edge snaps there; elsewhere it stays put.
- [ ] Walls: clings with arms toward the wall. Ceiling: hangs and swings gently.
- [ ] On a window: moves with the window; minimise/close the window → it falls to the floor.
- [ ] Right-click → Teleport: poof, reappears elsewhere (also on the second monitor); happens on its own every 5–15 min when idle (toggle in Settings).
- [ ] Chat bubble opens below the pet when it hangs from the ceiling; stays on the pet's monitor.
- [ ] Footprint still PASS when idle on the floor; CPU stays low while hanging from the ceiling.
```

- [ ] **Step 2:** Quit any running Companion, `powershell -ExecutionPolicy Bypass -File tools/publish.ps1`; confirm `app\Companion.exe` and `app\Companion.Memory.exe` exist.
- [ ] **Step 3:** Start `app\Companion.exe`, idle 60 s, run `tools/measure-footprint.ps1` (2 min), stop it. Record the numbers (target PASS).
- [ ] **Step 4: Commit** `chore: v1.1 checklist and publish`.
