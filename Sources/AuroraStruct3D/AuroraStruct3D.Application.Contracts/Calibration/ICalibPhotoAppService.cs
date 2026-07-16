using AuroraStruct3D.Calibration.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.Content;

namespace AuroraStruct3D.Calibration;

/// <summary>
/// 标定照片管理应用服务接口（Step 5）。
/// 负责：棋盘格参数配置、内参/外参拍照、照片查询/删除、内外参计算。
/// </summary>
public interface ICalibPhotoAppService : IApplicationService
{
    /// <summary>
    /// 更新标定项目的棋盘格参数配置（CalibProjectId 在 input 中传入）
    /// </summary>
    Task<CalibBoardConfigDto> UpdateBoardConfigAsync(UpdateBoardConfigInput input);

    /// <summary>
    /// 获取标定项目当前棋盘格参数
    /// </summary>
    Task<CalibBoardConfigDto> GetBoardConfigAsync(Guid calibProjectId);

    /// <summary>
    /// 导出标定板参数配置（JSON）。
    /// </summary>
    Task<IRemoteStreamContent> ExportBoardConfigAsync(Guid calibProjectId);

    /// <summary>
    /// 导入标定板参数配置（JSON），并写入指定项目。
    /// </summary>
    Task<CalibBoardConfigDto> ImportBoardConfigAsync(
        Guid calibProjectId,
        IRemoteStreamContent file
    );

    /// <summary>
    /// 内参拍照：后端自动关闭投影仪 LED → 触发相机拍照 → OpenCV 棋盘格角点检测 → 存 BLOB → 写 DB
    /// </summary>
    /// <returns>新建的照片记录（含缩略图 Base64）</returns>
    Task<CalibPhotoDto> TakeIntrinsicPhotoAsync(TakeIntrinsicPhotoInput input);

    /// <summary>
    /// 外参拍照：后端先关灯拍实体标定板，再开灯拍投影标定图案，并将两张照片按同一分组样本写入 DB。
    /// </summary>
    /// <returns>新建的投影外参双拍样本（含关灯/开灯两张缩略图）</returns>
    Task<CalibExtrinsicSampleDto> TakeExtrinsicPhotoAsync(TakeExtrinsicPhotoInput input);

    /// <summary>
    /// 外参圆点拍照：投影仪切换到 S1 白屏模式，拍摄圆点标定板。
    /// 返回新建的照片记录（含缩略图），用于后续与棋盘格照片配对。
    /// </summary>
    Task<CalibPhotoDto> TakeExtrinsicDotPhotoAsync(TakeExtrinsicPhotoInput input);

    /// <summary>
    /// 外参棋盘格拍照：投影仪切换到 S3 棋盘格模式，拍摄纯棋盘格（需先移除标定板）。
    /// 返回新建的照片记录（含缩略图），与之前拍摄的圆点照片配对为一组外参样本。
    /// </summary>
    Task<CalibPhotoDto> TakeExtrinsicCheckerboardPhotoAsync(TakeExtrinsicPhotoInput input);

    /// <summary>
    /// 双目联合外参成对拍照：一次采集主/从相机两张实体棋盘格照片，并以同组 ID 关联
    /// </summary>
    Task<CalibStereoPairPhotoDto> TakeStereoExtrinsicPairPhotoAsync(
        TakeStereoExtrinsicPairPhotoInput input
    );

    /// <summary>
    /// 获取指定相机的照片列表（按类型过滤，传 null 则返回全部）
    /// </summary>
    Task<List<CalibPhotoDto>> GetPhotoListAsync(
        Guid calibProjectId,
        Guid cameraDeviceId,
        CalibPhotoType? photoType = null
    );

    /// <summary>
    /// 删除一张照片（同时删除 BLOB 文件）——ABP 自动生成 DELETE /api/app/calib-photo/{id}
    /// </summary>
    Task DeleteAsync(Guid id);

    /// <summary>
    /// 删除指定相机的全部无效照片（同时删除 BLOB 文件）
    /// </summary>
    /// <param name="calibProjectId">标定项目 ID</param>
    /// <param name="cameraDeviceId">相机设备 ID</param>
    /// <returns>删除的照片数量</returns>
    Task<int> DeleteInvalidPhotosAsync(Guid calibProjectId, Guid cameraDeviceId);

    /// <summary>
    /// 仅计算相机内参（calibrateCamera），结果写入 CalibCameraParam。
    /// POST /api/app/calib-photo/compute-intrinsic
    /// </summary>
    Task<CalibComputeResultDto> ComputeIntrinsicAsync(Guid calibProjectId, Guid cameraDeviceId);

    /// <summary>
    /// 仅计算外参（solvePnP + 投影仪内参），依赖已保存的相机内参，结果写入 CalibCameraParam。
    /// POST /api/app/calib-photo/compute-extrinsic
    /// </summary>
    Task<CalibComputeResultDto> ComputeExtrinsicAsync(Guid calibProjectId, Guid cameraDeviceId);

    /// <summary>
    /// 获取指定相机的照片计数及最新标定结果汇总
    /// </summary>
    Task<CalibCameraStatusDto> GetCameraStatusAsync(Guid calibProjectId, Guid cameraDeviceId);

    /// <summary>
    /// 计算双目联合外参（StereoCalibrate），结果写入 CalibStereoResult
    /// </summary>
    Task<CalibStereoComputeResultDto> ComputeStereoCalibrationAsync(Guid calibProjectId);

    /// <summary>
    /// 获取双目联合标定状态（成对样本计数 + 最新结果）
    /// </summary>
    Task<CalibStereoStatusDto> GetStereoStatusAsync(Guid calibProjectId);

    /// <summary>
    /// 校验 Step 5 标定结果是否已全部完成。
    /// 所有绑定相机的内参已计算；单光系列还需外参已计算；双目还需双目外参已计算。
    /// GET /api/app/calib-photo/validate-step5?calibProjectId={id}
    /// </summary>
    Task<bool> ValidateStep5Async(Guid calibProjectId);

}
