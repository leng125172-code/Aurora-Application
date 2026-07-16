using System.Text;
using HidSharp;
using Microsoft.Extensions.Logging;

namespace AuroraStruct3D.Projectors.Protocol;

/// <summary>
/// 腾聚投影仪 USB HID 通信客户端（跨平台，Windows + Linux ARM64）。
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
    private const string LogTag = "[Projector]";

    // ─── 腾聚 TJ 投影仪默认 USB HID 标识 ─────────────────────────
    /// <summary>腾聚 TJ 投影仪默认 HID 厂商 ID（VID 0x0483）</summary>
    public const int DefaultVendorId = 0x0483;

    /// <summary>腾聚 TJ 投影仪默认 HID 产品 ID（PID 0x5750）</summary>
    public const int DefaultProductId = 0x5750;

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
    /// 创建 TJ 投影仪 HID 客户端
    /// </summary>
    /// <param name="vendorId">HID 厂商 ID（默认 0x0E6A）</param>
    /// <param name="productId">HID 产品 ID（默认 0x0317）</param>
    /// <param name="logger">日志记录器</param>
    /// <param name="deviceIndex">设备索引（同一 VID/PID 多台时从 0 开始区分）</param>
    public TjProjectorHidClient(int vendorId, int productId, ILogger logger, int deviceIndex = 0)
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
                HidDevice[] devices = DeviceList
                    .Local.GetHidDevices(_vendorId, _productId)
                    .ToArray();

                if (devices.Length == 0)
                {
                    throw new InvalidOperationException(
                        $"{LogTag} HID device with VID=0x{_vendorId:X4} PID=0x{_productId:X4} not found. "
                            + "Ensure the device is connected and /dev/hidraw* permissions are granted on Linux."
                    );
                }

                if (_deviceIndex >= devices.Length)
                {
                    throw new InvalidOperationException(
                        $"{LogTag} Device index {_deviceIndex} is out of range (found {devices.Length} device(s))."
                    );
                }

                _device = devices[_deviceIndex];
                _logger.LogInformation(
                    "{Tag} HID device selected (index {Index}): {Path}",
                    LogTag,
                    _deviceIndex,
                    _device.DevicePath
                );

                if (!_device.TryOpen(out _stream))
                {
                    _device = null;
                    throw new InvalidOperationException(
                        $"{LogTag} Cannot open HID device {_device?.DevicePath}. "
                            + "On Linux, check /dev/hidraw* permissions; on Windows, ensure no other process is using it."
                    );
                }

                // 设置读写超时（ms）
                _stream.ReadTimeout = 3000;
                _stream.WriteTimeout = 3000;

                _logger.LogInformation(
                    "{Tag} HID connected: OutputReport={Out}B, InputReport={In}B",
                    LogTag,
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
    /// 向投影仪发送 ASCII 命令（不等待响应）。
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
            _logger.LogDebug("{Tag} HID TX: {Cmd}", LogTag, command.TrimEnd('\r', '\n'));
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "{Tag} HID send command failed: {Cmd}",
                LogTag,
                command.TrimEnd('\r', '\n')
            );
            return false;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// 向投影仪发送 ASCII 命令并读取一行响应。
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

            // 发送命令前先排空缓冲区，避免读到上一条命令的残留响应
            DrainInputBuffer();

            WriteToDevice(command);
            _logger.LogDebug("{Tag} HID TX: {Cmd}", LogTag, command.TrimEnd('\r', '\n'));

            string? response = ReadFromDevice();
            _logger.LogDebug("{Tag} HID RX: {Response}", LogTag, response);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "{Tag} HID send/receive failed: {Cmd}",
                LogTag,
                command.TrimEnd('\r', '\n')
            );
            return null;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// 仅读取一行响应，不发送任何命令（用于等待 Flash page 写入完成的应答）。
    /// 超时或未连接时返回 null。
    /// </summary>
    public async Task<string?> ReadResponseAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureConnected();
            string? response = ReadFromDevice();
            _logger.LogDebug("{Tag} HID Page-write ACK: {Response}", LogTag, response);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "{Tag} HID ReadResponseAsync failed", LogTag);
            return null;
        }
        finally
        {
            _lock.Release();
        }
    }

    // ─── 内部辅助 ────────────────────────────────────────────────

    /// <summary>
    /// 排空 HID 接收缓冲区中的残留旧数据。
    /// 每次发送需要响应的命令之前调用，确保读到的是本次命令的真实响应。
    /// 使用极短超时（10ms）快速清空，缓冲区为空时捕获 TimeoutException 退出。
    /// </summary>
    private void DrainInputBuffer()
    {
        if (_device == null || _stream == null)
            return;

        int inLen = _device.GetMaxInputReportLength();
        byte[] buf = new byte[inLen];
        int savedTimeout = _stream.ReadTimeout;
        try
        {
            _stream.ReadTimeout = 10; // 10ms 快速轮询，无数据即超时
            while (true)
            {
                try
                {
                    int n = _stream.Read(buf, 0, inLen);
                    if (n == 0)
                        break;
                    // 有数据则继续排空
                }
                catch (TimeoutException)
                {
                    break; // 缓冲区已空
                }
            }
        }
        catch
        {
            // 排空过程中的任何其他异常均忽略，不影响主流程
        }
        finally
        {
            _stream.ReadTimeout = savedTimeout;
        }
    }

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
        return Encoding.ASCII.GetString(buf, 1, bytesRead - 1).TrimEnd('\0', '\r', '\n');
    }

    private void EnsureConnected()
    {
        if (_stream == null)
            throw new InvalidOperationException(
                $"{LogTag} HID device is not connected. Call ConnectAsync first."
            );
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _stream?.Dispose();
        _stream = null;
        _lock.Dispose();
    }
}
