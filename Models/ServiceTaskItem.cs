using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ZapretGui.Models;

public enum TaskExecutionState
{
    Idle,
    Running,
    Success,
    Failed
}

public partial class ServiceTaskItem : ObservableObject
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string CommandLabel { get; set; } = string.Empty;
    public string Icon { get; set; } = "⚙️";

    public bool HasSubActions { get; set; }
    public ObservableCollection<ServiceSubAction> SubActions { get; } = new();

    [ObservableProperty]
    private TaskExecutionState _state = TaskExecutionState.Idle;

    [ObservableProperty]
    private string _statusText = "Готов к запуску";

    [ObservableProperty]
    private string _lastOutput = string.Empty;

    [ObservableProperty]
    private bool _hasOutput;

    [ObservableProperty]
    private bool _isExpanded;
}
