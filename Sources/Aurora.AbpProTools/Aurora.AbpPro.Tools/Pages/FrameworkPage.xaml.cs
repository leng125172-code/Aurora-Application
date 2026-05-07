using System.Windows;
using System.Windows.Controls;
using Aurora.AbpPro.Tools.ViewModels;

namespace Aurora.AbpPro.Tools.Pages;

/// <summary>
/// 框架页：以卡片形式展示已注册仓库的 Release 信息
/// </summary>
public partial class FrameworkPage : UserControl
{
    private readonly FrameworkPageViewModel _viewModel;

    public FrameworkPage(FrameworkPageViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
    }

    /// <summary>
    /// 页面加载完成后触发首次数据加载
    /// </summary>
    private void OnLoadedHandler(object sender, RoutedEventArgs e)
    {
        if (_viewModel.LoadCommand.CanExecute(null))
        {
            _viewModel.LoadCommand.Execute(null);
        }
    }
}
