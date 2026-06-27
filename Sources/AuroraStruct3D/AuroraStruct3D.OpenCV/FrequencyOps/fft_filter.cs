namespace AuroraStruct3D.OpenCV.FrequencyOps;

/// <summary>
/// 工作流算子：傅里叶变换滤波。
/// <para>
/// 将图像转换到频域，应用理想滤波器（低通/高通/带通），再逆变换回空间域。
/// 低通滤波可去除高频噪声（平滑），高通滤波可增强边缘（锐化），
/// 带通滤波可提取特定频率的纹理特征。适用于周期性纹理缺陷检测。
/// </para>
/// <para>
/// 端口约定：
/// <list type="bullet">
///   <item>输入 <c>input_mat</c>（Mat）— 输入图像（建议为灰度图）</item>
///   <item>输出 <c>output_mat</c>（Mat）— 滤波后的图像（CV_8UC1）</item>
///   <item>输出 <c>spectrum_image</c>（Mat）— 频谱可视化图像（CV_8UC3）</item>
/// </list>
/// </para>
/// </summary>
[Guid("a1b2c3d4-0014-4000-8000-000000000027")]
[Category("2D预处理")]
[DisplayName("FFT滤波")]
[Description("到频域里滤掉周期性纹理或条纹噪声，普通滤波搞不定时用。")]
public class fft_filter : IOperator
{
    public static List<IVisionParameter>? InputVisionParameters =>
        new()
        {
            new MatImg() { ParameterName = "input_mat", DisplayName = "输入图像" },
        };

    public static List<IVisionParameter>? OutputVisionParameters =>
        new()
        {
            new MatImg() { ParameterName = "output_mat", DisplayName = "输出图像" },
            new MatImg() { ParameterName = "spectrum_image", DisplayName = "频谱图像" },
        };

    public static List<IConfigParameter>? ConfigParameters =>
        new()
        {
            new ConfigParameter
            {
                Name = "filterType",
                DisplayName = "滤波器类型",
                ParameterType = typeof(string),
                DefaultValue = "lowpass",
                ValueLimit = new[] { "lowpass", "highpass", "bandpass" },
                Required = false,
                ControlType = PortControlType.Select,
            },
            new ConfigParameter
            {
                Name = "cutoffLow",
                DisplayName = "低截止频率比例",
                ParameterType = typeof(double),
                DefaultValue = "0.1",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "cutoffHigh",
                DisplayName = "高截止频率比例",
                ParameterType = typeof(double),
                DefaultValue = "0.5",
                Required = false,
                ControlType = PortControlType.Input,
            },
        };

    private readonly string _filterType;
    private readonly double _cutoffLow;
    private readonly double _cutoffHigh;
    private bool _disposed;

    /// <summary>
    /// 初始化 FFT 频域滤波算子。
    /// </summary>
    /// <param name="filterType">滤波器类型：lowpass（低通）、highpass（高通）、bandpass（带通）。</param>
    /// <param name="cutoffLow">低截止频率比例（0~1），相对图像对角线的一半。</param>
    /// <param name="cutoffHigh">高截止频率比例（0~1），仅用于带通滤波器。</param>
    public fft_filter(
        string filterType = "lowpass",
        double cutoffLow = 0.1,
        double cutoffHigh = 0.5
    )
    {
        _filterType = filterType;
        _cutoffLow = cutoffLow;
        _cutoffHigh = cutoffHigh;
    }

    /// <inheritdoc/>
    public void Execute(IWorkflowContext context)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        Mat inputMat =
            context.Get<Mat>("input_mat")
            ?? throw new InvalidOperationException(
                "上下文变量 'input_mat' 为空，请确认输入绑定已正确设置。"
            );

        if (inputMat.Empty())
            throw new InvalidOperationException("输入矩阵为空，无法执行 FFT 滤波。");

        // 转为灰度图
        Mat gray;
        bool needDisposeGray = false;
        if (inputMat.Channels() > 1)
        {
            gray = new Mat();
            Cv2.CvtColor(inputMat, gray, ColorConversionCodes.BGR2GRAY);
            needDisposeGray = true;
        }
        else
        {
            gray = inputMat;
        }

