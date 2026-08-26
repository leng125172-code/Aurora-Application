# TucamFrameConcurrencyProbe

用于诊断双相机并行取图，以及同一相机被多个消费者同时调用 `WaitForFrame` 时的异常。

## 构建

```powershell
dotnet build Tools/TucamFrameConcurrencyProbe/TucamFrameConcurrencyProbe.csproj -c Release -r win-x64
```

Windows 上默认从 `C:\Program Files (x86)\TUCam_SDK\runtime\x64_all` 复制厂商运行库。也可以把 `TUCam.dll` 及其依赖手工放到程序输出目录。

## 推荐测试

### 双目曝光/视场诊断

关闭投影条纹或使用稳定照明，将圆点靶放在工作距离，使左右相机都能看到完整靶板，然后运行：

```powershell
dotnet run --project Tools/TucamFrameConcurrencyProbe/TucamFrameConcurrencyProbe.csproj -c Release -- --mode=stereo-diagnostic --frames=3 --timeout-ms=5000 --output=stereo-diagnostic-output
```

该模式按“主相机启动、取帧、停止，再从相机启动、取帧、停止”的顺序使用 FreeRunning，自动保存 RAW 与 BMP，并输出亮度均值、P1/P50/P99、暗像素率和饱和像素率。判断原则：

- `saturated > 5%`：严重过曝，优先降低曝光时间或增益。
- `saturated > 1%`：存在明显高光裁剪。
- `dark > 40%`：可能欠曝或照明覆盖不足。
- 角度不能只由亮度推断；比较同一 `shot` 的左右 BMP，圆点靶必须在两边完整可见且占据相近区域。若曝光正常但共同视场很小，才优先调整相机夹角。

```powershell
# 两台相机分别使用独立帧结构，统一软件触发、并行等待和复制（默认、安全）
dotnet run --project Tools/TucamFrameConcurrencyProbe/TucamFrameConcurrencyProbe.csproj -c Release -- --mode=dual-safe --frames=100 --timeout-ms=5000 --save-raw
```

严格按照相机 1、2、1、2 交替采集：

```powershell
dotnet run --project Tools/TucamFrameConcurrencyProbe/TucamFrameConcurrencyProbe.csproj -c Release -- --mode=alternating-safe --frames=50 --timeout-ms=5000
```

投影仪外触发两台相机（不下载条纹；临时切换 `B 2`，结束恢复 `B 0`）：

```powershell
dotnet run --project Tools/TucamFrameConcurrencyProbe/TucamFrameConcurrencyProbe.csproj -c Release -- --mode=projector-hardware --frames=5 --timeout-ms=5000 --save-raw
```

输出包含 SDK 帧号、缓冲区地址、尺寸、复制耗时和 FNV-1a 校验值。原始帧保存在 `frame-probe-output`。

仅在需要复现同一相机并发访问问题时运行下面的危险测试。它可能导致厂商 native 库崩溃：

```powershell
dotnet run --project Tools/TucamFrameConcurrencyProbe/TucamFrameConcurrencyProbe.csproj -c Release -- --mode=same-camera-unsafe --frames=10 --allow-unsafe
```

常用参数：

- `--camera-count=2`
- `--camera-offset=0`
- `--frames=20`
- `--timeout-ms=3000`
- `--config-path=.`
- `--output=frame-probe-output`
- `--save-raw`

## 结果判断

- `dual-safe` 全部成功：so 和 USB 链路基本支持双相机并行，业务层应采用每相机独立帧锁。
- `dual-safe` 出现 `Cap_Start` 或 `WaitForFrame` 错误：记录错误码，并检查两台相机是否位于同一 USB 控制器。
- `same-camera-unsafe` 异常而 `dual-safe` 正常：可确认同一相机多消费者共享帧结构是主要问题。
- `allSame=True`、尺寸为零或异常变化：帧数据或元信息存在明显异常。
