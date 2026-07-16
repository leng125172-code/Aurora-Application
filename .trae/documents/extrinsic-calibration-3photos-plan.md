# 外参标定三图方案实现计划

## 一、方案概述

### 当前方案（两图）
| 图片 | 内容 | 用途 |
|------|------|------|
| 白屏帧 | 圆点标定板 + 白屏投影 | 检测圆点角点 → SolvePnP |
| 棋盘格帧 | 圆点标定板 + 棋盘格投影 | 检测棋盘格角点 → 投影仪标定 |

### 新方案（三图）
| 图片 | 内容 | 用途 |
|------|------|------|
| 白屏帧 | 圆点标定板 + 白屏投影 | 检测圆点角点 → SolvePnP |
| 叠加帧 | 圆点标定板 + 棋盘格投影 | 差分验证 + 辅助检测 |
| 纯棋盘格帧 | 移除标定板 + 棋盘格投影 | **干净检测棋盘格角点** |

### 方案优势
1. **纯棋盘格帧无干扰**：移除标定板后，投影棋盘格不会被圆点遮挡，角点检测更可靠
2. **差分验证更准确**：白屏帧 vs 叠加帧验证投影仪切换；叠加帧 vs 纯棋盘格帧验证标定板移除
3. **提高标定精度**：避免棋盘格角点检测受标定板圆点干扰

---

## 二、需要修改的文件

### 1. 枚举定义
**文件**: `Sources/AuroraStruct3D/AuroraStruct3D.Domain.Shared/Calibration/CalibConsts.cs`

**修改**: 在 `ExtrinsicPhotoPhase` 枚举中添加新阶段：
```csharp
public enum ExtrinsicPhotoPhase
{
    ProjectorOff = 0,
    ProjectorOn = 1,
    WhiteScreen = 2,
    Checkerboard = 3,
    CheckerboardOnly = 4,  // 新增：移除标定板后的纯棋盘格帧
}
```

### 2. 样本分组结构
**文件**: `Sources/AuroraStruct3D/AuroraStruct3D.Application/Calibration/CalibPhotoAppService.cs`

**修改**: 更新 `ProjectorExtrinsicSampleGroup` 结构：
```csharp
private sealed class ProjectorExtrinsicSampleGroup
{
    public required Guid PairGroupId { get; init; }
    public required CalibPhotoRecord ProjectorOffPhoto { get; init; }      // 白屏帧
    public required CalibPhotoRecord ProjectorOnPhoto { get; init; }        // 叠加帧
    public required CalibPhotoRecord? CheckerboardOnlyPhoto { get; init; }  // 新增：纯棋盘格帧
    public required List<CalibPhotoRecord> ProjectorPatternPhotos { get; init; }
    
    public bool IsValid =>
        ProjectorPatternPhotos.Count > 0
        && ProjectorOffPhoto.IsValid
        && ProjectorOnPhoto.IsValid
        && (CheckerboardOnlyPhoto?.IsValid ?? true)  // 纯棋盘格帧可选但建议提供
        && (ProjectorOffPhoto.ImageDiffSignificant ?? true);
}
```

### 3. 拍照流程
**文件**: `Sources/AuroraStruct3D/AuroraStruct3D.Application/Calibration/CalibPhotoAppService.cs`

**修改**: 修改 `TakeExtrinsicPhotoAsync` 方法，增加第三张图拍摄：

当前流程（两图）：
1. S1 白屏模式 → 拍摄圆点标定板
2. S3 棋盘格模式 → 拍摄圆点标定板+棋盘格

新流程（三图）：
1. S1 白屏模式 → 拍摄圆点标定板（WhiteScreen）
2. S3 棋盘格模式 → 拍摄圆点标定板+棋盘格（Checkerboard）
3. **提示用户移除标定板** → 拍摄纯棋盘格（CheckerboardOnly）

### 4. 分组逻辑
**文件**: `Sources/AuroraStruct3D/AuroraStruct3D.Application/Calibration/CalibPhotoAppService.cs`

**修改**: 更新 `BuildExtrinsicSampleGroups` 方法，支持三图分组：
- WhiteScreen → ProjectorOffPhoto
- Checkerboard → ProjectorOnPhoto  
- CheckerboardOnly → CheckerboardOnlyPhoto

### 5. 外参计算算法
**文件**: `Sources/AuroraStruct3D/AuroraStruct3D.Application/Calibration/CalibPhotoAppService.cs`

