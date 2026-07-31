# STEP 6 在线扫描功能重构计划

## 概述

将 STEP 6 在线扫描从 MVP 占位实现（StereoBM 深度图预览，不操作投影仪）升级为完整的结构光扫描流程：投影仪投射条纹图 + 双目相机软件触发同步采集 + 实时原图推送。

## 现状分析

### 现有实现（MVP 占位）
- [CalibScanAppService.cs](file:///d:/GitRepos/Aurora%20Application/Sources/AuroraStruct3D/AuroraStruct3D.Application/Calibration/CalibScanAppService.cs)：已有 Start/Stop/GetStatus/SetImageEnhance，但 `BuildRealtimeMetricsAsync` 只做 StereoBM 深度图预览，**不操作投影仪**
- [CalibStep6OnlineScan.vue](file:///d:/GitRepos/Aurora%20Application/Sources/AuroraStruct3D/AuroraStruct3D.Frontend/src/views/calibration/CalibStep6OnlineScan.vue)：已有启动/停止按钮，只显示深度图 DataUri，**不显示主从原图**
- [CalibScanStateStore.cs](file:///d:/GitRepos/Aurora%20Application/Sources/AuroraStruct3D/AuroraStruct3D.Application/Calibration/CalibScanStateStore.cs)：已有会话状态管理和 MetricLoop 机制

### 已具备的基础设施
- [IDlpProjectorService](file:///d:/GitRepos/Aurora%20Application/Sources/AuroraStruct3D/AuroraStruct3D.Projectors/IDlpProjectorService.cs)：已有全部指令方法
  - `SetBootImageAsync(ProjectorBootImage.Cross)` → 发送 `S8 2`
  - `LedOnAsync()` → 发送 `LN`（开灯）
  - `SetDisplayModeAsync(ProjectorDisplayMode.Cross)` → 发送 `S2`（十字图）
  - `SetTriggerModeAsync(ProjectorTriggerMode.SingleFrame)` → 发送 `B 2`
  - `TriggerOnceAsync()` → 发送 `T`
  - `NextFrameAsync()` → 发送 `N`
- [CalibProjectorParam](file:///d:/GitRepos/Aurora%20Application/Sources/AuroraStruct3D/AuroraStruct3D.Domain/Calibration/CalibProjectorParam.cs)：Step3 周期参数实体，含 `PatternCount`/`PeriodCount`/`FringeType`/`PhaseShift`/`ResolutionWidth`/`ResolutionHeight`
- [ITucamCameraService](file:///d:/GitRepos/Aurora%20Application/Sources/AuroraStruct3D/AuroraStruct3D.Cameras/ITucamCameraService.cs)：已支持软件触发 `DoSoftwareTriggerAsync`、`GrabFrameRawAsync(imageRotationAngle)`、`SetGenICamIntAsync`、`_capStartActiveLock`（顺序单活采集）
- [CameraHub](file:///d:/GitRepos/Aurora%20Application/Sources/AuroraStruct3D/AuroraStruct3D.HttpApi.Host/Hubs/CameraHub.cs) + [SignalRCalibScanNotifier](file:///d:/GitRepos/Aurora%20Application/Sources/AuroraStruct3D/AuroraStruct3D.HttpApi.Host/Notifiers/SignalRCalibScanNotifier.cs)：已建立 `calib-scan:{projectId}` 分组推送机制
- [TjProjectorCommands.cs](file:///d:/GitRepos/Aurora%20Application/Sources/AuroraStruct3D/AuroraStruct3D.Projectors/Protocol/TjProjectorCommands.cs)：协议常量 `LN=开灯`、`LL=关灯`（用户确认 LL 是笔误）

### 关键约束
- 鑫图双目相机 USB 带宽不够，**不能同时打开/采集**，遵循 `_capStartActiveLock` 顺序单活采集（主相机先拍完，从相机再拍）
- 硬件触发线已拔除，**仅支持软件触发**（TriggerSource=Software, TriggerMode=2）
- 投影仪手动控制 API 有 `EnsureManualOrMaintenanceMode` 校验，Step6 扫描需**直接调用 `IDlpProjectorService`**（通过 `IProjectorConnectionPool` 获取已连接实例），绕过 AppService 的模式检查（与现有 `GrabMetricFrameAsync` 直接调用 `ITucamCameraService` 的模式一致）

## 用户确认的决策

| 问题 | 决策 |
|------|------|
| 开灯指令 | 使用 `LN`（用户描述的 LL 是笔误，遵循代码协议常量） |
| 同步机制 | **混合模式**：按计数推进抓拍 PatternCount 张，最后一帧后再抓一帧做十字图检测确认，确认后才开始下一轮 |
| 推送粒度 | **每帧实时推送**：每抓一张主/从图就推送原图到前端，前端显示最新主/从原图 + 轮次/帧序号 |

## 改动清单

### 1. 后端 DTO 扩展

**文件**：[Sources/AuroraStruct3D/AuroraStruct3D.Application.Contracts/Calibration/Dtos/CalibScanDtos.cs](file:///d:/GitRepos/Aurora%20Application/Sources/AuroraStruct3D/AuroraStruct3D.Application.Contracts/Calibration/Dtos/CalibScanDtos.cs)

扩展 `CalibScanMetricsDto`：
- 新增 `long RoundIndex`（当前轮次序号，从 1 开始）
- 新增 `int FrameIndexInRound`（当前轮内帧序号，0~PatternCount-1）
- 新增 `int PatternCount`（每轮总帧数，来自 Step3 `CalibProjectorParam.PatternCount`）
- 新增 `bool IsCrosshairDetected`（十字图检测结果，混合模式确认用）
- 保留 `Fps`/`DepthValidRate`/`Confidence`/`DepthMapDataUri` 字段但不再计算 StereoBM，全部置 0/null（避免破坏前端兼容）

新增 `CalibScanFrameDto`：
- `string CalibProjectId`
- `int CameraRole`（0=主相机, 1=从相机）
- `byte[] JpegBytes`（JPEG 原图二进制）
- `long RoundIndex`
- `int FrameIndexInRound`

新增枚举 `CalibScanCameraRole`：`Main = 0, Secondary = 1`

### 2. 后端 SignalR Hub 接口扩展

**文件**：[Sources/AuroraStruct3D/AuroraStruct3D.HttpApi.Host/Hubs/ICameraHub.cs](file:///d:/GitRepos/Aurora%20Application/Sources/AuroraStruct3D/AuroraStruct3D.HttpApi.Host/Hubs/ICameraHub.cs)

新增推送方法：
```csharp
Task ReceiveCalibScanFrameAsync(string calibProjectId, int cameraRole, byte[] jpegBytes, long roundIndex, int frameIndexInRound);
```

**文件**：[Sources/AuroraStruct3D/AuroraStruct3D.Application.Contracts/Calibration/ICalibScanNotifier.cs](file:///d:/GitRepos/Aurora%20Application/Sources/AuroraStruct3D/AuroraStruct3D.Application.Contracts/Calibration/ICalibScanNotifier.cs)

新增方法签名：
```csharp
Task NotifyFrameAsync(Guid calibProjectId, int cameraRole, byte[] jpegBytes, long roundIndex, int frameIndexInRound);
```

**文件**：[Sources/AuroraStruct3D/AuroraStruct3D.HttpApi.Host/Notifiers/SignalRCalibScanNotifier.cs](file:///d:/GitRepos/Aurora%20Application/Sources/AuroraStruct3D/AuroraStruct3D.HttpApi.Host/Notifiers/SignalRCalibScanNotifier.cs)

实现 `NotifyFrameAsync`，调用 `_hubContext.Clients.Group(...).ReceiveCalibScanFrameAsync(...)`。

### 3. 后端 CalibScanAppService 重构（核心）

**文件**：[Sources/AuroraStruct3D/AuroraStruct3D.Application/Calibration/CalibScanAppService.cs](file:///d:/GitRepos/Aurora%20Application/Sources/AuroraStruct3D/AuroraStruct3D.Application/Calibration/CalibScanAppService.cs)

#### 3.1 新增依赖注入
- `IRepository<CalibProjectorParam, Guid> _projectorParamRepository`：读取 Step3 周期参数
- `IProjectorConnectionPool _projectorConnectionPool`：获取已连接的投影仪服务实例
- `IRepository<ProjectorDevice, Guid> _projectorDeviceRepository`：读取投影仪设备信息

#### 3.2 重写 StartAsync 流程
1. 校验项目绑定（相机、投影仪）—— 复用现有 `ValidateBindingsForScanMode`
2. 读取 `CalibProjectorParam`（按 `CalibProjectId` + `ProjectorDeviceId` 查询）获取 `PatternCount`
3. 通过 `_projectorConnectionPool.TryGet(projectorDeviceId)` 获取已连接投影仪实例（若未连接抛 `UserFriendlyException("请先在设备管理页连接投影仪")`）
4. 初始化投影仪序列：
   - `SetBootImageAsync(ProjectorBootImage.Cross)` → S8 2 设置开机图为十字
   - `LedOnAsync()` → LN 开灯
   - `SetDisplayModeAsync(ProjectorDisplayMode.Cross)` → S2 切到十字图
5. 设置主从相机为软件触发模式：
   - `SetGenICamIntAsync(idx, "TriggerSource", 1)`（1=Software，需先设置触发源）
   - `SetGenICamIntAsync(idx, "TriggerMode", 2)`（2=On，软件触发模式）
6. 投影仪 `SetTriggerModeAsync(ProjectorTriggerMode.SingleFrame)` → B 2 单帧触发模式
7. 启动扫描循环（调用新的 `CalibScanStateStore.StartScanLoop`）

#### 3.3 新增扫描循环逻辑 ScanLoopAsync（替换 BuildRealtimeMetricsAsync）

```
while (!cts.IsCancellationRequested):
    roundIndex++
    # 第 1 张：T 指令
    projector.TriggerOnceAsync()  # T
    mainBytes = GrabScanFrameAsync(mainCamera)   # 主相机软件触发+抓拍（含旋转）
    notifier.NotifyFrameAsync(projectId, Main, mainBytes, roundIndex, 0)
    secondaryBytes = GrabScanFrameAsync(secondaryCamera)
    notifier.NotifyFrameAsync(projectId, Secondary, secondaryBytes, roundIndex, 0)

    # 第 2~PatternCount 张：N 指令
    for i in 1..PatternCount-1:
        projector.NextFrameAsync()  # N
        mainBytes = GrabScanFrameAsync(mainCamera)
        notifier.NotifyFrameAsync(projectId, Main, mainBytes, roundIndex, i)
        secondaryBytes = GrabScanFrameAsync(secondaryCamera)
        notifier.NotifyFrameAsync(projectId, Secondary, secondaryBytes, roundIndex, i)

    # 混合模式：抓一帧做十字图检测确认
    confirmBytes = GrabScanFrameAsync(mainCamera)
    isCross = DetectCrosshair(confirmBytes)
    metrics = { roundIndex, frameIndexInRound=PatternCount-1, patternCount, isCrosshairDetected=isCross, ... }
    notifier.NotifyMetricsAsync(projectId, metrics)
    stateStore.UpdateMetrics(projectId, metrics)

    if !isCross:
        logger.LogWarning("十字图未检测到，可能存在同步问题，继续下一轮")
    # 无论是否检测到，都继续下一轮（T 指令会重置投影仪到第一张）
```

#### 3.4 新增 GrabScanFrameAsync 方法（基于现有 GrabMetricFrameAsync 改造）
- 读取相机 `ImageRotationAngle` 配置
- `StartCaptureAsync` → `DoSoftwareTriggerAsync` → `GrabFrameRawAsync(idx, timeoutMs, maxWidth:0, jpegQuality:85, imageRotationAngle)` → `StopCaptureAsync`
- 遵循 `_capStartActiveLock` 顺序单活采集（主相机先 StopCapture，从相机再 StartCapture）
- 动态超时：曝光时间×2 + 1s，最少 8s（复用现有逻辑）

#### 3.5 新增 DetectCrosshair 方法（OpenCV 十字图检测）
```csharp
private static bool DetectCrosshair(byte[] jpegBytes)
{
    // 1. 灰度化
    using Mat gray = Cv2.ImDecode(jpegBytes, ImreadModes.Grayscale);
    if (gray.Empty()) return false;

    // 2. 判定整体亮度较高（白色背景）
    double meanBrightness = Cv2.Mean(gray).Val0;
    if (meanBrightness < 100) return false;

    // 3. 自适应阈值二值化（应对光照不均）
    using Mat binary = new();
    Cv2.AdaptiveThreshold(gray, binary, 255, AdaptiveThresholdTypes.GaussianC, ThresholdTypes.BinaryInv, 51, 10);

    // 4. 检测中心行（y=H/2）的暗像素比例（黑色水平线）
    int h = gray.Rows, w = gray.Cols;
    int cy = h / 2, cx = w / 2;
    int rowDark = 0, colDark = 0;
    for (int x = 0; x < w; x++) if (binary.At<byte>(cy, x) > 0) rowDark++;
    for (int y = 0; y < h; y++) if (binary.At<byte>(y, cx) > 0) colDark++;

    double rowRatio = (double)rowDark / w;
    double colRatio = (double)colDark / h;

    // 5. 中心十字线暗像素比例 > 60% 判定为十字图
    return rowRatio > 0.6 && colRatio > 0.6;
}
```

#### 3.6 重写 StopAsync 流程
1. 取消扫描循环（`_stateStore.Stop`）
2. 投影仪清理：
   - `LedOffAsync()` → LL 关灯
   - （可选）`SetDisplayModeAsync(ProjectorDisplayMode.Black)` → 恢复黑屏
3. 相机清理：主从相机 `StopCaptureAsync`（容错）
4. 推送最终状态

#### 3.7 移除/弃用旧 MVP 逻辑
- 移除 `BuildRealtimeMetricsAsync`、`ComputeStereoMetrics`、`ResizeForStereo`、`ComputeLaplacianVariance`、`NormalizeSharpness`、`ApplyClahe`、`BuildJpegDataUri` 等深度图相关方法
- 移除 `SetImageEnhanceAsync` 和 `GetImageEnhanceEnabled`（CLAHE 仅用于深度图预览，新流程不需要）
- 移除 `GrabMetricFrameAsync`，由 `GrabScanFrameAsync` 替代

### 4. 后端 CalibScanStateStore 调整

**文件**：[Sources/AuroraStruct3D/AuroraStruct3D.Application/Calibration/CalibScanStateStore.cs](file:///d:/GitRepos/Aurora%20Application/Sources/AuroraStruct3D/AuroraStruct3D.Application/Calibration/CalibScanStateStore.cs)

- `CalibScanSessionState` 新增字段：`int PatternCount`、`long CurrentRoundIndex`、`int CurrentFrameIndexInRound`
- `Start` 方法签名新增 `int patternCount` 参数
- `StartMetricLoop` 重命名为 `StartScanLoop`，回调签名调整为 `Func<CancellationToken, Task>`（循环体内部自行推送 frame/metrics，不再由 store 调度延迟）
- 移除 `SetImageEnhance`/`GetImageEnhanceEnabled` 和 `ImageEnhanceEnabled` 字段
- 移除固定 `Task.Delay(1000)` 节流，扫描循环节奏由投影仪 T/N 指令和相机抓拍耗时自然控制

### 5. 后端 ICalibScanAppService 接口调整

**文件**：[Sources/AuroraStruct3D/AuroraStruct3D.Application.Contracts/Calibration/ICalibScanAppService.cs](file:///d:/GitRepos/Aurora%20Application/Sources/AuroraStruct3D/AuroraStruct3D.Application.Contracts/Calibration/ICalibScanAppService.cs)

- 移除 `SetImageEnhanceAsync` 方法（如接口中存在）

### 6. 后端 SignalR 消息大小配置

**文件**：[Sources/AuroraStruct3D/AuroraStruct3D.HttpApi.Host/AuroraStruct3DHttpApiHostModule.cs](file:///d:/GitRepos/Aurora%20Application/Sources/AuroraStruct3D/AuroraStruct3D.HttpApi.Host/AuroraStruct3DHttpApiHostModule.cs) 第 111 行

将 `AddSignalR().AddMessagePackProtocol()` 改为：
```csharp
context.Services.AddSignalR(o => o.MaximumReceiveMessageSize = 10 * 1024 * 1024)
    .AddMessagePackProtocol();
```
支持推送 2448×2048 JPEG 原图二进制（约 1-2MB/帧）。

### 7. 前端 API 类型扩展

**文件**：[Sources/AuroraStruct3D/AuroraStruct3D.Frontend/src/api/calib-scan.ts](file:///d:/GitRepos/Aurora%20Application/Sources/AuroraStruct3D/AuroraStruct3D.Frontend/src/api/calib-scan.ts)

- 扩展 `CalibScanMetricsDto`：新增 `roundIndex`、`frameIndexInRound`、`patternCount`、`isCrosshairDetected`
- 新增 `CalibScanCameraRole` 枚举：`Main = 0, Secondary = 1`
- 新增 `CalibScanFrameDto` 接口：`{ calibProjectId: string, cameraRole: number, jpegBytes: Uint8Array, roundIndex: number, frameIndexInRound: number }`
- 移除 `setCalibScanImageEnhance` 函数和 `SetCalibScanImageEnhanceInput` 接口

### 8. 前端页面重构

**文件**：[Sources/AuroraStruct3D/AuroraStruct3D.Frontend/src/views/calibration/CalibStep6OnlineScan.vue](file:///d:/GitRepos/Aurora%20Application/Sources/AuroraStruct3D/AuroraStruct3D.Frontend/src/views/calibration/CalibStep6OnlineScan.vue)

#### 8.1 新增主从相机原图显示区域
- 两个 `<img>` 元素（主相机 / 从相机），并排显示
- 用 Blob URL 显示图像：`URL.createObjectURL(new Blob([jpegBytes], { type: 'image/jpeg' }))`
- 每次新帧到达时 `URL.revokeObjectURL(oldUrl)` 释放旧帧，防止内存泄漏
- 维护 `mainImageUrl` / `secondaryImageUrl` 两个 ref

#### 8.2 监听新 SignalR 事件
```typescript
hubConnection.on('ReceiveCalibScanFrameAsync',
    (projectId: string, cameraRole: number, jpegBytes: Uint8Array,
     roundIndex: number, frameIndexInRound: number) => {
        if (projectId !== props.project.id) return
        const blob = new Blob([jpegBytes], { type: 'image/jpeg' })
        const url = URL.createObjectURL(blob)
        if (cameraRole === CalibScanCameraRole.Main) {
            if (mainImageUrl.value) URL.revokeObjectURL(mainImageUrl.value)
            mainImageUrl.value = url
        } else {
            if (secondaryImageUrl.value) URL.revokeObjectURL(secondaryImageUrl.value)
            secondaryImageUrl.value = url
        }
    }
)
```

#### 8.3 状态指标面板更新
- 移除"深度有效率"、"置信度"、"深度图"显示
- 新增显示：当前轮次（`roundIndex`）、当前帧序号（`frameIndexInRound`/`patternCount`）、十字图检测状态（`isCrosshairDetected`，用 Tag 显示"已同步"/"未检测到"）
- 保留 FPS、帧序号、最后更新时间

#### 8.4 移除项
- 移除"启用 OpenCV 图像增强（CLAHE）"复选框及 `imageEnhanceEnabled` ref、`onImageEnhanceChange` 方法
- 移除"实时深度图"整个卡片区域
- 移除底部说明"双 USB 相机场景受底层驱动限制，不走同时预览；Step6 采用后台轮询抓拍主从相机并生成深度图"
- 新增说明："投影仪投射条纹图，双目相机软件触发同步采集。主从相机顺序抓拍，实时显示最新原图。"

#### 8.5 组件卸载清理
- `onUnmounted` 中 revoke 所有 Blob URL

## 假设与决策

1. **开灯指令**：使用 `LN`（`LedOnAsync`），用户确认 LL 是笔误
2. **同步机制**：混合模式 —— 按计数推进 PatternCount 张抓拍，最后一帧后再抓一帧做 OpenCV 十字图检测确认
3. **推送粒度**：每帧实时推送原图二进制（主/从分别推送，cameraRole 区分）
4. **设备运行模式**：直接调用 `IDlpProjectorService`（通过 `IProjectorConnectionPool` 获取已连接实例），绕过 `ProjectorDeviceAppService.EnsureManualOrMaintenanceMode` 检查
5. **相机顺序单活采集**：遵循现有 `_capStartActiveLock` 机制，主相机 StartCapture→软件触发→GrabFrame→StopCapture，释放锁后从相机再执行同样流程
6. **图像旋转**：抓拍时应用 `camera.ImageRotationAngle`（传给 `GrabFrameRawAsync` 的 `imageRotationAngle` 参数）
7. **相机软件触发设置**：启动时 `TriggerSource=1(Software)` + `TriggerMode=2(On)`，需在相机已打开前提下设置
8. **前端图像显示**：用 Blob URL 而非 base64 DataUri，避免 base64 编码开销（~33% 膨胀）和内存占用
9. **PatternCount 来源**：从 `CalibProjectorParam` 实体按 `(CalibProjectId, ProjectorDeviceId)` 查询，若未配置抛 `UserFriendlyException("请先在 Step3 完成投影仪参数配置")`
10. **停止时清理**：投影仪关灯（LL）+ 相机停止采集，不主动断开投影仪连接（保持连接池状态）
11. **SignalR 消息大小**：配置 `MaximumReceiveMessageSize = 10MB` 支持原图二进制推送
12. **MessagePack 二进制传输**：`jpegBytes` 以 `byte[]` 在 C# 端、`Uint8Array` 在前端传输，MessagePack 协议原生支持二进制，无需 base64

## 验证步骤

1. **编译验证**：
   - `dotnet build "Aurora Application.slnx"` 编译通过
   - 前端 `npm run type-check` 类型检查通过

2. **启动服务**：
   - 启动后端 Host：`dotnet run --project Sources/AuroraStruct3D/AuroraStruct3D.HttpApi.Host/AuroraStruct3D.HttpApi.Host.csproj`
   - 启动前端：`npm run dev`（在 `Sources/AuroraStruct3D/AuroraStruct3D.Frontend/`）

3. **前置准备**：
   - 在设备管理页连接投影仪（确保 `IProjectorConnectionPool` 有实例）
   - 在设备管理页打开主从相机
   - Step3 完成投影仪参数配置（确保 `CalibProjectorParam` 有 `PatternCount` 记录）

4. **功能验证**：
   - 进入 Step6 页面，点击"启动扫描"
   - 验证投影仪开灯（LN）并显示十字图（S2）
   - 验证主从相机原图实时刷新（每帧更新）
   - 验证轮次递增、帧序号 0~PatternCount-1 循环
   - 验证十字图检测状态显示
   - 点击"停止扫描"
   - 验证投影仪关灯（LL）
   - 验证状态恢复 Idle

5. **异常场景验证**：
   - 未连接投影仪时启动 → 应提示"请先在设备管理页连接投影仪"
   - Step3 未配置参数时启动 → 应提示"请先在 Step3 完成投影仪参数配置"
   - 相机未打开时启动 → 应提示"请先打开主/从相机"
