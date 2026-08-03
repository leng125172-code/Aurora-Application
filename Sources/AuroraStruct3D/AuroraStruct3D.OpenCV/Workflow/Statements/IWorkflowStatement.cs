namespace AuroraStruct3D.OpenCV.Workflow.Statements;

/// <summary>
/// 工作流语句接口，对应 Halcon 程序中的一条可执行语句。
/// <para>
/// 实现类型包括：
/// <list type="bullet">
///   <item><see cref="OperatorCallStatement"/>（算子调用，如 <c>read_image</c>）</item>
///   <item><see cref="ForLoopStatement"/>（for 循环，如 <c>for i := 1 to 10 by 1</c>）</item>
///   <item><see cref="IfElseStatement"/>（条件分支，如 <c>if (Button != [])</c>）</item>
///   <item><see cref="AssignStatement"/>（变量赋值，如 <c>RotX := 75</c>）</item>
/// </list>
/// </para>
/// </summary>
public interface IWorkflowStatement
{
    /// <summary>
    /// 在给定的运行时上下文中执行该语句。
    /// 语句执行时读写 <paramref name="context"/> 中的变量，
    /// 执行完毕后控制权返回调用方（<see cref="WorkflowExecutor"/>）。
    /// </summary>
    /// <param name="context">当前作用域的工作流运行时上下文。</param>
    void Execute(IWorkflowContext context);

    Task ExecuteAsync(IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        Execute(context);
        return Task.CompletedTask;
    }
}
