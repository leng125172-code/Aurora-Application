using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;

namespace AuroraStruct3D.Calibration;

/// <summary>
/// 双目联合外参标定结果实体。
/// 用于持久化 2目0光 / 2目1光 的双目联合标定输出。
/// </summary>
public class CalibStereoResult : FullAuditedEntity<Guid>
{
    /// <summary>所属标定项目ID</summary>
    public Guid CalibProjectId { get; private set; }

    /// <summary>主相机设备ID（左）</summary>
    public Guid MainCameraDeviceId { get; private set; }

    /// <summary>从相机设备ID（右）</summary>
    public Guid SecondaryCameraDeviceId { get; private set; }

    /// <summary>双目标定重投影误差（像素）</summary>
    public double StereoReprojectionError { get; private set; }

    /// <summary>左到右旋转矩阵 R(3x3) JSON</summary>
    public string RotationMatrixJson { get; private set; } = null!;

    /// <summary>左到右平移向量 t(3x1) JSON</summary>
    public string TranslationVectorJson { get; private set; } = null!;

    /// <summary>左到右 4x4 变换矩阵 JSON</summary>
    public string TransformLtoRJson { get; private set; } = null!;

    /// <summary>右到左 4x4 变换矩阵 JSON</summary>
    public string TransformRtoLJson { get; private set; } = null!;

    /// <summary>左相机立体校正旋转矩阵 R1 JSON</summary>
    public string RectificationR1Json { get; private set; } = null!;

    /// <summary>右相机立体校正旋转矩阵 R2 JSON</summary>
    public string RectificationR2Json { get; private set; } = null!;

    /// <summary>左相机投影矩阵 P1 JSON</summary>
    public string ProjectionP1Json { get; private set; } = null!;

    /// <summary>右相机投影矩阵 P2 JSON</summary>
    public string ProjectionP2Json { get; private set; } = null!;

    /// <summary>立体校正 map 宽度（像素）</summary>
    public int RectifyMapWidth { get; private set; }

    /// <summary>立体校正 map 高度（像素）</summary>
    public int RectifyMapHeight { get; private set; }

    /// <summary>左相机 map1x 数据 Blob Key</summary>
    public string Map1XBlobKey { get; private set; } = null!;

    /// <summary>左相机 map1y 数据 Blob Key</summary>
    public string Map1YBlobKey { get; private set; } = null!;

    /// <summary>右相机 map2x 数据 Blob Key</summary>
    public string Map2XBlobKey { get; private set; } = null!;

    /// <summary>右相机 map2y 数据 Blob Key</summary>
    public string Map2YBlobKey { get; private set; } = null!;

    protected CalibStereoResult() { }

    /// <summary>
    /// 创建双目标定结果。
    /// </summary>
    public CalibStereoResult(
        Guid id,
        Guid calibProjectId,
        Guid mainCameraDeviceId,
        Guid secondaryCameraDeviceId,
        double stereoReprojectionError,
        string rotationMatrixJson,
        string translationVectorJson,
        string transformLtoRJson,
        string transformRtoLJson,
        string rectificationR1Json,
        string rectificationR2Json,
        string projectionP1Json,
        string projectionP2Json,
        int rectifyMapWidth,
        int rectifyMapHeight,
        string map1XBlobKey,
        string map1YBlobKey,
        string map2XBlobKey,
        string map2YBlobKey
    )
        : base(id)
    {
        CalibProjectId = calibProjectId;
        MainCameraDeviceId = mainCameraDeviceId;
        SecondaryCameraDeviceId = secondaryCameraDeviceId;
        SetResult(
            stereoReprojectionError,
            rotationMatrixJson,
            translationVectorJson,
            transformLtoRJson,
            transformRtoLJson,
            rectificationR1Json,
            rectificationR2Json,
            projectionP1Json,
            projectionP2Json,
            rectifyMapWidth,
            rectifyMapHeight,
            map1XBlobKey,
            map1YBlobKey,
            map2XBlobKey,
            map2YBlobKey
        );
    }

    /// <summary>
    /// 更新双目标定结果。
    /// </summary>
    public CalibStereoResult SetResult(
        double stereoReprojectionError,
        string rotationMatrixJson,
        string translationVectorJson,
        string transformLtoRJson,
        string transformRtoLJson,
        string rectificationR1Json,
        string rectificationR2Json,
        string projectionP1Json,
        string projectionP2Json,
        int rectifyMapWidth,
        int rectifyMapHeight,
        string map1XBlobKey,
        string map1YBlobKey,
        string map2XBlobKey,
        string map2YBlobKey
    )
    {
        Check.NotNullOrWhiteSpace(
            rotationMatrixJson,
            nameof(rotationMatrixJson),
            CalibConsts.MaxCalibResultJsonLength
        );
        Check.NotNullOrWhiteSpace(
            translationVectorJson,
            nameof(translationVectorJson),
            CalibConsts.MaxCalibResultJsonLength
        );
        Check.NotNullOrWhiteSpace(
            transformLtoRJson,
            nameof(transformLtoRJson),
            CalibConsts.MaxCalibResultJsonLength
        );
        Check.NotNullOrWhiteSpace(
            transformRtoLJson,
            nameof(transformRtoLJson),
            CalibConsts.MaxCalibResultJsonLength
        );
        Check.NotNullOrWhiteSpace(
            rectificationR1Json,
            nameof(rectificationR1Json),
            CalibConsts.MaxCalibResultJsonLength
        );
        Check.NotNullOrWhiteSpace(
            rectificationR2Json,
            nameof(rectificationR2Json),
            CalibConsts.MaxCalibResultJsonLength
        );
        Check.NotNullOrWhiteSpace(
            projectionP1Json,
            nameof(projectionP1Json),
            CalibConsts.MaxCalibResultJsonLength
        );
        Check.NotNullOrWhiteSpace(
            projectionP2Json,
            nameof(projectionP2Json),
            CalibConsts.MaxCalibResultJsonLength
        );
        Check.NotNullOrWhiteSpace(map1XBlobKey, nameof(map1XBlobKey), CalibConsts.MaxBlobKeyLength);
        Check.NotNullOrWhiteSpace(map1YBlobKey, nameof(map1YBlobKey), CalibConsts.MaxBlobKeyLength);
        Check.NotNullOrWhiteSpace(map2XBlobKey, nameof(map2XBlobKey), CalibConsts.MaxBlobKeyLength);
        Check.NotNullOrWhiteSpace(map2YBlobKey, nameof(map2YBlobKey), CalibConsts.MaxBlobKeyLength);

        StereoReprojectionError = stereoReprojectionError;
        RotationMatrixJson = rotationMatrixJson;
        TranslationVectorJson = translationVectorJson;
        TransformLtoRJson = transformLtoRJson;
        TransformRtoLJson = transformRtoLJson;
        RectificationR1Json = rectificationR1Json;
        RectificationR2Json = rectificationR2Json;
        ProjectionP1Json = projectionP1Json;
        ProjectionP2Json = projectionP2Json;
        RectifyMapWidth = rectifyMapWidth;
        RectifyMapHeight = rectifyMapHeight;
        Map1XBlobKey = map1XBlobKey;
        Map1YBlobKey = map1YBlobKey;
        Map2XBlobKey = map2XBlobKey;
        Map2YBlobKey = map2YBlobKey;

        return this;
    }
}
