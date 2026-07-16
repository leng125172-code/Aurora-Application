# 外参标定两图分离方案实现计划

## 一、方案概述

### 用户提出的新方案（两图分离 + 两次点击）
相机和投影仪位置保持不变，分两次独立点击拍摄：

| 点击 | 按钮名称 | 图片内容 | 用途 |
|------|----------|----------|------|
| 第1次 | **外参圆点拍照** | 只有圆点标定板（投影仪白屏） | 检测圆点角点 → SolvePnP |
| 第2次 | **外参棋盘格拍照** | 只有投影棋盘格（移除标定板后） | 检测棋盘格角点 → 投影仪标定 |

### 当前方案（一次点击两图）
| 点击 | 图片内容 | 问题 |
|------|----------|------|
| 1次 | 白屏帧：圆点标定板 + 白屏投影 | 正常 |
| 1次 | 棋盘格帧：圆点标定板 + 棋盘格投影 | **圆点遮挡棋盘格，角点检测失败** |

### 方案优势
1. **无干扰检测**：圆点和棋盘格分开拍摄，互不干扰
2. **检测成功率高**：纯棋盘格图像的角点检测更可靠
3. **操作灵活**：两次独立点击，用户有充足时间移除标定板
4. **精度更高**：避免圆点和棋盘格叠加时的视觉干扰

---

## 二、交互流程

### 完整操作步骤
```
用户操作：
  1. 放置圆点标定板到投影区域
  2. 点击「外参圆点拍照」按钮
     → 后端：投影仪切换到S1白屏 → 相机拍摄圆点标定板 → 检测圆点角点 → 保存图片
  3. 移除圆点标定板（保持相机和投影仪不动）
  4. 点击「外参棋盘格拍照」按钮
     → 后端：投影仪切换到S3棋盘格 → 相机拍摄纯棋盘格 → 检测棋盘格角点 → 保存图片
  5. 重复步骤1-4，直到完成10组有效图片
  6. 点击「计算外参」按钮
```

### 分组机制
- 每对「圆点帧 + 棋盘格帧」通过 `PairGroupId` 关联
- 圆点帧标记为 `ExtrinsicPhotoPhase.WhiteScreen`
- 棋盘格帧标记为 `ExtrinsicPhotoPhase.Checkerboard`
- 同一组的两张图片共享相同的 `PairGroupId`

---

## 三、需要修改的文件

### 1. API 接口定义（核心修改）
**文件**: `Sources/AuroraStruct3D/AuroraStruct3D.Application.Contracts/Calibration/ICalibPhotoAppService.cs`

**修改**: 将原来的单 API 拆分为两个独立 API：
```csharp
/// <summary>
/// 外参圆点拍照：投影仪白屏模式，拍摄圆点标定板
/// </summary>
Task<CalibPhotoDto> TakeExtrinsicDotPhotoAsync(TakeExtrinsicPhotoInput input);

/// <summary>
/// 外参棋盘格拍照：投影仪棋盘格模式，拍摄纯棋盘格（需先移除标定板）
/// </summary>
Task<CalibPhotoDto> TakeExtrinsicCheckerboardPhotoAsync(TakeExtrinsicPhotoInput input);
```

### 2. 枚举定义
**文件**: `Sources/AuroraStruct3D/AuroraStruct3D.Domain.Shared/Calibration/CalibConsts.cs`

**修改**: 枚举保持不变：
```csharp
public enum ExtrinsicPhotoPhase
{
    ProjectorOff = 0,
    ProjectorOn = 1,
    WhiteScreen = 2,      // 白屏模式拍摄圆点标定板
    Checkerboard = 3,     // 棋盘格模式拍摄纯棋盘格
}
```

### 3. 拍照流程实现（核心修改）
**文件**: `Sources/AuroraStruct3D/AuroraStruct3D.Application/Calibration/CalibPhotoAppService.cs`

