namespace AuroraStruct3D.OpenCV.File.Images;

/// <summary>
/// 工作流算子：从文件系统读取图片，输出 <see cref="Mat"/> 图像矩阵。
/// <para>
/// 对应 Halcon 算子：<c>read_image(Image, FileName)</c>
/// </para>
/// <para>
/// 端口约定：
/// <list type="bullet">
///   <item>输入 <c>img_path</c>（string）— 图片文件路径，对应 Halcon 的 <c>FileName</c></item>
///   <item>输出 <c>output_mat</c>（Mat）— 读取结果，对应 Halcon 的 <c>Image</c></item>
/// </list>
/// </para>
/// <para>
/// 工作流翻译示例：
/// <code>
/// Halcon:  read_image(Image, 'combine')
///
/// 工作流:  new OperatorCallStatement(
///              typeof(read_image),
///              ctorArgs: [ImreadModes.Color],          // 算法配置（可选）
///              inputs:   { "img_path"    → Constant("combine") },
///              outputs:  { "output_mat"  → Variable("Image")   })
/// </code>
/// </para>
/// </summary>
[Guid("7544f3f3-040d-4571-b0f2-741c8f17ab41")]
[Category("文件操作")]
[DisplayName("读取图片")]
[Description("从文件系统读取图片，支持彩色、灰度等多种读取模式。输出 Mat 图像矩阵供后续节点使用。")]
public class read_image : IOperator
{
    /// <summary>
    /// 输入端口定义（工作流引擎反射用）。
    /// <c>img_path</c> 对应 Halcon 的 <c>FileName</c> 参数。
    /// </summary>
    public static List<IVisionParameter>? InputVisionParameters =>
        new() { new ImgFilePath(errorCheck: true) { ParameterName = "img_path" } };

    /// <summary>
    /// 输出端口定义（工作流引擎反射用）。
    /// <c>output_mat</c> 对应 Halcon 的 <c>Image</c> 输出变量。
    /// </summary>
    public static List<IVisionParameter>? OutputVisionParameters =>
        new() { new MatImg() { ParameterName = "output_mat" } };

    /// <summary>
    /// 构造函数配置参数定义（工作流引擎反射用）。
    /// <c>readMode</c> 对应 OpenCV 的图像读取模式。
    /// </summary>
    public static List<IConfigParameter>? ConfigParameters =>
        new()
        {
            ConfigParameter.ForEnum<ImreadModes>(
                name: "readMode",
                displayName: "读取模式",
                defaultValue: ImreadModes.Color,
                required: false
            ),
        };

    // 算法配置参数（仅在构造时绑定，不随工作流变量变化）
    private readonly ImreadModes _readMode;
    private bool _disposed;

    /// <summary>
    /// 初始化读取图片算子。
    /// </summary>
    /// <param name="readMode">
    /// 图像读取模式，默认 <see cref="ImreadModes.Color"/>（BGR 彩色）。
    /// 传入 <see cref="ImreadModes.Grayscale"/> 可直接读取灰度图。
    /// 对应 Halcon 中 <c>read_image</c> 通过后缀区分的读取行为。
    /// </param>
    public read_image(ImreadModes readMode = ImreadModes.Color)
    {
        _readMode = readMode;
    }

    /// <summary>
    /// 执行读取图片操作。
    /// 从 <paramref name="context"/> 读取 <c>img_path</c> 变量，
    /// 读取完成后将 Mat 写入 <c>output_mat</c> 变量。
    /// </summary>
    /// <param name="context">工作流运行时上下文。</param>
    /// <exception cref="ObjectDisposedException">算子已被释放时抛出。</exception>
    /// <exception cref="InvalidOperationException">
    /// img_path 变量为空，或图片读取失败时抛出。
    /// </exception>
    public void Execute(IWorkflowContext context)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // 从上下文读取输入变量（对应 Halcon FileName 参数）
        string filePath =
            context.Get<string>("img_path")
            ?? throw new InvalidOperationException(
                "上下文变量 'img_path' 为空，请确认 OperatorCallStatement 的输入绑定已正确设置。"
            );

        Mat mat = Cv2.ImRead(filePath, _readMode);

        if (mat.Empty())
        {
            mat.Dispose();
            throw new InvalidOperationException(
                $"图片读取失败：{filePath}。"
                    + "文件可能已损坏、格式不受 OpenCV 支持，或文件路径包含不支持的字符。"
            );
        }

        // 将结果写入上下文输出变量（对应 Halcon Image 输出）
        // OperatorCallStatement 会依据 OutputBinding 将 "output_mat" 重命名为工作流变量名
        context.Set("output_mat", mat);
    }

    /// <summary>
    /// 释放算子持有的资源。
    /// 注意：Mat 由 OperatorCallStatement 通过上下文管理，
    /// 此处只重置 disposed 标志，不 Dispose 上下文中的 Mat。
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
