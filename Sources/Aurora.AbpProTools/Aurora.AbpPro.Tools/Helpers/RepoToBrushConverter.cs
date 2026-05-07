using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Aurora.AbpPro.Tools.Helpers;

/// <summary>
/// 把仓库来源字符串映射为节点头部背景色（依赖图节点用）
/// </summary>
public class RepoToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var repo = value as string ?? string.Empty;
        return repo switch
        {
            "templates" => new SolidColorBrush(Color.FromRgb(0x4A, 0x90, 0xE2)),
            "abp-vnext-pro/aspnet-core" => new SolidColorBrush(Color.FromRgb(0x7E, 0xD3, 0x21)),
            "abpframework/abp" => new SolidColorBrush(Color.FromRgb(0xF5, 0xA6, 0x23)),
            "dotnetcore/CAP" => new SolidColorBrush(Color.FromRgb(0xBD, 0x10, 0xE0)),
            "HangfireIO/Hangfire" => new SolidColorBrush(Color.FromRgb(0xD0, 0x02, 0x1B)),
            _ => new SolidColorBrush(Color.FromRgb(0x60, 0x60, 0x60)),
        };
    }

    public object ConvertBack(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture
    ) => throw new NotSupportedException();
}
