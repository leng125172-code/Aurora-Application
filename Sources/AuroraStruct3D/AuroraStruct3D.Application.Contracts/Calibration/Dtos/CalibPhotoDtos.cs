using System.ComponentModel.DataAnnotations;

namespace AuroraStruct3D.Calibration.Dtos;

/// <summary>
/// 更新标定板参数输入 DTO
/// </summary>
public class UpdateBoardConfigInput
{
    /// <summary>标定项目 ID</summary>
    [Required]
    public Guid CalibProjectId { get; set; }

    /// <summary>标定板类型（默认棋盘格）。</summary>
    public CalibrationBoardType BoardType { get; set; } = CalibrationBoardType.Chessboard;

    /// <summary>实体棋盘格内角点行数（≥ 2）</summary>
    [Range(2, 100)]
    public int PhysicalCornerRows { get; set; } = 9;

    /// <summary>实体棋盘格内角点列数（≥ 2）</summary>
    [Range(2, 100)]
    public int PhysicalCornerCols { get; set; } = 6;

    /// <summary>实体棋盘格单个方格物理边长（mm，> 0）</summary>
    [Range(0.1, 1000.0)]
    public decimal PhysicalSquareSizeMm { get; set; } = 30m;

    /// <summary>投影棋盘格内角点行数（≥ 2）</summary>
    [Range(2, 100)]
    public int ProjectedCornerRows { get; set; } = 9;

    /// <summary>投影棋盘格内角点列数（≥ 2）</summary>
    [Range(2, 100)]
    public int ProjectedCornerCols { get; set; } = 6;

    /// <summary>投影棋盘格单个方格像素尺寸（px，≥ 1）</summary>
    [Range(1, 4096)]
    public int ProjectedPixelSize { get; set; } = 20;

    /// <summary>标定板厚度（mm，用于投影仪外参标定补偿，默认 1mm）</summary>
    [Range(0, 100)]
    public decimal BoardThicknessMm { get; set; } = 1m;

    /// <summary>圆点标定板配置（圆点类型必填）。</summary>
    public CircleBoardConfigDto? CircleBoardConfig { get; set; }
}

/// <summary>
/// 圆点标定板配置。
/// </summary>
public class CircleBoardConfigDto
{
    /// <summary>行列数（Width=列数，Height=行数）。</summary>
    public CirclePatternSizeDto PatternSize { get; set; } = new();

    /// <summary>圆点中心间距（mm）。</summary>
    public decimal CircleSpacing { get; set; } = 10m;

    /// <summary>圆点直径（mm，可选）。</summary>
    public decimal? CircleDiameter { get; set; }

    /// <summary>是否存在中心标记（缺孔）。</summary>
    public bool HasCenterMarker { get; set; }

    /// <summary>是否启用四角定位点（元数据，仅用于配置记录与导入导出）。</summary>
    public bool HasCornerLocators { get; set; }

    /// <summary>标记点行列坐标（默认 27x27 的中心 [13,13]）。</summary>
    public CircleMarkerPositionDto MarkerPosition { get; set; } = new() { Row = 13, Col = 13 };

    /// <summary>SimpleBlobDetector 参数（支持覆盖默认模板）。</summary>
    public CircleBlobDetectorConfigDto Detector { get; set; } =
        CircleBlobDetectorConfigDto.CreateDefault();
}

/// <summary>
/// 圆点网格尺寸。
/// </summary>
public class CirclePatternSizeDto
{
    /// <summary>列数。</summary>
    public int Width { get; set; } = 27;

    /// <summary>行数。</summary>
    public int Height { get; set; } = 27;
}

/// <summary>
/// 圆点标记坐标。
/// </summary>
public class CircleMarkerPositionDto
{
    /// <summary>行索引（从 0 开始）。</summary>
    public int Row { get; set; }

    /// <summary>列索引（从 0 开始）。</summary>
    public int Col { get; set; }
}

/// <summary>
/// 圆点检测器参数（SimpleBlobDetector）。
/// </summary>
public class CircleBlobDetectorConfigDto
{
    /// <summary>最小阈值。</summary>
    public double MinThreshold { get; set; } = 10;

    /// <summary>最大阈值。</summary>
    public double MaxThreshold { get; set; } = 220;

    /// <summary>最小面积（像素）。</summary>
    public double MinArea { get; set; } = 25;

    /// <summary>最大面积（像素）。</summary>
    public double MaxArea { get; set; } = 10000;

    /// <summary>最小圆度（0~1）。</summary>
    public double MinCircularity { get; set; } = 0.6;

    /// <summary>最小凸度（0~1）。</summary>
    public double MinConvexity { get; set; } = 0.8;

    /// <summary>创建默认参数。</summary>
    public static CircleBlobDetectorConfigDto CreateDefault()
    {
        return new CircleBlobDetectorConfigDto();
    }
}

