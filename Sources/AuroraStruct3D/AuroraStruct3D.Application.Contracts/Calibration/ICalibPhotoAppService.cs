using AuroraStruct3D.Calibration.Dtos;
using Volo.Abp.Application.Services;

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
    /// 内参拍照：后端自动关闭投影仪 LED → 触发相机拍照 → OpenCV 棋盘格角点检测 → 存 BLOB → 写 DB
    /// </summary>
    /// <returns>新建的照片记录（含缩略图 Base64）</returns>
    Task<CalibPhotoDto> TakeIntrinsicPhotoAsync(TakeIntrinsicPhotoInput input);

    /// <summary>
    /// 外参拍照：后端自动开灯 → 投影棋盘图 → 触发相机拍照 → 角点检测 → 存 BLOB → 写 DB
    /// </summary>
    /// <returns>新建的照片记录（含缩略图 Base64）</returns>
    Task<CalibPhotoDto> TakeExtrinsicPhotoAsync(TakeExtrinsicPhotoInput input);

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
    /// 计算内参（calibrateCamera）及外参（solvePnP），结果写入 CalibCameraParam
    /// </summary>
    /// <param name="calibProjectId">标定项目ID</param>
    /// <param name="cameraDeviceId">相机设备ID</param>
    /// <returns>计算结果 DTO</returns>
    Task<CalibComputeResultDto> ComputeCalibrationAsync(Guid calibProjectId, Guid cameraDeviceId);

    /// <summary>
    /// 获取指定相机的照片计数及最新标定结果汇总
    /// </summary>
    Task<CalibCameraStatusDto> GetCameraStatusAsync(Guid calibProjectId, Guid cameraDeviceId);
}
