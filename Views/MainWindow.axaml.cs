using Avalonia.Controls;
using Avalonia.Input;
using ZapretGui.ViewModels;

namespace ZapretGui.Views;

public partial class MainWindow : Window
{
    private bool _isExplicitExit;

    public MainWindow()
    {
        DataContext = new MainViewModel();
        InitializeComponent();
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void MinimizeButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void CloseButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!_isExplicitExit)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnClosing(e);
    }

    public void ExitApplication()
    {
        _isExplicitExit = true;
        Close();
    }
}
