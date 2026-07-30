using System.Text;
using AuroraStruct3D.Streaming;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AuroraStruct3D.Endpoints;

/// <summary>
/// 相机 MJPEG 流端点（Multipart/x-mixed-replace）。
/// 浏览器原生支持该格式：直接将端点 URL 赋给 <img src="..."> 即可持续播放。
/// 帧类型使用 BMP（无压缩），消除 JPEG 编码开销，保证最低延迟。
/// </summary>
public static class CameraStreamingEndpoints
{
    /// <summary>Multipart 分隔符标识（HTTP Content-Type 头中 boundary 的值）。
    /// 注意：RFC 2046 规定，分隔符标识本身不包含两个短横前缀；
    /// 每个 Part 真正发送的分隔符是 "--" + Boundary + "\r\n"。
    /// </summary>
    private const string Boundary = "auroraframe7f3d9a2b";

    /// <summary>等待首帧最长时间（毫秒）：startPreview 刚被调用但抓帧线程尚未 PublishFrame 时的缓冲期</summary>
    private const int FirstFrameWaitTimeoutMs = 10_000;

    /// <summary>等待后续新帧时的最大阻塞时间（毫秒）：超时后继续循环，避免订阅者永久挂起</summary>
    private const int FrameWaitTimeoutMs = 500;

    /// <summary>两次发送之间的最小间隔（毫秒）：避免浏览器过载，限制最大帧率约 60FPS</summary>
    private const int MinSendIntervalMs = 16;

    /// <summary>
    /// 在路由构建器上注册相机 MJPEG HTTP 流端点。
    /// 路由：GET /api/streaming/cameras/{cameraId}/preview
    /// </summary>
    public static IEndpointRouteBuilder MapCameraStreamingApi(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/streaming/cameras/{cameraId:guid}/preview", HandleGetPreviewStream);
        endpoints.MapGet(
            "/api/streaming/calibration/{projectId:guid}/cameras/{cameraRole:int}/preview",
            HandleGetCalibScanPreviewStream
        );
        return endpoints;
    }

