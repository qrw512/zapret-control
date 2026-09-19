using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ZapretGui.Models;
using ZapretGui.Services;

namespace ZapretGui.ViewModels;

public partial class ServiceViewModel : ObservableObject
{
    private readonly ServiceManager _serviceManager;
    private readonly Func<ZapretPreset?> _getSelectedPreset;
    private const int NotificationLifetimeMs = 6000;

    public LocalizationService Loc => LocalizationService.Instance;

    [ObservableProperty]
    private ObservableCollection<ServiceTaskItem> _tasks = new();

    [ObservableProperty]
    private ObservableCollection<NotificationItem> _notifications = new();

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty] private bool _isFakesModalOpen;
    [ObservableProperty] private int _selectedFakeTypeIndex;
    [ObservableProperty] private int _selectedFakeFileIndex;

    public ObservableCollection<string> FakeTypes { get; } = new();
    public ObservableCollection<string> FakeFiles { get; } = new();

    public bool CanApplyFakes =>
        SelectedFakeTypeIndex >= 0
        && SelectedFakeFileIndex >= 0
        && FakeFiles.Count > 0;

    public ServiceViewModel(string zapretFolderPath, Func<ZapretPreset?> getSelectedPreset)
    {
        _serviceManager = new ServiceManager(zapretFolderPath);
        _getSelectedPreset = getSelectedPreset;
        InitTasks();

        LocalizationService.Instance.PropertyChanged += OnLocChanged;
    }

    private void OnLocChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == "Item[]")
            ApplyTaskLocalization();
    }

    private void InitTasks()
    {
        Tasks = new ObservableCollection<ServiceTaskItem>
        {
            new() { Icon = "🔍", CommandLabel = "diagnostics"   },
            new() { Icon = "🌐", CommandLabel = "ipset_update"  },
            new() { Icon = "📝", CommandLabel = "hosts_check"   },
            new() { Icon = "🔄", CommandLabel = "replace_fakes" }
        };

        foreach (var t in Tasks)
            foreach (var s in t.SubActions)
                s.Parent = t;

        ApplyTaskLocalization();
    }

    private void ApplyTaskLocalization()
    {
        foreach (var t in Tasks)
        {
            var (titleKey, descKey) = t.CommandLabel switch
            {
                "diagnostics"   => ("TaskDiagnosticsTitle", "TaskDiagnosticsDesc"),
                "status"        => ("TaskStatusTitle",      "TaskStatusDesc"),
                "ipset_update"  => ("TaskIpsetTitle",       "TaskIpsetDesc"),
                "hosts_check"   => ("TaskHostsTitle",       "TaskHostsDesc"),
                "replace_fakes" => ("TaskFakesTitle",       "TaskFakesDesc"),
                "run_tests"     => ("TaskTestsTitle",       "TaskTestsDesc"),
                _               => ("TaskDiagnosticsTitle", "TaskDiagnosticsDesc")
            };
            t.Title = Loc[titleKey];
            t.Description = Loc[descKey];

            foreach (var s in t.SubActions)
            {
                s.Label = s.Argument switch
                {
                    "1" => Loc["TestStandard"],
                    "2" => Loc["TestDpi"],
                    _   => s.Label
                };
            }
        }
    }

    [RelayCommand]
    private Task RunTaskAsync(ServiceTaskItem task)
    {
        if (task.CommandLabel == "replace_fakes")
        {
            OpenFakesDialog();
            return Task.CompletedTask;
        }

        return RunTaskInternalAsync(task, null);
    }

    [RelayCommand]
    private Task RunSubActionAsync(ServiceSubAction sub) =>
        sub.Parent is null ? Task.CompletedTask : RunTaskInternalAsync(sub.Parent, sub.Argument);

    private async Task RunTaskInternalAsync(ServiceTaskItem task, string? argument)
    {
        if (task.State == TaskExecutionState.Running) return;

        task.State = TaskExecutionState.Running;
        task.StatusText = Loc["StatusInProgress"];
        task.LastOutput = string.Empty;
        task.HasOutput = false;
        task.IsExpanded = false;

        var (success, output) = await _serviceManager.RunAsync(task.CommandLabel, argument);

        ApplyTaskResult(task, success, output);
    }

    private void ApplyTaskResult(ServiceTaskItem task, bool success, string output)
    {
        task.LastOutput = output;
        task.HasOutput = !string.IsNullOrWhiteSpace(output);

        if (success)
        {
            task.State = TaskExecutionState.Success;
            task.StatusText = Loc["StatusDone"];
        }
        else
        {
            task.State = TaskExecutionState.Failed;
            task.StatusText = Loc["StatusFailed"];
        }

        task.IsExpanded = task.HasOutput;
        _ = ShowNotificationAsync(task);
    }

    [RelayCommand]
    private void OpenFakesDialog()
    {
        var info = _serviceManager.GetReplaceFakesInfo();

        FakeTypes.Clear();
        FakeTypes.Add($"{Loc["FakesTypeDiscord"]}  (current: {info.CurrentDiscordFake})");
        FakeTypes.Add($"{Loc["FakesTypeGame"]}  (current: {info.CurrentGameFake})");

        FakeFiles.Clear();
        foreach (var f in info.Files)
            FakeFiles.Add($"{f.Index}. {f.Name}");

        SelectedFakeTypeIndex = FakeTypes.Count > 0 ? 0 : -1;
        SelectedFakeFileIndex = FakeFiles.Count > 0 ? 0 : -1;

        OnPropertyChanged(nameof(CanApplyFakes));
        IsFakesModalOpen = true;
    }

    [RelayCommand]
    private void CloseFakesDialog() => IsFakesModalOpen = false;

    [RelayCommand]
    private async Task ApplyFakesAsync()
    {
        if (!CanApplyFakes) return;
        var type = (SelectedFakeTypeIndex + 1).ToString();
        var number = (SelectedFakeFileIndex + 1).ToString();

        var task = Tasks.FirstOrDefault(t => t.CommandLabel == "replace_fakes");
        if (task == null) return;

        IsFakesModalOpen = false;

        task.State = TaskExecutionState.Running;
        task.StatusText = Loc["StatusInProgress"];
        task.LastOutput = string.Empty;
        task.HasOutput = false;
        task.IsExpanded = false;

        var (success, output) = await _serviceManager.RunAsync("replace_fakes", $"{type} {number}");
        ApplyTaskResult(task, success, output);
    }

    partial void OnSelectedFakeTypeIndexChanged(int value) => OnPropertyChanged(nameof(CanApplyFakes));
    partial void OnSelectedFakeFileIndexChanged(int value) => OnPropertyChanged(nameof(CanApplyFakes));

    [RelayCommand]
    private void ToggleExpand(ServiceTaskItem task) => task.IsExpanded = !task.IsExpanded;

    [RelayCommand]
    private void DismissNotification(NotificationItem n)
    {
        if (Notifications.Contains(n)) Notifications.Remove(n);
    }

    private async Task ShowNotificationAsync(ServiceTaskItem task)
    {
        var message = ExtractSummary(task.LastOutput);
        if (string.IsNullOrWhiteSpace(message)) message = task.StatusText;

        var n = new NotificationItem
        {
            Title = task.Title,
            Message = message,
            IsError = task.State == TaskExecutionState.Failed
        };
        Notifications.Add(n);
        await Task.Delay(NotificationLifetimeMs);
        if (Notifications.Contains(n)) Notifications.Remove(n);
    }

    private static string ExtractSummary(string output)
    {
        if (string.IsNullOrWhiteSpace(output)) return string.Empty;

        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.TrimEnd('\r').Trim())
            .Where(l => l.Length > 0)
            .Where(l => !IsNoise(l))
            .Take(3)
            .ToList();

        if (lines.Count == 0) return string.Empty;
        var joined = string.Join("  •  ", lines);
        return joined.Length > 200 ? joined[..200] + "…" : joined;
    }

    private static bool IsNoise(string line)
    {
        if (line.Trim('-', '=', ' ', '_').Length == 0) return true;
        var lower = line.ToLowerInvariant();
        return lower.Contains("press any key") || lower.Contains("нажмите");
    }
}
