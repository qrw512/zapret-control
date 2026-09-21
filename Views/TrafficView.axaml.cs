using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;

namespace ZapretGui.Views;

public partial class TrafficView : UserControl
{
    public TrafficView()
    {
        InitializeComponent();
    }
}

public static class TrafficConverters
{
    public static readonly IValueConverter BoolToFontWeight =
        new FuncValueConverter<bool, FontWeight>(b => b ? FontWeight.Bold : FontWeight.Normal);
}
