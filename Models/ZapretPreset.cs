using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using ZapretGui.Services;

namespace ZapretGui.Models;

public partial class ZapretPreset : ObservableObject
{
    public string Name { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;

    public override string ToString() => Name;
}
