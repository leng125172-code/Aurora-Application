using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;

namespace AuroraStruct3D.Workflow;

/// <summary>
/// 工作流定义聚合根。
/// 由前端工作流编辑器离线编辑完成后整体提交，
/// <see cref="GraphData"/> 保存前端 payload 中的 <c>graphData</c> 属性（即 graphData 画布数据），
/// <see cref="ProjectId"/> 和 <see cref="Name"/> 单独落库，避免冗余嵌套。
/// 审计信息（创建人/时间、修改人/时间）由 FullAuditedAggregateRoot 自动记录。
/// <para>数据库表：AbpProWorkflowDefinitions</para>
/// </summary>
public class WorkflowDefinition : FullAuditedAggregateRoot<Guid>
{
    /// <summary>所属项目唯一标识。</summary>
    public Guid ProjectId { get; private set; }

    /// <summary>工作流名称。</summary>
    public string Name { get; private set; } = null!;

    /// <summary>工作流画布数据 JSON（graphData），按原文存储。</summary>
    public string GraphData { get; private set; } = null!;

    /// <summary>受限 C# 风格工作流脚本；运行与调试的权威源。</summary>
    public string? SourceCode { get; private set; }

    /// <summary>规范化脚本 SHA-256。</summary>
    public string? SourceHash { get; private set; }

    /// <summary>忽略注释和格式后的语义哈希。</summary>
    public string? SemanticHash { get; private set; }

    /// <summary>编译程序缓存键。</summary>
    public string? ProgramHash { get; private set; }

    /// <summary>工作流所绑定的算子契约哈希。</summary>
    public string? OperatorContractHash { get; private set; }

    /// <summary>脚本语言版本。</summary>
    public int LanguageVersion { get; private set; } = 1;

    /// <summary>脚本修订号，用于乐观并发编辑。</summary>
    public int SourceRevision { get; private set; }

    /// <summary>输出变量配置 JSON（变量名列表），用于运行时从所有变量中提取目标变量返回给前端。</summary>
    public string? OutputVariables { get; private set; }

    /// <summary>EF Core 所需的无参构造函数（不得直接使用）。</summary>
    protected WorkflowDefinition() { }

    /// <summary>
    /// 工厂方法：新建一个工作流定义。
    /// </summary>
    /// <param name="id">工作流唯一标识</param>
    /// <param name="projectId">所属项目 ID</param>
    /// <param name="name">工作流名称</param>
    /// <param name="graphData">工作流画布数据 JSON（graphData）</param>
    public static WorkflowDefinition Create(Guid id, Guid projectId, string name, string graphData)
    {
        Check.NotNullOrWhiteSpace(name, nameof(name), WorkflowDefinitionConsts.MaxNameLength);
        Check.NotNullOrWhiteSpace(graphData, nameof(graphData));

        return new WorkflowDefinition
        {
            Id = id,
            ProjectId = projectId,
            Name = name,
            GraphData = graphData,
        };
    }

    /// <summary>
    /// 修改保存：更新工作流名称与画布数据。
    /// </summary>
    /// <param name="name">工作流名称</param>
    /// <param name="graphData">工作流画布数据 JSON（graphData）</param>
    public void Update(string name, string graphData)
    {
        Check.NotNullOrWhiteSpace(name, nameof(name), WorkflowDefinitionConsts.MaxNameLength);
        Check.NotNullOrWhiteSpace(graphData, nameof(graphData));

        Name = name;
        GraphData = graphData;
    }

    /// <summary>
    /// 更新输出变量配置。
    /// </summary>
    /// <param name="outputVariablesJson">输出变量名列表的 JSON 字符串。</param>
    public void UpdateOutputVariables(string? outputVariablesJson)
    {
        OutputVariables = outputVariablesJson;
    }

    /// <summary>原子更新脚本源、派生画布与编译标识。</summary>
    public void UpdateSource(
        string name,
        string sourceCode,
        string graphData,
        string sourceHash,
        string programHash,
        int languageVersion,
        string? semanticHash = null,
        string? operatorContractHash = null
    )
    {
        Check.NotNullOrWhiteSpace(name, nameof(name), WorkflowDefinitionConsts.MaxNameLength);
        Check.NotNullOrWhiteSpace(sourceCode, nameof(sourceCode));
        Check.NotNullOrWhiteSpace(graphData, nameof(graphData));
        Check.NotNullOrWhiteSpace(sourceHash, nameof(sourceHash));
        Check.NotNullOrWhiteSpace(programHash, nameof(programHash));
        Name = name;
        SourceCode = sourceCode;
        GraphData = graphData;
        SourceHash = sourceHash;
        SemanticHash = semanticHash;
        ProgramHash = programHash;
        OperatorContractHash = operatorContractHash;
        LanguageVersion = languageVersion;
        SourceRevision++;
    }
}
