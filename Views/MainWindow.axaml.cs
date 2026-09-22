using System;
using Avalonia.Controls;
using Avalonia.Input;
using ZapretGui.ViewModels;

namespace ZapretGui.Views;

public partial class MainWindow : Window
{
    private bool _isExplicitExit;
    private bool _startHidden;

    public MainWindow() : this(false) { }

    public MainWindow(bool startHidden)
    {
        _startHidden = startHidden;

        try
        {
            DataContext = new MainViewModel();
            InitializeComponent();
        }
        catch (Exception ex)
        {
            Program.WriteCrashLog("MainWindow.Initialize", ex);
            throw;
        }
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (_startHidden)
        {
            _startHidden = false;
            Hide();
        }
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

    private void MaximizeButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
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
