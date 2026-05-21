using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using AuroraStruct3D.Cameras;
using AuroraStruct3D.Tucam.Interop;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace AuroraStruct3D.Tucam;

/// <summary>
/// TUCam相机操作服务实现，封装SDK P/Invoke调用
/// </summary>
public class TucamCameraService : ITucamCameraService, IDisposable
{
    private const string LogTag = "[Cameras]";

    private readonly ILogger<TucamCameraService> _logger;
    private readonly IServiceScopeFactory? _serviceScopeFactory;

    /// <summary>相机句柄字典，key为相机索引</summary>
    private readonly ConcurrentDictionary<int, IntPtr> _cameraHandles = new();

    /// <summary>相机索引 → 数据库 CameraDevice.Id 映射（Host 启动后注入）</summary>
    private IReadOnlyDictionary<int, Guid> _deviceIdByCameraIndex = new Dictionary<int, Guid>();

    /// <summary>SDK是否已初始化</summary>
    private bool _initialized;

    /// <summary>是否已释放资源</summary>
    private bool _disposed;

    public TucamCameraService(
        ILogger<TucamCameraService> logger,
        IServiceScopeFactory? serviceScopeFactory = null
    )
    {
        _logger = logger;
        _serviceScopeFactory = serviceScopeFactory;
    }

    /// <inheritdoc/>
    public void SetCameraDeviceIdMapping(IReadOnlyDictionary<int, Guid> deviceIds)
    {
        _deviceIdByCameraIndex = deviceIds;
        _logger.LogInformation(
            "{Tag} Camera operation-log mapping injected, total {Count} camera indexes",
            LogTag,
            deviceIds.Count
        );
    }

    /// <inheritdoc/>
    public Task<int> InitializeAsync()
    {
        ThrowIfDisposed();

        var initParam = new TUCamInit { uiCamCount = 0, pstrConfigPath = IntPtr.Zero };

        var ret = TUCamNative.TUCAM_Api_Init(ref initParam, 1000);
        if (ret != TUCamRet.Success)
        {
            _logger.LogError(
                "{Tag} TUCam SDK initialization failed, return code: {RetCode}",
                LogTag,
                ret
            );
            throw new InvalidOperationException($"{LogTag} TUCam SDK initialization failed: {ret}");
        }

        _initialized = true;
        int count = (int)initParam.uiCamCount;
        _logger.LogInformation(
            "{Tag} TUCam SDK initialized successfully, detected {Count} camera(s)",
            LogTag,
            count
        );
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
            _logger.LogInformation("{Tag} Camera index {Index} closed", LogTag, kv.Key);
        }
        _cameraHandles.Clear();

