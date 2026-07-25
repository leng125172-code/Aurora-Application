using System.Text;
using HidSharp;

namespace FringeTool;

public sealed class HidProjectorClient : IDisposable
{
    public const int DefaultVendorId = 0x0483;
    public const int DefaultProductId = 0x5750;

    private readonly int _vendorId;
    private readonly int _productId;
    private readonly int _deviceIndex;

    private HidDevice? _device;
    private HidStream? _stream;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public bool IsConnected => _stream != null;

    public HidProjectorClient(int vendorId = DefaultVendorId, int productId = DefaultProductId, int deviceIndex = 0)
    {
        _vendorId = vendorId;
        _productId = productId;
        _deviceIndex = deviceIndex;
    }

    public static int GetDeviceCount(int vendorId = DefaultVendorId, int productId = DefaultProductId)
    {
        return DeviceList.Local.GetHidDevices(vendorId, productId).Count();
    }

    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(
            () =>
            {
                HidDevice[] devices = DeviceList
                    .Local.GetHidDevices(_vendorId, _productId)
                    .ToArray();

                if (devices.Length == 0)
                {
                    throw new InvalidOperationException(
                        $"HID设备未找到: VID=0x{_vendorId:X4} PID=0x{_productId:X4}"
                    );
                }

                if (_deviceIndex >= devices.Length)
                {
                    throw new InvalidOperationException(
                        $"设备索引 {_deviceIndex} 超出范围（找到 {devices.Length} 台设备）"
                    );
                }

                _device = devices[_deviceIndex];
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] HID设备已选择（索引 {_deviceIndex}）: {_device.DevicePath}");

                if (!_device.TryOpen(out _stream))
                {
                    _device = null;
                    throw new InvalidOperationException(
                        $"无法打开HID设备: {_device?.DevicePath}"
                    );
                }

                _stream.ReadTimeout = 3000;
                _stream.WriteTimeout = 3000;

                Console.WriteLine(
                    $"[{DateTime.Now:HH:mm:ss}] HID已连接: OutputReport={_device.GetMaxOutputReportLength()}B, InputReport={_device.GetMaxInputReportLength()}B"
                );
            },
            cancellationToken
        );
    }

    public Task DisconnectAsync()
    {
        _stream?.Close();
        _stream = null;
        _device = null;
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] HID已断开连接");
        return Task.CompletedTask;
    }

    public async Task<bool> SendCommandAsync(string command, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureConnected();
            string normalizedCmd = EnsureTerminator(command);
            WriteToDevice(normalizedCmd);
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] HID发送命令失败: {ex.Message}");
            return false;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<string?> SendCommandAndReadAsync(string command, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureConnected();
            DrainInputBuffer();

            string normalizedCmd = EnsureTerminator(command);
            WriteToDevice(normalizedCmd);

            string? response = ReadFromDevice();
            return response;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] HID发送/接收失败: {ex.Message}");
            return null;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<string?> ReadResponseAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureConnected();
            return ReadFromDevice();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] HID读取响应失败: {ex.Message}");
            return null;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task DrainInputBufferAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            DrainInputBuffer();
        }
        finally
        {
            _lock.Release();
        }
    }

    private void DrainInputBuffer()
    {
        if (_device == null || _stream == null)
            return;

        int inLen = _device.GetMaxInputReportLength();
        byte[] buf = new byte[inLen];
        int savedTimeout = _stream.ReadTimeout;
        try
        {
            _stream.ReadTimeout = 10;
            while (true)
            {
                try
                {
                    int n = _stream.Read(buf, 0, inLen);
                    if (n == 0)
                        break;
                }
                catch (TimeoutException)
                {
                    break;
                }
            }
        }
        catch
        {
        }
        finally
        {
            _stream.ReadTimeout = savedTimeout;
        }
    }

    private void WriteToDevice(string command)
    {
        int outLen = _device!.GetMaxOutputReportLength();
        byte[] buf = new byte[outLen];

        byte[] cmdBytes = Encoding.ASCII.GetBytes(command);
        int copyLen = Math.Min(cmdBytes.Length, outLen - 1);
        Array.Copy(cmdBytes, 0, buf, 1, copyLen);

        _stream!.Write(buf, 0, outLen);
    }

    private string? ReadFromDevice()
    {
        int inLen = _device!.GetMaxInputReportLength();
        byte[] buf = new byte[inLen];
        int bytesRead = _stream!.Read(buf, 0, inLen);

        if (bytesRead <= 1)
            return null;

        return Encoding.ASCII.GetString(buf, 1, bytesRead - 1).TrimEnd('\0', '\r', '\n');
    }

    private void EnsureConnected()
    {
        if (_stream == null)
            throw new InvalidOperationException("HID设备未连接，请先调用 ConnectAsync");
    }

    private static string EnsureTerminator(string command)
    {
        if (command.EndsWith("\r\n", StringComparison.Ordinal))
            return command;
        if (command.EndsWith('\n'))
            return command.TrimEnd('\n') + "\r\n";
        if (command.EndsWith('\r'))
            return command.TrimEnd('\r') + "\r\n";
        return command + "\r\n";
    }

    public void Dispose()
    {
        _stream?.Dispose();
        _stream = null;
        _lock.Dispose();
    }
}