        try
        {
            // ① 转为 float32 并扩展到复数
            using Mat floatImg = new Mat();
            gray.ConvertTo(floatImg, MatType.CV_32FC1);

            Mat[] planes = { floatImg, Mat.Zeros(gray.Rows, gray.Cols, MatType.CV_32FC1) };
            using Mat complexImg = new Mat();
            Cv2.Merge(planes, complexImg);

            // ② DFT
            using Mat dftResult = new Mat();
            Cv2.Dft(complexImg, dftResult);

            // ③ 中心化频谱
            ShiftQuadrants(dftResult);

            // ④ 生成频谱可视化（在滤波前）
            Mat spectrumImage = BuildSpectrumImage(dftResult);

            // ⑤ 创建滤波器掩膜
            using Mat mask = CreateFilterMask(gray.Cols, gray.Rows);

            // ⑥ 应用掩膜
            Cv2.Multiply(dftResult, mask, dftResult);

            // ⑦ 逆中心化
            ShiftQuadrants(dftResult);

            // ⑧ 逆 DFT
            using Mat inverseDft = new Mat();
            Cv2.Dft(dftResult, inverseDft, DftFlags.Inverse | DftFlags.Scale);

            // ⑨ 提取实部，归一化到 0-255
            using Mat restored = new Mat();
            Cv2.ExtractChannel(inverseDft, restored, 0);
            Cv2.Normalize(restored, restored, 0, 255, NormTypes.MinMax);
            restored.ConvertTo(restored, MatType.CV_8UC1);

            context.Set("output_mat", restored.Clone());
            context.Set("spectrum_image", spectrumImage);
        }
        finally
        {
            if (needDisposeGray)
                gray.Dispose();
        }
    }

    /// <summary>
    /// 交换频谱的四个象限，将低频移到中心。
    /// </summary>
    private static void ShiftQuadrants(Mat dft)
    {
        int cx = dft.Cols / 2;
        int cy = dft.Rows / 2;

        using Mat q0 = new Mat(dft, new Rect(0, 0, cx, cy));
        using Mat q1 = new Mat(dft, new Rect(cx, 0, cx, cy));
        using Mat q2 = new Mat(dft, new Rect(0, cy, cx, cy));
        using Mat q3 = new Mat(dft, new Rect(cx, cy, cx, cy));

        using Mat tmp = new Mat();
        q0.CopyTo(tmp);
        q3.CopyTo(q0);
        tmp.CopyTo(q3);

        q1.CopyTo(tmp);
        q2.CopyTo(q1);
        tmp.CopyTo(q2);
    }

    /// <summary>
    /// 创建理想滤波器掩膜（频域）。
    /// </summary>
    private Mat CreateFilterMask(int cols, int rows)
    {
        Mat mask = new Mat(rows, cols, MatType.CV_32FC2, Scalar.All(0));
        int cx = cols / 2;
        int cy = rows / 2;
        double maxRadius = Math.Sqrt(cx * cx + cy * cy);
        double rLow = _cutoffLow * maxRadius;
        double rHigh = _cutoffHigh * maxRadius;

        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < cols; x++)
            {
                double dx = x - cx;
                double dy = y - cy;
                double r = Math.Sqrt(dx * dx + dy * dy);
                bool pass = _filterType switch
                {
                    "highpass" => r >= rLow,
                    "bandpass" => r >= rLow && r <= rHigh,
                    _ => r <= rLow, // lowpass
                };
                if (pass)
                    mask.Set(y, x, new Vec2f(1, 0));
            }
        }

        return mask;
    }

    /// <summary>
    /// 构建频谱可视化图像。
    /// </summary>
    private static Mat BuildSpectrumImage(Mat dft)
    {
        Mat[] planes = Cv2.Split(dft);

        using Mat magnitude = new Mat();
        Cv2.Magnitude(planes[0], planes[1], magnitude);

        // log 缩放
        Cv2.Add(magnitude, Scalar.All(1), magnitude);
        Cv2.Log(magnitude, magnitude);

        Cv2.Normalize(magnitude, magnitude, 0, 255, NormTypes.MinMax);
        magnitude.ConvertTo(magnitude, MatType.CV_8UC1);

        Mat colorMap = new Mat();
        Cv2.ApplyColorMap(magnitude, colorMap, ColormapTypes.Jet);

        return colorMap;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
