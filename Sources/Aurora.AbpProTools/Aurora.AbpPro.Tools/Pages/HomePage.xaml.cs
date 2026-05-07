using System.Windows.Controls;
using Aurora.AbpPro.Tools.ViewModels;

namespace Aurora.AbpPro.Tools.Pages;

/// <summary>
/// 主页
/// </summary>
public partial class HomePage : UserControl
{
    public HomePage(HomePageViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
