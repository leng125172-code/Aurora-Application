namespace AuroraStruct3D.Projectors;

/// <summary>
/// 腾聚结构光投影机状态信息
/// </summary>
public class DlpProjectorStatus
{
    /// <summary>投影机连接方式</summary>
    public string ConnectionType { get; init; } = string.Empty;

    /// <summary>投影机 IP 地址（TCP 模式）</summary>
    public string? IpAddress { get; init; }

    /// <summary>TCP 端口（TCP 模式）</summary>
    public int Port { get; init; }

    /// <summary>USB HID 设备路径（USB 模式）</summary>
    public string? HidDevicePath { get; init; }

    /// <summary>HID 厂商 ID（USB 模式）</summary>
    public int HidVendorId { get; init; }

    /// <summary>HID 产品 ID（USB 模式）</summary>
    public int HidProductId { get; init; }

    /// <summary>是否已连接</summary>
    public bool IsConnected { get; init; }

    /// <summary>固件版本（连接后查询）</summary>
    public string? FirmwareVersion { get; init; }

    /// <summary>设备标志字节（ID）</summary>
    public int DeviceId { get; init; }

    /// <summary>当前亮度值（10~200）</summary>
    public byte CurrentLight { get; init; }
}

/// <summary>
/// 腾聚（TJ）结构光投影机操作服务接口。
/// 支持 TCP/IP 和 USB HID（Megawin EasyPOD 芯片）两种连接方式，协议均为 ASCII 文本命令（\r\n 结尾）。
/// 全平台支持 linux-arm64 和 Windows，无需原生 DLL。
/// </summary>
public interface IDlpProjectorService
{
    /// <summary>
    /// 连接到指定 IP 的投影机（TCP 端口 1234）
    /// </summary>
    /// <param name="ip">投影机 IPv4 地址，如 "192.168.100.100"</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task ConnectAsync(string ip, CancellationToken cancellationToken = default);

    /// <summary>
    /// 连接到指定 IP 和端口的投影机
    /// </summary>
    /// <param name="ip">投影机 IPv4 地址</param>
    /// <param name="port">TCP 端口（默认 1234）</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task ConnectAsync(string ip, int port, CancellationToken cancellationToken = default);

    /// <summary>
    /// 通过 USB HID 连接投影机（Megawin EasyPOD 芯片，跨平台 Windows + Linux）
    /// </summary>
    /// <param name="vendorId">HID 厂商 ID（默认 0x0E6A）</param>
    /// <param name="productId">HID 产品 ID（默认 0x0317）</param>
    /// <param name="deviceIndex">设备索引（同一 VID/PID 多台时从 0 开始）</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task ConnectHidAsync(
        int vendorId = 0x0483,
        int productId = 0x5750,
        int deviceIndex = 0,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// 断开投影机连接
    /// </summary>
    Task DisconnectAsync();

    /// <summary>
    /// 绑定当前服务实例对应的投影机设备 ID（用于操作日志写入）。
    /// 在连接投影机后调用，传入数据库中 ProjectorDevice 的 Guid。
    /// 未调用此方法时操作日志不会写入。
    /// </summary>
    /// <param name="projectorDeviceId">投影机设备数据库 ID</param>
    void SetProjectorDeviceId(Guid projectorDeviceId);

    /// <summary>
    /// 注入 HID 设备索引到投影机设备 ID 的映射（用于自动绑定操作日志设备 ID）。
    /// 通常在应用启动时由数据库读取后注入。
    /// </summary>
    /// <param name="deviceIds">HID 设备索引 -> ProjectorDevice.Id</param>
    void SetProjectorDeviceIdMapping(IReadOnlyDictionary<int, Guid> deviceIds);

    /// <summary>
    /// 获取投影机当前状态（含连接状态、固件版本）
    /// </summary>
    Task<DlpProjectorStatus> GetStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取固件版本字符串
    /// </summary>
    Task<string?> GetFirmwareVersionAsync(CancellationToken cancellationToken = default);

    // ─── LED 控制 ─────────────────────────────────────────────────