**修改**: 修改 `ComputeExtrinsicAsync` 方法：
- SolvePnP 仍使用白屏帧的圆点角点
- 投影仪标定优先使用纯棋盘格帧检测棋盘格角点
- 如果纯棋盘格帧不可用，回退到叠加帧

### 6. DTO 定义
**文件**: `Sources/AuroraStruct3D/AuroraStruct3D.Application.Contracts/Calibration/Dtos/CalibPhotoDtos.cs`

**修改**: 
- 在 `ExtrinsicPhotoPhaseDto` 枚举中添加 `CheckerboardOnly`
- 更新 `CalibExtrinsicSampleDto` 添加 `CheckerboardOnlyPhoto` 字段

### 7. 前端展示
**文件**: `Sources/AuroraStruct3D/AuroraStruct3D.Frontend/src/views/calibration/CalibStep5CameraCalib.vue`

**修改**:
- 显示三张图片（白屏帧、叠加帧、纯棋盘格帧）
- 添加 "移除标定板" 的操作提示
- 更新验证逻辑

---

## 三、实现步骤

### 步骤 1：更新枚举定义
- 修改 `CalibConsts.cs` 添加 `CheckerboardOnly` 枚举值
- 修改 DTO 文件同步添加

### 步骤 2：更新样本分组结构
- 修改 `ProjectorExtrinsicSampleGroup` 添加 `CheckerboardOnlyPhoto` 属性
- 更新 `IsValid` 判断逻辑

### 步骤 3：修改拍照流程
- 修改 `TakeExtrinsicPhotoAsync` 方法
- 在拍摄完前两张后，提示用户移除标定板，等待确认后拍摄第三张
- 或者自动拍摄第三张（需要确保用户已移除标定板）

### 步骤 4：更新分组逻辑
- 修改 `BuildExtrinsicSampleGroups` 方法
- 支持将 CheckerboardOnly 照片正确分组

### 步骤 5：修改外参计算算法
- 修改 `ComputeExtrinsicAsync` 方法
- 优先使用 `CheckerboardOnlyPhoto` 检测棋盘格角点
- 若无则回退到 `ProjectorOnPhoto`

### 步骤 6：更新前端
- 修改 `CalibStep5CameraCalib.vue`
- 更新图片展示区域为三列布局
- 添加操作提示文案

### 步骤 7：更新 Python 分析脚本
- 修改 `Tools/calib_photo_analyzer.py`
- 支持三图分析模式

---

## 四、潜在风险与注意事项

### 1. 向后兼容性
- 旧的两图数据仍然需要支持
- `CheckerboardOnlyPhoto` 设置为可空，确保旧数据不会出错

### 2. 用户操作流程
- 第三张图需要用户手动移除标定板
- 需要在前端添加明确的操作指引
- 考虑是否需要暂停等待用户确认

### 3. 算法回退机制
- 如果纯棋盘格帧检测失败，应回退到使用叠加帧
- 需要确保两种模式都能正常工作

### 4. 差分验证逻辑
- 需要验证白屏帧 vs 叠加帧（投影仪切换）
- 需要验证叠加帧 vs 纯棋盘格帧（标定板移除）
- 两张差分都应显著才认为有效

---

## 五、验证标准

1. **编译通过**：所有修改的文件编译无错误
2. **旧数据兼容**：使用两图方案拍摄的历史数据仍可正常计算
3. **新流程可用**：三图方案拍摄的数据能正确分组和计算
4. **检测准确性**：纯棋盘格帧的棋盘格角点检测成功率显著提升
5. **前端展示**：三张图片正确显示，操作流程清晰

---

## 六、文件修改清单

| 文件路径 | 修改内容 |
|----------|----------|
| `Sources/AuroraStruct3D/AuroraStruct3D.Domain.Shared/Calibration/CalibConsts.cs` | 添加 `CheckerboardOnly` 枚举值 |
| `Sources/AuroraStruct3D/AuroraStruct3D.Application.Contracts/Calibration/Dtos/CalibPhotoDtos.cs` | 更新枚举和 DTO |
| `Sources/AuroraStruct3D/AuroraStruct3D.Application/Calibration/CalibPhotoAppService.cs` | 更新分组结构、拍照流程、分组逻辑、计算算法 |
| `Sources/AuroraStruct3D/AuroraStruct3D.Frontend/src/views/calibration/CalibStep5CameraCalib.vue` | 更新前端展示和操作流程 |
| `Tools/calib_photo_analyzer.py` | 支持三图分析模式 |
