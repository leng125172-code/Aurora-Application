using Volo.Abp.Domain.Entities;

namespace AuroraStruct3D.CalibrationManagement;

/// <summary>
/// 标定采集帧：一次多相机同步触发产生的一组图像，对应 Frames 子集合的一行。
/// 同一工程下 <see cref="FrameIndex"/> 唯一且递增。
/// </summary>
public class CalibrationCaptureFrame : Entity<Guid>
{
    /// <summary>所属标定工程 ID</summary>
    public Guid CalibrationProjectId { get; private set; }

    /// <summary>帧序号（工程内递增，从 1 开始）</summary>
    public int FrameIndex { get; private set; }

    /// <summary>采集时间（UTC）</summary>
    public DateTime CapturedTime { get; private set; }

    /// <summary>是否被接受为有效采集（用户可标记拒绝并重采集）</summary>
    public bool IsAccepted { get; private set; }

    /// <summary>拒绝原因（IsAccepted=false 时填写）</summary>
    public string? RejectionReason { get; private set; }

    /// <summary>本帧包含的所有相机图像</summary>
    public ICollection<CalibrationCaptureImage> Images { get; private set; } =
        new List<CalibrationCaptureImage>();

    /// <summary>EF Core 构造函数</summary>
    protected CalibrationCaptureFrame() { }

    /// <summary>创建采集帧</summary>
    public CalibrationCaptureFrame(Guid id, Guid calibrationProjectId, int frameIndex)
        : base(id)
    {
        CalibrationProjectId = calibrationProjectId;
        FrameIndex = frameIndex;
        CapturedTime = DateTime.UtcNow;
        IsAccepted = true;
    }

    /// <summary>标记为拒绝</summary>
    public void Reject(string reason)
    {
        IsAccepted = false;
        RejectionReason = reason;
    }

    /// <summary>恢复为接受</summary>
    public void Accept()
    {
        IsAccepted = true;
        RejectionReason = null;
    }
}
