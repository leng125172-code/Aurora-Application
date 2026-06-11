# TucamMultiCameraProbe

用于在目标主板上快速验证 TUCam 运行库是否支持多相机并发采集。

## 功能

- 单进程初始化 SDK（`TUCAM_Api_Init`）
- 打开前 N 台相机（默认 2 台）
- 每台相机独立 `Buf_Alloc + Cap_Start`
- 支持多种测试方式：
  - `--parallel`：并发 `WaitForFrame`（默认）
  - `--sequential`：串行轮询 `WaitForFrame`
  - `--sync-shot`：软件触发同步拍照
  - `--alternating-shot`：A/B 交替拍照
  - `--alternating-reopen`：交替拍照，失败时重开当前相机
  - `--single-active-shot`：严格单活交替（同一时刻仅一台相机打开）
- 输出每台相机成功帧数和错误码计数

## 构建

```bash
dotnet build Tools/TucamMultiCameraProbe/TucamMultiCameraProbe.csproj -c Release
```

## 运行

```bash
# 默认：2 台相机，并发模式，20 秒
dotnet run --project Tools/TucamMultiCameraProbe/TucamMultiCameraProbe.csproj -c Release -- --parallel

# 指定 2 台相机，测试 30 秒，超时 1500ms
dotnet run --project Tools/TucamMultiCameraProbe/TucamMultiCameraProbe.csproj -c Release -- --camera-count=2 --seconds=30 --timeout-ms=1500 --parallel

# 串行轮询模式（对照测试）
dotnet run --project Tools/TucamMultiCameraProbe/TucamMultiCameraProbe.csproj -c Release -- --camera-count=2 --seconds=30 --sequential

# 只测第 2 台相机（索引1）
dotnet run --project Tools/TucamMultiCameraProbe/TucamMultiCameraProbe.csproj -c Release -- --camera-count=1 --camera-offset=1 --seconds=20

# 指定 SDK 配置目录并给每台相机启动间隔
dotnet run --project Tools/TucamMultiCameraProbe/TucamMultiCameraProbe.csproj -c Release -- --camera-count=2 --camera-offset=0 --start-delay-ms=500 --config-path=/home/linaro/Release/net10.0

# 两台相机同步拍照（GenICam 软件触发；AcquisitionStart -> TriggerSoftwarePulse -> WaitForFrame -> AcquisitionStop）
dotnet run --project Tools/TucamMultiCameraProbe/TucamMultiCameraProbe.csproj -c Release -- --sync-shot --camera-count=2 --shot-count=20 --shot-interval-ms=100 --timeout-ms=1500

# 交替拍照（A完成后B再拍，不使用软件触发）
dotnet run --project Tools/TucamMultiCameraProbe/TucamMultiCameraProbe.csproj -c Release -- --alternating-shot --camera-count=2 --shot-count=20 --shot-interval-ms=100 --timeout-ms=1500

# 交替拍照（失败自动重开相机再重试）
dotnet run --project Tools/TucamMultiCameraProbe/TucamMultiCameraProbe.csproj -c Release -- --alternating-reopen --camera-count=2 --shot-count=20 --shot-interval-ms=100 --timeout-ms=1500

# 严格单活交替拍照（每次只开一台，拍完立即关闭，再开下一台）
dotnet run --project Tools/TucamMultiCameraProbe/TucamMultiCameraProbe.csproj -c Release -- --single-active-shot --camera-count=2 --shot-count=20 --shot-interval-ms=100 --timeout-ms=1500

# 显式打开-预览-快照-关闭（A 打开 -> 预览 -> 快照 -> 关闭 -> B 重复）
dotnet run --project Tools/TucamMultiCameraProbe/TucamMultiCameraProbe.csproj -c Release -- --sequential-open-close --camera-count=2 --shot-count=20 --shot-interval-ms=100 --timeout-ms=1500
```

## 结果解读

- `fatal=0x00000000` 且 `okFrames > 0`：该相机完成了采集。
- 出现 `Cap_Start` 失败（如 `0x80000111`）或 `okFrames=0`：通常代表驱动/so 并发能力或 USB 带宽有问题。
- `parallel` 失败但 `sequential` 成功：常见于运行库不支持并发访问。

## 注意

- Linux 上确保 `libTUCam.so` 在可加载路径（如 `LD_LIBRARY_PATH`）。
- 本工具只做能力探测，不包含业务逻辑与自动恢复策略。