        TUCamNative.TUCAM_Api_Uninit();
        _initialized = false;
        _logger.LogInformation("{Tag} TUCam SDK uninitialized", LogTag);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task OpenCameraAsync(int cameraIndex)
    {
        ThrowIfDisposed();
        ThrowIfNotInitialized();

        if (_cameraHandles.ContainsKey(cameraIndex))
        {
            _logger.LogWarning("{Tag} Camera index {Index} is already opened", LogTag, cameraIndex);
            return Task.CompletedTask;
        }

        Stopwatch sw = Stopwatch.StartNew();
        var openParam = new TUCamOpen { uiIdxOpen = (uint)cameraIndex, hIdxTUCam = IntPtr.Zero };

        var ret = TUCamNative.TUCAM_Dev_Open(ref openParam);
        sw.Stop();

        if (ret != TUCamRet.Success)
        {
            string errorMsg = $"{LogTag} Failed to open camera (index: {cameraIndex}): {ret}";
            _logger.LogError(
                "{Tag} Failed to open camera {Index}, return code: {RetCode}",
                LogTag,
                cameraIndex,
                ret
            );
            RecordCameraLog(
                cameraIndex,
                CameraOperationType.Open,
                false,
                sw.ElapsedMilliseconds,
                errorMsg
            );
            throw new InvalidOperationException(errorMsg);
        }

        _cameraHandles[cameraIndex] = openParam.hIdxTUCam;
        _logger.LogInformation(
            "{Tag} Camera {Index} opened successfully, handle: {Handle}",
            LogTag,
            cameraIndex,
            openParam.hIdxTUCam
        );
        RecordCameraLog(cameraIndex, CameraOperationType.Open, true, sw.ElapsedMilliseconds);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task CloseCameraAsync(int cameraIndex)
    {
        ThrowIfDisposed();

        if (!_cameraHandles.TryRemove(cameraIndex, out IntPtr handle))
        {
            _logger.LogWarning(
                "{Tag} Camera index {Index} is not opened or already closed",
                LogTag,
                cameraIndex
            );
            return Task.CompletedTask;
        }

        Stopwatch sw = Stopwatch.StartNew();
        var ret = TUCamNative.TUCAM_Dev_Close(handle);
        sw.Stop();

        if (ret != TUCamRet.Success)
        {
            string errorMsg = $"{LogTag} Close camera returned: {ret}";
            _logger.LogWarning(
                "{Tag} Closing camera {Index} returned: {RetCode}",
                LogTag,
                cameraIndex,
                ret
            );
            RecordCameraLog(
                cameraIndex,
                CameraOperationType.Close,
                false,
                sw.ElapsedMilliseconds,
                errorMsg
            );
        }
        else
        {
            _logger.LogInformation("{Tag} Camera {Index} closed", LogTag, cameraIndex);
            RecordCameraLog(cameraIndex, CameraOperationType.Close, true, sw.ElapsedMilliseconds);
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
                    "{Tag} Failed to get camera {Index} model, return code: {RetCode}",
                    LogTag,
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
                "{Tag} Failed to get camera {Index} property {Prop}, return code: {RetCode}",
                LogTag,
                cameraIndex,
                propId,
                ret
            );
            throw new InvalidOperationException(
                $"{LogTag} Failed to get camera property ({propId}): {ret}"
            );
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
                "{Tag} Failed to set camera {Index} property {Prop}={Value}, return code: {RetCode}",
                LogTag,
                cameraIndex,
                propId,
                value,
                ret
            );
            throw new InvalidOperationException(
                $"{LogTag} Failed to set camera property ({propId}={value}): {ret}"
            );
        }
        _logger.LogDebug(
            "{Tag} Camera {Index} property {Prop} set to {Value}",
            LogTag,
            cameraIndex,
            propId,
            value
        );
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
                "{Tag} Failed to get camera {Index} capability {Capa}, return code: {RetCode}",
                LogTag,
                cameraIndex,
                capaId,
                ret
            );
            throw new InvalidOperationException(
                $"{LogTag} Failed to get camera capability ({capaId}): {ret}"
            );
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
                "{Tag} Failed to set camera {Index} capability {Capa}={Value}, return code: {RetCode}",
                LogTag,
                cameraIndex,
                capaId,
                value,
                ret
            );
            throw new InvalidOperationException(
                $"{LogTag} Failed to set camera capability ({capaId}={value}): {ret}"
            );
        }
        _logger.LogDebug(
            "{Tag} Camera {Index} capability {Capa} set to {Value}",
            LogTag,
            cameraIndex,
            capaId,
            value
        );
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task StartCaptureAsync(int cameraIndex)
    {
        ThrowIfDisposed();
        IntPtr handle = GetHandle(cameraIndex);

        Stopwatch sw = Stopwatch.StartNew();

        // 先分配帧缓冲区
        var frame = new TUCamFrame { uiRsdSize = 1 };
        var allocRet = TUCamNative.TUCAM_Buf_Alloc(handle, ref frame);
        if (allocRet != TUCamRet.Success)
        {
            sw.Stop();
            string errorMsg = $"{LogTag} Failed to allocate frame buffer: {allocRet}";
            RecordCameraLog(
                cameraIndex,
                CameraOperationType.StartCapture,
                false,
                sw.ElapsedMilliseconds,
                errorMsg
            );
            throw new InvalidOperationException(errorMsg);
        }

        var ret = TUCamNative.TUCAM_Cap_Start(handle, (uint)TUCamCaptureMode.Sequence);
        sw.Stop();

        if (ret != TUCamRet.Success)
        {
            TUCamNative.TUCAM_Buf_Release(handle);
            string errorMsg = $"{LogTag} Failed to start capture: {ret}";
            RecordCameraLog(
                cameraIndex,
                CameraOperationType.StartCapture,
                false,
                sw.ElapsedMilliseconds,
                errorMsg
            );
            throw new InvalidOperationException(errorMsg);
        }

        _logger.LogInformation(
            "{Tag} Camera {Index} started continuous capture",
            LogTag,
            cameraIndex
        );
        RecordCameraLog(
            cameraIndex,
            CameraOperationType.StartCapture,
            true,
            sw.ElapsedMilliseconds
        );
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task StopCaptureAsync(int cameraIndex)
    {
        ThrowIfDisposed();
        IntPtr handle = GetHandle(cameraIndex);

        Stopwatch sw = Stopwatch.StartNew();
        TUCamNative.TUCAM_Buf_AbortWait(handle);
        TUCamNative.TUCAM_Cap_Stop(handle);
        TUCamNative.TUCAM_Buf_Release(handle);
        sw.Stop();

        _logger.LogInformation("{Tag} Camera {Index} stopped capture", LogTag, cameraIndex);
        RecordCameraLog(cameraIndex, CameraOperationType.StopCapture, true, sw.ElapsedMilliseconds);
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
            throw new InvalidOperationException($"{LogTag} Wait frame timeout or failed: {ret}");
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

    // ─── ROI 区域控制 ────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public Task<TUCamRoiAttr> GetRoiAsync(int cameraIndex)
    {
        ThrowIfDisposed();
        IntPtr handle = GetHandle(cameraIndex);

        TUCamRoiAttr roi = default;
        TUCamRet ret = TUCamNative.TUCAM_Cap_GetROI(handle, ref roi);
        if (ret != TUCamRet.Success)
        {
            throw new InvalidOperationException($"{LogTag} Failed to get ROI: {ret}");
        }
        return Task.FromResult(roi);
    }

    /// <inheritdoc/>
    public Task SetRoiAsync(int cameraIndex, TUCamRoiAttr roi)
    {
        ThrowIfDisposed();
        IntPtr handle = GetHandle(cameraIndex);

        TUCamRet ret = TUCamNative.TUCAM_Cap_SetROI(handle, roi);
        if (ret != TUCamRet.Success)
        {
            throw new InvalidOperationException($"{LogTag} Failed to set ROI: {ret}");
        }
        _logger.LogDebug("{Tag} Camera {Index} ROI set", LogTag, cameraIndex);
        return Task.CompletedTask;
    }

    // ─── 触发模式 ─────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public Task<TUCamTriggerAttr> GetTriggerAsync(int cameraIndex)
    {
        ThrowIfDisposed();
        IntPtr handle = GetHandle(cameraIndex);

        TUCamTriggerAttr trigger = default;
        TUCamRet ret = TUCamNative.TUCAM_Cap_GetTrigger(handle, ref trigger);
        if (ret != TUCamRet.Success)
        {
            throw new InvalidOperationException($"{LogTag} Failed to get trigger: {ret}");
        }
        return Task.FromResult(trigger);
    }

    /// <inheritdoc/>
    public Task SetTriggerAsync(int cameraIndex, TUCamTriggerAttr trigger)
    {
        ThrowIfDisposed();
        IntPtr handle = GetHandle(cameraIndex);

        TUCamRet ret = TUCamNative.TUCAM_Cap_SetTrigger(handle, trigger);
        if (ret != TUCamRet.Success)
        {
            throw new InvalidOperationException($"{LogTag} Failed to set trigger: {ret}");
        }
        _logger.LogDebug("{Tag} Camera {Index} trigger mode set", LogTag, cameraIndex);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task DoSoftwareTriggerAsync(int cameraIndex)
    {
        ThrowIfDisposed();
        IntPtr handle = GetHandle(cameraIndex);

        TUCamRet ret = TUCamNative.TUCAM_Cap_DoSoftwareTrigger(handle);
        if (ret != TUCamRet.Success)
        {
            throw new InvalidOperationException($"{LogTag} Software trigger failed: {ret}");
        }
        _logger.LogDebug("{Tag} Camera {Index} software trigger sent", LogTag, cameraIndex);
        return Task.CompletedTask;
    }

    // ─── 触发输出 ─────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public Task<TUCamTrgOutAttr> GetTriggerOutAsync(int cameraIndex, int port)
    {
        ThrowIfDisposed();
        IntPtr handle = GetHandle(cameraIndex);

        TUCamTrgOutAttr trgOut = new TUCamTrgOutAttr { nTgrOutPort = port };
        TUCamRet ret = TUCamNative.TUCAM_Cap_GetTriggerOut(handle, ref trgOut);
        if (ret != TUCamRet.Success)
        {
            throw new InvalidOperationException(
                $"{LogTag} Failed to get trigger output (port={port}): {ret}"
            );
        }
        return Task.FromResult(trgOut);
    }

    /// <inheritdoc/>
    public Task SetTriggerOutAsync(int cameraIndex, TUCamTrgOutAttr trgOut)
    {
        ThrowIfDisposed();
        IntPtr handle = GetHandle(cameraIndex);

        TUCamRet ret = TUCamNative.TUCAM_Cap_SetTriggerOut(handle, trgOut);
        if (ret != TUCamRet.Success)
        {
            throw new InvalidOperationException($"{LogTag} Failed to set trigger output: {ret}");
        }
        _logger.LogDebug(
            "{Tag} Camera {Index} trigger output port {Port} set",
            LogTag,
            cameraIndex,
            trgOut.nTgrOutPort
        );
        return Task.CompletedTask;
    }

    // ─── 计算 ROI（AE/WB 测光区域）──────────────────────────────────────────

    /// <inheritdoc/>
    public Task<TUCamCalcRoiAttr> GetCalcRoiAsync(int cameraIndex, TUCamIdCalcRoi calcId)
    {
        ThrowIfDisposed();
        IntPtr handle = GetHandle(cameraIndex);

        TUCamCalcRoiAttr roi = new TUCamCalcRoiAttr { idCalc = (int)calcId };
        TUCamRet ret = TUCamNative.TUCAM_Calc_GetROI(handle, ref roi);
        if (ret != TUCamRet.Success)
        {
            throw new InvalidOperationException($"{LogTag} Failed to get calc ROI: {ret}");
        }
        return Task.FromResult(roi);
    }

    /// <inheritdoc/>
    public Task SetCalcRoiAsync(int cameraIndex, TUCamCalcRoiAttr calcRoi)
    {
        ThrowIfDisposed();
        IntPtr handle = GetHandle(cameraIndex);

        TUCamRet ret = TUCamNative.TUCAM_Calc_SetROI(handle, calcRoi);
        if (ret != TUCamRet.Success)
        {
            throw new InvalidOperationException($"{LogTag} Failed to set calc ROI: {ret}");
        }
        _logger.LogDebug("{Tag} Camera {Index} calc ROI set", LogTag, cameraIndex);
        return Task.CompletedTask;
    }

    // ─── 用户配置文件 ─────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public Task LoadProfilesAsync(int cameraIndex, string profileName)
    {
        ThrowIfDisposed();
        IntPtr handle = GetHandle(cameraIndex);

        TUCamRet ret = TUCamNative.TUCAM_File_LoadProfiles(handle, profileName);
        if (ret != TUCamRet.Success)
        {
            throw new InvalidOperationException(
                $"{LogTag} Failed to load profiles '{profileName}': {ret}"
            );
        }
        _logger.LogInformation(
            "{Tag} Camera {Index} loaded profile '{Profile}'",
            LogTag,
            cameraIndex,
            profileName
        );
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task SaveProfilesAsync(int cameraIndex, string profileName)
    {
        ThrowIfDisposed();
        IntPtr handle = GetHandle(cameraIndex);

        TUCamRet ret = TUCamNative.TUCAM_File_SaveProfiles(handle, profileName);
        if (ret != TUCamRet.Success)
        {
            throw new InvalidOperationException(
                $"{LogTag} Failed to save profiles '{profileName}': {ret}"
            );
        }
        _logger.LogInformation(
            "{Tag} Camera {Index} saved profile '{Profile}'",
            LogTag,
            cameraIndex,
            profileName
        );
        return Task.CompletedTask;
    }

    // ─── 设备信息查询（数值型）────────────────────────────────────────────────

    /// <inheritdoc/>
    public Task<int> GetDeviceNumericInfoAsync(int cameraIndex, TUCamIdInfo infoId)
    {
        ThrowIfDisposed();

        // 整型信息通过 GetInfoEx 查询（使用相机索引，不使用句柄）
        var info = new TUCamValueInfo
        {
            nId = (int)infoId,
            pText = IntPtr.Zero,
            nTextSize = 0,
        };

        TUCamRet ret = TUCamNative.TUCAM_Dev_GetInfoEx((uint)cameraIndex, ref info);
        if (ret != TUCamRet.Success)
        {
            throw new InvalidOperationException(
                $"{LogTag} Failed to get device info ({infoId}): {ret}"
            );
        }
        return Task.FromResult(info.nValue);
    }

    // ─── 属性/能力元数据 ──────────────────────────────────────────────────────

    /// <inheritdoc/>
    public Task<TUCamPropAttr> GetPropertyAttrAsync(int cameraIndex, TUCamIdProp propId)
    {
        ThrowIfDisposed();
        IntPtr handle = GetHandle(cameraIndex);

        TUCamPropAttr attr = new TUCamPropAttr { idProp = (int)propId };
        TUCamRet ret = TUCamNative.TUCAM_Prop_GetAttr(handle, ref attr);
        if (ret != TUCamRet.Success)
        {
            throw new InvalidOperationException(
                $"{LogTag} Failed to get property attr ({propId}): {ret}"
            );
        }
        return Task.FromResult(attr);
    }

    /// <inheritdoc/>
    public Task<TUCamCapaAttr> GetCapabilityAttrAsync(int cameraIndex, TUCamIdCapa capaId)
    {
        ThrowIfDisposed();
        IntPtr handle = GetHandle(cameraIndex);

        TUCamCapaAttr attr = new TUCamCapaAttr { idCapa = (int)capaId };
        TUCamRet ret = TUCamNative.TUCAM_Capa_GetAttr(handle, ref attr);
        if (ret != TUCamRet.Success)
        {
            throw new InvalidOperationException(
                $"{LogTag} Failed to get capability attr ({capaId}): {ret}"
            );
        }
        return Task.FromResult(attr);
    }

    // ─── 原始帧抓取（用于单帧快照 / RTP 推流）────────────────────────────────

    /// <inheritdoc/>
    public Task<byte[]> GrabFrameRawAsync(int cameraIndex, int timeoutMs = 3000)
    {
        ThrowIfDisposed();
        IntPtr handle = GetHandle(cameraIndex);

        // 等待一帧就绪
        TUCamFrame frame = new TUCamFrame { uiRsdSize = 1 };
        TUCamRet ret = TUCamNative.TUCAM_Buf_WaitForFrame(handle, ref frame, timeoutMs);
        if (ret != TUCamRet.Success)
        {
            throw new InvalidOperationException(
                $"{LogTag} WaitForFrame failed (index={cameraIndex}): {ret}"
            );
        }

        int width = frame.usWidth;
        int height = frame.usHeight;
        int bitDepth = frame.ucDepth;
        int channels = frame.ucChannels;
        int dataSize = (int)frame.uiImgSize;

        // 将帧数据拷贝到托管数组
        byte[] rawData = new byte[dataSize];
        if (frame.pBuffer != IntPtr.Zero && dataSize > 0)
        {
            Marshal.Copy(frame.pBuffer + frame.usOffset, rawData, 0, dataSize);
        }

        // 编码为 JPEG
        byte[] jpegBytes = EncodeToJpeg(rawData, width, height, bitDepth, channels);
        return Task.FromResult(jpegBytes);
    }

    /// <summary>
    /// 将原始像素数据编码为 JPEG 字节数组（使用 SkiaSharp）
    /// </summary>
    /// <param name="rawData">原始像素字节数组</param>
    /// <param name="width">图像宽度</param>
    /// <param name="height">图像高度</param>
    /// <param name="bitDepth">位深度（8 或 12）</param>
    /// <param name="channels">通道数（1=灰度, 3=彩色）</param>
    private static byte[] EncodeToJpeg(
        byte[] rawData,
        int width,
        int height,
        int bitDepth,
        int channels
    )
    {
        // 确定 SkiaSharp 颜色类型
        SKColorType colorType = channels >= 3 ? SKColorType.Rgb888x : SKColorType.Gray8;

        byte[] pixelData;
        if (bitDepth <= 8)
        {
            // 8位直接使用（灰度单通道）
            pixelData = rawData;
        }
        else
        {
            // 12/16位转换为8位（右移取高8位）
            int pixelCount = width * height * (channels >= 3 ? 3 : 1);
            pixelData = new byte[pixelCount];
            int shift = bitDepth - 8;
            for (int i = 0, j = 0; i < pixelData.Length && j + 1 < rawData.Length; i++, j += 2)
            {
                int raw16 = rawData[j] | (rawData[j + 1] << 8);
                pixelData[i] = (byte)(raw16 >> shift);
            }
        }

        // 使用 SkiaSharp 编码为 JPEG
        using SKBitmap bitmap = new SKBitmap(width, height, colorType, SKAlphaType.Opaque);
        unsafe
        {
            fixed (byte* ptr = pixelData)
            {
                bitmap.SetPixels((IntPtr)ptr);
            }
        }

        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData encoded = image.Encode(SKEncodedImageFormat.Jpeg, 80);
        return encoded.ToArray();
    }

    /// <summary>
    /// 记录相机操作日志（fire-and-forget，失败仅记录警告不抛出）
    /// </summary>
    private void RecordCameraLog(
        int cameraIndex,
        CameraOperationType operationType,
        bool isSuccess,
        long roundTripMs,
        string? errorMessage = null,
        string? parameterSummary = null
    )
    {
        if (_serviceScopeFactory is null)
            return;

        if (!_deviceIdByCameraIndex.TryGetValue(cameraIndex, out Guid cameraDeviceId))
            return;

        _ = Task.Run(async () =>
        {
            try
            {
                using IServiceScope scope = _serviceScopeFactory.CreateScope();
                ICameraOperationLogRepository repo =
                    scope.ServiceProvider.GetRequiredService<ICameraOperationLogRepository>();

                CameraOperationLog log = isSuccess
                    ? CameraOperationLog.Success(
                        Guid.NewGuid(),
                        cameraDeviceId,
                        cameraIndex,
                        operationType,
                        roundTripMs,
                        parameterSummary
                    )
                    : CameraOperationLog.Failure(
                        Guid.NewGuid(),
                        cameraDeviceId,
                        cameraIndex,
                        operationType,
                        errorMessage ?? string.Empty,
                        roundTripMs,
                        parameterSummary
                    );

                await repo.InsertAsync(log, autoSave: true);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "{Tag} Failed to write camera operation log (CameraIndex={Index}, Operation={Op})",
                    LogTag,
                    cameraIndex,
                    operationType
                );
            }
        });
    }

    /// <summary>
    /// 获取相机句柄，不存在则抛出异常
    /// </summary>
    private IntPtr GetHandle(int cameraIndex)
    {
        if (!_cameraHandles.TryGetValue(cameraIndex, out IntPtr handle))
        {
            throw new InvalidOperationException(
                $"{LogTag} Camera (index: {cameraIndex}) is not opened. Call OpenCameraAsync first."
            );
        }
        return handle;
    }

    private void ThrowIfNotInitialized()
    {
        if (!_initialized)
        {
            throw new InvalidOperationException(
                $"{LogTag} TUCam SDK is not initialized. Call InitializeAsync first."
            );
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
