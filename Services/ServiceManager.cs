using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ZapretGui.Services;
using System.Security.Cryptography;

namespace ZapretGui.Models
{
    public class ServiceManager
    {
        private readonly string _baseDir;
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };
        private static readonly Regex AnsiRegex = new(@"\x1B\[[0-9;]*[A-Za-z]", RegexOptions.Compiled);
        private static readonly object _logLock = new();

        private const int ProcessTimeoutMs = 5 * 60 * 1000;

        private const string IpsetUrl = "https://raw.githubusercontent.com/Flowseal/zapret-discord-youtube/refs/heads/main/.service/ipset-service.txt";
        private const string HostsUrl = "https://raw.githubusercontent.com/Flowseal/zapret-discord-youtube/refs/heads/main/.service/hosts";

        private static LocalizationService Loc => LocalizationService.Instance;
        private static string L(string key) => Loc[key];
        private static string Lf(string key, params object[] args) => string.Format(Loc[key], args);

        public ServiceManager(string zapretFolderPath)
        {
            _baseDir = ResolveWorkingDir(zapretFolderPath);
            Log($"=== ServiceManager constructed === baseDir={_baseDir}");
        }


        private void Log(string message)
        {
            try
            {
                var dir = AppPaths.Logs;
                Directory.CreateDirectory(dir);
                var file = Path.Combine(dir, $"service-{DateTime.Now:yyyy-MM-dd}.log");
                lock (_logLock)
                    File.AppendAllText(file, $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}");
            }
            catch { }
        }

        private static string ResolveWorkingDir(string root)
        {
            try
            {
                if (!Directory.Exists(root)) return root;
                if (File.Exists(Path.Combine(root, "service.bat"))) return root;

                var versionDirs = Directory.GetDirectories(root)
                    .Where(d => File.Exists(Path.Combine(d, "service.bat")))
                    .OrderByDescending(d => Directory.GetLastWriteTimeUtc(d))
                    .ToList();

                if (versionDirs.Count > 0) return versionDirs[0];

                var nested = Directory.GetFiles(root, "service.bat", SearchOption.AllDirectories);
                if (nested.Length > 0)
                {
                    var latest = nested.OrderByDescending(File.GetLastWriteTimeUtc).First();
                    return Path.GetDirectoryName(latest)!;
                }
            }
            catch { }
            return root;
        }

