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
