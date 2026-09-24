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