**修改**: 将 `TakeExtrinsicPhotoAsync` 拆分为两个方法：
- `TakeExtrinsicDotPhotoAsync`：S1白屏模式拍摄圆点标定板
- `TakeExtrinsicCheckerboardPhotoAsync`：S3棋盘格模式拍摄纯棋盘格

### 4. 前端 API 调用
**文件**: `Sources/AuroraStruct3D/AuroraStruct3D.Frontend/src/api/calib-photo.ts`

**修改**: 添加两个新的 API 调用函数：
```typescript
export async function takeExtrinsicDotPhoto(input: TakeExtrinsicPhotoInput, timeout = 30000): Promise<CalibPhotoDto>

export async function takeExtrinsicCheckerboardPhoto(input: TakeExtrinsicPhotoInput, timeout = 30000): Promise<CalibPhotoDto>
```

### 5. 前端 UI（核心修改）
**文件**: `Sources/AuroraStruct3D/AuroraStruct3D.Frontend/src/views/calibration/CalibStep5CameraCalib.vue`

**修改**:
- 将原来的「外参拍照」按钮拆分为两个按钮：「外参圆点拍照」和「外参棋盘格拍照」
- 添加操作提示：
  - 圆点拍照前："请将圆点标定板放置到投影区域"
  - 棋盘格拍照前："请移除圆点标定板，保持相机和投影仪不动"
- 更新图片展示区域，显示两组图片

### 6. Python 分析脚本
**文件**: `Tools/calib_photo_analyzer.py`

**修改**:
- 更新分析逻辑，支持两图分离方案
- 添加纯棋盘格检测优化

---

## 四、实现步骤

### 步骤 1：添加新 API 接口
**文件**: `ICalibPhotoAppService.cs`

**修改内容**:
- 添加 `TakeExtrinsicDotPhotoAsync` 接口方法
- 添加 `TakeExtrinsicCheckerboardPhotoAsync` 接口方法
- 保留原 `TakeExtrinsicPhotoAsync` 方法（兼容旧数据）

### 步骤 2：实现拍照流程
**文件**: `CalibPhotoAppService.cs`

**修改内容**:
- 实现 `TakeExtrinsicDotPhotoAsync` 方法：
  - 投影仪切换到 S1 白屏模式
  - 等待稳定（500ms）
  - 相机拍摄
  - 检测圆点角点
  - 保存图片（标记为 WhiteScreen）

- 实现 `TakeExtrinsicCheckerboardPhotoAsync` 方法：
  - 投影仪切换到 S3 棋盘格模式
  - 等待稳定（500ms）
  - 相机拍摄
  - 检测棋盘格角点
  - 保存图片（标记为 Checkerboard）

- 修改 `BuildExtrinsicSampleGroups` 方法：
  - 支持将 WhiteScreen 和 Checkerboard 照片按 PairGroupId 分组
  - 同一组的两张照片共享相同的 PairGroupId

### 步骤 3：修改外参计算算法
**文件**: `CalibPhotoAppService.cs` → `ComputeExtrinsicAsync`

**修改内容**:
- SolvePnP：使用 WhiteScreen 帧的圆点角点（不变）
- 投影仪标定：使用 Checkerboard 帧的棋盘格角点（纯棋盘格，无干扰）

### 步骤 4：更新前端 API
**文件**: `calib-photo.ts`

**修改内容**:
- 添加 `takeExtrinsicDotPhoto` 函数
- 添加 `takeExtrinsicCheckerboardPhoto` 函数

### 步骤 5：更新前端 UI
**文件**: `CalibStep5CameraCalib.vue`

**修改内容**:
- 拆分按钮：「外参圆点拍照」和「外参棋盘格拍照」
- 添加操作提示文案
- 更新图片展示区域
- 更新验证逻辑（需要同时有圆点帧和棋盘格帧才算有效组）

### 步骤 6：更新 Python 分析脚本
**文件**: `calib_photo_analyzer.py`