/// <summary>
/// 内参拍照输入 DTO
/// </summary>
public class TakeIntrinsicPhotoInput
{
    /// <summary>标定项目ID</summary>
    [Required]
    public Guid CalibProjectId { get; set; }

    /// <summary>相机设备ID</summary>
    [Required]
    public Guid CameraDeviceId { get; set; }

    /// <summary>投影仪设备 ID（可选）——传入时后端将先自动关闭 LED，确保内参拍照不受投影光干扰</summary>
    public Guid? ProjectorDeviceId { get; set; }
}

/// <summary>
/// 外参拍照输入 DTO
/// </summary>
public class TakeExtrinsicPhotoInput
{
    /// <summary>标定项目ID</summary>
    [Required]
    public Guid CalibProjectId { get; set; }

    /// <summary>相机设备ID</summary>
    [Required]
    public Guid CameraDeviceId { get; set; }

    /// <summary>
    /// 条纹采集总帧数（T + N 的图片数）。
    /// </summary>
    [Range(2, 128)]
    public int StripeImageCount { get; set; } = 2;
}

/// <summary>
/// 投影外参双拍阶段。
/// </summary>
public enum ExtrinsicPhotoPhaseDto
{
    /// <summary>关灯拍实体标定板。</summary>
    ProjectorOff = 0,

    /// <summary>开灯拍投影标定图案。</summary>
    ProjectorOn = 1,

    /// <summary>S1 白屏模式拍摄圆点标定板。</summary>
    WhiteScreen = 2,

    /// <summary>S3 棋盘格模式拍摄投影棋盘格。</summary>
    Checkerboard = 3,
}

/// <summary>
/// 双目联合外参成对拍照输入 DTO
/// </summary>
public class TakeStereoExtrinsicPairPhotoInput
{
    /// <summary>标定项目ID</summary>
    [Required]
    public Guid CalibProjectId { get; set; }
}

/// <summary>
/// 标定照片列表项 DTO
/// </summary>
public class CalibPhotoDto
{
    /// <summary>照片记录ID</summary>
    public Guid Id { get; set; }

    /// <summary>照片类型（0=内参，1=外参）</summary>
    public CalibPhotoType PhotoType { get; set; }

    /// <summary>是否有效（成功检测到棋盘格角点）</summary>
    public bool IsValid { get; set; }

    /// <summary>检测到的角点数量</summary>
    public int CornerCountDetected { get; set; }

    /// <summary>拍摄时间</summary>
    public DateTime CapturedAt { get; set; }

    /// <summary>缩略图 Base64（JPEG，约 400px 宽）；前端显示用，可能为 null</summary>
    public string? ThumbnailBase64 { get; set; }

    /// <summary>双目成对拍照分组ID（仅 StereoExtrinsicPair 有值）</summary>
    public Guid? PairGroupId { get; set; }

    /// <summary>双目成对拍照角色（仅 StereoExtrinsicPair 有值）</summary>
    public StereoPhotoRole? StereoRole { get; set; }

    /// <summary>投影外参双拍阶段（仅 Extrinsic 有值）。</summary>
    public ExtrinsicPhotoPhaseDto? ExtrinsicPhase { get; set; }

    /// <summary>图像差分分数（0-1，与同组另一张照片的差异程度）</summary>
    public double? ImageDiffScore { get; set; }

    /// <summary>图像差分是否显著（两次拍摄有明显差异）</summary>
    public bool? ImageDiffSignificant { get; set; }
}

/// <summary>
/// 双目成对拍照返回 DTO
/// </summary>
public class CalibStereoPairPhotoDto
{
    /// <summary>成对拍照分组ID</summary>
    public Guid PairGroupId { get; set; }

    /// <summary>主相机照片记录</summary>
    public CalibPhotoDto MainPhoto { get; set; } = null!;

    /// <summary>从相机照片记录</summary>
    public CalibPhotoDto SecondaryPhoto { get; set; } = null!;
}

public class CalibComputeResultDto
{
    /// <summary>内参矩阵（3×3，JSON 序列化的 double[][] 数组）</summary>
    public string IntrinsicMatrixJson { get; set; } = string.Empty;

    /// <summary>畸变系数（JSON 序列化的 double[] 数组）</summary>
    public string DistCoeffsJson { get; set; } = string.Empty;

    /// <summary>重投影误差（像素）</summary>
    public double ReprojectionError { get; set; }

    /// <summary>外参旋转向量（JSON，无投影仪时为 null）</summary>
    public string? ExtrinsicRvecJson { get; set; }

    /// <summary>外参平移向量（JSON，无投影仪时为 null）</summary>
    public string? ExtrinsicTvecJson { get; set; }
}

/// <summary>
/// 双目联合标定结果 DTO
/// </summary>
public class CalibStereoComputeResultDto
{
    /// <summary>主相机设备ID</summary>
    public Guid MainCameraDeviceId { get; set; }

