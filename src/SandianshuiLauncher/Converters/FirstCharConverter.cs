using System.Globalization;
using System.Windows.Data;

namespace SandianshuiLauncher.Converters;

/// <summary>取字符串首字符（用于头像占位）。</summary>
public sealed class FirstCharConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string text = value as string ?? string.Empty;
        return text.Length > 0 ? text[0].ToString().ToUpper(culture) : "?";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
