using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
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

    private static void Log(string message)
    {
        try
        {
            var dir = AppPaths.Logs;
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, $"selfupdate-{DateTime.Now:yyyy-MM-dd}.log");
            File.AppendAllText(file, $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}");
        }
        catch { }
    }

    public string GetCurrentVersion()
    {
        var v = Assembly.GetExecutingAssembly().GetName().Version;
        return v == null ? "0.0.0" : $"{v.Major}.{v.Minor}.{v.Build}";
    }

    public async Task<SelfUpdateInfo?> GetLatestAsync()
    {
        try
        {
            Log("=== Проверка обновлений ===");

            var github = new GitHubClient(new ProductHeaderValue("ZapretControl"));

            var releases = await github.Repository.Release.GetAll(RepoOwner, RepoName);
            Log($"Получено релизов: {releases?.Count ?? 0}");

            if (releases == null || releases.Count == 0) return null;

            var latest = releases
                .Where(r => !r.Prerelease)
                .OrderByDescending(r => r.PublishedAt)
                .FirstOrDefault();

            if (latest == null) return null;

            Log($"Последний релиз: {latest.TagName} ({latest.PublishedAt})");

            var zip = latest.Assets.FirstOrDefault(a =>
                a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));

            if (zip == null)
            {
                var names = string.Join(", ", latest.Assets.Select(a => a.Name));
                Log($"Ошибка: нет .zip в релизе. Ассеты: [{names}]");
                return null;
            }

            return new SelfUpdateInfo
            {
                Version = latest.TagName.TrimStart('v', 'V'),
                DownloadUrl = zip.BrowserDownloadUrl
            };
        }
        catch (Exception ex)
        {
            Log($"Ошибка GetLatestAsync: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    public bool IsNewerVersion(string latest, string current)
    {
        return NormalizeVersion(latest) > NormalizeVersion(current);
    }

    private static Version NormalizeVersion(string v)
    {
        var parts = (v ?? string.Empty).Split('.');
        int major = parts.Length > 0 && int.TryParse(parts[0], out var a) ? a : 0;
        int minor = parts.Length > 1 && int.TryParse(parts[1], out var b) ? b : 0;
        int build = parts.Length > 2 && int.TryParse(parts[2], out var d) ? d : 0;
        return new Version(major, minor, build);
    }

    public async Task<bool> PrepareUpdateAsync(string downloadUrl, Action<long, long>? onProgress = null)
    {
        Log($"=== Подготовка обновления: {downloadUrl} ===");
        try
        {
            var currentExe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(currentExe))
            {
                Log("Ошибка: ProcessPath пуст.");
                return false;
            }

            var exeName = Path.GetFileNameWithoutExtension(currentExe);
            if (exeName.Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            {
                Log("Ошибка: запущено через `dotnet run`.");
                return false;
            }
            Log($"Текущий EXE: {currentExe}");

            var workDir = Path.Combine(Path.GetTempPath(), "ZapretControl_update");
            if (Directory.Exists(workDir)) Directory.Delete(workDir, true);
            Directory.CreateDirectory(workDir);
            Log($"Work dir: {workDir}");

            var zipPath = Path.Combine(workDir, "update.zip");

            using var handler = new SocketsHttpHandler
            {
                ConnectTimeout = TimeSpan.FromSeconds(30),
                PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            };

            using var http = new HttpClient(handler)
            {
                Timeout = System.Threading.Timeout.InfiniteTimeSpan,
            };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("ZapretControl");

            Log("Скачивание...");
            long totalBytes = 0;
            long downloadedBytes = 0;

            // Таймаут молчания сети: если 60 секунд ни байта — прерываем
            using var idleCts = new CancellationTokenSource();

            using (var response = await http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                totalBytes = response.Content.Headers.ContentLength ?? 0;
                Log($"Размер: {totalBytes} байт ({totalBytes / 1024.0 / 1024.0:F1} МБ)");

                await using var sourceStream = await response.Content.ReadAsStreamAsync();
                await using var fileStream = new FileStream(
                    zipPath,
                    System.IO.FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 81920,
                    useAsync: true);

                var buffer = new byte[81920];
                int read;
                var lastPercent = -1;

                while (true)
                {
                    using var readCts = CancellationTokenSource.CreateLinkedTokenSource(idleCts.Token);
                    readCts.CancelAfter(TimeSpan.FromSeconds(60));

                    try
                    {
                        read = await sourceStream.ReadAsync(buffer.AsMemory(0, buffer.Length), readCts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        Log($"Ошибка: сеть молчит больше 60 секунд. Скачано {downloadedBytes} байт.");
                        return false;
                    }

                    if (read <= 0) break;

                    await fileStream.WriteAsync(buffer.AsMemory(0, read));
                    downloadedBytes += read;

                    if (totalBytes > 0)
                    {
                        var percent = (int)(downloadedBytes * 100 / totalBytes);
                        if (percent != lastPercent)
                        {
                            lastPercent = percent;
                            onProgress?.Invoke(downloadedBytes, totalBytes);
                            if (percent % 20 == 0)
                                Log($"  ...{percent}%");
                        }
                    }
                }
            }

            Log($"Скачано {downloadedBytes} байт.");
            onProgress?.Invoke(downloadedBytes, totalBytes);

            var extractDir = Path.Combine(workDir, "extracted");
            Directory.CreateDirectory(extractDir);
            ZipFile.ExtractToDirectory(zipPath, extractDir, overwriteFiles: true);
            Log("Распаковано.");

            var currentName = Path.GetFileName(currentExe);
            var newExe = Directory.EnumerateFiles(extractDir, currentName, SearchOption.AllDirectories).FirstOrDefault()
                      ?? Directory.EnumerateFiles(extractDir, "*.exe", SearchOption.AllDirectories).FirstOrDefault();

            if (newExe == null)
            {
                var contents = string.Join(", ", Directory.EnumerateFileSystemEntries(extractDir));
                Log($"Ошибка: exe не найден. Содержимое: {contents}");
                return false;
            }
            Log($"Новый EXE: {newExe}");

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
            Log($"BAT готов: {batPath}");

            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"\"{batPath}\"\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            Process.Start(psi);
            Log("Updater запущен.");
            return true;
        }
        catch (Exception ex)
        {
            Log($"Ошибка PrepareUpdateAsync: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }
}
