using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Diagnostics.Tracing.Session;

namespace ZapretGui.Services;

public class TrafficMonitorService : IDisposable
{
    private const string SessionPrefix = "ZapretTraffic_";
    private static readonly int CurrentPid = Environment.ProcessId;

    private TraceEventSession? _session;
    private string? _sessionName;
    private Task? _monitorTask;
    private CancellationTokenSource? _cts;
    private Timer? _uiTimer;

    private readonly ConcurrentDictionary<int, long> _bytesSent = new();
    private readonly ConcurrentDictionary<int, long> _bytesReceived = new();
    private readonly ConcurrentDictionary<int, string> _processNames = new();

    public event Action? Updated;
    public event Action<string>? Failed;

    public IReadOnlyDictionary<int, long> BytesSent => _bytesSent;
    public IReadOnlyDictionary<int, long> BytesReceived => _bytesReceived;
    public IReadOnlyDictionary<int, string> ProcessNames => _processNames;

    public bool IsRunning => _session != null;
    public string LastError { get; private set; } = string.Empty;

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool LookupPrivilegeValue(string? lpSystemName, string lpName, out long lpLuid);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool AdjustTokenPrivileges(
        IntPtr TokenHandle, bool DisableAllPrivileges,
        ref TOKEN_PRIVILEGES NewState, uint BufferLength,
        IntPtr PreviousState, IntPtr ReturnLength);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct LUID_AND_ATTRIBUTES
    {
        public long Luid;
        public uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct TOKEN_PRIVILEGES
    {
        public uint PrivilegeCount;
        public LUID_AND_ATTRIBUTES Privileges;
    }

    private const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
    private const uint TOKEN_QUERY = 0x0008;
    private const uint SE_PRIVILEGE_ENABLED = 0x0002;

    private static bool EnablePrivilege(string name)
    {
        try
        {
            if (!OpenProcessToken(GetCurrentProcess(),
                    TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out var token))
                return false;

            if (!LookupPrivilegeValue(null, name, out var luid))
                return false;

            var tp = new TOKEN_PRIVILEGES
            {
                PrivilegeCount = 1,
                Privileges = new LUID_AND_ATTRIBUTES
                {
                    Luid = luid,
                    Attributes = SE_PRIVILEGE_ENABLED
                }
            };

            return AdjustTokenPrivileges(token, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero)
                   && Marshal.GetLastWin32Error() == 0;
        }
        catch
        {
            return false;
        }
    }

    private static void EnableEtwPrivileges()
    {
        EnablePrivilege("SeSystemProfilePrivilege");
        EnablePrivilege("SeProfileSingleProcessPrivilege");
    }
    
    private static readonly HashSet<string> IgnoredProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Idle", "System", "Registry", "Memory Compression", "Secure System",
        "smss", "csrss", "wininit", "services", "lsass", "winlogon",
        "fontdrvhost", "dwm", "sihost", "ctfmon", "svchost",
        "SearchIndexer", "SearchProtocolHost", "SearchFilterHost",
        "MsMpEng", "NisSrv", "SecurityHealthService", "SecurityHealthSystray",
        "RuntimeBroker", "backgroundTaskHost", "dllhost", "WmiPrvSE",
        "taskhostw", "ShellExperienceHost", "StartMenuExperienceHost",
        "ApplicationFrameHost", "TextInputHost", "SettingSyncHost",
        "conhost", "audiodg", "spoolsv", "WUDFHost", "nvcontainer",
        "MoUsoCoreWorker", "usocoreworker", "TrustedInstaller", "TiWorker",
        "svchost.exe", "wmiprvse.exe", "code",
        "winws", "winws.exe",
        "zapret",
        "WinDivert", "WinDivert14",
        "ZapretControl",
        "ZapretGui", "mDNSResponder",
    };

