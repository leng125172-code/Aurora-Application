namespace AuroraStruct3D.OpenCV.Workflow.Statements;

/// <summary>
/// 工作流条件分支语句，对应 Halcon 中的 <c>if ... else ... endif</c>。
/// <para>
/// 翻译示例：
/// <code>
/// Halcon:
///   if (Button != [])
///       RotX := fmod(Row, 360)
///       RotZ := fmod(Column, 360)
///   else
///       RotX := 75
///       RotZ := 45
///   endif
///
/// 工作流：
///   new IfElseStatement(
///       condition: ctx => ctx.Get("Button") != null,
///       thenBody:  [AssignStatement("RotX", ...), ...],
///       elseBody:  [AssignStatement("RotX", 75.0),  ...])
/// </code>
/// </para>
/// </summary>
public sealed class IfElseStatement : IWorkflowStatement
{
    /// <summary>
    /// 条件求值委托，接受当前上下文，返回 bool。
    /// 对应 Halcon <c>if (expression)</c> 的条件部分。
    /// </summary>
    public Func<IWorkflowContext, bool> Condition { get; }

    /// <summary>条件为 true 时执行的语句列表（Then 分支）。</summary>
    public IReadOnlyList<IWorkflowStatement> ThenBody { get; }

    /// <summary>
    /// 条件为 false 时执行的语句列表（Else 分支）。
    /// 无 else 分支时为空列表，不为 null。
    /// </summary>
    public IReadOnlyList<IWorkflowStatement> ElseBody { get; }

    /// <param name="condition">条件求值委托。</param>
    /// <param name="thenBody">Then 分支语句列表。</param>
    /// <param name="elseBody">Else 分支语句列表，可为 null（等同于空列表）。</param>
    public IfElseStatement(
        Func<IWorkflowContext, bool> condition,
        IEnumerable<IWorkflowStatement> thenBody,
        IEnumerable<IWorkflowStatement>? elseBody = null
    )
    {
        ArgumentNullException.ThrowIfNull(condition);
        ArgumentNullException.ThrowIfNull(thenBody);

        Condition = condition;
        ThenBody = thenBody.ToList().AsReadOnly();
        ElseBody =
            elseBody?.ToList().AsReadOnly() ?? Array.Empty<IWorkflowStatement>().AsReadOnly();
    }

    /// <summary>
    /// 求值条件并执行对应分支，Then/Else 均在当前作用域内执行（不创建子作用域）。
    /// 分支内赋值结果对父作用域可见，与 Halcon 行为一致。
    /// </summary>
    public void Execute(IWorkflowContext context)
    {
        bool result = Condition(context);

        IReadOnlyList<IWorkflowStatement> branch = result ? ThenBody : ElseBody;

        foreach (IWorkflowStatement statement in branch)
        {
            statement.Execute(context);
        }
    }
}