    private static async Task HandleGetCalibScanPreviewStream(
        Guid projectId,
        int cameraRole,
        HttpContext context,
        CancellationToken requestAborted
    )
    {
        if (cameraRole is < 0 or > 1)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        CalibScanFrameBufferService buffer =
            context.RequestServices.GetRequiredService<CalibScanFrameBufferService>();

        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = $"multipart/x-mixed-replace; boundary={Boundary}";
        context.Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
        context.Response.Headers["Pragma"] = "no-cache";
        context.Response.Headers["X-Frame-Content-Type"] = "image/bmp";
        await context.Response.StartAsync(requestAborted);

        SemaphoreSlim waiter = buffer.RegisterWaiter(projectId, cameraRole);
        byte[] partHeaderPrefix = Encoding.ASCII.GetBytes(
            $"\r\n--{Boundary}\r\nContent-Type: image/bmp\r\nContent-Length: "
        );
        byte[] partHeaderSuffix = Encoding.ASCII.GetBytes("\r\n\r\n");

        try
        {
            long lastVersion = -1;
            while (!requestAborted.IsCancellationRequested)
            {
                byte[]? frame = buffer.TryGetLatest(projectId, cameraRole, out long version);
                if (frame == null || version == lastVersion)
                {
                    try
                    {
                        await waiter.WaitAsync(FrameWaitTimeoutMs, requestAborted);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    continue;
                }

                try
                {
                    await context.Response.Body.WriteAsync(partHeaderPrefix, requestAborted);
                    await context.Response.Body.WriteAsync(
                        Encoding.ASCII.GetBytes(frame.Length.ToString()),
                        requestAborted
                    );
                    await context.Response.Body.WriteAsync(partHeaderSuffix, requestAborted);
                    await context.Response.Body.WriteAsync(frame, requestAborted);
                    await context.Response.Body.FlushAsync(requestAborted);
                    lastVersion = version;
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (IOException)
                {
                    break;
                }
            }
        }
        finally
        {
            buffer.UnregisterWaiter(projectId, cameraRole, waiter);
        }
    }

    /// <summary>
    /// 处理 MJPEG 流请求：写入 Multipart 响应头后进入帧推送循环。
    /// 客户端断开或取消时正常退出。
    /// </summary>
    private static async Task HandleGetPreviewStream(
        Guid cameraId,
        HttpContext context,
        CancellationToken requestAborted
    )
    {
        CameraFrameBufferService buffer =
            context.RequestServices.GetRequiredService<CameraFrameBufferService>();
        Microsoft.Extensions.Logging.ILogger? logger = context
            .RequestServices.GetService<Microsoft.Extensions.Logging.ILoggerFactory>()
            ?.CreateLogger("CameraStreaming");

        // 禁用响应缓冲 + 保活
        context.Response.StatusCode = StatusCodes.Status200OK;
        // 注意：这里 Content-Type 仍写 multipart/x-mixed-replace 作为"帧分片协议标记"
        // 但 Chrome 的原生 MJPEG 解码器只支持 image/jpeg，前端会用 fetch+ReadableStream
        // 自行按 boundary 分帧组装成 BMP Blob URL 再赋给 <img>。
        // boundary 值不带双引号、不加短横前缀，严格匹配 Part 中 --{Boundary}。
        context.Response.ContentType = $"multipart/x-mixed-replace; boundary={Boundary}";
        context.Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
        context.Response.Headers["Pragma"] = "no-cache";
        context.Response.Headers["Expires"] = "0";
        context.Response.Headers["Connection"] = "keep-alive";
        // 自定义响应头：告知前端帧内图像 MIME，便于组装 Blob 时指定正确类型
        context.Response.Headers["X-Frame-Content-Type"] = "image/bmp";
        await context.Response.StartAsync(requestAborted);

        SemaphoreSlim waiter = buffer.RegisterWaiter(cameraId);

        try
        {
            long lastVersion = -1;
            long lastSendTick = 0;

            // Part 真正的分隔符前缀（注意前面是 -- 再加 HTTP 头中的 boundary），符合 RFC 2046
            byte[] partHeaderPrefix = Encoding.ASCII.GetBytes(
                $"\r\n--{Boundary}\r\nContent-Type: image/bmp\r\nContent-Length: "
            );
            byte[] partHeaderSuffix = Encoding.ASCII.GetBytes("\r\n\r\n");

            // ─── 阶段 1：等待首帧 ────────────────────────────────────────────────
            // startPreview 刚被调用时抓帧线程尚未 PublishFrame，
            // 不要直接返回 404；给一个 10s 宽限期，等待 PublishFrame 唤醒。
            if (!buffer.HasFrame(cameraId))
            {
                logger?.LogInformation(
                    "相机 {CameraId} MJPEG 订阅者：首帧尚未就绪，等待中…",
                    cameraId
                );
                using var firstFrameCts = CancellationTokenSource.CreateLinkedTokenSource(
                    requestAborted
                );
                firstFrameCts.CancelAfter(FirstFrameWaitTimeoutMs);

                try
                {
                    while (
                        !firstFrameCts.Token.IsCancellationRequested && !buffer.HasFrame(cameraId)
                    )
                    {
                        await waiter.WaitAsync(500, firstFrameCts.Token);
                    }
                }
                catch (OperationCanceledException) when (!requestAborted.IsCancellationRequested)
                {
                    // 宽限期超时仍无帧：相机大概率没有启动 start-preview，404 退出
                    logger?.LogWarning(
                        "相机 {CameraId} MJPEG 订阅者：等待首帧 {TimeoutMs}ms 超时，终止响应",
                        cameraId,
                        FirstFrameWaitTimeoutMs
                    );
                    // 不要尝试修改 StatusCode（响应头已 StartAsync 发送），直接正常断开即可
                    return;
                }
                catch (OperationCanceledException)
                {
                    return; // 客户端主动取消
                }

                if (!buffer.HasFrame(cameraId))
                {
                    return;
                }
            }

            while (!requestAborted.IsCancellationRequested)
            {
                // 1. 读取最新帧
                byte[]? frame = buffer.TryGetLatest(cameraId, out long currentVersion);
                if (frame == null || currentVersion == lastVersion)
                {
                    // 没有新帧：等待 PublishFrame 唤醒，或超时后继续循环
                    try
                    {
                        await waiter.WaitAsync(FrameWaitTimeoutMs, requestAborted);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    continue;
                }

                // 2. 发送节流：避免帧率过高导致浏览器卡顿
                long nowTick = Environment.TickCount64;
                int elapsed = (int)(nowTick - lastSendTick);
                if (elapsed < MinSendIntervalMs && lastSendTick != 0)
                {
                    await Task.Delay(MinSendIntervalMs - elapsed, requestAborted);
                    nowTick = Environment.TickCount64;
                }

                // 3. 写入 Multipart Part：
                //    --{boundary}\r\n
                //    Content-Type: image/bmp\r\n
                //    Content-Length: N\r\n
                //    \r\n
                //    <BMP 二进制数据>
                try
                {
                    int frameLen = frame.Length;
                    byte[] lenBytes = Encoding.ASCII.GetBytes(frameLen.ToString());

                    await context.Response.Body.WriteAsync(partHeaderPrefix, requestAborted);
                    await context.Response.Body.WriteAsync(lenBytes, requestAborted);
                    await context.Response.Body.WriteAsync(partHeaderSuffix, requestAborted);
                    await context.Response.Body.WriteAsync(
                        frame.AsMemory(0, frameLen),
                        requestAborted
                    );
                    await context.Response.Body.FlushAsync(requestAborted);

                    lastVersion = currentVersion;
                    lastSendTick = nowTick;
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (IOException)
                {
                    // 客户端断开（浏览器关闭标签页/刷新/取消请求），正常退出
                    break;
                }
                catch (Exception ex)
                {
                    logger?.LogWarning(
                        ex,
                        "相机 {CameraId} MJPEG 流写入异常，推送已终止",
                        cameraId
                    );
                    break;
                }
            }
        }
        finally
        {
            buffer.UnregisterWaiter(cameraId, waiter);
            logger?.LogInformation("相机 {CameraId} MJPEG 流订阅者已断开", cameraId);
        }
    }
}