        public static bool IsAdmin()
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }

        public async Task<(bool success, string output)> RunAsync(
            string command, string? argument = null, string? subArgument = null)
        {
            Log($">>> RunAsync: command='{command}', argument='{argument}', sub='{subArgument}'");

            var (ok, output) = command switch
            {
                "status"        => await RunStatusAsync(),
                "diagnostics"   => await RunDiagnosticsAsync(),
                "install"       => await RunInstallAsync(argument),
                "remove"        => await RunRemoveAsync(),
                "ipset_update"  => await RunIpsetUpdateAsync(),
                "hosts_check"   => await RunHostsCheckAsync(),
                "run_tests"     => await RunTestsAsync(argument, subArgument),
                "replace_fakes" => await RunReplaceFakesAsync(argument),
                "game_filter"   => await RunGameFilterAsync(argument),
                _               => (false, $"Unknown command: {command}")
            };

            Log($"<<< RunAsync finished: command='{command}', ok={ok}, outputLen={output?.Length ?? 0}");
            return (ok, output ?? string.Empty);
        }

        public async Task<bool> CheckServiceInstalledAsync()
        {
            var (_, output) = await RunProcessAsync("sc", "query zapret", null);
            return !output.Contains("1060");
        }

        public int GetGameFilterMode()
        {
            var flagFile = Path.Combine(_baseDir, "utils", "game_filter.enabled");
            if (!File.Exists(flagFile)) return 0;
            try
            {
                var content = File.ReadAllText(flagFile).Trim().ToLowerInvariant();
                return content switch { "all" => 1, "tcp" => 2, "udp" => 3, _ => 0 };
            }
            catch { return 0; }
        }

        public ReplaceFakesInfo GetReplaceFakesInfo()
        {
            var info = new ReplaceFakesInfo();
            var binDir = Path.Combine(_baseDir, "bin");

            if (!Directory.Exists(binDir))
                return info;

            try
            {
                var discordActive = Path.Combine(binDir, "ACTIVE_DISCORD_UDP.bin");
                var gameActive    = Path.Combine(binDir, "ACTIVE_GAME_UDP.bin");

                var discordHash = File.Exists(discordActive) ? GetFileHash(discordActive) : null;
                var gameHash    = File.Exists(gameActive)    ? GetFileHash(gameActive)    : null;

                var fakes = Directory.EnumerateFiles(binDir, "*.bin")
                    .Where(f => !Path.GetFileName(f).StartsWith("ACTIVE_", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(f => Path.GetFileName(f))
                    .ToList();

                for (int i = 0; i < fakes.Count; i++)
                {
                    var name = Path.GetFileNameWithoutExtension(fakes[i]);
                    var hash = GetFileHash(fakes[i]);

                    info.Files.Add(new FakeFileItem { Index = i + 1, Name = name });

                    if (discordHash != null && hash == discordHash)
                        info.CurrentDiscordFake = name;
                    if (gameHash != null && hash == gameHash)
                        info.CurrentGameFake = name;
                }
            }
            catch (Exception ex)
            {
                Log($"GetReplaceFakesInfo failed: {ex.Message}");
            }

            return info;
        }

        private static string GetFileHash(string path)
        {
            try
            {
                using var stream = File.OpenRead(path);
                var hash = SHA256.HashData(stream);
                return Convert.ToHexString(hash);
            }
            catch { return string.Empty; }
        }


        private async Task<(bool, string)> RunStatusAsync()
        {
            var sb = new StringBuilder();
            if (!IsAdmin()) sb.AppendLine(L("MsgNoAdmin"));

            sb.AppendLine(await QueryServiceStateAsync("zapret"));
            sb.AppendLine(await QueryServiceStateAsync("WinDivert"));
            sb.AppendLine();
            sb.AppendLine(await QueryProcessAsync("winws.exe"));
            return (true, sb.ToString());
        }

        private async Task<string> QueryServiceStateAsync(string name)
        {
            var (_, output) = await RunProcessAsync("sc", $"query \"{name}\"", null);
            if (output.Contains("1060") || output.Contains("FAILED 1060"))
                return Lf("MsgServiceNotInstalledFmt", name);

            var m = Regex.Match(output, @"STATE\s*:\s*\d+\s+(\w+)");
            if (!m.Success) return Lf("MsgServiceNotRunning", name);

            return m.Groups[1].Value.Equals("RUNNING", StringComparison.OrdinalIgnoreCase)
                ? Lf("MsgServiceRunning", name)
                : Lf("MsgServiceNotRunning", name);
        }

        private async Task<string> QueryProcessAsync(string exeName)
        {
            var (_, output) = await RunProcessAsync("tasklist", $"/FI \"IMAGENAME eq {exeName}\"", null);
            return output.Contains(exeName, StringComparison.OrdinalIgnoreCase)
                ? L("MsgBypassRunning")
                : L("MsgBypassNotRunning");
        }


        private async Task<(bool, string)> RunDiagnosticsAsync()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"[INFO] Zapret: {_baseDir}");
            sb.AppendLine(IsAdmin() ? "[OK] Administrator rights detected" : "[X] No administrator rights");
            sb.AppendLine();

            var bfe = await QueryServiceStateAsync("BFE");
            sb.AppendLine(bfe.Contains("RUNNING", StringComparison.OrdinalIgnoreCase)
                ? "[OK] Base Filtering Engine check passed"
                : "[X] Base Filtering Engine is not running (required!)");
            sb.AppendLine();

            var (_, proxyOut) = await RunProcessAsync("reg",
                "query \"HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Internet Settings\" /v ProxyEnable", null);
            sb.AppendLine(proxyOut.Contains("0x1") ? "[?] System proxy is enabled" : "[OK] Proxy check passed");
            sb.AppendLine();

            var (_, tcpOut) = await RunProcessAsync("netsh", "interface tcp show global", null);
            if (tcpOut.Contains("timestamps", StringComparison.OrdinalIgnoreCase)
                && tcpOut.Contains("enabled", StringComparison.OrdinalIgnoreCase))
                sb.AppendLine("[OK] TCP timestamps check passed");
            else
            {
                await RunProcessAsync("netsh", "interface tcp set global timestamps=enabled", null);
                sb.AppendLine("[OK] TCP timestamps enabled");
            }
            sb.AppendLine();

            var (_, services) = await RunProcessAsync("sc", "query", null);

            var (_, adguard) = await RunProcessAsync("tasklist", "/FI \"IMAGENAME eq AdguardSvc.exe\"", null);
            sb.AppendLine(adguard.Contains("AdguardSvc.exe") ? "[X] Adguard process found" : "[OK] Adguard check passed");
            sb.AppendLine();

            sb.AppendLine(services.Contains("Killer", StringComparison.OrdinalIgnoreCase)
                ? "[X] Killer services found" : "[OK] Killer check passed");
            sb.AppendLine();

            bool intelConflict = services.Contains("Connectivity", StringComparison.OrdinalIgnoreCase)
                && services.Contains("Network", StringComparison.OrdinalIgnoreCase);
            sb.AppendLine(intelConflict ? "[X] Intel Connectivity Network Service found" : "[OK] Intel Connectivity check passed");
            sb.AppendLine();

            bool cp = services.Contains("TracSrvWrapper") || services.Contains("EPWD");
            sb.AppendLine(cp ? "[X] Check Point services found" : "[OK] Check Point check passed");
            sb.AppendLine();

            sb.AppendLine(services.Contains("SmartByte", StringComparison.OrdinalIgnoreCase)
                ? "[X] SmartByte services found" : "[OK] SmartByte check passed");
            sb.AppendLine();

            bool hasCyr = _baseDir.Any(c => c >= 0x0400 && c <= 0x04FF);
            sb.AppendLine(hasCyr ? "[?] Path contains Cyrillic characters" : "[OK] Cyrillic path check passed");
            sb.AppendLine();

            var oneDrive = Environment.GetEnvironmentVariable("OneDrive");
            bool inOneDrive = !string.IsNullOrEmpty(oneDrive)
                && _baseDir.StartsWith(oneDrive, StringComparison.OrdinalIgnoreCase);
            sb.AppendLine(inOneDrive ? "[X] Zapret is in a OneDrive folder" : "[OK] OneDrive check passed");
            sb.AppendLine();

            var binDir = Path.Combine(_baseDir, "bin");
            bool hasSys = Directory.Exists(binDir) && Directory.EnumerateFiles(binDir, "*.sys").Any();
            sb.AppendLine(hasSys ? "[OK] WinDivert driver file present" : "[X] WinDivert64.sys NOT found");
            sb.AppendLine();

            bool hasVpn = services.Contains("VPN", StringComparison.OrdinalIgnoreCase);
            sb.AppendLine(hasVpn ? "[?] VPN services found" : "[OK] VPN check passed");
            sb.AppendLine();

            var winws = await QueryProcessAsync("winws.exe");
            var windivert = await QueryServiceStateAsync("WinDivert");
            if (winws.Contains("NOT", StringComparison.OrdinalIgnoreCase)
                && windivert.Contains("RUNNING", StringComparison.OrdinalIgnoreCase))
            {
                sb.AppendLine("[?] winws.exe not running but WinDivert is active. Removing...");
                await RunProcessAsync("net", "stop WinDivert", null);
                await RunProcessAsync("sc", "delete WinDivert", null);
                var recheck = await QueryServiceStateAsync("WinDivert");
                sb.AppendLine(recheck.Contains("NOT", StringComparison.OrdinalIgnoreCase)
                    ? "[OK] WinDivert successfully removed"
                    : "[X] Failed to remove WinDivert");
                sb.AppendLine();
            }

            var conflicts = new[] { "GoodbyeDPI", "discordfix_zapret", "winws1", "winws2" }
                .Where(s => Regex.IsMatch(services, $@"SERVICE_NAME:\s*{s}\b", RegexOptions.IgnoreCase))
                .ToList();
            sb.AppendLine(conflicts.Count > 0
                ? $"[X] Conflicting services found: {string.Join(", ", conflicts)}"
                : "[OK] No conflicting bypass services");

            return (true, sb.ToString());
        }

        private static string FilterTestOutput(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return raw;

            var lines = raw.Replace("\r\n", "\n").Split('\n');
            var result = new List<string>();
            bool capturing = false;
            bool everStarted = false;

            foreach (var line in lines)
            {
                var trimmed = line.TrimEnd();

                if (!capturing)
                {
                    var t = trimmed.Trim();
                    if (t.Length >= 20 && t.All(c => c == '='))
                    {
                        capturing = true;
                        everStarted = true;
                    }
                    else
                    {
                        continue;
                    }
                }

                if (trimmed.StartsWith("Results saved to")) break;
                if (trimmed.StartsWith("[INFO] Restoring")) break;
                if (trimmed.StartsWith("Press any key"))    break;

                if (trimmed.Trim() == "All tests finished.") continue;

                result.Add(trimmed);
            }

            if (!everStarted) return raw;

            while (result.Count > 0 && string.IsNullOrWhiteSpace(result[^1]))
                result.RemoveAt(result.Count - 1);

            return string.Join(Environment.NewLine, result);
        }

        private async Task<(bool, string)> RunTestsAsync(string? typeArg, string? currentPresetPath)
        {
            Log($"RunTestsAsync started, typeArg='{typeArg}', currentPreset='{currentPresetPath}'");

            if (!IsAdmin())
            {
                var msg = L("MsgNeedAdmin") + "\r\n\r\n" + L("MsgTestsNeedAdmin");
                Log("Tests aborted: not admin");
                return (false, msg);
            }

            var ps1 = Path.Combine(_baseDir, "utils", "test zapret.ps1");
            if (!File.Exists(ps1))
            {
                var cand = Directory.Exists(_baseDir)
                    ? Directory.EnumerateFiles(_baseDir, "test*.ps1", SearchOption.AllDirectories).ToList()
                    : new List<string>();
                if (cand.Count > 0) ps1 = cand[0];
                else
                {
                    Log("test zapret.ps1 not found");
                    return (false, "test zapret.ps1 not found");
                }
            }
            Log($"PS1 path: {ps1}");

            var isCurrentMode = !string.IsNullOrWhiteSpace(currentPresetPath);
            var hiddenFiles = new List<(string hidden, string original)>();

            if (isCurrentMode)
            {
                try
                {
                    var target = Path.GetFullPath(currentPresetPath!);
                    foreach (var bat in Directory.GetFiles(_baseDir, "*.bat", SearchOption.TopDirectoryOnly))
                    {
                        if (Path.GetFullPath(bat).Equals(target, StringComparison.OrdinalIgnoreCase))
                            continue;

                        var hidden = bat + ".gui_hidden";
                        File.Move(bat, hidden, overwrite: true);
                        hiddenFiles.Add((hidden, bat));
                    }
                    Log($"Current-mode test: hidden {hiddenFiles.Count} other .bat files");
                }
                catch (Exception ex)
                {
                    Log($"Failed to hide other .bat files: {ex.Message}");
                    foreach (var (h, o) in hiddenFiles)
                        try { File.Move(h, o, overwrite: true); } catch { }
                    return (false, $"Failed to prepare test: {ex.Message}");
                }
            }

            string patchedScript;
            try
            {
                patchedScript = await File.ReadAllTextAsync(ps1);

                var prefix =
                    "[Console]::OutputEncoding = [System.Text.Encoding]::UTF8\r\n" +
                    "$OutputEncoding = [System.Text.Encoding]::UTF8\r\n";

                patchedScript = prefix + patchedScript
                    .Replace("[System.Console]::ReadKey($true)", "[System.Console]::ReadLine()")
                    .Replace("[System.Console]::ReadKey()",       "[System.Console]::ReadLine()");
            }
            catch (Exception ex)
            {
                Log($"Failed to read PS1: {ex.Message}");
                foreach (var (h, o) in hiddenFiles)
                    try { File.Move(h, o, overwrite: true); } catch { }
                return (false, $"Failed to read PS1: {ex.Message}");
            }

            var utilsDir = Path.GetDirectoryName(ps1)!;
            var patchedPs1 = Path.Combine(utilsDir, $".zapret_test_{Guid.NewGuid():N}.ps1");

            try
            {
                await File.WriteAllTextAsync(patchedPs1, patchedScript, new UTF8Encoding(true));
                var choice = string.IsNullOrWhiteSpace(typeArg) ? "1" : typeArg.Trim();
                var stdin = choice + "\r\n" + "1\r\n";

                Log($"Launching tests: choice={choice}, currentOnly={isCurrentMode}");

                var (ok, output) = await RunProcessAsync(
                    "powershell.exe",
                    $"-NoProfile -ExecutionPolicy Bypass -File \"{patchedPs1}\"",
                    stdin,
                    workingDir: _baseDir,
                    timeoutMs: ProcessTimeoutMs);

                Log($"Tests finished: ok={ok}, outputLen={output.Length}");
                var filtered = FilterTestOutput(output);
                return (ok, filtered);
            }
            finally
            {
                try { if (File.Exists(patchedPs1)) File.Delete(patchedPs1); } catch { }

                foreach (var (h, o) in hiddenFiles)
                {
                    try { if (File.Exists(h)) File.Move(h, o, overwrite: true); }
                    catch (Exception ex) { Log($"Failed to restore {o}: {ex.Message}"); }
                }
            }
        }

        private async Task<(bool, string)> RunIpsetUpdateAsync()
        {
            try
            {
                var listFile = Path.Combine(_baseDir, "lists", "ipset-all.txt");
                Directory.CreateDirectory(Path.GetDirectoryName(listFile)!);
                var content = await Http.GetStringAsync(IpsetUrl);
                await File.WriteAllTextAsync(listFile, content);
                Log($"IPSet updated: {content.Length} bytes");
                return (true, Lf("MsgIpsetUpdated", content.Length));
            }
            catch (Exception ex)
            {
                Log($"IPSet update failed: {ex.Message}");
                return (false, Lf("MsgIpsetFailed", ex.Message));
            }
        }

        private async Task<(bool, string)> RunHostsCheckAsync()
        {
            try
            {
                var hostsFile = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.System),
                    "drivers", "etc", "hosts");
                if (!File.Exists(hostsFile)) return (false, L("MsgHostsNotFound"));

                var remote = await Http.GetStringAsync(HostsUrl);
                var remoteLines = remote.Split('\n').Select(l => l.Trim())
                    .Where(l => l.Length > 0).ToList();
                if (remoteLines.Count < 2) return (false, L("MsgHostsRemoteEmpty"));

                var hostsContent = await File.ReadAllTextAsync(hostsFile);
                bool needsUpdate = !hostsContent.Contains(remoteLines.First())
                                || !hostsContent.Contains(remoteLines.Last());

                return (true, needsUpdate ? L("MsgHostsNeedsUpdate") : L("MsgHostsUpToDate"));
            }
            catch (Exception ex)
            {
                Log($"Hosts check failed: {ex.Message}");
                return (false, Lf("MsgHostsCheckFailed", ex.Message));
            }
        }

        private async Task<(bool, string)> RunRemoveAsync()
        {
            var sb = new StringBuilder();
            if (!IsAdmin()) return (false, L("MsgNeedAdmin"));

            var (_, exists) = await RunProcessAsync("sc", "query zapret", null);
            if (!exists.Contains("1060"))
            {
                await RunProcessAsync("net", "stop zapret", null);
                await RunProcessAsync("sc", "delete zapret", null);
                sb.AppendLine(L("MsgServiceRemoved"));
            }
            else sb.AppendLine(L("MsgServiceNotInstalled"));

            await RunProcessAsync("taskkill", "/IM winws.exe /F", null);

            var (_, wd) = await RunProcessAsync("sc", "query WinDivert", null);
            if (!wd.Contains("1060"))
            {
                await RunProcessAsync("net", "stop WinDivert", null);
                await RunProcessAsync("sc", "delete WinDivert", null);
            }
            await RunProcessAsync("net", "stop WinDivert14", null);
            await RunProcessAsync("sc", "delete WinDivert14", null);
            sb.AppendLine(L("MsgCleanupDone"));
            return (true, sb.ToString());
        }

        private async Task<(bool, string)> RunInstallAsync(string? presetBatName)
        {
            var log = new StringBuilder();

            if (!IsAdmin()) return (false, L("MsgNeedAdmin"));
            if (string.IsNullOrWhiteSpace(presetBatName)) return (false, L("MsgNoPreset"));

            var presetPath = Path.Combine(_baseDir, presetBatName);
            if (!File.Exists(presetPath))
            {
                var alt = Directory.Exists(_baseDir)
                    ? Directory.EnumerateFiles(_baseDir, presetBatName, SearchOption.AllDirectories).FirstOrDefault()
                    : null;
                if (alt == null) return (false, Lf("MsgPresetNotFound", presetPath));
                presetPath = alt;
            }
            log.AppendLine($"[INFO] Preset: {presetPath}");

            var winwsArgs = ParseWinwsArgs(presetPath);
            if (string.IsNullOrWhiteSpace(winwsArgs))
                return (false, Lf("MsgArgsExtractFail", Path.GetFileName(presetPath)));

            log.AppendLine($"[INFO] Args: {Truncate(winwsArgs, 400)}");

            var binPath = Path.Combine(_baseDir, "bin", "winws.exe");
            if (!File.Exists(binPath)) return (false, Lf("MsgWinwsMissing", binPath));

            var strategy = Path.GetFileNameWithoutExtension(presetBatName);

            await RunProcessAsync("net", "stop zapret", null);
            await RunProcessAsync("sc", "delete zapret", null);

            var (createOk, createOut) = await RunScCreateAsync(binPath, winwsArgs);
            log.AppendLine(createOut.TrimEnd());
            if (!createOk) return (false, L("MsgScCreateFailed") + "\n" + log);

            var (_, verifyOut) = await RunProcessAsync("sc", "query zapret", null);
            if (verifyOut.Contains("1060"))
            {
                log.AppendLine(L("MsgScCreateOkNoService"));
                return (false, log.ToString());
            }

            await RunProcessAsync("sc", "description zapret \"Zapret DPI bypass software\"", null);
            await RunProcessAsync("reg",
                $"add \"HKLM\\System\\CurrentControlSet\\Services\\zapret\" /v zapret-discord-youtube /t REG_SZ /d \"{strategy}\" /f",
                null);

            var (startOk, startOut) = await RunProcessAsync("sc", "start zapret", null);
            log.AppendLine(startOut.TrimEnd());

            var (_, stateOut) = await RunProcessAsync("sc", "query zapret", null);
            bool running = Regex.IsMatch(stateOut, @"STATE\s*:\s*4\s+RUNNING", RegexOptions.IgnoreCase);

            if (!startOk || !running)
            {
                log.Insert(0, L("MsgInstallCreatedNoRun") + "\r\n\r\n");
                return (false, log.ToString());
            }

            log.Insert(0, Lf("MsgInstallOK", strategy) + "\r\n\r\n");
            return (true, log.ToString());
        }

        private async Task<(bool success, string output)> RunScCreateAsync(string binPath, string args)
        {
            var sb = new StringBuilder();
            var psi = new ProcessStartInfo
            {
                FileName = "sc.exe",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                WorkingDirectory = _baseDir
            };

            psi.ArgumentList.Add("create");
            psi.ArgumentList.Add("zapret");
            psi.ArgumentList.Add("binPath=");
            psi.ArgumentList.Add($"\"{binPath}\" {args}");
            psi.ArgumentList.Add("DisplayName=");
            psi.ArgumentList.Add("zapret");
            psi.ArgumentList.Add("start=");
            psi.ArgumentList.Add("auto");

            try
            {
                using var p = new Process { StartInfo = psi };
                p.OutputDataReceived += (s, e) => { if (e.Data != null) sb.AppendLine(Clean(e.Data)); };
                p.ErrorDataReceived  += (s, e) => { if (e.Data != null) sb.AppendLine(Clean(e.Data)); };

                p.Start();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                await p.WaitForExitAsync();
                p.WaitForExit();
                return (p.ExitCode == 0, sb.ToString());
            }
            catch (Exception ex) { return (false, ex.Message); }
        }

        private string ParseWinwsArgs(string batPath)
        {
            string? binVar = null, listsVar = null;
            var rawLines = File.ReadAllLines(batPath);

            foreach (var line in rawLines)
            {
                var mB = Regex.Match(line, @"set\s+""?BIN=(.*?)""?\s*(?:&.*)?$", RegexOptions.IgnoreCase);
                if (mB.Success) binVar = mB.Groups[1].Value.TrimEnd('"');

                var mL = Regex.Match(line, @"set\s+""?LISTS=(.*?)""?\s*(?:&.*)?$", RegexOptions.IgnoreCase);
                if (mL.Success) listsVar = mL.Groups[1].Value.TrimEnd('"');
            }

            var joined = new List<string>();
            var buffer = new StringBuilder();
            foreach (var raw in rawLines)
            {
                var line = raw.TrimEnd();
                if (line.EndsWith("^")) buffer.Append(line, 0, line.Length - 1);
                else { buffer.Append(line); joined.Add(buffer.ToString()); buffer.Clear(); }
            }
            if (buffer.Length > 0) joined.Add(buffer.ToString());

            foreach (var line in joined)
            {
                var idx = line.IndexOf("winws.exe", StringComparison.OrdinalIgnoreCase);
                if (idx < 0) continue;

                var tail = line.Substring(idx + "winws.exe".Length).Trim();
                if (tail.Length == 0) continue;

                tail = tail.Replace("%~dp0", _baseDir + Path.DirectorySeparatorChar);
                if (binVar != null)   tail = tail.Replace("%BIN%", binVar);
                if (listsVar != null) tail = tail.Replace("%LISTS%", listsVar);
                tail = tail.Replace("^", "");

                tail = Regex.Replace(tail, "\"([^\"]*)\"", m =>
                {
                    var inner = m.Groups[1].Value;
                    if (inner.Length == 0) return m.Value;
                    var at = inner.StartsWith("@");
                    var pathPart = at ? inner.Substring(1) : inner;
                    if (!Path.IsPathRooted(pathPart))
                        pathPart = Path.Combine(_baseDir, pathPart);
                    return "\"" + (at ? "@" + pathPart : pathPart) + "\"";
                });

                return tail.Trim();
            }
            return string.Empty;
        }

        private Task<(bool, string)> RunReplaceFakesAsync(string? arg)
        {
            if (string.IsNullOrWhiteSpace(arg))
                return Task.FromResult<(bool, string)>((false, "Expected '<type> <number>' (e.g. '1 4')"));

            var parts = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
                return Task.FromResult<(bool, string)>((false, "Format: <type 1|2> <number>"));

            var binDir = Path.Combine(_baseDir, "bin");
            if (!Directory.Exists(binDir))
                return Task.FromResult<(bool, string)>((false, "bin folder not found"));

            var activeFile = parts[0] switch
            {
                "1" => Path.Combine(binDir, "ACTIVE_DISCORD_UDP.bin"),
                "2" => Path.Combine(binDir, "ACTIVE_GAME_UDP.bin"),
                _   => null
            };
            if (activeFile == null)
                return Task.FromResult<(bool, string)>((false, "Type must be 1 or 2"));

            var fakes = Directory.EnumerateFiles(binDir, "*.bin")
                .Where(f => !Path.GetFileName(f).StartsWith("ACTIVE_", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => Path.GetFileName(f))
                .ToList();

            if (!int.TryParse(parts[1], out int num) || num < 1 || num > fakes.Count)
                return Task.FromResult<(bool, string)>((false, $"Number must be 1..{fakes.Count}"));

            var src = fakes[num - 1];
            File.Copy(src, activeFile, overwrite: true);
            return Task.FromResult<(bool, string)>((true,
                $"[OK] {Path.GetFileName(activeFile)} ← {Path.GetFileName(src)}"));
        }

        private Task<(bool, string)> RunGameFilterAsync(string? mode)
        {
            var utilsDir = Path.Combine(_baseDir, "utils");
            var flagFile = Path.Combine(utilsDir, "game_filter.enabled");

            try { Directory.CreateDirectory(utilsDir); }
            catch (Exception ex) { return Task.FromResult<(bool, string)>((false, Lf("MsgUtilsCreateFail", ex.Message))); }

            switch (mode)
            {
                case "0":
                    if (File.Exists(flagFile)) File.Delete(flagFile);
                    return Task.FromResult<(bool, string)>((true,
                        L("MsgGameFilterOff") + "\r\n" + L("MsgGameFilterHint")));
                case "1":
                    File.WriteAllText(flagFile, "all");
                    return Task.FromResult<(bool, string)>((true,
                        L("MsgGameFilterAll") + "\r\n" + L("MsgGameFilterHint")));
                case "2":
                    File.WriteAllText(flagFile, "tcp");
                    return Task.FromResult<(bool, string)>((true,
                        L("MsgGameFilterTcp") + "\r\n" + L("MsgGameFilterHint")));
                case "3":
                    File.WriteAllText(flagFile, "udp");
                    return Task.FromResult<(bool, string)>((true,
                        L("MsgGameFilterUdp") + "\r\n" + L("MsgGameFilterHint")));
                default:
                    return Task.FromResult<(bool, string)>((false, "Mode must be 0..3"));
            }
        }

        private async Task<(bool success, string output)> RunProcessAsync(
            string fileName, string args, string? stdin,
            string? workingDir = null, int timeoutMs = ProcessTimeoutMs)
        {
            var sb = new StringBuilder();
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = stdin != null,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                WorkingDirectory = workingDir ?? _baseDir
            };

            Log($"Process start: {fileName} {args} (stdin={(stdin == null ? "no" : stdin.Length + "b")}, workDir={psi.WorkingDirectory})");
            var sw = Stopwatch.StartNew();

            const int MaxRepeatedLine = 25;
            const int MaxTotalLines = 800;

            int totalLines = 0;
            string? lastLine = null;
            int repeatCount = 0;
            bool killed = false;

            try
            {
                using var p = new Process { StartInfo = psi };

                void HandleLine(string data, string tag)
                {
                    if (killed) return;

                    var clean = Clean(data);
                    sb.AppendLine(clean);
                    totalLines++;

                    var meaningful = clean.Trim();

                    if (meaningful.Length > 0 && meaningful == lastLine)
                    {
                        repeatCount++;
                        if (repeatCount >= MaxRepeatedLine)
                        {
                            Log($"!!! Output loop detected ({repeatCount}× '{Truncate(meaningful, 80)}'). Killing process.");
                            killed = true;
                            try { p.Kill(entireProcessTree: true); } catch { }
                            return;
                        }
                    }
                    else if (meaningful.Length > 0)
                    {
                        lastLine = meaningful;
                        repeatCount = 1;
                    }

                    if (totalLines >= MaxTotalLines)
                    {
                        Log($"!!! Output too long ({totalLines} lines). Killing process — likely a prompt loop.");
                        killed = true;
                        try { p.Kill(entireProcessTree: true); } catch { }
                        return;
                    }

                    if (totalLines == 1 || totalLines % 100 == 0)
                        Log($"[{tag} #{totalLines}] {Truncate(meaningful, 140)}");
                }

                p.OutputDataReceived += (s, e) => { if (e.Data != null) HandleLine(e.Data, "out"); };
                p.ErrorDataReceived  += (s, e) => { if (e.Data != null) HandleLine(e.Data, "err"); };

                p.Start();

                if (stdin != null)
                    try { await p.StandardInput.WriteAsync(stdin); } catch { }

                try { p.StandardInput.Close(); } catch { }

                p.BeginOutputReadLine();
                p.BeginErrorReadLine();

                using var cts = new CancellationTokenSource(timeoutMs);
                try
                {
                    await p.WaitForExitAsync(cts.Token);
                }
                catch (OperationCanceledException)
                {
                    Log($"!!! Process TIMEOUT ({timeoutMs}ms): {fileName} {args}. Killing...");
                    try { p.Kill(entireProcessTree: true); } catch { }
                    try { await p.WaitForExitAsync(); } catch { }

                    sb.AppendLine($"[TIMEOUT] Процесс не завершился за {timeoutMs / 1000} секунд и был принудительно остановлен.");
                    Log($"Process KILLED by timeout: {fileName} (elapsed {sw.ElapsedMilliseconds}ms, lines={totalLines})");
                    return (false, sb.ToString());
                }

                p.WaitForExit();
                sw.Stop();

                Log($"Process exit: {fileName}, code={p.ExitCode}, elapsed={sw.ElapsedMilliseconds}ms, outputLen={sb.Length}, lines={totalLines}, killed={killed}");

                if (killed)
                {
                    sb.AppendLine();
                    sb.AppendLine("[Процесс остановлен принудительно — обнаружен цикл вывода. Скорее всего, PS1-скрипт ждал ответ, который ему не был передан.]");
                    return (false, sb.ToString());
                }

                return (p.ExitCode == 0, sb.ToString());
            }
            catch (Exception ex)
            {
                sw.Stop();
                Log($"Process error: {fileName} -> {ex.Message} (elapsed {sw.ElapsedMilliseconds}ms)");
                return (false, ex.Message);
            }
        }

        private static string Clean(string s) =>
            AnsiRegex.Replace(s.Replace("\f", "").Replace("\x0C", ""), "");

        private static string Truncate(string s, int n) =>
            s.Length <= n ? s : s[..n] + "…";
    }
}
