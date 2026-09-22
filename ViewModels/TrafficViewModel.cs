#nullable disable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ZapretGui.Services;

namespace ZapretGui.ViewModels;

public partial class TrafficItem : ObservableObject
{
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

    private DateTime _lastChartUpdate = DateTime.Now;
    private const int ChartPointsCount = 60;
    private readonly List<double> _downloadHistory = new();
    private readonly List<double> _uploadHistory = new();

    private double _smoothedDownload = 0;
    private double _smoothedUpload = 0;
    private const double SmoothingFactor = 0.3;

    private const double MinChartScale = 102400;
    private double _chartMaxVal = MinChartScale;

    public LocalizationService Loc => LocalizationService.Instance;

    [ObservableProperty] private ObservableCollection<TrafficItem> _items = new();
    [ObservableProperty] private string _totalDownloadRateText = "0 KB/s";
    [ObservableProperty] private string _totalUploadRateText = "0 KB/s";
    [ObservableProperty] private string _totalDownloadSumText = "0 KB";
    [ObservableProperty] private string _totalUploadSumText = "0 KB";
    [ObservableProperty] private bool _isMonitoring;
    [ObservableProperty] private string _errorMessage = string.Empty;
    [ObservableProperty] private bool _hasError;

    [ObservableProperty] private string _maxSpeedText = "0 KB/s";
    [ObservableProperty] private string _halfSpeedText = "0 KB/s";
    [ObservableProperty] private PathGeometry _downloadGeometry = new();
    [ObservableProperty] private PathGeometry _uploadGeometry = new();
    [ObservableProperty] private PathGeometry _downloadFillGeometry = new();
    [ObservableProperty] private PathGeometry _uploadFillGeometry = new();

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

        _lastChartUpdate = DateTime.Now;
        _downloadHistory.Clear();
        _uploadHistory.Clear();
        _smoothedDownload = 0;
        _smoothedUpload = 0;
        _chartMaxVal = MinChartScale;
        MaxSpeedText = "0 KB/s";
        HalfSpeedText = "0 KB/s";
        DownloadGeometry = new PathGeometry();
        UploadGeometry = new PathGeometry();
        DownloadFillGeometry = new PathGeometry();
        UploadFillGeometry = new PathGeometry();

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
        TotalDownloadRateText = "0 KB/s";
        TotalUploadRateText   = "0 KB/s";

        _downloadHistory.Clear();
        _uploadHistory.Clear();
        _smoothedDownload = 0;
        _smoothedUpload = 0;
        _chartMaxVal = MinChartScale;
        MaxSpeedText = "0 KB/s";
        HalfSpeedText = "0 KB/s";
        DownloadGeometry = new PathGeometry();
        UploadGeometry = new PathGeometry();
        DownloadFillGeometry = new PathGeometry();
        UploadFillGeometry = new PathGeometry();
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

        var combined = new Dictionary<int, (long totalSent, long totalRecv, string name)>();

        foreach (var kv in _monitor.BytesSent)
        {
            var recv = _monitor.BytesReceived.TryGetValue(kv.Key, out var r) ? r : 0;
            var name = _monitor.ProcessNames.TryGetValue(kv.Key, out var n) ? n : $"PID {kv.Key}";
            if (string.IsNullOrEmpty(name)) continue;
            combined[kv.Key] = (kv.Value, recv, name);
        }
        foreach (var kv in _monitor.BytesReceived)
        {
            if (!combined.ContainsKey(kv.Key))
            {
                var name = _monitor.ProcessNames.TryGetValue(kv.Key, out var n) ? n : $"PID {kv.Key}";
                if (string.IsNullOrEmpty(name)) continue;
                combined[kv.Key] = (0, kv.Value, name);
            }
        }

        var grouped = new Dictionary<string, (long totalSent, long totalRecv, long sentRate, long recvRate)>();

        long totalSentRate = 0, totalRecvRate = 0;
        long totalSentSum = 0, totalRecvSum = 0;

        foreach (var kv in combined)
        {
            var pid = kv.Key;
            var (sent, recv, name) = kv.Value;

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

            _snapshot[pid] = (sent, recv);

            if (!grouped.ContainsKey(name))
                grouped[name] = (0, 0, 0, 0);

            var current = grouped[name];
            grouped[name] = (
                current.totalSent + sent,
                current.totalRecv + recv,
                current.sentRate + sentRate,
                current.recvRate + recvRate
            );
        }

        var list = new List<TrafficItem>();

