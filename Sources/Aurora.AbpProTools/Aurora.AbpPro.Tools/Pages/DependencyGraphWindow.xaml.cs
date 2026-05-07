using System.Windows;
using Aurora.AbpPro.Tools.ViewModels;

namespace Aurora.AbpPro.Tools.Pages;

/// <summary>
/// 依赖图弹窗：以节点连线方式展示「读取框架」流水线的输出图谱
/// </summary>
public partial class DependencyGraphWindow : Window
{
    public DependencyGraphWindow(DependencyGraphViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
