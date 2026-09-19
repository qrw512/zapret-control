using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Octokit;

namespace ZapretGui.Services;

public class SelfUpdateInfo
{
    public string Version { get; set; } = string.Empty;
    public string DownloadUrl { get; set; } = string.Empty;
}

public class SelfUpdateService
{
    private const string RepoOwner = "qrw512";
    private const string RepoName  = "zapret-control";
    public string GetCurrentVersion()
    {
        var v = Assembly.GetExecutingAssembly().GetName().Version;
        return v == null ? "0.0" : $"{v.Major}.{v.Minor}";
    }

    public async Task<SelfUpdateInfo?> GetLatestAsync()
    {
        try
        {
            var github = new GitHubClient(new ProductHeaderValue("ZapretControl"));
            var latest = await github.Repository.Release.GetLatest(RepoOwner, RepoName);

            var zip = latest.Assets.FirstOrDefault(a =>
                a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
            if (zip == null) return null;

            return new SelfUpdateInfo
            {
                Version = latest.TagName.TrimStart('v', 'V'),
                DownloadUrl = zip.BrowserDownloadUrl
            };
        }
        catch
        {
            return null;
        }
    }

    public bool IsNewerVersion(string latest, string current)
    {
        if (Version.TryParse(latest, out var lv) && Version.TryParse(current, out var cv))
            return lv > cv;
        return false;
    }

    public async Task<bool> PrepareUpdateAsync(string downloadUrl)
    {
        try
        {
            var currentExe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(currentExe)) return false;

            var exeName = Path.GetFileNameWithoutExtension(currentExe);
            if (exeName.Equals("dotnet", StringComparison.OrdinalIgnoreCase))
                return false;

            var workDir = Path.Combine(Path.GetTempPath(), "ZapretControl_update");
            if (Directory.Exists(workDir)) Directory.Delete(workDir, true);
            Directory.CreateDirectory(workDir);

            using var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("ZapretControl");
            var zipBytes = await http.GetByteArrayAsync(downloadUrl);
            var zipPath = Path.Combine(workDir, "update.zip");
            await File.WriteAllBytesAsync(zipPath, zipBytes);

            var extractDir = Path.Combine(workDir, "extracted");
            Directory.CreateDirectory(extractDir);
            ZipFile.ExtractToDirectory(zipPath, extractDir, overwriteFiles: true);

            var currentName = Path.GetFileName(currentExe);
            var newExe = Directory.EnumerateFiles(extractDir, currentName, SearchOption.AllDirectories).FirstOrDefault()
                      ?? Directory.EnumerateFiles(extractDir, "*.exe", SearchOption.AllDirectories).FirstOrDefault();
            if (newExe == null) return false;

            var batPath = Path.Combine(workDir, "apply_update.bat");
            var bat = new StringBuilder()
                .AppendLine("@echo off")
                .AppendLine(":wait")
                .AppendLine("timeout /t 1 /nobreak >nul")
                .AppendLine($"tasklist /FI \"IMAGENAME eq {currentName}\" 2>nul | find /I \"{currentName}\" >nul")
                .AppendLine("if \"%ERRORLEVEL%\"==\"0\" goto wait")
                .AppendLine($"copy /Y \"{newExe}\" \"{currentExe}\" >nul")
                .AppendLine($"start \"\" \"{currentExe}\"")
                .AppendLine($"del \"{zipPath}\" >nul 2>&1")
                .AppendLine($"rmdir /S /Q \"{extractDir}\" >nul 2>&1")
                .AppendLine("(goto) 2>nul & del \"%~f0\"")
                .ToString();

            await File.WriteAllTextAsync(batPath, bat, new UTF8Encoding(false));

            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"\"{batPath}\"\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            Process.Start(psi);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
