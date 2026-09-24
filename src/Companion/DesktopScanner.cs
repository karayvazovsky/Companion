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