    /// <summary>
    /// 开启投影灯。不使用时建议关闭以延长设备寿命
    /// </summary>
    Task<bool> LedOnAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 关闭投影灯
    /// </summary>
    Task<bool> LedOffAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 设置投影亮度（10~200）。亮度 > 175 时注意散热
    /// </summary>
    /// <param name="light">亮度值，范围 10~200</param>
    Task<bool> SetLightAsync(byte light, CancellationToken cancellationToken = default);

    // ─── 显示内容控制 ─────────────────────────────────────────────

    /// <summary>
    /// 设置投影内容显示模式（黑屏/白屏/十字线/棋盘格）
    /// </summary>
    /// <param name="mode">显示模式</param>
    Task<bool> SetDisplayModeAsync(
        ProjectorDisplayMode mode,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// 设置投影颜色（仅多光谱结构光投影机支持）
    /// </summary>
    /// <param name="color">颜色</param>
    Task<bool> SetColorAsync(ProjectorColor color, CancellationToken cancellationToken = default);

    // ─── 条纹投影触发 ─────────────────────────────────────────────

    /// <summary>
    /// 触发一次条纹投影（末尾帧为白色，等同于 nGray=255）
    /// </summary>
    Task<bool> TriggerOnceAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 触发一次条纹投影，并指定末尾帧灰度值（0=黑，255=白）
    /// </summary>
    /// <param name="endGray">末尾帧灰度值（0~254，注意 255 走白色快速命令）</param>
    Task<bool> TriggerOnceAsync(byte endGray, CancellationToken cancellationToken = default);

    // ─── 通用命令 ────────────────────────────────────────────────

    /// <summary>
    /// 发送原始 ASCII 命令（无响应等待）
    /// </summary>
    /// <param name="command">命令字符串（需自行包含 \r\n）</param>
    Task<bool> SendRawCommandAsync(string command, CancellationToken cancellationToken = default);

    /// <summary>
    /// 发送原始 ASCII 命令并读取响应
    /// </summary>
    /// <param name="command">命令字符串（需自行包含 \r\n）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>响应文本，超时时返回 null</returns>
    Task<string?> SendRawCommandAndReadAsync(
        string command,
        CancellationToken cancellationToken = default
    );

    // ─── 高级控制（Phase 3 新增）────────────────────────────────

    /// <summary>
    /// 设置图像翻转模式（None/FlipX/FlipY/FlipXY）
    /// </summary>
    Task<bool> SetFlipAsync(ProjectorFlipMode flip, CancellationToken cancellationToken = default);

    /// <summary>
    /// 设置触发模式（Normal/Loop/SingleFrame）
    /// </summary>
    Task<bool> SetTriggerModeAsync(
        ProjectorTriggerMode mode,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// 设置开机默认图案
    /// </summary>
    Task<bool> SetBootImageAsync(
        ProjectorBootImage image,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// 设置棋盘格像素尺寸（像素，建议范围 5~100）
    /// </summary>
    Task<bool> SetCheckerboardPixelSizeAsync(
        int pixelSize,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// 设置 RGB 分量亮度（Aura Sync 彩光模式）
    /// </summary>
    /// <param name="r">红色分量（0~255）</param>
    /// <param name="g">绿色分量（0~255）</param>
    /// <param name="b">蓝色分量（0~255）</param>
    Task<bool> SetRgbColorAsync(
        byte r,
        byte g,
        byte b,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// 软复位（发送 X 指令重启投影机固件）
    /// </summary>
    Task<bool> SoftResetAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 保存当前参数到设备内部非易失性存储
    /// </summary>
    Task<bool> SaveParamsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 读取指定寄存器值
    /// </summary>
    /// <param name="address">寄存器地址（如 0 对应 "pr 0\r\n"）</param>
    Task<string?> ReadRegisterAsync(int address, CancellationToken cancellationToken = default);

    /// <summary>
    /// 写入指定寄存器值
    /// </summary>
    /// <param name="address">寄存器地址</param>
    /// <param name="value">写入值</param>
    Task<bool> WriteRegisterAsync(
        int address,
        int value,
        CancellationToken cancellationToken = default
    );
}
