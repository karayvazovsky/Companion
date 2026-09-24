# Desktop Companion v1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a tiny pixel-art desktop pet for Windows that lives on the taskbar, behaves like a low-maintenance tamagotchi, and answers questions through Claude Opus 5.5 with streaming, web search and long-term memory.

**Architecture:** A native C# / .NET 10 WPF app (`src/Companion`) hosts the pet window, chat bubble, tray icon and Win32 glue. All logic that can be tested without a screen lives in `src/Companion.Core`: pet state machine and meters, skin parsing, settings/secrets/memory stores, spend tracking, conversation handling, the `Brain` orchestrator and the Anthropic SDK client. Only questions go to the network.

**Tech Stack:** .NET 10 SDK (10.0.401), WPF + a WinForms `NotifyIcon`, `Anthropic` NuGet 12.50.0 (beta Messages streaming), `System.Security.Cryptography.ProtectedData` (DPAPI), xUnit.

**Spec:** `docs/superpowers/specs/2026-09-24-desktop-companion-design.md`

## Global Constraints

- Repository root and app root: `D:\Claude\Companion`. Run every command from there unless stated otherwise.
- All projects target `net10.0-windows` (set once in `Directory.Build.props`).
- `Anthropic` package pinned to `12.50.0` (the C# code in Task 12 was compile-verified against it).
- Model id is exactly `claude-opus-5-5`. Everyday effort `low`, think-harder effort `high`. Never send `thinking: disabled`, `budget_tokens`, or forced `tool_choice` (`any`/`tool`) — all return 400 on this model.
- Every request uses beta `thinking-binding-controls-2026-08-01` with `prefix_mismatch_behavior = drop_block`. Completed exchanges are replayed as plain text only.
- All runtime data lives under `<root>\data` (`settings.json`, `secrets.bin`, `memory.json`, `pet.json`, `logs\`). Nothing in AppData. `data\`, `app\`, `workspace\` are git-ignored.
- API key stored only DPAPI-encrypted (CurrentUser) in `data\secrets.bin`. Never log it.
- Defaults: hotkey `Ctrl+Alt+Space`; monthly cap `$30`; pet scale `4` (allowed 3/4/6); autostart off.
- Default skin: 16×16 frames, 4 columns × 8 rows, `skins\default\sheet.png` + `sheet.json`.
- Footprint targets: idle working set < 60 MB, idle CPU < 1 % (all cores). No network unless the user asks a question or a key is validated.
- Unit tests never call the real API. Only `tools/Smoke` does, and only when run by hand.
- Commit at the end of every task, message body ending with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## File Map

```
D:\Claude\Companion\
├─ Companion.sln
├─ Directory.Build.props                 shared TFM / nullable / usings
├─ .gitignore
├─ skins\default\sheet.png, sheet.json   placeholder creature (Task 15)
├─ tools\SkinGen\                        renders sheet.png from code (Task 15)
├─ tools\Smoke\                          real-API smoke test + --save-key (Task 12)
├─ tools\publish.ps1, measure-footprint.ps1 (Task 20)
├─ docs\manual-checklist.md              (Task 20)
└─ src\
   ├─ Companion.Core\
   │  ├─ AppPaths.cs            root/data folder resolution
   │  ├─ Clock.cs               IClock, SystemClock
   │  ├─ Log.cs                 ILog, NullLog, FileLog
   │  ├─ JsonFile.cs            atomic JSON load/save + corrupt-file recovery
   │  ├─ Settings.cs            EffortLevel, AppSettings, SettingsStore
   │  ├─ Secrets.cs             ISecretProtector, DpapiProtector, SecretStore
   │  ├─ Memory.cs              Fact, MemoryStore
   │  ├─ PetState.cs            PetState, PetStateStore
   │  ├─ Skin.cs                AnimationDef, SkinDefinition, AnimationPlayer
   │  ├─ PetMeters.cs           attention / energy
   │  ├─ PetLife.cs             PetStateKind, ActivitySnapshot, IUserActivity, PetTick, PetLife, PetLines
   │  ├─ Spend.cs               UsageTotals, Pricing, SpendTracker
   │  ├─ Conversation.cs        Exchange, Conversation
   │  ├─ SystemPrompt.cs        personality prompt builder
   │  ├─ ChatLog.cs             local jsonl chat log
   │  ├─ Tools.cs               ToolSpec, ToolOutcome, ITool, ToolRegistry, RememberTool, ForgetTool
   │  ├─ ClaudeContracts.cs     IClaudeClient + request/response records + ClaudeException
   │  ├─ Brain.cs               IBrainListener, BrainReply, Brain
   │  ├─ AnthropicClaudeClient.cs  SDK implementation of IClaudeClient
   │  ├─ MarkdownLite.cs        tiny markdown parser for the bubble
   │  └─ Onboarding.cs          first-run flow
   ├─ Companion\  (WPF, AssemblyName Companion → Companion.exe)
   │  ├─ App.xaml(.cs)          startup, single instance, crash logging
   │  ├─ AppHost.cs             composition root
   │  ├─ NativeMethods.cs       P/Invoke + WindowStyles + MemoryTrim
   │  ├─ UserActivity.cs        idle / fullscreen / lock detection
   │  ├─ HotkeyService.cs       global hotkey
   │  ├─ Autostart.cs           HKCU Run entry
   │  ├─ SpriteSheet.cs         loads sheet.png into frames
   │  ├─ PetWindow.xaml(.cs)    transparent sprite window
   │  ├─ PetController.cs       drives PetLife → window, walking, menu
   │  ├─ LineBubble.xaml(.cs)   4-second mini speech bubble
   │  ├─ TrayIcon.cs            NotifyIcon + menu
   │  ├─ ChatBubble.xaml(.cs)   chat window
   │  ├─ MarkdownView.cs        renders MarkdownLite blocks to WPF
   │  ├─ ChatController.cs      bubble ↔ Brain ↔ pet
   │  └─ SettingsWindow.xaml(.cs)
   └─ Companion.Tests\  (xUnit, references Core and the app)
```

---

### Task 1: Solution scaffold and app root resolution

**Files:**
- Create: `Companion.sln`, `Directory.Build.props`, `.gitignore`, `skins/default/sheet.json`
- Create: `src/Companion.Core/Companion.Core.csproj`, `src/Companion.Core/AppPaths.cs`
- Create: `src/Companion/Companion.csproj` (WPF template, csproj replaced)
- Create: `src/Companion.Tests/Companion.Tests.csproj`, `src/Companion.Tests/TestSupport.cs`, `src/Companion.Tests/AppPathsTests.cs`

**Interfaces:**
- Produces: `AppPaths(string Root)` with `Data`, `Logs`, `Skins`, `Workspace`, `SettingsFile`, `SecretsFile`, `MemoryFile`, `PetFile`; `static AppPaths Resolve(string startDir, string? overrideRoot = null)`; `void EnsureCreated()`. Test helper `TempDir` with `Root` and `PathOf(string)`.

- [ ] **Step 1: Create the projects**

```powershell
cd D:\Claude\Companion
dotnet new sln -n Companion --format sln
dotnet new classlib -n Companion.Core -o src/Companion.Core
dotnet new wpf -n Companion -o src/Companion
dotnet new xunit -n Companion.Tests -o src/Companion.Tests
Remove-Item src/Companion.Core/Class1.cs, src/Companion.Tests/UnitTest1.cs
dotnet sln add src/Companion.Core src/Companion src/Companion.Tests
```

- [ ] **Step 2: Shared build settings**

Create `Directory.Build.props`:

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>
</Project>
```

In `src/Companion.Tests/Companion.Tests.csproj` delete the `<TargetFramework>…</TargetFramework>` line (the props file supplies it). Keep everything else the template generated, and add inside the first `<PropertyGroup>`:

```xml
    <UseWPF>true</UseWPF>
```

and add a new item group:

```xml
  <ItemGroup>
    <ProjectReference Include="..\Companion.Core\Companion.Core.csproj" />
  </ItemGroup>
```

Replace `src/Companion.Core/Companion.Core.csproj` with:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <InternalsVisibleTo Include="Companion.Tests" />
  </ItemGroup>
</Project>
```

Then add packages:

```powershell
dotnet add src/Companion.Core package Anthropic --version 12.50.0
dotnet add src/Companion.Core package System.Security.Cryptography.ProtectedData
```

Replace `src/Companion/Companion.csproj` with:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <UseWPF>true</UseWPF>
    <UseWindowsForms>true</UseWindowsForms>
    <AssemblyName>Companion</AssemblyName>
    <RootNamespace>Companion</RootNamespace>
    <ServerGarbageCollection>false</ServerGarbageCollection>
    <ConcurrentGarbageCollection>false</ConcurrentGarbageCollection>
  </PropertyGroup>
  <ItemGroup>
    <Using Remove="System.Windows.Forms" />
    <Using Remove="System.Drawing" />
  </ItemGroup>
  <ItemGroup>
    <RuntimeHostConfigurationOption Include="System.GC.ConserveMemory" Value="5" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\Companion.Core\Companion.Core.csproj" />
  </ItemGroup>
</Project>
```

Create `.gitignore`:

```gitignore
bin/
obj/
.vs/
*.user
/app/
/data/
/workspace/
```

Create `skins/default/sheet.json` now (the `skins` folder is how the app finds its root; the PNG arrives in Task 15):

```json
{
  "frameSize": 16,
  "animations": {
    "idle":     { "fps": 3, "loop": true,  "frames": [0, 1, 0, 1, 0, 2] },
    "walk":     { "fps": 8, "loop": true,  "frames": [4, 5, 6, 7] },
    "happy":    { "fps": 8, "loop": false, "frames": [8, 9, 10, 9, 8, 9, 10, 9] },
    "bored":    { "fps": 2, "loop": true,  "frames": [12, 12, 13, 13] },
    "sleepy":   { "fps": 2, "loop": true,  "frames": [16, 16, 17] },
    "sleep":    { "fps": 2, "loop": true,  "frames": [20, 21] },
    "thinking": { "fps": 4, "loop": true,  "frames": [24, 25, 26] },
    "talking":  { "fps": 8, "loop": true,  "frames": [28, 29] },
    "egg":      { "fps": 2, "loop": true,  "frames": [30, 30, 31, 30] }
  }
}
```

- [ ] **Step 3: Write the failing test**

Create `src/Companion.Tests/TestSupport.cs`:

```csharp
namespace Companion.Tests;

public sealed class TempDir : IDisposable
{
    public TempDir()
    {
        Root = Path.Combine(Path.GetTempPath(), "companion-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public string PathOf(string relative) => Path.Combine(Root, relative);

    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
```

Create `src/Companion.Tests/AppPathsTests.cs`:

```csharp
using Companion.Core;
using Xunit;

namespace Companion.Tests;

public class AppPathsTests
{
    [Fact]
    public void Resolve_walks_up_to_the_folder_that_contains_skins()
    {
        using var tmp = new TempDir();
        Directory.CreateDirectory(tmp.PathOf("skins"));
        var start = tmp.PathOf(Path.Combine("app", "bin"));
        Directory.CreateDirectory(start);

        var paths = AppPaths.Resolve(start);

        Assert.Equal(Path.GetFullPath(tmp.Root), paths.Root);
        Assert.Equal(Path.Combine(paths.Root, "data"), paths.Data);
        Assert.Equal(Path.Combine(paths.Root, "data", "logs"), paths.Logs);
        Assert.Equal(Path.Combine(paths.Root, "data", "secrets.bin"), paths.SecretsFile);
    }

    [Fact]
    public void Resolve_prefers_the_override_root()
    {
        using var tmp = new TempDir();
        var paths = AppPaths.Resolve(@"C:\somewhere\else", tmp.Root);
        Assert.Equal(Path.GetFullPath(tmp.Root), paths.Root);
    }

    [Fact]
    public void EnsureCreated_creates_data_logs_and_workspace()
    {
        using var tmp = new TempDir();
        var paths = new AppPaths(tmp.Root);

        paths.EnsureCreated();

        Assert.True(Directory.Exists(paths.Data));
        Assert.True(Directory.Exists(paths.Logs));
        Assert.True(Directory.Exists(paths.Workspace));
    }
}
```

- [ ] **Step 4: Run the test to verify it fails**

Run: `dotnet test src/Companion.Tests --filter "FullyQualifiedName~AppPathsTests"`
Expected: build FAILS with `CS0246: The type or namespace name 'AppPaths' could not be found`.

- [ ] **Step 5: Implement `AppPaths`**

Create `src/Companion.Core/AppPaths.cs`:

```csharp
namespace Companion.Core;

/// <summary>Every folder and file the app uses, relative to one portable root.</summary>
public sealed record AppPaths(string Root)
{
    public string Data => Path.Combine(Root, "data");
    public string Logs => Path.Combine(Data, "logs");
    public string Skins => Path.Combine(Root, "skins");
    public string Workspace => Path.Combine(Root, "workspace");
    public string SettingsFile => Path.Combine(Data, "settings.json");
    public string SecretsFile => Path.Combine(Data, "secrets.bin");
    public string MemoryFile => Path.Combine(Data, "memory.json");
    public string PetFile => Path.Combine(Data, "pet.json");

    /// <summary>
    /// The root is the first folder at or above <paramref name="startDir"/> that has a "skins" folder.
    /// This works both for the published app (root\app\Companion.exe) and for dev builds (root\src\...\bin\...).
    /// </summary>
    public static AppPaths Resolve(string startDir, string? overrideRoot = null)
    {
        if (!string.IsNullOrWhiteSpace(overrideRoot)) return new AppPaths(Path.GetFullPath(overrideRoot));
        for (var dir = new DirectoryInfo(Path.GetFullPath(startDir)); dir is not null; dir = dir.Parent)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "skins"))) return new AppPaths(dir.FullName);
        }
        return new AppPaths(Path.GetFullPath(startDir));
    }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(Data);
        Directory.CreateDirectory(Logs);
        Directory.CreateDirectory(Workspace);
    }
}
```

- [ ] **Step 6: Run the tests to verify they pass, and build everything**

Run: `dotnet test src/Companion.Tests --filter "FullyQualifiedName~AppPathsTests"`
Expected: 3 passed.
Run: `dotnet build Companion.sln`
Expected: `Build succeeded.` (the WPF template still has its `MainWindow`; it is replaced in Task 17).

- [ ] **Step 7: Commit**

```powershell
git add -A
git commit -m "chore: scaffold solution and resolve app root folder" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: Clock, logging and atomic JSON files

**Files:**
- Create: `src/Companion.Core/Clock.cs`, `src/Companion.Core/Log.cs`, `src/Companion.Core/JsonFile.cs`
- Modify: `src/Companion.Tests/TestSupport.cs` (add `FakeClock`)
- Test: `src/Companion.Tests/InfrastructureTests.cs`

**Interfaces:**
- Produces: `IClock { DateTimeOffset Now }`, `SystemClock`; `ILog { void Info(string); void Error(string, Exception? = null) }`, `NullLog.Instance`, `FileLog(string dir, IClock clock, int keepDays = 7)`; `JsonFile.Options`, `JsonFile.Load<T>(string path, Func<T> fallback, ILog log, IClock clock)`, `JsonFile.Save<T>(string path, T value)`. Test helper `FakeClock(DateTimeOffset start)` with settable `Now` and `Advance(TimeSpan)`.

- [ ] **Step 1: Add the fake clock to test support**

Append to `src/Companion.Tests/TestSupport.cs`:

```csharp
public sealed class FakeClock(DateTimeOffset start) : Companion.Core.IClock
{
    public DateTimeOffset Now { get; set; } = start;

    public void Advance(TimeSpan by) => Now += by;

    public static FakeClock At(int year, int month, int day, int hour, int minute = 0) =>
        new(new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.Zero));
}
```

- [ ] **Step 2: Write the failing tests**

Create `src/Companion.Tests/InfrastructureTests.cs`:

```csharp
using Companion.Core;
using Xunit;

namespace Companion.Tests;

public class JsonFileTests
{
    private sealed class Sample
    {
        public string Name { get; set; } = "default";
        public int Count { get; set; }
    }

    [Fact]
    public void Load_returns_fallback_when_file_is_missing()
    {
        using var tmp = new TempDir();
        var value = JsonFile.Load(tmp.PathOf("missing.json"), () => new Sample(), NullLog.Instance, FakeClock.At(2026, 9, 24, 12));
        Assert.Equal("default", value.Name);
    }

    [Fact]
    public void Save_then_Load_round_trips_and_leaves_no_temp_file()
    {
        using var tmp = new TempDir();
        var path = tmp.PathOf("sample.json");

        JsonFile.Save(path, new Sample { Name = "pip", Count = 3 });
        var loaded = JsonFile.Load(path, () => new Sample(), NullLog.Instance, FakeClock.At(2026, 9, 24, 12));

        Assert.Equal("pip", loaded.Name);
        Assert.Equal(3, loaded.Count);
        Assert.False(File.Exists(path + ".tmp"));
        Assert.Contains("\"name\"", File.ReadAllText(path));
    }

    [Fact]
    public void Load_moves_a_corrupt_file_aside_and_returns_fallback()
    {
        using var tmp = new TempDir();
        var path = tmp.PathOf("broken.json");
        File.WriteAllText(path, "{ not json");

        var value = JsonFile.Load(path, () => new Sample(), NullLog.Instance, FakeClock.At(2026, 9, 24, 12));

        Assert.Equal("default", value.Name);
        Assert.False(File.Exists(path));
        Assert.True(File.Exists(path + ".bad-20260924120000"));
    }
}

public class FileLogTests
{
    [Fact]
    public void Writes_daily_file_and_deletes_files_older_than_keep_days()
    {
        using var tmp = new TempDir();
        File.WriteAllText(tmp.PathOf("app-2026-09-10.log"), "old");
        File.WriteAllText(tmp.PathOf("app-2026-09-20.log"), "recent");
        var clock = FakeClock.At(2026, 9, 24, 8);

        var log = new FileLog(tmp.Root, clock);
        log.Info("hello");
        log.Error("boom", new InvalidOperationException("bad"));

        Assert.False(File.Exists(tmp.PathOf("app-2026-09-10.log")));
        Assert.True(File.Exists(tmp.PathOf("app-2026-09-20.log")));
        var today = File.ReadAllText(tmp.PathOf("app-2026-09-24.log"));
        Assert.Contains("INFO hello", today);
        Assert.Contains("ERROR boom", today);
        Assert.Contains("InvalidOperationException", today);
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test src/Companion.Tests --filter "FullyQualifiedName~JsonFileTests|FullyQualifiedName~FileLogTests"`
Expected: build FAILS — `IClock`, `JsonFile`, `NullLog`, `FileLog` not found.

- [ ] **Step 4: Implement**

Create `src/Companion.Core/Clock.cs`:

```csharp
namespace Companion.Core;

public interface IClock
{
    DateTimeOffset Now { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset Now => DateTimeOffset.Now;
}
```

Create `src/Companion.Core/Log.cs`:

```csharp
using System.Globalization;

namespace Companion.Core;

public interface ILog
{
    void Info(string message);
    void Error(string message, Exception? exception = null);
}

public sealed class NullLog : ILog
{
    public static readonly NullLog Instance = new();
    public void Info(string message) { }
    public void Error(string message, Exception? exception = null) { }
}

/// <summary>Writes data\logs\app-YYYY-MM-DD.log and keeps the last <c>keepDays</c> days.</summary>
public sealed class FileLog : ILog
{
    private readonly string _directory;
    private readonly IClock _clock;
    private readonly object _gate = new();

    public FileLog(string directory, IClock clock, int keepDays = 7)
    {
        _directory = directory;
        _clock = clock;
        Directory.CreateDirectory(directory);

        var cutoff = clock.Now.Date.AddDays(-keepDays);
        foreach (var file in Directory.GetFiles(directory, "app-*.log"))
        {
            var stamp = Path.GetFileNameWithoutExtension(file)[4..];
            if (DateTime.TryParseExact(stamp, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) && day < cutoff)
            {
                File.Delete(file);
            }
        }
    }

    public void Info(string message) => Write("INFO", message);

    public void Error(string message, Exception? exception = null) =>
        Write("ERROR", exception is null ? message : $"{message}{Environment.NewLine}{exception}");

    private void Write(string level, string message)
    {
        var now = _clock.Now;
        var path = Path.Combine(_directory, $"app-{now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.log");
        var line = $"{now.ToString("HH:mm:ss", CultureInfo.InvariantCulture)} {level} {message}{Environment.NewLine}";
        lock (_gate)
        {
            try { File.AppendAllText(path, line); } catch (IOException) { }
        }
    }
}
```

Create `src/Companion.Core/JsonFile.cs`:

```csharp
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Companion.Core;

public static class JsonFile
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>Loads a JSON file. Missing → fallback. Corrupt → renamed to *.bad-yyyyMMddHHmmss, logged, fallback.</summary>
    public static T Load<T>(string path, Func<T> fallback, ILog log, IClock clock)
    {
        if (!File.Exists(path)) return fallback();
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options)
                ?? throw new JsonException("Document was null.");
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            var bad = $"{path}.bad-{clock.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture)}";
            File.Move(path, bad, overwrite: true);
            log.Error($"Corrupt file {path}; moved to {bad}", ex);
            return fallback();
        }
    }

    /// <summary>Writes to a temp file then renames, so a crash never leaves a half-written file.</summary>
    public static void Save<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(value, Options));
        File.Move(temp, path, overwrite: true);
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test src/Companion.Tests --filter "FullyQualifiedName~JsonFileTests|FullyQualifiedName~FileLogTests"`
Expected: 4 passed.

- [ ] **Step 6: Commit**

```powershell
git add -A
git commit -m "feat(core): clock, file log and atomic JSON storage" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: Settings and encrypted API key

**Files:**
- Create: `src/Companion.Core/Settings.cs`, `src/Companion.Core/Secrets.cs`
- Test: `src/Companion.Tests/SettingsTests.cs`

**Interfaces:**
- Consumes: `JsonFile`, `IClock`, `ILog` (Task 2).
- Produces: `enum EffortLevel { Low, Medium, High }`; `AppSettings` (props `Hotkey`, `EverydayEffort`, `ThinkHarderEffort`, `MonthlyCapUsd`, `PetScale`, `StartWithWindows`, `SpendByMonth`, `CapWarnedMonth`); `SettingsStore(string path, IClock clock, ILog log)` with `Current`, `Save()`, `event Action? Changed`; `ISecretProtector`, `DpapiProtector`; `SecretStore(string path, ISecretProtector protector, ILog log)` with `HasKey`, `LoadApiKey()`, `SaveApiKey(string)`, `Clear()`.

- [ ] **Step 1: Write the failing tests**

Create `src/Companion.Tests/SettingsTests.cs`:

```csharp
using Companion.Core;
using Xunit;

namespace Companion.Tests;

public class SettingsStoreTests
{
    [Fact]
    public void New_store_has_spec_defaults()
    {
        using var tmp = new TempDir();
        var store = new SettingsStore(tmp.PathOf("settings.json"), FakeClock.At(2026, 9, 24, 12), NullLog.Instance);

        Assert.Equal("Ctrl+Alt+Space", store.Current.Hotkey);
        Assert.Equal(EffortLevel.Low, store.Current.EverydayEffort);
        Assert.Equal(EffortLevel.High, store.Current.ThinkHarderEffort);
        Assert.Equal(30m, store.Current.MonthlyCapUsd);
        Assert.Equal(4, store.Current.PetScale);
        Assert.False(store.Current.StartWithWindows);
    }

    [Fact]
    public void Save_persists_and_writes_enums_as_text()
    {
        using var tmp = new TempDir();
        var path = tmp.PathOf("settings.json");
        var store = new SettingsStore(path, FakeClock.At(2026, 9, 24, 12), NullLog.Instance);
        var changed = 0;
        store.Changed += () => changed++;

        store.Current.Hotkey = "Ctrl+Shift+K";
        store.Current.EverydayEffort = EffortLevel.Medium;
        store.Current.SpendByMonth["2026-09"] = 1.25m;
        store.Save();

        var reloaded = new SettingsStore(path, FakeClock.At(2026, 9, 24, 12), NullLog.Instance);
        Assert.Equal("Ctrl+Shift+K", reloaded.Current.Hotkey);
        Assert.Equal(EffortLevel.Medium, reloaded.Current.EverydayEffort);
        Assert.Equal(1.25m, reloaded.Current.SpendByMonth["2026-09"]);
        Assert.Contains("\"medium\"", File.ReadAllText(path));
        Assert.Equal(1, changed);
    }

    [Fact]
    public void Invalid_pet_scale_is_normalized_to_4()
    {
        using var tmp = new TempDir();
        var path = tmp.PathOf("settings.json");
        File.WriteAllText(path, """{ "petScale": 5 }""");

        var store = new SettingsStore(path, FakeClock.At(2026, 9, 24, 12), NullLog.Instance);

        Assert.Equal(4, store.Current.PetScale);
    }
}

public class SecretStoreTests
{
    [Fact]
    public void Api_key_round_trips_and_is_not_stored_in_plain_text()
    {
        using var tmp = new TempDir();
        var path = tmp.PathOf("secrets.bin");
        var store = new SecretStore(path, new DpapiProtector(), NullLog.Instance);
        Assert.False(store.HasKey);

        store.SaveApiKey("  sk-ant-test-123  ");

        Assert.True(store.HasKey);
        Assert.Equal("sk-ant-test-123", new SecretStore(path, new DpapiProtector(), NullLog.Instance).LoadApiKey());
        Assert.DoesNotContain("sk-ant-test-123", System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(path)));
    }

    [Fact]
    public void Garbage_file_loads_as_null()
    {
        using var tmp = new TempDir();
        var path = tmp.PathOf("secrets.bin");
        File.WriteAllBytes(path, [1, 2, 3, 4]);

        Assert.Null(new SecretStore(path, new DpapiProtector(), NullLog.Instance).LoadApiKey());
    }

    [Fact]
    public void Clear_removes_the_key()
    {
        using var tmp = new TempDir();
        var store = new SecretStore(tmp.PathOf("secrets.bin"), new DpapiProtector(), NullLog.Instance);
        store.SaveApiKey("sk-ant-x");

        store.Clear();

        Assert.False(store.HasKey);
        Assert.Null(store.LoadApiKey());
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test src/Companion.Tests --filter "FullyQualifiedName~SettingsStoreTests|FullyQualifiedName~SecretStoreTests"`
Expected: build FAILS — `SettingsStore`, `SecretStore`, `DpapiProtector`, `EffortLevel` not found.

- [ ] **Step 3: Implement**

Create `src/Companion.Core/Settings.cs`:

```csharp
namespace Companion.Core;

public enum EffortLevel { Low, Medium, High }

public sealed class AppSettings
{
    public string Hotkey { get; set; } = "Ctrl+Alt+Space";
    public EffortLevel EverydayEffort { get; set; } = EffortLevel.Low;
    public EffortLevel ThinkHarderEffort { get; set; } = EffortLevel.High;
    public decimal MonthlyCapUsd { get; set; } = 30m;
    public int PetScale { get; set; } = 4;
    public bool StartWithWindows { get; set; }

    /// <summary>Estimated spend per calendar month, keyed "yyyy-MM".</summary>
    public Dictionary<string, decimal> SpendByMonth { get; set; } = new();

    /// <summary>Month key for which the 80 % warning was already shown.</summary>
    public string? CapWarnedMonth { get; set; }
}

public sealed class SettingsStore
{
    private readonly string _path;

    public SettingsStore(string path, IClock clock, ILog log)
    {
        _path = path;
        Current = JsonFile.Load(path, () => new AppSettings(), log, clock);
        Normalize();
    }

    public AppSettings Current { get; }

    public event Action? Changed;

    public void Save()
    {
        Normalize();
        JsonFile.Save(_path, Current);
        Changed?.Invoke();
    }

    private void Normalize()
    {
        if (Current.PetScale is not (3 or 4 or 6)) Current.PetScale = 4;
        if (Current.MonthlyCapUsd < 0) Current.MonthlyCapUsd = 0;
        if (string.IsNullOrWhiteSpace(Current.Hotkey)) Current.Hotkey = "Ctrl+Alt+Space";
        Current.SpendByMonth ??= new();
    }
}
```

Create `src/Companion.Core/Secrets.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;

namespace Companion.Core;

public interface ISecretProtector
{
    byte[] Protect(byte[] data);
    byte[] Unprotect(byte[] data);
}

/// <summary>Windows DPAPI, CurrentUser scope: only this Windows account can decrypt.</summary>
public sealed class DpapiProtector : ISecretProtector
{
    private static readonly byte[] Entropy = "Companion.v1"u8.ToArray();

    public byte[] Protect(byte[] data) => ProtectedData.Protect(data, Entropy, DataProtectionScope.CurrentUser);

    public byte[] Unprotect(byte[] data) => ProtectedData.Unprotect(data, Entropy, DataProtectionScope.CurrentUser);
}

public sealed class SecretStore(string path, ISecretProtector protector, ILog log)
{
    public bool HasKey => File.Exists(path);

    public string? LoadApiKey()
    {
        if (!File.Exists(path)) return null;
        try
        {
            return Encoding.UTF8.GetString(protector.Unprotect(File.ReadAllBytes(path)));
        }
        catch (CryptographicException ex)
        {
            log.Error("Could not decrypt the stored API key.", ex);
            return null;
        }
    }

    public void SaveApiKey(string apiKey)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllBytes(temp, protector.Protect(Encoding.UTF8.GetBytes(apiKey.Trim())));
        File.Move(temp, path, overwrite: true);
    }

    public void Clear()
    {
        if (File.Exists(path)) File.Delete(path);
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test src/Companion.Tests --filter "FullyQualifiedName~SettingsStoreTests|FullyQualifiedName~SecretStoreTests"`
Expected: 6 passed.

- [ ] **Step 5: Commit**

```powershell
git add -A
git commit -m "feat(core): settings store and DPAPI-encrypted API key" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: Memory of the user and pet state

**Files:**
- Create: `src/Companion.Core/Memory.cs`, `src/Companion.Core/PetState.cs`
- Test: `src/Companion.Tests/MemoryTests.cs`

**Interfaces:**
- Consumes: `JsonFile`, `IClock`, `ILog`.
- Produces: `record Fact(string Id, string Text, DateTimeOffset Created)`; `record MemoryResult(bool Ok, string Message, Fact? Fact)`; `MemoryStore(string path, IClock clock, ILog log)` with `const int SoftLimit = 100`, `IReadOnlyList<Fact> Facts`, `MemoryResult Add(string)`, `bool Remove(string id)`, `bool Update(string id, string text)`, `event Action? Changed`. `PetState` (props `PetName`, `UserName`, `Hatched`, `Attention`=80, `Energy`=80, `PositionX`); `PetStateStore(string path, IClock clock, ILog log)` with `Current`, `Save()`, `Reset()`.

- [ ] **Step 1: Write the failing tests**

Create `src/Companion.Tests/MemoryTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test src/Companion.Tests --filter "FullyQualifiedName~MemoryStoreTests|FullyQualifiedName~PetStateStoreTests"`
Expected: build FAILS — `MemoryStore`, `PetStateStore` not found.

- [ ] **Step 3: Implement**

Create `src/Companion.Core/Memory.cs`:

```csharp
using System.Globalization;

namespace Companion.Core;

public sealed record Fact(string Id, string Text, DateTimeOffset Created);

public sealed record MemoryResult(bool Ok, string Message, Fact? Fact);

/// <summary>Facts about the user, stored in data\memory.json. Thread-safe: tools run on background threads.</summary>
public sealed class MemoryStore
{
    public const int SoftLimit = 100;

    private sealed class Document
    {
        public List<Fact> Facts { get; set; } = new();
    }

    private readonly string _path;
    private readonly IClock _clock;
    private readonly object _gate = new();
    private readonly Document _doc;

    public MemoryStore(string path, IClock clock, ILog log)
    {
        _path = path;
        _clock = clock;
        _doc = JsonFile.Load(path, () => new Document(), log, clock);
    }

    public event Action? Changed;

    public IReadOnlyList<Fact> Facts
    {
        get { lock (_gate) return _doc.Facts.ToList(); }
    }

    public MemoryResult Add(string text)
    {
        text = text.Trim();
        if (text.Length == 0) return new MemoryResult(false, "The fact is empty.", null);
        Fact fact;
        lock (_gate)
        {
            var existing = _doc.Facts.FirstOrDefault(f => string.Equals(f.Text, text, StringComparison.OrdinalIgnoreCase));
            if (existing is not null) return new MemoryResult(true, $"Already remembered as {existing.Id}.", existing);
            if (_doc.Facts.Count >= SoftLimit)
                return new MemoryResult(false, $"Memory is full ({SoftLimit} facts). Merge or forget older facts first.", null);
            fact = new Fact(NextId(), text, _clock.Now);
            _doc.Facts.Add(fact);
            JsonFile.Save(_path, _doc);
        }
        Changed?.Invoke();
        return new MemoryResult(true, $"Saved as {fact.Id}.", fact);
    }

    public bool Remove(string id)
    {
        lock (_gate)
        {
            if (_doc.Facts.RemoveAll(f => f.Id == id) == 0) return false;
            JsonFile.Save(_path, _doc);
        }
        Changed?.Invoke();
        return true;
    }

    public bool Update(string id, string text)
    {
        text = text.Trim();
        if (text.Length == 0) return false;
        lock (_gate)
        {
            var index = _doc.Facts.FindIndex(f => f.Id == id);
            if (index < 0) return false;
            _doc.Facts[index] = _doc.Facts[index] with { Text = text };
            JsonFile.Save(_path, _doc);
        }
        Changed?.Invoke();
        return true;
    }

    private string NextId()
    {
        var max = 0;
        foreach (var fact in _doc.Facts)
        {
            if (fact.Id.StartsWith("f_", StringComparison.Ordinal) &&
                int.TryParse(fact.Id[2..], NumberStyles.None, CultureInfo.InvariantCulture, out var n))
            {
                max = Math.Max(max, n);
            }
        }
        return $"f_{(max + 1).ToString("00", CultureInfo.InvariantCulture)}";
    }
}
```

Create `src/Companion.Core/PetState.cs`:

```csharp
namespace Companion.Core;

public sealed class PetState
{
    public string? PetName { get; set; }
    public string? UserName { get; set; }
    public bool Hatched { get; set; }
    public double Attention { get; set; } = 80;
    public double Energy { get; set; } = 80;
    public double? PositionX { get; set; }
}

public sealed class PetStateStore
{
    private readonly string _path;

    public PetStateStore(string path, IClock clock, ILog log)
    {
        _path = path;
        Current = JsonFile.Load(path, () => new PetState(), log, clock);
    }

    /// <summary>Live object shared with PetLife; never replaced, only mutated.</summary>
    public PetState Current { get; }

    public void Save() => JsonFile.Save(_path, Current);

    public void Reset()
    {
        var fresh = new PetState();
        Current.PetName = fresh.PetName;
        Current.UserName = fresh.UserName;
        Current.Hatched = fresh.Hatched;
        Current.Attention = fresh.Attention;
        Current.Energy = fresh.Energy;
        Current.PositionX = fresh.PositionX;
        Save();
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test src/Companion.Tests --filter "FullyQualifiedName~MemoryStoreTests|FullyQualifiedName~PetStateStoreTests"`
Expected: 5 passed.

- [ ] **Step 5: Commit**

```powershell
git add -A
git commit -m "feat(core): user memory facts and persisted pet state" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: Skin definition and animation timing

**Files:**
- Create: `src/Companion.Core/Skin.cs`
- Test: `src/Companion.Tests/SkinTests.cs`

**Interfaces:**
- Consumes: `JsonFile.Options`.
- Produces: `record AnimationDef(int Fps, bool Loop, int[] Frames)`; `SkinDefinition` with `int FrameSize`, `Dictionary<string, AnimationDef> Animations`, `static SkinDefinition Parse(string json)` (throws `FormatException`), `AnimationDef Get(string name)` (falls back to `idle`), `static (int X, int Y) FrameOrigin(int index, int frameSize, int sheetWidth)`; `AnimationPlayer(SkinDefinition)` with `string Current`, `void Play(string name, TimeSpan now)`, `int FrameAt(TimeSpan now)`, `TimeSpan FrameInterval`.

- [ ] **Step 1: Write the failing tests**

Create `src/Companion.Tests/SkinTests.cs`:

```csharp
using Companion.Core;
using Xunit;

namespace Companion.Tests;

public class SkinTests
{
    private const string Json = """
        {
          "frameSize": 16,
          "animations": {
            "idle":  { "fps": 4, "loop": true,  "frames": [0, 1, 2, 1] },
            "happy": { "fps": 8, "loop": false, "frames": [8, 9, 10] }
          }
        }
        """;

    [Fact]
    public void Parse_reads_frames_and_falls_back_to_idle()
    {
        var skin = SkinDefinition.Parse(Json);

        Assert.Equal(16, skin.FrameSize);
        Assert.Equal(new[] { 8, 9, 10 }, skin.Get("happy").Frames);
        Assert.Same(skin.Get("idle"), skin.Get("walk"));
    }

    [Theory]
    [InlineData("""{ "frameSize": 16, "animations": { "walk": { "fps": 4, "loop": true, "frames": [0] } } }""")]
    [InlineData("""{ "frameSize": 0, "animations": { "idle": { "fps": 4, "loop": true, "frames": [0] } } }""")]
    [InlineData("""{ "frameSize": 16, "animations": { "idle": { "fps": 0, "loop": true, "frames": [0] } } }""")]
    [InlineData("""{ "frameSize": 16, "animations": { "idle": { "fps": 4, "loop": true, "frames": [] } } }""")]
    [InlineData("not json")]
    public void Parse_rejects_invalid_skins(string json)
    {
        Assert.Throws<FormatException>(() => SkinDefinition.Parse(json));
    }

    [Fact]
    public void FrameOrigin_is_row_major()
    {
        Assert.Equal((0, 0), SkinDefinition.FrameOrigin(0, 16, 64));
        Assert.Equal((48, 0), SkinDefinition.FrameOrigin(3, 16, 64));
        Assert.Equal((16, 16), SkinDefinition.FrameOrigin(5, 16, 64));
    }

    [Fact]
    public void Player_loops_and_clamps_non_looping_animations()
    {
        var player = new AnimationPlayer(SkinDefinition.Parse(Json));

        player.Play("idle", TimeSpan.FromSeconds(10));
        Assert.Equal("idle", player.Current);
        Assert.Equal(0, player.FrameAt(TimeSpan.FromSeconds(10)));
        Assert.Equal(1, player.FrameAt(TimeSpan.FromSeconds(10.25)));
        Assert.Equal(0, player.FrameAt(TimeSpan.FromSeconds(11)));
        Assert.Equal(TimeSpan.FromSeconds(0.25), player.FrameInterval);

        player.Play("happy", TimeSpan.Zero);
        Assert.Equal(10, player.FrameAt(TimeSpan.FromSeconds(5)));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test src/Companion.Tests --filter "FullyQualifiedName~SkinTests"`
Expected: build FAILS — `SkinDefinition`, `AnimationPlayer` not found.

- [ ] **Step 3: Implement**

Create `src/Companion.Core/Skin.cs`:

```csharp
using System.Text.Json;

namespace Companion.Core;

public sealed record AnimationDef(int Fps, bool Loop, int[] Frames);

/// <summary>Contents of skins\&lt;name&gt;\sheet.json.</summary>
public sealed class SkinDefinition
{
    public int FrameSize { get; init; }
    public Dictionary<string, AnimationDef> Animations { get; init; } = new();

    public static SkinDefinition Parse(string json)
    {
        SkinDefinition? skin;
        try { skin = JsonSerializer.Deserialize<SkinDefinition>(json, JsonFile.Options); }
        catch (JsonException ex) { throw new FormatException("sheet.json is not valid JSON.", ex); }

        if (skin is null) throw new FormatException("sheet.json is empty.");
        if (skin.FrameSize <= 0) throw new FormatException("frameSize must be positive.");
        if (!skin.Animations.ContainsKey("idle")) throw new FormatException("An 'idle' animation is required.");
        foreach (var (name, animation) in skin.Animations)
        {
            if (animation.Fps is < 1 or > 60) throw new FormatException($"Animation '{name}' needs fps between 1 and 60.");
            if (animation.Frames is null || animation.Frames.Length == 0 || animation.Frames.Any(f => f < 0))
                throw new FormatException($"Animation '{name}' needs at least one non-negative frame.");
        }
        return skin;
    }

    public AnimationDef Get(string name) =>
        Animations.TryGetValue(name, out var animation) ? animation : Animations["idle"];

    public static (int X, int Y) FrameOrigin(int index, int frameSize, int sheetWidth)
    {
        var columns = Math.Max(1, sheetWidth / frameSize);
        return (index % columns * frameSize, index / columns * frameSize);
    }
}

/// <summary>Pure frame-timing logic; the window asks it which frame to show at a given time.</summary>
public sealed class AnimationPlayer(SkinDefinition skin)
{
    private TimeSpan _start;

    public string Current { get; private set; } = "";

    public TimeSpan FrameInterval => TimeSpan.FromSeconds(1.0 / skin.Get(Current).Fps);

    public void Play(string name, TimeSpan now)
    {
        Current = name;
        _start = now;
    }

    public int FrameAt(TimeSpan now)
    {
        var animation = skin.Get(Current);
        var step = (int)Math.Max(0, (now - _start).TotalSeconds * animation.Fps);
        return animation.Loop
            ? animation.Frames[step % animation.Frames.Length]
            : animation.Frames[Math.Min(step, animation.Frames.Length - 1)];
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test src/Companion.Tests --filter "FullyQualifiedName~SkinTests"`
Expected: 8 passed.

- [ ] **Step 5: Commit**

```powershell
git add -A
git commit -m "feat(core): skin definition parsing and animation timing" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: Pet meters (attention and energy)

**Files:**
- Create: `src/Companion.Core/PetMeters.cs`
- Test: `src/Companion.Tests/PetMetersTests.cs`

**Interfaces:**
- Consumes: `PetState` (Task 4).
- Produces: `PetMeters(PetState state)` with constants `AttentionDrainPerMinute = 0.5`, `EnergyDrainPerMinute = 1/3`, `EnergyGainPerMinuteAsleep = 2`, `ClickAttention = 15`, `ChatAttention = 30`; properties `Attention`, `Energy`; methods `Tick(TimeSpan elapsed, bool userActive, bool asleep)`, `OnClick()`, `OnChat()`.

- [ ] **Step 1: Write the failing tests**

Create `src/Companion.Tests/PetMetersTests.cs`:

```csharp
using Companion.Core;
using Xunit;

namespace Companion.Tests;

public class PetMetersTests
{
    [Fact]
    public void Attention_drains_only_while_the_user_is_active_and_pet_awake()
    {
        var state = new PetState();
        var meters = new PetMeters(state);

        meters.Tick(TimeSpan.FromMinutes(10), userActive: true, asleep: false);
        Assert.Equal(75, meters.Attention, 3);

        meters.Tick(TimeSpan.FromMinutes(10), userActive: false, asleep: false);
        Assert.Equal(75, meters.Attention, 3);
    }

    [Fact]
    public void Energy_drains_awake_and_refills_asleep_clamped_to_100()
    {
        var state = new PetState();
        var meters = new PetMeters(state);

        meters.Tick(TimeSpan.FromMinutes(30), userActive: false, asleep: false);
        Assert.Equal(70, meters.Energy, 3);

        meters.Tick(TimeSpan.FromMinutes(60), userActive: false, asleep: true);
        Assert.Equal(100, meters.Energy, 3);
        Assert.Equal(100, state.Energy, 3);
    }

    [Fact]
    public void Click_and_chat_add_attention_clamped()
    {
        var state = new PetState { Attention = 10 };
        var meters = new PetMeters(state);

        meters.OnClick();
        Assert.Equal(25, meters.Attention, 3);
        meters.OnChat();
        meters.OnChat();
        meters.OnChat();
        Assert.Equal(100, meters.Attention, 3);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test src/Companion.Tests --filter "FullyQualifiedName~PetMetersTests"`
Expected: build FAILS — `PetMeters` not found.

- [ ] **Step 3: Implement**

Create `src/Companion.Core/PetMeters.cs`:

```csharp
namespace Companion.Core;

/// <summary>Light-care needs. They only influence mood; nothing bad ever happens.</summary>
public sealed class PetMeters(PetState state)
{
    public const double AttentionDrainPerMinute = 0.5;      // −1 per 2 min while the user is at the PC
    public const double EnergyDrainPerMinute = 1.0 / 3.0;   // −1 per 3 min while awake
    public const double EnergyGainPerMinuteAsleep = 2;
    public const double ClickAttention = 15;
    public const double ChatAttention = 30;

    public double Attention => state.Attention;
    public double Energy => state.Energy;

    public void Tick(TimeSpan elapsed, bool userActive, bool asleep)
    {
        var minutes = Math.Max(0, elapsed.TotalMinutes);
        if (userActive && !asleep) state.Attention = Clamp(state.Attention - AttentionDrainPerMinute * minutes);
        state.Energy = Clamp(asleep
            ? state.Energy + EnergyGainPerMinuteAsleep * minutes
            : state.Energy - EnergyDrainPerMinute * minutes);
    }

    public void OnClick() => state.Attention = Clamp(state.Attention + ClickAttention);

    public void OnChat() => state.Attention = Clamp(state.Attention + ChatAttention);

    private static double Clamp(double value) => Math.Clamp(value, 0, 100);
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test src/Companion.Tests --filter "FullyQualifiedName~PetMetersTests"`
Expected: 3 passed.

- [ ] **Step 5: Commit**

```powershell
git add -A
git commit -m "feat(core): attention and energy meters" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 7: Pet life state machine and built-in lines

**Files:**
- Create: `src/Companion.Core/PetLife.cs`
- Test: `src/Companion.Tests/PetLifeTests.cs`

**Interfaces:**
- Consumes: `PetState`, `PetMeters`, `IClock`.
- Produces: `enum PetStateKind { Egg, Idle, Walk, Happy, Bored, Sleepy, Sleep, Thinking, Talking }`; `readonly record struct ActivitySnapshot(TimeSpan UserIdle, bool Suspended)`; `interface IUserActivity { ActivitySnapshot Sample(); }`; `record PetTick(PetStateKind State, bool Suspended, string? Line)`; `PetLife(PetState state, IClock clock, Random rng)` with `Meters`, `State`, `WalkDirection` (−1/+1), `PetTick Tick(ActivitySnapshot)`, `OnClick()`, `Hatch()`, `ForceSleep()`, `OnChatStarted()`, `OnFirstToken()`, `OnChatFinished(bool success)`, `ReverseWalk()`; `static class PetLines` with `Bored`, `Sleepy`, `string? Pick(PetStateKind, Random)`.

- [ ] **Step 1: Write the failing tests**

Create `src/Companion.Tests/PetLifeTests.cs`:

```csharp
using Companion.Core;
using Xunit;

namespace Companion.Tests;

public class PetLifeTests
{
    private static readonly ActivitySnapshot Present = new(TimeSpan.FromSeconds(5), false);

    private static (PetLife Life, FakeClock Clock, PetState State) NewPet(int hour = 14, bool hatched = true)
    {
        var clock = FakeClock.At(2026, 9, 24, hour);
        var state = new PetState { Hatched = hatched };
        return (new PetLife(state, clock, new Random(7)), clock, state);
    }

    [Fact]
    public void Unhatched_pet_is_an_egg_until_hatched_then_happy()
    {
        var (life, clock, _) = NewPet(hatched: false);
        Assert.Equal(PetStateKind.Egg, life.Tick(Present).State);

        life.Hatch();
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(PetStateKind.Happy, life.Tick(Present).State);
    }

    [Fact]
    public void Click_makes_it_happy_for_three_seconds()
    {
        var (life, clock, state) = NewPet();
        life.OnClick();
        Assert.Equal(PetStateKind.Happy, life.Tick(Present).State);
        Assert.Equal(95, state.Attention, 3);

        clock.Advance(TimeSpan.FromSeconds(3));
        Assert.Equal(PetStateKind.Idle, life.Tick(Present).State);
    }

    [Fact]
    public void Low_attention_is_bored_and_low_energy_is_sleepy()
    {
        var (life, _, state) = NewPet();
        state.Attention = 20;
        Assert.Equal(PetStateKind.Bored, life.Tick(Present).State);

        state.Energy = 10;
        Assert.Equal(PetStateKind.Sleepy, life.Tick(Present).State);
    }

    [Fact]
    public void Sleeps_after_five_idle_minutes_and_yawns_when_the_user_returns()
    {
        var (life, clock, _) = NewPet();
        Assert.Equal(PetStateKind.Sleepy, life.Tick(new(TimeSpan.FromMinutes(4), false)).State);
        Assert.Equal(PetStateKind.Sleep, life.Tick(new(TimeSpan.FromMinutes(6), false)).State);

        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(PetStateKind.Sleepy, life.Tick(new(TimeSpan.FromSeconds(1), false)).State);

        clock.Advance(TimeSpan.FromSeconds(3));
        Assert.Equal(PetStateKind.Idle, life.Tick(Present).State);
    }

    [Fact]
    public void At_night_it_sleeps_after_one_idle_minute()
    {
        var (life, _, _) = NewPet(hour: 2);
        Assert.NotEqual(PetStateKind.Sleep, life.Tick(new(TimeSpan.FromSeconds(30), false)).State);
        Assert.Equal(PetStateKind.Sleep, life.Tick(new(TimeSpan.FromMinutes(2), false)).State);
    }

    [Fact]
    public void Forced_sleep_lasts_until_clicked()
    {
        var (life, clock, _) = NewPet();
        life.ForceSleep();
        Assert.Equal(PetStateKind.Sleep, life.Tick(Present).State);

        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(PetStateKind.Sleep, life.Tick(Present).State);

        life.OnClick();
        Assert.Equal(PetStateKind.Happy, life.Tick(Present).State);
    }

    [Fact]
    public void Chat_states_override_everything_and_finishing_makes_it_happy()
    {
        var (life, _, state) = NewPet();
        state.Attention = 10;
        state.Energy = 5;

        life.OnChatStarted();
        Assert.Equal(PetStateKind.Thinking, life.Tick(Present).State);
        life.OnFirstToken();
        Assert.Equal(PetStateKind.Talking, life.Tick(Present).State);
        life.OnChatFinished(success: true);
        Assert.Equal(PetStateKind.Happy, life.Tick(Present).State);
        Assert.Equal(40, state.Attention, 1);
    }

    [Fact]
    public void Walks_within_a_minute_in_a_valid_direction()
    {
        var (life, clock, _) = NewPet();
        var walked = false;
        for (var i = 0; i < 61 && !walked; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            walked = life.Tick(Present).State == PetStateKind.Walk;
        }

        Assert.True(walked);
        Assert.Contains(life.WalkDirection, new[] { -1, 1 });
        var before = life.WalkDirection;
        life.ReverseWalk();
        Assert.Equal(-before, life.WalkDirection);
    }

    [Fact]
    public void Bored_line_appears_at_most_every_twenty_minutes_and_only_when_present()
    {
        var (life, clock, state) = NewPet();
        var lines = new List<int>();
        for (var minute = 1; minute <= 45; minute++)
        {
            clock.Advance(TimeSpan.FromMinutes(1));
            state.Attention = 20;
            if (life.Tick(Present).Line is not null) lines.Add(minute);
        }
        Assert.Equal(new[] { 20, 40 }, lines);

        clock.Advance(TimeSpan.FromMinutes(30));
        Assert.Null(life.Tick(new(TimeSpan.FromSeconds(1), false)).Line);   // typing right now
        Assert.Null(life.Tick(new(TimeSpan.FromSeconds(5), true)).Line);    // fullscreen
        Assert.NotNull(life.Tick(Present).Line);
    }

    [Fact]
    public void Tick_reports_suspension()
    {
        var (life, _, _) = NewPet();
        Assert.True(life.Tick(new(TimeSpan.Zero, true)).Suspended);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test src/Companion.Tests --filter "FullyQualifiedName~PetLifeTests"`
Expected: build FAILS — `PetLife`, `ActivitySnapshot`, `PetStateKind` not found.

- [ ] **Step 3: Implement**

Create `src/Companion.Core/PetLife.cs`:

```csharp
namespace Companion.Core;

public enum PetStateKind { Egg, Idle, Walk, Happy, Bored, Sleepy, Sleep, Thinking, Talking }

/// <param name="UserIdle">Time since the last keyboard/mouse input.</param>
/// <param name="Suspended">A fullscreen app is in front or the session is locked.</param>
public readonly record struct ActivitySnapshot(TimeSpan UserIdle, bool Suspended);

public interface IUserActivity
{
    ActivitySnapshot Sample();
}

public sealed record PetTick(PetStateKind State, bool Suspended, string? Line);

/// <summary>
/// The pet's brainless half: decides state once per second from time, meters and user activity.
/// Priority: Thinking/Talking &gt; Happy &gt; Sleep &gt; Sleepy &gt; Bored &gt; Walk &gt; Idle.
/// </summary>
public sealed class PetLife
{
    public static readonly TimeSpan HappyDuration = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan WakeYawnDuration = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan LineCooldown = TimeSpan.FromMinutes(20);
    public static readonly TimeSpan SleepyAfterIdle = TimeSpan.FromMinutes(3);
    public static readonly TimeSpan SleepAfterIdle = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan NightSleepAfterIdle = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan PresentWithin = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan TypingGuard = TimeSpan.FromSeconds(2);

    private enum ChatPhase { None, Thinking, Talking }

    private readonly PetState _state;
    private readonly IClock _clock;
    private readonly Random _rng;
    private DateTimeOffset _lastTick;
    private DateTimeOffset _lastLine;
    private DateTimeOffset _happyUntil;
    private DateTimeOffset _yawnUntil;
    private DateTimeOffset _walkUntil;
    private DateTimeOffset _nextWalkAt;
    private bool _forcedSleep;
    private bool _wasAsleep;
    private ChatPhase _chat;

    public PetLife(PetState state, IClock clock, Random rng)
    {
        _state = state;
        _clock = clock;
        _rng = rng;
        Meters = new PetMeters(state);
        var now = clock.Now;
        _lastTick = now;
        _lastLine = now;
        _nextWalkAt = now + NextWalkDelay();
        State = state.Hatched ? PetStateKind.Idle : PetStateKind.Egg;
    }

    public PetMeters Meters { get; }

    public PetStateKind State { get; private set; }

    public int WalkDirection { get; private set; } = 1;

    public PetTick Tick(ActivitySnapshot activity)
    {
        var now = _clock.Now;
        var elapsed = now - _lastTick;
        if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
        _lastTick = now;

        if (!_state.Hatched)
        {
            State = PetStateKind.Egg;
            return new PetTick(State, activity.Suspended, null);
        }

        Meters.Tick(elapsed, activity.UserIdle < PresentWithin, State == PetStateKind.Sleep);
        State = Decide(activity, now);

        string? line = null;
        if (!activity.Suspended
            && activity.UserIdle >= TypingGuard
            && activity.UserIdle < PresentWithin
            && now - _lastLine >= LineCooldown)
        {
            line = PetLines.Pick(State, _rng);
            if (line is not null) _lastLine = now;
        }
        return new PetTick(State, activity.Suspended, line);
    }

    public void OnClick()
    {
        if (!_state.Hatched) return;
        _forcedSleep = false;
        Meters.OnClick();
        _happyUntil = _clock.Now + HappyDuration;
    }

    public void Hatch()
    {
        _state.Hatched = true;
        _happyUntil = _clock.Now + HappyDuration;
    }

    public void ForceSleep() => _forcedSleep = true;

    public void OnChatStarted()
    {
        _forcedSleep = false;
        _chat = ChatPhase.Thinking;
    }

    public void OnFirstToken() => _chat = ChatPhase.Talking;

    public void OnChatFinished(bool success)
    {
        _chat = ChatPhase.None;
        if (!success) return;
        Meters.OnChat();
        _happyUntil = _clock.Now + HappyDuration;
    }

    public void ReverseWalk() => WalkDirection = -WalkDirection;

    private PetStateKind Decide(ActivitySnapshot activity, DateTimeOffset now)
    {
        if (_chat == ChatPhase.Thinking) return PetStateKind.Thinking;
        if (_chat == ChatPhase.Talking) return PetStateKind.Talking;
        if (now < _happyUntil) return PetStateKind.Happy;

        var sleep = _forcedSleep
            || activity.UserIdle >= SleepAfterIdle
            || (now.Hour < 7 && activity.UserIdle >= NightSleepAfterIdle);
        if (_wasAsleep && !sleep) _yawnUntil = now + WakeYawnDuration;
        _wasAsleep = sleep;
        if (sleep) return PetStateKind.Sleep;
        if (now < _yawnUntil) return PetStateKind.Sleepy;
        if (Meters.Energy < 25 || activity.UserIdle >= SleepyAfterIdle) return PetStateKind.Sleepy;
        if (Meters.Attention < 30) return PetStateKind.Bored;
        if (now < _walkUntil) return PetStateKind.Walk;
        if (now >= _nextWalkAt)
        {
            _walkUntil = now + TimeSpan.FromSeconds(2 + _rng.NextDouble() * 2);
            _nextWalkAt = _walkUntil + NextWalkDelay();
            WalkDirection = _rng.Next(2) == 0 ? -1 : 1;
            return PetStateKind.Walk;
        }
        return PetStateKind.Idle;
    }

    private TimeSpan NextWalkDelay() => TimeSpan.FromSeconds(20 + _rng.NextDouble() * 40);
}

/// <summary>Built-in lines shown in a mini bubble. Local only: they never cost API money.</summary>
public static class PetLines
{
    public static readonly string[] Bored =
    [
        "psst… anything I can help with?",
        "*sniffs around the taskbar*",
        "I could look something up for you!",
        "*wags* ask me anything!",
        "is it snack o'clock?",
    ];

    public static readonly string[] Sleepy =
    [
        "*yaaawn*",
        "long day, huh?",
        "*rubs eyes*",
    ];

    public static string? Pick(PetStateKind state, Random rng) => state switch
    {
        PetStateKind.Bored => Bored[rng.Next(Bored.Length)],
        PetStateKind.Sleepy => Sleepy[rng.Next(Sleepy.Length)],
        _ => null,
    };
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test src/Companion.Tests --filter "FullyQualifiedName~PetLifeTests"`
Expected: 10 passed.

- [ ] **Step 5: Commit**

```powershell
git add -A
git commit -m "feat(core): pet life state machine with moods, sleep and built-in lines" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 8: Spend tracking and the monthly cap

**Files:**
- Create: `src/Companion.Core/Spend.cs`
- Test: `src/Companion.Tests/SpendTests.cs`

**Interfaces:**
- Consumes: `SettingsStore`, `IClock`.
- Produces: `readonly record struct UsageTotals(long InputTokens, long OutputTokens, long CacheReadTokens, long CacheWriteTokens, long WebSearches)` with `operator +`; `static class Pricing` (`InputPerMillion` 4.00, `OutputPerMillion` 20.00, `CacheReadPerMillion` 0.20, `CacheWritePerMillion` 5.00, `PerWebSearch` 0.01, `decimal Cost(UsageTotals)`); `SpendTracker(SettingsStore settings, IClock clock)` with `Cap`, `ThisMonth`, `CapReached`, `decimal Record(UsageTotals)`, `bool TakeWarning()`.

- [ ] **Step 1: Write the failing tests**

Create `src/Companion.Tests/SpendTests.cs`:

```csharp
using Companion.Core;
using Xunit;

namespace Companion.Tests;

public class SpendTests
{
    [Fact]
    public void Cost_uses_opus_5_5_prices()
    {
        Assert.Equal(4.00m, Pricing.Cost(new UsageTotals(1_000_000, 0, 0, 0, 0)));
        Assert.Equal(20.00m, Pricing.Cost(new UsageTotals(0, 1_000_000, 0, 0, 0)));
        Assert.Equal(0.20m, Pricing.Cost(new UsageTotals(0, 0, 1_000_000, 0, 0)));
        Assert.Equal(5.00m, Pricing.Cost(new UsageTotals(0, 0, 0, 1_000_000, 0)));
        Assert.Equal(0.03m, Pricing.Cost(new UsageTotals(0, 0, 0, 0, 3)));
        Assert.Equal(0.014m, Pricing.Cost(new UsageTotals(2000, 300, 0, 0, 0)));
    }

    [Fact]
    public void Usage_totals_add_up()
    {
        var sum = new UsageTotals(1, 2, 3, 4, 5) + new UsageTotals(10, 20, 30, 40, 50);
        Assert.Equal(new UsageTotals(11, 22, 33, 44, 55), sum);
    }

    [Fact]
    public void Record_accumulates_per_month_and_persists()
    {
        using var tmp = new TempDir();
        var clock = FakeClock.At(2026, 9, 24, 12);
        var settings = new SettingsStore(tmp.PathOf("settings.json"), clock, NullLog.Instance);
        var spend = new SpendTracker(settings, clock);

        spend.Record(new UsageTotals(2000, 300, 0, 0, 0));
        spend.Record(new UsageTotals(2000, 300, 0, 0, 0));
        Assert.Equal(0.028m, spend.ThisMonth);

        clock.Advance(TimeSpan.FromDays(10));
        Assert.Equal(0m, spend.ThisMonth);
        var reloaded = new SettingsStore(tmp.PathOf("settings.json"), clock, NullLog.Instance);
        Assert.Equal(0.028m, reloaded.Current.SpendByMonth["2026-09"]);
    }

    [Fact]
    public void Cap_and_one_time_warning_at_80_percent()
    {
        using var tmp = new TempDir();
        var clock = FakeClock.At(2026, 9, 24, 12);
        var settings = new SettingsStore(tmp.PathOf("settings.json"), clock, NullLog.Instance);
        settings.Current.MonthlyCapUsd = 1.00m;
        var spend = new SpendTracker(settings, clock);

        settings.Current.SpendByMonth["2026-09"] = 0.79m;
        Assert.False(spend.TakeWarning());
        settings.Current.SpendByMonth["2026-09"] = 0.85m;
        Assert.True(spend.TakeWarning());
        Assert.False(spend.TakeWarning());
        Assert.False(spend.CapReached);

        settings.Current.SpendByMonth["2026-09"] = 1.00m;
        Assert.True(spend.CapReached);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test src/Companion.Tests --filter "FullyQualifiedName~SpendTests"`
Expected: build FAILS — `UsageTotals`, `Pricing`, `SpendTracker` not found.

- [ ] **Step 3: Implement**

Create `src/Companion.Core/Spend.cs`:

```csharp
using System.Globalization;

namespace Companion.Core;

public readonly record struct UsageTotals(long InputTokens, long OutputTokens, long CacheReadTokens, long CacheWriteTokens, long WebSearches)
{
    public static UsageTotals operator +(UsageTotals a, UsageTotals b) => new(
        a.InputTokens + b.InputTokens,
        a.OutputTokens + b.OutputTokens,
        a.CacheReadTokens + b.CacheReadTokens,
        a.CacheWriteTokens + b.CacheWriteTokens,
        a.WebSearches + b.WebSearches);
}

/// <summary>Estimated USD prices for claude-opus-5-5. The Anthropic console is the source of truth.</summary>
public static class Pricing
{
    public const decimal InputPerMillion = 4.00m;
    public const decimal OutputPerMillion = 20.00m;
    public const decimal CacheReadPerMillion = 0.20m;
    public const decimal CacheWritePerMillion = 5.00m;
    public const decimal PerWebSearch = 0.01m;

    public static decimal Cost(UsageTotals usage) =>
        (usage.InputTokens * InputPerMillion
         + usage.OutputTokens * OutputPerMillion
         + usage.CacheReadTokens * CacheReadPerMillion
         + usage.CacheWriteTokens * CacheWritePerMillion) / 1_000_000m
        + usage.WebSearches * PerWebSearch;
}

/// <summary>Month-to-date spend estimate and the local cap (a cap of 0 blocks all questions).</summary>
public sealed class SpendTracker(SettingsStore settings, IClock clock)
{
    private string MonthKey => clock.Now.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    public decimal Cap => settings.Current.MonthlyCapUsd;

    public decimal ThisMonth => settings.Current.SpendByMonth.GetValueOrDefault(MonthKey);

    public bool CapReached => ThisMonth >= Cap;

    public decimal Record(UsageTotals usage)
    {
        var cost = Pricing.Cost(usage);
        if (cost <= 0) return 0;
        settings.Current.SpendByMonth[MonthKey] = ThisMonth + cost;
        settings.Save();
        return cost;
    }

    /// <summary>True exactly once per month, the first time spend reaches 80 % of the cap.</summary>
    public bool TakeWarning()
    {
        if (Cap <= 0 || ThisMonth < Cap * 0.8m || ThisMonth >= Cap || settings.Current.CapWarnedMonth == MonthKey) return false;
        settings.Current.CapWarnedMonth = MonthKey;
        settings.Save();
        return true;
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test src/Companion.Tests --filter "FullyQualifiedName~SpendTests"`
Expected: 4 passed.

- [ ] **Step 5: Commit**

```powershell
git add -A
git commit -m "feat(core): spend estimate per month with cap and 80% warning" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 9: Conversation window, system prompt and chat log

**Files:**
- Create: `src/Companion.Core/Conversation.cs`, `src/Companion.Core/SystemPrompt.cs`, `src/Companion.Core/ChatLog.cs`
- Test: `src/Companion.Tests/ConversationTests.cs`

**Interfaces:**
- Consumes: `IClock`, `Fact`.
- Produces: `record Exchange(string User, string Assistant)`; `Conversation(IClock clock)` with `MaxExchanges = 10`, `ResetAfter = 30 min`, `string? SystemPrompt`, `IReadOnlyList<Exchange> Exchanges`, `void BeginTurn(Func<string> buildSystemPrompt)`, `string FormatUserTurn(string text)`, `void Add(string userTurn, string assistant)`, `void Reset()`; `static string SystemPrompt.Build(string petName, string userName, IReadOnlyList<Fact> facts)`; `ChatLog(string logsDirectory, IClock clock)` with `void Append(string user, string assistant)`.

- [ ] **Step 1: Write the failing tests**

Create `src/Companion.Tests/ConversationTests.cs`:

```csharp
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

        conversation.BeginTurn(Build);
        conversation.Add("u1", "a1");
        clock.Advance(TimeSpan.FromMinutes(5));
        conversation.BeginTurn(Build);

        Assert.Equal(1, builds);
        Assert.Equal("prompt 1", conversation.SystemPrompt);
    }

    [Fact]
    public void Resets_after_thirty_quiet_minutes()
    {
        var clock = FakeClock.At(2026, 9, 24, 14);
        var conversation = new Conversation(clock);
        var builds = 0;
        conversation.BeginTurn(() => $"prompt {++builds}");
        conversation.Add("u1", "a1");

        clock.Advance(TimeSpan.FromMinutes(30));
        conversation.BeginTurn(() => $"prompt {++builds}");

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
        conversation.BeginTurn(() => "p");
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
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test src/Companion.Tests --filter "FullyQualifiedName~ConversationTests|FullyQualifiedName~SystemPromptTests|FullyQualifiedName~ChatLogTests"`
Expected: build FAILS — `Conversation`, `SystemPrompt`, `ChatLog` not found.

- [ ] **Step 3: Implement**

Create `src/Companion.Core/Conversation.cs`:

```csharp
using System.Globalization;

namespace Companion.Core;

public sealed record Exchange(string User, string Assistant);

/// <summary>
/// Text-only history of the current chat. The system prompt is frozen for the whole conversation
/// so the cached prefix stays byte-identical between turns.
/// </summary>
public sealed class Conversation(IClock clock)
{
    public const int MaxExchanges = 10;
    public static readonly TimeSpan ResetAfter = TimeSpan.FromMinutes(30);

    private readonly List<Exchange> _exchanges = new();
    private DateTimeOffset _lastActivity;

    public string? SystemPrompt { get; private set; }

    public IReadOnlyList<Exchange> Exchanges => _exchanges;

    public void BeginTurn(Func<string> buildSystemPrompt)
    {
        var now = clock.Now;
        if (SystemPrompt is not null && now - _lastActivity >= ResetAfter) Reset();
        SystemPrompt ??= buildSystemPrompt();
        _lastActivity = now;
    }

    public string FormatUserTurn(string text) =>
        $"[{clock.Now.ToString("yyyy-MM-dd HH:mm, dddd", CultureInfo.InvariantCulture)}]\n{text}";

    public void Add(string userTurn, string assistant)
    {
        _exchanges.Add(new Exchange(userTurn, assistant));
        while (_exchanges.Count > MaxExchanges) _exchanges.RemoveAt(0);
        _lastActivity = clock.Now;
    }

    public void Reset()
    {
        _exchanges.Clear();
        SystemPrompt = null;
    }
}
```

Create `src/Companion.Core/SystemPrompt.cs`:

```csharp
using System.Text;

namespace Companion.Core;

public static class SystemPrompt
{
    public static string Build(string petName, string userName, IReadOnlyList<Fact> facts)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"You are {petName}, a small pixel-art creature who lives on {userName}'s Windows desktop and is their loyal companion - like a friendly, clever dog who happens to know a lot.");
        sb.AppendLine();
        sb.AppendLine("How you talk:");
        sb.AppendLine("- Warm, playful and a little dog-like, but being genuinely useful always comes first.");
        sb.AppendLine("- Short, direct answers by default (a few sentences). Give detail, steps or code only when asked or when the question clearly needs it.");
        sb.AppendLine("- You speak in a small speech bubble: use light markdown only - **bold**, bullet or numbered lists, `inline code` and ``` code blocks. No tables.");
        sb.AppendLine();
        sb.AppendLine("What you can do:");
        sb.AppendLine("- Answer questions and help think things through.");
        sb.AppendLine("- Search the web when a question needs current or specific information.");
        sb.AppendLine($"- Use the remember tool when {userName} shares something worth keeping (preferences, projects, tools they use) or asks you to remember something. Use the forget tool when asked to forget something or a fact is outdated. Confirm saves briefly, e.g. \"got it, I'll remember that\".");
        sb.AppendLine("- You cannot yet see the screen, read the clipboard, set reminders or control the PC. If asked, say that is coming in a future version.");
        sb.AppendLine();
        sb.AppendLine("Each user message starts with the current local date and time in [brackets]. Use it when time matters; never mention the brackets.");
        sb.AppendLine();
        sb.AppendLine($"What you know about {userName}:");
        if (facts.Count == 0) sb.AppendLine("(nothing yet)");
        foreach (var fact in facts) sb.AppendLine($"- [{fact.Id}] {fact.Text}");
        return sb.ToString().TrimEnd();
    }
}
```

Create `src/Companion.Core/ChatLog.cs`:

```csharp
using System.Globalization;
using System.Text.Json;

namespace Companion.Core;

/// <summary>Local-only chat history: data\logs\chat-YYYY-MM.jsonl.</summary>
public sealed class ChatLog(string logsDirectory, IClock clock)
{
    private readonly object _gate = new();

    public void Append(string user, string assistant)
    {
        var now = clock.Now;
        var line = JsonSerializer.Serialize(new { time = now, user, assistant });
        var path = Path.Combine(logsDirectory, $"chat-{now.ToString("yyyy-MM", CultureInfo.InvariantCulture)}.jsonl");
        lock (_gate)
        {
            Directory.CreateDirectory(logsDirectory);
            File.AppendAllText(path, line + Environment.NewLine);
        }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test src/Companion.Tests --filter "FullyQualifiedName~ConversationTests|FullyQualifiedName~SystemPromptTests|FullyQualifiedName~ChatLogTests"`
Expected: 8 passed.

- [ ] **Step 5: Commit**

```powershell
git add -A
git commit -m "feat(core): conversation window, personality prompt and local chat log" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 10: Tool registry with remember and forget

**Files:**
- Create: `src/Companion.Core/Tools.cs`
- Test: `src/Companion.Tests/ToolsTests.cs`

**Interfaces:**
- Consumes: `MemoryStore` (Task 4).
- Produces: `record ToolSpec(string Name, string Description, IReadOnlyDictionary<string, JsonElement> Properties, IReadOnlyList<string> Required)`; `record ToolOutcome(string Content, bool IsError)`; `interface ITool { ToolSpec Spec; ToolOutcome Execute(IReadOnlyDictionary<string, JsonElement> input); }`; `ToolRegistry` with `Specs`, `Register(ITool)`, `ToolOutcome Execute(string name, IReadOnlyDictionary<string, JsonElement> input)`; `RememberTool(MemoryStore)` (name `remember`, input `fact`); `ForgetTool(MemoryStore)` (name `forget`, input `fact_id`). Test helper `ToolInput.From(object)`.

- [ ] **Step 1: Write the failing tests**

Create `src/Companion.Tests/ToolsTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test src/Companion.Tests --filter "FullyQualifiedName~ToolsTests"`
Expected: build FAILS — `ToolRegistry`, `RememberTool`, `ForgetTool` not found.

- [ ] **Step 3: Implement**

Create `src/Companion.Core/Tools.cs`:

```csharp
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
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test src/Companion.Tests --filter "FullyQualifiedName~ToolsTests"`
Expected: 3 passed.

- [ ] **Step 5: Commit**

```powershell
git add -A
git commit -m "feat(core): tool registry with remember and forget" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 11: Claude contracts and the Brain

**Files:**
- Create: `src/Companion.Core/ClaudeContracts.cs`, `src/Companion.Core/Brain.cs`
- Test: `src/Companion.Tests/BrainTests.cs`

**Interfaces:**
- Consumes: `UsageTotals`, `SpendTracker` (Task 8); `Conversation`, `ChatLog` (Task 9); `ToolRegistry`, `ToolSpec`, `ToolOutcome` (Task 10); `SettingsStore`, `EffortLevel` (Task 3).
- Produces (contracts): `record HistoryMessage(bool FromUser, string Text)`; `record TurnRequest(string SystemPrompt, IReadOnlyList<HistoryMessage> Messages, EffortLevel Effort, IReadOnlyList<ToolSpec> Tools, bool WebSearch)`; `interface ITurnHandler { void OnTextDelta(string); void OnWebSearch(); ToolOutcome ExecuteTool(string, IReadOnlyDictionary<string, JsonElement>); }`; `enum TurnStop { EndTurn, Refusal, MaxTokens, ToolLoopLimit }`; `record TurnResult(string Text, TurnStop Stop, UsageTotals Usage)`; `interface IClaudeClient { Task<TurnResult> RunTurnAsync(TurnRequest, ITurnHandler, CancellationToken); Task ValidateKeyAsync(CancellationToken); }`; `enum ClaudeErrorKind { Network, Unauthorized, Billing, RateLimited, Server, BadRequest, Unknown }`; `ClaudeException(ClaudeErrorKind kind, string message, Exception? inner = null)` with `Kind`.
- Produces (brain): `interface IBrainListener { void OnThinking(); void OnFirstText(); void OnText(string delta); void OnStatus(string status); }`; `enum BrainOutcome { Ok, Refused, CutOff, CapReached, Error }`; `record BrainReply(BrainOutcome Outcome, string Text, string? Notice)`; `Brain(IClaudeClient client, Conversation conversation, Func<string> systemPrompt, ToolRegistry tools, SpendTracker spend, SettingsStore settings, ChatLog? chatLog, ILog log)` with `const string ThinkHarderPrompt`, `bool Busy`, `UsageTotals LastUsage`, `Task<BrainReply> AskAsync(string text, IBrainListener listener, CancellationToken ct = default)`, `Task<BrainReply> ThinkHarderAsync(IBrainListener listener, CancellationToken ct = default)`, `void NewChat()`.

- [ ] **Step 1: Create the contracts**

Create `src/Companion.Core/ClaudeContracts.cs`:

```csharp
using System.Text.Json;

namespace Companion.Core;

public sealed record HistoryMessage(bool FromUser, string Text);

public sealed record TurnRequest(
    string SystemPrompt,
    IReadOnlyList<HistoryMessage> Messages,
    EffortLevel Effort,
    IReadOnlyList<ToolSpec> Tools,
    bool WebSearch);

/// <summary>Callbacks from the client during one turn. May be invoked on a background thread.</summary>
public interface ITurnHandler
{
    void OnTextDelta(string text);
    void OnWebSearch();
    ToolOutcome ExecuteTool(string name, IReadOnlyDictionary<string, JsonElement> input);
}

public enum TurnStop { EndTurn, Refusal, MaxTokens, ToolLoopLimit }

public sealed record TurnResult(string Text, TurnStop Stop, UsageTotals Usage);

/// <summary>One user turn against Claude, including any tool rounds, streamed.</summary>
public interface IClaudeClient
{
    Task<TurnResult> RunTurnAsync(TurnRequest request, ITurnHandler handler, CancellationToken ct);
    Task ValidateKeyAsync(CancellationToken ct);
}

public enum ClaudeErrorKind { Network, Unauthorized, Billing, RateLimited, Server, BadRequest, Unknown }

public sealed class ClaudeException(ClaudeErrorKind kind, string message, Exception? inner = null) : Exception(message, inner)
{
    public ClaudeErrorKind Kind { get; } = kind;
}
```

- [ ] **Step 2: Write the failing Brain tests**

Create `src/Companion.Tests/BrainTests.cs`:

```csharp
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
        await rig.Brain.AskAsync("one", rig.Listener);
        rig.Claude.Behavior = (_, _) => new TurnResult("ok", TurnStop.EndTurn, new UsageTotals(10, 1, 0, 0, 0));

        await rig.Brain.AskAsync("two", rig.Listener);

        Assert.Equal("(no answer)", rig.Claude.Requests[1].Messages[1].Text);
    }

    [Theory]
    [InlineData(ClaudeErrorKind.Network, "internet")]
    [InlineData(ClaudeErrorKind.Unauthorized, "API key")]
    [InlineData(ClaudeErrorKind.Billing, "credit")]
    [InlineData(ClaudeErrorKind.RateLimited, "busy")]
    [InlineData(ClaudeErrorKind.Server, "trouble")]
    public async Task Errors_become_friendly_notices(ClaudeErrorKind kind, string expected)
    {
        using var rig = new Rig();
        rig.Claude.Throw = new ClaudeException(kind, "raw detail");

        var reply = await rig.Brain.AskAsync("hi", rig.Listener);

        Assert.Equal(BrainOutcome.Error, reply.Outcome);
        Assert.Contains(expected, reply.Notice);
        Assert.False(rig.Brain.Busy);
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
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test src/Companion.Tests --filter "FullyQualifiedName~BrainTests"`
Expected: build FAILS — `Brain`, `IBrainListener`, `BrainOutcome` not found.

- [ ] **Step 4: Implement the Brain**

Create `src/Companion.Core/Brain.cs`:

```csharp
using System.Text;
using System.Text.Json;

namespace Companion.Core;

/// <summary>UI callbacks. May be invoked from background threads; the UI must marshal.</summary>
public interface IBrainListener
{
    void OnThinking();
    void OnFirstText();
    void OnText(string delta);
    void OnStatus(string status);
}

public enum BrainOutcome { Ok, Refused, CutOff, CapReached, Error }

public sealed record BrainReply(BrainOutcome Outcome, string Text, string? Notice);

/// <summary>Orchestrates one question: cap check → conversation → Claude → tools → spend → history.</summary>
public sealed class Brain(
    IClaudeClient client,
    Conversation conversation,
    Func<string> systemPrompt,
    ToolRegistry tools,
    SpendTracker spend,
    SettingsStore settings,
    ChatLog? chatLog,
    ILog log)
{
    public const string ThinkHarderPrompt = "Think this through more carefully and give me your best answer.";

    public bool Busy { get; private set; }

    public UsageTotals LastUsage { get; private set; }

    public Task<BrainReply> AskAsync(string text, IBrainListener listener, CancellationToken ct = default) =>
        RunAsync(text, settings.Current.EverydayEffort, listener, ct);

    public Task<BrainReply> ThinkHarderAsync(IBrainListener listener, CancellationToken ct = default) =>
        RunAsync(ThinkHarderPrompt, settings.Current.ThinkHarderEffort, listener, ct);

    public void NewChat() => conversation.Reset();

    private async Task<BrainReply> RunAsync(string text, EffortLevel effort, IBrainListener listener, CancellationToken ct)
    {
        if (Busy) return new BrainReply(BrainOutcome.Error, "", "Still working on the last one…");
        if (spend.CapReached)
        {
            return new BrainReply(BrainOutcome.CapReached, "",
                $"I've reached this month's ${spend.Cap:0.##} spending cap. You can raise it in Settings.");
        }

        Busy = true;
        try
        {
            conversation.BeginTurn(systemPrompt);
            var userTurn = conversation.FormatUserTurn(text);
            var messages = new List<HistoryMessage>();
            foreach (var exchange in conversation.Exchanges)
            {
                messages.Add(new HistoryMessage(true, exchange.User));
                messages.Add(new HistoryMessage(false, exchange.Assistant));
            }
            messages.Add(new HistoryMessage(true, userTurn));

            var handler = new TurnHandler(listener, tools);
            listener.OnThinking();
            TurnResult result;
            try
            {
                result = await client.RunTurnAsync(
                    new TurnRequest(conversation.SystemPrompt!, messages, effort, tools.Specs, WebSearch: true), handler, ct);
            }
            catch (ClaudeException ex)
            {
                log.Error($"Claude call failed ({ex.Kind}).", ex);
                return new BrainReply(BrainOutcome.Error, handler.Text, FriendlyError(ex.Kind));
            }
            catch (OperationCanceledException)
            {
                return new BrainReply(BrainOutcome.Error, handler.Text, "Okay, stopped.");
            }

            LastUsage = result.Usage;
            spend.Record(result.Usage);
            var answer = result.Text.Trim();
            if (result.Stop == TurnStop.Refusal)
            {
                return new BrainReply(BrainOutcome.Refused, answer, "Sorry, I can't help with that one.");
            }

            var outcome = BrainOutcome.Ok;
            if (result.Stop is TurnStop.MaxTokens or TurnStop.ToolLoopLimit)
            {
                answer += " …(cut off)";
                outcome = BrainOutcome.CutOff;
            }

            conversation.Add(userTurn, answer.Length == 0 ? "(no answer)" : answer);
            chatLog?.Append(text, answer);
            var notice = spend.TakeWarning() ? $"Heads up: that's 80% of this month's ${spend.Cap:0.##} budget." : null;
            return new BrainReply(outcome, answer, notice);
        }
        finally
        {
            Busy = false;
        }
    }

    private static string FriendlyError(ClaudeErrorKind kind) => kind switch
    {
        ClaudeErrorKind.Network => "I can't reach the internet right now.",
        ClaudeErrorKind.Unauthorized => "My API key doesn't work. Check it in Settings.",
        ClaudeErrorKind.Billing => "Out of API credit. Top up at console.anthropic.com.",
        ClaudeErrorKind.RateLimited => "Claude is busy right now. Try again in a minute.",
        ClaudeErrorKind.Server => "Claude is having trouble right now. Try again in a bit.",
        _ => "Something went wrong (details are in data\\logs).",
    };

    private sealed class TurnHandler(IBrainListener listener, ToolRegistry tools) : ITurnHandler
    {
        private readonly StringBuilder _text = new();
        private bool _gotText;

        public string Text => _text.ToString();

        public void OnTextDelta(string text)
        {
            if (!_gotText)
            {
                _gotText = true;
                listener.OnFirstText();
            }
            _text.Append(text);
            listener.OnText(text);
        }

        public void OnWebSearch() => listener.OnStatus("searching the web…");

        public ToolOutcome ExecuteTool(string name, IReadOnlyDictionary<string, JsonElement> input) => tools.Execute(name, input);
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test src/Companion.Tests --filter "FullyQualifiedName~BrainTests"`
Expected: 15 passed (10 facts + 5 theory cases).

- [ ] **Step 6: Commit**

```powershell
git add -A
git commit -m "feat(core): brain orchestrating conversation, tools, spend and errors" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 12: Anthropic SDK client and smoke tool

**Files:**
- Create: `src/Companion.Core/AnthropicClaudeClient.cs`
- Create: `tools/Smoke/Smoke.csproj`, `tools/Smoke/Program.cs`
- Modify: `Companion.sln` (add Smoke)

**Interfaces:**
- Consumes: everything in `ClaudeContracts.cs`, `Brain`, stores.
- Produces: `AnthropicClaudeClient(string apiKey) : IClaudeClient` with constants `Model = "claude-opus-5-5"`, `MaxTokens = 8000`, `MaxRounds = 5`. CLI: `dotnet run --project tools/Smoke -- --save-key <key>` stores an encrypted key; `dotnet run --project tools/Smoke` runs a real 3-question conversation.

This class was compile-verified against `Anthropic` 12.50.0 during planning. It is covered by the smoke test, not unit tests, because it only translates to the SDK.

- [ ] **Step 1: Create the client**

Create `src/Companion.Core/AnthropicClaudeClient.cs`:

```csharp
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Beta.Messages;

namespace Companion.Core;

/// <summary>
/// Streams one turn from claude-opus-5-5 through the beta Messages endpoint, running client tool rounds.
/// Completed exchanges arrive as plain text; thinking/tool blocks are echoed only within this turn.
/// </summary>
public sealed class AnthropicClaudeClient : IClaudeClient
{
    public const string Model = "claude-opus-5-5";
    public const int MaxTokens = 8000;
    public const int MaxRounds = 5;
    private const string BindingBeta = "thinking-binding-controls-2026-08-01";

    private readonly AnthropicClient _client;

    public AnthropicClaudeClient(string apiKey) => _client = new AnthropicClient { ApiKey = apiKey };

    /// <summary>Free check: retrieving model metadata fails with 401 on a bad key.</summary>
    public async Task ValidateKeyAsync(CancellationToken ct)
    {
        try { await _client.Models.Retrieve(Model, cancellationToken: ct); }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested) { throw Map(ex); }
    }

    public async Task<TurnResult> RunTurnAsync(TurnRequest request, ITurnHandler handler, CancellationToken ct)
    {
        var messages = request.Messages
            .Select(m => new BetaMessageParam { Role = m.FromUser ? Role.User : Role.Assistant, Content = m.Text })
            .ToList();
        var text = new StringBuilder();
        var usage = new UsageTotals();

        for (var round = 0; round < MaxRounds; round++)
        {
            BetaMessage message;
            try
            {
                message = await Tap(_client.Beta.Messages.CreateStreaming(BuildParams(request, messages), ct), handler, text).Aggregate();
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                throw Map(ex);
            }

            usage += ToUsage(message);
            string stop = message.StopReason?.Raw() ?? "end_turn";
            if (stop == "refusal") return new TurnResult(text.ToString(), TurnStop.Refusal, usage);
            if (stop == "max_tokens") return new TurnResult(text.ToString(), TurnStop.MaxTokens, usage);
            if (stop != "tool_use" && stop != "pause_turn") return new TurnResult(text.ToString(), TurnStop.EndTurn, usage);

            var (echo, results) = BuildToolRound(message, handler);
            messages.Add(new BetaMessageParam { Role = Role.Assistant, Content = echo });
            if (results.Count > 0) messages.Add(new BetaMessageParam { Role = Role.User, Content = results });
        }
        return new TurnResult(text.ToString(), TurnStop.ToolLoopLimit, usage);
    }

    private static MessageCreateParams BuildParams(TurnRequest request, List<BetaMessageParam> messages)
    {
        var tools = new List<BetaToolUnion>();
        if (request.WebSearch) tools.Add(new BetaToolUnion(new BetaWebSearchTool20260209 { MaxUses = 3 }));
        foreach (var spec in request.Tools)
        {
            tools.Add(new BetaTool
            {
                Name = spec.Name,
                Description = spec.Description,
                InputSchema = new() { Properties = spec.Properties.ToDictionary(), Required = [.. spec.Required] },
            });
        }

        return new MessageCreateParams
        {
            Model = Model,
            MaxTokens = MaxTokens,
            Betas = [BindingBeta],
            Thinking = new BetaThinkingConfigAdaptive
            {
                BlockBinding = new BetaThinkingBlockBinding { PrefixMismatchBehavior = BetaThinkingPrefixMismatchBehavior.DropBlock },
            },
            OutputConfig = new BetaOutputConfig
            {
                Effort = request.Effort switch { EffortLevel.Low => Effort.Low, EffortLevel.Medium => Effort.Medium, _ => Effort.High },
            },
            System = new List<BetaTextBlockParam> { new() { Text = request.SystemPrompt, CacheControl = new BetaCacheControlEphemeral() } },
            Tools = tools,
            Messages = messages,
        };
    }

    /// <summary>Forwards text deltas to the UI while the SDK aggregates the full message.</summary>
    private static async IAsyncEnumerable<BetaRawMessageStreamEvent> Tap(
        IAsyncEnumerable<BetaRawMessageStreamEvent> source, ITurnHandler handler, StringBuilder text,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var firstTextThisRound = true;
        await foreach (var ev in source.WithCancellation(ct))
        {
            if (ev.TryPickContentBlockDelta(out var delta) && delta.Delta.TryPickText(out var t))
            {
                var piece = t.Text;
                if (firstTextThisRound && text.Length > 0 && text[^1] != '\n') piece = "\n\n" + piece;
                firstTextThisRound = false;
                text.Append(piece);
                handler.OnTextDelta(piece);
            }
            else if (ev.TryPickContentBlockStart(out var start) && start.ContentBlock.TryPickBetaServerToolUse(out _))
            {
                handler.OnWebSearch();
            }
            yield return ev;
        }
    }

    private static (List<BetaContentBlockParam> Echo, List<BetaContentBlockParam> Results) BuildToolRound(BetaMessage message, ITurnHandler handler)
    {
        var echo = new List<BetaContentBlockParam>();
        var results = new List<BetaContentBlockParam>();
        foreach (var block in message.Content)
        {
            if (block.TryPickText(out var text)) echo.Add(new BetaTextBlockParam { Text = text.Text });
            else if (block.TryPickThinking(out var thinking)) echo.Add(new BetaThinkingBlockParam { Thinking = thinking.Thinking, Signature = thinking.Signature });
            else if (block.TryPickRedactedThinking(out var redacted)) echo.Add(new BetaRedactedThinkingBlockParam { Data = redacted.Data });
            else if (block.TryPickServerToolUse(out var serverUse)) echo.Add(Convert<BetaServerToolUseBlockParam>(serverUse));
            else if (block.TryPickWebSearchToolResult(out var searchResult)) echo.Add(Convert<BetaWebSearchToolResultBlockParam>(searchResult));
            else if (block.TryPickToolUse(out var toolUse))
            {
                echo.Add(new BetaToolUseBlockParam { ID = toolUse.ID, Name = toolUse.Name, Input = toolUse.Input });
                var outcome = handler.ExecuteTool(toolUse.Name, toolUse.Input);
                results.Add(new BetaToolResultBlockParam { ToolUseID = toolUse.ID, Content = outcome.Content, IsError = outcome.IsError });
            }
        }
        return (echo, results);
    }

    // Response blocks and request params share the same wire shape; round-trip through JSON.
    private static T Convert<T>(object block) =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(block, block.GetType()))
        ?? throw new InvalidOperationException("Could not convert " + block.GetType().Name);

    private static UsageTotals ToUsage(BetaMessage message) => new(
        message.Usage.InputTokens,
        message.Usage.OutputTokens,
        message.Usage.CacheReadInputTokens ?? 0,
        message.Usage.CacheCreationInputTokens ?? 0,
        message.Usage.ServerToolUse?.WebSearchRequests ?? 0);

    private static ClaudeException Map(Exception ex) => ex switch
    {
        ClaudeException ce => ce,
        AnthropicUnauthorizedException => new(ClaudeErrorKind.Unauthorized, ex.Message, ex),
        AnthropicRateLimitException => new(ClaudeErrorKind.RateLimited, ex.Message, ex),
        Anthropic5xxException => new(ClaudeErrorKind.Server, ex.Message, ex),
        AnthropicBadRequestException when ex.Message.Contains("credit balance", StringComparison.OrdinalIgnoreCase)
            => new(ClaudeErrorKind.Billing, ex.Message, ex),
        AnthropicBadRequestException => new(ClaudeErrorKind.BadRequest, ex.Message, ex),
        AnthropicIOException or HttpRequestException or OperationCanceledException => new(ClaudeErrorKind.Network, ex.Message, ex),
        AnthropicApiException => new(ClaudeErrorKind.Unknown, ex.Message, ex),
        _ => new(ClaudeErrorKind.Unknown, ex.Message, ex),
    };
}
```

- [ ] **Step 2: Build**

Run: `dotnet build src/Companion.Core`
Expected: `Build succeeded.` If a type name differs in a newer SDK patch, fix from the compiler message; do not change the package version.

- [ ] **Step 3: Create the smoke tool**

Create `tools/Smoke/Smoke.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\Companion.Core\Companion.Core.csproj" />
  </ItemGroup>
</Project>
```

Create `tools/Smoke/Program.cs`:

```csharp
using Companion.Core;

var paths = AppPaths.Resolve(AppContext.BaseDirectory, Environment.GetEnvironmentVariable("COMPANION_ROOT"));
paths.EnsureCreated();
var clock = new SystemClock();
var log = new FileLog(paths.Logs, clock);
var secrets = new SecretStore(paths.SecretsFile, new DpapiProtector(), log);

if (args.Length == 2 && args[0] == "--save-key")
{
    secrets.SaveApiKey(args[1]);
    Console.WriteLine($"API key saved (encrypted) to {paths.SecretsFile}");
    return 0;
}

var key = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") ?? secrets.LoadApiKey();
if (string.IsNullOrWhiteSpace(key))
{
    Console.Error.WriteLine("No API key. Run: dotnet run --project tools/Smoke -- --save-key sk-ant-...");
    return 1;
}

var client = new AnthropicClaudeClient(key);
await client.ValidateKeyAsync(CancellationToken.None);
Console.WriteLine("Key OK.");

var temp = Path.Combine(Path.GetTempPath(), "companion-smoke-" + Guid.NewGuid().ToString("N"));
var settings = new SettingsStore(Path.Combine(temp, "settings.json"), clock, log);
var memory = new MemoryStore(Path.Combine(temp, "memory.json"), clock, log);
var tools = new ToolRegistry();
tools.Register(new RememberTool(memory));
tools.Register(new ForgetTool(memory));
var spend = new SpendTracker(settings, clock);
var brain = new Brain(client, new Conversation(clock), () => SystemPrompt.Build("Pip", "Tester", memory.Facts),
    tools, spend, settings, null, log);

string[] questions =
[
    "Remember that my favourite colour is teal. Then tell me a one-line joke.",
    "What's my favourite colour?",
    "Search the web: what is the latest stable .NET version? One sentence.",
];
foreach (var question in questions)
{
    Console.WriteLine($"\n> {question}");
    var reply = await brain.AskAsync(question, new ConsoleListener());
    var u = brain.LastUsage;
    Console.WriteLine($"\n[{reply.Outcome}] notice={reply.Notice ?? "-"} in={u.InputTokens} out={u.OutputTokens} cacheRead={u.CacheReadTokens} cacheWrite={u.CacheWriteTokens} searches={u.WebSearches}");
}

Console.WriteLine($"\nFacts: {string.Join(" | ", memory.Facts.Select(f => f.Text))}");
Console.WriteLine($"Estimated cost of this smoke run: ${spend.ThisMonth:0.0000}");
return 0;

sealed class ConsoleListener : IBrainListener
{
    public void OnThinking() => Console.Write("(thinking) ");
    public void OnFirstText() { }
    public void OnText(string delta) => Console.Write(delta);
    public void OnStatus(string status) => Console.Write($"({status}) ");
}
```

- [ ] **Step 4: Add the smoke tool to the solution and build it**

Run: `dotnet sln add tools/Smoke/Smoke.csproj`, then `dotnet build tools/Smoke`
Expected: `Build succeeded.`

- [ ] **Step 5: Run the smoke test (needs the user)**

This spends real API credit (roughly a few US cents) and needs the user's key. Do not type or paste the key yourself. Ask the user to run, in their own terminal:

```powershell
dotnet run --project tools/Smoke -- --save-key <their key>
```

Then, with the user's go-ahead, run: `dotnet run --project tools/Smoke`
Expected: `Key OK.`, three streamed answers each ending `[Ok]`, `Facts: My favourite colour is teal` (or similar wording), a non-zero `searches=` on the third question, and a cost line. Note whether `cacheRead` is non-zero on questions 2–3 (fine either way; the prompt may be below the minimum cacheable size).
If the third question fails with a 400 mentioning a web-search block, report the exact message: it means `Convert<T>` for server-tool blocks needs adjusting.

- [ ] **Step 6: Commit**

```powershell
git add -A
git commit -m "feat(core): Anthropic SDK client for claude-opus-5-5 plus smoke tool" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 13: Minimal markdown parser for the bubble

**Files:**
- Create: `src/Companion.Core/MarkdownLite.cs`
- Test: `src/Companion.Tests/MarkdownLiteTests.cs`

**Interfaces:**
- Produces: `enum MdBlockKind { Paragraph, Heading, Bullet, Numbered, Code }`; `record MdSpan(string Text, bool Bold = false, bool Code = false)`; `record MdBlock(MdBlockKind Kind, IReadOnlyList<MdSpan> Spans, string? CodeText = null, int Number = 0)`; `static IReadOnlyList<MdBlock> MarkdownLite.Parse(string)`, `static IReadOnlyList<MdSpan> MarkdownLite.ParseInline(string)`.

- [ ] **Step 1: Write the failing tests**

Create `src/Companion.Tests/MarkdownLiteTests.cs`:

```csharp
using Companion.Core;
using Xunit;

namespace Companion.Tests;

public class MarkdownLiteTests
{
    [Fact]
    public void Inline_bold_and_code()
    {
        var spans = MarkdownLite.ParseInline("Hello **world** and `x()` ok");

        Assert.Equal(new[] { "Hello ", "world", " and ", "x()", " ok" }, spans.Select(s => s.Text));
        Assert.True(spans[1].Bold);
        Assert.True(spans[3].Code);
        Assert.False(spans[4].Bold);
    }

    [Fact]
    public void Unclosed_bold_runs_to_the_end()
    {
        var spans = MarkdownLite.ParseInline("a **b");
        Assert.False(spans[0].Bold);
        Assert.True(spans[1].Bold);
    }

    [Fact]
    public void Lists_headings_and_paragraphs()
    {
        var blocks = MarkdownLite.Parse("## Title\nline one\nline two\n\n- a\n* b\n2. c\n**Bold** start");

        Assert.Equal(
            new[] { MdBlockKind.Heading, MdBlockKind.Paragraph, MdBlockKind.Bullet, MdBlockKind.Bullet, MdBlockKind.Numbered, MdBlockKind.Paragraph },
            blocks.Select(b => b.Kind));
        Assert.Equal("line one\nline two", blocks[1].Spans[0].Text);
        Assert.Equal(2, blocks[4].Number);
        Assert.True(blocks[5].Spans[0].Bold);
    }

    [Fact]
    public void Fenced_code_keeps_indentation()
    {
        var blocks = MarkdownLite.Parse("Before\r\n```cs\r\nvar x = 1;\r\n  y();\r\n```\r\nAfter");

        Assert.Equal(new[] { MdBlockKind.Paragraph, MdBlockKind.Code, MdBlockKind.Paragraph }, blocks.Select(b => b.Kind));
        Assert.Equal("var x = 1;\n  y();", blocks[1].CodeText);
    }

    [Fact]
    public void Unclosed_fence_while_streaming_is_still_code()
    {
        var blocks = MarkdownLite.Parse("```\nabc");
        Assert.Equal("abc", Assert.Single(blocks).CodeText);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test src/Companion.Tests --filter "FullyQualifiedName~MarkdownLiteTests"`
Expected: build FAILS — `MarkdownLite` not found.

- [ ] **Step 3: Implement**

Create `src/Companion.Core/MarkdownLite.cs`:

```csharp
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Companion.Core;

public enum MdBlockKind { Paragraph, Heading, Bullet, Numbered, Code }

public sealed record MdSpan(string Text, bool Bold = false, bool Code = false);

public sealed record MdBlock(MdBlockKind Kind, IReadOnlyList<MdSpan> Spans, string? CodeText = null, int Number = 0);

/// <summary>Just enough markdown for a speech bubble: paragraphs, headings, lists, fenced code, **bold**, `code`.</summary>
public static partial class MarkdownLite
{
    [GeneratedRegex(@"^#{1,6}\s+(.*)$")]
    private static partial Regex HeadingRegex();

    [GeneratedRegex(@"^\s*[-*•]\s+(.*)$")]
    private static partial Regex BulletRegex();

    [GeneratedRegex(@"^\s*(\d{1,6})[.)]\s+(.*)$")]
    private static partial Regex NumberedRegex();

    public static IReadOnlyList<MdBlock> Parse(string markdown)
    {
        var blocks = new List<MdBlock>();
        var paragraph = new List<string>();
        StringBuilder? code = null;

        void FlushParagraph()
        {
            if (paragraph.Count == 0) return;
            blocks.Add(new MdBlock(MdBlockKind.Paragraph, ParseInline(string.Join("\n", paragraph))));
            paragraph.Clear();
        }

        foreach (var raw in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.TrimEnd();
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                if (code is null)
                {
                    FlushParagraph();
                    code = new StringBuilder();
                }
                else
                {
                    blocks.Add(new MdBlock(MdBlockKind.Code, [], code.ToString().TrimEnd('\n')));
                    code = null;
                }
                continue;
            }
            if (code is not null)
            {
                code.Append(line).Append('\n');
                continue;
            }
            if (line.Length == 0)
            {
                FlushParagraph();
                continue;
            }

            Match match;
            if ((match = HeadingRegex().Match(line)).Success)
            {
                FlushParagraph();
                blocks.Add(new MdBlock(MdBlockKind.Heading, ParseInline(match.Groups[1].Value)));
            }
            else if ((match = BulletRegex().Match(line)).Success)
            {
                FlushParagraph();
                blocks.Add(new MdBlock(MdBlockKind.Bullet, ParseInline(match.Groups[1].Value)));
            }
            else if ((match = NumberedRegex().Match(line)).Success)
            {
                FlushParagraph();
                var number = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                blocks.Add(new MdBlock(MdBlockKind.Numbered, ParseInline(match.Groups[2].Value), Number: number));
            }
            else
            {
                paragraph.Add(line);
            }
        }

        FlushParagraph();
        if (code is not null) blocks.Add(new MdBlock(MdBlockKind.Code, [], code.ToString().TrimEnd('\n')));
        return blocks;
    }

    public static IReadOnlyList<MdSpan> ParseInline(string text)
    {
        var spans = new List<MdSpan>();
        var current = new StringBuilder();
        var bold = false;

        void Flush()
        {
            if (current.Length == 0) return;
            spans.Add(new MdSpan(current.ToString(), bold));
            current.Clear();
        }

        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '`')
            {
                var end = text.IndexOf('`', i + 1);
                if (end > i)
                {
                    Flush();
                    spans.Add(new MdSpan(text[(i + 1)..end], bold, Code: true));
                    i = end;
                    continue;
                }
            }
            if (text[i] == '*' && i + 1 < text.Length && text[i + 1] == '*')
            {
                Flush();
                bold = !bold;
                i++;
                continue;
            }
            current.Append(text[i]);
        }

        Flush();
        return spans;
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test src/Companion.Tests --filter "FullyQualifiedName~MarkdownLiteTests"`
Expected: 5 passed.

- [ ] **Step 5: Commit**

```powershell
git add -A
git commit -m "feat(core): minimal markdown parser for chat bubbles" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 14: First-run onboarding flow

**Files:**
- Create: `src/Companion.Core/Onboarding.cs`
- Test: `src/Companion.Tests/OnboardingTests.cs`

**Interfaces:**
- Consumes: `PetStateStore` (Task 4), `SecretStore` (Task 3), `ClaudeException` (Task 11).
- Produces: `enum OnboardingStep { PetName, UserName, ApiKey, Done }`; `OnboardingFlow(PetStateStore pet, SecretStore secrets, Func<string, CancellationToken, Task> validateKey, string hotkey)` with `MaxNameLength = 20`, `Step`, `Prompt`, `Task<string?> SubmitAsync(string input, CancellationToken ct)` (returns an error message or null), `static bool IsComplete(PetStateStore, SecretStore)`.

- [ ] **Step 1: Write the failing tests**

Create `src/Companion.Tests/OnboardingTests.cs`:

```csharp
using Companion.Core;
using Xunit;

namespace Companion.Tests;

public class OnboardingTests
{
    private sealed class Rig : IDisposable
    {
        public TempDir Tmp { get; } = new();
        public PetStateStore Pet { get; }
        public SecretStore Secrets { get; }
        public Exception? ValidationError { get; set; }
        public List<string> ValidatedKeys { get; } = new();

        public Rig()
        {
            Pet = new PetStateStore(Tmp.PathOf("pet.json"), FakeClock.At(2026, 9, 24, 12), NullLog.Instance);
            Secrets = new SecretStore(Tmp.PathOf("secrets.bin"), new DpapiProtector(), NullLog.Instance);
        }

        public OnboardingFlow Flow() => new(Pet, Secrets, (key, _) =>
        {
            ValidatedKeys.Add(key);
            return ValidationError is null ? Task.CompletedTask : Task.FromException(ValidationError);
        }, "Ctrl+Alt+Space");

        public void Dispose() => Tmp.Dispose();
    }

    [Fact]
    public async Task Walks_through_names_and_key()
    {
        using var rig = new Rig();
        var flow = rig.Flow();
        Assert.Equal(OnboardingStep.PetName, flow.Step);
        Assert.False(OnboardingFlow.IsComplete(rig.Pet, rig.Secrets));

        Assert.Null(await flow.SubmitAsync(" Pip ", CancellationToken.None));
        Assert.Equal(OnboardingStep.UserName, flow.Step);
        Assert.Contains("Pip", flow.Prompt);

        Assert.Null(await flow.SubmitAsync("Karco", CancellationToken.None));
        Assert.Equal(OnboardingStep.ApiKey, flow.Step);
        Assert.Contains("console.anthropic.com", flow.Prompt);

        Assert.Null(await flow.SubmitAsync("sk-ant-good", CancellationToken.None));
        Assert.Equal(OnboardingStep.Done, flow.Step);
        Assert.Contains("Ctrl+Alt+Space", flow.Prompt);
        Assert.Equal("sk-ant-good", rig.Secrets.LoadApiKey());
        Assert.Equal("Pip", rig.Pet.Current.PetName);
        Assert.True(OnboardingFlow.IsComplete(rig.Pet, rig.Secrets));
    }

    [Theory]
    [InlineData("")]
    [InlineData("A name that is far too long")]
    public async Task Rejects_bad_names(string name)
    {
        using var rig = new Rig();
        var flow = rig.Flow();

        Assert.NotNull(await flow.SubmitAsync(name, CancellationToken.None));
        Assert.Equal(OnboardingStep.PetName, flow.Step);
    }

    [Fact]
    public async Task Rejects_keys_that_do_not_look_right_or_fail_validation()
    {
        using var rig = new Rig();
        rig.Pet.Current.PetName = "Pip";
        rig.Pet.Current.UserName = "Karco";
        var flow = rig.Flow();
        Assert.Equal(OnboardingStep.ApiKey, flow.Step);

        Assert.NotNull(await flow.SubmitAsync("hello", CancellationToken.None));
        Assert.Empty(rig.ValidatedKeys);

        rig.ValidationError = new ClaudeException(ClaudeErrorKind.Unauthorized, "401");
        Assert.Contains("didn't work", await flow.SubmitAsync("sk-ant-bad", CancellationToken.None));

        rig.ValidationError = new ClaudeException(ClaudeErrorKind.Network, "offline");
        Assert.Contains("Network", await flow.SubmitAsync("sk-ant-bad", CancellationToken.None));

        Assert.Equal(OnboardingStep.ApiKey, flow.Step);
        Assert.False(rig.Secrets.HasKey);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test src/Companion.Tests --filter "FullyQualifiedName~OnboardingTests"`
Expected: build FAILS — `OnboardingFlow` not found.

- [ ] **Step 3: Implement**

Create `src/Companion.Core/Onboarding.cs`:

```csharp
namespace Companion.Core;

public enum OnboardingStep { PetName, UserName, ApiKey, Done }

/// <summary>The hatch conversation: pet name → user name → API key. Resumes at the first missing piece.</summary>
public sealed class OnboardingFlow
{
    public const int MaxNameLength = 20;

    private readonly PetStateStore _pet;
    private readonly SecretStore _secrets;
    private readonly Func<string, CancellationToken, Task> _validateKey;
    private readonly string _hotkey;

    public OnboardingFlow(PetStateStore pet, SecretStore secrets, Func<string, CancellationToken, Task> validateKey, string hotkey)
    {
        _pet = pet;
        _secrets = secrets;
        _validateKey = validateKey;
        _hotkey = hotkey;
        Step = NextStep();
    }

    public OnboardingStep Step { get; private set; }

    public static bool IsComplete(PetStateStore pet, SecretStore secrets) =>
        pet.Current.PetName is not null && pet.Current.UserName is not null && secrets.HasKey;

    public string Prompt => Step switch
    {
        OnboardingStep.PetName => "*blinks* Hi! I just hatched. What should my name be?",
        OnboardingStep.UserName => $"**{_pet.Current.PetName}**! I love it. And what should I call you?",
        OnboardingStep.ApiKey =>
            $"Nice to meet you, {_pet.Current.UserName}! Last thing: I need an Anthropic API key to think.\n\n" +
            "1. Open **console.anthropic.com** → **API Keys**\n" +
            "2. Create a key and paste it here (it is stored encrypted on this PC only).",
        _ => $"All set! Double-click me or press **{_hotkey}** whenever you need me. *wags*",
    };

    public async Task<string?> SubmitAsync(string input, CancellationToken ct)
    {
        input = input.Trim();
        switch (Step)
        {
            case OnboardingStep.PetName:
            case OnboardingStep.UserName:
                if (input.Length is 0 or > MaxNameLength) return $"Please pick a name between 1 and {MaxNameLength} characters.";
                if (Step == OnboardingStep.PetName) _pet.Current.PetName = input;
                else _pet.Current.UserName = input;
                _pet.Save();
                break;

            case OnboardingStep.ApiKey:
                if (!input.StartsWith("sk-", StringComparison.Ordinal))
                    return "That doesn't look like an API key (they start with **sk-**). Try again?";
                try
                {
                    await _validateKey(input, ct);
                }
                catch (ClaudeException ex) when (ex.Kind == ClaudeErrorKind.Unauthorized)
                {
                    return "Hmm, that key didn't work. Double-check it and paste it again?";
                }
                catch (ClaudeException ex)
                {
                    return $"I couldn't check the key right now ({ex.Kind}). Try again in a moment?";
                }
                _secrets.SaveApiKey(input);
                break;

            default:
                return null;
        }

        Step = NextStep();
        return null;
    }

    private OnboardingStep NextStep() =>
        _pet.Current.PetName is null ? OnboardingStep.PetName
        : _pet.Current.UserName is null ? OnboardingStep.UserName
        : !_secrets.HasKey ? OnboardingStep.ApiKey
        : OnboardingStep.Done;
}
```

- [ ] **Step 4: Run all tests**

Run: `dotnet test src/Companion.Tests`
Expected: all tests pass (about 70).

- [ ] **Step 5: Commit**

```powershell
git add -A
git commit -m "feat(core): first-run onboarding flow" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 15: Placeholder pixel creature (SkinGen)

**Files:**
- Create: `tools/SkinGen/SkinGen.csproj`, `tools/SkinGen/Program.cs`
- Create (generated): `skins/default/sheet.png`
- Test: `src/Companion.Tests/DefaultSkinTests.cs`

**Interfaces:**
- Consumes: `skins/default/sheet.json` (Task 1), `SkinDefinition` (Task 5), `AppPaths` (Task 1).
- Produces: a 64×128 PNG, 32 frames of 16×16 in a 4-column grid. Frame map: 0–2 idle (breathe, blink), 4–7 walk, 8–10 happy with heart, 12–13 bored, 16–17 sleepy/yawn, 20–21 sleep with Z, 24–26 thinking dots, 28–29 talking, 30–31 egg wobble. Unused cells repeat a neighbour.

- [ ] **Step 1: Write the failing test**

Create `src/Companion.Tests/DefaultSkinTests.cs`:

```csharp
using System.Buffers.Binary;
using Companion.Core;
using Xunit;

namespace Companion.Tests;

public class DefaultSkinTests
{
    [Fact]
    public void Default_skin_png_matches_its_json()
    {
        var skinDir = Path.Combine(AppPaths.Resolve(AppContext.BaseDirectory).Skins, "default");
        var skin = SkinDefinition.Parse(File.ReadAllText(Path.Combine(skinDir, "sheet.json")));
        var png = File.ReadAllBytes(Path.Combine(skinDir, "sheet.png"));

        var width = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4));
        var height = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4));
        var frameCount = width / skin.FrameSize * (height / skin.FrameSize);

        Assert.Equal(64, width);
        Assert.Equal(128, height);
        Assert.All(skin.Animations.Values.SelectMany(a => a.Frames), f => Assert.InRange(f, 0, frameCount - 1));
        foreach (var name in new[] { "idle", "walk", "happy", "bored", "sleepy", "sleep", "thinking", "talking", "egg" })
            Assert.True(skin.Animations.ContainsKey(name), $"missing animation {name}");
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test src/Companion.Tests --filter "FullyQualifiedName~DefaultSkinTests"`
Expected: FAIL with `FileNotFoundException` for `sheet.png`.

- [ ] **Step 3: Create the generator**

Create `tools/SkinGen/SkinGen.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <UseWPF>true</UseWPF>
  </PropertyGroup>
</Project>
```

Create `tools/SkinGen/Program.cs`:

```csharp
using System.Windows.Media;
using System.Windows.Media.Imaging;

if (args.Length != 1)
{
    Console.Error.WriteLine("usage: SkinGen <output sheet.png>");
    return 1;
}

var frames = Frames.Build();
var output = Path.GetFullPath(args[0]);
Exception? failure = null;
var thread = new Thread(() =>
{
    try { Frames.WritePng(frames, output); }
    catch (Exception ex) { failure = ex; }
});
thread.SetApartmentState(ApartmentState.STA);
thread.Start();
thread.Join();
if (failure is not null)
{
    Console.Error.WriteLine(failure);
    return 1;
}
Console.WriteLine($"Wrote {frames.Length} frames to {output}");
return 0;

enum EyeShape { Open, Half, Closed, Happy }

enum Legs { Stand, StepA, StepB, Tucked }

/// <summary>16×16 character grid; '.' is transparent, other characters map to palette colours.</summary>
sealed class Grid
{
    public const int Size = 16;
    private readonly char[,] _cells = new char[Size, Size];

    public Grid()
    {
        for (var y = 0; y < Size; y++)
            for (var x = 0; x < Size; x++)
                _cells[y, x] = '.';
    }

    public char this[int x, int y] => _cells[y, x];

    public void Set(int x, int y, char c)
    {
        if (x is >= 0 and < Size && y is >= 0 and < Size) _cells[y, x] = c;
    }

    public void Row(int y, int fromX, int toX, char c)
    {
        for (var x = fromX; x <= toX; x++) Set(x, y, c);
    }

    public Grid Stamp(string[] art, int left, int top)
    {
        for (var y = 0; y < art.Length; y++)
            for (var x = 0; x < art[y].Length; x++)
                if (art[y][x] != '.') Set(left + x, top + y, art[y][x]);
        return this;
    }

    public Grid Shifted(int dx)
    {
        var copy = new Grid();
        for (var y = 0; y < Size; y++)
            for (var x = 0; x < Size; x++)
                if (_cells[y, x] != '.') copy.Set(x + dx, y, _cells[y, x]);
        return copy;
    }
}

static class Frames
{
    private static readonly string[] Heart = [".P.P.", "PPPPP", ".PPP.", "..P.."];
    private static readonly string[] Z = ["ZZZ", ".Z.", "ZZZ"];
    private static readonly string[] Egg =
    [
        "................",
        "................",
        "................",
        "................",
        "................",
        "......EEEE......",
        ".....EEEEEE.....",
        "....EESEEEEE....",
        "....EEEEESEE....",
        "...EEEEEEEEEE...",
        "...EESEEEEEEE...",
        "...EEEEEESEEE...",
        "...EEEEEEEEEE...",
        "....EEESEEEE....",
        ".....EEEEEE.....",
        "......EEEE......",
    ];

    // ARGB. Body colour matches the reference critter's orange.
    private static readonly Dictionary<char, uint> Palette = new()
    {
        ['O'] = 0xFFD97757, // body
        ['D'] = 0xFFB25C40, // belly shade
        ['K'] = 0xFF1E1A1A, // eyes
        ['M'] = 0xFF6B2E1F, // mouth
        ['P'] = 0xFFFF5C8A, // heart
        ['Z'] = 0xFF8EC5FF, // sleep Z
        ['G'] = 0xFFBFBFBF, // thinking dots
        ['E'] = 0xFFF4E9D0, // egg shell
        ['S'] = 0xFFD8B98A, // egg spots
    };

    public static Grid[] Build()
    {
        var f = new Grid[32];
        f[0] = Creature(0, EyeShape.Open, 0, Legs.Stand);
        f[1] = Creature(1, EyeShape.Open, 0, Legs.Stand);
        f[2] = Creature(0, EyeShape.Closed, 0, Legs.Stand);
        f[3] = f[0];
        f[4] = Creature(0, EyeShape.Open, 1, Legs.StepA);
        f[5] = Creature(1, EyeShape.Open, 1, Legs.Stand);
        f[6] = Creature(0, EyeShape.Open, 1, Legs.StepB);
        f[7] = f[5];
        f[8] = Creature(0, EyeShape.Happy, 0, Legs.Stand).Stamp(Heart, 11, 0);
        f[9] = Creature(1, EyeShape.Happy, 0, Legs.Stand).Stamp(Heart, 11, 1);
        f[10] = Creature(-1, EyeShape.Happy, 0, Legs.Stand).Stamp(Heart, 11, 0);
        f[11] = f[8];
        f[12] = Creature(1, EyeShape.Half, -1, Legs.Stand);
        f[13] = Creature(1, EyeShape.Half, 1, Legs.Stand);
        f[14] = f[12];
        f[15] = f[13];
        f[16] = Creature(1, EyeShape.Half, 0, Legs.Stand);
        f[17] = Creature(1, EyeShape.Closed, 0, Legs.Stand, mouth: true);
        f[18] = f[16];
        f[19] = f[17];
        f[20] = Creature(0, EyeShape.Closed, 0, Legs.Tucked).Stamp(Z, 12, 3);
        f[21] = Creature(0, EyeShape.Closed, 0, Legs.Tucked).Stamp(Z, 12, 3).Stamp(Z, 13, 0);
        f[22] = f[20];
        f[23] = f[21];
        f[24] = Creature(0, EyeShape.Open, 1, Legs.Stand).Stamp(["G"], 6, 2);
        f[25] = Creature(0, EyeShape.Open, 1, Legs.Stand).Stamp(["G.G"], 6, 2);
        f[26] = Creature(0, EyeShape.Open, 1, Legs.Stand).Stamp(["G.G.G"], 6, 2);
        f[27] = f[24];
        f[28] = Creature(0, EyeShape.Open, 0, Legs.Stand, mouth: true);
        f[29] = f[0];
        f[30] = new Grid().Stamp(Egg, 0, 0);
        f[31] = f[30].Shifted(1);
        return f;
    }

    /// <param name="bob">Vertical offset of the body: -1 jump, 0 normal, 1 squat.</param>
    /// <param name="look">Horizontal eye offset: -1 left, 0 centre, 1 right.</param>
    private static Grid Creature(int bob, EyeShape eyes, int look, Legs legs, bool mouth = false)
    {
        var g = new Grid();
        var top = legs == Legs.Tucked ? 7 : 5 + bob;
        for (var y = top; y <= top + 3; y++) g.Row(y, 3, 12, 'O');
        g.Row(top + 4, 1, 14, 'O');
        g.Row(top + 5, 1, 14, 'O');
        g.Row(top + 6, 3, 12, 'O');
        g.Row(top + 7, 3, 12, 'D');

        var eyeY = top + 2;
        foreach (var eyeX in new[] { 5 + look, 10 + look })
        {
            switch (eyes)
            {
                case EyeShape.Open:
                    g.Set(eyeX, eyeY, 'K');
                    g.Set(eyeX, eyeY + 1, 'K');
                    break;
                case EyeShape.Half:
                    g.Set(eyeX, eyeY + 1, 'K');
                    break;
                case EyeShape.Closed:
                    g.Set(eyeX - 1, eyeY + 1, 'K');
                    g.Set(eyeX, eyeY + 1, 'K');
                    break;
                case EyeShape.Happy:
                    g.Set(eyeX, eyeY, 'K');
                    g.Set(eyeX - 1, eyeY + 1, 'K');
                    g.Set(eyeX + 1, eyeY + 1, 'K');
                    break;
            }
        }

        if (mouth)
        {
            g.Row(top + 4, 7, 8, 'M');
            g.Row(top + 5, 7, 8, 'M');
        }

        int[] legColumns = legs switch
        {
            Legs.Stand => [3, 5, 10, 12],
            Legs.StepA => [2, 5, 10, 13],
            Legs.StepB => [4, 6, 9, 11],
            _ => [],
        };
        for (var y = top + 8; y < Grid.Size; y++)
            foreach (var x in legColumns)
                g.Set(x, y, 'O');
        return g;
    }

    public static void WritePng(Grid[] frames, string path)
    {
        const int columns = 4;
        var rows = (frames.Length + columns - 1) / columns;
        var width = columns * Grid.Size;
        var height = rows * Grid.Size;
        var pixels = new uint[width * height];
        for (var i = 0; i < frames.Length; i++)
        {
            var originX = i % columns * Grid.Size;
            var originY = i / columns * Grid.Size;
            for (var y = 0; y < Grid.Size; y++)
                for (var x = 0; x < Grid.Size; x++)
                    if (Palette.TryGetValue(frames[i][x, y], out var argb))
                        pixels[(originY + y) * width + originX + x] = argb;
        }

        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
```

- [ ] **Step 4: Generate the sheet**

```powershell
dotnet sln add tools/SkinGen/SkinGen.csproj
dotnet run --project tools/SkinGen -- skins/default/sheet.png
```

Expected: `Wrote 32 frames to D:\Claude\Companion\skins\default\sheet.png`.

Look at the result: open the PNG with the Read tool (it renders images). You should see a 4×8 grid of small orange creatures (breathing, walking, a heart, Zs, dots) and two cream eggs at the bottom right. If a frame is clearly broken (for example eyes outside the body), fix the art in `Frames.Build`/`Creature` and regenerate.

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test src/Companion.Tests --filter "FullyQualifiedName~DefaultSkinTests"`
Expected: 1 passed.

- [ ] **Step 6: Commit**

```powershell
git add -A
git commit -m "feat(skin): generated placeholder pixel creature sprite sheet" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 16: Windows integration services

**Files:**
- Create: `src/Companion/NativeMethods.cs`, `src/Companion/UserActivity.cs`, `src/Companion/HotkeyService.cs`, `src/Companion/Autostart.cs`
- Modify: `src/Companion.Tests/Companion.Tests.csproj` (reference the app)
- Test: `src/Companion.Tests/HotkeyTests.cs`

**Interfaces:**
- Consumes: `IUserActivity`, `ActivitySnapshot` (Task 7).
- Produces: internal `NativeMethods`, `WindowStyles.MakeToolWindow(Window, bool clickThrough)`, `MemoryTrim.Now()`; public `UserActivity : IUserActivity, IDisposable`; `HotkeyService : IDisposable` with `event Action? Pressed`, `bool Register(Window window, string hotkey)`, `static bool TryParse(string text, out uint modifiers, out uint virtualKey)`; `static class Autostart` with `Apply(bool enabled, string exePath)`.

- [ ] **Step 1: Reference the app from the tests**

Add to the `<ItemGroup>` with the Core reference in `src/Companion.Tests/Companion.Tests.csproj`:

```xml
    <ProjectReference Include="..\Companion\Companion.csproj" />
```

- [ ] **Step 2: Write the failing test**

Create `src/Companion.Tests/HotkeyTests.cs`:

```csharp
using Companion;
using Xunit;

namespace Companion.Tests;

public class HotkeyTests
{
    [Theory]
    [InlineData("Ctrl+Alt+Space", 0x3u, 0x20u)]
    [InlineData(" alt + f1 ", 0x1u, 0x70u)]
    [InlineData("Win+Shift+K", 0xCu, 0x4Bu)]
    public void Parses_valid_hotkeys(string text, uint modifiers, uint virtualKey)
    {
        Assert.True(HotkeyService.TryParse(text, out var mods, out var vk));
        Assert.Equal(modifiers, mods);
        Assert.Equal(virtualKey, vk);
    }

    [Theory]
    [InlineData("Space")]
    [InlineData("Ctrl+Nope")]
    [InlineData("Ctrl+A+B")]
    [InlineData("Ctrl+5")]
    [InlineData("")]
    public void Rejects_invalid_hotkeys(string text)
    {
        Assert.False(HotkeyService.TryParse(text, out _, out _));
    }
}
```

- [ ] **Step 3: Run the test to verify it fails**

Run: `dotnet test src/Companion.Tests --filter "FullyQualifiedName~HotkeyTests"`
Expected: build FAILS — `HotkeyService` not found.

- [ ] **Step 4: Implement the services**

Create `src/Companion/NativeMethods.cs`:

```csharp
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Companion;

internal static class NativeMethods
{
    internal const int GWL_EXSTYLE = -20;
    internal const int WS_EX_TRANSPARENT = 0x00000020;
    internal const int WS_EX_TOOLWINDOW = 0x00000080;
    internal const int WS_EX_NOACTIVATE = 0x08000000;
    internal const int WM_HOTKEY = 0x0312;
    internal const uint MOD_ALT = 0x1;
    internal const uint MOD_CONTROL = 0x2;
    internal const uint MOD_SHIFT = 0x4;
    internal const uint MOD_WIN = 0x8;
    internal const uint MOD_NOREPEAT = 0x4000;
    internal const int QUNS_BUSY = 2;
    internal const int QUNS_RUNNING_D3D_FULL_SCREEN = 3;
    internal const int QUNS_PRESENTATION_MODE = 4;

    [StructLayout(LayoutKind.Sequential)]
    internal struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll")]
    internal static extern bool GetLastInputInfo(ref LASTINPUTINFO info);

    [DllImport("shell32.dll")]
    internal static extern int SHQueryUserNotificationState(out int state);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll")]
    internal static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    internal static extern int GetWindowLong(IntPtr hWnd, int index);

    [DllImport("user32.dll")]
    internal static extern int SetWindowLong(IntPtr hWnd, int index, int value);

    [DllImport("psapi.dll")]
    internal static extern bool EmptyWorkingSet(IntPtr process);
}

internal static class WindowStyles
{
    /// <summary>Hides the window from Alt+Tab, stops it stealing focus, and optionally lets clicks pass through.</summary>
    public static void MakeToolWindow(Window window, bool clickThrough)
    {
        var handle = new WindowInteropHelper(window).Handle;
        var style = NativeMethods.GetWindowLong(handle, NativeMethods.GWL_EXSTYLE)
                    | NativeMethods.WS_EX_TOOLWINDOW
                    | NativeMethods.WS_EX_NOACTIVATE;
        if (clickThrough) style |= NativeMethods.WS_EX_TRANSPARENT;
        NativeMethods.SetWindowLong(handle, NativeMethods.GWL_EXSTYLE, style);
    }
}

internal static class MemoryTrim
{
    /// <summary>Hands idle pages back to Windows; they are paged back in on demand.</summary>
    public static void Now()
    {
        using var process = Process.GetCurrentProcess();
        NativeMethods.EmptyWorkingSet(process.Handle);
    }
}
```

Create `src/Companion/UserActivity.cs`:

```csharp
using System.Runtime.InteropServices;
using Companion.Core;
using Microsoft.Win32;

namespace Companion;

/// <summary>Idle time from GetLastInputInfo; "suspended" when a fullscreen app is in front or the session is locked.</summary>
public sealed class UserActivity : IUserActivity, IDisposable
{
    private volatile bool _locked;

    public UserActivity() => SystemEvents.SessionSwitch += OnSessionSwitch;

    public ActivitySnapshot Sample() => new(IdleTime(), _locked || FullscreenAppInFront());

    public void Dispose() => SystemEvents.SessionSwitch -= OnSessionSwitch;

    private static TimeSpan IdleTime()
    {
        var info = new NativeMethods.LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<NativeMethods.LASTINPUTINFO>() };
        if (!NativeMethods.GetLastInputInfo(ref info)) return TimeSpan.Zero;
        var idleMs = unchecked((uint)Environment.TickCount - info.dwTime);
        return TimeSpan.FromMilliseconds(idleMs);
    }

    private static bool FullscreenAppInFront() =>
        NativeMethods.SHQueryUserNotificationState(out var state) == 0
        && state is NativeMethods.QUNS_BUSY or NativeMethods.QUNS_RUNNING_D3D_FULL_SCREEN or NativeMethods.QUNS_PRESENTATION_MODE;

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason == SessionSwitchReason.SessionLock) _locked = true;
        else if (e.Reason == SessionSwitchReason.SessionUnlock) _locked = false;
    }
}
```

Create `src/Companion/HotkeyService.cs`:

```csharp
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace Companion;

public sealed class HotkeyService : IDisposable
{
    private const int HotkeyId = 0xC0DE;
    private HwndSource? _source;
    private bool _registered;

    public event Action? Pressed;

    /// <summary>Registers (or re-registers) the global hotkey on the given window's handle.</summary>
    public bool Register(Window window, string hotkey)
    {
        Unregister();
        if (!TryParse(hotkey, out var modifiers, out var virtualKey)) return false;
        if (_source is null)
        {
            _source = HwndSource.FromHwnd(new WindowInteropHelper(window).EnsureHandle());
            _source.AddHook(WndProc);
        }
        _registered = NativeMethods.RegisterHotKey(_source.Handle, HotkeyId, modifiers | NativeMethods.MOD_NOREPEAT, virtualKey);
        return _registered;
    }

    /// <summary>Parses "Ctrl+Alt+Space": at least one modifier plus exactly one non-digit key.</summary>
    public static bool TryParse(string text, out uint modifiers, out uint virtualKey)
    {
        modifiers = 0;
        virtualKey = 0;
        Key? key = null;
        foreach (var part in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl" or "control": modifiers |= NativeMethods.MOD_CONTROL; break;
                case "alt": modifiers |= NativeMethods.MOD_ALT; break;
                case "shift": modifiers |= NativeMethods.MOD_SHIFT; break;
                case "win": modifiers |= NativeMethods.MOD_WIN; break;
                default:
                    if (key is not null || int.TryParse(part, out _) || !Enum.TryParse(part, ignoreCase: true, out Key parsed)) return false;
                    key = parsed;
                    break;
            }
        }
        if (key is null || modifiers == 0) return false;
        virtualKey = (uint)KeyInterop.VirtualKeyFromKey(key.Value);
        return virtualKey != 0;
    }

    public void Dispose()
    {
        Unregister();
        _source?.RemoveHook(WndProc);
        _source = null;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            Pressed?.Invoke();
            handled = true;
        }
        return IntPtr.Zero;
    }

    private void Unregister()
    {
        if (_registered && _source is not null) NativeMethods.UnregisterHotKey(_source.Handle, HotkeyId);
        _registered = false;
    }
}
```

Create `src/Companion/Autostart.cs`:

```csharp
using Microsoft.Win32;

namespace Companion;

/// <summary>User-level "start with Windows" entry. Only touched when the user changes the setting.</summary>
public static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Companion";

    public static void Apply(bool enabled, string exePath)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        if (enabled) key.SetValue(ValueName, $"\"{exePath}\"");
        else key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test src/Companion.Tests --filter "FullyQualifiedName~HotkeyTests"`
Expected: 8 passed.

- [ ] **Step 6: Commit**

```powershell
git add -A
git commit -m "feat(app): idle/fullscreen detection, global hotkey and autostart helpers" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 17: The pet on the taskbar

**Files:**
- Delete: `src/Companion/MainWindow.xaml`, `src/Companion/MainWindow.xaml.cs`
- Replace: `src/Companion/App.xaml`, `src/Companion/App.xaml.cs`
- Create: `src/Companion/SpriteSheet.cs`, `src/Companion/PetWindow.xaml`, `src/Companion/PetWindow.xaml.cs`, `src/Companion/LineBubble.xaml`, `src/Companion/LineBubble.xaml.cs`, `src/Companion/TrayIcon.cs`, `src/Companion/PetController.cs`, `src/Companion/AppHost.cs`

**Interfaces:**
- Consumes: `PetLife`, `PetStateStore`, `SettingsStore`, `SpendTracker`, `SkinDefinition`, `AnimationPlayer`, `AppPaths`, `FileLog`; `UserActivity`, `WindowStyles`, `MemoryTrim` (Task 16).
- Produces: `SpriteSheet` (`Load(string dir)`, `Fallback()`, `Definition`, `Frame(int)`); `PetWindow` (`Clicked`, `DoubleClicked`, `Dragged(double left)`, `SetFrame`, `SetSize`, `SetFacing`); `LineBubble.Say(string text, Rect anchor)`; `TrayIcon(BitmapSource)` with events `ChatClicked`, `ToggleClicked`, `SleepClicked`, `SettingsClicked`, `QuitClicked` and `SetTooltip(string)`; `PetController(PetWindow, PetLife, SpriteSheet, IUserActivity, SettingsStore, PetStateStore)` with `Life`, `Window`, `Bounds`, `Start()`, `Refresh()`, `ApplyScale()`, `HideFor(TimeSpan)`, `ToggleVisible()`, events `ChatRequested`, `EggClicked`, `SettingsRequested`, `QuitRequested`, `LineReady(string)`; `AppHost(AppPaths, IClock, ILog)` with `Start()`, `event Action? QuitRequested`, `Dispose()`; `App.Log`.

No unit tests: this task is pure UI glue over tested logic. Verification is running the app.

- [ ] **Step 1: Replace the application shell**

```powershell
Remove-Item src/Companion/MainWindow.xaml, src/Companion/MainWindow.xaml.cs
```

Replace `src/Companion/App.xaml`:

```xml
<Application x:Class="Companion.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             ShutdownMode="OnExplicitShutdown">
    <Application.Resources>
        <SolidColorBrush x:Key="BubbleBackground" Color="#FF1F1F1F" />
        <SolidColorBrush x:Key="BubbleBorder" Color="#FF3A3A3A" />
        <SolidColorBrush x:Key="BubbleText" Color="#FFECECEC" />
        <SolidColorBrush x:Key="BubbleMuted" Color="#FF9A9A9A" />
        <SolidColorBrush x:Key="Accent" Color="#FFD97757" />
        <SolidColorBrush x:Key="CodeBackground" Color="#FF141414" />
        <Style x:Key="BubbleButton" TargetType="Button">
            <Setter Property="Foreground" Value="{StaticResource BubbleText}" />
            <Setter Property="Background" Value="#FF2C2C2C" />
            <Setter Property="BorderThickness" Value="0" />
            <Setter Property="Padding" Value="8,3" />
            <Setter Property="Cursor" Value="Hand" />
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="Button">
                        <Border x:Name="Bd" Background="{TemplateBinding Background}" CornerRadius="6" Padding="{TemplateBinding Padding}">
                            <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center" />
                        </Border>
                        <ControlTemplate.Triggers>
                            <Trigger Property="IsMouseOver" Value="True">
                                <Setter TargetName="Bd" Property="Background" Value="#FF3A3A3A" />
                            </Trigger>
                            <Trigger Property="IsEnabled" Value="False">
                                <Setter Property="Opacity" Value="0.4" />
                            </Trigger>
                        </ControlTemplate.Triggers>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>
    </Application.Resources>
</Application>
```

Replace `src/Companion/App.xaml.cs`:

```csharp
using System.Windows;
using Companion.Core;

namespace Companion;

public partial class App : Application
{
    private Mutex? _singleInstance;
    private AppHost? _host;

    public static ILog Log { get; private set; } = NullLog.Instance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _singleInstance = new Mutex(initiallyOwned: true, @"Local\Companion.Pet.SingleInstance", out var isFirst);
        if (!isFirst)
        {
            Shutdown();
            return;
        }

        var paths = AppPaths.Resolve(AppContext.BaseDirectory, Environment.GetEnvironmentVariable("COMPANION_ROOT"));
        paths.EnsureCreated();
        var clock = new SystemClock();
        var log = new FileLog(paths.Logs, clock);
        Log = log;
        log.Info($"Starting. Root: {paths.Root}");
        DispatcherUnhandledException += (_, args) =>
        {
            log.Error("Unhandled UI exception.", args.Exception);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) => log.Error("Unhandled exception.", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            log.Error("Unobserved task exception.", args.Exception);
            args.SetObserved();
        };

        _host = new AppHost(paths, clock, log);
        _host.QuitRequested += Shutdown;
        _host.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _host?.Dispose();
        _singleInstance?.Dispose();
        Log.Info("Exited.");
        base.OnExit(e);
    }
}
```

- [ ] **Step 2: Sprite sheet loader**

Create `src/Companion/SpriteSheet.cs`:

```csharp
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Companion.Core;

namespace Companion;

public sealed class SpriteSheet
{
    private SpriteSheet(SkinDefinition definition, IReadOnlyList<BitmapSource> frames)
    {
        Definition = definition;
        Frames = frames;
    }

    public SkinDefinition Definition { get; }

    public IReadOnlyList<BitmapSource> Frames { get; }

    public BitmapSource Frame(int index) => Frames[((index % Frames.Count) + Frames.Count) % Frames.Count];

    public static SpriteSheet Load(string skinDirectory)
    {
        var definition = SkinDefinition.Parse(File.ReadAllText(Path.Combine(skinDirectory, "sheet.json")));
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = new Uri(Path.GetFullPath(Path.Combine(skinDirectory, "sheet.png")));
        image.EndInit();
        image.Freeze();

        var size = definition.FrameSize;
        var count = image.PixelWidth / size * (image.PixelHeight / size);
        if (count == 0) throw new FormatException("sheet.png is smaller than one frame.");
        var frames = new List<BitmapSource>(count);
        for (var i = 0; i < count; i++)
        {
            var (x, y) = SkinDefinition.FrameOrigin(i, size, image.PixelWidth);
            var frame = new CroppedBitmap(image, new Int32Rect(x, y, size, size));
            frame.Freeze();
            frames.Add(frame);
        }
        return new SpriteSheet(definition, frames);
    }

    /// <summary>A plain orange block, used when the skin is missing or broken.</summary>
    public static SpriteSheet Fallback()
    {
        const int size = 16;
        var pixels = new uint[size * size];
        for (var y = 4; y < 14; y++)
            for (var x = 3; x < 13; x++)
                pixels[y * size + x] = 0xFFD97757;
        var bitmap = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, pixels, size * 4);
        bitmap.Freeze();
        var definition = SkinDefinition.Parse("""{ "frameSize": 16, "animations": { "idle": { "fps": 1, "loop": true, "frames": [0] } } }""");
        return new SpriteSheet(definition, [bitmap]);
    }
}
```

- [ ] **Step 3: Pet window**

Create `src/Companion/PetWindow.xaml`:

```xml
<Window x:Class="Companion.PetWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Companion" WindowStyle="None" AllowsTransparency="True" Background="Transparent"
        Topmost="True" ShowInTaskbar="False" ShowActivated="False" ResizeMode="NoResize">
    <Image x:Name="Sprite" Stretch="Fill" RenderOptions.BitmapScalingMode="NearestNeighbor" RenderTransformOrigin="0.5,0.5">
        <Image.RenderTransform>
            <ScaleTransform x:Name="Flip" ScaleX="1" />
        </Image.RenderTransform>
    </Image>
</Window>
```

Create `src/Companion/PetWindow.xaml.cs`:

```csharp
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Companion;

/// <summary>Transparent, borderless, non-activating sprite window. Fully transparent pixels let clicks through.</summary>
public partial class PetWindow : Window
{
    private Point _pressPoint;
    private double _pressLeft;
    private bool _pressed;
    private bool _dragging;

    public PetWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => WindowStyles.MakeToolWindow(this, clickThrough: false);
    }

    public event Action? Clicked;
    public event Action? DoubleClicked;
    public event Action<double>? Dragged;

    public void SetFrame(ImageSource frame) => Sprite.Source = frame;

    public void SetSize(double size)
    {
        Width = size;
        Height = size;
    }

    public void SetFacing(int direction) => Flip.ScaleX = direction < 0 ? -1 : 1;

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
        _pressLeft = Left;
        CaptureMouse();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!_pressed || e.LeftButton != MouseButtonState.Pressed) return;
        var dx = ScreenPoint(e).X - _pressPoint.X;
        if (!_dragging && Math.Abs(dx) < 4) return;
        _dragging = true;
        var area = SystemParameters.WorkArea;
        Left = Math.Clamp(_pressLeft + dx, area.Left, Math.Max(area.Left, area.Right - Width));
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (!_pressed) return;
        e.Handled = true;
        _pressed = false;
        ReleaseMouseCapture();
        if (_dragging) Dragged?.Invoke(Left);
        else Clicked?.Invoke();
    }

    /// <summary>Mouse position in screen DIPs (same units as Left/Top).</summary>
    private Point ScreenPoint(MouseEventArgs e)
    {
        var device = PointToScreen(e.GetPosition(this));
        return PresentationSource.FromVisual(this)?.CompositionTarget is { } target
            ? target.TransformFromDevice.Transform(device)
            : device;
    }
}
```

- [ ] **Step 4: Mini line bubble**

Create `src/Companion/LineBubble.xaml`:

```xml
<Window x:Class="Companion.LineBubble"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Companion line" WindowStyle="None" AllowsTransparency="True" Background="Transparent"
        Topmost="True" ShowInTaskbar="False" ShowActivated="False" ResizeMode="NoResize"
        SizeToContent="WidthAndHeight" IsHitTestVisible="False">
    <Border Margin="6" Padding="10,6" CornerRadius="10" MaxWidth="240"
            Background="{StaticResource BubbleBackground}" BorderBrush="{StaticResource BubbleBorder}" BorderThickness="1">
        <TextBlock x:Name="LineText" Foreground="{StaticResource BubbleText}" TextWrapping="Wrap" FontFamily="Segoe UI" FontSize="12" />
    </Border>
</Window>
```

Create `src/Companion/LineBubble.xaml.cs`:

```csharp
using System.Windows;
using System.Windows.Threading;

namespace Companion;

/// <summary>Click-through speech bubble that disappears after 4 seconds.</summary>
public partial class LineBubble : Window
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(4) };

    public LineBubble()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => WindowStyles.MakeToolWindow(this, clickThrough: true);
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            Hide();
        };
    }

    public void Say(string text, Rect anchor)
    {
        LineText.Text = text;
        Show();
        UpdateLayout();
        var area = SystemParameters.WorkArea;
        Left = Math.Clamp(anchor.Left + anchor.Width / 2 - ActualWidth / 2, area.Left, Math.Max(area.Left, area.Right - ActualWidth));
        Top = Math.Max(area.Top, anchor.Top - ActualHeight);
        _timer.Stop();
        _timer.Start();
    }
}
```

- [ ] **Step 5: Tray icon**

Create `src/Companion/TrayIcon.cs`:

```csharp
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace Companion;

public sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Drawing.Icon _image;

    public TrayIcon(BitmapSource frame)
    {
        _image = ToIcon(frame);
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Chat", null, (_, _) => ChatClicked?.Invoke());
        menu.Items.Add("Show / hide", null, (_, _) => ToggleClicked?.Invoke());
        menu.Items.Add("Sleep", null, (_, _) => SleepClicked?.Invoke());
        menu.Items.Add("Settings", null, (_, _) => SettingsClicked?.Invoke());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => QuitClicked?.Invoke());
        _icon = new Forms.NotifyIcon { Icon = _image, Text = "Companion", Visible = true, ContextMenuStrip = menu };
        _icon.DoubleClick += (_, _) => ChatClicked?.Invoke();
    }

    public event Action? ChatClicked;
    public event Action? ToggleClicked;
    public event Action? SleepClicked;
    public event Action? SettingsClicked;
    public event Action? QuitClicked;

    public void SetTooltip(string text) => _icon.Text = text.Length <= 127 ? text : text[..127];

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _image.Dispose();
    }

    /// <summary>Nearest-neighbour upscale of a sprite frame to a crisp 32×32 icon.</summary>
    private static Drawing.Icon ToIcon(BitmapSource frame)
    {
        const int size = 32;
        var source = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        var width = source.PixelWidth;
        var height = source.PixelHeight;
        var pixels = new byte[width * height * 4];
        source.CopyPixels(pixels, width * 4, 0);
        using var bitmap = new Drawing.Bitmap(size, size, Drawing.Imaging.PixelFormat.Format32bppArgb);
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var i = ((y * height / size) * width + x * width / size) * 4;
                bitmap.SetPixel(x, y, Drawing.Color.FromArgb(pixels[i + 3], pixels[i + 2], pixels[i + 1], pixels[i]));
            }
        }
        return Drawing.Icon.FromHandle(bitmap.GetHicon());
    }
}
```

- [ ] **Step 6: Pet controller**

Create `src/Companion/PetController.cs`:

```csharp
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Companion.Core;

namespace Companion;

/// <summary>Drives PetLife once per second, plays animations only when the frame changes, walks along the taskbar.</summary>
public sealed class PetController : IDisposable
{
    private const double WalkSpeedDipPerSecond = 24;

    private readonly PetWindow _window;
    private readonly PetLife _life;
    private readonly SpriteSheet _sheet;
    private readonly AnimationPlayer _player;
    private readonly IUserActivity _activity;
    private readonly SettingsStore _settings;
    private readonly PetStateStore _petStore;
    private readonly DispatcherTimer _lifeTimer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _animationTimer = new(DispatcherPriority.Render);
    private readonly Stopwatch _time = Stopwatch.StartNew();
    private TimeSpan _lastAnimationTick;
    private int _shownFrame = -1;
    private bool _suspended;
    private DateTime _hiddenUntil = DateTime.MinValue;
    private long _ticks;

    public PetController(PetWindow window, PetLife life, SpriteSheet sheet, IUserActivity activity, SettingsStore settings, PetStateStore petStore)
    {
        _window = window;
        _life = life;
        _sheet = sheet;
        _activity = activity;
        _settings = settings;
        _petStore = petStore;
        _player = new AnimationPlayer(sheet.Definition);

        _lifeTimer.Tick += (_, _) => Refresh();
        _animationTimer.Tick += (_, _) => OnAnimationTick();
        _window.Clicked += OnClicked;
        _window.DoubleClicked += () => ChatRequested?.Invoke();
        _window.Dragged += left => _petStore.Current.PositionX = left;
        _window.ContextMenu = BuildMenu();
        ApplyScale();
    }

    public event Action? ChatRequested;
    public event Action? EggClicked;
    public event Action? SettingsRequested;
    public event Action? QuitRequested;
    public event Action<string>? LineReady;

    public PetLife Life => _life;

    public Window Window => _window;

    public Rect Bounds => new(_window.Left, _window.Top, _window.Width, _window.Height);

    public void Start()
    {
        _window.Show();
        Place();
        Refresh();
        _lifeTimer.Start();
    }

    /// <summary>Re-evaluates the pet now (called every second and right after user interactions).</summary>
    public void Refresh()
    {
        _ticks++;
        var tick = _life.Tick(_activity.Sample());
        var hide = tick.Suspended || DateTime.Now < _hiddenUntil;
        if (hide != _suspended) SetSuspended(hide);
        if (_ticks % 60 == 0) _petStore.Save();
        if (hide) return;

        Play(AnimationFor(tick.State));
        _window.SetFacing(tick.State == PetStateKind.Walk ? _life.WalkDirection : 1);
        if (_ticks % 5 == 0)
        {
            _window.Topmost = false;   // re-assert z-order; the taskbar sometimes covers topmost windows
            _window.Topmost = true;
            Place();
        }
        if (tick.Line is { } line) LineReady?.Invoke(line);
    }

    public void ApplyScale()
    {
        _window.SetSize(_sheet.Definition.FrameSize * _settings.Current.PetScale);
        if (_window.IsVisible) Place();
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

    public void Dispose()
    {
        _lifeTimer.Stop();
        _animationTimer.Stop();
        _petStore.Save();
        _window.Close();
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
            Place();
        }
    }

    private void Play(string animation)
    {
        if (_player.Current == animation && _animationTimer.IsEnabled) return;
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
        if (_life.State == PetStateKind.Walk) Walk(now - _lastAnimationTick);
        _lastAnimationTick = now;
    }

    private void Walk(TimeSpan elapsed)
    {
        var area = SystemParameters.WorkArea;
        var max = Math.Max(area.Left, area.Right - _window.Width);
        var x = _window.Left + _life.WalkDirection * WalkSpeedDipPerSecond * elapsed.TotalSeconds;
        if (x <= area.Left || x >= max)
        {
            x = Math.Clamp(x, area.Left, max);
            _life.ReverseWalk();
            _window.SetFacing(_life.WalkDirection);
        }
        _window.Left = x;
        _petStore.Current.PositionX = x;
    }

    /// <summary>Sits on top of the taskbar of the primary monitor, at the remembered x position.</summary>
    private void Place()
    {
        var area = SystemParameters.WorkArea;
        _window.Top = area.Bottom - _window.Height;
        var x = _petStore.Current.PositionX ?? area.Right - _window.Width - 160;
        _window.Left = Math.Clamp(x, area.Left, Math.Max(area.Left, area.Right - _window.Width));
    }

    private ContextMenu BuildMenu()
    {
        var menu = new ContextMenu();
        menu.Items.Add(Item("Chat", () => ChatRequested?.Invoke()));
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

    private static string AnimationFor(PetStateKind state) => state switch
    {
        PetStateKind.Egg => "egg",
        PetStateKind.Walk => "walk",
        PetStateKind.Happy => "happy",
        PetStateKind.Bored => "bored",
        PetStateKind.Sleepy => "sleepy",
        PetStateKind.Sleep => "sleep",
        PetStateKind.Thinking => "thinking",
        PetStateKind.Talking => "talking",
        _ => "idle",
    };
}
```

- [ ] **Step 7: Composition root (first version)**

Create `src/Companion/AppHost.cs`:

```csharp
using System.Windows.Threading;
using Companion.Core;

namespace Companion;

/// <summary>Builds and wires everything. Chat arrives in Task 18, settings and onboarding in Task 19.</summary>
public sealed class AppHost : IDisposable
{
    private readonly ILog _log;
    private readonly PetStateStore _petStore;
    private readonly SpendTracker _spend;
    private readonly UserActivity _activity = new();
    private readonly PetController _pet;
    private readonly TrayIcon _tray;
    private readonly LineBubble _line = new();
    private readonly DispatcherTimer _tooltipTimer = new() { Interval = TimeSpan.FromSeconds(30) };

    public AppHost(AppPaths paths, IClock clock, ILog log)
    {
        _log = log;
        var settings = new SettingsStore(paths.SettingsFile, clock, log);
        _petStore = new PetStateStore(paths.PetFile, clock, log);
        _spend = new SpendTracker(settings, clock);
        var sheet = LoadSheet(Path.Combine(paths.Skins, "default"));
        _pet = new PetController(new PetWindow(), new PetLife(_petStore.Current, clock, new Random()), sheet, _activity, settings, _petStore);
        _tray = new TrayIcon(sheet.Frame(0));

        _pet.EggClicked += Hatch;
        _pet.ChatRequested += OnChatRequested;
        _pet.SettingsRequested += OpenSettings;
        _pet.QuitRequested += () => QuitRequested?.Invoke();
        _pet.LineReady += text => _line.Say(text, _pet.Bounds);
        _tray.ChatClicked += OnChatRequested;
        _tray.ToggleClicked += _pet.ToggleVisible;
        _tray.SleepClicked += () => { _pet.Life.ForceSleep(); _pet.Refresh(); };
        _tray.SettingsClicked += OpenSettings;
        _tray.QuitClicked += () => QuitRequested?.Invoke();
        _tooltipTimer.Tick += (_, _) => UpdateTooltip();
    }

    public event Action? QuitRequested;

    private string PetName => _petStore.Current.PetName ?? "Companion";

    public void Start()
    {
        _pet.Start();
        _tooltipTimer.Start();
        UpdateTooltip();
        var trim = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        trim.Tick += (_, _) =>
        {
            trim.Stop();
            MemoryTrim.Now();
        };
        trim.Start();
    }

    public void Dispose()
    {
        _tooltipTimer.Stop();
        _pet.Dispose();
        _tray.Dispose();
        _activity.Dispose();
        _line.Close();
    }

    private void Hatch()
    {
        _pet.Life.Hatch();
        _pet.Refresh();
    }

    private void OnChatRequested()
    {
        if (!_petStore.Current.Hatched)
        {
            Hatch();
            return;
        }
        _line.Say("Chat arrives in the next build step!", _pet.Bounds);
    }

    private void OpenSettings() => _line.Say("Settings arrive soon!", _pet.Bounds);

    private void UpdateTooltip() =>
        _tray.SetTooltip($"{PetName} · {_pet.Life.State.ToString().ToLowerInvariant()} · ${_spend.ThisMonth:0.00} this month");

    private SpriteSheet LoadSheet(string directory)
    {
        try
        {
            return SpriteSheet.Load(directory);
        }
        catch (Exception ex) when (ex is IOException or FormatException or UnauthorizedAccessException or NotSupportedException)
        {
            _log.Error($"Skin at {directory} failed to load; using the fallback block.", ex);
            return SpriteSheet.Fallback();
        }
    }
}
```

- [ ] **Step 8: Build and run all tests**

Run: `dotnet build Companion.sln` → Expected: `Build succeeded.`
Run: `dotnet test src/Companion.Tests` → Expected: all pass.

- [ ] **Step 9: Run the app and verify by hand**

Start it in the background: `dotnet run --project src/Companion` (use `run_in_background`). Then check, taking screenshots if computer-use is available, otherwise asking the user:

1. An egg (cream, 64 px) sits on top of the taskbar near the right side and wobbles.
2. Clicking the egg hatches it: orange creature bounces with a heart, then breathes and blinks.
3. Within about a minute it walks a few steps, turns at screen edges, and faces its walking direction.
4. Dragging moves it horizontally. Clicking gives a heart.
5. Right-click menu: **Sleep** curls it up with Zs (a click wakes it), **Hide for 1 hour** hides it, tray **Show / hide** brings it back, **Quit** exits.
6. The tray icon shows the creature. Hovering shows `Companion · idle · $0.00 this month`.
7. Start fullscreen video (e.g. a browser video in F11): the pet disappears, and comes back after leaving fullscreen.
8. Quit and restart: it is still hatched and in the same x position (`data\pet.json` exists).
9. `data\logs\app-<today>.log` has `Starting.` and no `ERROR` lines.

Stop the app with tray → Quit before continuing.

- [ ] **Step 10: Commit**

```powershell
git add -A
git commit -m "feat(app): living pet window on the taskbar with tray icon" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 18: Chat bubble connected to the Brain

**Files:**
- Create: `src/Companion/ChatBubble.xaml`, `src/Companion/ChatBubble.xaml.cs`, `src/Companion/MarkdownView.cs`, `src/Companion/ChatController.cs`
- Replace: `src/Companion/AppHost.cs`

**Interfaces:**
- Consumes: `Brain`, `IBrainListener`, `BrainReply`, `BrainOutcome` (Task 11); `AnthropicClaudeClient` (Task 12); `MarkdownLite` (Task 13); `OnboardingFlow` (Task 14, used by `ChatController` now, wired in Task 19); `HotkeyService` (Task 16); `PetController` (Task 17).
- Produces: `ChatBubble` (`Submitted(string)`, `ThinkHarderClicked`, `NewChatClicked`, `SetTitle`, `SetConversationButtons(bool visible, bool thinkHarderEnabled)`, `SetBusy`, `SetSecretMode`, `SetStatus`, `ShowContent(string? question, string markdown, string? notice)`, `ShowAt(Rect)`, `FocusInput()`); `MarkdownView.Render(string, Panel)`; `ChatController(ChatBubble, PetController, Func<Brain?>, Func<string> petName, ILog)` with `IsOpen`, `Open()`, `Toggle()`, `StartOnboarding(OnboardingFlow)`, `event Action? OnboardingCompleted`.

- [ ] **Step 1: Chat bubble window**

Create `src/Companion/ChatBubble.xaml`:

```xml
<Window x:Class="Companion.ChatBubble"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Companion chat" Width="400" SizeToContent="Height"
        WindowStyle="None" AllowsTransparency="True" Background="Transparent"
        Topmost="True" ShowInTaskbar="False" ResizeMode="NoResize"
        FontFamily="Segoe UI" FontSize="13" PreviewKeyDown="OnWindowKeyDown">
    <Border Margin="8" Padding="12" CornerRadius="12"
            Background="{StaticResource BubbleBackground}" BorderBrush="{StaticResource BubbleBorder}" BorderThickness="1">
        <Border.Effect>
            <DropShadowEffect BlurRadius="14" ShadowDepth="2" Opacity="0.45" />
        </Border.Effect>
        <DockPanel>
            <DockPanel DockPanel.Dock="Top" Margin="0,0,0,8" LastChildFill="False">
                <TextBlock x:Name="TitleText" DockPanel.Dock="Left" VerticalAlignment="Center" FontWeight="SemiBold"
                           Foreground="{StaticResource Accent}" Text="Companion" />
                <Button DockPanel.Dock="Right" Content="✕" Style="{StaticResource BubbleButton}" Click="OnCloseClick" ToolTip="Close (Esc)" />
                <Button x:Name="NewChatButton" DockPanel.Dock="Right" Margin="0,0,6,0" Content="New chat"
                        Style="{StaticResource BubbleButton}" Click="OnNewChatClick" />
                <Button x:Name="ThinkHarderButton" DockPanel.Dock="Right" Margin="0,0,6,0" Content="🧠 Think harder"
                        Style="{StaticResource BubbleButton}" Click="OnThinkHarderClick" IsEnabled="False" />
            </DockPanel>
            <Grid DockPanel.Dock="Bottom" Margin="0,8,0,0">
                <TextBox x:Name="Input" AcceptsReturn="True" TextWrapping="Wrap" MaxHeight="120" VerticalScrollBarVisibility="Auto"
                         Background="#FF2A2A2A" Foreground="{StaticResource BubbleText}" CaretBrush="{StaticResource BubbleText}"
                         BorderBrush="{StaticResource BubbleBorder}" Padding="6,4" PreviewKeyDown="OnInputKeyDown" />
                <PasswordBox x:Name="SecretInput" Visibility="Collapsed" Background="#FF2A2A2A" Foreground="{StaticResource BubbleText}"
                             BorderBrush="{StaticResource BubbleBorder}" Padding="6,4" PreviewKeyDown="OnInputKeyDown" />
            </Grid>
            <TextBlock x:Name="StatusText" DockPanel.Dock="Bottom" Margin="0,6,0,0" FontStyle="Italic"
                       Foreground="{StaticResource BubbleMuted}" Visibility="Collapsed" />
            <ScrollViewer x:Name="AnswerScroll" MaxHeight="420" VerticalScrollBarVisibility="Auto">
                <StackPanel x:Name="AnswerPanel" />
            </ScrollViewer>
        </DockPanel>
    </Border>
</Window>
```

Create `src/Companion/ChatBubble.xaml.cs`:

```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Companion;

/// <summary>Speech bubble anchored above the pet. Esc or clicking elsewhere hides it; streaming continues.</summary>
public partial class ChatBubble : Window
{
    private Rect _anchor;

    public ChatBubble()
    {
        InitializeComponent();
        Deactivated += (_, _) => Hide();
        SizeChanged += (_, _) => Reposition();
    }

    public event Action<string>? Submitted;
    public event Action? ThinkHarderClicked;
    public event Action? NewChatClicked;

    public void SetTitle(string title) => TitleText.Text = title;

    public void SetConversationButtons(bool visible, bool thinkHarderEnabled)
    {
        var visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        ThinkHarderButton.Visibility = visibility;
        NewChatButton.Visibility = visibility;
        ThinkHarderButton.IsEnabled = thinkHarderEnabled;
    }

    public void SetBusy(bool busy)
    {
        Input.IsEnabled = !busy;
        SecretInput.IsEnabled = !busy;
        NewChatButton.IsEnabled = !busy;
        if (busy) ThinkHarderButton.IsEnabled = false;
    }

    public void SetSecretMode(bool secret)
    {
        Input.Visibility = secret ? Visibility.Collapsed : Visibility.Visible;
        SecretInput.Visibility = secret ? Visibility.Visible : Visibility.Collapsed;
    }

    public void SetStatus(string? status)
    {
        StatusText.Text = status ?? "";
        StatusText.Visibility = string.IsNullOrEmpty(status) ? Visibility.Collapsed : Visibility.Visible;
    }

    public void ShowContent(string? question, string markdown, string? notice)
    {
        AnswerPanel.Children.Clear();
        if (!string.IsNullOrWhiteSpace(question))
        {
            AnswerPanel.Children.Add(new TextBlock
            {
                Text = question,
                Foreground = (Brush)FindResource("BubbleMuted"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 6),
            });
        }
        MarkdownView.Render(markdown, AnswerPanel);
        if (!string.IsNullOrWhiteSpace(notice))
        {
            AnswerPanel.Children.Add(new TextBlock
            {
                Text = notice,
                Foreground = (Brush)FindResource("Accent"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 0),
            });
        }
        AnswerScroll.ScrollToEnd();
    }

    public void ShowAt(Rect anchor)
    {
        _anchor = anchor;
        if (!IsVisible) Show();
        Reposition();
        Activate();
        FocusInput();
    }

    public void FocusInput()
    {
        if (SecretInput.Visibility == Visibility.Visible) SecretInput.Focus();
        else Input.Focus();
    }

    private void Reposition()
    {
        if (!IsVisible) return;
        var area = SystemParameters.WorkArea;
        Left = Math.Clamp(_anchor.Left + _anchor.Width / 2 - ActualWidth / 2, area.Left, Math.Max(area.Left, area.Right - ActualWidth));
        Top = Math.Max(area.Top, _anchor.Top - ActualHeight + 8);
    }

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        Hide();
    }

    private void OnInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) return;
        e.Handled = true;
        var text = SecretInput.Visibility == Visibility.Visible ? SecretInput.Password : Input.Text;
        if (string.IsNullOrWhiteSpace(text)) return;
        Input.Clear();
        SecretInput.Clear();
        Submitted?.Invoke(text.Trim());
    }

    private void OnThinkHarderClick(object sender, RoutedEventArgs e) => ThinkHarderClicked?.Invoke();

    private void OnNewChatClick(object sender, RoutedEventArgs e) => NewChatClicked?.Invoke();

    private void OnCloseClick(object sender, RoutedEventArgs e) => Hide();
}
```

- [ ] **Step 2: Markdown renderer**

Create `src/Companion/MarkdownView.cs`:

```csharp
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Companion.Core;

namespace Companion;

/// <summary>Turns MarkdownLite blocks into WPF elements. Code blocks get a Copy button.</summary>
public static class MarkdownView
{
    private static readonly FontFamily Mono = new("Cascadia Mono, Consolas");

    public static void Render(string markdown, Panel target)
    {
        var text = Resource("BubbleText");
        foreach (var block in MarkdownLite.Parse(markdown))
        {
            switch (block.Kind)
            {
                case MdBlockKind.Code:
                    target.Children.Add(CodeBlock(block.CodeText ?? ""));
                    break;
                case MdBlockKind.Bullet:
                case MdBlockKind.Numbered:
                    var row = new DockPanel { Margin = new Thickness(4, 1, 0, 1) };
                    var marker = new TextBlock
                    {
                        Text = block.Kind == MdBlockKind.Bullet ? "•" : $"{block.Number}.",
                        Foreground = text,
                        Margin = new Thickness(0, 0, 6, 0),
                    };
                    DockPanel.SetDock(marker, Dock.Left);
                    row.Children.Add(marker);
                    row.Children.Add(Inlines(block.Spans, text, bold: false));
                    target.Children.Add(row);
                    break;
                case MdBlockKind.Heading:
                    var heading = Inlines(block.Spans, text, bold: true);
                    heading.Margin = new Thickness(0, 4, 0, 2);
                    target.Children.Add(heading);
                    break;
                default:
                    var paragraph = Inlines(block.Spans, text, bold: false);
                    paragraph.Margin = new Thickness(0, 2, 0, 4);
                    target.Children.Add(paragraph);
                    break;
            }
        }
    }

    private static TextBlock Inlines(IReadOnlyList<MdSpan> spans, Brush foreground, bool bold)
    {
        var block = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = foreground };
        foreach (var span in spans)
        {
            var run = new Run(span.Text);
            if (span.Code)
            {
                run.FontFamily = Mono;
                run.Background = Resource("CodeBackground");
            }
            if (span.Bold || bold) run.FontWeight = FontWeights.SemiBold;
            block.Inlines.Add(run);
        }
        return block;
    }

    private static UIElement CodeBlock(string code)
    {
        var box = new TextBox
        {
            Text = code,
            IsReadOnly = true,
            FontFamily = Mono,
            FontSize = 12,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Foreground = Resource("BubbleText"),
            TextWrapping = TextWrapping.NoWrap,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(0, 18, 0, 0),
        };
        var copy = new Button
        {
            Content = "Copy",
            Style = (Style)Application.Current.FindResource("BubbleButton"),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            FontSize = 11,
            Padding = new Thickness(6, 1, 6, 1),
        };
        copy.Click += (_, _) =>
        {
            try
            {
                Clipboard.SetText(code);
                copy.Content = "Copied";
            }
            catch (ExternalException)
            {
                copy.Content = "Busy, retry";
            }
        };
        var grid = new Grid();
        grid.Children.Add(box);
        grid.Children.Add(copy);
        return new Border
        {
            Background = Resource("CodeBackground"),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8),
            Margin = new Thickness(0, 4, 0, 4),
            Child = grid,
        };
    }

    private static Brush Resource(string key) => (Brush)Application.Current.FindResource(key);
}
```

- [ ] **Step 3: Chat controller**

Create `src/Companion/ChatController.cs`:

```csharp
using System.Text;
using System.Windows.Threading;
using Companion.Core;

namespace Companion;

/// <summary>Connects the bubble to the Brain and the pet. Brain callbacks arrive on background threads.</summary>
public sealed class ChatController : IBrainListener
{
    private readonly ChatBubble _bubble;
    private readonly PetController _pet;
    private readonly Func<Brain?> _brain;
    private readonly Func<string> _petName;
    private readonly ILog _log;
    private readonly Dispatcher _ui;
    private readonly DispatcherTimer _renderTimer;
    private readonly DispatcherTimer _slowTimer;
    private readonly StringBuilder _answer = new();
    private string? _question;
    private string? _notice;
    private bool _dirty;
    private OnboardingFlow? _onboarding;

    public ChatController(ChatBubble bubble, PetController pet, Func<Brain?> brain, Func<string> petName, ILog log)
    {
        _bubble = bubble;
        _pet = pet;
        _brain = brain;
        _petName = petName;
        _log = log;
        _ui = bubble.Dispatcher;
        _renderTimer = new DispatcherTimer(DispatcherPriority.Background, _ui) { Interval = TimeSpan.FromMilliseconds(60) };
        _renderTimer.Tick += (_, _) => RenderIfDirty();
        _slowTimer = new DispatcherTimer(DispatcherPriority.Normal, _ui) { Interval = TimeSpan.FromSeconds(8) };
        _slowTimer.Tick += (_, _) =>
        {
            _slowTimer.Stop();
            _bubble.SetStatus("hmm, one sec…");
        };
        bubble.Submitted += OnSubmitted;
        bubble.ThinkHarderClicked += OnThinkHarder;
        bubble.NewChatClicked += OnNewChat;
    }

    public event Action? OnboardingCompleted;

    public bool IsOpen => _bubble.IsVisible;

    public void Toggle()
    {
        if (_bubble.IsVisible) _bubble.Hide();
        else Open();
    }

    public void Open()
    {
        _bubble.SetTitle(_petName());
        var chatting = _onboarding is null;
        _bubble.SetConversationButtons(chatting, chatting && _answer.Length > 0 && !(_brain()?.Busy ?? false));
        if (chatting && _answer.Length == 0 && _question is null)
        {
            _bubble.ShowContent(null, "*wags* What's up?", null);
        }
        _bubble.ShowAt(_pet.Bounds);
    }

    public void StartOnboarding(OnboardingFlow flow)
    {
        _onboarding = flow;
        _answer.Clear();
        _question = null;
        _notice = null;
        ShowOnboardingPrompt(null);
        Open();
    }

    private void ShowOnboardingPrompt(string? error)
    {
        _bubble.SetSecretMode(_onboarding!.Step == OnboardingStep.ApiKey);
        _bubble.ShowContent(null, _onboarding.Prompt, error);
    }

    private async void OnSubmitted(string text)
    {
        if (_onboarding is { } flow)
        {
            await SubmitOnboardingAsync(flow, text);
            return;
        }
        await RunAsync(text, (brain, listener) => brain.AskAsync(text, listener));
    }

    private async Task SubmitOnboardingAsync(OnboardingFlow flow, string text)
    {
        _bubble.SetBusy(true);
        _bubble.SetStatus(flow.Step == OnboardingStep.ApiKey ? "checking the key…" : null);
        string? error;
        try
        {
            error = await flow.SubmitAsync(text, CancellationToken.None);
        }
        finally
        {
            _bubble.SetBusy(false);
            _bubble.SetStatus(null);
        }

        _bubble.SetTitle(_petName());
        if (flow.Step == OnboardingStep.Done)
        {
            _onboarding = null;
            _bubble.SetSecretMode(false);
            _bubble.ShowContent(null, flow.Prompt, null);
            _bubble.SetConversationButtons(true, false);
            OnboardingCompleted?.Invoke();
        }
        else
        {
            ShowOnboardingPrompt(error);
        }
        _bubble.FocusInput();
    }

    private async void OnThinkHarder() =>
        await RunAsync("🧠 Think harder", (brain, listener) => brain.ThinkHarderAsync(listener));

    private void OnNewChat()
    {
        _brain()?.NewChat();
        _answer.Clear();
        _question = null;
        _notice = null;
        _bubble.ShowContent(null, "*fresh start!* What's up?", null);
        _bubble.SetConversationButtons(true, false);
        _bubble.FocusInput();
    }

    private async Task RunAsync(string question, Func<Brain, IBrainListener, Task<BrainReply>> call)
    {
        var brain = _brain();
        if (brain is null)
        {
            _bubble.ShowContent(null, "I need an API key before I can think. Right-click me → **Settings**.", null);
            return;
        }
        if (brain.Busy) return;

        _question = question;
        _answer.Clear();
        _notice = null;
        _dirty = true;
        _bubble.SetBusy(true);
        _renderTimer.Start();
        BrainReply reply;
        try
        {
            reply = await call(brain, this);
        }
        catch (Exception ex)
        {
            _log.Error("Chat turn failed.", ex);
            reply = new BrainReply(BrainOutcome.Error, _answer.ToString(), "Something went wrong (details are in data\\logs).");
        }
        finally
        {
            _slowTimer.Stop();
            _renderTimer.Stop();
            _bubble.SetBusy(false);
            _bubble.SetStatus(null);
        }

        _answer.Clear().Append(reply.Text);
        _notice = reply.Notice;
        _dirty = true;
        RenderIfDirty();
        _bubble.SetConversationButtons(true, reply.Outcome is BrainOutcome.Ok or BrainOutcome.CutOff);
        _pet.Life.OnChatFinished(reply.Outcome == BrainOutcome.Ok);
        _pet.Refresh();
        _bubble.FocusInput();
    }

    private void RenderIfDirty()
    {
        if (!_dirty) return;
        _dirty = false;
        _bubble.ShowContent(_question, _answer.ToString(), _notice);
    }

    public void OnThinking() => _ui.InvokeAsync(() =>
    {
        _pet.Life.OnChatStarted();
        _pet.Refresh();
        _bubble.SetStatus("thinking…");
        _slowTimer.Start();
    });

    public void OnFirstText() => _ui.InvokeAsync(() =>
    {
        _pet.Life.OnFirstToken();
        _pet.Refresh();
        _slowTimer.Stop();
        _bubble.SetStatus(null);
    });

    public void OnText(string delta) => _ui.InvokeAsync(() =>
    {
        _answer.Append(delta);
        _dirty = true;
    });

    public void OnStatus(string status) => _ui.InvokeAsync(() => _bubble.SetStatus(status));
}
```

- [ ] **Step 4: Composition root with chat**

Replace `src/Companion/AppHost.cs`:

```csharp
using System.Windows.Threading;
using Companion.Core;

namespace Companion;

/// <summary>Builds and wires everything. Settings and onboarding arrive in Task 19.</summary>
public sealed class AppHost : IDisposable
{
    private readonly ILog _log;
    private readonly SettingsStore _settings;
    private readonly SecretStore _secrets;
    private readonly MemoryStore _memory;
    private readonly PetStateStore _petStore;
    private readonly SpendTracker _spend;
    private readonly ChatLog _chatLog;
    private readonly Conversation _conversation;
    private readonly ToolRegistry _tools = new();
    private readonly UserActivity _activity = new();
    private readonly HotkeyService _hotkey = new();
    private readonly PetController _pet;
    private readonly TrayIcon _tray;
    private readonly LineBubble _line = new();
    private readonly ChatBubble _bubble = new();
    private readonly ChatController _chat;
    private readonly DispatcherTimer _tooltipTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    private Brain? _brain;

    public AppHost(AppPaths paths, IClock clock, ILog log)
    {
        _log = log;
        _settings = new SettingsStore(paths.SettingsFile, clock, log);
        _secrets = new SecretStore(paths.SecretsFile, new DpapiProtector(), log);
        _memory = new MemoryStore(paths.MemoryFile, clock, log);
        _petStore = new PetStateStore(paths.PetFile, clock, log);
        _spend = new SpendTracker(_settings, clock);
        _chatLog = new ChatLog(paths.Logs, clock);
        _conversation = new Conversation(clock);
        _tools.Register(new RememberTool(_memory));
        _tools.Register(new ForgetTool(_memory));

        var sheet = LoadSheet(Path.Combine(paths.Skins, "default"));
        _pet = new PetController(new PetWindow(), new PetLife(_petStore.Current, clock, new Random()), sheet, _activity, _settings, _petStore);
        _tray = new TrayIcon(sheet.Frame(0));
        _chat = new ChatController(_bubble, _pet, () => _brain, () => PetName, log);

        _pet.EggClicked += Hatch;
        _pet.ChatRequested += OnChatRequested;
        _pet.SettingsRequested += OpenSettings;
        _pet.QuitRequested += () => QuitRequested?.Invoke();
        _pet.LineReady += text => { if (!_chat.IsOpen) _line.Say(text, _pet.Bounds); };
        _tray.ChatClicked += OnChatRequested;
        _tray.ToggleClicked += _pet.ToggleVisible;
        _tray.SleepClicked += () => { _pet.Life.ForceSleep(); _pet.Refresh(); };
        _tray.SettingsClicked += OpenSettings;
        _tray.QuitClicked += () => QuitRequested?.Invoke();
        _hotkey.Pressed += OnChatRequested;
        _bubble.IsVisibleChanged += (_, e) => { if (e.NewValue is false) MemoryTrim.Now(); };
        _tooltipTimer.Tick += (_, _) => UpdateTooltip();
        RebuildBrain();
    }

    public event Action? QuitRequested;

    private string PetName => _petStore.Current.PetName ?? "Companion";

    public void Start()
    {
        _pet.Start();
        RegisterHotkey();
        _tooltipTimer.Start();
        UpdateTooltip();
        var trim = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        trim.Tick += (_, _) =>
        {
            trim.Stop();
            MemoryTrim.Now();
        };
        trim.Start();
    }

    public void Dispose()
    {
        _tooltipTimer.Stop();
        _hotkey.Dispose();
        _bubble.Close();
        _pet.Dispose();
        _tray.Dispose();
        _activity.Dispose();
        _line.Close();
    }

    private void Hatch()
    {
        _pet.Life.Hatch();
        _pet.Refresh();
    }

    private void OnChatRequested()
    {
        if (!_petStore.Current.Hatched)
        {
            Hatch();
            return;
        }
        _chat.Toggle();
    }

    private void OpenSettings() => _line.Say("Settings arrive in the next build step!", _pet.Bounds);

    private void RebuildBrain()
    {
        if (_brain is { Busy: true }) return;
        var key = _secrets.LoadApiKey();
        _brain = key is null
            ? null
            : new Brain(new AnthropicClaudeClient(key), _conversation,
                () => SystemPrompt.Build(PetName, _petStore.Current.UserName ?? "my human", _memory.Facts),
                _tools, _spend, _settings, _chatLog, _log);
    }

    private void RegisterHotkey()
    {
        if (!_hotkey.Register(_pet.Window, _settings.Current.Hotkey))
            _log.Info($"Hotkey '{_settings.Current.Hotkey}' could not be registered (invalid or used by another app).");
    }

    private void UpdateTooltip() =>
        _tray.SetTooltip($"{PetName} · {_pet.Life.State.ToString().ToLowerInvariant()} · ${_spend.ThisMonth:0.00} this month");

    private SpriteSheet LoadSheet(string directory)
    {
        try
        {
            return SpriteSheet.Load(directory);
        }
        catch (Exception ex) when (ex is IOException or FormatException or UnauthorizedAccessException or NotSupportedException)
        {
            _log.Error($"Skin at {directory} failed to load; using the fallback block.", ex);
            return SpriteSheet.Fallback();
        }
    }
}
```

- [ ] **Step 5: Build and run all tests**

Run: `dotnet build Companion.sln` → Expected: `Build succeeded.`
Run: `dotnet test src/Companion.Tests` → Expected: all pass.

- [ ] **Step 6: Run the app and verify chat by hand**

This needs the API key saved in Task 12 (`data\secrets.bin`) and spends a few cents. Ask the user before sending real questions. Start `dotnet run --project src/Companion` in the background, then:

1. Double-click the pet (and separately press `Ctrl+Alt+Space`): the bubble opens above it with "*wags* What's up?" and focus in the text box.
2. Ask "Give me a 3-step list for making tea, with one line of code in a code block." The pet switches to thinking (dots) and then talking; text streams in; the list and code block render; **Copy** puts the code on the clipboard.
3. **🧠 Think harder** becomes enabled after the answer and gives a new answer with the question line "🧠 Think harder".
4. Ask "Remember that I work in Cinema 4D." Then check `data\memory.json` contains the fact.
5. Ask "What's the weather in Sofia today?": the status shows "searching the web…" before the answer.
6. Esc and clicking elsewhere hide the bubble. Reopening shows the last exchange. **New chat** resets it.
7. The tray tooltip shows a non-zero spend after a minute. `data\logs\chat-<month>.jsonl` has the exchanges. The app log has no `ERROR`.
8. Temporarily rename `data\secrets.bin`, restart, and ask something: the bubble says it needs an API key. Rename the file back.

Quit from the tray.

- [ ] **Step 7: Commit**

```powershell
git add -A
git commit -m "feat(app): chat bubble with streaming answers, markdown and think-harder" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 19: Settings window and first-run hatching

**Files:**
- Create: `src/Companion/SettingsWindow.xaml`, `src/Companion/SettingsWindow.xaml.cs`
- Replace: `src/Companion/AppHost.cs`

**Interfaces:**
- Consumes: `OnboardingFlow` (Task 14), `ChatController.StartOnboarding` / `OnboardingCompleted` (Task 18), `Autostart` and `HotkeyService.TryParse` (Task 16), `PetController.ApplyScale` (Task 17).
- Produces: `record SettingsDeps(SettingsStore Settings, SecretStore Secrets, MemoryStore Memory, SpendTracker Spend, Func<string, CancellationToken, Task> ValidateKey, Action Applied, Action ResetPet)`; `SettingsWindow(SettingsDeps)`; final `AppHost`.

- [ ] **Step 1: Settings window**

Create `src/Companion/SettingsWindow.xaml`:

```xml
<Window x:Class="Companion.SettingsWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Companion settings" Width="480" SizeToContent="Height" ResizeMode="NoResize"
        WindowStartupLocation="CenterScreen" FontFamily="Segoe UI" FontSize="13">
    <StackPanel Margin="16">
        <TextBlock Text="Anthropic API key" FontWeight="SemiBold" />
        <DockPanel Margin="0,4,0,0">
            <Button x:Name="SaveKeyButton" DockPanel.Dock="Right" Content="Test &amp; save" Padding="10,2" Margin="6,0,0,0" Click="OnSaveKey" />
            <PasswordBox x:Name="KeyBox" />
        </DockPanel>
        <TextBlock x:Name="KeyStatus" Margin="0,2,0,12" Foreground="Gray" TextWrapping="Wrap" />

        <Grid Margin="0,0,0,8">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="170" />
                <ColumnDefinition />
            </Grid.ColumnDefinitions>
            <Grid.RowDefinitions>
                <RowDefinition Height="Auto" />
                <RowDefinition Height="Auto" />
                <RowDefinition Height="Auto" />
                <RowDefinition Height="Auto" />
                <RowDefinition Height="Auto" />
                <RowDefinition Height="Auto" />
            </Grid.RowDefinitions>
            <TextBlock Grid.Row="0" Text="Chat hotkey" VerticalAlignment="Center" />
            <TextBox x:Name="HotkeyBox" Grid.Row="0" Grid.Column="1" Margin="0,3" />
            <TextBlock Grid.Row="1" Text="Everyday effort" VerticalAlignment="Center" />
            <ComboBox x:Name="EverydayBox" Grid.Row="1" Grid.Column="1" Margin="0,3" />
            <TextBlock Grid.Row="2" Text="Think-harder effort" VerticalAlignment="Center" />
            <ComboBox x:Name="HarderBox" Grid.Row="2" Grid.Column="1" Margin="0,3" />
            <TextBlock Grid.Row="3" Text="Monthly cap (USD)" VerticalAlignment="Center" />
            <TextBox x:Name="CapBox" Grid.Row="3" Grid.Column="1" Margin="0,3" />
            <TextBlock Grid.Row="4" Text="Pet size" VerticalAlignment="Center" />
            <ComboBox x:Name="ScaleBox" Grid.Row="4" Grid.Column="1" Margin="0,3" />
            <CheckBox x:Name="AutostartBox" Grid.Row="5" Grid.ColumnSpan="2" Margin="0,8,0,0" Content="Start with Windows" />
        </Grid>
        <TextBlock x:Name="SpendText" Foreground="Gray" Margin="0,0,0,12" TextWrapping="Wrap" />

        <TextBlock Text="What I remember about you" FontWeight="SemiBold" />
        <ListBox x:Name="FactsList" Height="150" Margin="0,4,0,4" DisplayMemberPath="Text" SelectionChanged="OnFactSelected" />
        <DockPanel>
            <Button DockPanel.Dock="Right" Content="Delete" Padding="10,2" Margin="6,0,0,0" Click="OnDeleteFact" />
            <Button DockPanel.Dock="Right" Content="Update" Padding="10,2" Margin="6,0,0,0" Click="OnUpdateFact" />
            <TextBox x:Name="FactBox" />
        </DockPanel>

        <DockPanel Margin="0,16,0,0" LastChildFill="False">
            <Button DockPanel.Dock="Left" Content="Reset pet…" Padding="10,3" Click="OnResetPet" />
            <Button DockPanel.Dock="Right" Content="Close" Padding="14,3" Margin="6,0,0,0" IsCancel="True" Click="OnClose" />
            <Button DockPanel.Dock="Right" Content="Save" Padding="14,3" IsDefault="True" Click="OnSave" />
        </DockPanel>
    </StackPanel>
</Window>
```

Create `src/Companion/SettingsWindow.xaml.cs`:

```csharp
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Companion.Core;

namespace Companion;

public sealed record SettingsDeps(
    SettingsStore Settings,
    SecretStore Secrets,
    MemoryStore Memory,
    SpendTracker Spend,
    Func<string, CancellationToken, Task> ValidateKey,
    Action Applied,
    Action ResetPet);

public partial class SettingsWindow : Window
{
    private readonly SettingsDeps _deps;

    public SettingsWindow(SettingsDeps deps)
    {
        InitializeComponent();
        _deps = deps;
        EverydayBox.ItemsSource = Enum.GetValues<EffortLevel>();
        HarderBox.ItemsSource = Enum.GetValues<EffortLevel>();
        ScaleBox.ItemsSource = new[] { 3, 4, 6 };

        var settings = deps.Settings.Current;
        HotkeyBox.Text = settings.Hotkey;
        EverydayBox.SelectedItem = settings.EverydayEffort;
        HarderBox.SelectedItem = settings.ThinkHarderEffort;
        CapBox.Text = settings.MonthlyCapUsd.ToString("0.##", CultureInfo.InvariantCulture);
        ScaleBox.SelectedItem = settings.PetScale;
        AutostartBox.IsChecked = settings.StartWithWindows;
        SpendText.Text = $"Estimated spend this month: ${deps.Spend.ThisMonth:0.00} of ${deps.Spend.Cap:0.##}. A cap of 0 stops all questions.";
        KeyStatus.Text = deps.Secrets.HasKey ? "A key is saved (encrypted on this PC)." : "No key saved yet.";

        RefreshFacts();
        deps.Memory.Changed += OnMemoryChanged;
        Closed += (_, _) => deps.Memory.Changed -= OnMemoryChanged;
    }

    private void OnMemoryChanged() => Dispatcher.InvokeAsync(RefreshFacts);

    private void RefreshFacts()
    {
        FactsList.ItemsSource = _deps.Memory.Facts;
        FactBox.Text = "";
    }

    private void OnFactSelected(object sender, SelectionChangedEventArgs e) =>
        FactBox.Text = (FactsList.SelectedItem as Fact)?.Text ?? "";

    private void OnUpdateFact(object sender, RoutedEventArgs e)
    {
        if (FactsList.SelectedItem is Fact fact) _deps.Memory.Update(fact.Id, FactBox.Text);
    }

    private void OnDeleteFact(object sender, RoutedEventArgs e)
    {
        if (FactsList.SelectedItem is Fact fact) _deps.Memory.Remove(fact.Id);
    }

    private async void OnSaveKey(object sender, RoutedEventArgs e)
    {
        var key = KeyBox.Password.Trim();
        if (!key.StartsWith("sk-", StringComparison.Ordinal))
        {
            KeyStatus.Text = "That doesn't look like an API key (they start with sk-).";
            return;
        }
        SaveKeyButton.IsEnabled = false;
        KeyStatus.Text = "Checking…";
        try
        {
            await _deps.ValidateKey(key, CancellationToken.None);
            _deps.Secrets.SaveApiKey(key);
            KeyBox.Clear();
            KeyStatus.Text = "The key works and is saved (encrypted on this PC).";
            _deps.Applied();
        }
        catch (ClaudeException ex)
        {
            KeyStatus.Text = ex.Kind == ClaudeErrorKind.Unauthorized ? "That key was rejected." : $"Couldn't check the key right now ({ex.Kind}).";
        }
        finally
        {
            SaveKeyButton.IsEnabled = true;
        }
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (!HotkeyService.TryParse(HotkeyBox.Text, out _, out _))
        {
            MessageBox.Show(this, "The hotkey must look like Ctrl+Alt+Space: at least one of Ctrl/Alt/Shift/Win plus one key.", "Companion");
            return;
        }
        if (!decimal.TryParse(CapBox.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var cap) || cap < 0)
        {
            MessageBox.Show(this, "The monthly cap must be a number like 30 or 12.50.", "Companion");
            return;
        }

        var settings = _deps.Settings.Current;
        settings.Hotkey = HotkeyBox.Text.Trim();
        settings.EverydayEffort = (EffortLevel)EverydayBox.SelectedItem;
        settings.ThinkHarderEffort = (EffortLevel)HarderBox.SelectedItem;
        settings.MonthlyCapUsd = cap;
        settings.PetScale = (int)ScaleBox.SelectedItem;
        settings.StartWithWindows = AutostartBox.IsChecked == true;
        _deps.Settings.Save();
        _deps.Applied();
        Close();
    }

    private void OnResetPet(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(this,
            "Reset the pet? It becomes an egg again and forgets its name. Your memory facts and API key stay.",
            "Companion", MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (answer != MessageBoxResult.OK) return;
        _deps.ResetPet();
        Close();
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
```

- [ ] **Step 2: Final composition root**

Replace `src/Companion/AppHost.cs`:

```csharp
using System.Windows.Threading;
using Companion.Core;

namespace Companion;

/// <summary>Composition root: builds every service once and wires events between them.</summary>
public sealed class AppHost : IDisposable
{
    private readonly ILog _log;
    private readonly SettingsStore _settings;
    private readonly SecretStore _secrets;
    private readonly MemoryStore _memory;
    private readonly PetStateStore _petStore;
    private readonly SpendTracker _spend;
    private readonly ChatLog _chatLog;
    private readonly Conversation _conversation;
    private readonly ToolRegistry _tools = new();
    private readonly UserActivity _activity = new();
    private readonly HotkeyService _hotkey = new();
    private readonly PetController _pet;
    private readonly TrayIcon _tray;
    private readonly LineBubble _line = new();
    private readonly ChatBubble _bubble = new();
    private readonly ChatController _chat;
    private readonly DispatcherTimer _tooltipTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    private Brain? _brain;
    private SettingsWindow? _settingsWindow;

    public AppHost(AppPaths paths, IClock clock, ILog log)
    {
        _log = log;
        _settings = new SettingsStore(paths.SettingsFile, clock, log);
        _secrets = new SecretStore(paths.SecretsFile, new DpapiProtector(), log);
        _memory = new MemoryStore(paths.MemoryFile, clock, log);
        _petStore = new PetStateStore(paths.PetFile, clock, log);
        _spend = new SpendTracker(_settings, clock);
        _chatLog = new ChatLog(paths.Logs, clock);
        _conversation = new Conversation(clock);
        _tools.Register(new RememberTool(_memory));
        _tools.Register(new ForgetTool(_memory));

        var sheet = LoadSheet(Path.Combine(paths.Skins, "default"));
        _pet = new PetController(new PetWindow(), new PetLife(_petStore.Current, clock, new Random()), sheet, _activity, _settings, _petStore);
        _tray = new TrayIcon(sheet.Frame(0));
        _chat = new ChatController(_bubble, _pet, () => _brain, () => PetName, log);

        _pet.EggClicked += Hatch;
        _pet.ChatRequested += OnChatRequested;
        _pet.SettingsRequested += OpenSettings;
        _pet.QuitRequested += () => QuitRequested?.Invoke();
        _pet.LineReady += text => { if (!_chat.IsOpen) _line.Say(text, _pet.Bounds); };
        _tray.ChatClicked += OnChatRequested;
        _tray.ToggleClicked += _pet.ToggleVisible;
        _tray.SleepClicked += () => { _pet.Life.ForceSleep(); _pet.Refresh(); };
        _tray.SettingsClicked += OpenSettings;
        _tray.QuitClicked += () => QuitRequested?.Invoke();
        _hotkey.Pressed += OnChatRequested;
        _chat.OnboardingCompleted += () => { RebuildBrain(); UpdateTooltip(); };
        _bubble.IsVisibleChanged += (_, e) => { if (e.NewValue is false) MemoryTrim.Now(); };
        _tooltipTimer.Tick += (_, _) => UpdateTooltip();
        RebuildBrain();
    }

    public event Action? QuitRequested;

    private string PetName => _petStore.Current.PetName ?? "Companion";

    public void Start()
    {
        _pet.Start();
        RegisterHotkey();
        _tooltipTimer.Start();
        UpdateTooltip();
        var trim = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        trim.Tick += (_, _) =>
        {
            trim.Stop();
            MemoryTrim.Now();
        };
        trim.Start();
    }

    public void Dispose()
    {
        _tooltipTimer.Stop();
        _settingsWindow?.Close();
        _hotkey.Dispose();
        _bubble.Close();
        _pet.Dispose();
        _tray.Dispose();
        _activity.Dispose();
        _line.Close();
    }

    private void Hatch()
    {
        _pet.Life.Hatch();
        _pet.Refresh();
        if (!OnboardingFlow.IsComplete(_petStore, _secrets)) _chat.StartOnboarding(CreateOnboarding());
    }

    private void OnChatRequested()
    {
        if (!_petStore.Current.Hatched) Hatch();
        else if (!OnboardingFlow.IsComplete(_petStore, _secrets)) _chat.StartOnboarding(CreateOnboarding());
        else _chat.Toggle();
    }

    private OnboardingFlow CreateOnboarding() =>
        new(_petStore, _secrets, (key, ct) => new AnthropicClaudeClient(key).ValidateKeyAsync(ct), _settings.Current.Hotkey);

    private void OpenSettings()
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }
        _settingsWindow = new SettingsWindow(new SettingsDeps(
            _settings, _secrets, _memory, _spend,
            (key, ct) => new AnthropicClaudeClient(key).ValidateKeyAsync(ct),
            ApplySettings,
            ResetPet));
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
    }

    private void ApplySettings()
    {
        RegisterHotkey();
        _pet.ApplyScale();
        try
        {
            Autostart.Apply(_settings.Current.StartWithWindows, Environment.ProcessPath!);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            _log.Error("Could not update the autostart entry.", ex);
        }
        RebuildBrain();
        UpdateTooltip();
    }

    private void ResetPet()
    {
        _bubble.Hide();
        _conversation.Reset();
        _petStore.Reset();
        _pet.Refresh();
    }

    private void RebuildBrain()
    {
        if (_brain is { Busy: true }) return;
        var key = _secrets.LoadApiKey();
        _brain = key is null
            ? null
            : new Brain(new AnthropicClaudeClient(key), _conversation,
                () => SystemPrompt.Build(PetName, _petStore.Current.UserName ?? "my human", _memory.Facts),
                _tools, _spend, _settings, _chatLog, _log);
    }

    private void RegisterHotkey()
    {
        if (!_hotkey.Register(_pet.Window, _settings.Current.Hotkey))
            _log.Info($"Hotkey '{_settings.Current.Hotkey}' could not be registered (invalid or used by another app).");
    }

    private void UpdateTooltip() =>
        _tray.SetTooltip($"{PetName} · {_pet.Life.State.ToString().ToLowerInvariant()} · ${_spend.ThisMonth:0.00} this month");

    private SpriteSheet LoadSheet(string directory)
    {
        try
        {
            return SpriteSheet.Load(directory);
        }
        catch (Exception ex) when (ex is IOException or FormatException or UnauthorizedAccessException or NotSupportedException)
        {
            _log.Error($"Skin at {directory} failed to load; using the fallback block.", ex);
            return SpriteSheet.Fallback();
        }
    }
}
```

- [ ] **Step 3: Build and run all tests**

Run: `dotnet build Companion.sln` → Expected: `Build succeeded.`
Run: `dotnet test src/Companion.Tests` → Expected: all pass.

- [ ] **Step 4: Verify the first-run flow and settings by hand**

Back up and clear state so the app starts fresh (the key is kept aside, not deleted):

```powershell
New-Item -ItemType Directory -Force data\backup | Out-Null
Move-Item data\pet.json, data\secrets.bin data\backup -ErrorAction SilentlyContinue
```

Start `dotnet run --project src/Companion` in the background, then check:

1. An egg appears. Clicking it hatches the pet and opens the bubble: "*blinks* Hi! I just hatched. What should my name be?"
2. Entering a name, then the user's name, then a masked API-key prompt appears. **The user pastes their key**; never type it yourself. A bad key shows "didn't work"; a good one shows "All set! … Ctrl+Alt+Space".
3. A question now works (costs a cent or two; ask the user first).
4. Right-click → **Settings**: all fields show current values; the memory list shows facts, and **Update** / **Delete** change `data\memory.json`.
5. Change the hotkey to `Ctrl+Alt+J`, the size to 6 and save: the pet grows and the new hotkey opens chat.
6. Tick **Start with Windows** and save: `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` has `Companion` (check with `Get-ItemProperty HKCU:\Software\Microsoft\Windows\CurrentVersion\Run`). Untick and save: the value is gone.
7. **Reset pet…** → OK: the pet turns back into an egg; hatching asks for names again, but not for the key.

Quit, then restore the user's original state if they want the previous pet back:

```powershell
Move-Item data\backup\* data -Force
Remove-Item data\backup
```

Set the hotkey and size back to the defaults in Settings if they were changed.

- [ ] **Step 5: Commit**

```powershell
git add -A
git commit -m "feat(app): settings window, memory editor and first-run hatching" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 20: Publish, measure the footprint, final checklist

**Files:**
- Create: `tools/publish.ps1`, `tools/measure-footprint.ps1`, `docs/manual-checklist.md`

**Interfaces:**
- Consumes: the finished app.
- Produces: `D:\Claude\Companion\app\Companion.exe`; footprint numbers against the spec targets.

- [ ] **Step 1: Publish script**

Create `tools/publish.ps1`:

```powershell
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (Get-Process Companion -ErrorAction SilentlyContinue) {
    throw 'Companion is running. Quit it from the tray first.'
}
dotnet publish "$root\src\Companion\Companion.csproj" -c Release -r win-x64 --self-contained false -o "$root\app"
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }
Write-Host "Published to $root\app\Companion.exe"
```

- [ ] **Step 2: Footprint script**

Create `tools/measure-footprint.ps1`:

```powershell
param([int]$Seconds = 120)
$ErrorActionPreference = 'Stop'

$process = Get-Process Companion | Select-Object -First 1
$cores = [Environment]::ProcessorCount
$cpuStart = $process.TotalProcessorTime
$start = Get-Date
$samples = New-Object System.Collections.Generic.List[double]
for ($i = 0; $i -lt $Seconds; $i++) {
    Start-Sleep -Seconds 1
    $process.Refresh()
    $samples.Add($process.WorkingSet64 / 1MB)
}
$process.Refresh()
$elapsed = ((Get-Date) - $start).TotalSeconds
$cpu = ($process.TotalProcessorTime - $cpuStart).TotalSeconds / $elapsed / $cores * 100
$average = ($samples | Measure-Object -Average).Average
$maximum = ($samples | Measure-Object -Maximum).Maximum

'Working set: avg {0:N1} MB, max {1:N1} MB' -f $average, $maximum
'Private bytes: {0:N1} MB' -f ($process.PrivateMemorySize64 / 1MB)
'CPU: {0:N2} % of all cores' -f $cpu
if ($average -lt 60 -and $cpu -lt 1) { 'PASS (targets: < 60 MB working set, < 1 % CPU)' }
else { 'FAIL (targets: < 60 MB working set, < 1 % CPU)' }
```

- [ ] **Step 3: Manual checklist**

Create `docs/manual-checklist.md`:

```markdown
# Companion manual checklist

Run after any change to the app project. Start `app\Companion.exe` (or `dotnet run --project src/Companion`).

## Pet
- [ ] Egg on first run; click hatches; bubble asks pet name, user name, API key (masked).
- [ ] Idle breathing and blinking; walks every 20–60 s and turns at screen edges.
- [ ] Click → heart; drag moves it horizontally; position survives restart.
- [ ] Right-click menu: Chat, Sleep (Zs, click wakes), Hide for 1 hour, Settings, Quit.
- [ ] Sleeps after 5 idle minutes (1 minute at night); yawns when you come back.
- [ ] Hidden while a fullscreen app runs or the PC is locked; returns afterwards.
- [ ] Bored line appears in a mini bubble at most every 20 minutes.

## Chat
- [ ] Double-click, tray Chat and the hotkey all open the bubble above the pet.
- [ ] Answers stream; pet shows thinking dots, then talking, then a happy bounce.
- [ ] Lists, bold, inline code and code blocks render; Copy works.
- [ ] 🧠 Think harder gives a new, deeper answer.
- [ ] "searching the web…" appears for current-events questions.
- [ ] "Remember that …" adds a fact visible in Settings; "forget …" removes it.
- [ ] Esc / click-away hides; New chat resets; after 30 quiet minutes a new chat starts.
- [ ] No network: bubble says it can't reach the internet.

## Settings
- [ ] Key test & save; hotkey, effort, cap, size, autostart all apply on Save.
- [ ] Memory list update/delete works.
- [ ] Reset pet turns it back into an egg and keeps the key.

## Footprint
- [ ] `tools\measure-footprint.ps1` after 1 idle minute: PASS.
```

- [ ] **Step 4: Full test run and publish**

Run: `dotnet test src/Companion.Tests` → Expected: all pass.
Run: `powershell -ExecutionPolicy Bypass -File tools/publish.ps1` → Expected: `Published to D:\Claude\Companion\app\Companion.exe`.

- [ ] **Step 5: Measure the footprint**

Start `app\Companion.exe` (it finds `D:\Claude\Companion` as its root because `skins\` is one level up). Leave it idle with the bubble closed for 1 minute, then run the measurement in the background (it takes 2 minutes):

```powershell
powershell -ExecutionPolicy Bypass -File tools/measure-footprint.ps1
```

Expected: `PASS`. If it prints `FAIL`, report the exact numbers to the user rather than tuning blindly. The known levers are already applied (workstation non-concurrent GC, `ConserveMemory`, working-set trim after startup and after chats). Next candidates to discuss: lowering idle fps, or dropping the WinForms tray dependency.

- [ ] **Step 6: Walk the manual checklist**

Go through `docs/manual-checklist.md` with the user (visual items need their eyes, or screenshots via computer use). Record anything that fails as a follow-up; do not mark the task done with failing items unless the user accepts them.

- [ ] **Step 7: Commit**

```powershell
git add -A
git commit -m "chore: publish and footprint scripts plus manual checklist" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

## Spec coverage

| Spec section | Task(s) |
|---|---|
| §3 Folder layout, portable root | 1, 17 (`COMPANION_ROOT`), 20 |
| §4 Components | 2–19 (see File Map) |
| §5.1 States and priorities | 7, 17 |
| §5.2 Meters | 6 |
| §5.3 Built-in lines | 7, 17 (`LineBubble`) |
| §5.4 Energy-saving rules | 17 (frame-change redraw, suspend on fullscreen/lock), 16 |
| §6 Skin format, scaling | 1 (json), 5, 15, 17 |
| §7 Brain & Claude | 9, 10, 11, 12 |
| §8 Memory | 4, 10, 19 (editor), 9 (chat log) |
| §9 Spend tracking | 8, 11, 17/19 (tooltip, settings) |
| §10 Error handling | 2 (corrupt files, log), 11 (friendly errors), 17 (skin fallback), 18 ("hmm, one sec…") |
| §11 First run | 14, 18, 19 |
| §12 Settings | 3, 16, 19 |
| §13 Platform & build | 1, 20 |
| §14 Testing | every task (unit), 12 (smoke), 20 (footprint, checklist) |