    private static void Log(string msg)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.Logs);
            var f = Path.Combine(AppPaths.Logs, $"traffic-{DateTime.Now:yyyy-MM-dd}.log");
            File.AppendAllText(f, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}{Environment.NewLine}");
        }
        catch { }
    }

    private static bool IsAdmin()
    {
        try
        {
            using var id = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }

    private static void CleanupOldSessions()
    {
        try
        {
            var active = TraceEventSession.GetActiveSessionNames();
            if (active == null || !active.Any()) return;

            foreach (var name in active)
            {
                if (!name.StartsWith(SessionPrefix, StringComparison.OrdinalIgnoreCase))
                    continue;

                Log($"Удаляю старую сессию: {name}");
                try
                {
                    using var temp = new TraceEventSession(name);
                    temp.Stop();
                }
                catch (Exception ex)
                {
                    Log($"Не удалось остановить {name}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Log($"CleanupOldSessions ошибка: {ex.Message}");
        }
    }

    public void Start()
    {
        if (_session != null) return;

        LastError = string.Empty;

        if (!IsAdmin())
        {
            LastError = "AdminRequired";
            Log("!!! Не запущено от имени администратора — ETW недоступен");
            Failed?.Invoke(LastError);
            return;
        }

        EnableEtwPrivileges();

        CleanupOldSessions();

        _cts = new CancellationTokenSource();

        _monitorTask = Task.Run(() => RunMonitor());
    }

    private void RunMonitor()
    {
        try
        {
            _sessionName = SessionPrefix + Guid.NewGuid().ToString("N").Substring(0, 8);
            Log($"Создаю ETW-сессию: {_sessionName}");

            _session = new TraceEventSession(_sessionName)
            {
                StopOnDispose = true
            };

            try
            {
                _session.BufferSizeMB = 16;
            }
            catch (Exception ex)
            {
                Log($"BufferSizeMB не установлен: {ex.Message}");
            }

            bool providerOk = false;
            Exception? lastEx = null;

            var keywordCombos = new[]
            {
                KernelTraceEventParser.Keywords.NetworkTCPIP,
            };

            foreach (var kw in keywordCombos)
            {
                try
                {
                    _session.EnableKernelProvider(kw);
                    providerOk = true;
                    Log($"EnableKernelProvider OK (keywords={kw})");
                    break;
                }
                catch (Exception ex)
                {
                    lastEx = ex;
                    Log($"EnableKernelProvider FAIL ({kw}): {ex.Message}");
                }
            }

            if (!providerOk)
            {
                throw lastEx ?? new Exception("EnableKernelProvider failed");
            }

            _session.Source.Kernel.TcpIpSend += d => Accumulate(d.ProcessID, d.size, sent: true,  dport: d.dport, sport: d.sport);
            _session.Source.Kernel.TcpIpRecv += d => Accumulate(d.ProcessID, d.size, sent: false, dport: d.dport, sport: d.sport);
            _session.Source.Kernel.UdpIpSend += d => Accumulate(d.ProcessID, d.size, sent: true,  dport: d.dport, sport: d.sport);
            _session.Source.Kernel.UdpIpRecv += d => Accumulate(d.ProcessID, d.size, sent: false, dport: d.dport, sport: d.sport);

            _uiTimer = new Timer(_ => Updated?.Invoke(), null, 1000, 1000);

            Log("ETW-сессия запущена, ждём события...");
            _session.Source.Process();
            Log("ETW-сессия завершена");
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            Log($"!!! ETW ERROR: {ex.GetType().Name}: {ex.Message}");

            try { _uiTimer?.Dispose(); } catch { }
            try { _session?.Dispose(); } catch { }
            _session = null;
            _uiTimer = null;

            Failed?.Invoke(LastError);
        }
    }

    private bool IsIgnoredProcess(int pid)
    {
        if (pid == CurrentPid) return true;

        if (_processNames.TryGetValue(pid, out var cached))
            return string.IsNullOrEmpty(cached);

        string name;
        try
        {
            using var p = Process.GetProcessById(pid);
            name = p.ProcessName;
        }
        catch
        {
            name = string.Empty;
        }

        if (string.IsNullOrEmpty(name) || IgnoredProcesses.Contains(name))
            name = string.Empty;

        _processNames[pid] = name;
        return string.IsNullOrEmpty(name);
    }

    private void Accumulate(int pid, int size, bool sent, int dport, int sport)
    {
        if (pid <= 0 || size <= 0) return;
        if (IsIgnoredProcess(pid)) return;

        if (sent) _bytesSent.AddOrUpdate(pid, size, (_, v) => v + size);
        else      _bytesReceived.AddOrUpdate(pid, size, (_, v) => v + size);
    }

    public void Reset()
    {
        _bytesSent.Clear();
        _bytesReceived.Clear();
    }

    public void Stop()
    {
        try { _cts?.Cancel(); } catch { }
        try { _uiTimer?.Dispose(); } catch { }

        try { _session?.Dispose(); } catch { }

        if (!string.IsNullOrEmpty(_sessionName))
        {
            try
            {
                using var temp = new TraceEventSession(_sessionName);
                temp.Stop();
            }
            catch { }
        }

        try { _monitorTask?.Wait(2000); } catch { }

        _session = null;
        _sessionName = null;
        _uiTimer = null;
    }

    public void Dispose() => Stop();
}
