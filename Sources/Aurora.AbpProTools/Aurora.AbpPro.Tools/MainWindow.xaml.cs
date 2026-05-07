using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Aurora.AbpPro.Tools.ViewModels;
using HandyControl.Tools;
using HandyControl.Tools.Extension;

namespace Aurora.AbpPro.Tools;

/// <summary>
/// 主窗口：仅承载布局，所有逻辑下沉至 MainWindowViewModel
/// </summary>
public partial class MainWindow
{
    private readonly MainWindowViewModel _viewModel;

    /// <summary>
    /// 折叠前缓存的左侧栏列宽，用于展开时还原
    /// </summary>
    private GridLength _columnDefinitionWidth;

    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;

        Loaded += OnLoaded;
    }

    /// <summary>
    /// 窗口加载完成（保留扩展点；当前所有控件均通过绑定/事件直接驱动）
    /// </summary>
    private void OnLoaded(object sender, RoutedEventArgs e) { }

    /// <summary>
    /// 设置按钮点击：弹出/收起配置面板
    /// </summary>
    private void ButtonConfig_OnClick(object sender, RoutedEventArgs e)
    {
        PopupConfig.IsOpen = !PopupConfig.IsOpen;
    }

    /// <summary>
    /// 语言按钮点击事件（事件冒泡到容器统一处理）
    /// </summary>
    private void ButtonLangs_OnClick(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is Button { Tag: string language })
        {
            _viewModel.SwitchLanguageCommand.Execute(language);
        }
    }

    /// <summary>
    /// 主题按钮点击事件（事件冒泡到容器统一处理）
    /// </summary>
    private void ButtonSkins_OnClick(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is Button { Tag: string skin })
        {
            _viewModel.SwitchThemeCommand.Execute(skin);
        }
    }

    /// <summary>
    /// 折叠侧边栏：左移动画后将其列宽收为 0
    /// </summary>
    private void OnLeftMainContentShiftOut(object sender, RoutedEventArgs e)
    {
        ButtonShiftOut.Collapse();

        double targetValue = -ColumnDefinitionLeft.MaxWidth;
        _columnDefinitionWidth = ColumnDefinitionLeft.Width;

        var animation = AnimationHelper.CreateAnimation(targetValue, milliseconds: 100);
        animation.FillBehavior = FillBehavior.Stop;
        animation.Completed += OnAnimationCompleted;
        LeftMainContent.RenderTransform.BeginAnimation(TranslateTransform.XProperty, animation);

        void OnAnimationCompleted(object? _, EventArgs args)
        {
            animation.Completed -= OnAnimationCompleted;
            LeftMainContent.RenderTransform.SetCurrentValue(
                TranslateTransform.XProperty,
                targetValue
            );

            // 让右侧内容跨越两列，占满整个主体区域

            ColumnDefinitionLeft.MinWidth = 0;
            // 注意：new GridLength() 默认为 1*，必须显式给 0，否则左列仍会按比例占位
            ColumnDefinitionLeft.Width = new GridLength(0);
            ButtonShiftIn.Show();
        }
    }

    /// <summary>
    /// 展开侧边栏：右移动画后还原列宽
    /// </summary>
    private void OnLeftMainContentShiftIn(object sender, RoutedEventArgs e)
    {
        ButtonShiftIn.Collapse();

        // 此时列宽已被收起为 0，把平移动画回到列内（X=0）即可
        const double targetValue = 0d;

        var animation = AnimationHelper.CreateAnimation(targetValue, milliseconds: 100);
        animation.FillBehavior = FillBehavior.Stop;
        animation.Completed += OnAnimationCompleted;
        LeftMainContent.RenderTransform.BeginAnimation(TranslateTransform.XProperty, animation);

        void OnAnimationCompleted(object? _, EventArgs args)
        {
            animation.Completed -= OnAnimationCompleted;
            LeftMainContent.RenderTransform.SetCurrentValue(
                TranslateTransform.XProperty,
                targetValue
            );

            ColumnDefinitionLeft.MinWidth = 180;
            ColumnDefinitionLeft.Width = _columnDefinitionWidth;
            ButtonShiftOut.Show();
        }
    }
}
