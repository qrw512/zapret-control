using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ZapretGui.Services;

namespace ZapretGui.ViewModels;

public partial class TrafficItem : ObservableObject
{
    public int ProcessId { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public long BytesSentPerSecond { get; set; }
    public long BytesReceivedPerSecond { get; set; }
    public long TotalSentBytes { get; set; }
    public long TotalReceivedBytes { get; set; }
    public bool IsZapret { get; set; }

    public string SentRateText => TrafficViewModel.FormatBytes(BytesSentPerSecond) + "/s";
    public string ReceivedRateText => TrafficViewModel.FormatBytes(BytesReceivedPerSecond) + "/s";
    public string TotalSentText => TrafficViewModel.FormatBytes(TotalSentBytes);
    public string TotalReceivedText => TrafficViewModel.FormatBytes(TotalReceivedBytes);
}

public partial class TrafficViewModel : ObservableObject, IDisposable
{
    private readonly TrafficMonitorService _monitor = new();
    private readonly Dictionary<int, (long sent, long recv)> _snapshot = new();
    private DateTime _lastUpdate = DateTime.Now;
    private bool _firstRefresh = true;

    public LocalizationService Loc => LocalizationService.Instance;

    [ObservableProperty] private ObservableCollection<TrafficItem> _items = new();
    [ObservableProperty] private string _totalDownloadRateText = "0 B/s";
    [ObservableProperty] private string _totalUploadRateText = "0 B/s";
    [ObservableProperty] private string _totalDownloadSumText = "0 B";
    [ObservableProperty] private string _totalUploadSumText = "0 B";
    [ObservableProperty] private bool _isMonitoring;
    [ObservableProperty] private string _errorMessage = string.Empty;
    [ObservableProperty] private bool _hasError;

    public TrafficViewModel()
    {
        _monitor.Updated += OnMonitorUpdated;
        _monitor.Failed += OnMonitorFailed;
        StartMonitoring();
    }

    [RelayCommand]
    private void StartMonitoring()
    {
        if (IsMonitoring) return;

        HasError = false;
        ErrorMessage = string.Empty;

        _monitor.Reset();
        _snapshot.Clear();
        _lastUpdate = DateTime.Now;
        _firstRefresh = true;

        _monitor.Start();
        _ = CheckStartedAsync();

        IsMonitoring = true;
    }

    private async Task CheckStartedAsync()
    {
        await Task.Delay(900);

        if (!_monitor.IsRunning && !string.IsNullOrEmpty(_monitor.LastError))
        {
            HasError = true;
            var err = _monitor.LastError;

            ErrorMessage = err == "AdminRequired"
                ? Loc["TrafficNotAdmin"]
                : (err.Contains("800705AA") || err.Contains("Insufficient"))
                    ? Loc["TrafficSessionLimit"]
                    : $"{Loc["TrafficEtwError"]}: {err}";
        }
    }

    [RelayCommand]
    private void StopMonitoring()
    {
        if (!IsMonitoring) return;
        _monitor.Stop();
        IsMonitoring = false;
        Items.Clear();
        TotalDownloadRateText = "0 B/s";
        TotalUploadRateText   = "0 B/s";
    }

    private void OnMonitorUpdated() => Dispatcher.UIThread.Post(RefreshItems);

    private void OnMonitorFailed(string error) => Dispatcher.UIThread.Post(() =>
    {
        HasError = true;

        if (error == "AdminRequired")
        {
            ErrorMessage = Loc["TrafficNotAdmin"];
        }
        else if (error.Contains("800705AA") || error.Contains("Insufficient"))
        {
            ErrorMessage = Loc["TrafficSessionLimit"];
        }
        else
        {
            ErrorMessage = $"{Loc["TrafficEtwError"]}: {error}";
        }
    });

    private void RefreshItems()
    {
        var now = DateTime.Now;
        var elapsed = (now - _lastUpdate).TotalSeconds;
        if (elapsed <= 0.1) elapsed = 1;
        _lastUpdate = now;

        var combined = new Dictionary<int, (long totalSent, long totalRecv)>();

        foreach (var kv in _monitor.BytesSent)
        {
            var recv = _monitor.BytesReceived.TryGetValue(kv.Key, out var r) ? r : 0;
            combined[kv.Key] = (kv.Value, recv);
        }
        foreach (var kv in _monitor.BytesReceived)
        {
            if (!combined.ContainsKey(kv.Key))
                combined[kv.Key] = (0, kv.Value);
        }

        var list = new List<TrafficItem>();
        long totalSentRate = 0, totalRecvRate = 0;
        long totalSentSum = 0, totalRecvSum = 0;

        foreach (var kv in combined)
        {
            var pid = kv.Key;
            var (sent, recv) = kv.Value;

            _snapshot.TryGetValue(pid, out var snap);
            long sentRate, recvRate;

            if (_firstRefresh || (snap.sent == 0 && snap.recv == 0))
            {
                sentRate = 0;
                recvRate = 0;
            }
            else
            {
                sentRate = (long)Math.Max(0, (sent - snap.sent) / elapsed);
                recvRate = (long)Math.Max(0, (recv - snap.recv) / elapsed);
            }

            totalSentRate += sentRate;
            totalRecvRate += recvRate;
            totalSentSum += sent;
            totalRecvSum += recv;

            var name = _monitor.ProcessNames.TryGetValue(pid, out var n) ? n : $"PID {pid}";
            if (string.IsNullOrEmpty(name)) continue;

            var isZapret = name.Equals("winws", StringComparison.OrdinalIgnoreCase)
                        || name.Equals("winws.exe", StringComparison.OrdinalIgnoreCase);

            list.Add(new TrafficItem
            {
                ProcessId = pid,
                ProcessName = name,
                BytesSentPerSecond = sentRate,
                BytesReceivedPerSecond = recvRate,
                TotalSentBytes = sent,
                TotalReceivedBytes = recv,
                IsZapret = isZapret
            });

            _snapshot[pid] = (sent, recv);
        }

        _firstRefresh = false;
        list.Sort((a, b) =>
        {
            if (a.IsZapret != b.IsZapret) return a.IsZapret ? -1 : 1;
            return (b.TotalSentBytes + b.TotalReceivedBytes)
                .CompareTo(a.TotalSentBytes + a.TotalReceivedBytes);
        });

        Items.Clear();
        foreach (var it in list.Take(80)) Items.Add(it);

        TotalDownloadRateText = FormatBytes(totalRecvRate) + "/s";
        TotalUploadRateText   = FormatBytes(totalSentRate) + "/s";
        TotalDownloadSumText  = FormatBytes(totalRecvSum);
        TotalUploadSumText    = FormatBytes(totalSentSum);
    }

    public static string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1) { order++; len /= 1024; }
        return $"{len:0.##} {sizes[order]}";
    }

    public void Dispose() => _monitor.Dispose();
}
