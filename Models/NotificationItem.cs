using CommunityToolkit.Mvvm.ComponentModel;

namespace ZapretGui.Models;

public partial class NotificationItem : ObservableObject
{
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public bool IsError { get; set; }
}
