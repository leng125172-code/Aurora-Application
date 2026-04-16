using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

using AuroraStruct3D.Avalonia.ViewModels;
using AuroraStruct3D.Avalonia.Views;

namespace AuroraStruct3D.Avalonia;

/// <summary>
/// 应用程序入口类
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// 初始化 AXAML 资源
    /// </summary>
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    /// 框架初始化完成后配置主窗口
    /// </summary>
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel()
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
