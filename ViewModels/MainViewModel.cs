using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ZapretGui.Models;
using ZapretGui.Services;

namespace ZapretGui.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly UpdateService _updateService = new();
    private readonly SelfUpdateService _selfUpdateService = new();
    private SelfUpdateInfo? _pendingSelfUpdate;
    private readonly ZapretManager _zapretManager = new();
    private readonly ConfigService _configService = new();

    private AppConfig _config;
    private Func<string>? _updateStatusFactory;
    public LocalizationService Loc => LocalizationService.Instance;

    private bool _suppressServiceSync;
    private bool _suppressGameFilterSync;
    private bool _suppressThemeSync;
    private bool _hasLastResult;
    private bool _suppressStartWithWindows;
    private bool _suppressAutoStartBypass;
    private bool _suppressBypassModeSync;

    private string _lastTestTitle = string.Empty;
    private string _lastTestSubtitle = string.Empty;
    private string _lastTestBody = string.Empty;

    [ObservableProperty] private bool _isTestModalOpen;
    [ObservableProperty] private string _testModalTitle = string.Empty;
    [ObservableProperty] private string _testModalSubtitle = string.Empty;
    [ObservableProperty] private string _testModalBody = string.Empty;
    [ObservableProperty] private bool _isAnyTestRunning;
    [ObservableProperty] private string _zapretFolderPath = AppPaths.Versions;
    [ObservableProperty] private ServiceViewModel _serviceViewModel;
    [ObservableProperty] private ObservableCollection<ZapretPreset> _presets = new();
    [ObservableProperty] private ZapretPreset? _selectedPreset;
    [ObservableProperty] private string _serviceStatusText = string.Empty;
    [ObservableProperty] private bool _isServiceRunning;
    [ObservableProperty] private string _updateStatusText = string.Empty;
    [ObservableProperty] private bool _isUpdating;
    [ObservableProperty] private bool _isUpdateAvailable;
    [ObservableProperty] private bool _isSelfUpdateOpen;
    [ObservableProperty] private string _selfUpdateMessage = string.Empty;
    [ObservableProperty] private bool _isSelfUpdateDownloading;
    [ObservableProperty] private bool _isSettingsOpen;
    [ObservableProperty] private bool _isAboutOpen;
    [ObservableProperty] private bool _isTrafficOpen;
    [ObservableProperty] private string _selfUpdateStatusText = string.Empty;
    [ObservableProperty] private bool _isServiceInstalled;
    [ObservableProperty] private int _gameFilterModeIndex;
    [ObservableProperty] private int _themeIndex;
    [ObservableProperty] private bool _startWithWindows;
    [ObservableProperty] private bool _autoStartBypass;
    [ObservableProperty] private ObservableCollection<string> _gameFilterOptions = new();
    [ObservableProperty] private ObservableCollection<string> _themeOptions = new();
    [ObservableProperty] private string _settingsStatusText = string.Empty;
    [ObservableProperty] private double _selfUpdateProgress;
    [ObservableProperty] private bool   _isSelfUpdateProgressVisible;
    [ObservableProperty] private int _bypassModeIndex;

    public bool IsSelfUpdateReady => !IsSelfUpdateDownloading;

    public string AppVersion => "v" + _selfUpdateService.GetCurrentVersion();

    partial void OnIsSelfUpdateDownloadingChanged(bool value)
    => OnPropertyChanged(nameof(IsSelfUpdateReady));
    public Action? RequestExit { get; set; }

    public string SelectedPresetText =>
        SelectedPreset == null ? string.Empty : string.Format(Loc["SelectedFormat"], SelectedPreset.Name);

    public string BypassModeDescription =>
        BypassModeIndex == 1 ? Loc["BypassModeHintAll"] : Loc["BypassModeHintWhitelist"];

    public bool IsBypassPage   => !IsSettingsOpen && !ServiceViewModel.IsOpen && !IsAboutOpen && !IsTrafficOpen;
    public bool IsToolsPage    => ServiceViewModel.IsOpen;
    public bool IsSettingsPage => IsSettingsOpen;
    public bool IsAboutPage    => IsAboutOpen;
    public bool IsTrafficPage  => IsTrafficOpen;

    public TrafficViewModel TrafficViewModel { get; }

    private void RaisePageFlags()
    {
        OnPropertyChanged(nameof(IsBypassPage));
        OnPropertyChanged(nameof(IsToolsPage));
        OnPropertyChanged(nameof(IsSettingsPage));
        OnPropertyChanged(nameof(IsAboutPage));
        OnPropertyChanged(nameof(IsTrafficPage));
    }

    partial void OnIsSettingsOpenChanged(bool value) => RaisePageFlags();
    partial void OnIsAboutOpenChanged(bool value)     => RaisePageFlags();
    partial void OnIsTrafficOpenChanged(bool value)   => RaisePageFlags();

    private BypassMode GetBypassMode() =>
        BypassModeIndex == 1 ? BypassMode.AllSites : BypassMode.Whitelist;

    private async Task CheckSelfUpdateAndStartAsync()
    {
        var latest = await _selfUpdateService.GetLatestAsync();

        if (latest != null)
        {
            var current = _selfUpdateService.GetCurrentVersion();
            if (_selfUpdateService.IsNewerVersion(latest.Version, current))
            {
                _pendingSelfUpdate = latest;
                UpdateSelfUpdateMessage();
                IsSelfUpdateOpen = true;
                return;
            }
        }
        await AutoStartBypassIfNeededAsync();
    }

    private void UpdateSelfUpdateMessage()
    {
        if (_pendingSelfUpdate == null) return;
        var current = _selfUpdateService.GetCurrentVersion();
        SelfUpdateMessage = string.Format(Loc["SelfUpdateAvailableFmt"], _pendingSelfUpdate.Version, current);
    }

    public MainViewModel()
    {
        _config = _configService.LoadConfig();

        LocalizationService.Instance.PropertyChanged += OnLocalizationChanged;

        RefreshLocalizedOptions();

        _suppressThemeSync = true;
        ThemeIndex = _config.Theme switch { "Light" => 1, "Dark" => 2, _ => 0 };
        _suppressThemeSync = false;
        ApplyTheme(ThemeIndex);

        SetUpdateStatusFactory(() => Loc["CheckUpdates"]);
        RefreshServiceStatusText();

        _serviceViewModel = new ServiceViewModel(ZapretFolderPath, () => SelectedPreset);
        _serviceViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ServiceViewModel.IsOpen))
                RaisePageFlags();
        };

        TrafficViewModel = new TrafficViewModel();

        LoadPresets();

        _suppressStartWithWindows = true;
        StartWithWindows = AutoStartService.IsEnabled();
        _suppressStartWithWindows = false;

        _suppressAutoStartBypass = true;
        AutoStartBypass = _config.AutoStartBypass;
        _suppressAutoStartBypass = false;

        _suppressBypassModeSync = true;
        BypassModeIndex = _config.BypassModeIndex;
        _suppressBypassModeSync = false;

        _ = CheckAutoUpdatesAsync();
        _ = RefreshRuntimeStateAsync();
        _ = CheckSelfUpdateAndStartAsync();
    }

    private void OnLocalizationChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (!string.IsNullOrEmpty(e.PropertyName) && e.PropertyName != "Item[]")
            return;

        RefreshLocalizedOptions();
        RefreshServiceStatusText();
        RefreshUpdateStatusText();
        UpdateSelfUpdateMessage();
        OnPropertyChanged(nameof(SelectedPresetText));
        OnPropertyChanged(nameof(BypassModeDescription));
    }

    private void RefreshLocalizedOptions()
    {
        _suppressGameFilterSync = true;
        _suppressThemeSync = true;

        var gf = GameFilterModeIndex;
        var th = ThemeIndex;

        GameFilterOptions = new ObservableCollection<string>
        {
            Loc["GameFilterOff"],
            Loc["GameFilterAll"],
            Loc["GameFilterTcp"],
            Loc["GameFilterUdp"]
        };

        ThemeOptions = new ObservableCollection<string>
        {
            Loc["ThemeSystem"],
            Loc["ThemeLight"],
            Loc["ThemeDark"]
        };

        GameFilterModeIndex = gf;
        ThemeIndex = th;

        _suppressGameFilterSync = false;
        _suppressThemeSync = false;
    }

    private void RefreshServiceStatusText()
    {
        ServiceStatusText = IsServiceRunning ? Loc["StatusRunning"] : Loc["StatusStopped"];
    }

    private void SetUpdateStatusFactory(Func<string> factory)
    {
        _updateStatusFactory = factory;
        UpdateStatusText = factory();
    }

    private void RefreshUpdateStatusText()
    {
        if (_updateStatusFactory != null)
            UpdateStatusText = _updateStatusFactory();
    }

    public void LoadPresets()
    {
        Presets.Clear();

        if (!Directory.Exists(ZapretFolderPath)) return;

        string targetDir = ZapretFolderPath;

        if (!string.IsNullOrEmpty(_config.CurrentVersion))
        {
            var versionDir = Path.Combine(ZapretFolderPath, _config.CurrentVersion.Trim());
            if (Directory.Exists(versionDir))
                targetDir = versionDir;
        }

        if (targetDir == ZapretFolderPath)
        {
            var versionDirs = Directory.GetDirectories(ZapretFolderPath)
                .Where(d => Version.TryParse(Path.GetFileName(d), out _))
                .OrderByDescending(d => Version.Parse(Path.GetFileName(d)))
                .ToList();

            if (versionDirs.Count > 0)
            {
                targetDir = versionDirs[0];
                var version = Path.GetFileName(targetDir);
                if (!string.Equals(_config.CurrentVersion, version, StringComparison.OrdinalIgnoreCase))
                {
                    _config.CurrentVersion = version;
                    _configService.SaveConfig(_config);
                }
            }
        }

        if (!Directory.Exists(targetDir)) return;

        var batFiles = Directory.GetFiles(targetDir, "*.bat", SearchOption.TopDirectoryOnly)
            .Where(f => !Path.GetFileName(f).Equals("service.bat", StringComparison.OrdinalIgnoreCase) &&
                        !Path.GetFileName(f).StartsWith("service_", StringComparison.OrdinalIgnoreCase));

        var sortedFiles = batFiles.OrderBy(f =>
            Regex.Replace(Path.GetFileNameWithoutExtension(f), @"\d+", m => m.Value.PadLeft(10, '0')));

        foreach (var file in sortedFiles)
        {
            Presets.Add(new ZapretPreset
            {
                Name = Path.GetFileNameWithoutExtension(file),
                FilePath = file
            });
        }

        var savedPreset = Presets.FirstOrDefault(p =>
            p.Name.Equals(_config.LastSelectedPreset, StringComparison.OrdinalIgnoreCase));
        SelectedPreset = savedPreset ?? Presets.FirstOrDefault();
    }

    partial void OnSelectedPresetChanged(ZapretPreset? value)
    {
        if (value != null)
        {
            _config.LastSelectedPreset = value.Name;
            _configService.SaveConfig(_config);
        }
        OnPropertyChanged(nameof(SelectedPresetText));
        if (value != null && IsServiceRunning)
        {
            _ = SwapPresetAsync(value);
        }
    }

    private bool _isSwappingPreset;

    private async Task SwapPresetAsync(ZapretPreset preset)
    {
        if (_isSwappingPreset) return;
        _isSwappingPreset = true;

        try
        {
            ServiceStatusText = string.Format(Loc["StatusSwitching"], preset.Name);
            _zapretManager.StopZapret();
            await Task.Delay(500);
            if (_zapretManager.StartZapret(preset.FilePath, GetBypassMode()))
            {
                ServiceStatusText = Loc["StatusRunning"];
            }
            else
            {
                IsServiceRunning = false;
                ServiceStatusText = Loc["StatusError"];
            }
        }
        catch
        {
            IsServiceRunning = false;
            ServiceStatusText = Loc["StatusError"];
        }
        finally
        {
            _isSwappingPreset = false;
        }
    }

    private async Task AutoStartBypassIfNeededAsync()
    {
        if (!AutoStartBypass) return;
        if (SelectedPreset == null) return;
        if (IsServiceRunning) return;

        await Task.Delay(1500);

        if (SelectedPreset == null || IsServiceRunning) return;

        try
        {
            if (_zapretManager.StartZapret(SelectedPreset.FilePath, GetBypassMode()))
            {
                IsServiceRunning = true;
                ServiceStatusText = Loc["StatusRunning"];
            }
        }
        catch
        {
            IsServiceRunning = false;
            RefreshServiceStatusText();
        }
    }

    [RelayCommand]
    private Task TestCurrentStandard() => RunCurrentModeTestAsync("1");

    [RelayCommand]
    private Task TestCurrentDpi() => RunCurrentModeTestAsync("2");

    [RelayCommand]
    private Task TestAllModes() => RunAllModesTestAsync();

    [RelayCommand]
    private void ShowLastResult()
    {
        if (!_hasLastResult)
        {
            TestModalTitle = Loc["TestStandard"];
            TestModalSubtitle = string.Empty;
            TestModalBody = Loc["MsgTestNoResult"];
            IsTestModalOpen = true;
            return;
        }

        TestModalTitle = _lastTestTitle;
        TestModalSubtitle = _lastTestSubtitle;
        TestModalBody = _lastTestBody;
        IsTestModalOpen = true;
    }

    [RelayCommand]
    private void CloseTestModal() => IsTestModalOpen = false;

    [RelayCommand]
    private async Task DismissSelfUpdateAsync()
    {
        IsSelfUpdateOpen = false;
        _pendingSelfUpdate = null;
        await AutoStartBypassIfNeededAsync();
    }

    [RelayCommand]
    private async Task ApplySelfUpdateAsync()
    {
        if (_pendingSelfUpdate == null) return;

        IsSelfUpdateDownloading = true;
        IsSelfUpdateProgressVisible = true;
        SelfUpdateProgress = 0;
        SelfUpdateMessage = Loc["SelfUpdateDownloading"];

        var ok = await _selfUpdateService.PrepareUpdateAsync(
            _pendingSelfUpdate.DownloadUrl,
            onProgress: (downloaded, total) =>
            {
                if (total <= 0) return;

                var percent = downloaded * 100.0 / total;
                var mb = downloaded / 1024.0 / 1024.0;
                var totalMb = total / 1024.0 / 1024.0;

                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    SelfUpdateProgress = percent;
                    SelfUpdateMessage = $"{Loc["SelfUpdateDownloading"]}  {mb:F1} / {totalMb:F1} МБ";
                });
            });

        IsSelfUpdateProgressVisible = false;

        if (!ok)
        {
            IsSelfUpdateDownloading = false;
            SelfUpdateMessage = Loc["SelfUpdateFailed"];
            return;
        }

        ShutdownForExit();
        RequestExit?.Invoke();
    }

    private async Task<(bool wasServiceInstalled, bool wasBypassRunning)> SuppressZapretForTestAsync()
    {
        var sm = new ServiceManager(ZapretFolderPath);
        var wasServiceInstalled = await sm.CheckServiceInstalledAsync();
        var wasBypassRunning = Process.GetProcessesByName("winws").Length > 0;

        if (wasServiceInstalled)
        {
            await sm.RunAsync("remove");
        }
        else if (wasBypassRunning)
        {
            _zapretManager.StopZapret();
        }

        foreach (var p in Process.GetProcessesByName("winws"))
            try { p.Kill(); } catch { }

        await Task.Delay(800);
        return (wasServiceInstalled, wasBypassRunning);
    }

    private async Task RestoreZapretAfterTestAsync((bool wasServiceInstalled, bool wasBypassRunning) state)
    {
        if (SelectedPreset == null)
        {
            IsServiceRunning = false;
            RefreshServiceStatusText();
            return;
        }

        var presetName = Path.GetFileName(SelectedPreset.FilePath);

        if (state.wasServiceInstalled)
        {
            var sm = new ServiceManager(ZapretFolderPath);
            var (ok, _) = await sm.RunAsync("install", presetName);
            IsServiceRunning = ok;
        }
        else if (state.wasBypassRunning)
        {
            IsServiceRunning = _zapretManager.StartZapret(SelectedPreset.FilePath, GetBypassMode());
        }
        else
        {
            IsServiceRunning = false;
        }

        RefreshServiceStatusText();
    }

    private async Task RunCurrentModeTestAsync(string type)
    {
        if (IsAnyTestRunning) return;

        if (SelectedPreset == null)
        {
            _lastTestTitle = Loc["MsgPresetNotSelected"];
            _lastTestSubtitle = string.Empty;
            _lastTestBody = Loc["MsgPresetNotSelected"];
            _hasLastResult = true;

            TestModalTitle = _lastTestTitle;
            TestModalSubtitle = _lastTestSubtitle;
            TestModalBody = _lastTestBody;
            IsTestModalOpen = true;
            return;
        }

        IsAnyTestRunning = true;

        var state = await SuppressZapretForTestAsync();
        var subtitle = type == "2" ? Loc["TestDpi"] : Loc["TestStandard"];
        var presetName = SelectedPreset.Name;
        var presetPath = SelectedPreset.FilePath;

        try
        {
            var sm = new ServiceManager(ZapretFolderPath);
            var (_, output) = await sm.RunAsync("run_tests", type, presetPath);

            _lastTestTitle = string.Format(Loc["TestModalTitleFmt"], presetName);
            _lastTestSubtitle = subtitle;
            _lastTestBody = output;
            _hasLastResult = true;

            TestModalTitle = _lastTestTitle;
            TestModalSubtitle = _lastTestSubtitle;
            TestModalBody = _lastTestBody;
            IsTestModalOpen = true;
        }
        finally
        {
            IsAnyTestRunning = false;
            await RestoreZapretAfterTestAsync(state);
        }
    }

    private async Task RunAllModesTestAsync()
    {
        if (IsAnyTestRunning) return;

        if (!ServiceManager.IsAdmin())
        {
            _lastTestTitle = Loc["TestingAllModes"];
            _lastTestSubtitle = string.Empty;
            _lastTestBody = Loc["MsgTestAllNeedAdmin"];
            _hasLastResult = true;

            TestModalTitle = _lastTestTitle;
            TestModalSubtitle = _lastTestSubtitle;
            TestModalBody = _lastTestBody;
            IsTestModalOpen = true;
            return;
        }

        if (Presets.Count == 0) return;

        IsAnyTestRunning = true;

        var state = await SuppressZapretForTestAsync();

        try
        {
            var sm = new ServiceManager(ZapretFolderPath);
            var (_, output) = await sm.RunAsync("run_tests", "1", null);

            _lastTestTitle = Loc["TestingAllModes"];
            _lastTestSubtitle = $"{Presets.Count} {Loc["BypassModes"].ToLowerInvariant()}";
            _lastTestBody = output;
            _hasLastResult = true;

            TestModalTitle = _lastTestTitle;
            TestModalSubtitle = _lastTestSubtitle;
            TestModalBody = _lastTestBody;
            IsTestModalOpen = true;
        }
        finally
        {
            IsAnyTestRunning = false;
            await RestoreZapretAfterTestAsync(state);
        }
    }

    public async Task RefreshRuntimeStateAsync()
    {
        try
        {
            var sm = new ServiceManager(ZapretFolderPath);
            var installed = await sm.CheckServiceInstalledAsync();

            _suppressServiceSync = true;
            IsServiceInstalled = installed;
            _suppressServiceSync = false;

            _suppressGameFilterSync = true;
            GameFilterModeIndex = sm.GetGameFilterMode();
            _suppressGameFilterSync = false;

            var isBypassRunning = Process.GetProcessesByName("winws").Length > 0;
            IsServiceRunning = isBypassRunning;
            RefreshServiceStatusText();
        }
        catch { }
    }

    public void ShutdownForExit()
    {
        try { _zapretManager.StopZapret(); } catch { }
    }

    partial void OnIsServiceInstalledChanged(bool value)
    {
        if (_suppressServiceSync) return;
        _ = ApplyServiceStateAsync(value);
    }

    partial void OnGameFilterModeIndexChanged(int value)
    {
        if (_suppressGameFilterSync) return;
        if (value < 0 || value > 3) return;
        _ = ApplyGameFilterStateAsync(value);
    }

    partial void OnThemeIndexChanged(int value)
    {
        if (_suppressThemeSync) return;
        if (value < 0 || value > 2) return;

        ApplyTheme(value);

        _config.Theme = value switch { 1 => "Light", 2 => "Dark", _ => "System" };
        _configService.SaveConfig(_config);
    }

    partial void OnStartWithWindowsChanged(bool value)
    {
        if (_suppressStartWithWindows) return;

        if (value) AutoStartService.Enable();
        else       AutoStartService.Disable();

        _config.StartWithWindows = value;
        _configService.SaveConfig(_config);
    }

    partial void OnAutoStartBypassChanged(bool value)
    {
        if (_suppressAutoStartBypass) return;

        _config.AutoStartBypass = value;
        _configService.SaveConfig(_config);
    }

    partial void OnBypassModeIndexChanged(int value)
    {
        OnPropertyChanged(nameof(BypassModeDescription));

        if (_suppressBypassModeSync) return;

        _config.BypassModeIndex = value;
        _configService.SaveConfig(_config);
        if (IsServiceRunning && SelectedPreset != null)
            _ = SwapPresetAsync(SelectedPreset);
    }

    private void ApplyTheme(int idx)
    {
        if (Application.Current == null) return;
        Application.Current.RequestedThemeVariant = idx switch
        {
            1 => ThemeVariant.Light,
            2 => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };
    }

    private async Task ApplyServiceStateAsync(bool install)
    {
        try
        {
            var sm = new ServiceManager(ZapretFolderPath);

            if (install)
            {
                var presetPath = SelectedPreset?.FilePath;
                if (presetPath == null && !string.IsNullOrEmpty(_config.LastSelectedPreset))
                {
                    var found = Presets.FirstOrDefault(p =>
                        p.Name.Equals(_config.LastSelectedPreset, StringComparison.OrdinalIgnoreCase));
                    presetPath = found?.FilePath;
                }

                if (presetPath == null)
                {
                    SettingsStatusText = Loc["MsgPresetNotSelected"];
                    RevertServiceToggle(false);
                    return;
                }

                var presetName = Path.GetFileName(presetPath);
                var (ok, output) = await sm.RunAsync("install", presetName);
                SettingsStatusText = ok
                    ? string.Format(Loc["MsgServiceInstalledOk"], presetName)
                    : output;
                if (!ok) RevertServiceToggle(false);
            }
            else
            {
                var (ok, output) = await sm.RunAsync("remove");
                SettingsStatusText = ok ? Loc["MsgServiceRemovedOk"] : output;
                if (!ok) RevertServiceToggle(true);
            }
        }
        catch (Exception ex)
        {
            SettingsStatusText = string.Format(Loc["MsgError"], ex.Message);
            RevertServiceToggle(!install);
        }
    }

    private async Task ApplyGameFilterStateAsync(int mode)
    {
        try
        {
            var sm = new ServiceManager(ZapretFolderPath);
            var (ok, output) = await sm.RunAsync("game_filter", mode.ToString());
            SettingsStatusText = output;
            if (!ok) RevertGameFilter();
        }
        catch (Exception ex)
        {
            SettingsStatusText = string.Format(Loc["MsgError"], ex.Message);
            RevertGameFilter();
        }
    }

    private void RevertServiceToggle(bool value)
    {
        _suppressServiceSync = true;
        IsServiceInstalled = value;
        _suppressServiceSync = false;
    }

    private void RevertGameFilter()
    {
        try
        {
            var sm = new ServiceManager(ZapretFolderPath);
            var actual = sm.GetGameFilterMode();
            _suppressGameFilterSync = true;
            GameFilterModeIndex = actual;
            _suppressGameFilterSync = false;
        }
        catch { }
    }

    private async Task CheckAutoUpdatesAsync()
    {
        var releaseInfo = await _updateService.GetLatestReleaseInfoAsync();

        if (releaseInfo == null)
        {
            IsUpdateAvailable = false;
            SetUpdateStatusFactory(() => Loc["UpdateCheckFailed"]);
            return;
        }

        var (latestVersion, _) = releaseInfo.Value;
        var currentVersion = (_config.CurrentVersion ?? string.Empty).Trim();
        var versionFolderPath = Path.Combine(ZapretFolderPath, latestVersion);
        bool folderExists = Directory.Exists(versionFolderPath);
        bool versionsMatch = currentVersion.Equals(latestVersion, StringComparison.OrdinalIgnoreCase);

        if (versionsMatch || folderExists)
        {
            if (!versionsMatch)
            {
                _config.CurrentVersion = latestVersion;
                _configService.SaveConfig(_config);
            }

            IsUpdateAvailable = false;
            SetUpdateStatusFactory(() => string.Format(Loc["LatestVersion"], latestVersion));
        }
        else
        {
            IsUpdateAvailable = true;
            SetUpdateStatusFactory(() => string.Format(Loc["UpdateAvailable"], latestVersion));
        }
    }

    [RelayCommand]
    private void ToggleService()
    {
        if (IsServiceRunning)
        {
            try
            {
                _zapretManager.StopZapret();
            }
            catch { }

            IsServiceRunning = false;
            RefreshServiceStatusText();
        }
        else
        {
            if (SelectedPreset == null)
            {
                ServiceStatusText = Loc["MsgPresetNotSelected"];
                IsServiceRunning = false;
                return;
            }

            bool ok;
            try
            {
                ok = _zapretManager.StartZapret(SelectedPreset.FilePath, GetBypassMode());
            }
            catch
            {
                ok = false;
            }

            if (ok)
            {
                IsServiceRunning = true;
                RefreshServiceStatusText();
            }
            else
            {
                IsServiceRunning = false;
                ServiceStatusText = Loc["StatusError"];
            }
        }
    }

    [RelayCommand]
    private void OpenSettings()
    {
        IsSettingsOpen = true;
        ServiceViewModel.IsOpen = false;
        IsAboutOpen = false;
        IsTrafficOpen = false;
        SettingsStatusText = string.Empty;
        _ = RefreshRuntimeStateAsync();
    }

    [RelayCommand]
    private void CloseSettings() => IsSettingsOpen = false;

    [RelayCommand]
    private void OpenServiceView()
    {
        ServiceViewModel.IsOpen = true;
        IsSettingsOpen = false;
        IsAboutOpen = false;
        IsTrafficOpen = false;
        ServiceViewModel.LoadTabData();
    }

    [RelayCommand]
    private void ShowBypassPage()
    {
        IsSettingsOpen = false;
        ServiceViewModel.IsOpen = false;
        IsAboutOpen = false;
        IsTrafficOpen = false;
    }

    [RelayCommand]
    private void OpenTrafficPage()
    {
        IsSettingsOpen = false;
        ServiceViewModel.IsOpen = false;
        IsAboutOpen = false;
        IsTrafficOpen = true;
    }

    [RelayCommand]
    private void CloseTrafficPage() => IsTrafficOpen = false;

    [RelayCommand]
    private void OpenAbout()
    {
        IsSettingsOpen = false;
        ServiceViewModel.IsOpen = false;
        IsAboutOpen = true;
        IsTrafficOpen = false;
        _ = RefreshSelfUpdateStatusAsync();
    }

    [RelayCommand]
    private void OpenGitHub()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://github.com/qrw512/zapret-control",
                UseShellExecute = true
            });
        }
        catch { }
    }

    [RelayCommand]
    private async Task CheckSelfUpdateAsync()
    {
        if (IsUpdating) return;

        IsUpdating = true;
        SelfUpdateStatusText = Loc["CheckUpdates"];

        var latest = await _selfUpdateService.GetLatestAsync();
        if (latest == null)
        {
            IsUpdating = false;
            SelfUpdateStatusText = Loc["UpdateCheckFailed"];
            return;
        }

        var current = _selfUpdateService.GetCurrentVersion();
        if (_selfUpdateService.IsNewerVersion(latest.Version, current))
        {
            _pendingSelfUpdate = latest;
            UpdateSelfUpdateMessage();
            IsSelfUpdateOpen = true;
            SelfUpdateStatusText = string.Format(Loc["UpdateAvailable"], latest.Version);
        }
        else
        {
            SelfUpdateStatusText = Loc["UpToDateMsg"];
        }

        IsUpdating = false;
    }

    private async Task RefreshSelfUpdateStatusAsync()
    {
        var latest = await _selfUpdateService.GetLatestAsync();
        if (latest == null) return;

        var current = _selfUpdateService.GetCurrentVersion();
        SelfUpdateStatusText = _selfUpdateService.IsNewerVersion(latest.Version, current)
            ? string.Format(Loc["UpdateAvailable"], latest.Version)
            : Loc["UpToDateMsg"];
    }

    [RelayCommand] private void CloseServiceView() => ServiceViewModel.IsOpen = false;

    [RelayCommand]
    private async Task CheckAndInstallUpdatesAsync()
    {
        IsUpdating = true;
        SetUpdateStatusFactory(() => Loc["CheckUpdates"]);

        var releaseInfo = await _updateService.GetLatestReleaseInfoAsync();
        if (releaseInfo == null)
        {
            IsUpdating = false;
            if (IsUpdateAvailable) SetUpdateStatusFactory(() => Loc["UpdateCheckFailed"]);
            return;
        }

        var (latestVersion, downloadUrl) = releaseInfo.Value;
        var versionFolderPath = Path.Combine(ZapretFolderPath, latestVersion);
        _config.CurrentVersion = latestVersion;
        _configService.SaveConfig(_config);

        try
        {
            await _updateService.DownloadAndExtractAsync(downloadUrl, versionFolderPath);

            IsUpdateAvailable = false;
            SetUpdateStatusFactory(() => string.Format(Loc["UpdateSuccess"], latestVersion));
            LoadPresets();
        }
        catch (Exception ex)
        {
            SetUpdateStatusFactory(() => ex.Message);
        }
        finally
        {
            IsUpdating = false;
        }
    }
}
