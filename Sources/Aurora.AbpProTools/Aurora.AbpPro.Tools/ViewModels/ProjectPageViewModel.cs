using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Aurora.AbpPro.Tools.ViewModels;

/// <summary>
/// 项目页 ViewModel
/// </summary>
public partial class ProjectPageViewModel : ObservableObject
{
    private readonly ILogger<ProjectPageViewModel> _logger;

    public ProjectPageViewModel(ILogger<ProjectPageViewModel> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// 演示命令：触发一条日志
    /// </summary>
    [RelayCommand]
    private void Run()
    {
        _logger.LogInformation("项目页：执行示例命令");
    }
}
