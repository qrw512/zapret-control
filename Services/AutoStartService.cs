using System;
using System.IO;
using Microsoft.Win32;

namespace ZapretGui.Services;

public static class AutoStartService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "ZapretControl";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
            var value = key?.GetValue(AppName) as string;
            return !string.IsNullOrEmpty(value);
        }
        catch { return false; }
    }

    public static bool Enable()
    {
        try
        {
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath)) return false;

            var exeName = Path.GetFileNameWithoutExtension(exePath);
            if (exeName.Equals("dotnet", StringComparison.OrdinalIgnoreCase)) return false;

            using var key = Registry.CurrentUser.OpenSubKey(RunKey, true)
                            ?? Registry.CurrentUser.CreateSubKey(RunKey);

            key?.SetValue(AppName, $"\"{exePath}\" --autostart");
            return true;
        }
        catch { return false; }
    }

    public static void Disable()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
            key?.DeleteValue(AppName, false);
        }
        catch { }
    }
}
