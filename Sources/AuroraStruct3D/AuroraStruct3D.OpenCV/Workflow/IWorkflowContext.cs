namespace AuroraStruct3D.OpenCV.Workflow;

/// <summary>
/// 工作流运行时上下文接口，持有当前作用域的所有命名变量。
/// <para>
/// 对应 Halcon 中的变量概念：每个变量由字符串名称唯一标识，
/// 算子在 <see cref="IOperator.Execute"/> 中通过此接口读写变量：
/// <code>
/// Halcon:  read_image(Image, 'combine')
/// C# 算子: context.Set("Image", mat);           // 写输出
///          string path = context.Get&lt;string&gt;("img_path"); // 读输入
/// </code>
/// </para>
/// </summary>
public interface IWorkflowContext
{
    /// <summary>
    /// 将值写入指定名称的变量。若变量不存在则创建，已存在则覆盖。
    /// </summary>
    /// <param name="name">变量名，区分大小写。</param>
    /// <param name="value">变量值，可为 null。</param>
    void Set(string name, object? value);

    /// <summary>
    /// 读取指定名称的变量值（无类型）。
    /// </summary>
    /// <param name="name">变量名。</param>
    /// <returns>变量值，不存在时返回 null。</returns>
    object? Get(string name);

    /// <summary>
    /// 读取指定名称的变量值并强制转换为 <typeparamref name="T"/>。
    /// </summary>
    /// <typeparam name="T">目标类型。</typeparam>
    /// <param name="name">变量名。</param>
    /// <returns>
    /// 转换后的值；变量不存在或值为 null 时返回 <typeparamref name="T"/> 的默认值。
    /// </returns>
    T? Get<T>(string name);

    /// <summary>
    /// 判断指定名称的变量是否存在于当前作用域（含父作用域）。
    /// </summary>
    /// <param name="name">变量名。</param>
    /// <returns>存在返回 true，否则返回 false。</returns>
    bool Contains(string name);

    /// <summary>
    /// 删除指定名称的变量（仅在当前作用域有效，不影响父作用域）。
    /// </summary>
    /// <param name="name">变量名。</param>
    void Remove(string name);

    /// <summary>
    /// 获取当前作用域及所有父作用域中所有变量名的集合（去重）。
    /// </summary>
    IEnumerable<string> VariableNames { get; }

    /// <summary>
    /// 创建子作用域上下文，用于 <c>for</c> 循环等局部作用域场景。
    /// <para>
    /// 子作用域可读取父作用域变量（向上查找），
    /// 子作用域写入的变量不影响父作用域。
    /// </para>
    /// </summary>
    /// <returns>子作用域上下文实例。</returns>
    IWorkflowContext CreateChildScope();
}
