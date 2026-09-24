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
