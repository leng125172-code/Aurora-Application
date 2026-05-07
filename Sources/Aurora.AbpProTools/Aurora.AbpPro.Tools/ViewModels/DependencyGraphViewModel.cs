using System.Collections.ObjectModel;
using System.Windows;
using Aurora.AbpPro.Tools.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Aurora.AbpPro.Tools.ViewModels;

/// <summary>
/// 依赖图节点：在 Nodify 编辑器中展示，绑定 Location（坐标）、Header（节点名）、SourceRepo（来源仓库，用于配色）
/// </summary>
public partial class DependencyNodeViewModel : ObservableObject
{
    /// <summary>项目名（同时作为节点唯一 Id）</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>来源仓库（用于头像/底色：templates / abpframework/abp ...）</summary>
    public string SourceRepo { get; init; } = string.Empty;

    /// <summary>真实 csproj 路径（鼠标悬停 Tooltip）</summary>
    public string FilePath { get; init; } = string.Empty;

    /// <summary>层级（0 = 模板）</summary>
    public int Depth { get; set; }

    /// <summary>当前画布坐标</summary>
    [ObservableProperty]
    private Point _location;

    /// <summary>是否已展开下一层（避免重复展开）</summary>
    [ObservableProperty]
    private bool _isExpanded;

    /// <summary>是否为叶子（没有可继续展开的子项）</summary>
    [ObservableProperty]
    private bool _isLeaf;
}

/// <summary>
/// 依赖图连线：起点节点 → 终点节点
/// </summary>
public class DependencyConnectionViewModel
{
    public DependencyNodeViewModel Source { get; init; } = null!;
    public DependencyNodeViewModel Target { get; init; } = null!;
}

/// <summary>
/// 依赖图 ViewModel：基于 FrameworkReadResult 进行延迟展开（默认仅模板 + 一层依赖，点击节点继续展开下一层）
/// </summary>
public partial class DependencyGraphViewModel : ObservableObject
{
    private readonly FrameworkReadResult _result;

    /// <summary>节点集合（绑定到 NodifyEditor.ItemsSource）</summary>
    public ObservableCollection<DependencyNodeViewModel> Nodes { get; } = new();

    /// <summary>连线集合（绑定到 NodifyEditor.Connections）</summary>
    public ObservableCollection<DependencyConnectionViewModel> Connections { get; } = new();

    /// <summary>每列分配的下一个 Y 坐标（用于自动堆叠布局）</summary>
    private readonly System.Collections.Generic.Dictionary<int, double> _nextYPerColumn = new();

    /// <summary>已加入图中的节点索引（按项目名）</summary>
    private readonly System.Collections.Generic.Dictionary<string, DependencyNodeViewModel> _index =
        new(System.StringComparer.OrdinalIgnoreCase);

    /// <summary>列宽（X 间距）</summary>
    private const double ColumnWidth = 320d;

    /// <summary>节点垂直间距</summary>
    private const double RowHeight = 90d;

    public DependencyGraphViewModel(FrameworkReadResult result)
    {
        _result = result;

        // 初始化：把全部种子放到第 0 列，再把它们的直接子节点（已存在 Edges）放到第 1 列
        foreach (var t in result.SeedProjects)
        {
            var node = AddNode(
                new DependencyNodeViewModel
                {
                    Name = t.Name,
                    SourceRepo = t.SourceRepo,
                    FilePath = t.FilePath,
                    Depth = 0,
                }
            );
            ExpandNode(node);
        }
    }

    /// <summary>
    /// 双击节点展开下一层（从 Edges 中取该节点的直接子项目）
    /// </summary>
    [RelayCommand]
    private void Expand(DependencyNodeViewModel? node)
    {
        if (node is null)
        {
            return;
        }
        ExpandNode(node);
    }

    /// <summary>
    /// 展开指定节点的所有下游：
    /// - 已存在的子节点：补一条边
    /// - 新发现的子节点：在 (parent.Depth+1) 列追加
    /// </summary>
    private void ExpandNode(DependencyNodeViewModel parent)
    {
        if (parent.IsExpanded)
        {
            return;
        }
        parent.IsExpanded = true;

        if (!_result.Edges.TryGetValue(parent.Name, out var children) || children.Count == 0)
        {
            parent.IsLeaf = true;
            return;
        }

        foreach (var childName in children)
        {
            if (!_result.AllProjects.TryGetValue(childName, out var childInfo))
            {
                continue;
            }
            DependencyNodeViewModel childNode;
            if (_index.TryGetValue(childName, out var existed))
            {
                childNode = existed;
            }
            else
            {
                childNode = AddNode(
                    new DependencyNodeViewModel
                    {
                        Name = childInfo.Name,
                        SourceRepo = childInfo.SourceRepo,
                        FilePath = childInfo.FilePath,
                        Depth = parent.Depth + 1,
                    }
                );
            }
            // 避免重复连线
            bool dup = false;
            foreach (var c in Connections)
            {
                if (ReferenceEquals(c.Source, parent) && ReferenceEquals(c.Target, childNode))
                {
                    dup = true;
                    break;
                }
            }
            if (!dup)
            {
                Connections.Add(
                    new DependencyConnectionViewModel { Source = parent, Target = childNode }
                );
            }
        }
    }

    /// <summary>
    /// 新增节点：分配位置（按列垂直堆叠），登记索引，加入 Nodes
    /// </summary>
    private DependencyNodeViewModel AddNode(DependencyNodeViewModel node)
    {
        var col = node.Depth;
        if (!_nextYPerColumn.TryGetValue(col, out var y))
        {
            y = 0;
        }
        node.Location = new Point(col * ColumnWidth, y);
        _nextYPerColumn[col] = y + RowHeight;
        _index[node.Name] = node;
        Nodes.Add(node);
        return node;
    }
}
