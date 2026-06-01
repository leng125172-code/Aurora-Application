namespace AuroraStruct3D.CalibrationManagement;

/// <summary>
/// 标定计算进度通知器接口。
/// 由 HttpApi.Host 层通过 SignalR Hub 实现，用于向前端实时推送计算各阶段的进度事件。
/// </summary>
public interface ICalibrationProgressNotifier
{
    /// <summary>
    /// 通知前端标定计算阶段进度变化。
    /// </summary>
    /// <param name="projectId">标定工程 Id</param>
    /// <param name="stage">当前阶段名称（如 "intrinsics" / "extrinsics" / "structured-light" / "saving"）</param>
    /// <param name="percent">0～100 的整数进度百分比</param>
    /// <param name="message">可选的中文状态说明（如"正在计算相机内参（2/3）"）</param>
    Task NotifyProgressAsync(Guid projectId, string stage, int percent, string? message = null);

    /// <summary>
    /// 通知前端标定计算已完成（成功或失败）。
    /// </summary>
    /// <param name="projectId">标定工程 Id</param>
    /// <param name="success">是否成功</param>
    /// <param name="resultId">成功时写入的 CalibrationResult Id；失败时为 null</param>
    /// <param name="errorMessage">失败时的错误信息；成功时为 null</param>
    Task NotifyCompletedAsync(
        Guid projectId,
        bool success,
        Guid? resultId = null,
        string? errorMessage = null
    );
}
