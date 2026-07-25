using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using AuroraStruct3D.Cameras;
using AuroraStruct3D.Tucam.GenICam;
using AuroraStruct3D.Tucam.Interop;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SkiaSharp;
using Volo.Abp.Uow;

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

    /// <summary>相机采集状态字典，key为相机索引</summary>
    private readonly ConcurrentDictionary<int, CameraCaptureState> _captureStates = new();

    /// <summary>GenICam NodeMap 与依赖图缓存，key 为相机索引</summary>
    private readonly ConcurrentDictionary<
        int,
        (GenICamNodeMap NodeMap, GenICamDependencyGraph Graph)
    > _nodeMapCache = new();

    /// <summary>
    /// 进程级 Tucam SDK 全局互斥锁。
    /// Tucam SDK 内部存在全局共享状态，即使操作不同相机句柄，并发调用也会导致堆损坏（SIGSEGV / double free）。
    /// 所有 GenICam 枚举与依赖探测必须通过此锁串行执行。
    /// </summary>
    private static readonly SemaphoreSlim _globalSdkLock = new(1, 1);

    /// <summary>
    /// 进程级单活采集锁：同一时刻仅允许一台相机处于 Cap_Start 活跃状态。
    /// TUCam SDK 在 RK3588 USB 环境下不支持多相机并发 Cap_Start，USB 带宽竞争会导致采集失败。
    /// 第二台相机的 StartCaptureAsync 会阻塞等待（最长 30s），直到前一台调用 StopCaptureAsync 后释放。
    /// </summary>
    private static readonly SemaphoreSlim _capStartActiveLock = new(1, 1);

    /// <summary>GenICam 节点探测/枚举互斥锁，避免同一相机并发探测污染选择器状态</summary>
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _nodeMapProbeLocks = new();

    /// <summary>每台相机的 NodeMap 预热 Task，用于在开始采集前等待其完成，避免并发 SDK 调用导致堆损坏</summary>
    private readonly ConcurrentDictionary<int, Task> _prewarmTasks = new();

    /// <summary>相机索引 → 数据库 CameraDevice.Id 映射（Host 启动后注入）</summary>
    private IReadOnlyDictionary<int, Guid> _deviceIdByCameraIndex = new Dictionary<int, Guid>();

    /// <summary>SDK是否已初始化</summary>
    private bool _initialized;

    /// <summary>最近一次 SDK 扫描到的相机数量</summary>
    private int _lastCameraCount;

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

        if (_initialized && !_cameraHandles.IsEmpty)
        {
            _logger.LogInformation(
                "{Tag} SDK already initialized and {OpenedCount} camera(s) opened, skip re-initialization",
                LogTag,
                _cameraHandles.Count
            );
            return Task.FromResult(_lastCameraCount);
        }

        // 若 SDK 已初始化且没有打开相机，先反初始化以确保能重新扫描到设备
        if (_initialized)
        {
            TUCamNative.TUCAM_Api_Uninit();
            _initialized = false;
            _lastCameraCount = 0;
            _logger.LogInformation("{Tag} SDK pre-uninit before re-initialization", LogTag);
        }

        var initParam = new TUCamInit { uiCamCount = 0, pstrConfigPath = IntPtr.Zero };

        TUCamRet ret;
        try
        {
            ret = TUCamNative.TUCAM_Api_Init(ref initParam, 1000);
        }
        catch (DllNotFoundException ex)
        {
            _logger.LogWarning(
                ex,
                "{Tag} TUCam.dll not found, camera subsystem disabled (SDK not installed)",
                LogTag
            );
            _lastCameraCount = 0;
            return Task.FromResult(0);
        }

        if (ret == TUCamRet.Init)
        {
            // SDK 已加载但无法初始化——通常是相机驱动未安装或无相机连接
            // 降级处理：记录警告，返回 0 台相机，不抛出异常
            _logger.LogWarning(
                "{Tag} TUCam SDK returned Init error (camera driver not installed or no camera connected)",
                LogTag
            );
            _lastCameraCount = 0;
            return Task.FromResult(0);
        }

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
        _lastCameraCount = count;
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

        foreach (int cameraIndex in _cameraHandles.Keys.ToList())
        {
            if (IsCapturing(cameraIndex))
            {
                StopCaptureAsync(cameraIndex).GetAwaiter().GetResult();
            }
        }

        // 关闭所有已打开的相机
        foreach (var kv in _cameraHandles)
        {
            TUCamNative.TUCAM_Dev_Close(kv.Value);
            _logger.LogInformation("{Tag} Camera index {Index} closed", LogTag, kv.Key);
        }
        _cameraHandles.Clear();
        _captureStates.Clear();

        TUCamNative.TUCAM_Api_Uninit();
        _initialized = false;
        _lastCameraCount = 0;
        _logger.LogInformation("{Tag} TUCam SDK uninitialized", LogTag);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task OpenCameraAsync(int cameraIndex)
    {
        ThrowIfDisposed();
        ThrowIfNotInitialized();

        // 使用全局 SDK 锁：
        // 1. 将 ContainsKey 检查与 TUCAM_Dev_Open 置于同一临界区，避免并发请求双双调用 SDK
        // 2. TUCAM_Dev_Open 本身是 SDK 调用，不能与 Probe/Enumerate 并发执行
        _globalSdkLock.Wait();
        try
        {
            if (_cameraHandles.ContainsKey(cameraIndex))
            {
                _logger.LogWarning(
                    "{Tag} Camera index {Index} is already opened",
                    LogTag,
                    cameraIndex
                );
                return Task.CompletedTask;
            }

            Stopwatch sw = Stopwatch.StartNew();
            var openParam = new TUCamOpen
            {
                uiIdxOpen = (uint)cameraIndex,
                hIdxTUCam = IntPtr.Zero,
            };
            TUCamRet ret = TUCamRet.Failure;
            int[] retryDelaysMs = { 0, 200, 500, 1000, 2000 };
            for (int attempt = 0; attempt < retryDelaysMs.Length; attempt++)
            {
                if (retryDelaysMs[attempt] > 0)
                {
                    Thread.Sleep(retryDelaysMs[attempt]);
                }

                openParam = new TUCamOpen
                {
                    uiIdxOpen = (uint)cameraIndex,
                    hIdxTUCam = IntPtr.Zero,
                };
                ret = TUCamNative.TUCAM_Dev_Open(ref openParam);
                if (ret == TUCamRet.Success)
                {
                    if (attempt > 0)
                    {
                        _logger.LogInformation(
                            "{Tag} Camera {Index} Dev_Open 在第 {Attempt} 次重试后成功",
                            LogTag,
                            cameraIndex,
                            attempt + 1
                        );
                    }
                    break;
                }

                if (ret != TUCamRet.FailOpenCamera)
                {
                    break;
                }
            }

            if (ret == TUCamRet.FailOpenCamera && _cameraHandles.IsEmpty)
            {
                _logger.LogWarning(
                    "{Tag} Camera {Index} Dev_Open 持续返回 FailOpenCamera，尝试重置 SDK 后再打开",
                    LogTag,
                    cameraIndex
                );

                if (TryReinitializeSdkCore($"open camera {cameraIndex} recovery"))
                {
                    openParam = new TUCamOpen
                    {
                        uiIdxOpen = (uint)cameraIndex,
                        hIdxTUCam = IntPtr.Zero,
                    };
                    ret = TUCamNative.TUCAM_Dev_Open(ref openParam);
                }
            }

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
        }
        finally
        {
            _globalSdkLock.Release();
        }

        // 后台异步预跑 NodeMap 枚举 + 选择器依赖探测，结果写入缓存
        // 注意：保存 Task 引用，StartCaptureAsync 会等待其完成以避免并发 SDK 调用导致堆损坏
        // 注意：预热任务在 _globalSdkLock 释放后再启动，避免长时间持锁
        IntPtr newHandle = _cameraHandles.TryGetValue(cameraIndex, out IntPtr h) ? h : IntPtr.Zero;
        if (newHandle != IntPtr.Zero)
        {
            _prewarmTasks[cameraIndex] = Task.Run(() =>
                PrewarmGenICamNodeMapAsync(cameraIndex, newHandle)
            );
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task CloseCameraAsync(int cameraIndex)
    {
        ThrowIfDisposed();

        if (IsCapturing(cameraIndex))
        {
            try
            {
                StopCaptureAsync(cameraIndex).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "{Tag} Camera {Index} stop capture before close failed, continue closing",
                    LogTag,
                    cameraIndex
                );
            }
        }

        WaitForPrewarmCompletion(cameraIndex, "close camera");

        _globalSdkLock.Wait();
        try
        {
            if (!_cameraHandles.TryRemove(cameraIndex, out IntPtr handle))
            {
                _logger.LogWarning(
                    "{Tag} Camera index {Index} is not opened or already closed",
                    LogTag,
                    cameraIndex
                );
                return Task.CompletedTask;
            }
            _captureStates.TryRemove(cameraIndex, out _);
            _nodeMapCache.TryRemove(cameraIndex, out _);
            _prewarmTasks.TryRemove(cameraIndex, out _);

            Stopwatch sw = Stopwatch.StartNew();
            TUCamRet ret = TUCamNative.TUCAM_Dev_Close(handle);
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
                RecordCameraLog(
                    cameraIndex,
                    CameraOperationType.Close,
                    true,
                    sw.ElapsedMilliseconds
                );
            }

            // TUCam SDK 特性：关闭最后一台相机后必须重新初始化，否则下次 Open 会返回 FailOpenCamera
            if (_cameraHandles.IsEmpty)
            {
                TryReinitializeSdkCore($"close last camera {cameraIndex}");
            }
        }
        finally
        {
            _globalSdkLock.Release();
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 关闭句柄前等待后台 GenICam 预热结束，避免 SDK 在已关闭句柄上继续枚举导致 native 异常。
    /// </summary>
    private void WaitForPrewarmCompletion(int cameraIndex, string operation, int timeoutMs = 5000)
    {
        if (
            !_prewarmTasks.TryGetValue(cameraIndex, out Task? prewarmTask)
            || prewarmTask.IsCompleted
        )
        {
            return;
        }

        _logger.LogInformation(
            "{Tag} Camera {Index} 等待 GenICam 预热结束后再执行 {Operation}",
            LogTag,
            cameraIndex,
            operation
        );

        try
        {
            if (!prewarmTask.Wait(timeoutMs))
            {
                _logger.LogWarning(
                    "{Tag} Camera {Index} GenICam 预热在 {Timeout}ms 内未结束，继续执行 {Operation}",
                    LogTag,
                    cameraIndex,
                    timeoutMs,
                    operation
                );
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(
                ex,
                "{Tag} Camera {Index} 等待 GenICam 预热完成时发生异常，继续执行 {Operation}",
                LogTag,
                cameraIndex,
                operation
            );
        }
    }

    /// <summary>
    /// 在已持有全局 SDK 锁的前提下重置 SDK，供关闭最后一台相机或 Open 恢复路径复用。
    /// </summary>
    private bool TryReinitializeSdkCore(string reason)
    {
        if (!_initialized)
        {
            return false;
        }

        TUCamNative.TUCAM_Api_Uninit();
        _initialized = false;
        _lastCameraCount = 0;

        var reinitParam = new TUCamInit { uiCamCount = 0, pstrConfigPath = IntPtr.Zero };
        TUCamRet reinitRet = TUCamNative.TUCAM_Api_Init(ref reinitParam, 1000);
        if (reinitRet == TUCamRet.Success)
        {
            _initialized = true;
            _lastCameraCount = (int)reinitParam.uiCamCount;
            _logger.LogInformation(
                "{Tag} SDK re-initialized for {Reason}, detected {Count} camera(s)",
                LogTag,
                reason,
                reinitParam.uiCamCount
            );
            return true;
        }

        _logger.LogWarning(
            "{Tag} SDK re-initialization for {Reason} returned: {Ret}",
            LogTag,
            reason,
            reinitRet
        );
        return false;
    }

    /// <inheritdoc/>
    public Task<string> GetCameraModelAsync(int cameraIndex)
    {
        ThrowIfDisposed();
        IntPtr handle = GetHandle(cameraIndex);

        // TUCAM_Dev_GetInfo(CameraModel) 在部分固件下返回空字符串，
        // 改用 GenICam DeviceModelName 节点（ElementAttr 方式读取更可靠）
        return Task.FromResult(
            GenICamGetString(handle, "DeviceModelName")
                ?? ReadDeviceString(handle, TUCamIdInfo.CameraModel, cameraIndex)
        );
    }

    /// <inheritdoc/>
    public Task<string> GetModelByIndexAsync(int cameraIndex)
    {
        ThrowIfDisposed();

        // 使用 TUCAM_Dev_GetInfoEx 按索引读取，无需打开相机
        // 申请并零初始化缓冲区，防止读到未初始化内存
        byte[] buffer = new byte[256];
        GCHandle pin = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            IntPtr textBuffer = pin.AddrOfPinnedObject();
            var info = new TUCamValueInfo
            {
                nId = (int)TUCamIdInfo.CameraModel,
                pText = textBuffer,
                nTextSize = 256,
            };

            TUCamRet ret = TUCamNative.TUCAM_Dev_GetInfoEx((uint)cameraIndex, ref info);
            if (ret != TUCamRet.Success)
            {
                _logger.LogWarning(
                    "{Tag} TUCAM_Dev_GetInfoEx returned {Ret} for camera index {Index}",
                    LogTag,
                    ret,
                    cameraIndex
                );
                return Task.FromResult(string.Empty);
            }

            // 记录原始字节，便于诊断编码问题
            string hexDump = string.Join(" ", buffer.Take(16).Select(b => b.ToString("X2")));
            _logger.LogDebug(
                "{Tag} Camera {Index} model raw bytes (hex): {Hex}",
                LogTag,
                cameraIndex,
                hexDump
            );

            // 优先通过 Marshal.PtrToStringAnsi 读取：使用系统 ANSI 代码页（中文 Windows 为 GBK），
            // 且在 SDK 修改了 pText 指针指向内部字符串时也能正确读取
            string model = string.Empty;
            if (info.pText != IntPtr.Zero)
            {
                model = Marshal.PtrToStringAnsi(info.pText) ?? string.Empty;
            }

            // 若 ANSI 读取结果为空，则回退到字节缓冲区解码
            if (string.IsNullOrEmpty(model))
            {
                model = DecodeNullTerminated(buffer);
            }

            _logger.LogDebug(
                "{Tag} Camera {Index} model decoded: '{Model}'",
                LogTag,
                cameraIndex,
                model
            );
            return Task.FromResult(model.Trim());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "{Tag} Failed to read model for camera index {Index}",
                LogTag,
                cameraIndex
            );
            return Task.FromResult(string.Empty);
        }
        finally
        {
            pin.Free();
        }
    }

    /// <summary>
    /// 通过相机句柄读取指定 ID 的字符串信息
    /// </summary>
    private string ReadDeviceString(IntPtr handle, TUCamIdInfo infoId, int cameraIndex)
    {
        byte[] buffer = new byte[256];
        GCHandle pin = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            IntPtr textBuffer = pin.AddrOfPinnedObject();
            var info = new TUCamValueInfo
            {
                nId = (int)infoId,
                pText = textBuffer,
                nTextSize = 256,
            };

            TUCamRet ret = TUCamNative.TUCAM_Dev_GetInfo(handle, ref info);
            if (ret != TUCamRet.Success)
            {
                _logger.LogWarning(
                    "{Tag} TUCAM_Dev_GetInfo({InfoId}) returned {Ret} for camera {Index}",
                    LogTag,
                    infoId,
                    ret,
                    cameraIndex
                );
                return string.Empty;
            }

            return DecodeNullTerminated(buffer);
        }
        finally
        {
            pin.Free();
        }
    }

    /// <summary>
    /// 将以 null 结尾的字节数组解码为字符串。
    /// 优先尝试 UTF-8，若结果包含替换字符则通过 Marshal.PtrToStringAnsi 方式
    /// 回退到系统 ANSI 代码页（中文 Windows 为 GBK）。
    /// </summary>
    private static string DecodeNullTerminated(byte[] buffer)
    {
        int length = Array.IndexOf(buffer, (byte)0);
        if (length <= 0)
        {
            return string.Empty;
        }

        // 先尝试 UTF-8
        string utf8Result = System.Text.Encoding.UTF8.GetString(buffer, 0, length);
        if (!utf8Result.Contains('\uFFFD'))
        {
            return utf8Result;
        }

        // 回退到 Latin-1（ISO-8859-1）：对字节做 1:1 映射，保留原始字节值，
        // 避免 .NET 5+ Encoding.Default=UTF-8 导致的再次解码失败
        return System.Text.Encoding.Latin1.GetString(buffer, 0, length);
    }

    /// <inheritdoc/>
    public Task<double> GetPropertyValueAsync(int cameraIndex, TUCamIdProp propId, int channel = 0)
    {
        ThrowIfDisposed();
        IntPtr handle = GetHandle(cameraIndex);

        // GenICam 相机通过节点名称读取属性值
        switch (propId)
        {
            case TUCamIdProp.ExposureTime:
            {
                long? val = GenICamGetInt(handle, "ExposureTime");
                return Task.FromResult((double)(val ?? 0));
            }
            case TUCamIdProp.Gamma:
            {
                long? val = GenICamGetInt(handle, "Gamma");
                return Task.FromResult((double)(val ?? 0));
            }
            case TUCamIdProp.Contrast:
            {
                long? val = GenICamGetInt(handle, "Contrast");
                return Task.FromResult((double)(val ?? 0));
            }
            case TUCamIdProp.Brightness:
            {
                long? val = GenICamGetInt(handle, "Bright");
                return Task.FromResult((double)(val ?? 0));
            }
            case TUCamIdProp.Saturation:
            {
                long? val = GenICamGetInt(handle, "SaturationControl");
                return Task.FromResult((double)(val ?? 0));
            }
            case TUCamIdProp.FrameRate:
            {
                double? val = GenICamGetFloat(handle, "AcquisitionFrameRate");
                return Task.FromResult(val ?? 0.0);
            }
            case TUCamIdProp.AverageGray:
            {
                double? val = GenICamGetFloat(handle, "AutoTargetGray");
                return Task.FromResult(val ?? 0.0);
            }
            case TUCamIdProp.ExposureMax:
            {
                long? val = GenICamGetInt(handle, "ExposureAutoMaxTime");
                return Task.FromResult((double)(val ?? 0));
            }
            case TUCamIdProp.ExposureMin:
            {
                long? val = GenICamGetInt(handle, "ExposureAutoMinTime");
                return Task.FromResult((double)(val ?? 0));
            }
            case TUCamIdProp.ChannelGain:
            {
                // channel: 1=Red, 2=Green, 3=Blue → BalanceRatioSelector: 0=Red, 1=Green, 2=Blue
                int selector = channel - 1;
                if (selector >= 0 && selector <= 2)
                {
                    GenICamSetInt(handle, "BalanceRatioSelector", selector);
                    long? val = GenICamGetInt(handle, "BalanceRatio");
                    return Task.FromResult((double)(val ?? 0));
                }
                return Task.FromResult(0.0);
            }
            default:
                _logger.LogWarning(
                    "{Tag} GetPropertyValue: {Prop} 无对应 GenICam 节点，返回0",
                    LogTag,
                    propId
                );
                return Task.FromResult(0.0);
        }
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

        // GenICam 相机通过节点名称写入属性值
        TUCamRet ret;
        switch (propId)
        {
            case TUCamIdProp.ExposureTime:
                ret = GenICamSetInt(handle, "ExposureTime", (long)value);
                break;
            case TUCamIdProp.Gamma:
                ret = GenICamSetInt(handle, "Gamma", (long)value);
                break;
            case TUCamIdProp.Contrast:
                ret = GenICamSetInt(handle, "Contrast", (long)value);
                break;
            case TUCamIdProp.Brightness:
                ret = GenICamSetInt(handle, "Bright", (long)value);
                break;
            case TUCamIdProp.Saturation:
                ret = GenICamSetInt(handle, "SaturationControl", (long)value);
                break;
            case TUCamIdProp.FrameRate:
                ret = GenICamSetFloat(handle, "AcquisitionFrameRate", value);
                break;
            case TUCamIdProp.AverageGray:
                ret = GenICamSetFloat(handle, "AutoTargetGray", value);
                break;
            case TUCamIdProp.ExposureMax:
                ret = GenICamSetInt(handle, "ExposureAutoMaxTime", (long)value);
                break;
            case TUCamIdProp.ExposureMin:
                ret = GenICamSetInt(handle, "ExposureAutoMinTime", (long)value);
                break;
            case TUCamIdProp.ChannelGain:
            {
                // channel: 1=Red, 2=Green, 3=Blue → BalanceRatioSelector: 0=Red, 1=Green, 2=Blue
                int selector = channel - 1;
                if (selector >= 0 && selector <= 2)
                {
                    GenICamSetInt(handle, "BalanceRatioSelector", selector);
                    ret = GenICamSetInt(handle, "BalanceRatio", (long)value);
                }
                else
                {
                    ret = TUCamRet.Success;
                }
                break;
            }
            default:
                _logger.LogWarning(
                    "{Tag} SetPropertyValue: {Prop} 无对应 GenICam 节点，跳过",
                    LogTag,
                    propId
                );
                return Task.CompletedTask;
        }

        if (ret != TUCamRet.Success)
        {
            _logger.LogWarning(
                "{Tag} GenICam SetElementValue 返回 {Ret}，{Prop}={Value} 未生效",
                LogTag,
                ret,
                propId,
                value
            );
        }
        else
        {
            _logger.LogDebug(
                "{Tag} Camera {Index} {Prop} = {Value} (已通过 GenICam 写入)",
                LogTag,
                cameraIndex,
                propId,
                value
            );
        }
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<int> GetCapabilityValueAsync(int cameraIndex, TUCamIdCapa capaId)
    {
        ThrowIfDisposed();
        IntPtr handle = GetHandle(cameraIndex);

        // GenICam 相机通过节点名称读取能力值
        switch (capaId)
        {
            case TUCamIdCapa.BitOfDepth:
            {
                long? val = GenICamGetInt(handle, "PixelSize");
                return Task.FromResult((int)(val ?? 0));
            }
            case TUCamIdCapa.Horizontal:
            {
                long? val = GenICamGetInt(handle, "ReverseX");
                return Task.FromResult((int)(val ?? 0));
            }
            case TUCamIdCapa.Vertical:
            {
                long? val = GenICamGetInt(handle, "ReverseY");
                return Task.FromResult((int)(val ?? 0));
            }
            case TUCamIdCapa.BinningSum:
            case TUCamIdCapa.BinningAvg:
            {
                long? val = GenICamGetInt(handle, "BinningSelector");
                return Task.FromResult((int)(val ?? 0));
            }
            case TUCamIdCapa.AutoExposure:
            case TUCamIdCapa.AutoExposureMode:
            {
                long? val = GenICamGetInt(handle, "ExposureAuto");
                return Task.FromResult((int)(val ?? 0));
            }
            case TUCamIdCapa.AutoWhiteBalance:
            {
                long? val = GenICamGetInt(handle, "BalanceWhiteAuto");
                return Task.FromResult((int)(val ?? 0));
            }
            case TUCamIdCapa.Shutter:
            {
                long? val = GenICamGetInt(handle, "GainMode");
                return Task.FromResult((int)(val ?? 0));
            }
            case TUCamIdCapa.EnableImgPro:
            {
                long? val = GenICamGetInt(handle, "DeviceLedEnable");
                return Task.FromResult((int)(val ?? 0));
            }
            default:
                _logger.LogWarning(
                    "{Tag} GetCapabilityValue: {Capa} 无对应 GenICam 节点，返回0",
                    LogTag,
                    capaId
                );
                return Task.FromResult(0);
        }
    }

    /// <inheritdoc/>
    public Task SetCapabilityValueAsync(int cameraIndex, TUCamIdCapa capaId, int value)
    {
        ThrowIfDisposed();
        IntPtr handle = GetHandle(cameraIndex);

        // GenICam 相机通过节点名称写入能力值
        TUCamRet ret;
        switch (capaId)
        {
            case TUCamIdCapa.BitOfDepth:
                ret = GenICamSetInt(handle, "PixelSize", value);
                break;
            case TUCamIdCapa.Horizontal:
                ret = GenICamSetInt(handle, "ReverseX", value != 0 ? 1 : 0);
                break;
            case TUCamIdCapa.Vertical:
                ret = GenICamSetInt(handle, "ReverseY", value != 0 ? 1 : 0);
                break;
            case TUCamIdCapa.BinningSum:
            case TUCamIdCapa.BinningAvg:
                ret = GenICamSetInt(handle, "BinningSelector", value);
                break;
            case TUCamIdCapa.AutoExposure:
            case TUCamIdCapa.AutoExposureMode:
                ret = GenICamSetInt(handle, "ExposureAuto", value);
                break;
            case TUCamIdCapa.AutoWhiteBalance:
                ret = GenICamSetInt(handle, "BalanceWhiteAuto", value);
                break;
            case TUCamIdCapa.Shutter:
                ret = GenICamSetInt(handle, "GainMode", value);
                break;
            case TUCamIdCapa.EnableImgPro:
                ret = GenICamSetInt(handle, "DeviceLedEnable", value != 0 ? 1 : 0);
                break;
            default:
                _logger.LogWarning(
                    "{Tag} SetCapabilityValue: {Capa} 无对应 GenICam 节点，跳过",
                    LogTag,
                    capaId
                );
                return Task.CompletedTask;
        }

        if (ret != TUCamRet.Success)
        {
            _logger.LogWarning(
                "{Tag} GenICam SetElementValue 返回 {Ret}，{Capa}={Value} 未生效",
                LogTag,
                ret,
                capaId,
                value
            );
        }
        else
        {
            _logger.LogDebug(
                "{Tag} Camera {Index} {Capa} = {Value} (已通过 GenICam 写入)",
                LogTag,
                cameraIndex,
                capaId,
                value
            );
        }
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task StartCaptureAsync(int cameraIndex)
    {
        ThrowIfDisposed();
        IntPtr handle = GetHandle(cameraIndex);
        CameraCaptureState captureState = GetCaptureState(cameraIndex);

        // Tucam SDK 在 ARM64/Linux 上非线程安全：若 NodeMap 预热仍在后台运行，
        // 并发调用 Buf_Alloc/Cap_Start 会导致 SDK 内部堆损坏（double free / munmap invalid pointer）。
        // 此处阻塞等待预热完成，确保所有 SDK 调用串行化。
        if (
            _prewarmTasks.TryGetValue(cameraIndex, out Task? prewarmTask)
            && !prewarmTask.IsCompleted
        )
        {
            _logger.LogInformation(
                "{Tag} Camera {Index} NodeMap 预热尚未完成，等待后再启动采集…",
                LogTag,
                cameraIndex
            );
            try
            {
                prewarmTask.GetAwaiter().GetResult();
            }
            catch
            {
                // 预热失败不阻止采集，仅记录（预热内部已有日志）
            }
        }

        // 快速幂等检查：相机已在采集中则直接返回，无需竞争单活锁
        lock (captureState.SyncRoot)
        {
            if (captureState.IsCapturing && captureState.Frame.pBuffer != IntPtr.Zero)
            {
                _logger.LogDebug(
                    "{Tag} Camera {Index} continuous capture already started",
                    LogTag,
                    cameraIndex
                );
                return Task.CompletedTask;
            }
        }

        // 单活采集锁：同一时刻仅允许一台相机处于 Cap_Start 活跃状态（USB 带宽限制）
        if (!_capStartActiveLock.Wait(30_000))
        {
            string lockTimeoutMsg =
                $"{LogTag} Camera {cameraIndex} 等待单活 Cap_Start 锁超时（30s），"
                + "另一台相机正在采集中，请先停止其他相机的采集再试。";
            _logger.LogWarning(
                "{Tag} Camera {Index} 单活 Cap_Start 锁等待超时",
                LogTag,
                cameraIndex
            );
            RecordCameraLog(
                cameraIndex,
                CameraOperationType.StartCapture,
                false,
                30_000,
                lockTimeoutMsg
            );
            throw new InvalidOperationException(lockTimeoutMsg);
        }

        // capStartLockAcquired 用于失败路径的 catch 块释放锁；
        // 成功路径将其置 false，表示锁已转移给 StopCaptureAsync 持有。
        bool capStartLockAcquired = true;
        Stopwatch sw = Stopwatch.StartNew();
        try
        {
            lock (captureState.SyncRoot)
            {
                while (captureState.StopRequested || captureState.ActiveWaiters > 0)
                {
                    Monitor.Wait(captureState.SyncRoot, TimeSpan.FromMilliseconds(100));
                }

                if (captureState.IsCapturing && captureState.Frame.pBuffer != IntPtr.Zero)
                {
                    sw.Stop();
                    _logger.LogDebug(
                        "{Tag} Camera {Index} continuous capture already started",
                        LogTag,
                        cameraIndex
                    );
                    // 已在采集中：释放单活锁后幂等返回
                    _capStartActiveLock.Release();
                    capStartLockAcquired = false;
                    return Task.CompletedTask;
                }

                // 防御性释放：若上次会话崩溃未正常 Release，相机硬件仍保有旧缓冲区，
                // 此时 TUCAM_Buf_Alloc 会返回 Excluded（资源冲突）。先无条件 Release，
                // 忽略返回值（无缓冲区时 Release 也会返回非 Success，属于正常情况）。
                TUCamNative.TUCAM_Buf_Release(handle);

                // 先分配帧缓冲区，再启动连续采集。WaitForFrame 必须复用此处返回的 pBuffer。
                // 多相机并发场景下，另一台相机大流量 USB 传输会导致本相机的 Buf_Alloc 控制
                // 传输被抢占而返回 Excluded。采用指数退避重试，最多 5 次（约 1.5s 总等待）。
                var frame = new TUCamFrame { uiRsdSize = 1 };
                TUCamRet allocRet = TUCamRet.Failure;
                int[] retryDelaysMs = { 0, 100, 200, 400, 800 };
                for (int attempt = 0; attempt < retryDelaysMs.Length; attempt++)
                {
                    if (retryDelaysMs[attempt] > 0)
                    {
                        Thread.Sleep(retryDelaysMs[attempt]);
                        // 每次重试前再次 Release，确保 SDK 内部状态清零
                        TUCamNative.TUCAM_Buf_Release(handle);
                    }

                    allocRet = TUCamNative.TUCAM_Buf_Alloc(handle, ref frame);
                    if (allocRet == TUCamRet.Success)
                    {
                        if (attempt > 0)
                        {
                            _logger.LogInformation(
                                "{Tag} Camera {Index} Buf_Alloc 在第 {Attempt} 次重试后成功（可能因另一相机占用 USB 带宽）",
                                LogTag,
                                cameraIndex,
                                attempt + 1
                            );
                        }
                        break;
                    }

                    // Excluded 是典型的"资源被占用"返回码，可重试；其他错误码立即失败
                    if (allocRet != TUCamRet.Excluded)
                    {
                        break;
                    }
                }

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

                // 启动连续采集，对 FailOpenBulkIn 进行指数退避重试：
                // 多相机共享 USB 控制器场景下，另一台相机正在 bulk-in 传输时，
                // 本相机的 OpenBulkIn 会因带宽预留失败而返回 FailOpenBulkIn。
                // 等待对方完成一帧传输（约 200ms）后重试通常即可成功。
                TUCamRet ret = TUCamRet.Failure;
                int[] capRetryDelaysMs = { 0, 100, 200, 400, 800 };
                for (int attempt = 0; attempt < capRetryDelaysMs.Length; attempt++)
                {
                    if (capRetryDelaysMs[attempt] > 0)
                    {
                        Thread.Sleep(capRetryDelaysMs[attempt]);
                    }

                    ret = TUCamNative.TUCAM_Cap_Start(handle, (uint)TUCamCaptureMode.Sequence);
                    if (ret == TUCamRet.Success)
                    {
                        if (attempt > 0)
                        {
                            _logger.LogInformation(
                                "{Tag} Camera {Index} Cap_Start 在第 {Attempt} 次重试后成功（USB bulk-in 带宽竞争）",
                                LogTag,
                                cameraIndex,
                                attempt + 1
                            );
                        }
                        break;
                    }

                    // 仅对 FailOpenBulkIn 重试；其他错误立即失败
                    if (ret != TUCamRet.FailOpenBulkIn)
                    {
                        break;
                    }
                }

                // 终极回退：若所有重试仍是 FailOpenBulkIn，说明设备句柄已进入持久错误态
                // （SDK 内部 USB bulk-in 端点状态损坏），简单重试无法恢复。必须 Close + Open
                // 重新拿到一个干净的设备句柄，再走一次完整的 Release/Alloc/Cap_Start 流程。
                if (ret == TUCamRet.FailOpenBulkIn)
                {
                    _logger.LogWarning(
                        "{Tag} Camera {Index} Cap_Start 持续 FailOpenBulkIn，尝试关闭并重新打开设备句柄进行恢复",
                        LogTag,
                        cameraIndex
                    );

                    TUCamNative.TUCAM_Buf_Release(handle);
                    TUCamRet closeRet = TUCamNative.TUCAM_Dev_Close(handle);
                    _cameraHandles.TryRemove(cameraIndex, out _);
                    _nodeMapCache.TryRemove(cameraIndex, out _);
                    _logger.LogInformation(
                        "{Tag} Camera {Index} Dev_Close 返回 {Ret}",
                        LogTag,
                        cameraIndex,
                        closeRet
                    );

                    // 给 USB 驱动一点时间复位端点
                    Thread.Sleep(500);

                    // TUCam SDK 特性：在所有相机句柄释放后，再次 Dev_Open 会返回 FailOpenCamera，
                    // 必须先 Api_Uninit + Api_Init 让 SDK 重新枚举设备。CloseCameraAsync 内已对
                    // "关闭最后一台相机" 场景做了此处理，但本恢复路径在多相机场景下可能仍有其他
                    // 相机句柄存在，故仅在 _cameraHandles 为空时执行 SDK 级重置。
                    if (_cameraHandles.IsEmpty && _initialized)
                    {
                        TUCamNative.TUCAM_Api_Uninit();
                        _initialized = false;
                        _lastCameraCount = 0;
                        var reinitParam = new TUCamInit
                        {
                            uiCamCount = 0,
                            pstrConfigPath = IntPtr.Zero,
                        };
                        TUCamRet reinitRet = TUCamNative.TUCAM_Api_Init(ref reinitParam, 1000);
                        if (reinitRet == TUCamRet.Success)
                        {
                            _initialized = true;
                            _lastCameraCount = (int)reinitParam.uiCamCount;
                            _logger.LogInformation(
                                "{Tag} Camera {Index} SDK 重新初始化成功（恢复路径），检测到 {Count} 台相机",
                                LogTag,
                                cameraIndex,
                                reinitParam.uiCamCount
                            );
                        }
                        else
                        {
                            _logger.LogWarning(
                                "{Tag} Camera {Index} SDK 重新初始化失败（恢复路径）: {Ret}",
                                LogTag,
                                cameraIndex,
                                reinitRet
                            );
                        }
                    }

                    // 对 Dev_Open 做多次重试：USB 端点复位 + 设备重新枚举均可能需要更长时间，
                    // 单次 300ms 等待往往不够。退避序列累计 ~3.5s 通常足以让设备恢复。
                    TUCamRet openRet = TUCamRet.Failure;
                    IntPtr reopenedHandle = IntPtr.Zero;
                    int[] openRetryDelaysMs = { 0, 500, 1000, 2000 };
                    for (int openAttempt = 0; openAttempt < openRetryDelaysMs.Length; openAttempt++)
                    {
                        if (openRetryDelaysMs[openAttempt] > 0)
                        {
                            Thread.Sleep(openRetryDelaysMs[openAttempt]);
                        }

                        var reopenParam = new TUCamOpen
                        {
                            uiIdxOpen = (uint)cameraIndex,
                            hIdxTUCam = IntPtr.Zero,
                        };
                        openRet = TUCamNative.TUCAM_Dev_Open(ref reopenParam);
                        if (openRet == TUCamRet.Success)
                        {
                            reopenedHandle = reopenParam.hIdxTUCam;
                            if (openAttempt > 0)
                            {
                                _logger.LogInformation(
                                    "{Tag} Camera {Index} Dev_Open 在第 {Attempt} 次重试后成功",
                                    LogTag,
                                    cameraIndex,
                                    openAttempt + 1
                                );
                            }
                            break;
                        }

                        _logger.LogWarning(
                            "{Tag} Camera {Index} Dev_Open 第 {Attempt} 次返回 {Ret}，继续重试",
                            LogTag,
                            cameraIndex,
                            openAttempt + 1,
                            openRet
                        );
                    }

                    if (openRet != TUCamRet.Success)
                    {
                        captureState.Frame = default;
                        captureState.IsCapturing = false;
                        sw.Stop();
                        string errorMsg =
                            $"{LogTag} Failed to reopen camera after FailOpenBulkIn: {openRet}";
                        RecordCameraLog(
                            cameraIndex,
                            CameraOperationType.StartCapture,
                            false,
                            sw.ElapsedMilliseconds,
                            errorMsg
                        );
                        throw new InvalidOperationException(errorMsg);
                    }
                    handle = reopenedHandle;
                    _cameraHandles[cameraIndex] = handle;
                    _logger.LogInformation(
                        "{Tag} Camera {Index} Dev_Open 重新打开成功，新句柄: {Handle}",
                        LogTag,
                        cameraIndex,
                        handle
                    );

                    // 重新分配缓冲区（新句柄上 SDK 状态干净，Buf_Alloc 通常一次即成功）
                    TUCamNative.TUCAM_Buf_Release(handle);
                    frame = new TUCamFrame { uiRsdSize = 1 };
                    allocRet = TUCamNative.TUCAM_Buf_Alloc(handle, ref frame);
                    if (allocRet != TUCamRet.Success)
                    {
                        captureState.Frame = default;
                        captureState.IsCapturing = false;
                        sw.Stop();
                        string errorMsg =
                            $"{LogTag} Failed to re-alloc buffer after reopen: {allocRet}";
                        RecordCameraLog(
                            cameraIndex,
                            CameraOperationType.StartCapture,
                            false,
                            sw.ElapsedMilliseconds,
                            errorMsg
                        );
                        throw new InvalidOperationException(errorMsg);
                    }

                    // 重新启动采集
                    ret = TUCamNative.TUCAM_Cap_Start(handle, (uint)TUCamCaptureMode.Sequence);
                    if (ret == TUCamRet.Success)
                    {
                        _logger.LogInformation(
                            "{Tag} Camera {Index} Cap_Start 在 Dev_Close+Open 复位后成功",
                            LogTag,
                            cameraIndex
                        );
                    }
                }

                if (ret != TUCamRet.Success)
                {
                    TUCamNative.TUCAM_Buf_Release(handle);
                    captureState.Frame = default;
                    captureState.IsCapturing = false;
                    sw.Stop();
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

                captureState.Frame = frame;
                captureState.IsCapturing = true;
                captureState.StopRequested = false;
                captureState.HoldsCapStartLock = true;
                capStartLockAcquired = false; // 单活锁转移给 StopCaptureAsync 持有并释放

                // 缓存像素格式信息，避免每帧调用 GenICam 读取
                captureState.CachedPixelFormat = GenICamGetString(handle, "PixelFormat");
                captureState.CachedPixelFormatValue = GenICamGetInt(handle, "PixelFormat");
                captureState.CachedPixelSize = GenICamGetInt(handle, "PixelSize");
                captureState.CachedBitDepth = ResolveFrameBitDepth(
                    frame,
                    captureState.CachedPixelFormat,
                    captureState.CachedPixelFormatValue,
                    captureState.CachedPixelSize
                );
            }

            // 部分 GenICam 设备需要显式发送 AcquisitionStart 命令才能真正开始出帧
            // （TUCAM SDK 的 TUCAM_Cap_Start 仅在 Transport Layer 启动采集线程，
            //  但 ACQ 状态机仍处于 Stopped）。失败仅记录警告，不影响主流程。
            try
            {
                TUCamRet acqRet = GenICamSetInt(handle, "AcquisitionStart", 1);
                if (acqRet != TUCamRet.Success)
                {
                    _logger.LogDebug(
                        "{Tag} Camera {Index} AcquisitionStart 命令返回 {Ret}，部分设备无需此命令，已忽略",
                        LogTag,
                        cameraIndex,
                        acqRet
                    );
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(
                    ex,
                    "{Tag} Camera {Index} AcquisitionStart 命令异常，已忽略",
                    LogTag,
                    cameraIndex
                );
            }

            sw.Stop();
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
        catch
        {
            // 采集启动失败：释放单活锁（成功路径已将 capStartLockAcquired 置 false，锁由 Stop 负责释放）
            if (capStartLockAcquired)
            {
                _capStartActiveLock.Release();
                _logger.LogDebug(
                    "{Tag} Camera {Index} 单活 Cap_Start 锁已在失败路径中释放",
                    LogTag,
                    cameraIndex
                );
            }
            throw;
        }
    }

    /// <inheritdoc/>
    public Task StopCaptureAsync(int cameraIndex)
    {
        ThrowIfDisposed();
        IntPtr handle = GetHandle(cameraIndex);
        CameraCaptureState captureState = GetCaptureState(cameraIndex);

        Stopwatch sw = Stopwatch.StartNew();
        try
        {
            lock (captureState.SyncRoot)
            {
                if (!captureState.IsCapturing)
                {
                    sw.Stop();
                    _logger.LogDebug(
                        "{Tag} Camera {Index} continuous capture already stopped",
                        LogTag,
                        cameraIndex
                    );
                    return Task.CompletedTask;
                }

                captureState.StopRequested = true;
            }

            // 对应 StartCaptureAsync 的 AcquisitionStart，软发 AcquisitionStop（失败仅 Debug 日志）
            try
            {
                TUCamRet acqRet = GenICamSetInt(handle, "AcquisitionStop", 1);
                if (acqRet != TUCamRet.Success)
                {
                    _logger.LogDebug(
                        "{Tag} Camera {Index} AcquisitionStop 命令返回 {Ret}，已忽略",
                        LogTag,
                        cameraIndex,
                        acqRet
                    );
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(
                    ex,
                    "{Tag} Camera {Index} AcquisitionStop 命令异常，已忽略",
                    LogTag,
                    cameraIndex
                );
            }

            TUCamNative.TUCAM_Buf_AbortWait(handle);

            lock (captureState.SyncRoot)
            {
                long waitDeadline = Environment.TickCount64 + 5000;
                while (captureState.ActiveWaiters > 0)
                {
                    int remainingMs = (int)Math.Max(0, waitDeadline - Environment.TickCount64);
                    if (remainingMs == 0 || !Monitor.Wait(captureState.SyncRoot, remainingMs))
                    {
                        string timeoutMessage =
                            $"{LogTag} Timeout waiting frame waiter to exit before stopping capture (index={cameraIndex})";
                        throw new InvalidOperationException(timeoutMessage);
                    }
                }

                TUCamNative.TUCAM_Cap_Stop(handle);
                TUCamNative.TUCAM_Buf_Release(handle);
                captureState.Frame = default;
                captureState.IsCapturing = false;
                captureState.StopRequested = false;
                // 释放单活采集锁，允许下一台相机执行 Cap_Start
                if (captureState.HoldsCapStartLock)
                {
                    captureState.HoldsCapStartLock = false;
                    _capStartActiveLock.Release();
                    _logger.LogInformation(
                        "{Tag} Camera {Index} 已释放单活 Cap_Start 锁",
                        LogTag,
                        cameraIndex
                    );
                }
                Monitor.PulseAll(captureState.SyncRoot);
            }
        }
        catch (Exception ex)
        {
            lock (captureState.SyncRoot)
            {
                captureState.StopRequested = false;
                // 即使 Stop 失败也释放单活锁，避免系统永久阻塞
                if (captureState.HoldsCapStartLock)
                {
                    captureState.HoldsCapStartLock = false;
                    _capStartActiveLock.Release();
                }
                Monitor.PulseAll(captureState.SyncRoot);
            }
            sw.Stop();
            RecordCameraLog(
                cameraIndex,
                CameraOperationType.StopCapture,
                false,
                sw.ElapsedMilliseconds,
                ex.Message
            );
            throw;
        }

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
        ref TUCamFrame frame = ref BeginFrameWait(cameraIndex, out CameraCaptureState captureState);
        byte[] imageData;

        try
        {
            var ret = TUCamNative.TUCAM_Buf_WaitForFrame(handle, ref frame, timeoutMs);
            if (ret != TUCamRet.Success)
            {
                throw new InvalidOperationException(
                    $"{LogTag} Wait frame timeout or failed: {ret}"
                );
            }

            int dataSize = (int)frame.uiImgSize;
            imageData = new byte[dataSize];
            if (frame.pBuffer == IntPtr.Zero || dataSize <= 0)
            {
                throw new InvalidOperationException(
                    $"{LogTag} Camera {cameraIndex} returned invalid frame buffer: pBuffer=0x{frame.pBuffer:X}, size={dataSize}B"
                );
            }

            Marshal.Copy(frame.pBuffer + frame.usOffset, imageData, 0, dataSize);
        }
        finally
        {
            EndFrameWait(captureState, frame);
        }

        // PixelFormat 描述的是传感器/GenICam 输出模式，而 SDK 可能把该模式转换为
        // 8bit 三通道显示帧后再交给应用。必须结合本次帧的实际布局重新解析位深，
        // 否则 BayGB12Packed 的显示帧会被再次当作 Packed Bayer 解包。
        int resolvedBitDepth = ResolveFrameBitDepth(
            frame,
            captureState.CachedPixelFormat,
            captureState.CachedPixelFormatValue,
            captureState.CachedPixelSize
        );

        var frameData = new CameraFrameData
        {
            Width = frame.usWidth,
            Height = frame.usHeight,
            BitDepth = (byte)resolvedBitDepth,
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

    /// <inheritdoc/>
    public bool IsCapturing(int cameraIndex)
    {
        if (!_captureStates.TryGetValue(cameraIndex, out CameraCaptureState? captureState))
        {
            return false;
        }

        lock (captureState.SyncRoot)
        {
            return captureState.IsCapturing && captureState.Frame.pBuffer != IntPtr.Zero;
        }
    }

    // ─── ROI 区域控制 ────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public Task<TUCamRoiAttr> GetRoiAsync(int cameraIndex)
    {
        ThrowIfDisposed();
        IntPtr handle = GetHandle(cameraIndex);

        // GenICam 相机通过节点读取当前 ROI 参数
        long width = GenICamGetInt(handle, "Width") ?? 0;
        long height = GenICamGetInt(handle, "Height") ?? 0;
        long offsetX = GenICamGetInt(handle, "OffsetX") ?? 0;
        long offsetY = GenICamGetInt(handle, "OffsetY") ?? 0;

        TUCamRoiAttr roi = new TUCamRoiAttr
        {
            bEnable = 1, // GenICam 相机始终应用 ROI 设置
            nWidth = (int)width,
            nHeight = (int)height,
            nHOffset = (int)offsetX,
            nVOffset = (int)offsetY,
        };
        return Task.FromResult(roi);
    }

    /// <inheritdoc/>
    public Task SetRoiAsync(int cameraIndex, TUCamRoiAttr roi)
    {
        ThrowIfDisposed();
        IntPtr handle = GetHandle(cameraIndex);

        // GenICam 相机 ROI 修改需在停止采集后进行
        bool wasCapturing = IsCapturing(cameraIndex);
        if (wasCapturing)
        {
            StopCaptureAsync(cameraIndex).GetAwaiter().GetResult();
        }

        try
        {
            int targetWidth,
                targetHeight,
                targetOffsetX,
                targetOffsetY;

            if (roi.bEnable == 0)
            {
                // 禁用 ROI：恢复传感器最大分辨率
                (_, _, long maxW) = GenICamGetIntRange(handle, "Width");
                (_, _, long maxH) = GenICamGetIntRange(handle, "Height");
                targetWidth = (int)maxW;
                targetHeight = (int)maxH;
                targetOffsetX = 0;
                targetOffsetY = 0;
            }
            else
            {
                targetWidth = roi.nWidth;
                targetHeight = roi.nHeight;
                targetOffsetX = roi.nHOffset;
                targetOffsetY = roi.nVOffset;
            }

            // 按 GenICam 标准顺序：先将偏移清零，再设尺寸，最后设偏移
            GenICamSetInt(handle, "OffsetX", 0);
            GenICamSetInt(handle, "OffsetY", 0);
            GenICamSetInt(handle, "Width", targetWidth);
            GenICamSetInt(handle, "Height", targetHeight);
            GenICamSetInt(handle, "OffsetX", targetOffsetX);
            GenICamSetInt(handle, "OffsetY", targetOffsetY);

            _logger.LogDebug(
                "{Tag} Camera {Index} ROI 设置为 {W}x{H}@({X},{Y})",
                LogTag,
                cameraIndex,
                targetWidth,
                targetHeight,
                targetOffsetX,
                targetOffsetY
            );
        }
        finally
        {
            if (wasCapturing)
            {
                StartCaptureAsync(cameraIndex).GetAwaiter().GetResult();
            }
        }

        return Task.CompletedTask;
    }

    // ─── 触发模式 ─────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public Task<TUCamTriggerAttr> GetTriggerAsync(int cameraIndex)
    {
        ThrowIfDisposed();
        IntPtr handle = GetHandle(cameraIndex);

        // GenICam 相机通过节点读取触发参数
        long tgrMode = GenICamGetInt(handle, "TriggerMode") ?? 0;
        long edgeMode = GenICamGetInt(handle, "TriggerInputEdge") ?? 0;
        long delayTm = GenICamGetInt(handle, "TriggerInputDelay") ?? 0;
        long frames = GenICamGetInt(handle, "TriggerMultipleImages") ?? 1;

        TUCamTriggerAttr trigger = new TUCamTriggerAttr
        {
            nTgrMode = (int)tgrMode,
            nExpMode = 1, // GenICam 相机默认使用曝光时间模式
            nEdgeMode = (int)edgeMode,
            nDelayTm = (int)delayTm,
            nFrames = (int)frames,
            nBufFrames = 1, // 缓冲帧数由软件层管理，固定返回1
        };
        return Task.FromResult(trigger);
    }

    /// <inheritdoc/>
    public Task SetTriggerAsync(int cameraIndex, TUCamTriggerAttr trigger)
    {
        ThrowIfDisposed();
        IntPtr handle = GetHandle(cameraIndex);

        // GenICam 相机触发参数修改需在停止采集后进行
        bool wasCapturing = IsCapturing(cameraIndex);
        if (wasCapturing)
        {
            StopCaptureAsync(cameraIndex).GetAwaiter().GetResult();
        }

        try
        {
            GenICamSetInt(handle, "TriggerMode", trigger.nTgrMode);
            GenICamSetInt(handle, "TriggerInputEdge", trigger.nEdgeMode);
            GenICamSetInt(handle, "TriggerInputDelay", trigger.nDelayTm);
            GenICamSetInt(handle, "TriggerMultipleImages", trigger.nFrames);
            _logger.LogDebug(
                "{Tag} Camera {Index} 触发参数已通过 GenICam 写入",
                LogTag,
                cameraIndex
            );
        }
        finally
        {
            if (wasCapturing)
            {
                StartCaptureAsync(cameraIndex).GetAwaiter().GetResult();
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task DoSoftwareTriggerAsync(int cameraIndex)
    {
        ThrowIfDisposed();
        IntPtr handle = GetHandle(cameraIndex);

        // RK3588 上 TUCAM_Cap_DoSoftwareTrigger 返回 NotSupport。
        // 按照 TucamMultiCameraProbe 测试结论：软件触发必须通过 GenICam 命令节点
        // "TriggerSoftwarePulse" 执行，与测试工具 DoGenICamCommand 保持一致。
        TUCamRet ret = GenICamSetInt(handle, "TriggerSoftwarePulse", 1);
        if (ret != TUCamRet.Success)
        {
            throw new InvalidOperationException($"{LogTag} Software trigger failed: {ret}");
        }
        _logger.LogDebug(
            "{Tag} Camera {Index} software trigger (TriggerSoftwarePulse) sent",
            LogTag,
            cameraIndex
        );
        return Task.CompletedTask;
    }

    // ─── 触发输出 ─────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public Task<TUCamTrgOutAttr> GetTriggerOutAsync(int cameraIndex, int port)
    {
        ThrowIfDisposed();
        IntPtr handle = GetHandle(cameraIndex);

        // 先选择端口，再读取对应端口的触发输出参数
        GenICamSetInt(handle, "TriggerOutputSelector", port);
        long outMode = GenICamGetInt(handle, "TriggerOutputSignal") ?? 0;
        long edgeMode = GenICamGetInt(handle, "TriggerOutputEdge") ?? 0;
        long delayTm = GenICamGetInt(handle, "TriggerOutputDelay") ?? 0;
        long width = GenICamGetInt(handle, "TriggerOutputWidth") ?? 0;

        TUCamTrgOutAttr trgOut = new TUCamTrgOutAttr
        {
            nTgrOutPort = port,
            nTgrOutMode = (int)outMode,
            nEdgeMode = (int)edgeMode,
            nDelayTm = (int)delayTm,
            nWidth = (int)width,
        };
        return Task.FromResult(trgOut);
    }

    /// <inheritdoc/>
    public Task SetTriggerOutAsync(int cameraIndex, TUCamTrgOutAttr trgOut)
    {
        ThrowIfDisposed();
        IntPtr handle = GetHandle(cameraIndex);

        // 先选择端口，再写入对应端口的触发输出参数
        GenICamSetInt(handle, "TriggerOutputSelector", trgOut.nTgrOutPort);
        GenICamSetInt(handle, "TriggerOutputSignal", trgOut.nTgrOutMode);
        GenICamSetInt(handle, "TriggerOutputEdge", trgOut.nEdgeMode);
        GenICamSetInt(handle, "TriggerOutputDelay", trgOut.nDelayTm);
        GenICamSetInt(handle, "TriggerOutputWidth", trgOut.nWidth);

        _logger.LogDebug(
            "{Tag} Camera {Index} 触发输出端口 {Port} 已通过 GenICam 写入",
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
            _logger.LogWarning(
                "{Tag} GetCalcROI returned {Ret} for camera {Index} calcId {CalcId}，返回默认值",
                LogTag,
                ret,
                cameraIndex,
                calcId
            );
            return Task.FromResult(roi);
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
            _logger.LogWarning(
                "{Tag} Failed to get device info {InfoId} for camera {Index}, return code: {RetCode}, returning 0",
                LogTag,
                infoId,
                cameraIndex,
                ret
            );
            return Task.FromResult(0);
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

        // GenICam 相机：FrameRate 通过浮点节点读取范围
        if (propId == TUCamIdProp.FrameRate)
        {
            (double cur, double min, double max) = GenICamGetFloatRange(
                handle,
                "AcquisitionFrameRate"
            );
            attr.dbValMin = min;
            attr.dbValMax = max;
            attr.dbValDft = cur;
            attr.dbValStep = 0.001;
            return Task.FromResult(attr);
        }

        _logger.LogWarning(
            "{Tag} GetPropertyAttr: {Prop} 无对应 GenICam 节点，返回默认值",
            LogTag,
            propId
        );
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
            _logger.LogWarning(
                "{Tag} GetCapabilityAttr returned {Ret} for camera {Index} capa {Capa}，返回默认值",
                LogTag,
                ret,
                cameraIndex,
                capaId
            );
            return Task.FromResult(attr);
        }
        return Task.FromResult(attr);
    }

    // ─── 原始帧抓取（用于单帧快照 / RTP 推流）────────────────────────────────

    /// <inheritdoc/>
    public Task<byte[]> GrabFrameRawAsync(
        int cameraIndex,
        int timeoutMs = 3000,
        int maxWidth = 0,
        int jpegQuality = 85,
        int imageRotationAngle = 0
    )
    {
        ThrowIfDisposed();
        IntPtr handle = GetHandle(cameraIndex);
        ref TUCamFrame frame = ref BeginFrameWait(cameraIndex, out CameraCaptureState captureState);
        int width;
        int height;
        int channels;
        int dataSize;
        int widthStep;
        int elemBytes;
        int frameDepth;
        byte[] rawData;

        try
        {
            TUCamRet ret = TUCamNative.TUCAM_Buf_WaitForFrame(handle, ref frame, timeoutMs);
            if (ret != TUCamRet.Success)
            {
                // Bug 4/5 诊断：超时时附加关键 GenICam 节点值，便于判断是否需调用 AcquisitionStart
                string? exp = GenICamGetString(handle, "ExposureTime");
                string? tm = GenICamGetString(handle, "TriggerMode");
                string? am = GenICamGetString(handle, "AcquisitionMode");
                long? fr = GenICamGetInt(handle, "AcquisitionFrameRate");
                throw new InvalidOperationException(
                    $"{LogTag} WaitForFrame failed (index={cameraIndex}): {ret}. timeout={timeoutMs}ms, ExposureTime={exp}, TriggerMode={tm}, AcquisitionMode={am}, AcquisitionFrameRate={fr}"
                );
            }

            width = frame.usWidth;
            height = frame.usHeight;
            channels = frame.ucChannels;
            dataSize = (int)frame.uiImgSize;
            widthStep = (int)frame.uiWidthStep;
            elemBytes = frame.ucElemBytes;
            frameDepth = frame.ucDepth;

            // 防止零尺寸帧导致 SkiaSharp 崩溃
            if (width <= 0 || height <= 0 || dataSize <= 0 || frame.pBuffer == IntPtr.Zero)
            {
                throw new InvalidOperationException(
                    $"{LogTag} Camera {cameraIndex} returned invalid frame: {width}x{height}, size={dataSize}B, pBuffer=0x{frame.pBuffer:X}"
                );
            }

            rawData = new byte[dataSize];
            Marshal.Copy(frame.pBuffer + frame.usOffset, rawData, 0, dataSize);
        }
        finally
        {
            EndFrameWait(captureState, frame);
        }

        // 查询像素格式（用于区分 BayGB12Packed 与 16bit 容器格式）
        // 使用缓存的像素格式信息，避免每帧调用 GenICam 读取
        string? pixelFormat = captureState.CachedPixelFormat;
        long? pixelFormatValue = captureState.CachedPixelFormatValue;
        long? pixelSize = captureState.CachedPixelSize;
        if (
            pixelFormat is null
            && !pixelFormatValue.HasValue
            && !pixelSize.HasValue
        )
        {
            pixelFormat = GenICamGetString(handle, "PixelFormat");
            pixelFormatValue = GenICamGetInt(handle, "PixelFormat");
            pixelSize = GenICamGetInt(handle, "PixelSize");
        }
        // 节点信息可以缓存，但位深必须针对本次 SDK 帧的实际内存布局解析。
        int bitDepth = ResolveFrameBitDepth(frame, pixelFormat, pixelFormatValue, pixelSize);

        _logger.LogDebug(
            "{Tag} Camera {Index} WaitForFrame 成功：pBuffer=0x{PBuffer:X}  {W}x{H}  ch={Channels}  widthStep={WidthStep}  size={Size}  offset={Offset}  elemBytes={ElemBytes}  frameDepth={FrameDepth}  bitDepth={BitDepth}  pixelSize={PixelSize}  pixelFormatValue={PixelFormatValue}  pixelFormat={PixelFormat}",
            LogTag,
            cameraIndex,
            frame.pBuffer,
            width,
            height,
            channels,
            widthStep,
            dataSize,
            frame.usOffset,
            elemBytes,
            frameDepth,
            bitDepth,
            pixelSize,
            pixelFormatValue,
            pixelFormat ?? "N/A"
        );

        // 编码为 BMP（含 Bayer 解包 + 双线性插值解马赛克）
        byte[] bmpBytes = EncodeToBmp(
            rawData,
            width,
            height,
            bitDepth,
            channels,
            widthStep,
            pixelFormat,
            pixelFormatValue,
            pixelSize,
            maxWidth,
            jpegQuality,
            imageRotationAngle
        );
        return Task.FromResult(bmpBytes);
    }

    /// <inheritdoc/>
    public Task<bool> DrainFrameAsync(int cameraIndex, int timeoutMs = 1000)
    {
        ThrowIfDisposed();
        IntPtr handle = GetHandle(cameraIndex);

        // 若相机未处于采集状态直接抛异常（与 BeginFrameWait 行为一致），
        // 否则预览循环会在 drain 路径无限静默自旋
        if (
            !_captureStates.TryGetValue(cameraIndex, out CameraCaptureState? state)
            || !state.IsCapturing
        )
        {
            throw new InvalidOperationException(
                $"{LogTag} Camera {cameraIndex} is not capturing. Call StartCaptureAsync first."
            );
        }

        ref TUCamFrame frame = ref BeginFrameWait(cameraIndex, out CameraCaptureState captureState);
        try
        {
            TUCamRet ret = TUCamNative.TUCAM_Buf_WaitForFrame(handle, ref frame, timeoutMs);
            // 成功或超时都不记录日志（drain 超时属于正常情况）
            return Task.FromResult(ret == TUCamRet.Success);
        }
        finally
        {
            // EndFrameWait 更新内部帧状态，不向 SDK 归还帧（由下次 WaitForFrame 隐式释放）
            EndFrameWait(captureState, frame);
        }
    }

    // ─── GenICam 节点公共访问方法 ────────────────────────────────────────────

    /// <inheritdoc/>
    public Task<long> GetGenICamIntAsync(int cameraIndex, string nodeName)
    {
        ThrowIfDisposed();
        IntPtr handle = GetHandle(cameraIndex);
        long? val = GenICamGetInt(handle, nodeName);
        return Task.FromResult(val ?? 0L);
    }

    /// <inheritdoc/>
    public Task SetGenICamIntAsync(int cameraIndex, string nodeName, long value)
    {
        ThrowIfDisposed();
        IntPtr handle = GetHandle(cameraIndex);
        TUCamRet ret = GenICamSetInt(handle, nodeName, value);
        if (ret != TUCamRet.Success)
        {
            _logger.LogWarning(
                "{Tag} GenICam SetInt {Node}={Value} 返回 {Ret}，未生效",
                LogTag,
                nodeName,
                value,
                ret
            );
        }
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<double> GetGenICamFloatAsync(int cameraIndex, string nodeName)
    {
        ThrowIfDisposed();
        IntPtr handle = GetHandle(cameraIndex);
        double? val = GenICamGetFloat(handle, nodeName);
        return Task.FromResult(val ?? 0.0);
    }

    /// <inheritdoc/>
    public Task SetGenICamFloatAsync(int cameraIndex, string nodeName, double value)
    {
        ThrowIfDisposed();
        IntPtr handle = GetHandle(cameraIndex);
        TUCamRet ret = GenICamSetFloat(handle, nodeName, value);
        if (ret != TUCamRet.Success)
        {
            _logger.LogWarning(
                "{Tag} GenICam SetFloat {Node}={Value} 返回 {Ret}，未生效",
                LogTag,
                nodeName,
                value,
                ret
            );
        }
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task ExecuteGenICamCommandAsync(int cameraIndex, string nodeName)
    {
        ThrowIfDisposed();
        IntPtr handle = GetHandle(cameraIndex);
        // Command 节点通过写入整数值 1 来触发
        TUCamRet ret = GenICamSetInt(handle, nodeName, 1);
        if (ret != TUCamRet.Success)
        {
            _logger.LogWarning(
                "{Tag} GenICam ExecuteCommand {Node} 返回 {Ret}，命令可能未执行",
                LogTag,
                nodeName,
                ret
            );
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// 根据 GenICam 像素格式和 SDK 帧信息解析实际位深。
    /// </summary>
    private static int ResolveFrameBitDepth(
        TUCamFrame frame,
        string? pixelFormat,
        long? pixelFormatValue,
        long? pixelSize
    )
    {
        // TUCAM 对彩色相机可能直接返回 8bit 三通道显示帧，此时 PixelSize 仍会表示传感器模式。
        // 若继续按 12bit/16bit 读，会把 BGR888 误解为高位深容器，导致画面重影或错位。
        if (IsRgb8Frame(frame))
        {
            return 8;
        }

        // Libra 3405C 的 PixelSize 节点最贴近用户选择：0=HighDepth12bit，1=Speed8bit。
        if (pixelSize == 0)
        {
            return 12;
        }

        if (pixelSize == 1)
        {
            return 8;
        }

        long? normalizedPixelFormatValue = NormalizePixelFormatValue(pixelFormat, pixelFormatValue);

        if (IsPackedPixelFormat(pixelFormat, normalizedPixelFormatValue))
        {
            int strideBitDepth = InferPackedBitDepthFromStride(frame);
            if (strideBitDepth > 0)
            {
                return strideBitDepth;
            }
        }

        if (
            normalizedPixelFormatValue is 2 or >= 12 and <= 15
            || ContainsPixelFormatToken(pixelFormat, "12")
        )
        {
            return 12;
        }

        if (
            normalizedPixelFormatValue is 3 or >= 8 and <= 11
            || ContainsPixelFormatToken(pixelFormat, "10")
        )
        {
            return 10;
        }

        if (
            normalizedPixelFormatValue is 1 or >= 4 and <= 7
            || ContainsPixelFormatToken(pixelFormat, "8")
        )
        {
            return 8;
        }

        if (normalizedPixelFormatValue == 0)
        {
            return 16;
        }

        if (frame.ucDepth is > 0 and <= 16)
        {
            return frame.ucDepth;
        }

        return frame.ucElemBytes >= 2 ? 12 : 8;
    }

    /// <summary>
    /// 判断 SDK 返回的帧是否已经是 8bit 三通道显示帧。
    /// </summary>
    private static bool IsRgb8Frame(TUCamFrame frame)
    {
        if (frame.ucChannels < 3)
        {
            return false;
        }

        int width = frame.usWidth;
        int height = frame.usHeight;
        int rowStride = (int)frame.uiWidthStep;
        int imageSize = (int)frame.uiImgSize;
        int expectedStride = width * frame.ucChannels;
        int expectedSize = expectedStride * height;

        return frame.ucElemBytes <= 1 || rowStride == expectedStride || imageSize == expectedSize;
    }

    /// <summary>
    /// 判断当前 PixelFormat 是否为 Packed 格式。
    /// </summary>
    private static bool IsPackedPixelFormat(string? pixelFormat, long? pixelFormatValue)
    {
        long? normalizedPixelFormatValue = NormalizePixelFormatValue(pixelFormat, pixelFormatValue);

        return ContainsPixelFormatToken(pixelFormat, "Packed")
            || normalizedPixelFormatValue is 2 or 3 or >= 8 and <= 15;
    }

    /// <summary>
    /// PixelFormat 字符串有时是枚举名，有时是数字文本；这里统一归一化。
    /// </summary>
    private static long? NormalizePixelFormatValue(string? pixelFormat, long? pixelFormatValue)
    {
        if (pixelFormatValue.HasValue)
        {
            return pixelFormatValue.Value;
        }

        return long.TryParse(pixelFormat, out long parsedValue) ? parsedValue : null;
    }

    /// <summary>
    /// 根据 Packed 行步长反推位深，用于兜底修正 SDK 枚举值与 PixelSize 不一致的情况。
    /// </summary>
    private static int InferPackedBitDepthFromStride(TUCamFrame frame)
    {
        int width = frame.usWidth;
        int rowStride = (int)frame.uiWidthStep;
        if (width <= 0 || rowStride <= 0)
        {
            return 0;
        }

        int packed12BytesPerRow = (width + 1) / 2 * 3;
        int packed10BytesPerRow = (width * 10 + 7) / 8;
        if (rowStride >= packed12BytesPerRow)
        {
            return 12;
        }

        return rowStride >= packed10BytesPerRow ? 10 : 0;
    }

    /// <summary>
    /// 不区分大小写检查 PixelFormat 文本中是否包含指定片段。
    /// </summary>
    private static bool ContainsPixelFormatToken(string? pixelFormat, string token)
    {
        return !string.IsNullOrWhiteSpace(pixelFormat)
            && pixelFormat.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>
    /// 12bit Packed 字节布局。
    /// </summary>
    private enum Packed12Layout
    {
        /// <summary>低字节在前，第二字节混合两个像素的高 4 位</summary>
        LsbFirst,

        /// <summary>高字节在前，第二字节混合两个像素的低/高 4 位</summary>
        MsbFirst,

        /// <summary>前两个字节分别是两个像素高 8 位，第三字节存两个低 4 位</summary>
        HighBytesSharedLowNibble,
    }

    private enum BayerColor
    {
        Red,
        Green,
        Blue,
    }

    private enum BayerPattern
    {
        Gbrg,
        Rggb,
        Grbg,
        Bggr,
    }

    /// <summary>
    /// 将 12bit Packed Bayer 数据解包并拉伸为 8bit 显示灰度。
    /// </summary>
    private static byte[] DecodePacked12ToDisplay8(
        byte[] rawData,
        int width,
        int height,
        int widthStep
    )
    {
        int packedBytesPerRow = (width + 1) / 2 * 3;
        int srcRowStride = widthStep > 0 ? widthStep : packedBytesPerRow;
        Packed12Layout layout = DetectPacked12Layout(rawData, width, height, srcRowStride);
        var unpacked = new ushort[width * height];

        for (int row = 0; row < height; row++)
        {
            int srcRowStart = row * srcRowStride;
            int dstRowStart = row * width;
            int dstCol = 0;
            int srcCol = 0;
            while (dstCol < width && srcRowStart + srcCol + 2 < rawData.Length)
            {
                DecodePacked12Pair(
                    rawData[srcRowStart + srcCol],
                    rawData[srcRowStart + srcCol + 1],
                    rawData[srcRowStart + srcCol + 2],
                    layout,
                    out ushort firstPixel,
                    out ushort secondPixel
                );

                unpacked[dstRowStart + dstCol] = firstPixel;
                if (dstCol + 1 < width)
                {
                    unpacked[dstRowStart + dstCol + 1] = secondPixel;
                }

                dstCol += 2;
                srcCol += 3;
            }
        }

        return NormalizeHighDepthToDisplay8(unpacked, 12);
    }

    /// <summary>
    /// 按同色邻域平滑度选择最可能的 12bit Packed 字节布局。
    /// </summary>
    private static Packed12Layout DetectPacked12Layout(
        byte[] rawData,
        int width,
        int height,
        int srcRowStride
    )
    {
        Packed12Layout bestLayout = Packed12Layout.LsbFirst;
        double bestScore = double.MaxValue;
        Packed12Layout[] layouts =
        [
            Packed12Layout.LsbFirst,
            Packed12Layout.MsbFirst,
            Packed12Layout.HighBytesSharedLowNibble,
        ];

        foreach (Packed12Layout layout in layouts)
        {
            double score = CalculatePacked12SmoothnessScore(
                rawData,
                width,
                height,
                srcRowStride,
                layout
            );
            if (score < bestScore)
            {
                bestScore = score;
                bestLayout = layout;
            }
        }

        return bestLayout;
    }

    /// <summary>
    /// 计算同色 Bayer 采样点的亮度差，差值越小越可能是正确解包方式。
    /// </summary>
    private static double CalculatePacked12SmoothnessScore(
        byte[] rawData,
        int width,
        int height,
        int srcRowStride,
        Packed12Layout layout
    )
    {
        int rowStep = Math.Max(1, height / 64);
        int colStep = Math.Max(2, width / 64);
        if ((colStep & 1) == 1)
        {
            colStep++;
        }

        long totalDiff = 0;
        int sampleCount = 0;
        for (int row = 0; row < height; row += rowStep)
        {
            for (int col = 0; col + 2 < width; col += colStep)
            {
                ushort current = DecodePacked12Pixel(
                    rawData,
                    width,
                    srcRowStride,
                    row,
                    col,
                    layout
                );
                ushort next = DecodePacked12Pixel(
                    rawData,
                    width,
                    srcRowStride,
                    row,
                    col + 2,
                    layout
                );
                totalDiff += Math.Abs(current - next);
                sampleCount++;
            }
        }

        return sampleCount > 0 ? totalDiff / (double)sampleCount : double.MaxValue;
    }

    /// <summary>
    /// 解出指定位置的 12bit Packed 像素值。
    /// </summary>
    private static ushort DecodePacked12Pixel(
        byte[] rawData,
        int width,
        int srcRowStride,
        int row,
        int col,
        Packed12Layout layout
    )
    {
        int pixelPair = col / 2;
        int srcIndex = row * srcRowStride + pixelPair * 3;
        if (col < 0 || col >= width || srcIndex + 2 >= rawData.Length)
        {
            return 0;
        }

        DecodePacked12Pair(
            rawData[srcIndex],
            rawData[srcIndex + 1],
            rawData[srcIndex + 2],
            layout,
            out ushort firstPixel,
            out ushort secondPixel
        );

        return (col & 1) == 0 ? firstPixel : secondPixel;
    }

    /// <summary>
    /// 按指定布局解包两个 12bit 像素。
    /// </summary>
    private static void DecodePacked12Pair(
        byte firstByte,
        byte secondByte,
        byte thirdByte,
        Packed12Layout layout,
        out ushort firstPixel,
        out ushort secondPixel
    )
    {
        switch (layout)
        {
            case Packed12Layout.MsbFirst:
                firstPixel = (ushort)((firstByte << 4) | (secondByte >> 4));
                secondPixel = (ushort)(((secondByte & 0x0F) << 8) | thirdByte);
                break;
            case Packed12Layout.HighBytesSharedLowNibble:
                firstPixel = (ushort)((firstByte << 4) | (thirdByte & 0x0F));
                secondPixel = (ushort)((secondByte << 4) | (thirdByte >> 4));
                break;
            default:
                firstPixel = (ushort)(firstByte | ((secondByte & 0x0F) << 8));
                secondPixel = (ushort)((secondByte >> 4) | (thirdByte << 4));
                break;
        }
    }

    /// <summary>
    /// 将高位深像素按百分位拉伸到 8bit，避免暗场只取高 8 位时整张发黑。
    /// </summary>
    private static byte[] NormalizeHighDepthToDisplay8(ushort[] values, int bitDepth)
    {
        var result = new byte[values.Length];
        if (values.Length == 0)
        {
            return result;
        }

        int normalizedBitDepth = Math.Clamp(bitDepth, 1, 16);
        int maxValue = normalizedBitDepth == 16 ? ushort.MaxValue : (1 << normalizedBitDepth) - 1;
        var histogram = new int[maxValue + 1];
        foreach (ushort value in values)
        {
            histogram[Math.Min(value, maxValue)]++;
        }

        int lowTarget = Math.Max(0, values.Length / 200);
        int highTarget = Math.Min(values.Length - 1, values.Length - values.Length / 200);
        int cumulative = 0;
        int lowValue = 0;
        for (int value = 0; value <= maxValue; value++)
        {
            cumulative += histogram[value];
            if (cumulative >= lowTarget)
            {
                lowValue = value;
                break;
            }
        }

        cumulative = 0;
        int highValue = maxValue;
        for (int value = 0; value <= maxValue; value++)
        {
            cumulative += histogram[value];
            if (cumulative >= highTarget)
            {
                highValue = value;
                break;
            }
        }

        int range = highValue - lowValue;
        if (range <= 0)
        {
            int shift = Math.Max(0, normalizedBitDepth - 8);
            for (int index = 0; index < values.Length; index++)
            {
                result[index] = (byte)Math.Clamp(values[index] >> shift, 0, 255);
            }

            return result;
        }

        for (int index = 0; index < values.Length; index++)
        {
            int value = Math.Clamp(values[index], lowValue, highValue);
            result[index] = (byte)((value - lowValue) * 255 / range);
        }

        return result;
    }

    /// <summary>
    /// 判断 SDK 已解马赛克的 8bit 三通道帧是否需要按显示范围拉伸。
    /// </summary>
    private static bool ShouldNormalizeRgbDisplayFrame(
        string? pixelFormat,
        long? pixelFormatValue,
        long? pixelSize
    )
    {
        if (pixelSize == 0)
        {
            return true;
        }

        long? normalizedPixelFormatValue = NormalizePixelFormatValue(pixelFormat, pixelFormatValue);
        return normalizedPixelFormatValue is 2 or >= 12 and <= 15
            || ContainsPixelFormatToken(pixelFormat, "12");
    }

    /// <summary>
    /// 对 8bit 三通道显示帧做亮度百分位拉伸，避免 12bit 模式下 SDK 显示帧整体偏暗。
    /// </summary>
    private static byte[] NormalizeRgb8DisplayFrame(byte[] pixels, bool isBgr)
    {
        var result = new byte[pixels.Length];
        if (pixels.Length < 3)
        {
            Buffer.BlockCopy(pixels, 0, result, 0, pixels.Length);
            return result;
        }

        int pixelCount = pixels.Length / 3;
        var histogram = new int[256];
        for (int index = 0; index + 2 < pixels.Length; index += 3)
        {
            int red = isBgr ? pixels[index + 2] : pixels[index];
            int green = pixels[index + 1];
            int blue = isBgr ? pixels[index] : pixels[index + 2];
            int luma = (77 * red + 150 * green + 29 * blue + 128) >> 8;
            histogram[luma]++;
        }

        int lowTarget = Math.Max(0, pixelCount / 200);
        int highTarget = Math.Min(pixelCount - 1, pixelCount - pixelCount / 200);

        int cumulative = 0;
        int lowValue = 0;
        for (int value = 0; value <= 255; value++)
        {
            cumulative += histogram[value];
            if (cumulative >= lowTarget)
            {
                lowValue = value;
                break;
            }
        }

        cumulative = 0;
        int highValue = 255;
        for (int value = 0; value <= 255; value++)
        {
            cumulative += histogram[value];
            if (cumulative >= highTarget)
            {
                highValue = value;
                break;
            }
        }

        if (lowValue <= 8 && highValue >= 220)
        {
            Buffer.BlockCopy(pixels, 0, result, 0, pixels.Length);
            return result;
        }

        int range = highValue - lowValue;
        if (range <= 0)
        {
            Buffer.BlockCopy(pixels, 0, result, 0, pixels.Length);
            return result;
        }

        for (int index = 0; index < pixels.Length; index++)
        {
            int value = Math.Clamp(pixels[index], lowValue, highValue);
            result[index] = (byte)((value - lowValue) * 255 / range);
        }

        return result;
    }

    /// <summary>
    /// 将原始像素数据编码为 BMP 字节数组。
    /// 支持 BayGB8（Bayer GBRG 8bit）、BayGB12Packed（Packed 12bit Bayer）及预解马赛克 RGB 格式。
    /// Bayer 单通道数据通过双线性插值解马赛克输出全分辨率彩色图像。
    /// </summary>
    /// <param name="rawData">原始像素字节数组（含行步长填充）</param>
    /// <param name="width">图像宽度（像素）</param>
    /// <param name="height">图像高度（像素）</param>
    /// <param name="bitDepth">位深度（8 或 12）</param>
    /// <param name="channels">通道数（0/1=Bayer 单通道, 3=已解马赛克 RGB）</param>
    /// <param name="widthStep">行步长字节数（含对齐填充；0 表示紧凑布局）</param>
    /// <param name="pixelFormat">GenICam PixelFormat 字符串（用于区分 Packed 格式；null 时按位深推断）</param>
    /// <param name="pixelFormatValue">GenICam PixelFormat 枚举值</param>
    /// <param name="pixelSize">GenICam PixelSize 枚举值，0 表示 HighDepth12bit，1 表示 Speed8bit</param>
    /// <param name="maxOutputWidth">BMP 输出最大宽度，0 表示保持原始宽度</param>
    /// <param name="jpegQuality">未使用（保留参数兼容性）</param>
    /// <param name="imageRotationAngle">图像顺时针旋转角度（度，支持 0/90/180/270）</param>
    private static byte[] EncodeToBmp(
        byte[] rawData,
        int width,
        int height,
        int bitDepth,
        int channels,
        int widthStep,
        string? pixelFormat,
        long? pixelFormatValue,
        long? pixelSize,
        int maxOutputWidth = 0,
        int jpegQuality = 85,
        int imageRotationAngle = 0
    )
    {
        // 快速路径：三通道数据 + 不需要旋转 + 不需要缩放 + 非 Bayer
        bool isThreeChannelFast = channels >= 3;
        bool isWindows = OperatingSystem.IsWindows();
        int normalizedRotationAngle = NormalizeImageRotationAngle(imageRotationAngle);
        bool isPacked =
            IsPackedPixelFormat(pixelFormat, pixelFormatValue)
            || (pixelSize == 0 && bitDepth > 8 && channels <= 1);
        // PixelFormat 可能仍是 BayGB12Packed，但 TUCam SDK 实际返回的已经是
        // 三通道显示帧。实际通道数优先，只有单通道帧才需要 Bayer 解码。
        bool isBayer = channels <= 1;

        // 快速路径只在不需要缩放和旋转时启用，且不是 Bayer 格式（即使是 Packed 只要是三通道也可以用快速路径）
        if (
            isThreeChannelFast
            && !isBayer
            && normalizedRotationAngle == 0
            && (maxOutputWidth <= 0 || width <= maxOutputWidth)
        )
        {
            // 直接用原始数据写 BMP，跳过所有中间步骤！
            bool isBgrFast = isWindows;
            int srcBytesPerRow = widthStep > 0 ? widthStep : width * 3 * (bitDepth > 8 ? 2 : 1);
            byte[] result = EncodeThreeChannelToBmpFromRaw(
                rawData,
                width,
                height,
                isBgrFast,
                srcBytesPerRow,
                bitDepth
            );
            return result;
        }

        int outputWidth = width;
        int outputHeight = height;
        BayerPattern bayerPattern = BayerPattern.Gbrg;

        // ── 步骤 A：解包到紧凑 8bit 数组（去除行步长填充）──────────────────────
        byte[] decoded;
        int outChannels;

        if (isBayer && isPacked && bitDepth >= 12)
        {
            decoded = DecodePacked12ToDisplay8(rawData, width, height, widthStep);
            outChannels = 1;
        }
        else if (isBayer)
        {
            int srcBytesPerRow = widthStep > 0 ? widthStep : width * (bitDepth > 8 ? 2 : 1);
            decoded = new byte[width * height];
            if (bitDepth <= 8)
            {
                for (int row = 0; row < height; row++)
                {
                    int srcStart = row * srcBytesPerRow;
                    int dstStart = row * width;
                    int copyLen = Math.Min(width, rawData.Length - srcStart);
                    if (copyLen <= 0)
                        break;
                    Buffer.BlockCopy(rawData, srcStart, decoded, dstStart, copyLen);
                }
            }
            else
            {
                var highDepthValues = new ushort[width * height];
                for (int row = 0; row < height; row++)
                {
                    int srcRowStart = row * srcBytesPerRow;
                    int dstRowStart = row * width;
                    for (int col = 0; col < width; col++)
                    {
                        int srcIdx = srcRowStart + col * 2;
                        if (srcIdx + 1 >= rawData.Length)
                            break;
                        int raw16 = rawData[srcIdx] | (rawData[srcIdx + 1] << 8);
                        highDepthValues[dstRowStart + col] = (ushort)raw16;
                    }
                }

                decoded = NormalizeHighDepthToDisplay8(highDepthValues, Math.Min(bitDepth, 16));
            }
            outChannels = 1;
        }
        else
        {
            int srcBytesPerRow = widthStep > 0 ? widthStep : width * 3 * (bitDepth > 8 ? 2 : 1);
            int dstBytesPerRow = width * 3;
            decoded = new byte[width * height * 3];
            if (bitDepth <= 8)
            {
                for (int row = 0; row < height; row++)
                {
                    int srcStart = row * srcBytesPerRow;
                    int dstStart = row * dstBytesPerRow;
                    int copyLen = Math.Min(dstBytesPerRow, rawData.Length - srcStart);
                    if (copyLen <= 0)
                        break;
                    Buffer.BlockCopy(rawData, srcStart, decoded, dstStart, copyLen);
                }
            }
            else
            {
                var highDepthValues = new ushort[width * height * 3];
                for (int row = 0; row < height; row++)
                {
                    int srcRowStart = row * srcBytesPerRow;
                    int dstRowStart = row * dstBytesPerRow;
                    for (int col = 0; col < width * 3; col++)
                    {
                        int srcIdx = srcRowStart + col * 2;
                        if (srcIdx + 1 >= rawData.Length)
                            break;
                        int raw16 = rawData[srcIdx] | (rawData[srcIdx + 1] << 8);
                        highDepthValues[dstRowStart + col] = (ushort)raw16;
                    }
                }

                decoded = NormalizeHighDepthToDisplay8(highDepthValues, Math.Min(bitDepth, 16));
            }
            outChannels = 3;
        }

        // ── 步骤 B：Bayer GBRG → RGB 双线性插值解马赛克 ────────────────────────
        // 仅 outChannels=1（Bayer 单通道）时执行；outChannels=3 时 SDK 已处理，跳过
        //
        // TUCam SDK 通道顺序平台差异：
        //   Windows：SDK 返回三通道 BGR 帧（沿用 GDI BITMAPINFO 惯例）
        //   Linux/ARM64：SDK 返回三通道 RGB 帧（无 Windows GDI 约束）
        // 因此在 Linux 下 decodedIsBgr=false，像素填充时需做 B/R 交换，否则蓝色偏红。
        bool decodedIsBgr = outChannels == 3 && OperatingSystem.IsWindows();
        bool isThreeChannel = outChannels == 3;
        if (
            isThreeChannel
            && bitDepth <= 8
            && ShouldNormalizeRgbDisplayFrame(pixelFormat, pixelFormatValue, pixelSize)
        )
        {
            decoded = NormalizeRgb8DisplayFrame(decoded, isBgr: decodedIsBgr);
        }

        if (normalizedRotationAngle != 0)
        {
            if (outChannels == 1)
            {
                decoded = RotateInterleavedPixels(
                    decoded,
                    outputWidth,
                    outputHeight,
                    1,
                    normalizedRotationAngle
                );
                bayerPattern = GetRotatedBayerPattern(
                    bayerPattern,
                    outputWidth,
                    outputHeight,
                    normalizedRotationAngle
                );
            }
            else
            {
                decoded = RotateInterleavedPixels(
                    decoded,
                    outputWidth,
                    outputHeight,
                    3,
                    normalizedRotationAngle
                );
            }

            if (normalizedRotationAngle is 90 or 270)
            {
                (outputWidth, outputHeight) = (outputHeight, outputWidth);
            }
        }

        if (maxOutputWidth > 0 && outputWidth > maxOutputWidth && outChannels == 3)
        {
            int scaledWidth = maxOutputWidth;
            int scaledHeight = Math.Max(
                1,
                (int)Math.Round(outputHeight * (scaledWidth / (double)outputWidth))
            );
            decoded = ScaleThreeChannelNearest(
                decoded,
                outputWidth,
                outputHeight,
                scaledWidth,
                scaledHeight
            );
            outputWidth = scaledWidth;
            outputHeight = scaledHeight;
        }

        // 对于三通道数据，直接保存为 BMP 格式，不做任何压缩
        if (outChannels == 3)
        {
            byte[] result = EncodeThreeChannelToBmp(
                decoded,
                outputWidth,
                outputHeight,
                decodedIsBgr
            );
            return result;
        }

        // ── 步骤 D：颜色数据 → SkiaSharp Bgra8888 + BMP 编码 ───────────────────
        // 对于 Bayer 单通道：直接解马赛克并填充到 Bgra8888 缓冲区
        using SKBitmap bitmap = new SKBitmap(
            outputWidth,
            outputHeight,
            SKColorType.Bgra8888,
            SKAlphaType.Opaque
        );
        IntPtr bitmapPtr = bitmap.GetPixels();
        if (bitmapPtr == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                $"Failed to allocate SkiaSharp bitmap buffer ({outputWidth}x{outputHeight})"
            );
        }

        DemosaicBayerToBgra8888(decoded, outputWidth, outputHeight, bayerPattern, bitmapPtr);

        int quality = Math.Clamp(jpegQuality, 1, 100);

        using SKBitmap? scaledBitmap = ScaleBitmap(bitmap, maxOutputWidth);
        SKBitmap outputBitmap = scaledBitmap ?? bitmap;
        return EncodeBitmap(outputBitmap, quality, outputBitmap.Width, outputBitmap.Height);
    }

    /// <summary>
    /// 缩放紧凑排列的三通道预览帧。预览优先吞吐和低延迟，使用最近邻采样，
    /// 避免为每帧构造额外的图像编解码对象。
    /// </summary>
    private static byte[] ScaleThreeChannelNearest(
        byte[] source,
        int sourceWidth,
        int sourceHeight,
        int targetWidth,
        int targetHeight
    )
    {
        byte[] target = new byte[targetWidth * targetHeight * 3];

        for (int targetY = 0; targetY < targetHeight; targetY++)
        {
            int sourceY = (int)((long)targetY * sourceHeight / targetHeight);
            int sourceRow = sourceY * sourceWidth * 3;
            int targetRow = targetY * targetWidth * 3;

            for (int targetX = 0; targetX < targetWidth; targetX++)
            {
                int sourceX = (int)((long)targetX * sourceWidth / targetWidth);
                int sourceIndex = sourceRow + sourceX * 3;
                int targetIndex = targetRow + targetX * 3;
                target[targetIndex] = source[sourceIndex];
                target[targetIndex + 1] = source[sourceIndex + 1];
                target[targetIndex + 2] = source[sourceIndex + 2];
            }
        }

        return target;
    }

    /// <summary>
    /// 直接从原始数据（包含行步长填充）写 BMP，不做任何中间复制！
    /// 最快速度路径，跳过所有解包、复制步骤
    /// </summary>
    private static byte[] EncodeThreeChannelToBmpFromRaw(
        byte[] rawData,
        int width,
        int height,
        bool isBgr,
        int srcBytesPerRow,
        int bitDepth
    )
    {
        // 计算 BMP 所需大小
        int padding = (4 - (width * 3) % 4) % 4; // 行对齐到 4 字节
        int fileSize = 14 + 40 + (width * 3 + padding) * height;

        byte[] bmp = new byte[fileSize];

        // 写 BMP 文件头 (14字节)
        bmp[0] = (byte)'B';
        bmp[1] = (byte)'M';
        WriteInt32(bmp, 2, fileSize);
        WriteInt16(bmp, 6, 0);
        WriteInt16(bmp, 8, 0);
        WriteInt32(bmp, 10, 54); // 像素数据偏移

        // 写 BMP 信息头 (40字节)
        WriteInt32(bmp, 14, 40); // 信息头大小
        WriteInt32(bmp, 18, width); // 宽度
        WriteInt32(bmp, 22, -height); // 高度（负数表示从上到下）
        WriteInt16(bmp, 26, 1); // 平面数
        WriteInt16(bmp, 28, 24); // 24位色
        WriteInt32(bmp, 30, 0); // 无压缩
        WriteInt32(bmp, 34, (width * 3 + padding) * height); // 像素数据大小
        WriteInt32(bmp, 38, 2835); // 水平分辨率
        WriteInt32(bmp, 42, 2835); // 垂直分辨率
        WriteInt32(bmp, 46, 0); // 颜色数
        WriteInt32(bmp, 50, 0); // 重要颜色数

        // 写像素数据（直接从原始数据处理）
        int dstOffset = 54;
        int rowSize = width * 3;

        // 只用安全的 Span<T>，100% 安全
        Span<byte> rawSpan = rawData.AsSpan();
        Span<byte> bmpSpan = bmp.AsSpan();

        if (bitDepth <= 8)
        {
            // 8bit 版本：直接逐行复制
            for (int row = 0; row < height; row++)
            {
                int srcRowOffset = row * srcBytesPerRow;
                int dstRowOffset = dstOffset;

                // 直接复制一行像素（8bit）
                int copyLength = Math.Min(rowSize, rawData.Length - srcRowOffset);
                if (copyLength > 0)
                {
                    rawSpan.Slice(srcRowOffset, copyLength).CopyTo(bmpSpan.Slice(dstRowOffset));
                }
                // 如果不够，填充0
                if (copyLength < rowSize)
                {
                    bmpSpan.Slice(dstRowOffset + copyLength, rowSize - copyLength).Clear();
                }

                // 添加行对齐填充
                if (padding > 0)
                {
                    bmpSpan.Slice(dstRowOffset + rowSize, padding).Clear();
                }

                dstOffset += rowSize + padding;
            }
        }
        else
        {
            // 12bit/16bit 版本：使用 unsafe 指针大幅提高性能
            unsafe
            {
                fixed (byte* rawPtr = rawData)
                fixed (byte* bmpPtr = bmp)
                {
                    byte* srcBase = rawPtr;
                    byte* dstBase = bmpPtr + 54;

                    for (int row = 0; row < height; row++)
                    {
                        byte* srcRow = srcBase + row * srcBytesPerRow;
                        byte* dstRow = dstBase + row * (rowSize + padding);

                        int srcPixelCount = width * 3;
                        int srcEnd = row * srcBytesPerRow + srcPixelCount * 2;
                        int availablePixels = Math.Min(
                            srcPixelCount,
                            srcEnd <= rawData.Length
                                ? srcPixelCount
                                : (rawData.Length - row * srcBytesPerRow - 1) / 2
                        );

                        // 高性能版本：使用指针配合循环展开，最大化性能
                        // 一次处理 8 个像素，减少循环开销
                        int i = 0;

                        // 主体部分：循环展开处理
                        for (; i <= availablePixels - 8; i += 8)
                        {
                            // 一次处理 8 个像素
                            dstRow[i + 0] = srcRow[(i + 0) * 2 + 1];
                            dstRow[i + 1] = srcRow[(i + 1) * 2 + 1];
                            dstRow[i + 2] = srcRow[(i + 2) * 2 + 1];
                            dstRow[i + 3] = srcRow[(i + 3) * 2 + 1];
                            dstRow[i + 4] = srcRow[(i + 4) * 2 + 1];
                            dstRow[i + 5] = srcRow[(i + 5) * 2 + 1];
                            dstRow[i + 6] = srcRow[(i + 6) * 2 + 1];
                            dstRow[i + 7] = srcRow[(i + 7) * 2 + 1];
                        }

                        // 处理剩余的像素
                        for (; i < availablePixels; i++)
                        {
                            dstRow[i] = srcRow[i * 2 + 1];
                        }

                        // 填充剩下的像素为0
                        if (availablePixels < srcPixelCount)
                        {
                            Unsafe.InitBlockUnaligned(
                                dstRow + availablePixels,
                                0,
                                (uint)(srcPixelCount - availablePixels)
                            );
                        }

                        // 添加行对齐填充
                        if (padding > 0)
                        {
                            Unsafe.InitBlockUnaligned(dstRow + rowSize, 0, (uint)padding);
                        }
                    }
                }
            }
        }

        return bmp;
    }

    /// <summary>
    /// 直接将三通道 RGB/BGR 数据编码为 BMP，不做任何压缩
    /// 优化：直接写 BMP 文件头，跳过 System.Drawing，最快速度
    /// </summary>
    private static byte[] EncodeThreeChannelToBmp(byte[] data, int width, int height, bool isBgr)
    {
        // 计算 BMP 所需大小
        int padding = (4 - (width * 3) % 4) % 4; // 行对齐到 4 字节
        int fileSize = 14 + 40 + (width * 3 + padding) * height;

        byte[] bmp = new byte[fileSize];

        // 写 BMP 文件头 (14字节)
        bmp[0] = (byte)'B';
        bmp[1] = (byte)'M';
        WriteInt32(bmp, 2, fileSize);
        WriteInt16(bmp, 6, 0);
        WriteInt16(bmp, 8, 0);
        WriteInt32(bmp, 10, 54); // 像素数据偏移

        // 写 BMP 信息头 (40字节)
        WriteInt32(bmp, 14, 40); // 信息头大小
        WriteInt32(bmp, 18, width); // 宽度
        WriteInt32(bmp, 22, -height); // 高度（负数表示从上到下）
        WriteInt16(bmp, 26, 1); // 平面数
        WriteInt16(bmp, 28, 24); // 24位色
        WriteInt32(bmp, 30, 0); // 无压缩
        WriteInt32(bmp, 34, (width * 3 + padding) * height); // 像素数据大小
        WriteInt32(bmp, 38, 2835); // 水平分辨率
        WriteInt32(bmp, 42, 2835); // 垂直分辨率
        WriteInt32(bmp, 46, 0); // 颜色数
        WriteInt32(bmp, 50, 0); // 重要颜色数

        // 写像素数据
        int dstOffset = 54;
        int srcStride = width * 3;
        int dstStride = width * 3 + padding;

        for (int y = 0; y < height; y++)
        {
            int srcIdx = y * srcStride;

            for (int x = 0; x < width; x++)
            {
                if (isBgr)
                {
                    // 已经是 BGR，直接写
                    bmp[dstOffset + x * 3 + 0] = data[srcIdx + x * 3 + 0];
                    bmp[dstOffset + x * 3 + 1] = data[srcIdx + x * 3 + 1];
                    bmp[dstOffset + x * 3 + 2] = data[srcIdx + x * 3 + 2];
                }
                else
                {
                    // RGB → BGR 转换
                    bmp[dstOffset + x * 3 + 0] = data[srcIdx + x * 3 + 2];
                    bmp[dstOffset + x * 3 + 1] = data[srcIdx + x * 3 + 1];
                    bmp[dstOffset + x * 3 + 2] = data[srcIdx + x * 3 + 0];
                }
            }

            // 填充 padding
            for (int p = 0; p < padding; p++)
            {
                bmp[dstOffset + width * 3 + p] = 0;
            }

            dstOffset += dstStride;
        }

        return bmp;
    }

    private static void WriteInt16(byte[] buffer, int offset, short value)
    {
        buffer[offset + 0] = (byte)(value & 0xFF);
        buffer[offset + 1] = (byte)(value >> 8);
    }

    private static void WriteInt32(byte[] buffer, int offset, int value)
    {
        buffer[offset + 0] = (byte)(value & 0xFF);
        buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
        buffer[offset + 2] = (byte)((value >> 16) & 0xFF);
        buffer[offset + 3] = (byte)(value >> 24);
    }

    /// <summary>
    /// 将位图编码为 BMP。使用 System.Drawing 进行编码，性能更高。
    /// </summary>
    private static byte[] EncodeBitmap(SKBitmap bitmap, int quality, int width, int height)
    {
        try
        {
            return EncodeWithSystemDrawing(bitmap);
        }
        catch
        {
            return EncodeWithSkiaSharp(bitmap, width, height);
        }
    }

    /// <summary>
    /// 使用 System.Drawing 进行 BMP 编码（更快）
    /// 优化：直接使用指针复制像素数据，避免 SKColor[] 分配
    /// </summary>
    private static byte[] EncodeWithSystemDrawing(SKBitmap bitmap)
    {
        using MemoryStream ms = new();
        using Bitmap bmp = new(
            bitmap.Width,
            bitmap.Height,
            System.Drawing.Imaging.PixelFormat.Format24bppRgb
        );

        BitmapData bmpData = bmp.LockBits(
            new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height),
            ImageLockMode.WriteOnly,
            PixelFormat.Format24bppRgb
        );

        int srcWidth = bitmap.Width;
        int srcHeight = bitmap.Height;
        IntPtr srcPtr = bitmap.GetPixels();

        unsafe
        {
            byte* src = (byte*)srcPtr.ToPointer();
            byte* dest = (byte*)bmpData.Scan0.ToPointer();
            int destStride = bmpData.Stride;

            for (int y = 0; y < srcHeight; y++)
            {
                byte* srcRow = src + y * srcWidth * 4;
                byte* dstRow = dest + y * destStride;

                for (int x = 0; x < srcWidth; x++)
                {
                    // SKBitmap 是 BGRA 8888 格式
                    // System.Drawing 24bpp RGB 格式
                    dstRow[x * 3 + 2] = srcRow[x * 4 + 0]; // B → R
                    dstRow[x * 3 + 1] = srcRow[x * 4 + 1]; // G → G
                    dstRow[x * 3 + 0] = srcRow[x * 4 + 2]; // R → B
                    // 忽略 Alpha 通道
                }
            }
        }

        bmp.UnlockBits(bmpData);

        bmp.Save(ms, ImageFormat.Bmp);

        return ms.ToArray();
    }

    private static ImageCodecInfo? GetEncoder(ImageFormat format)
    {
        ImageCodecInfo[] codecs = ImageCodecInfo.GetImageEncoders();
        foreach (ImageCodecInfo codec in codecs)
        {
            if (codec.FormatID == format.Guid)
            {
                return codec;
            }
        }
        return null;
    }

    /// <summary>
    /// 使用 SkiaSharp 进行 BMP 编码（降级路径）
    /// </summary>
    private static byte[] EncodeWithSkiaSharp(SKBitmap bitmap, int width, int height)
    {
        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData? encoded = image.Encode(SKEncodedImageFormat.Bmp, 100);
        if (encoded is not null)
        {
            return encoded.ToArray();
        }

        using SKBitmap converted = bitmap.Copy(SKColorType.Rgba8888);
        using SKImage convertedImage = SKImage.FromBitmap(converted);
        using SKData fallback =
            convertedImage.Encode(SKEncodedImageFormat.Bmp, 100)
            ?? throw new InvalidOperationException($"BMP 编码失败：{width}x{height}");
        return fallback.ToArray();
    }

    /// <summary>
    /// 快速缩放 Bayer 数据（使用整数运算，避免浮点运算开销）。
    /// </summary>
    private static byte[] ScaleBayerFast(
        byte[] bayer,
        int srcWidth,
        int srcHeight,
        int dstWidth,
        int dstHeight
    )
    {
        byte[] result = new byte[dstWidth * dstHeight];
        int srcY = 0;
        int yStep = (srcHeight << 16) / dstHeight;

        for (int dstY = 0; dstY < dstHeight; dstY++)
        {
            int srcY0 = srcY >> 16;
            int srcY1 = Math.Min(srcY0 + 1, srcHeight - 1);
            int yFrac = srcY & 0xFFFF;
            srcY += yStep;

            int srcX = 0;
            int xStep = (srcWidth << 16) / dstWidth;

            for (int dstX = 0; dstX < dstWidth; dstX++)
            {
                int srcX0 = srcX >> 16;
                int srcX1 = Math.Min(srcX0 + 1, srcWidth - 1);
                int xFrac = srcX & 0xFFFF;
                srcX += xStep;

                int tl = bayer[srcY0 * srcWidth + srcX0];
                int tr = bayer[srcY0 * srcWidth + srcX1];
                int bl = bayer[srcY1 * srcWidth + srcX0];
                int br = bayer[srcY1 * srcWidth + srcX1];

                int top = tl + ((tr - tl) * xFrac >> 16);
                int bottom = bl + ((br - bl) * xFrac >> 16);
                int val = top + ((bottom - top) * yFrac >> 16);

                result[dstY * dstWidth + dstX] = (byte)Math.Clamp(val, 0, 255);
            }
        }

        return result;
    }

    /// <summary>
    /// 缩放单通道 Bayer 数据（性能优化：先缩放再解马赛克）。
    /// 使用简单的平均缩放，对于 Bayer 数据足够精确，且比后续解马赛克+缩放快得多。
    /// </summary>
    private static byte[] ScaleBayer(
        byte[] bayer,
        int srcWidth,
        int srcHeight,
        int dstWidth,
        int dstHeight
    )
    {
        byte[] result = new byte[dstWidth * dstHeight];
        float xScale = (float)srcWidth / dstWidth;
        float yScale = (float)srcHeight / dstHeight;

        for (int dstY = 0; dstY < dstHeight; dstY++)
        {
            int srcY0 = (int)(dstY * yScale);
            int srcY1 = Math.Min(srcY0 + 1, srcHeight - 1);
            float yFrac = (dstY * yScale) - srcY0;

            for (int dstX = 0; dstX < dstWidth; dstX++)
            {
                int srcX0 = (int)(dstX * xScale);
                int srcX1 = Math.Min(srcX0 + 1, srcWidth - 1);
                float xFrac = (dstX * xScale) - srcX0;

                // 双线性插值
                float tl = bayer[srcY0 * srcWidth + srcX0];
                float tr = bayer[srcY0 * srcWidth + srcX1];
                float bl = bayer[srcY1 * srcWidth + srcX0];
                float br = bayer[srcY1 * srcWidth + srcX1];

                float top = tl * (1 - xFrac) + tr * xFrac;
                float bottom = bl * (1 - xFrac) + br * xFrac;
                float val = top * (1 - yFrac) + bottom * yFrac;

                result[dstY * dstWidth + dstX] = (byte)Math.Clamp(val, 0, 255);
            }
        }

        return result;
    }

    /// <summary>
    /// 按最大宽度缩放预览图，避免全分辨率帧持续压垮浏览器和 SignalR。
    /// </summary>
    private static SKBitmap? ScaleBitmap(SKBitmap source, int maxOutputWidth)
    {
        if (maxOutputWidth <= 0 || source.Width <= maxOutputWidth)
        {
            return null;
        }

        int targetWidth = maxOutputWidth;
        int targetHeight = Math.Max(
            1,
            (int)Math.Round(source.Height * (targetWidth / (double)source.Width))
        );
        var resized = new SKBitmap(
            targetWidth,
            targetHeight,
            SKColorType.Bgra8888,
            SKAlphaType.Opaque
        );
        if (!source.ScalePixels(resized, SKSamplingOptions.Default))
        {
            resized.Dispose();
            return null;
        }

        return resized;
    }

    /// <summary>
    /// 基于已解马赛克的彩色像素数据计算帧图像质量评分。
    /// 采用 4 倍下采样加速计算，误差可忽略。
    /// </summary>
    /// <param name="colorPixels">RGB 或 BGR 三通道像素数组（紧凑排列，行宽=width*3）</param>
    /// <param name="width">图像宽度</param>
    /// <param name="height">图像高度</param>
    /// <param name="isBgr">true=BGR 通道顺序，false=RGB</param>
    private static FrameQualityScore ComputeFrameQuality(
        byte[] colorPixels,
        int width,
        int height,
        bool isBgr
    )
    {
        // ── 1. 下采样转灰度（step=4 → 1/16 像素量，速度快，精度足够）────────────
        const int Step = 4;
        int sw = (width + Step - 1) / Step;
        int sh = (height + Step - 1) / Step;
        var gray = new byte[sw * sh];
        int gi = 0;
        for (int y = 0; y < height && gi < gray.Length; y += Step)
        {
            for (int x = 0; x < width && gi < gray.Length; x += Step)
            {
                int src = (y * width + x) * 3;
                if (src + 2 >= colorPixels.Length)
                    break;
                int r = isBgr ? colorPixels[src + 2] : colorPixels[src];
                int g = colorPixels[src + 1];
                int b = isBgr ? colorPixels[src] : colorPixels[src + 2];
                gray[gi++] = (byte)((77 * r + 150 * g + 29 * b) >> 8);
            }
        }

        int totalPixels = gi;
        if (totalPixels < 9)
        {
            return new FrameQualityScore
            {
                FocusScore = 50f,
                ApertureScore = 50f,
                ApertureHint = ApertureHint.Good,
            };
        }

        // ── 2. 对焦评分：Laplacian 方差（对角线 5 点核，中心=4，四邻=-1）────────
        double lapSumSq = 0;
        int lapCount = 0;
        for (int y = 1; y < sh - 1; y++)
        {
            for (int x = 1; x < sw - 1; x++)
            {
                int idx = y * sw + x;
                int lap =
                    4 * gray[idx] - gray[idx - 1] - gray[idx + 1] - gray[idx - sw] - gray[idx + sw];
                lapSumSq += (double)lap * lap;
                lapCount++;
            }
        }

        double variance = lapCount > 0 ? lapSumSq / lapCount : 0;
        // log10 映射：variance=1→0分，variance=10000→100分（对数刻度感知更线性）
        float focusScore = (float)
            Math.Clamp(Math.Log10(Math.Max(1.0, variance)) / Math.Log10(10000.0) * 100.0, 0, 100);

        // ── 3. 光圈（曝光）评分：过曝/欠曝像素比例 + 均值偏差 ────────────────────
        var histogram = new int[256];
        long pixelSum = 0;
        for (int i = 0; i < totalPixels; i++)
        {
            histogram[gray[i]]++;
            pixelSum += gray[i];
        }

        double mean = (double)pixelSum / totalPixels;

        // 过曝：值 > 245；欠曝：值 < 10
        int overCount = 0;
        int underCount = 0;
        for (int v = 246; v < 256; v++)
            overCount += histogram[v];
        for (int v = 0; v < 10; v++)
            underCount += histogram[v];

        float overRatio = (float)overCount / totalPixels;
        float underRatio = (float)underCount / totalPixels;

        // 过曝惩罚（3% 以上过曝即接近满分扣罚）
        float clipPenalty = Math.Clamp(
            overRatio / 0.03f * 0.8f + underRatio / 0.08f * 0.3f,
            0f,
            1f
        );
        // 均值偏差惩罚（理想均值约 115，允许 ±80 范围）
        float meanPenalty = Math.Clamp((float)Math.Abs(mean - 115) / 80f, 0f, 1f) * 0.3f;
        float apertureScore = Math.Clamp((1f - clipPenalty) * (1f - meanPenalty) * 100f, 0f, 100f);

        // 光圈调节建议
        ApertureHint hint =
            overRatio > 0.03f ? ApertureHint.Decrease
            : underRatio > 0.06f || mean < 35 ? ApertureHint.Increase
            : ApertureHint.Good;

        return new FrameQualityScore
        {
            FocusScore = focusScore,
            ApertureScore = apertureScore,
            ApertureHint = hint,
        };
    }

    /// <summary>
    /// 标准化图像顺时针旋转角度。
    /// </summary>
    private static int NormalizeImageRotationAngle(int imageRotationAngle)
    {
        int normalizedAngle = imageRotationAngle % 360;
        if (normalizedAngle < 0)
        {
            normalizedAngle += 360;
        }

        return normalizedAngle switch
        {
            0 or 90 or 180 or 270 => normalizedAngle,
            _ => throw new ArgumentOutOfRangeException(
                nameof(imageRotationAngle),
                "图像旋转角度仅支持 0、90、180、270 度"
            ),
        };
    }

    /// <summary>
    /// 按顺时针角度旋转紧凑像素数组，支持单通道 Bayer 和三通道 BGR/RGB。
    /// </summary>
    private static byte[] RotateInterleavedPixels(
        byte[] pixels,
        int width,
        int height,
        int channelCount,
        int imageRotationAngle
    )
    {
        int normalizedAngle = NormalizeImageRotationAngle(imageRotationAngle);
        if (normalizedAngle == 0)
        {
            return pixels;
        }

        int targetWidth = normalizedAngle is 90 or 270 ? height : width;
        int targetHeight = normalizedAngle is 90 or 270 ? width : height;
        byte[] rotated = new byte[targetWidth * targetHeight * channelCount];

        for (int sourceRow = 0; sourceRow < height; sourceRow++)
        {
            for (int sourceColumn = 0; sourceColumn < width; sourceColumn++)
            {
                int targetRow;
                int targetColumn;
                switch (normalizedAngle)
                {
                    case 90:
                        targetRow = sourceColumn;
                        targetColumn = height - 1 - sourceRow;
                        break;
                    case 180:
                        targetRow = height - 1 - sourceRow;
                        targetColumn = width - 1 - sourceColumn;
                        break;
                    default:
                        targetRow = width - 1 - sourceColumn;
                        targetColumn = sourceRow;
                        break;
                }

                int sourceIndex = (sourceRow * width + sourceColumn) * channelCount;
                int targetIndex = (targetRow * targetWidth + targetColumn) * channelCount;
                for (int channelIndex = 0; channelIndex < channelCount; channelIndex++)
                {
                    rotated[targetIndex + channelIndex] = pixels[sourceIndex + channelIndex];
                }
            }
        }

        return rotated;
    }

    /// <summary>
    /// 根据旋转角度推导旋转后的 Bayer 排列，确保先旋转原始 Bayer 再解马赛克时颜色仍正确。
    /// </summary>
    private static BayerPattern GetRotatedBayerPattern(
        BayerPattern sourcePattern,
        int sourceWidth,
        int sourceHeight,
        int imageRotationAngle
    )
    {
        int normalizedAngle = NormalizeImageRotationAngle(imageRotationAngle);
        if (normalizedAngle == 0)
        {
            return sourcePattern;
        }

        BayerColor topLeft = GetRotatedBayerColor(
            sourcePattern,
            sourceWidth,
            sourceHeight,
            normalizedAngle,
            0,
            0
        );
        BayerColor topRight = GetRotatedBayerColor(
            sourcePattern,
            sourceWidth,
            sourceHeight,
            normalizedAngle,
            0,
            1
        );
        BayerColor bottomLeft = GetRotatedBayerColor(
            sourcePattern,
            sourceWidth,
            sourceHeight,
            normalizedAngle,
            1,
            0
        );
        BayerColor bottomRight = GetRotatedBayerColor(
            sourcePattern,
            sourceWidth,
            sourceHeight,
            normalizedAngle,
            1,
            1
        );

        return (topLeft, topRight, bottomLeft, bottomRight) switch
        {
            (BayerColor.Green, BayerColor.Blue, BayerColor.Red, BayerColor.Green) =>
                BayerPattern.Gbrg,
            (BayerColor.Red, BayerColor.Green, BayerColor.Green, BayerColor.Blue) =>
                BayerPattern.Rggb,
            (BayerColor.Green, BayerColor.Red, BayerColor.Blue, BayerColor.Green) =>
                BayerPattern.Grbg,
            (BayerColor.Blue, BayerColor.Green, BayerColor.Green, BayerColor.Red) =>
                BayerPattern.Bggr,
            _ => sourcePattern,
        };
    }

    private static BayerColor GetRotatedBayerColor(
        BayerPattern sourcePattern,
        int sourceWidth,
        int sourceHeight,
        int imageRotationAngle,
        int targetRow,
        int targetColumn
    )
    {
        int sourceRow;
        int sourceColumn;
        switch (imageRotationAngle)
        {
            case 90:
                sourceRow = sourceHeight - 1 - targetColumn;
                sourceColumn = targetRow;
                break;
            case 180:
                sourceRow = sourceHeight - 1 - targetRow;
                sourceColumn = sourceWidth - 1 - targetColumn;
                break;
            case 270:
                sourceRow = targetColumn;
                sourceColumn = sourceWidth - 1 - targetRow;
                break;
            default:
                sourceRow = targetRow;
                sourceColumn = targetColumn;
                break;
        }

        return GetBayerColor(sourcePattern, sourceRow, sourceColumn);
    }

    /// <summary>
    /// 对 Bayer 单通道图像执行双线性插值解马赛克，直接输出到 Bgra8888 缓冲区。
    /// 避免中间 RGB 数组，减少一次内存分配和复制。
    /// </summary>
    private static unsafe void DemosaicBayerToBgra8888(
        byte[] bayer,
        int width,
        int height,
        BayerPattern bayerPattern,
        IntPtr dstPtr
    )
    {
        byte* dst = (byte*)dstPtr.ToPointer();
        int dstStride = width * 4;

        for (int y = 0; y < height; y++)
        {
            byte* rowDst = dst + y * dstStride;

            for (int x = 0; x < width; x++)
            {
                int idx = y * width + x;
                bool evenRow = (y & 1) == 0;
                bool evenCol = (x & 1) == 0;

                byte r,
                    g,
                    b;

                if (bayerPattern == BayerPattern.Gbrg)
                {
                    if (evenRow)
                    {
                        if (evenCol)
                        {
                            b = bayer[idx];
                            g = (byte)(
                                (
                                    bayer[idx - 1]
                                    + bayer[idx + 1]
                                    + bayer[idx - width]
                                    + bayer[idx + width]
                                ) >> 2
                            );
                            r = (byte)(
                                (
                                    bayer[idx - width - 1]
                                    + bayer[idx - width + 1]
                                    + bayer[idx + width - 1]
                                    + bayer[idx + width + 1]
                                ) >> 2
                            );
                        }
                        else
                        {
                            r = bayer[idx];
                            g = (byte)(
                                (
                                    bayer[idx - 1]
                                    + bayer[idx + 1]
                                    + bayer[idx - width]
                                    + bayer[idx + width]
                                ) >> 2
                            );
                            b = (byte)(
                                (
                                    bayer[idx - width - 1]
                                    + bayer[idx - width + 1]
                                    + bayer[idx + width - 1]
                                    + bayer[idx + width + 1]
                                ) >> 2
                            );
                        }
                    }
                    else
                    {
                        if (evenCol)
                        {
                            r = bayer[idx];
                            g = (byte)(
                                (
                                    bayer[idx - 1]
                                    + bayer[idx + 1]
                                    + bayer[idx - width]
                                    + bayer[idx + width]
                                ) >> 2
                            );
                            b = (byte)(
                                (
                                    bayer[idx - width - 1]
                                    + bayer[idx - width + 1]
                                    + bayer[idx + width - 1]
                                    + bayer[idx + width + 1]
                                ) >> 2
                            );
                        }
                        else
                        {
                            g = bayer[idx];
                            r = (byte)((bayer[idx - 1] + bayer[idx + 1]) >> 1);
                            b = (byte)((bayer[idx - width] + bayer[idx + width]) >> 1);
                        }
                    }
                }
                else
                {
                    r = g = b = bayer[idx];
                }

                rowDst[x * 4] = b;
                rowDst[x * 4 + 1] = g;
                rowDst[x * 4 + 2] = r;
                rowDst[x * 4 + 3] = 255;
            }
        }
    }

    /// <summary>
    /// 对 Bayer 单通道图像执行双线性插值解马赛克，输出全分辨率 RGB 三通道图像。
    /// </summary>
    private static byte[] DemosaicBayer(
        byte[] bayer,
        int width,
        int height,
        BayerPattern bayerPattern
    )
    {
        byte[] rgb = new byte[width * height * 3];

        byte GetBayer(int row, int column)
        {
            int clampedRow = Math.Clamp(row, 0, height - 1);
            int clampedColumn = Math.Clamp(column, 0, width - 1);
            return bayer[clampedRow * width + clampedColumn];
        }

        byte Average2(int firstValue, int secondValue) => (byte)((firstValue + secondValue) >> 1);

        byte Average4(int firstValue, int secondValue, int thirdValue, int fourthValue) =>
            (byte)((firstValue + secondValue + thirdValue + fourthValue) >> 2);

        for (int row = 0; row < height; row++)
        {
            for (int column = 0; column < width; column++)
            {
                int targetIndex = (row * width + column) * 3;
                BayerColor pixelColor = GetBayerColor(bayerPattern, row, column);
                byte redValue;
                byte greenValue;
                byte blueValue;

                if (pixelColor == BayerColor.Red)
                {
                    redValue = GetBayer(row, column);
                    greenValue = Average4(
                        GetBayer(row, column - 1),
                        GetBayer(row, column + 1),
                        GetBayer(row - 1, column),
                        GetBayer(row + 1, column)
                    );
                    blueValue = Average4(
                        GetBayer(row - 1, column - 1),
                        GetBayer(row - 1, column + 1),
                        GetBayer(row + 1, column - 1),
                        GetBayer(row + 1, column + 1)
                    );
                }
                else if (pixelColor == BayerColor.Blue)
                {
                    blueValue = GetBayer(row, column);
                    greenValue = Average4(
                        GetBayer(row, column - 1),
                        GetBayer(row, column + 1),
                        GetBayer(row - 1, column),
                        GetBayer(row + 1, column)
                    );
                    redValue = Average4(
                        GetBayer(row - 1, column - 1),
                        GetBayer(row - 1, column + 1),
                        GetBayer(row + 1, column - 1),
                        GetBayer(row + 1, column + 1)
                    );
                }
                else
                {
                    greenValue = GetBayer(row, column);
                    bool redNeighborsAreHorizontal =
                        GetBayerColor(bayerPattern, row, column - 1) == BayerColor.Red
                        || GetBayerColor(bayerPattern, row, column + 1) == BayerColor.Red;
                    if (redNeighborsAreHorizontal)
                    {
                        redValue = Average2(GetBayer(row, column - 1), GetBayer(row, column + 1));
                        blueValue = Average2(GetBayer(row - 1, column), GetBayer(row + 1, column));
                    }
                    else
                    {
                        redValue = Average2(GetBayer(row - 1, column), GetBayer(row + 1, column));
                        blueValue = Average2(GetBayer(row, column - 1), GetBayer(row, column + 1));
                    }
                }

                rgb[targetIndex] = redValue;
                rgb[targetIndex + 1] = greenValue;
                rgb[targetIndex + 2] = blueValue;
            }
        }

        return rgb;
    }

    private static BayerColor GetBayerColor(BayerPattern bayerPattern, int row, int column)
    {
        bool evenRow = (row & 1) == 0;
        bool evenColumn = (column & 1) == 0;

        if (bayerPattern == BayerPattern.Rggb)
        {
            return evenRow
                ? evenColumn
                    ? BayerColor.Red
                    : BayerColor.Green
                : evenColumn
                    ? BayerColor.Green
                    : BayerColor.Blue;
        }

        if (bayerPattern == BayerPattern.Grbg)
        {
            return evenRow
                ? evenColumn
                    ? BayerColor.Green
                    : BayerColor.Red
                : evenColumn
                    ? BayerColor.Blue
                    : BayerColor.Green;
        }

        if (bayerPattern == BayerPattern.Bggr)
        {
            return evenRow
                ? evenColumn
                    ? BayerColor.Blue
                    : BayerColor.Green
                : evenColumn
                    ? BayerColor.Green
                    : BayerColor.Red;
        }

        return evenRow
            ? evenColumn
                ? BayerColor.Green
                : BayerColor.Blue
            : evenColumn
                ? BayerColor.Red
                : BayerColor.Green;
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

                // requiresNew: true 创建完全独立的 UoW，覆盖 Task.Run 从 HTTP 请求继承的
                // AsyncLocal<IUnitOfWork>，确保后台日志写入使用独立 DbContext，
                // 不与请求的 UoW.CompleteAsync() → SaveChangesAsync() 产生并发冲突
                IUnitOfWorkManager uowManager =
                    scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
                using IUnitOfWork uow = uowManager.Begin(requiresNew: true);

                // repo 必须在 Begin(requiresNew: true) 之后获取，
                // 此时 Current 已指向新 UoW，GetDbContextAsync() 将使用新的独立 DbContext
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

                // autoSave: false，由 uow.CompleteAsync() 统一提交，避免直接调用 SaveChangesAsync
                await repo.InsertAsync(log, autoSave: false);
                await uow.CompleteAsync();
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
    /// 获取或创建单台相机的采集上下文
    /// </summary>
    private CameraCaptureState GetCaptureState(int cameraIndex)
    {
        return _captureStates.GetOrAdd(cameraIndex, _ => new CameraCaptureState());
    }

    /// <summary>
    /// 开始一次帧等待，登记等待者并返回 SDK 分配的帧结构体的引用。
    /// 使用 ref 返回避免值拷贝，确保 TUCAM_Buf_WaitForFrame 直接修改原始数据。
    /// </summary>
    private ref TUCamFrame BeginFrameWait(int cameraIndex, out CameraCaptureState captureState)
    {
        captureState = GetCaptureState(cameraIndex);
        lock (captureState.SyncRoot)
        {
            if (
                !captureState.IsCapturing
                || captureState.StopRequested
                || captureState.Frame.pBuffer == IntPtr.Zero
            )
            {
                throw new InvalidOperationException(
                    $"{LogTag} Camera {cameraIndex} is not capturing or frame buffer is not allocated. Call StartCaptureAsync first."
                );
            }

            captureState.ActiveWaiters++;
            return ref captureState.Frame;
        }
    }

    /// <summary>
    /// 结束一次帧等待，保存最新帧结构体并唤醒等待停止采集的线程。
    /// </summary>
    private static void EndFrameWait(CameraCaptureState captureState, TUCamFrame frame)
    {
        lock (captureState.SyncRoot)
        {
            if (captureState.ActiveWaiters > 0)
            {
                captureState.ActiveWaiters--;
            }

            if (captureState.IsCapturing && frame.pBuffer != IntPtr.Zero)
            {
                captureState.Frame = frame;
            }

            Monitor.PulseAll(captureState.SyncRoot);
        }
    }

    /// <summary>
    /// 单台相机的采集上下文。
    /// </summary>
    private sealed class CameraCaptureState
    {
        public object SyncRoot { get; } = new();

        public bool IsCapturing { get; set; }

        public bool StopRequested { get; set; }

        public int ActiveWaiters { get; set; }

        public TUCamFrame Frame;

        /// <summary>
        /// 当前相机是否持有进程级单活 Cap_Start 锁（_capStartActiveLock）。
        /// StartCaptureAsync 成功后置 true，StopCaptureAsync 释放锁时置 false。
        /// </summary>
        public bool HoldsCapStartLock { get; set; }

        /// <summary>
        /// 缓存的像素格式信息，避免每帧调用 GenICam 读取
        /// </summary>
        public string? CachedPixelFormat { get; set; }

        public long? CachedPixelFormatValue { get; set; }

        public long? CachedPixelSize { get; set; }

        public int CachedBitDepth { get; set; }
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

    // ─── GenICam 节点读写辅助方法 ────────────────────────────────────────────

    /// <summary>
    /// 通过 GenICam 节点名称读取整数/枚举/布尔节点当前值，失败返回 null
    /// </summary>
    private long? GenICamGetInt(IntPtr handle, string nodeName)
    {
        IntPtr pName = Marshal.StringToHGlobalAnsi(nodeName);
        try
        {
            TucamElement elem = default;
            elem.pName = pName;
            TUCamRet ret = TUCamNative.TUCAM_GenICam_GetElementValue(handle, ref elem, 0);
            if (ret != TUCamRet.Success)
            {
                _logger.LogWarning(
                    "{Tag} GenICamGetInt [{Node}] 返回 {Ret}",
                    LogTag,
                    nodeName,
                    ret
                );
                return null;
            }
            return elem.uValue.IntValue.nVal;
        }
        finally
        {
            Marshal.FreeHGlobal(pName);
        }
    }

    /// <summary>
    /// 通过 GenICam 节点名称读取整数节点的当前值/最小值/最大值，失败返回全零
    /// </summary>
    private (long current, long min, long max) GenICamGetIntRange(IntPtr handle, string nodeName)
    {
        IntPtr pName = Marshal.StringToHGlobalAnsi(nodeName);
        try
        {
            TucamElement elem = default;
            elem.pName = pName;
            TUCamRet ret = TUCamNative.TUCAM_GenICam_GetElementValue(handle, ref elem, 0);
            if (ret != TUCamRet.Success)
            {
                _logger.LogWarning(
                    "{Tag} GenICamGetIntRange [{Node}] 返回 {Ret}",
                    LogTag,
                    nodeName,
                    ret
                );
                return (0, 0, 0);
            }
            return (
                elem.uValue.IntValue.nVal,
                elem.uValue.IntValue.nMin,
                elem.uValue.IntValue.nMax
            );
        }
        finally
        {
            Marshal.FreeHGlobal(pName);
        }
    }

    /// <summary>
    /// 通过 GenICam 节点名称设置整数/枚举/布尔节点值，返回 SDK 结果码
    /// </summary>
    private TUCamRet GenICamSetInt(IntPtr handle, string nodeName, long value)
    {
        IntPtr pName = Marshal.StringToHGlobalAnsi(nodeName);
        try
        {
            TucamElement elem = default;
            elem.pName = pName;
            // 先 Get 获取节点元数据（类型等信息）
            TUCamRet getRet = TUCamNative.TUCAM_GenICam_GetElementValue(handle, ref elem, 0);
            if (getRet != TUCamRet.Success)
            {
                _logger.LogWarning(
                    "{Tag} GenICamSetInt [{Node}] Get 返回 {Ret}，跳过",
                    LogTag,
                    nodeName,
                    getRet
                );
                return getRet;
            }
            elem.pName = pName; // SDK 可能改变 pName，重置为调用方管理的指针
            elem.uValue.IntValue.nVal = value;
            return TUCamNative.TUCAM_GenICam_SetElementValue(handle, ref elem, 0);
        }
        finally
        {
            Marshal.FreeHGlobal(pName);
        }
    }

    /// <summary>
    /// 通过 GenICam 节点名称读取浮点节点当前值，失败返回 null
    /// </summary>
    private double? GenICamGetFloat(IntPtr handle, string nodeName)
    {
        IntPtr pName = Marshal.StringToHGlobalAnsi(nodeName);
        try
        {
            TucamElement elem = default;
            elem.pName = pName;
            TUCamRet ret = TUCamNative.TUCAM_GenICam_GetElementValue(handle, ref elem, 0);
            if (ret != TUCamRet.Success)
            {
                _logger.LogWarning(
                    "{Tag} GenICamGetFloat [{Node}] 返回 {Ret}",
                    LogTag,
                    nodeName,
                    ret
                );
                return null;
            }
            return elem.uValue.FloatValue.dbVal;
        }
        finally
        {
            Marshal.FreeHGlobal(pName);
        }
    }

    /// <summary>
    /// 通过 GenICam 节点名称读取浮点节点的当前值/最小值/最大值，失败返回全零
    /// </summary>
    private (double current, double min, double max) GenICamGetFloatRange(
        IntPtr handle,
        string nodeName
    )
    {
        IntPtr pName = Marshal.StringToHGlobalAnsi(nodeName);
        try
        {
            TucamElement elem = default;
            elem.pName = pName;
            TUCamRet ret = TUCamNative.TUCAM_GenICam_GetElementValue(handle, ref elem, 0);
            if (ret != TUCamRet.Success)
            {
                _logger.LogWarning(
                    "{Tag} GenICamGetFloatRange [{Node}] 返回 {Ret}",
                    LogTag,
                    nodeName,
                    ret
                );
                return (0, 0, 0);
            }
            return (
                elem.uValue.FloatValue.dbVal,
                elem.uValue.FloatValue.dbMin,
                elem.uValue.FloatValue.dbMax
            );
        }
        finally
        {
            Marshal.FreeHGlobal(pName);
        }
    }

    /// <summary>
    /// 通过 GenICam 节点名称设置浮点节点值，返回 SDK 结果码
    /// </summary>
    private TUCamRet GenICamSetFloat(IntPtr handle, string nodeName, double value)
    {
        IntPtr pName = Marshal.StringToHGlobalAnsi(nodeName);
        try
        {
            TucamElement elem = default;
            elem.pName = pName;
            TUCamRet getRet = TUCamNative.TUCAM_GenICam_GetElementValue(handle, ref elem, 0);
            if (getRet != TUCamRet.Success)
            {
                _logger.LogWarning(
                    "{Tag} GenICamSetFloat [{Node}] Get 返回 {Ret}，跳过",
                    LogTag,
                    nodeName,
                    getRet
                );
                return getRet;
            }
            elem.pName = pName; // SDK 可能改变 pName，重置为调用方管理的指针
            elem.uValue.FloatValue.dbVal = value;
            return TUCamNative.TUCAM_GenICam_SetElementValue(handle, ref elem, 0);
        }
        finally
        {
            Marshal.FreeHGlobal(pName);
        }
    }

    /// <summary>
    /// 通过 GenICam 节点读取 String 类型值（内部辅助，不抛异常）
    /// 注意：对 String 节点，GetElementValue 只返回字符串长度（nVal），pTransfer 始终为 null；
    /// 须改用 ElementAttr，其 pTransfer 才携带实际字符串内容。
    /// </summary>
    private string? GenICamGetString(IntPtr handle, string nodeName)
    {
        IntPtr pName = Marshal.StringToHGlobalAnsi(nodeName);
        try
        {
            TucamElement elem = default;
            // ElementAttr 将 pName 作为独立参数传入，xml=0（TU_CAMERA_XML）
            TUCamRet ret = TUCamNative.TUCAM_GenICam_ElementAttr(handle, ref elem, pName, 0);
            if (ret != TUCamRet.Success)
            {
                _logger.LogWarning(
                    "{Tag} GenICamGetString [{Node}] 返回 {Ret}",
                    LogTag,
                    nodeName,
                    ret
                );
                return null;
            }
            string? value = Marshal.PtrToStringAnsi(elem.pTransfer);
            return SanitizeGenICamString(value);
        }
        finally
        {
            Marshal.FreeHGlobal(pName);
        }
    }

    /// <summary>
    /// 通过 GenICam 节点名称设置字符串节点值，返回 SDK 结果码
    /// </summary>
    private TUCamRet GenICamSetString(IntPtr handle, string nodeName, string value)
    {
        IntPtr pName = Marshal.StringToHGlobalAnsi(nodeName);
        IntPtr pValue = Marshal.StringToHGlobalAnsi(value);
        try
        {
            TucamElement elem = default;
            TUCamRet attrRet = TUCamNative.TUCAM_GenICam_ElementAttr(handle, ref elem, pName, 0);
            if (attrRet != TUCamRet.Success)
            {
                _logger.LogWarning(
                    "{Tag} GenICamSetString [{Node}] Attr 返回 {Ret}，跳过",
                    LogTag,
                    nodeName,
                    attrRet
                );
                return attrRet;
            }

            long byteLength = GetNullTerminatedByteLength(pValue);
            if (elem.uValue.IntValue.nMax > 0 && byteLength > elem.uValue.IntValue.nMax)
            {
                _logger.LogWarning(
                    "{Tag} GenICamSetString [{Node}] 字符串长度 {Length} 超过最大长度 {MaxLength}，跳过",
                    LogTag,
                    nodeName,
                    byteLength,
                    elem.uValue.IntValue.nMax
                );
                return TUCamRet.InvalidValue;
            }

            elem.pName = pName;
            elem.pTransfer = pValue;
            elem.uValue.IntValue.nVal = byteLength;
            return TUCamNative.TUCAM_GenICam_SetElementValue(handle, ref elem, 0);
        }
        finally
        {
            Marshal.FreeHGlobal(pValue);
            Marshal.FreeHGlobal(pName);
        }
    }

    /// <summary>
    /// 清理 SDK 字符串中的不可见控制字符
    /// </summary>
    private static string? SanitizeGenICamString(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        char[] buffer = new char[value.Length];
        int length = 0;
        foreach (char character in value)
        {
            if (
                char.IsControl(character)
                && character != '\t'
                && character != '\r'
                && character != '\n'
            )
            {
                continue;
            }

            buffer[length] = character;
            length++;
        }

        return length == value.Length ? value : new string(buffer, 0, length);
    }

    /// <summary>
    /// 获取非托管 ANSI 字符串的字节长度，不包含结尾空字符
    /// </summary>
    private static long GetNullTerminatedByteLength(IntPtr stringPointer)
    {
        int length = 0;
        while (Marshal.ReadByte(stringPointer, length) != 0)
        {
            length++;
        }

        return length;
    }

    /// <inheritdoc/>
    public Task<string?> GetGenICamStringAsync(int cameraIndex, string nodeName)
    {
        ThrowIfDisposed();
        IntPtr handle = GetHandle(cameraIndex);
        return Task.FromResult(GenICamGetString(handle, nodeName));
    }

    /// <inheritdoc/>
    public Task SetGenICamStringAsync(int cameraIndex, string nodeName, string value)
    {
        ThrowIfDisposed();
        IntPtr handle = GetHandle(cameraIndex);
        string sanitizedValue = SanitizeGenICamString(value) ?? string.Empty;
        TUCamRet ret = GenICamSetString(handle, nodeName, sanitizedValue);
        if (ret != TUCamRet.Success)
        {
            // Bug 1b：String 写入失败必须抛出，避免前端误显示「保存成功」
            _logger.LogWarning(
                "{Tag} GenICam SetString {Node}='{Value}' 返回 {Ret}，未生效",
                LogTag,
                nodeName,
                sanitizedValue,
                ret
            );
            throw new InvalidOperationException(
                $"{LogTag} GenICam SetString {nodeName}='{sanitizedValue}' 失败 ret=0x{(int)ret:X8}"
            );
        }
        return Task.CompletedTask;
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

    // ─── GenICam NodeMap 缓存与动态读取 ────────────────────────────────────

    /// <inheritdoc/>
    public GenICamNodeMap? GetCachedNodeMap(int cameraIndex)
    {
        ThrowIfDisposed();
        return _nodeMapCache.TryGetValue(cameraIndex, out var cached) ? cached.NodeMap : null;
    }

    /// <inheritdoc/>
    public GenICamDependencyGraph? GetCachedDependencyGraph(int cameraIndex)
    {
        ThrowIfDisposed();
        return _nodeMapCache.TryGetValue(cameraIndex, out var cached) ? cached.Graph : null;
    }

    /// <inheritdoc/>
    public void UpdateCachedNodeValues(int cameraIndex, IReadOnlyList<GenICamNodeValue> values)
    {
        ThrowIfDisposed();
        if (values is null || values.Count == 0)
        {
            return;
        }

        if (!_nodeMapCache.TryGetValue(cameraIndex, out var cached))
        {
            return;
        }

        GenICamNodeMap nodeMap = cached.NodeMap;
        foreach (GenICamNodeValue v in values)
        {
            if (!v.Success)
            {
                continue;
            }

            if (
                !string.IsNullOrEmpty(v.NodeName)
                && nodeMap.NodesByName.TryGetValue(v.NodeName, out GenICamNodeMeta? meta)
            )
            {
                // 将回读值写回可变属性，保持缓存与硬件一致
                meta.CurrentValue = v.Value;
                meta.Access = v.Access;
                meta.IsLocked = v.IsLocked;
            }
        }
    }

    /// <inheritdoc/>
    public async Task<GenICamNodeMap> RefreshGenICamNodeMapAsync(int cameraIndex)
    {
        ThrowIfDisposed();
        IntPtr handle = GetHandle(cameraIndex);
        return await ProbeAndCacheNodeMapAsync(cameraIndex, handle).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<GenICamNodeValue>> ReadGenICamNodesAsync(
        int cameraIndex,
        IReadOnlyList<string> nodeNames
    )
    {
        ThrowIfDisposed();
        IntPtr handle = GetHandle(cameraIndex);

        if (nodeNames is null || nodeNames.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<GenICamNodeValue>>(
                Array.Empty<GenICamNodeValue>()
            );
        }

        GenICamNodeMap? nodeMap = GetCachedNodeMap(cameraIndex);
        List<GenICamNodeValue> results = new(nodeNames.Count);

        foreach (string name in nodeNames)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            try
            {
                results.Add(ReadSingleGenICamNode(handle, name, nodeMap));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "{Tag} ReadGenICamNode [{Node}] 抛出异常", LogTag, name);
                results.Add(
                    new GenICamNodeValue
                    {
                        NodeName = name,
                        Success = false,
                        Error = ex.Message,
                    }
                );
            }
        }

        return Task.FromResult<IReadOnlyList<GenICamNodeValue>>(results);
    }

    /// <summary>
    /// 读取单个节点：根据缓存中的节点类型选择 Int / Float / String 解析方式，
    /// 缓存缺失时退化为基于 ElementAttr 返回类型的通用分支。
    /// </summary>
    private GenICamNodeValue ReadSingleGenICamNode(
        IntPtr handle,
        string nodeName,
        GenICamNodeMap? cachedMap
    )
    {
        TuElemType? hintType = null;
        if (
            cachedMap is not null
            && cachedMap.NodesByName.TryGetValue(nodeName, out GenICamNodeMeta? meta)
        )
        {
            hintType = meta.Type;
        }

        IntPtr pName = Marshal.StringToHGlobalAnsi(nodeName);
        try
        {
            TucamElement attrElement = default;
            TUCamRet attrRet = TUCamNative.TUCAM_GenICam_ElementAttr(
                handle,
                ref attrElement,
                pName,
                0
            );
            if (attrRet != TUCamRet.Success)
            {
                return new GenICamNodeValue
                {
                    NodeName = nodeName,
                    Success = false,
                    Error = $"ElementAttr ret=0x{(int)attrRet:X8}",
                };
            }

            TuElemType actualType = hintType ?? attrElement.Type;
            TuAccessMode access = attrElement.Access;
            bool isLocked = attrElement.IsLocked != 0;

            if (actualType is TuElemType.Command or TuElemType.Category or TuElemType.Port)
            {
                return new GenICamNodeValue
                {
                    NodeName = nodeName,
                    Success = true,
                    Value = null,
                    Access = access,
                    IsLocked = isLocked,
                };
            }

            TucamElement valElement = default;
            valElement.pName = pName;
            TUCamRet getRet = TUCamNative.TUCAM_GenICam_GetElementValue(handle, ref valElement, 0);
            if (getRet != TUCamRet.Success)
            {
                return new GenICamNodeValue
                {
                    NodeName = nodeName,
                    Success = false,
                    Access = access,
                    IsLocked = isLocked,
                    Error = $"GetElementValue ret=0x{(int)getRet:X8}",
                };
            }

            // String 节点兼容读取策略：
            // 1) 先走 GetElementValue 触发 SDK 刷新（解决 DeviceUserID 写后读旧值问题）
            // 2) 若 pTransfer 为空，再回退到 ElementAttr.pTransfer（兼容部分 RO 字符串节点）
            string value;
            if (actualType == TuElemType.String)
            {
                string? latest = SanitizeGenICamString(
                    Marshal.PtrToStringAnsi(valElement.pTransfer)
                );
                if (latest is null)
                {
                    latest = GenICamGetString(handle, nodeName);
                }
                value = latest ?? string.Empty;
            }
            else if (actualType == TuElemType.Float)
            {
                value = valElement.uValue.FloatValue.dbVal.ToString(
                    "G",
                    System.Globalization.CultureInfo.InvariantCulture
                );
            }
            else
            {
                value = valElement.uValue.IntValue.nVal.ToString(
                    System.Globalization.CultureInfo.InvariantCulture
                );
            }

            return new GenICamNodeValue
            {
                NodeName = nodeName,
                Success = true,
                Value = value,
                Access = access,
                IsLocked = isLocked,
            };
        }
        finally
        {
            Marshal.FreeHGlobal(pName);
        }
    }

    /// <summary>OpenCamera 后台预跑 NodeMap + 选择器依赖图（异常仅记录、不传播）</summary>
    private async Task PrewarmGenICamNodeMapAsync(int cameraIndex, IntPtr handle)
    {
        try
        {
            await ProbeAndCacheNodeMapAsync(cameraIndex, handle).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "{Tag} Camera {Index} GenICam 预跑失败", LogTag, cameraIndex);
        }
    }

    /// <summary>在互斥锁保护下：枚举 NodeMap → 探测依赖图 → 写缓存</summary>
    private async Task<GenICamNodeMap> ProbeAndCacheNodeMapAsync(int cameraIndex, IntPtr handle)
    {
        SemaphoreSlim semaphore = _nodeMapProbeLocks.GetOrAdd(
            cameraIndex,
            _ => new SemaphoreSlim(1, 1)
        );
        await semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            // Tucam SDK 存在进程级全局共享状态：不同相机的枚举/探测同样不能并发执行。
            // 此处先获取进程级全局锁，再执行 SDK 调用，确保跨相机串行。
            await _globalSdkLock.WaitAsync().ConfigureAwait(false);
            try
            {
                // 关键：TUCam SDK 的 GenICam 节点必须在至少一次采集启动后才能正确读取
                // 参考 SDK 示例：先调用 startcapture() 后才能调用 getallelementattr()
                InitGenICamNodeMap(handle);

                Stopwatch sw = Stopwatch.StartNew();
                GenICamNodeMap map = TucamGenICamEnumerator.Enumerate(handle, _logger);
                GenICamDependencyGraph graph = TucamGenICamDependencyProber.Probe(
                    handle,
                    map,
                    _logger
                );
                sw.Stop();

                _nodeMapCache[cameraIndex] = (map, graph);
                _logger.LogInformation(
                    "{Tag} Camera {Index} GenICam NodeMap 枚举完成：节点 {NodeCount} 个，依赖边 {EdgeCount} 条，耗时 {Ms}ms",
                    LogTag,
                    cameraIndex,
                    map.Nodes.Count,
                    graph.Edges.Count,
                    sw.ElapsedMilliseconds
                );

                return map;
            }
            finally
            {
                _globalSdkLock.Release();
            }
        }
        finally
        {
            semaphore.Release();
        }
    }

    /// <summary>
    /// 启动一次快速采集以初始化 GenICam NodeMap
    /// TUCam SDK 的 GenICam 节点必须在至少一次采集启动后才能正确读取
    /// 参考 SDK 示例代码：先 startcapture() 后才能 getallelementattr()
    /// </summary>
    private void InitGenICamNodeMap(IntPtr handle)
    {
        try
        {
            var frame = new TUCamFrame { uiRsdSize = 1 };
            TUCamRet ret = TUCamNative.TUCAM_Buf_Alloc(handle, ref frame);
            if (ret != TUCamRet.Success)
            {
                _logger.LogWarning(
                    "{Tag} InitGenICamNodeMap: TUCAM_Buf_Alloc failed: {RetCode}",
                    LogTag,
                    ret
                );
                return;
            }

            ret = TUCamNative.TUCAM_Cap_Start(handle, (uint)TUCamCaptureMode.Sequence);
            if (ret != TUCamRet.Success)
            {
                _logger.LogWarning(
                    "{Tag} InitGenICamNodeMap: TUCAM_Cap_Start failed: {RetCode}",
                    LogTag,
                    ret
                );
                TUCamNative.TUCAM_Buf_Release(handle);
                return;
            }

            ret = TUCamNative.TUCAM_Buf_WaitForFrame(handle, ref frame, 1000);
            if (ret != TUCamRet.Success)
            {
                _logger.LogWarning(
                    "{Tag} InitGenICamNodeMap: TUCAM_Buf_WaitForFrame failed: {RetCode}",
                    LogTag,
                    ret
                );
            }

            TUCamNative.TUCAM_Cap_Stop(handle);
            TUCamNative.TUCAM_Buf_Release(handle);

            _logger.LogInformation("{Tag} InitGenICamNodeMap: GenICam NodeMap 初始化完成", LogTag);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "{Tag} InitGenICamNodeMap 异常，继续执行", LogTag);
        }
    }
}
