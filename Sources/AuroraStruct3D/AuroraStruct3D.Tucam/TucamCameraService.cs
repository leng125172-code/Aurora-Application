using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using AuroraStruct3D.Tucam.Interop;
using Microsoft.Extensions.Logging;

namespace AuroraStruct3D.Tucam;

/// <summary>
/// TUCam相机操作服务实现，封装SDK P/Invoke调用
/// </summary>
public class TucamCameraService : ITucamCameraService, IDisposable
{
    private readonly ILogger<TucamCameraService> _logger;

    /// <summary>相机句柄字典，key为相机索引</summary>
    private readonly ConcurrentDictionary<int, IntPtr> _cameraHandles = new();

    /// <summary>SDK是否已初始化</summary>
    private bool _initialized;

    /// <summary>是否已释放资源</summary>
    private bool _disposed;

    public TucamCameraService(ILogger<TucamCameraService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public Task<int> InitializeAsync()
    {
        ThrowIfDisposed();

        var initParam = new TUCamInit { uiCamCount = 0, pstrConfigPath = IntPtr.Zero };

        var ret = TUCamNative.TUCAM_Api_Init(ref initParam, 1000);
        if (ret != TUCamRet.Success)
        {
            _logger.LogError("TUCam SDK初始化失败，返回码: {RetCode}", ret);
            throw new InvalidOperationException($"TUCam SDK初始化失败: {ret}");
        }

        _initialized = true;
        int count = (int)initParam.uiCamCount;
        _logger.LogInformation("TUCam SDK初始化成功，检测到 {Count} 台相机", count);
        return Task.FromResult(count);
    }

    /// <inheritdoc/>
    public Task UninitializeAsync()
    {
        if (!_initialized)
        {
            return Task.CompletedTask;
        }

        // 关闭所有已打开的相机
        foreach (var kv in _cameraHandles)
        {
            TUCamNative.TUCAM_Dev_Close(kv.Value);
            _logger.LogInformation("已关闭相机索引: {Index}", kv.Key);
        }
        _cameraHandles.Clear();

        TUCamNative.TUCAM_Api_Uninit();
        _initialized = false;
        _logger.LogInformation("TUCam SDK已反初始化");
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task OpenCameraAsync(int cameraIndex)
    {
        ThrowIfDisposed();
        ThrowIfNotInitialized();

        if (_cameraHandles.ContainsKey(cameraIndex))
        {
            _logger.LogWarning("相机索引 {Index} 已经打开", cameraIndex);
            return Task.CompletedTask;
        }

        var openParam = new TUCamOpen { uiIdxOpen = (uint)cameraIndex, hIdxTUCam = IntPtr.Zero };

        var ret = TUCamNative.TUCAM_Dev_Open(ref openParam);
        if (ret != TUCamRet.Success)
        {
            _logger.LogError("打开相机 {Index} 失败，返回码: {RetCode}", cameraIndex, ret);
            throw new InvalidOperationException($"打开相机失败（索引: {cameraIndex}）: {ret}");
        }

        _cameraHandles[cameraIndex] = openParam.hIdxTUCam;
        _logger.LogInformation(
            "相机 {Index} 打开成功，句柄: {Handle}",
            cameraIndex,
            openParam.hIdxTUCam
        );
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task CloseCameraAsync(int cameraIndex)
    {
        ThrowIfDisposed();

        if (!_cameraHandles.TryRemove(cameraIndex, out IntPtr handle))
        {
            _logger.LogWarning("相机索引 {Index} 未打开或已关闭", cameraIndex);
            return Task.CompletedTask;
        }

        var ret = TUCamNative.TUCAM_Dev_Close(handle);
        if (ret != TUCamRet.Success)
        {
            _logger.LogWarning("关闭相机 {Index} 时返回: {RetCode}", cameraIndex, ret);
        }
        else
        {
            _logger.LogInformation("相机 {Index} 已关闭", cameraIndex);
        }
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<string> GetCameraModelAsync(int cameraIndex)
    {
        ThrowIfDisposed();
        IntPtr handle = GetHandle(cameraIndex);

        // 分配文本缓冲区
        IntPtr textBuffer = Marshal.AllocHGlobal(256);
        try
        {
            var info = new TUCamValueInfo
            {
                nId = (int)TUCamIdInfo.CameraModel,
                pText = textBuffer,
                nTextSize = 256,
            };

            var ret = TUCamNative.TUCAM_Dev_GetInfo(handle, ref info);
            if (ret != TUCamRet.Success)
            {
                _logger.LogWarning(
                    "获取相机 {Index} 型号失败，返回码: {RetCode}",
                    cameraIndex,
                    ret
                );
                return Task.FromResult(string.Empty);
            }

            string model = Marshal.PtrToStringAnsi(textBuffer) ?? string.Empty;
            return Task.FromResult(model);
        }
        finally
        {
            Marshal.FreeHGlobal(textBuffer);
        }
    }

    /// <inheritdoc/>
    public Task<double> GetPropertyValueAsync(int cameraIndex, TUCamIdProp propId, int channel = 0)
    {
        ThrowIfDisposed();
        IntPtr handle = GetHandle(cameraIndex);

        double value = 0;
        var ret = TUCamNative.TUCAM_Prop_GetValue(handle, (int)propId, ref value, channel);
        if (ret != TUCamRet.Success)
        {
            _logger.LogWarning(
                "获取相机 {Index} 属性 {Prop} 失败，返回码: {RetCode}",
                cameraIndex,
                propId,
                ret
            );
            throw new InvalidOperationException($"获取相机属性失败（{propId}）: {ret}");
        }
        return Task.FromResult(value);
    }

    /// <inheritdoc/>
    public Task SetPropertyValueAsync(
        int cameraIndex,
        TUCamIdProp propId,
        double value,
        int channel = 0
    )
    {
        ThrowIfDisposed();
        IntPtr handle = GetHandle(cameraIndex);

        var ret = TUCamNative.TUCAM_Prop_SetValue(handle, (int)propId, value, channel);
        if (ret != TUCamRet.Success)
        {
            _logger.LogWarning(
                "设置相机 {Index} 属性 {Prop}={Value} 失败，返回码: {RetCode}",
                cameraIndex,
                propId,
                value,
                ret
            );
            throw new InvalidOperationException($"设置相机属性失败（{propId}={value}）: {ret}");
        }
        _logger.LogDebug("相机 {Index} 属性 {Prop} 已设置为 {Value}", cameraIndex, propId, value);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<int> GetCapabilityValueAsync(int cameraIndex, TUCamIdCapa capaId)
    {
        ThrowIfDisposed();
        IntPtr handle = GetHandle(cameraIndex);

        int value = 0;
        var ret = TUCamNative.TUCAM_Capa_GetValue(handle, (int)capaId, ref value);
        if (ret != TUCamRet.Success)
        {
            _logger.LogWarning(
                "获取相机 {Index} 能力 {Capa} 失败，返回码: {RetCode}",
                cameraIndex,
                capaId,
                ret
            );
            throw new InvalidOperationException($"获取相机能力失败（{capaId}）: {ret}");
        }
        return Task.FromResult(value);
    }

    /// <inheritdoc/>
    public Task SetCapabilityValueAsync(int cameraIndex, TUCamIdCapa capaId, int value)
    {
        ThrowIfDisposed();
        IntPtr handle = GetHandle(cameraIndex);

        var ret = TUCamNative.TUCAM_Capa_SetValue(handle, (int)capaId, value);
        if (ret != TUCamRet.Success)
        {
            _logger.LogWarning(
                "设置相机 {Index} 能力 {Capa}={Value} 失败，返回码: {RetCode}",
                cameraIndex,
                capaId,
                value,
                ret
            );
            throw new InvalidOperationException($"设置相机能力失败（{capaId}={value}）: {ret}");
        }
        _logger.LogDebug("相机 {Index} 能力 {Capa} 已设置为 {Value}", cameraIndex, capaId, value);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task StartCaptureAsync(int cameraIndex)
    {
        ThrowIfDisposed();
        IntPtr handle = GetHandle(cameraIndex);

        // 先分配帧缓冲区
        var frame = new TUCamFrame { uiRsdSize = 1 };
        var allocRet = TUCamNative.TUCAM_Buf_Alloc(handle, ref frame);
        if (allocRet != TUCamRet.Success)
        {
            throw new InvalidOperationException($"分配帧缓冲区失败: {allocRet}");
        }

        var ret = TUCamNative.TUCAM_Cap_Start(handle, (uint)TUCamCaptureMode.Sequence);
        if (ret != TUCamRet.Success)
        {
            TUCamNative.TUCAM_Buf_Release(handle);
            throw new InvalidOperationException($"启动采集失败: {ret}");
        }

        _logger.LogInformation("相机 {Index} 开始连续采集", cameraIndex);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task StopCaptureAsync(int cameraIndex)
    {
        ThrowIfDisposed();
        IntPtr handle = GetHandle(cameraIndex);

        TUCamNative.TUCAM_Buf_AbortWait(handle);
        TUCamNative.TUCAM_Cap_Stop(handle);
        TUCamNative.TUCAM_Buf_Release(handle);
        _logger.LogInformation("相机 {Index} 已停止采集", cameraIndex);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<CameraFrameData> GrabFrameAsync(int cameraIndex, int timeoutMs = 3000)
    {
        ThrowIfDisposed();
        IntPtr handle = GetHandle(cameraIndex);

        var frame = new TUCamFrame { uiRsdSize = 1 };
        var ret = TUCamNative.TUCAM_Buf_WaitForFrame(handle, ref frame, timeoutMs);
        if (ret != TUCamRet.Success)
        {
            throw new InvalidOperationException($"等待帧超时或失败: {ret}");
        }

        // 拷贝图像数据
        int dataSize = (int)frame.uiImgSize;
        byte[] imageData = new byte[dataSize];
        if (frame.pBuffer != IntPtr.Zero && dataSize > 0)
        {
            Marshal.Copy(frame.pBuffer + frame.usOffset, imageData, 0, dataSize);
        }

        var frameData = new CameraFrameData
        {
            Width = frame.usWidth,
            Height = frame.usHeight,
            BitDepth = frame.ucDepth,
            Channels = frame.ucChannels,
            FrameIndex = frame.uiIndex,
            Data = imageData,
        };

        return Task.FromResult(frameData);
    }

    /// <inheritdoc/>
    public bool IsCameraOpen(int cameraIndex)
    {
        return _cameraHandles.ContainsKey(cameraIndex);
    }

    /// <summary>
    /// 获取相机句柄，不存在则抛出异常
    /// </summary>
    private IntPtr GetHandle(int cameraIndex)
    {
        if (!_cameraHandles.TryGetValue(cameraIndex, out IntPtr handle))
        {
            throw new InvalidOperationException(
                $"相机（索引: {cameraIndex}）未打开，请先调用 OpenCameraAsync"
            );
        }
        return handle;
    }

    private void ThrowIfNotInitialized()
    {
        if (!_initialized)
        {
            throw new InvalidOperationException("TUCam SDK未初始化，请先调用 InitializeAsync");
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(TucamCameraService));
        }
    }

    /// <summary>释放资源</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        UninitializeAsync().GetAwaiter().GetResult();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
