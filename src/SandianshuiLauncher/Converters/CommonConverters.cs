using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SandianshuiLauncher.Converters;

/// <summary>布尔值取反。</summary>
public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is not bool b || !b;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is not bool b || !b;
}

/// <summary>
/// null 或空字符串（含纯空白）折叠，非空文本可见。
/// 用于在可选字段（如相关模组标识）没有值时隐藏对应标签。
/// </summary>
public sealed class EmptyStringToCollapsedConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>模组启用状态 → 中文状态文案（已启用 / 已禁用）。</summary>
public sealed class EnabledToStatusConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool enabled && enabled ? "已启用" : "已禁用";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>模组启用状态 → 操作按钮文案（禁用 / 启用）。</summary>
public sealed class EnabledToActionConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool enabled && enabled ? "禁用" : "启用";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
