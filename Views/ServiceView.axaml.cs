using Avalonia.Controls;
using Avalonia.Interactivity;
using ZapretGui.ViewModels;

namespace ZapretGui.Views;

public partial class ServiceView : UserControl
{
    public ServiceView()
    {
        InitializeComponent();
    }

    private void Back_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ServiceViewModel vm)
        {
            vm.IsOpen = false;
        }
    }
}
