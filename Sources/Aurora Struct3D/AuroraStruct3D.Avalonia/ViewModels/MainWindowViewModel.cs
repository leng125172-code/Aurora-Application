using CommunityToolkit.Mvvm.ComponentModel;

namespace AuroraStruct3D.Avalonia.ViewModels;

/// <summary>
/// 主窗口 ViewModel
/// </summary>
public partial class MainWindowViewModel : ViewModelBase
{
    /// <summary>
    /// 窗口标题
    /// </summary>
    [ObservableProperty]
    private string _title = "Aurora Struct3D";
}
