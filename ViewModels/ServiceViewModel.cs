using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
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

    [ObservableProperty] private ObservableCollection<ServiceTaskItem> _tasks = new();
    [ObservableProperty] private ObservableCollection<NotificationItem> _notifications = new();
    [ObservableProperty] private bool _isOpen;
    [ObservableProperty] private bool _isFakesModalOpen;
    [ObservableProperty] private int _selectedFakeTypeIndex;
    [ObservableProperty] private int _selectedFakeFileIndex;

    public ObservableCollection<string> FakeTypes { get; } = new();
    public ObservableCollection<string> FakeFiles { get; } = new();

    public bool CanApplyFakes =>
        SelectedFakeTypeIndex >= 0 &&
        SelectedFakeFileIndex >= 0 &&
        FakeFiles.Count > 0;

    [ObservableProperty] private string _excludeIpText = string.Empty;
    [ObservableProperty] private string _newSiteText = string.Empty;
    [ObservableProperty] private string _newExcludeSiteText = string.Empty;
    [ObservableProperty] private ObservableCollection<string> _sites = new();
    [ObservableProperty] private ObservableCollection<string> _excludeSites = new();
    [ObservableProperty] private string _settingsStatusText = string.Empty;

    public ServiceViewModel(string zapretFolderPath, Func<ZapretPreset?> getSelectedPreset)
    {
        _serviceManager = new ServiceManager(zapretFolderPath);
        _getSelectedPreset = getSelectedPreset;

        LocalizationService.Instance.PropertyChanged += OnLocChanged;
    }

    public void LoadTabData()
    {
        _ = LoadIpExcludeAsync();
        _ = LoadSitesAsync();
    }

    private void OnLocChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == "Item[]")
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


    private async Task LoadIpExcludeAsync()
    {
        try
        {
            var content = await _serviceManager.ReadIpExcludeAsync();
            await Dispatcher.UIThread.InvokeAsync(() => ExcludeIpText = content);
        }
        catch { }
    }

    [RelayCommand]
    private async Task UpdateIpsetAsync()
    {
        var (ok, output) = await _serviceManager.RunAsync("ipset_update");
        SettingsStatusText = output;
        ShowToast(ok ? Loc["TaskIpsetTitle"] : Loc["StatusFailed"], output, !ok);
    }

    [RelayCommand]
    private async Task UpdateHostsAsync()
    {
        var (ok, output) = await _serviceManager.ForceUpdateHostsAsync();
        SettingsStatusText = output;
        ShowToast(Loc["BtnUpdateHosts"], output, !ok);
    }

    [RelayCommand]
    private async Task SaveExcludeIpsAsync()
    {
        await _serviceManager.WriteIpExcludeAsync(ExcludeIpText);
        var count = (ExcludeIpText ?? string.Empty)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Count(l => !string.IsNullOrWhiteSpace(l));

        var msg = string.Format(Loc["MsgExcludeIpsSaved"], count);
        SettingsStatusText = msg;
        ShowToast(Loc["BtnSaveExcludeIps"], msg, false);
    }


    private async Task LoadSitesAsync()
    {
        try
        {
            var sites    = await _serviceManager.ReadListAsync(ServiceManager.ListGeneralUser);
            var excludes = await _serviceManager.ReadListAsync(ServiceManager.ListExcludeUser);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Sites.Clear();
                foreach (var s in sites) Sites.Add(s);

                ExcludeSites.Clear();
                foreach (var s in excludes) ExcludeSites.Add(s);
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Sites] LoadSitesAsync failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task AddSiteAsync()
    {
        var entry = (NewSiteText ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(entry)) return;

        var added = await _serviceManager.AppendToListAsync(ServiceManager.ListGeneralUser, entry);
        if (added)
        {
            Sites.Add(entry);
            NewSiteText = string.Empty;
            ShowToast(Loc["BtnAddSite"], string.Format(Loc["MsgSiteAdded"], entry), false);
        }
        else
        {
            ShowToast(Loc["BtnAddSite"], string.Format(Loc["MsgSiteExists"], entry), true);
        }
    }

    [RelayCommand]
    private async Task AddExcludeSiteAsync()
    {
        var entry = (NewExcludeSiteText ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(entry)) return;

        var added = await _serviceManager.AppendToListAsync(ServiceManager.ListExcludeUser, entry);
        if (added)
        {
            ExcludeSites.Add(entry);
            NewExcludeSiteText = string.Empty;
            ShowToast(Loc["BtnAddExcludeSite"], string.Format(Loc["MsgSiteAddedExclude"], entry), false);
        }
        else
        {
            ShowToast(Loc["BtnAddExcludeSite"], string.Format(Loc["MsgSiteExists"], entry), true);
        }
    }

    [RelayCommand]
    private async Task RemoveSiteAsync(string? site)
    {
        if (string.IsNullOrWhiteSpace(site)) return;

        try
        {
            var list = await _serviceManager.ReadListAsync(ServiceManager.ListGeneralUser);
            list.RemoveAll(x => x.Equals(site, StringComparison.OrdinalIgnoreCase));
            await _serviceManager.WriteListAsync(ServiceManager.ListGeneralUser, list);

            var found = Sites.FirstOrDefault(x => x.Equals(site, StringComparison.OrdinalIgnoreCase));
            if (found != null) Sites.Remove(found);

            ShowToast(Loc["BtnAddSite"],
                    $"Удалён: {site}", false);
        }
        catch (Exception ex)
        {
            ShowToast(Loc["BtnAddSite"], $"Ошибка удаления: {ex.Message}", true);
        }
    }

    [RelayCommand]
    private async Task RemoveExcludeSiteAsync(string? site)
    {
        if (string.IsNullOrWhiteSpace(site)) return;

        try
        {
            var list = await _serviceManager.ReadListAsync(ServiceManager.ListExcludeUser);
            list.RemoveAll(x => x.Equals(site, StringComparison.OrdinalIgnoreCase));
            await _serviceManager.WriteListAsync(ServiceManager.ListExcludeUser, list);

            var found = ExcludeSites.FirstOrDefault(x => x.Equals(site, StringComparison.OrdinalIgnoreCase));
            if (found != null) ExcludeSites.Remove(found);

            ShowToast(Loc["BtnAddExcludeSite"],
                    $"Удалён: {site}", false);
        }
        catch (Exception ex)
        {
            ShowToast(Loc["BtnAddExcludeSite"], $"Ошибка удаления: {ex.Message}", true);
        }
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

    private void ShowToast(string title, string message, bool isError)
    {
        var n = new NotificationItem
        {
            Title = title,
            Message = message,
            IsError = isError
        };
        Notifications.Add(n);
        _ = Task.Delay(NotificationLifetimeMs).ContinueWith(_ =>
            Dispatcher.UIThread.Post(() => Notifications.Remove(n)));
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
