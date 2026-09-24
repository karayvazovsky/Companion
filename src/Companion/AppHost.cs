using System.IO;
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
