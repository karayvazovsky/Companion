using System.Windows.Threading;

namespace Companion;

/// <summary>
/// Gives memory back to Windows shortly after the app has been used. <see cref="Request"/> is debounced: every call
/// restarts a short timer, and the working set is trimmed once things have been quiet for a moment. A slow timer
/// also requests a trim now and then so a long idle stays small.
/// </summary>
internal sealed class TrimScheduler : IDisposable
{
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly DispatcherTimer _periodic = new() { Interval = TimeSpan.FromMinutes(10) };

    public TrimScheduler()
    {
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            MemoryTrim.Now();
        };
        _periodic.Tick += (_, _) => Request();
    }

    /// <summary>Starts the periodic idle trim.</summary>
    public void Start() => _periodic.Start();

    /// <summary>Trims about 3 s from now; calling again before then pushes it back.</summary>
    public void Request()
    {
        _debounce.Stop();
        _debounce.Start();
    }

    public void Dispose()
    {
        _debounce.Stop();
        _periodic.Stop();
    }
}
