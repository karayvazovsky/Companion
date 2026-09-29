using System.Windows.Threading;

namespace Companion;

/// <summary>
/// Gives memory back to Windows shortly after the app has been used. <see cref="Request"/> is debounced: every call
/// restarts a short timer, and the working set is trimmed once things have been quiet for a moment. While the app is
/// busy (dragging, mid-teleport, chatting) the trim waits another round. A slow timer also requests a trim now and
/// then so a long idle stays small.
/// </summary>
internal sealed class TrimScheduler : IDisposable
{
    private readonly Func<bool> _isBusy;
    private readonly Action _trim;
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly DispatcherTimer _startup = new() { Interval = TimeSpan.FromSeconds(10) };
    private readonly DispatcherTimer _periodic = new() { Interval = TimeSpan.FromMinutes(10) };
    private bool _disposed;

    public TrimScheduler(Func<bool> isBusy, Action? trim = null)
    {
        _isBusy = isBusy;
        _trim = trim ?? MemoryTrim.Now;
        _debounce.Tick += (_, _) => OnDebounceElapsed();
        _startup.Tick += (_, _) =>
        {
            _startup.Stop();
            Request();
        };
        _periodic.Tick += (_, _) => Request();
    }

    /// <summary>True while a trim is waiting for its quiet moment.</summary>
    internal bool IsPending => _debounce.IsEnabled;

    /// <summary>Schedules the one-shot startup trim (10 s) and the periodic idle trim (10 min).</summary>
    public void Start()
    {
        if (_disposed) return;
        _startup.Start();
        _periodic.Start();
    }

    /// <summary>Trims about 3 s from now; calling again before then pushes it back.</summary>
    public void Request()
    {
        if (_disposed) return;
        _debounce.Stop();
        _debounce.Start();
    }

    public void Dispose()
    {
        _disposed = true;
        _debounce.Stop();
        _startup.Stop();
        _periodic.Stop();
    }

    /// <summary>The debounce fired: trim if the app is quiet, otherwise wait another round.</summary>
    internal void OnDebounceElapsed()
    {
        if (_disposed) return;
        _debounce.Stop();
        if (_isBusy())
        {
            _debounce.Start();
            return;
        }
        _trim();
    }
}
