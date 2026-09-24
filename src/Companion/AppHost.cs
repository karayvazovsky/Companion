using System.IO;
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
