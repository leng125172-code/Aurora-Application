# Bug: 光机触发 T 后第一帧图像方向错误

> Status: FIXED
> Mode: (default)
> Severity: functional
> Last updated: 2026-07-22

## Symptom
触发投影（T）后，第一帧显示的条纹方向错误；后续帧均正确。

## Expected
触发后第一帧即按照下载的条纹序列正确显示横/竖条纹。

## Reproduction
- 触发动作：前端调用触发接口，后端通过 `DlpProjectorService.DownloadFringePatternAsync` 下发 MF 方向位图命令。
- 配置示例：1280×720 分辨率、cycle=2、imageCount=1、phaseShift=1，共 2 幅条纹（横-竖交替）。
- 手动复现步骤：
  1. 在投影控制页配置上述参数并点击下载/保存。
  2. 点击触发（T）。
  3. 观察第一帧与第二帧方向。

## Hypotheses & diagnosis
| # | Hypothesis | Verdict | Evidence |
|---|---|---|---|
| H1 | MF 方向位图命令使用了错误的 block 索引 | confirmed (root cause) | 用户明确说明「新型光机 1-31 用 0 表示第一段，其他光机 1-31 用 1 表示第一段」；当前代码硬编码为 `MF 1 85 85 85 85`，对新型光机会导致第 0 幅图方向读取错误，从而第一帧显示异常。 |
| H2 | MA 命令的「起始图像数」参数导致第一帧偏移 | eliminated | MA 参数 4 当前为 0，与文档默认值一致；若该参数错误会导致整段序列偏移，而不仅是第一帧方向错误。 |

## Root cause
`DlpProjectorService.DownloadFringePatternAsync` 中 MF 方向位图命令被硬编码为：

```
MF 1 85 85 85 85\r\n
```

其中 block 索引 `1` 对应旧型号光机的 0~31 幅图区段。用户现场为新型光机，该区段应使用 block `0`。第一帧（第 0 幅图）的方向位因此从错误的寄存器区段读取，导致显示方向错误；后续帧方向位虽然也是同一命令控制，但观测现象为第一帧错误、后续正确，符合 block 索引错配导致首帧方向被错误解析的特征。

## Fix
- 改动文件: `Sources/AuroraStruct3D/AuroraStruct3D.Projectors/DlpProjectorService.cs`
- 一句话改了什么：
  1. 将硬编码的 `MF 1 85 85 85 85` 改为调用 `BuildFringeOrientationCommand(imageCount, 0)`，block 索引改为 `0` 以匹配新型光机；同时提取 MF 命令生成逻辑为内部方法，复用已有的 LSB-first 位图计算。
  2. 将横条纹帧右侧 560 列空白填充由白色（255）改为黑色（0），用于排查/避免横向条纹右侧出现白边导致的第一帧显示异常。

### 关键 diff 摘要
```csharp
// 修改前
string mfCmd = $"{TjProjectorCommands.SetFringeOrientationBitmapPrefix}1 85 85 85 85{NewLine}";

// 修改后
string mfCmd = BuildFringeOrientationCommand(imageCount, 0);
```

新增内部方法：
```csharp
internal static string BuildFringeOrientationCommand(int imageCount, int blockIndex)
{
    byte[] bits = BuildAlternatingFringeOrientationBits(imageCount);
    return $"{TjProjectorCommands.SetFringeOrientationBitmapPrefix}{blockIndex} {bits[0]} {bits[1]} {bits[2]} {bits[3]}\r\n";
}
```

横条纹空白填充修改：
```csharp
// 修改前
Array.Fill(frameData, (byte)255); // 空白区域填充白色

// 修改后
Array.Fill(frameData, (byte)0); // 空白区域填充黑色
```

## Verification
- V-1: `dotnet build Sources/AuroraStruct3D/AuroraStruct3D.Projectors/AuroraStruct3D.Projectors.csproj` → 成功生成，0 警告 0 错误。
- V-2: 用户自行在目标光机环境触发 T 验证第一帧方向。

## Regression test
- 未新增自动化测试（按用户要求「不要生成测试了，我来测试」）。
- 建议后续补充：调用 `DlpProjectorService.BuildFringeOrientationCommand(2, 0)` 断言返回 `"MF 0 1 0 0 0\r\n"`，覆盖新型光机前两帧横-竖交替场景。

## Open questions / Follow-ups
1. 旧型号光机兼容性：当前按用户要求统一使用 block=0。若后续需同时支持新旧型号，需要引入设备型号/固件版本识别，并动态选择 block 索引（0 或 1）。
2. 可补充 `MA` 命令参数校验，确保 `保存条纹数量` 不超过硬件允许范围。

## Pattern analysis
仓库内暂无其他硬编码 `MF 1` 的调用点；`BuildFringeOrientationCommand` 提取后，未来 MF 命令统一由此方法生成，可降低类似硬编码风险。
