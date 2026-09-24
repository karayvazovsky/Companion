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
    private readonly DispatcherTimer _poofTimer = new(DispatcherPriority.Normal) { Interval = PoofDuration };
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
    private Action? _afterPoof;
    private int _shownFrame = -1;
    private bool _suspended;
    private bool _held;
    private bool _disposed;
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
        _poofTimer.Tick += (_, _) => OnPoofTick();
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

    /// <summary>Re-evaluates the pet now (called every second and right after user interactions).</summary>
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
            if (!_held && _fallTargetY is null && _overrideAnimation is null) Reperch();   // taskbar moved, monitor unplugged…
        }
        Play(AnimationFor(tick.State));
        if (!_held) _window.SetFacing(tick.State == PetStateKind.Walk && _perch.CanWalk ? _life.WalkDirection : 1);
        if (tick.Line is { } line) LineReady?.Invoke(line);
        MaybeAutoTeleport(tick.State, activity);
    }

    public void ApplyScale()
    {
        if (_window.SpriteSize == Size) return;   // settings saved without a size change: stay on the current perch
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
        AfterPoof(() =>
        {
            _screens = _desktop.Screens();
            _windows = _desktop.Windows();
            SetPerch(PerchPlanner.Random(_rng, Size, _screens, _windows));
            Play("poof", restart: true);
            AfterPoof(() =>
            {
                _overrideAnimation = null;
                Refresh();
            });
        });
    }

    public void Dispose()
    {
        _disposed = true;
        _lifeTimer.Stop();
        _animationTimer.Stop();
        _motionTimer.Stop();
        _followTimer.Stop();
        CancelTeleport();
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

    /// <summary>
    /// Re-derives the perch against the current screens and sprite size so it stays on-screen; WindowTop is the follow
    /// timer's job. Only moves the pet when the result differs (keeps the ceiling swing going).
    /// </summary>
    private void Reperch()
    {
        var next = _perch.Kind switch
        {
            PerchKind.WindowTop => _perch,
            PerchKind.Floor => PerchPlanner.Fall(_perch.X, Size, _perch.Y, Screens()),
            _ => PerchPlanner.ForDrop(_perch.X, _perch.Y, Size, Screens(), []),
        };
        if (next != _perch) SetPerch(next);
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

    /// <summary>Motion (30 fps) only while held, falling or hanging from the ceiling; following only on a window top.</summary>
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
            CancelTeleport();
            _animationTimer.Stop();
            _window.Hide();
        }
        else
        {
            // Screens or the pet size may have changed while hidden (a fall in progress just lands).
            _window.Show();
            _screens = _desktop.Screens();
            Reperch();
            _window.SpritePosition = new Point(_perch.X, _perch.Y);
            ApplyPose();
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
        _nextTeleport = DateTime.Now + NextTeleportDelay();
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
        CancelTeleport();   // grabbed mid-poof: stay in the hand, don't jump to the random perch
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
        if (_disposed) return;   // capture is lost while the window closes
        _held = false;
        _nextTeleport = DateTime.Now + NextTeleportDelay();   // the user just placed it: don't whisk it away
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

    /// <summary>Runs <paramref name="action"/> once the current poof has played. One step pending at a time.</summary>
    private void AfterPoof(Action action)
    {
        _afterPoof = action;
        _poofTimer.Stop();
        _poofTimer.Start();
    }

    private void OnPoofTick()
    {
        _poofTimer.Stop();
        var next = _afterPoof;
        _afterPoof = null;
        next?.Invoke();
    }

    /// <summary>Abandons a teleport in progress (hidden or shutting down); the pet stays wherever it is now.</summary>
    private void CancelTeleport()
    {
        _poofTimer.Stop();
        _afterPoof = null;
        _overrideAnimation = null;
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
