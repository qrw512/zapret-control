namespace ZapretGui.Models;

public class ServiceSubAction
{
    public string Label { get; set; } = string.Empty;
    public string Argument { get; set; } = string.Empty;
    public ServiceTaskItem? Parent { get; set; }
}
