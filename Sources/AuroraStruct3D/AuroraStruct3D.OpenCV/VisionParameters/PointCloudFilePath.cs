namespace AuroraStruct3D.OpenCV.VisionParameters;

/// <summary>
/// 点云文件路径参数，用于验证点云文件路径的合法性。
/// <para>
/// 支持的点云文件格式：
/// <list type="bullet">
///   <item><description>PLY - Polygon File Format</description></item>
///   <item><description>PCD - Point Cloud Data</description></item>
///   <item><description>XYZ - 简单文本格式</description></item>
///   <item><description>TXT - 文本格式</description></item>
///   <item><description>PTS - Leica点云格式</description></item>
/// </list>
/// </para>
/// </summary>
[DisplayName("点云文件路径")]
public class PointCloudFilePath : IVisionParameter
{
    /// <summary>参数变量名，工作流连线唯一标识符。</summary>
    public string? ParameterName { get; set; }

    /// <summary>端口 UI 显示名（实例级），为 null 时回退到类型上的 [DisplayName] 特性。</summary>
    public string? DisplayName { get; set; }

    /// <summary>参数实际数据类型：点云文件路径的值为 <see cref="string"/>。</summary>
    public Type ParameterType => typeof(string);

    private object? _value;

    public object? Value
    {
        get { return _value; }
        set
        {
            if (ErrorCheck)
            {
                CheckValue(value);
                _value = value;
            }
            else
            {
                _value = value;
            }
        }
    }

    public object? DefaultValue { get; set; } = null;

    public object? ValueLimit { get; set; } = null;

    public bool ErrorCheck { get; set; }

    public PortControlType ControlType => PortControlType.PointCloudUpload;

    public PointCloudFilePath(bool errorCheck)
    {
        ErrorCheck = errorCheck;
        ValueLimit = new string[] { ".ply", ".pcd", ".xyz", ".txt", ".pts", ".asc" };
    }

    private bool CheckValue(object? value)
    {
        if (value == null)
        {
            throw new ArgumentException($"参数 {ParameterName} 的值不能为空");
        }

        bool isString = value is string;
        if (!isString)
        {
            throw new ArgumentException($"参数 {ParameterName} 的值必须是字符串类型");
        }

        string filePath = value as string ?? string.Empty;
        if (string.IsNullOrEmpty(filePath))
        {
            throw new ArgumentException("文件路径不能为空");
        }

        if (!System.IO.File.Exists(filePath))
        {
            throw new ArgumentException($"文件路径 {filePath} 不存在");
        }

        string fileExtension = System.IO.Path.GetExtension(filePath).ToLower();
        string[] allowedExtensions = ValueLimit as string[] ?? Array.Empty<string>();
        if (!allowedExtensions.Contains(fileExtension))
        {
            throw new ArgumentException(
                $"文件类型 {fileExtension} 不受支持，允许的文件类型有：{string.Join(", ", allowedExtensions)}"
            );
        }

        // 文件路径合法且文件类型受支持
        return true;
    }
}
