using Avalonia;

namespace AuroraStruct3D.Avalonia;

/// <summary>
/// 程序入口
/// </summary>
internal sealed class Program
{
    /// <summary>
    /// 应用程序主入口点
    /// </summary>
    [STAThread]
    public static void Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    /// <summary>
    /// 构建 Avalonia 应用配置
    /// </summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
