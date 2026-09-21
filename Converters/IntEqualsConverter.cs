using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace ZapretGui.Converters;

public class IntEqualsConverter : IValueConverter
{
    public static readonly IntEqualsConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int i && parameter is string s && int.TryParse(s, out var p))
            return i == p;
        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is true && parameter is string s && int.TryParse(s, out var p))
            return p;
        return BindingOperations.DoNothing;
    }
}
