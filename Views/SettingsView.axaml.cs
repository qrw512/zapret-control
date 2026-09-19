using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using ZapretGui.Services;
using ZapretGui.ViewModels;

namespace ZapretGui.Views;

public partial class SettingsView : UserControl
{
    private readonly ConfigService _configService = new();
    private AppConfig _config;

    public SettingsView()
    {
        InitializeComponent();

        _config = _configService.LoadConfig();
        LanguageComboBox.SelectedIndex = _config.Language == "EN" ? 1 : 0;
        LanguageComboBox.SelectionChanged += LanguageComboBox_SelectionChanged;
    }

    private async void BrowseButton_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        if (DataContext is MainViewModel mainVm)
        {
            var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = mainVm.Loc["PathToZapret"],
                AllowMultiple = false
            });

            if (folders.Count > 0)
            {
                mainVm.ZapretFolderPath = folders[0].Path.LocalPath;
                mainVm.LoadPresets();
            }
        }
    }

    private void LanguageComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var lang = LanguageComboBox.SelectedIndex == 1 ? "EN" : "RU";
        _config.Language = lang;

        LocalizationService.Instance.CurrentLanguage = lang;
        _configService.SaveConfig(_config);
    }
}
