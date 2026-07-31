namespace AuroraStruct3D.OpenCV.Workflow.Values;

/// <summary>
/// 工作流变量值类型令牌（ValueType）的规范常量，用于 Redis 暂存的 <c>valueType</c> 字段
/// 与编解码器分发。与 VariableStage DTO 注释中约定的字符串一致。
/// </summary>
public static class WorkflowValueTypes
{
    /// <summary>字符串。</summary>
    public const string String = "string";

    /// <summary>OperatorFile Blob 键；文件子类型由键的扩展名确定。</summary>
    public const string Blob = "blob";

    /// <summary>32 位整数。</summary>
    public const string Int = "int";

    /// <summary>64 位整数。</summary>
    public const string Long = "long";

    /// <summary>双精度浮点。</summary>
    public const string Double = "double";

    /// <summary>单精度浮点。</summary>
    public const string Float = "float";

    /// <summary>高精度十进制数。</summary>
    public const string Decimal = "decimal";

    /// <summary>布尔。</summary>
    public const string Bool = "bool";

    /// <summary>JSON 对象。</summary>
    public const string Object = "object";

    /// <summary>JSON 数组。</summary>
    public const string Array = "array";

    /// <summary>日期时间，值使用 ISO 8601 字符串。</summary>
    public const string DateTime = "datetime";

    /// <summary>GUID，值使用标准字符串。</summary>
    public const string Guid = "guid";

    /// <summary>OpenCvSharp 图像矩阵（原始二进制）。</summary>
    public const string Mat = "Mat";

    /// <summary>点云数据（坐标 + 可选颜色，原始二进制）。</summary>
    public const string PointCloud = "PointCloudData";

    /// <summary>判断是否为标量类型（非 Mat / 点云）。</summary>
    public static bool IsScalar(string valueType) =>
        valueType
            is String
                or Blob
                or Int
                or Long
                or Float
                or Double
                or Decimal
                or Bool
                or Object
                or Array
                or DateTime
                or Guid;
}
