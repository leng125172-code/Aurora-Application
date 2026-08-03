namespace AuroraStruct3D.OpenCV.Workflow.Statements;

/// <summary>
/// 工作流 for 循环语句，对应 Halcon 中的 <c>for ... endfor</c>。
/// <para>
/// 翻译示例：
/// <code>
/// Halcon:
///   for i := 1 to 10 by 1
///       ... body ...
///   endfor
///
/// 工作流：
///   new ForLoopStatement("i", from: 1, to: 10, step: 1, body: [...])
/// </code>
/// </para>
/// <para>
/// 循环变量与循环体内的写入都作用在<b>当前作用域</b>（与 Halcon 一致）：
/// 循环体产生的变量（算子输出、累加器等）跨迭代保留，循环结束后在外层依然可见。
/// 循环结束后循环变量保留最后一次迭代的值。
/// </para>
/// </summary>
public sealed class ForLoopStatement : IWorkflowStatement
{
    /// <summary>循环变量名，对应 Halcon 的 <c>i</c>。</summary>
    public string VariableName { get; }

    /// <summary>循环起始值（含），对应 Halcon 的 <c>from</c>。</summary>
    public double From { get; }

    /// <summary>循环终止值（含），对应 Halcon 的 <c>to</c>。</summary>
    public double To { get; }

    /// <summary>循环步长，对应 Halcon 的 <c>by</c>，可为负数（倒序循环）。</summary>
    public double Step { get; }

    /// <summary>循环体语句列表，顺序执行。</summary>
    public IReadOnlyList<IWorkflowStatement> Body { get; }

    /// <param name="variableName">循环变量名。</param>
    /// <param name="from">起始值（含）。</param>
    /// <param name="to">终止值（含）。</param>
    /// <param name="step">步长，不可为 0。</param>
    /// <param name="body">循环体语句列表。</param>
    public ForLoopStatement(
        string variableName,
        double from,
        double to,
        double step,
        IEnumerable<IWorkflowStatement> body
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(variableName);
        ArgumentNullException.ThrowIfNull(body);

        if (step == 0)
            throw new ArgumentException("步长 step 不可为 0。", nameof(step));

        VariableName = variableName;
        From = from;
        To = to;
        Step = step;
        Body = body.ToList().AsReadOnly();
    }

    /// <summary>
    /// 执行 for 循环：在当前作用域内逐次将循环变量赋值并执行循环体。
    /// 循环体的写入保留在当前作用域，跨迭代可累加、循环后可见（与 Halcon 一致）。
    /// </summary>
    public void Execute(IWorkflowContext context)
    {
        // 判断循环方向（正序 or 倒序）
        bool isAscending = Step > 0;

        for (double i = From; isAscending ? i <= To : i >= To; i += Step)
        {
            context.Set(VariableName, i);

            foreach (IWorkflowStatement statement in Body)
            {
                statement.Execute(context);
            }
        }
    }

    public async Task ExecuteAsync(IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        if (Step == 0) throw new InvalidOperationException("ForLoop step cannot be zero.");
        for (double value = From; Step > 0 ? value <= To : value >= To; value += Step)
        {
            cancellationToken.ThrowIfCancellationRequested();
            context.Set(VariableName, value);
            foreach (IWorkflowStatement statement in Body)
                await statement.ExecuteAsync(context, cancellationToken);
        }
    }
}
