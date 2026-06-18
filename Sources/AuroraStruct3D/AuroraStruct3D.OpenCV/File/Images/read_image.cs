namespace AuroraStruct3D.OpenCV.File.Images;

/// <summary>
/// 工作流节点：从文件系统读取图片，输出 <see cref="Mat"/> 图像矩阵。
/// <para>
/// 支持 OpenCV 所有标准图像格式（JPEG、PNG、BMP、TIFF 等）。
/// 读取模式可通过构造函数参数指定，默认以彩色模式（BGR）读取。
/// </para>
/// </summary>
[Category("文件操作")]
[SubCategory("图片")]
[DisplayName("读取图片")]
[Description("从文件系统读取图片，支持彩色、灰度等多种读取模式。输出 Mat 图像矩阵供后续节点使用。")]
public class read_image : IOperator
{
    /// <summary>
    /// 输入参数定义：图片文件路径。
    /// 每次访问返回新实例，供工作流引擎反射读取节点结构时使用。
    /// </summary>
    public static List<IVisionParameter>? InputVisionParameters =>
        new() { new ImgFilePath(errorCheck: true) { ParameterName = "图片路径" } };

    /// <summary>
    /// 输出参数定义：图像矩阵。
    /// 每次访问返回新实例，供工作流引擎反射读取节点结构时使用。
    /// </summary>
    public static List<IVisionParameter>? OutputVisionParameters =>
        new() { new MatImg() { ParameterName = "输出图像" } };

    private readonly ImgFilePath _imgFilePath;
    private readonly ImreadModes _readMode;
    private readonly MatImg _outputImg;
    private bool _disposed;

    /// <summary>
    /// 初始化读取图片算子。
    /// </summary>
    /// <param name="imgFilePath">图片路径参数，必须是 <see cref="ImgFilePath"/> 类型实例。</param>
    /// <param name="readMode">
    /// 图像读取模式，默认 <see cref="ImreadModes.Color"/>（BGR 彩色）。
    /// 传入 <see cref="ImreadModes.Grayscale"/> 可直接读取灰度图。
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="imgFilePath"/> 不是 <see cref="ImgFilePath"/> 类型时抛出。</exception>
    public read_image(IVisionParameter imgFilePath, ImreadModes readMode = ImreadModes.Color)
    {
        _imgFilePath =
            imgFilePath as ImgFilePath
            ?? throw new ArgumentException(
                $"imgFilePath 必须是 {nameof(ImgFilePath)} 类型，实际传入类型：{imgFilePath?.GetType().FullName ?? "null"}",
                nameof(imgFilePath)
            );
        _readMode = readMode;
        _outputImg = new MatImg { ParameterName = "输出图像" };
    }

    /// <summary>
    /// 执行读取图片操作。
    /// </summary>
    /// <returns>包含输出图像矩阵的参数列表，索引 0 为 <see cref="MatImg"/> 实例。</returns>
    /// <exception cref="ObjectDisposedException">算子已被释放时抛出。</exception>
    /// <exception cref="InvalidOperationException">
    /// 图片路径为空，或图片读取失败（文件损坏、格式不支持等）时抛出。
    /// </exception>
    public List<IVisionParameter> Execute()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        string filePath =
            _imgFilePath.Value as string
            ?? throw new InvalidOperationException(
                "图片路径不能为空，请在执行前通过 ImgFilePath.Value 设置有效的文件路径。"
            );

        // 释放上次执行产生的 Mat，防止内存泄漏
        _outputImg.DisposeMat();

        Mat mat = Cv2.ImRead(filePath, _readMode);

        if (mat.Empty())
        {
            mat.Dispose();
            throw new InvalidOperationException(
                $"图片读取失败：{filePath}。"
                    + $"文件可能已损坏、格式不受 OpenCV 支持，或文件路径包含不支持的字符。"
            );
        }

        _outputImg.Value = mat;
        return new List<IVisionParameter> { _outputImg };
    }

    /// <summary>
    /// 获取最近一次 <see cref="Execute"/> 输出的图像矩阵。
    /// 未执行或已释放时返回 <see langword="null"/>。
    /// </summary>
    public Mat? OutputMat => _outputImg.Mat;

    /// <summary>
    /// 释放算子持有的非托管资源（内部 <see cref="Mat"/> 图像内存）。
    /// 多次调用安全（幂等）。
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _outputImg.DisposeMat();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