**修改内容**:
- 更新分析逻辑
- 添加纯棋盘格检测优化

---

## 五、分组机制设计

### 数据结构
```
CalibPhotoRecord:
{
    Id: Guid,
    PairGroupId: Guid,      // 同一组照片共享此 ID
    ExtrinsicPhase: WhiteScreen | Checkerboard,
    PhotoType: Extrinsic,
    IsValid: bool,
    ...
}
```

### 分组逻辑
1. 每次点击「外参圆点拍照」时，生成新的 `PairGroupId`
2. 对应的「外参棋盘格拍照」使用相同的 `PairGroupId`
3. 计算时按 `PairGroupId` 分组，每组需要包含：
   - 1张 WhiteScreen 照片（圆点标定板）
   - 1张 Checkerboard 照片（纯棋盘格）

### 前端状态管理
```typescript
interface ExtrinsicSample {
    pairGroupId: string;
    dotPhoto: CalibPhotoDto | null;      // 圆点帧
    checkerboardPhoto: CalibPhotoDto | null;  // 棋盘格帧
    isValid: boolean;
}
```

---

## 六、潜在风险与注意事项

### 1. 用户操作流程
- 需要在前端添加明确的操作指引
- 用户可能忘记移除标定板就点击棋盘格拍照

### 2. 分组配对
- 需要确保圆点帧和棋盘格帧正确配对
- 如果用户只拍了圆点帧或只拍了棋盘格帧，需要提示补拍

### 3. 稳定性要求
- 两次拍摄期间，相机和投影仪必须保持不动
- 环境光照条件应尽量一致

### 4. 向后兼容性
- 旧的两图叠加数据仍可使用（兼容模式）
- 新增的两图分离数据也能正常计算

### 5. 检测算法优化
- 纯棋盘格图像的角点检测需要优化参数

---

## 七、验证标准

1. **编译通过**：所有修改的文件编译无错误
2. **旧数据兼容**：使用旧方案拍摄的历史数据仍可正常计算
3. **新流程可用**：两图分离方案拍摄的数据能正确分组和计算
4. **检测准确性**：棋盘格角点检测成功率显著提升（从0%提升到>90%）
5. **前端交互**：两个按钮独立工作，操作流程清晰

---

## 八、文件修改清单

| 文件路径 | 修改内容 |
|----------|----------|
| `Sources/AuroraStruct3D/AuroraStruct3D.Application.Contracts/Calibration/ICalibPhotoAppService.cs` | 添加两个新 API 接口 |
| `Sources/AuroraStruct3D/AuroraStruct3D.Application/Calibration/CalibPhotoAppService.cs` | 实现两个新拍照方法，修改分组逻辑和计算算法 |
| `Sources/AuroraStruct3D/AuroraStruct3D.Frontend/src/api/calib-photo.ts` | 添加两个新 API 调用函数 |
| `Sources/AuroraStruct3D/AuroraStruct3D.Frontend/src/views/calibration/CalibStep5CameraCalib.vue` | 拆分按钮，更新 UI 和交互流程 |
| `Tools/calib_photo_analyzer.py` | 更新分析逻辑 |

---

## 九、方案对比

| 对比项 | 当前方案（一次点击两图） | 新方案（两次点击两图分离） |
|--------|------------------------|--------------------------|
| 点击次数 | 1次 | 2次 |
| 圆点检测 | ✅ 正常 | ✅ 正常 |
| 棋盘格检测 | ❌ 被圆点遮挡 | ✅ 纯棋盘格 |
| 操作灵活性 | 低（自动连续拍摄） | 高（用户控制节奏） |
| 用户操作时间 | 无间隔 | 充足时间移除标定板 |
| 检测成功率 | 低 | 高 |
| 实现难度 | - | 中 |

**结论**：两次点击的两图分离方案是最优选择，检测成功率高且操作灵活。
