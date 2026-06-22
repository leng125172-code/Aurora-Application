using AuroraStruct3D.OpenCV.Registry;
using AuroraStruct3D.Workflow.Dtos;

namespace AuroraStruct3D.Workflow;

/// <summary>
/// 工作流节点面板应用服务。
/// 合并内置结构节点（ForLoop、IfElse、Assign）和算子注册表中的算子节点，
/// 返回按 Category / DisplayName 排序的完整节点面板数据。
/// ABP 自动生成 REST 端点：GET /api/app/workflow-node-palette
/// </summary>
public class WorkflowNodePaletteAppService
    : AuroraStruct3DAppService,
        IWorkflowNodePaletteAppService
{
    private readonly IOperatorRegistry _registry;

    public WorkflowNodePaletteAppService(IOperatorRegistry registry)
    {
        _registry = registry;
    }

    /// <inheritdoc/>
    public async Task<NodePaletteDto> GetAsync()
    {
        // ① 内置结构节点（静态，顺序固定在前）
        List<NodeCategoryDto> categories = BuildBuiltinCategories();

        // ② 算子节点（来自注册表，按 Category 分组）
        IReadOnlyList<OperatorDescriptor> operators = await _registry.GetAllOperatorsAsync(
            CancellationToken.None
        );

        List<NodeCategoryDto> operatorCategories = await BuildOperatorCategoriesAsync(operators);

        categories.AddRange(operatorCategories);

        return new NodePaletteDto { Categories = categories.AsReadOnly() };
    }

    // ── 内置节点构建 ──────────────────────────────────────────────────────────

    private static List<NodeCategoryDto> BuildBuiltinCategories()
    {
        return
        [
            new NodeCategoryDto
            {
                Name = "流程控制",
                Nodes = new List<NodeDefinitionDto>
                {
                    BuildForLoopNode(),
                    BuildIfElseNode(),
                }.AsReadOnly(),
            },
            new NodeCategoryDto
            {
                Name = "参数赋值",
                Nodes = new List<NodeDefinitionDto> { BuildAssignNode() }.AsReadOnly(),
            },
        ];
    }

    private static NodeDefinitionDto BuildForLoopNode() =>
        new()
        {
            Id = "builtin::for_loop",
            NodeType = "ForLoop",
            DisplayName = "For 循环",
            Description = "在指定数值范围内按步长迭代，对应 Halcon for ... endfor。",
            HasBody = true,
            InputPorts = Array.Empty<NodePortDto>(),
            OutputPorts = Array.Empty<NodePortDto>(),
            ConfigFields = new List<NodeConfigFieldDto>
            {
                new()
                {
                    Name = "variableName",
                    DisplayName = "循环变量名",
                    ValueTypeName = "System.String",
                    DefaultValue = "i",
                    Required = true,
                },
                new()
                {
                    Name = "from",
                    DisplayName = "起始值",
                    ValueTypeName = "System.Double",
                    DefaultValue = 1.0,
                    Required = true,
                },
                new()
                {
                    Name = "to",
                    DisplayName = "终止值",
                    ValueTypeName = "System.Double",
                    DefaultValue = 10.0,
                    Required = true,
                },
                new()
                {
                    Name = "step",
                    DisplayName = "步长",
                    ValueTypeName = "System.Double",
                    DefaultValue = 1.0,
                    Required = true,
                },
            }.AsReadOnly(),
        };

    private static NodeDefinitionDto BuildIfElseNode() =>
        new()
        {
            Id = "builtin::if_else",
            NodeType = "IfElse",
            DisplayName = "条件分支",
            Description = "根据条件执行不同分支，对应 Halcon if ... else ... endif。",
            HasBody = true,
            InputPorts = Array.Empty<NodePortDto>(),
            OutputPorts = Array.Empty<NodePortDto>(),
            ConfigFields = Array.Empty<NodeConfigFieldDto>(),
        };

    private static NodeDefinitionDto BuildAssignNode() =>
        new()
        {
            Id = "builtin::assign",
            NodeType = "Assign",
            DisplayName = "变量赋值",
            Description = "将值赋给工作流变量，对应 Halcon 赋值语句 :=。",
            HasBody = false,
            InputPorts = Array.Empty<NodePortDto>(),
            OutputPorts = Array.Empty<NodePortDto>(),
            ConfigFields = new List<NodeConfigFieldDto>
            {
                new()
                {
                    Name = "variableName",
                    DisplayName = "目标变量名",
                    ValueTypeName = "System.String",
                    Required = true,
                },
            }.AsReadOnly(),
        };

    // ── 算子节点构建 ──────────────────────────────────────────────────────────

    private async Task<List<NodeCategoryDto>> BuildOperatorCategoriesAsync(
        IReadOnlyList<OperatorDescriptor> operators
    )
    {
        var result = new List<NodeCategoryDto>();

        var byCategory = operators
            .GroupBy(op => op.Category, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

        foreach (var catGroup in byCategory)
        {
            var nodes = new List<NodeDefinitionDto>();

            foreach (
                OperatorDescriptor op in catGroup.OrderBy(
                    o => o.DisplayName,
                    StringComparer.OrdinalIgnoreCase
                )
            )
            {
                OperatorParametersDescriptor? ports = await _registry.GetParametersAsync(op.Id);
                nodes.Add(BuildOperatorNode(op, ports));
            }

            result.Add(new NodeCategoryDto { Name = catGroup.Key, Nodes = nodes.AsReadOnly() });
        }

        return result;
    }

    private static NodeDefinitionDto BuildOperatorNode(
        OperatorDescriptor op,
        OperatorParametersDescriptor? ports
    ) =>
        new()
        {
            Id = op.Id.ToString(),
            NodeType = "Operator",
            DisplayName = op.DisplayName,
            Description = op.Description,
            HasBody = false,
            InputPorts =
                ports?.Inputs.Select(MapPort).ToList().AsReadOnly()
                ?? (IReadOnlyList<NodePortDto>)Array.Empty<NodePortDto>(),
            OutputPorts =
                ports?.Outputs.Select(MapPort).ToList().AsReadOnly()
                ?? (IReadOnlyList<NodePortDto>)Array.Empty<NodePortDto>(),
            ConfigFields =
                ports?.Config.Select(MapConfig).ToList().AsReadOnly()
                ?? (IReadOnlyList<NodeConfigFieldDto>)Array.Empty<NodeConfigFieldDto>(),
        };

    private static NodePortDto MapPort(ParameterDescriptor p) =>
        new()
        {
            Name = p.ParameterName,
            DisplayName = p.DisplayName,
            PortTypeName = p.ParameterTypeName,
            DefaultValue = p.DefaultValue,
            ValueLimit = p.ValueLimit,
            ErrorCheck = p.ErrorCheck,
            ControlType = p.ControlType,
            MatType = p.MatType,
        };

    private static NodeConfigFieldDto MapConfig(ConfigParameterDescriptor c) =>
        new()
        {
            Name = c.Name,
            DisplayName = c.DisplayName,
            ValueTypeName = c.ParameterTypeName,
            DefaultValue = c.DefaultValue,
            ValueLimit = c.ValueLimit,
            Required = c.Required,
            ControlType = c.ControlType,
        };
}
