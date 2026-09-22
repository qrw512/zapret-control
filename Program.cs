using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using ZapretGui.Services;

namespace ZapretGui;

class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            WriteCrashLog("AppDomain.UnhandledException", e.ExceptionObject as Exception);

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            WriteCrashLog("TaskScheduler.UnobservedTaskException", e.Exception);
            e.SetObserved();
        };

        try
        {
            RunApp(args);
        }
        catch (Exception ex)
        {
            WriteCrashLog("Main", ex);
            throw;
        }
    }

    private static void RunApp(string[] args)
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

    public static void WriteCrashLog(string source, Exception? ex)
    {
        try
        {
            var logsDir = AppPaths.Logs;
            Directory.CreateDirectory(logsDir);

            var path = Path.Combine(logsDir, $"crash-{DateTime.Now:yyyy-MM-dd-HH-mm-ss}.log");
            var sb = new StringBuilder();

            sb.AppendLine("=== CRASH ===");
            sb.AppendLine($"Source: {source}");
            sb.AppendLine($"Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
            sb.AppendLine($"OS: {Environment.OSVersion}");
            sb.AppendLine($".NET: {Environment.Version}");
            sb.AppendLine($"App version: {typeof(Program).Assembly.GetName().Version}");
            sb.AppendLine($"Command line: {Environment.CommandLine}");
            sb.AppendLine();

            if (ex != null)
            {
                sb.AppendLine("--- Exception ---");
                sb.AppendLine($"Type: {ex.GetType().FullName}");
                sb.AppendLine($"Message: {ex.Message}");
                sb.AppendLine();
                sb.AppendLine("--- StackTrace ---");
                sb.AppendLine(ex.StackTrace);
                sb.AppendLine();

                var inner = ex.InnerException;
                int depth = 1;
                while (inner != null)
                {
                    sb.AppendLine($"--- Inner Exception #{depth} ---");
                    sb.AppendLine($"Type: {inner.GetType().FullName}");
                    sb.AppendLine($"Message: {inner.Message}");
                    sb.AppendLine(inner.StackTrace);
                    sb.AppendLine();
                    inner = inner.InnerException;
                    depth++;
                }
            }
            else
            {
                sb.AppendLine("Exception object is null.");
            }

            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        }
        catch
        {
            // если не удалось записать лог — молча игнорируем
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
            var processName = Path.GetFileNameWithoutExtension(exePath);
            if (processName.Equals("dotnet", StringComparison.OrdinalIgnoreCase))
                return false;

            var cmdArgs = Environment.GetCommandLineArgs().Skip(1).Select(QuoteIfNeeded).ToList();
            var argString = string.Join(" ", cmdArgs);

            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = argString,
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = AppContext.BaseDirectory
            };

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
