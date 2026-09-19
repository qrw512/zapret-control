using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.Input;
using ZapretGui.Services;
using ZapretGui.ViewModels;
using ZapretGui.Views;

namespace ZapretGui;

public partial class App : Application
{
    public LocalizationService Loc => LocalizationService.Instance;

    private MainViewModel? _main;
    private NativeMenuItem? _presetsRoot;
    private NativeMenuItem? _toggleItem;
    private NativeMenuItem? _openItem;
    private NativeMenuItem? _exitItem;

    public App()
    {
        DataContext = this;
    }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var config = new ConfigService().LoadConfig();

        RequestedThemeVariant = config.Theme switch
        {
            "Light" => ThemeVariant.Light,
            "Dark"  => ThemeVariant.Dark,
            _       => ThemeVariant.Default
        };

        LocalizationService.Instance.CurrentLanguage = config.Language;

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            var mw = new MainWindow();
            _main = mw.DataContext as MainViewModel;
            desktop.MainWindow = mw;

            if (_main != null)
            {
                _main.PropertyChanged += OnMainPropertyChanged;
                _main.Presets.CollectionChanged += (_, _) => RebuildPresetsMenu();
            }

            LocalizationService.Instance.PropertyChanged += OnLocalizationChanged;
            desktop.Exit += (_, _) => ShutdownZapret();

            BuildTrayMenu();
        }

        base.OnFrameworkInitializationCompleted();
    }


    private void ShutdownZapret()
    {
        try { _main?.ShutdownForExit(); } catch { }
        try
        {
            foreach (var p in Process.GetProcessesByName("winws"))
            {
                try { p.Kill(); } catch { }
            }
        }
        catch { }
    }
    private void BuildTrayMenu()
    {
        var tray = TrayIcon.GetIcons(this)?.FirstOrDefault();
        if (tray == null || _main == null) return;

        var menu = new NativeMenu();

        _presetsRoot = new NativeMenuItem { Header = Loc["TrayPresetsHeader"] };
        _presetsRoot.Menu = new NativeMenu();
        menu.Add(_presetsRoot);

        menu.Add(new NativeMenuItemSeparator());

        _toggleItem = new NativeMenuItem();
        _toggleItem.Click += (_, _) =>
        {
            if (_main == null) return;
            _main.IsServiceRunning = !_main.IsServiceRunning;
            _main.ToggleServiceCommand.Execute(null);
        };
        menu.Add(_toggleItem);

        menu.Add(new NativeMenuItemSeparator());

        _openItem = new NativeMenuItem { Header = Loc["TrayOpen"] };
        _openItem.Click += (_, _) => ShowWindow();
        menu.Add(_openItem);

        menu.Add(new NativeMenuItemSeparator());

        _exitItem = new NativeMenuItem { Header = Loc["TrayExit"] };
        _exitItem.Click += (_, _) => ExitApplication();
        menu.Add(_exitItem);

        tray.Menu = menu;

        RebuildPresetsMenu();
        RefreshToggleHeader();
    }

    private void RebuildPresetsMenu()
    {
        if (_presetsRoot?.Menu == null || _main == null) return;

        _presetsRoot.Header = Loc["TrayPresetsHeader"];
        _presetsRoot.Menu.Items.Clear();

        foreach (var preset in _main.Presets)
        {
            var captured = preset;
            var item = new NativeMenuItem
            {
                Header = preset.Name,
                ToggleType = MenuItemToggleType.Radio,
                IsChecked = preset == _main.SelectedPreset
            };
            item.Click += (_, _) =>
            {
                if (_main != null)
                    _main.SelectedPreset = captured;
                RebuildPresetsMenu();
            };
            _presetsRoot.Menu.Items.Add(item);
        }
    }

    private void RefreshToggleHeader()
    {
        if (_toggleItem == null || _main == null) return;
        _toggleItem.Header = _main.IsServiceRunning
            ? Loc["TrayToggleStop"]
            : Loc["TrayToggleStart"];
    }

    private void OnLocalizationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!string.IsNullOrEmpty(e.PropertyName) && e.PropertyName != "Item[]")
            return;

        RefreshToggleHeader();
        if (_presetsRoot != null) _presetsRoot.Header = Loc["TrayPresetsHeader"];
        if (_openItem    != null) _openItem.Header    = Loc["TrayOpen"];
        if (_exitItem    != null) _exitItem.Header    = Loc["TrayExit"];
    }

    private void OnMainPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsServiceRunning))
            RefreshToggleHeader();
        else if (e.PropertyName == nameof(MainViewModel.SelectedPreset))
            RebuildPresetsMenu();
    }

    [RelayCommand]
    private void TrayIconClicked() => ShowWindow();

    [RelayCommand]
    private void ShowWindow()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow is MainWindow mw)
        {
            mw.Show();
            mw.WindowState = WindowState.Normal;
            mw.Activate();
        }
    }

    [RelayCommand]
    private void ExitApplication()
    {
        ShutdownZapret();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (desktop.MainWindow is MainWindow mw)
                mw.ExitApplication();
            desktop.Shutdown();
        }
    }
}