    /// <summary>从相机设备ID</summary>
    public Guid SecondaryCameraDeviceId { get; set; }

    /// <summary>双目标定重投影误差（像素）</summary>
    public double StereoReprojectionError { get; set; }

    /// <summary>左到右旋转矩阵 R(3x3) JSON</summary>
    public string RotationMatrixJson { get; set; } = string.Empty;

    /// <summary>左到右平移向量 t(3x1) JSON</summary>
    public string TranslationVectorJson { get; set; } = string.Empty;

    /// <summary>左到右 4x4 变换矩阵 JSON</summary>
    public string TransformLtoRJson { get; set; } = string.Empty;

    /// <summary>右到左 4x4 变换矩阵 JSON</summary>
    public string TransformRtoLJson { get; set; } = string.Empty;

    /// <summary>左相机立体校正旋转矩阵 R1 JSON</summary>
    public string RectificationR1Json { get; set; } = string.Empty;

    /// <summary>右相机立体校正旋转矩阵 R2 JSON</summary>
    public string RectificationR2Json { get; set; } = string.Empty;

    /// <summary>左相机投影矩阵 P1 JSON</summary>
    public string ProjectionP1Json { get; set; } = string.Empty;

    /// <summary>右相机投影矩阵 P2 JSON</summary>
    public string ProjectionP2Json { get; set; } = string.Empty;

    /// <summary>立体校正 map 宽度（像素）</summary>
    public int RectifyMapWidth { get; set; }

    /// <summary>立体校正 map 高度（像素）</summary>
    public int RectifyMapHeight { get; set; }

    /// <summary>左相机 map1x 数据 Blob Key</summary>
    public string Map1XBlobKey { get; set; } = string.Empty;

    /// <summary>左相机 map1y 数据 Blob Key</summary>
    public string Map1YBlobKey { get; set; } = string.Empty;

    /// <summary>右相机 map2x 数据 Blob Key</summary>
    public string Map2XBlobKey { get; set; } = string.Empty;

    /// <summary>右相机 map2y 数据 Blob Key</summary>
    public string Map2YBlobKey { get; set; } = string.Empty;
}

/// <summary>
/// 标定板参数 DTO（读取用）
/// </summary>
public class CalibBoardConfigDto
{
    /// <summary>标定板类型。</summary>
    public CalibrationBoardType BoardType { get; set; }

    /// <summary>实体棋盘格内角点行数</summary>
    public int PhysicalCornerRows { get; set; }

    /// <summary>实体棋盘格内角点列数</summary>
    public int PhysicalCornerCols { get; set; }

    /// <summary>实体棋盘格单个方格物理边长（mm）</summary>
    public decimal PhysicalSquareSizeMm { get; set; }

    /// <summary>投影棋盘格内角点行数</summary>
    public int ProjectedCornerRows { get; set; }

    /// <summary>投影棋盘格内角点列数</summary>
    public int ProjectedCornerCols { get; set; }

    /// <summary>投影棋盘格单个方格像素尺寸（px）</summary>
    public int ProjectedPixelSize { get; set; }

    /// <summary>标定板厚度（mm，用于投影仪外参标定补偿）</summary>
    public decimal BoardThicknessMm { get; set; }

    /// <summary>圆点标定板配置（非圆点类型时为 null）。</summary>
    public CircleBoardConfigDto? CircleBoardConfig { get; set; }
}

/// <summary>
/// 相机标定汇总 DTO（照片计数 + 标定结果）
/// </summary>
public class CalibCameraStatusDto
{
    /// <summary>相机设备ID</summary>
    public Guid CameraDeviceId { get; set; }

    /// <summary>内参照片总数</summary>
    public int IntrinsicTotal { get; set; }

    /// <summary>内参有效照片数</summary>
    public int IntrinsicValid { get; set; }

    /// <summary>外参照片总数</summary>
    public int ExtrinsicTotal { get; set; }

    /// <summary>外参有效照片数</summary>
    public int ExtrinsicValid { get; set; }

    /// <summary>双目成对照片总数</summary>
    public int StereoTotal { get; set; }

    /// <summary>双目成对有效照片数</summary>
    public int StereoValid { get; set; }

    /// <summary>最新标定结果（未计算则为 null）</summary>
    public CalibComputeResultDto? LatestResult { get; set; }
}

/// <summary>
/// 双目联合标定状态 DTO
/// </summary>
public class CalibStereoStatusDto
{
    /// <summary>成对拍照总组数</summary>
    public int PairTotal { get; set; }

    /// <summary>有效成对组数（左右均检测到角点）</summary>
    public int PairValid { get; set; }

    /// <summary>最新双目联合标定结果（未计算则为 null）</summary>
    public CalibStereoComputeResultDto? LatestResult { get; set; }
}
