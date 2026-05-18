using HidSharp;
using Microsoft.Extensions.Logging;
using System.Text;

namespace AuroraStruct3D.DLP.Protocol;

/// <summary>
/// 腾聚投影机 USB HID 通信客户端（跨平台，Windows + Linux ARM64）。
///
/// 硬件：Megawin EasyPOD HID 芯片，默认 VID=0x0E6A，PID=0x0317。
/// Linux 设备节点：/dev/hidraw0（需 udev 规则或 root 权限）。
/// Windows：通过 HID API 自动枚举，无需驱动安装。
///
/// 数据帧格式（与 C++ SDK EasyPOD / Win32 HID 模式一致）：
///   写入：byte[0]=报告ID(0x00)，byte[1..N]=ASCII命令，其余补零，总长度=OutputReportByteLength
///   读取：byte[0]=报告ID(跳过)，byte[1..N]=ASCII响应，总长度=InputReportByteLength
/// </summary>
public sealed class TjProjectorHidClient : IDisposable
{
    // ─── 腾聚 TJ 投影机默认 USB HID 标识 ─────────────────────────
    /// <summary>腾聚 TJ 投影机默认 HID 厂商 ID（Megawin EasyPOD 芯片）</summary>
    public const int DefaultVendorId = 0x0E6A;

    /// <summary>腾聚 TJ 投影机默认 HID 产品 ID</summary>
    public const int DefaultProductId = 0x0317;

    private readonly int _vendorId;
    private readonly int _productId;
    private readonly int _deviceIndex;
    private readonly ILogger _logger;

    private HidDevice? _device;
    private HidStream? _stream;
    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>是否已连接</summary>
    public bool IsConnected => _stream != null;

    /// <summary>
    /// 创建 TJ 投影机 HID 客户端
    /// </summary>
    /// <param name="vendorId">HID 厂商 ID（默认 0x0E6A）</param>
    /// <param name="productId">HID 产品 ID（默认 0x0317）</param>
    /// <param name="logger">日志记录器</param>
    /// <param name="deviceIndex">设备索引（同一 VID/PID 多台时从 0 开始区分）</param>
    public TjProjectorHidClient(
        int vendorId,
        int productId,
        ILogger logger,
        int deviceIndex = 0
    )
    {
        _vendorId = vendorId;
        _productId = productId;
        _logger = logger;
        _deviceIndex = deviceIndex;
    }

    /// <summary>
    /// 打开 HID 连接。
    /// Linux 提示：运行用户需有 /dev/hidraw* 读写权限，
    /// 推荐添加 udev 规则：SUBSYSTEM=="hidraw", ATTRS{idVendor}=="0e6a", ATTRS{idProduct}=="0317", MODE="0666"
    /// </summary>
    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(
            () =>
            {
                // 枚举所有匹配 VID/PID 的 HID 设备
                HidDevice[] devices = DeviceList.Local
                    .GetHidDevices(_vendorId, _productId)
                    .ToArray();

                if (devices.Length == 0)
                {
                    throw new InvalidOperationException(
                        $"未找到 VID=0x{_vendorId:X4} PID=0x{_productId:X4} 的 HID 设备，"
                            + "请确认设备已插入并在 Linux 上具备 /dev/hidraw* 读写权限"
                    );
                }

                if (_deviceIndex >= devices.Length)
                {
                    throw new InvalidOperationException(
                        $"设备索引 {_deviceIndex} 超出范围（共找到 {devices.Length} 台设备）"
                    );
                }

                _device = devices[_deviceIndex];
                _logger.LogInformation(
                    "HID 设备已找到（第 {Index} 台）：{Path}",
                    _deviceIndex,
                    _device.DevicePath
                );

                if (!_device.TryOpen(out _stream))
                {
                    _device = null;
                    throw new InvalidOperationException(
                        $"无法打开 HID 设备 {_device?.DevicePath}。"
                            + "Linux 请检查 /dev/hidraw* 权限；Windows 请确认设备未被其他程序占用"
                    );
                }

                // 设置读写超时（ms）
                _stream.ReadTimeout = 3000;
                _stream.WriteTimeout = 3000;

                _logger.LogInformation(
                    "HID 连接成功：OutputReport={Out}B, InputReport={In}B",
                    _device.GetMaxOutputReportLength(),
                    _device.GetMaxInputReportLength()
                );
            },
            cancellationToken
        );
    }

    /// <summary>关闭 HID 连接</summary>
    public Task DisconnectAsync()
    {
        _stream?.Close();
        _stream = null;
        _device = null;
        return Task.CompletedTask;
    }

    /// <summary>
    /// 向投影机发送 ASCII 命令（不等待响应）。
    /// 返回 true 表示写入成功。
    /// </summary>
    public async Task<bool> SendCommandAsync(
        string command,
        CancellationToken cancellationToken = default
    )
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureConnected();
            WriteToDevice(command);
            _logger.LogDebug("HID 发送: {Cmd}", command.TrimEnd('\r', '\n'));
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "HID 发送命令失败: {Cmd}", command.TrimEnd('\r', '\n'));
            return false;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// 向投影机发送 ASCII 命令并读取一行响应。
    /// 返回响应字符串，超时或失败时返回 null。
    /// </summary>
    public async Task<string?> SendCommandAndReadAsync(
        string command,
        CancellationToken cancellationToken = default
    )
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureConnected();
            WriteToDevice(command);
            _logger.LogDebug("HID 发送: {Cmd}", command.TrimEnd('\r', '\n'));

            string? response = ReadFromDevice();
            _logger.LogDebug("HID 接收: {Response}", response);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "HID 收发命令失败: {Cmd}", command.TrimEnd('\r', '\n'));
            return null;
        }
        finally
        {
            _lock.Release();
        }
    }

    // ─── 内部辅助 ────────────────────────────────────────────────

    /// <summary>
    /// 向 HID 设备写入命令（同步，内部使用）。
    /// 帧格式：byte[0]=0x00(报告ID)，byte[1..N]=ASCII命令，其余补零。
    /// </summary>
    private void WriteToDevice(string command)
    {
        // OutputReportByteLength 通常为 65（1 字节报告ID + 64 字节数据）
        int outLen = _device!.GetMaxOutputReportLength();
        byte[] buf = new byte[outLen];
        // buf[0] = 0x00（报告 ID，数组已零初始化）

        byte[] cmdBytes = Encoding.ASCII.GetBytes(command);
        int copyLen = Math.Min(cmdBytes.Length, outLen - 1);
        Array.Copy(cmdBytes, 0, buf, 1, copyLen);

        _stream!.Write(buf, 0, outLen);
    }

    /// <summary>
    /// 从 HID 设备读取响应（同步，内部使用）。
    /// 跳过 byte[0]（报告 ID），返回去除末尾空字节和换行符的 ASCII 字符串。
    /// </summary>
    private string? ReadFromDevice()
    {
        // InputReportByteLength 通常为 65（1 字节报告ID + 64 字节数据）
        int inLen = _device!.GetMaxInputReportLength();
        byte[] buf = new byte[inLen];
        int bytesRead = _stream!.Read(buf, 0, inLen);

        if (bytesRead <= 1)
            return null;

        // 跳过 buf[0]（报告 ID），从 buf[1] 开始解析 ASCII
        return Encoding.ASCII
            .GetString(buf, 1, bytesRead - 1)
            .TrimEnd('\0', '\r', '\n');
    }

    private void EnsureConnected()
    {
        if (_stream == null)
            throw new InvalidOperationException("HID 设备未连接，请先调用 ConnectAsync");
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _stream?.Dispose();
        _stream = null;
        _lock.Dispose();
    }
}
