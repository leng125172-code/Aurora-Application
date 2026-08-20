namespace AuroraStruct3D.OpenCV.VisionParameters;

[DisplayName("图片路径")]
public class ImgFilePath : IVisionParameter
{
    /// <summary>参数变量名，工作流连线唯一标识符。</summary>
    public string? ParameterName { get; set; }

    /// <summary>端口 UI 显示名（实例级），为 null 时回退到类型上的 [DisplayName] 特性。</summary>
    public string? DisplayName { get; set; }

    public string? Description { get; set; }

    /// <summary>参数实际数据类型：图片路径的值为 <see cref="string"/>。</summary>
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
                _value = value;
        }
    }

    public object? DefaultValue { get; set; } = null;

    public object? ValueLimit { get; set; } = null;

    public bool ErrorCheck { get; set; }

    public PortControlType ControlType => PortControlType.ImageUpload;

    public ImgFilePath(bool errorCheck)
    {
        ErrorCheck = errorCheck;
        ValueLimit = new string[]
        {
            ".jpg",
            ".jpeg",
            ".png",
            ".jpe",
            ".bmp",
            ".tif",
            ".tiff",
            ".ppm",
            ".pgm",
            ".pbm",
            ".webp",
            ".sr",
            ".ras",
        };
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
        if (filePath == null)
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

        if (!System.IO.File.Exists(filePath))
        {
            throw new ArgumentException($"文件路径 {filePath} 不存在");
        }
        else
        {
            // 文件路径合法且文件类型受支持
            return true;
        }
    }
}
