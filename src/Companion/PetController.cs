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
