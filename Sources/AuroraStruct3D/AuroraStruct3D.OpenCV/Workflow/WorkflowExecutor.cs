using AuroraStruct3D.OpenCV.Workflow.Statements;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AuroraStruct3D.OpenCV.Workflow;

/// <summary>
/// 工作流执行引擎，逐条执行 <see cref="WorkflowDefinition"/> 中的语句，
/// 维护运行时上下文（<see cref="WorkflowContext"/>）。
/// <para>
/// 使用方式：
/// <code>
/// var executor = new WorkflowExecutor();
/// WorkflowContext ctx = executor.Execute(workflow);
/// Mat result = ctx.Get&lt;Mat&gt;("Image");
/// </code>
/// </para>
/// </summary>
public sealed class WorkflowExecutor
{
    private readonly ILogger<WorkflowExecutor> _logger;

    /// <param name="logger">可选日志记录器，不传入时使用 NullLogger。</param>
    public WorkflowExecutor(ILogger<WorkflowExecutor>? logger = null)
    {
        _logger = logger ?? NullLogger<WorkflowExecutor>.Instance;
    }

    /// <summary>
    /// 执行工作流，返回执行完毕后的根作用域上下文。
    /// 上下文包含工作流所有顶层变量，调用方可按变量名读取结果。
    /// </summary>
    /// <param name="workflow">要执行的工作流定义。</param>
    /// <param name="initialVariables">
    /// 初始变量字典，可预置常量或外部输入值。
    /// 例如可预置 <c>WindowHandle</c> 对应 Halcon 的窗口句柄。
    /// </param>
    /// <returns>执行完毕后的根上下文，包含所有顶层变量。</returns>
    /// <exception cref="WorkflowExecutionException">
    /// 任意语句执行失败时包装原始异常并抛出，携带失败语句类型和上下文快照信息。
    /// </exception>
    public WorkflowContext Execute(
        WorkflowDefinition workflow,
        IReadOnlyDictionary<string, object?>? initialVariables = null
    )
    {
        ArgumentNullException.ThrowIfNull(workflow);

        var context = new WorkflowContext();

        // 注入初始变量（如外部传入的路径、句柄等）
        if (initialVariables is not null)
        {
            foreach ((string name, object? value) in initialVariables)
            {
                context.Set(name, value);
            }
        }

        _logger.LogInformation("开始执行工作流：{WorkflowName}", workflow.Name);

        for (int i = 0; i < workflow.Statements.Count; i++)
        {
            IWorkflowStatement statement = workflow.Statements[i];
            _logger.LogDebug(
                "执行语句 [{Index}/{Total}]：{StatementType}",
                i + 1,
                workflow.Statements.Count,
                statement.GetType().Name
            );

            try
            {
                statement.Execute(context);
            }
            catch (Exception ex) when (ex is not WorkflowExecutionException)
            {
                string statementDesc = DescribeStatement(statement);
                _logger.LogError(
                    ex,
                    "工作流 {WorkflowName} 第 {Index} 条语句执行失败：{Statement}",
                    workflow.Name,
                    i + 1,
                    statementDesc
                );

                throw new WorkflowExecutionException(workflow.Name, i + 1, statementDesc, ex);
            }
        }

        _logger.LogInformation(
            "工作流 {WorkflowName} 执行完成，上下文变量数：{VarCount}",
            workflow.Name,
            context.VariableNames.Count()
        );

        return context;
    }

    private static string DescribeStatement(IWorkflowStatement statement) =>
        statement switch
        {
            OperatorCallStatement op => $"OperatorCall({op.OperatorType.Name})",
            ForLoopStatement loop =>
                $"ForLoop({loop.VariableName} := {loop.From} to {loop.To} by {loop.Step})",
            IfElseStatement => "IfElse",
            AssignStatement assign => $"Assign({assign.VariableName})",
            _ => statement.GetType().Name,
        };
}
