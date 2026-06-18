using System;
using System.Collections.Generic;
using System.Text;

namespace AuroraStruct3D.OpenCV;

public interface IOperator : IDisposable
{
    /// <summary>
    /// 输入参数定义列表，描述该算子节点所需的输入参数及其类型。
    /// 由工作流引擎反射读取，用于构建节点连接 UI。
    /// </summary>
    static List<IVisionParameter>? InputVisionParameters { get; }

    /// <summary>
    /// 输出参数定义列表，描述该算子节点产生的输出参数及其类型。
    /// 由工作流引擎反射读取，用于构建节点连接 UI。
    /// </summary>
    static List<IVisionParameter>? OutputVisionParameters { get; }

    /// <summary>
    /// 执行算子逻辑。
    /// </summary>
    /// <returns>
    /// 输出参数列表，顺序与 <see cref="OutputVisionParameters"/> 定义一致，
    /// 调用方可依据索引或参数名取得运行时结果。
    /// </returns>
    /// <exception cref="ObjectDisposedException">算子已被释放时抛出。</exception>
    /// <exception cref="InvalidOperationException">输入参数非法或执行失败时抛出。</exception>
    List<IVisionParameter> Execute();
}
