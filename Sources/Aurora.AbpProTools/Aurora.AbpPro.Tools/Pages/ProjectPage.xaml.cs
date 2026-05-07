using System.Windows.Controls;
using Aurora.AbpPro.Tools.ViewModels;

namespace Aurora.AbpPro.Tools.Pages;

/// <summary>
/// 项目页
/// </summary>
public partial class ProjectPage : UserControl
{
    public ProjectPage(ProjectPageViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
