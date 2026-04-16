using System;

using Avalonia.Controls;
using Avalonia.Controls.Templates;

using AuroraStruct3D.Avalonia.ViewModels;

namespace AuroraStruct3D.Avalonia;

/// <summary>
/// 视图定位器：根据 ViewModel 类型自动查找对应的 View
/// </summary>
public class ViewLocator : IDataTemplate
{
    /// <summary>
    /// 根据 ViewModel 创建对应的视图实例
    /// </summary>
    public Control? Build(object? data)
    {
        if (data is null)
            return null;

        var name = data.GetType().FullName!
            .Replace("ViewModel", "View", StringComparison.Ordinal);

        var type = Type.GetType(name);

        if (type != null)
        {
            var control = (Control)Activator.CreateInstance(type)!;
            control.DataContext = data;
            return control;
        }

        return new TextBlock { Text = "未找到视图: " + name };
    }

    /// <summary>
    /// 判断数据是否为 ViewModelBase 类型
    /// </summary>
    public bool Match(object? data)
    {
        return data is ViewModelBase;
    }
}
