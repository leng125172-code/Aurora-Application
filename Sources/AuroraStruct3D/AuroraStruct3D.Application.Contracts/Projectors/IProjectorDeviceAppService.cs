using AuroraStruct3D.Projectors.Dtos;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace AuroraStruct3D.Projectors;

/// <summary>
/// 投影机设备管理及手动控制应用服务接口
/// </summary>
public interface IProjectorDeviceAppService : IApplicationService
{
    // ─── 设备 CRUD ────────────────────────────────────────────────────────

    /// <summary>
    /// 获取所有投影机设备列表（分页）
    /// </summary>
    Task<PagedResultDto<ProjectorDeviceDto>> GetListAsync(GetProjectorListDto input);

    /// <summary>
    /// 获取单个投影机设备详情
    /// </summary>
    Task<ProjectorDeviceDto> GetAsync(Guid id);

    /// <summary>
    /// 扫描当前连接的 USB HID 投影机，自动同步数据库记录（新增未知设备，保留已有设备）。
    /// 仅处理 HID 类型投影机；TCP 类型设备不受影响。
    /// </summary>
    /// <returns>检测到的 HID 投影机数量</returns>
    Task<int> ScanProjectorsAsync();

    /// <summary>
    /// 更新投影机设备基本信息
    /// </summary>
    Task<ProjectorDeviceDto> UpdateAsync(Guid id, UpdateProjectorDeviceDto input);

    // ─── 连接管理 ─────────────────────────────────────────────────────────

    /// <summary>
    /// 连接投影机（根据设备配置自动选择 TCP 或 HID）
    /// </summary>
    Task ConnectAsync(Guid id);

    /// <summary>
    /// 断开投影机连接
    /// </summary>
    Task DisconnectAsync(Guid id);

    // ─── LED 控制 ─────────────────────────────────────────────────────────

    /// <summary>
    /// 开灯
    /// </summary>
    Task<bool> LedOnAsync(Guid id);

    /// <summary>
    /// 关灯
    /// </summary>
    Task<bool> LedOffAsync(Guid id);

    /// <summary>
    /// 设置亮度
    /// </summary>
    Task<bool> SetLightAsync(SetProjectorLightDto input);

    // ─── 显示控制 ─────────────────────────────────────────────────────────

    /// <summary>
    /// 设置显示模式（黑屏/白屏/十字/棋盘格/内部图像）
    /// </summary>
    Task<bool> SetDisplayModeAsync(SetProjectorDisplayModeDto input);

    /// <summary>
    /// 设置颜色（多光谱模式）
    /// </summary>
    Task<bool> SetColorAsync(SetProjectorColorDto input);

    /// <summary>
    /// 设置图像翻转模式
    /// </summary>
    Task<bool> SetFlipAsync(SetProjectorFlipDto input);

    /// <summary>
    /// 设置触发模式
    /// </summary>
    Task<bool> SetTriggerModeAsync(SetProjectorTriggerModeDto input);

    /// <summary>
    /// 设置开机默认图案
    /// </summary>
    Task<bool> SetBootImageAsync(SetProjectorBootImageDto input);

    /// <summary>
    /// 设置棋盘格像素尺寸
    /// </summary>
    Task<bool> SetCheckerboardPixelSizeAsync(SetProjectorCheckerboardDto input);

    /// <summary>
    /// 设置 RGB 分量亮度（彩光模式）
    /// </summary>
    Task<bool> SetRgbColorAsync(SetProjectorRgbDto input);

    // ─── 条纹触发 ─────────────────────────────────────────────────────────

    /// <summary>
    /// 触发一次条纹投影
    /// </summary>
    Task<bool> TriggerOnceAsync(TriggerProjectorDto input);

    // ─── 高级操作 ─────────────────────────────────────────────────────────

    /// <summary>
    /// 软复位投影机
    /// </summary>
    Task<bool> SoftResetAsync(Guid id);

    /// <summary>
    /// 保存参数到设备内存
    /// </summary>
    Task<bool> SaveParamsAsync(Guid id);

    /// <summary>
    /// 读取寄存器
    /// </summary>
    Task<string?> ReadRegisterAsync(Guid id, int address);

    /// <summary>
    /// 写入寄存器
    /// </summary>
    Task<bool> WriteRegisterAsync(WriteProjectorRegisterDto input);

    // ─── 操作日志 ─────────────────────────────────────────────────────────

    /// <summary>
    /// 获取操作日志列表（分页）
    /// </summary>
    Task<PagedResultDto<ProjectorOperationLogDto>> GetLogsAsync(GetProjectorLogListDto input);

    // ─── 像素分辨率与条纹下载 ────────────────────────────────────────────

    /// <summary>
    /// 通过 Fp 指令查询投影机像素分辨率（宽度像素数与像素模式描述）
    /// </summary>
    /// <param name="id">投影机设备 ID</param>
    Task<ProjectorPixelResolutionDto> GetPixelResolutionAsync(Guid id);

    /// <summary>
    /// 在后端生成条纹图案预览数据并返回给前端。
    /// </summary>
    Task<List<FringePreviewImageDto>> GenerateFringePreviewAsync(
        DownloadFringePatternInputDto input
    );

    /// <summary>
    /// 获取指定投影机当前条纹下载状态。
    /// </summary>
    Task<ProjectorFringeDownloadStatusDto> GetFringeDownloadStatusAsync(Guid id);

    /// <summary>
    /// 启动条纹图案下载到投影机 Flash。
    /// 接口在后台任务启动后立即返回，进度与结果通过状态查询和 SignalR 推送。
    /// </summary>
    Task DownloadFringePatternAsync(DownloadFringePatternInputDto input);
}
