using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Principal;
using Avalonia;
using ZapretGui.Services;

namespace ZapretGui;

class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        AppPaths.EnsureCreated();

        if (OperatingSystem.IsWindows() && !IsAdmin())
        {
            if (TryRelaunchElevated())
                return;
        }
        
        if (!SingleInstanceService.TryAcquire())
        {
            SingleInstanceService.SendShowSignal();
            return;
        }

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            SingleInstanceService.Stop();
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();

    private static bool IsAdmin()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }

    private static bool TryRelaunchElevated()
    {
        try
        {
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath)) return false;

            var cmdArgs = Environment.GetCommandLineArgs().Skip(1).Select(QuoteIfNeeded).ToList();
            var argString = string.Join(" ", cmdArgs);

            ProcessStartInfo psi;
            var processName = Path.GetFileNameWithoutExtension(exePath);

            if (processName.Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            {
                var dllPath = Assembly.GetEntryAssembly()?.Location;
                if (string.IsNullOrEmpty(dllPath)) return false;

                psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = $"\"{dllPath}\" {argString}".Trim(),
                    UseShellExecute = true,
                    Verb = "runas",
                    WorkingDirectory = AppContext.BaseDirectory
                };
            }
            else
            {
                psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = argString,
                    UseShellExecute = true,
                    Verb = "runas",
                    WorkingDirectory = AppContext.BaseDirectory
                };
            }

            Process.Start(psi);
            return true;
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return false;
        }
        catch
        {
            return false;
        }
    }

    private static string QuoteIfNeeded(string s)
        => s.Contains(' ') ? "\"" + s.Replace("\"", "\\\"") + "\"" : s;
}
