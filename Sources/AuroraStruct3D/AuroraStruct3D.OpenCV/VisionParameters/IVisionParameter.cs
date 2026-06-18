using System;
using System.Collections.Generic;
using System.Text;

namespace AuroraStruct3D.OpenCV.VisionParameters;

public interface IVisionParameter
{
    /// <summary>
    /// 参数名称，由工作流引擎或算子在绑定时赋值，可为 null（未绑定时）。
    /// </summary>
    string? ParameterName { get; }

    /// <summary>
    /// 参数的 CLR 类型全名，用于工作流引擎进行类型匹配校验。
    /// </summary>
    string ParameterType { get; }

    /// <summary>
    /// 参数当前值，可为 null（未赋值或输出图像为空时）。
    /// </summary>
    object? Value { get; }

    /// <summary>
    /// 参数默认值，无默认值时为 null。
    /// </summary>
    object? DefaultValue { get; }

    /// <summary>
    /// 值范围约束：
    /// 文件路径类型 → 允许的扩展名数组；
    /// 数值类型 → [Min, Max] 数组；
    /// 枚举类型 → 枚举值列表；
    /// 无约束时为 null。
    /// </summary>
    object? ValueLimit { get; }

    /// <summary>
    /// 是否在赋值时进行合法性校验。
    /// </summary>
    bool ErrorCheck { get; }
}