        foreach (var kv in grouped)
        {
            var name = kv.Key;
            var (totalSent, totalRecv, sentRate, recvRate) = kv.Value;

            if (totalSent == 0 && totalRecv == 0) continue;

            if (name.Contains("Service", StringComparison.OrdinalIgnoreCase) || 
                name.Contains("Responder", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Helper", StringComparison.OrdinalIgnoreCase)) continue;

            totalSentRate += sentRate;
            totalRecvRate += recvRate;
            totalSentSum += totalSent;
            totalRecvSum += totalRecv;

            var isZapret = name.Equals("winws", StringComparison.OrdinalIgnoreCase)
                        || name.Equals("winws.exe", StringComparison.OrdinalIgnoreCase);

            list.Add(new TrafficItem
            {
                ProcessName = name,
                BytesSentPerSecond = sentRate,
                BytesReceivedPerSecond = recvRate,
                TotalSentBytes = totalSent,
                TotalReceivedBytes = totalRecv,
                IsZapret = isZapret
            });
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

        if ((now - _lastChartUpdate).TotalSeconds >= 5)
        {
            _lastChartUpdate = now;
            UpdateChartHistory(totalRecvRate, totalSentRate);
        }
    }

    private void UpdateChartHistory(long downloadRate, long uploadRate)
    {
        _smoothedDownload = SmoothingFactor * downloadRate + (1 - SmoothingFactor) * _smoothedDownload;
        _smoothedUpload = SmoothingFactor * uploadRate + (1 - SmoothingFactor) * _smoothedUpload;

        _downloadHistory.Add(_smoothedDownload);
        _uploadHistory.Add(_smoothedUpload);

        if (_downloadHistory.Count > ChartPointsCount) _downloadHistory.RemoveAt(0);
        if (_uploadHistory.Count > ChartPointsCount) _uploadHistory.RemoveAt(0);

        double currentMax = 0;
        if (_downloadHistory.Count > 0) currentMax = Math.Max(_downloadHistory.Max(), _uploadHistory.Max());
        double targetMax = Math.Max(MinChartScale, currentMax * 1.2);
        
        if (targetMax > _chartMaxVal)
            _chartMaxVal = targetMax;
        else
            _chartMaxVal = _chartMaxVal * 0.95 + targetMax * 0.05;

        MaxSpeedText = FormatBytes((long)_chartMaxVal) + "/s";
        HalfSpeedText = FormatBytes((long)(_chartMaxVal / 2)) + "/s";

        var dlPoints = new List<Point>();
        var ulPoints = new List<Point>();

        int pointsCount = _downloadHistory.Count;
        double step = 1000.0 / (ChartPointsCount - 1);

        for (int i = 0; i < pointsCount; i++)
        {
            double x = 1000 - (pointsCount - 1 - i) * step;
            
            double dlY = 200 - (_downloadHistory[i] / _chartMaxVal * 200);
            
            double ulY = 200 - (_uploadHistory[i] / _chartMaxVal * 200);

            dlPoints.Add(new Point(x, dlY));
            ulPoints.Add(new Point(x, ulY));
        }

        DownloadGeometry = CreateSmoothGeometry(dlPoints, isFill: false, baseY: 200);
        UploadGeometry = CreateSmoothGeometry(ulPoints, isFill: false, baseY: 200);
        DownloadFillGeometry = CreateSmoothGeometry(dlPoints, isFill: true, baseY: 200);
        UploadFillGeometry = CreateSmoothGeometry(ulPoints, isFill: true, baseY: 200);
    }

    private PathGeometry CreateSmoothGeometry(List<Point> points, bool isFill, double baseY)
    {
        if (points.Count < 2) return new PathGeometry();

        var geometry = new PathGeometry();
        var figure = new PathFigure { IsClosed = isFill, IsFilled = isFill };

        if (isFill)
        {
            figure.StartPoint = new Point(points[0].X, baseY);
            var startSegment = new LineSegment { Point = points[0] };
            figure.Segments.Add(startSegment);
        }
        else
        {
            figure.StartPoint = points[0];
        }

        for (int i = 0; i < points.Count - 1; i++)
        {
            Point p0 = points[i];
            Point p1 = points[i + 1];

            Point cp1 = new Point(
                p0.X + (p1.X - (i > 0 ? points[i - 1].X : p0.X)) / 6,
                p0.Y + (p1.Y - (i > 0 ? points[i - 1].Y : p0.Y)) / 6
            );
            Point cp2 = new Point(
                p1.X - ((i < points.Count - 2 ? points[i + 2].X : p1.X) - p0.X) / 6,
                p1.Y - ((i < points.Count - 2 ? points[i + 2].Y : p1.Y) - p0.Y) / 6
            );

            var segment = new BezierSegment { Point1 = cp1, Point2 = cp2, Point3 = p1 };
            figure.Segments.Add(segment);
        }

        if (isFill)
        {
            var endSegment = new LineSegment { Point = new Point(points[points.Count - 1].X, baseY) };
            figure.Segments.Add(endSegment);
            var closeSegment = new LineSegment { Point = new Point(points[0].X, baseY) };
            figure.Segments.Add(closeSegment);
        }

        geometry.Figures.Add(figure);
        return geometry;
    }

    public static string FormatBytes(long bytes)
    {
        string[] sizes = { "KB", "MB", "GB", "TB" };
        double len = bytes / 1024.0;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1) { order++; len /= 1024; }
        return $"{len:0.##} {sizes[order]}";
    }

    public void Dispose() => _monitor.Dispose();
}
