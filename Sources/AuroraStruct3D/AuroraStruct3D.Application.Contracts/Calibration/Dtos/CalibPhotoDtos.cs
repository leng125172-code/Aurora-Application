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

/// <summary>
/// 标定计算结果 DTO
/// </summary>
public class CalibComputeResultDto
{
    /// <summary>内参矩阵（3×3，JSON 序列化的 double[][] 数组）</summary>
    public string IntrinsicMatrixJson { get; set; } = string.Empty;

    /// <summary>畸变系数（JSON 序列化的 double[] 数组）</summary>
    public string DistCoeffsJson { get; set; } = string.Empty;

    /// <summary>重投影误差（像素）</summary>
    public double ReprojectionError { get; set; }

    /// <summary>投影外参重投影误差（像素，无投影仪时为 null）</summary>
    public double? ProjectorReprojectionError { get; set; }

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
