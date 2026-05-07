using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using Aurora.AbpPro.Tools.Services;

namespace Aurora.AbpPro.Tools.Pages;

/// <summary>
/// 「生成框架」结果弹窗：展示状态、统计、详细日志，并提供打开 slnx 文件夹的快捷按钮
/// </summary>
public partial class FrameworkGenerateResultWindow : Window
{
    public string HeaderText { get; }
    public string SummaryText { get; }
    public string DetailText { get; }
    public Brush HeaderBrush { get; }

    private readonly string _slnxPath;

    public FrameworkGenerateResultWindow(FrameworkGenerateResult result)
    {
        _slnxPath = result.SlnxPath;
        HeaderText = result.Success ? "生成框架成功" : "生成框架失败";
        HeaderBrush = result.Success
            ? new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32))
            : new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28));
        SummaryText =
            $"项目 {result.CopiedProjectCount} 个 | 文件 {result.CopiedFileCount} 个 | 重写 csproj {result.RewrittenCsprojCount} 个";

        var sb = new StringBuilder();
        foreach (var msg in result.Messages)
        {
            sb.AppendLine(msg);
        }
        if (!string.IsNullOrEmpty(result.Error))
        {
            sb.AppendLine();
            sb.AppendLine("=== 错误 ===");
            sb.AppendLine(result.Error);
        }
        sb.AppendLine();
        sb.AppendLine("=== 输出 ===");
        sb.AppendLine($"slnx：{result.SlnxPath}");
        DetailText = sb.ToString();

        DataContext = this;
        InitializeComponent();
    }

    private void OnOpenSlnxClick(object sender, RoutedEventArgs e)
    {
        var dir = Path.GetDirectoryName(_slnxPath);
        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
        {
            Process.Start(
                new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true }
            );
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
