using System;
using System.Diagnostics;
using System.IO;

namespace ZapretGui.Services;

public class ZapretManager
{
    private Process? _process;
    private string? _lastHiddenBatPath;
    private string? _vbsPath;

    public bool StartZapret(string batchPath)
    {
        try
        {
            if (!File.Exists(batchPath)) return false;
            var batchDir = Path.GetDirectoryName(batchPath)!;
            var tempDir = Path.Combine(Path.GetTempPath(), "ZapretGui_run");
            Directory.CreateDirectory(tempDir);

            _lastHiddenBatPath = Path.Combine(tempDir, "run_hidden.bat");
            _vbsPath = Path.Combine(tempDir, "run_silent.vbs");

            string content = File.ReadAllText(batchPath);

            content = content.Replace("start \"zapret: %~n0\" /min \"%BIN%winws.exe\"", "\"%BIN%winws.exe\"")
                             .Replace("start \"zapret: %~n0\" \"%BIN%winws.exe\"", "\"%BIN%winws.exe\"")
                             .Replace("start \"\" /min \"%BIN%winws.exe\"", "\"%BIN%winws.exe\"")
                             .Replace("start \"\" \"%BIN%winws.exe\"", "\"%BIN%winws.exe\"");

            content = content.Replace("%~dp0", batchDir + Path.DirectorySeparatorChar);

            File.WriteAllText(_lastHiddenBatPath, content);

            string vbsContent = "Set WshShell = CreateObject(\"WScript.Shell\")\r\n" +
                                $"WshShell.Run \"cmd.exe /c \" & Chr(34) & \"{_lastHiddenBatPath}\" & Chr(34), 0, False";

            File.WriteAllText(_vbsPath, vbsContent);

            var startInfo = new ProcessStartInfo
            {
                FileName = "wscript.exe",
                Arguments = $"\"{_vbsPath}\"",
                WorkingDirectory = batchDir,
                UseShellExecute = true,
                Verb = "runas"
            };

            _process = Process.Start(startInfo);
            return _process != null;
        }
        catch
        {
            return false;
        }
    }

    public void StopZapret()
    {
        foreach (var proc in Process.GetProcessesByName("winws"))
        {
            try { proc.Kill(); } catch { }
        }

        try
        {
            if (!string.IsNullOrEmpty(_lastHiddenBatPath) && File.Exists(_lastHiddenBatPath))
                File.Delete(_lastHiddenBatPath);

            if (!string.IsNullOrEmpty(_vbsPath) && File.Exists(_vbsPath))
                File.Delete(_vbsPath);
        }
        catch { }
    }
}
