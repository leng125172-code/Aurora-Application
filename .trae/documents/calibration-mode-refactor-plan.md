# 标定模式重构方案

## 一、需求分析

### 1.1 用户需求
系统需要支持三种标定模式：

| 模式 | 相机配置 | 投影仪配置 | 标定内容 | 优先级 |
|------|----------|------------|----------|--------|
| 单目结构光 | 相机垂拍 | 投影仪侧投 | 单目内参标定 + 投影仪内外参标定（相机-投影仪整体标定） | 保持不变 |
| 双目 | 双目斜拍 | 无 | 单目内参 + 双目外参 | 保持不变 |
| 双目结构光 | 双目斜拍 | 投影仪垂直投影 | 单目内参 + 双目外参，**结构光仅作为纹理生成器**，不参与标定 | 重点实现 |

### 1.2 核心变更
- **单目结构光**（`OneCamera1Light`）：保持现有逻辑，需要投影仪内外参标定
- **双目结构光**（`TwoCamera1Light`）：投影仪**不参与任何标定**，仅作为纹理投影生成工具（光栅/棋盘格发生器）
- **关键区别**：单目结构光是相机垂直拍照，需要和投影仪标定；双目结构光是两个相机成一定夹角拍摄，优先走双目成像

## 二、代码分析

### 2.1 关键文件清单

#### 后端文件
| 文件 | 说明 | 修改类型 |
|------|------|----------|
| [CalibConsts.cs](file:///d:/GitRepos/Aurora%20Application/Sources/AuroraStruct3D/AuroraStruct3D.Domain.Shared/Calibration/CalibConsts.cs) | 标定常量定义，包含设备类型枚举 | 修改 |
| [CalibProject.cs](file:///d:/GitRepos/Aurora%20Application/Sources/AuroraStruct3D/AuroraStruct3D.Domain/Calibration/CalibProject.cs) | 标定项目实体，包含设备类型判断逻辑 | 修改 |
| [CalibCameraParam.cs](file:///d:/GitRepos/Aurora%20Application/Sources/AuroraStruct3D/AuroraStruct3D.Domain/Calibration/CalibCameraParam.cs) | 相机标定参数实体，包含投影仪标定字段 | 修改 |
| [CalibPhotoAppService.cs](file:///d:/GitRepos/Aurora%20Application/Sources/AuroraStruct3D/AuroraStruct3D.Application/Calibration/CalibPhotoAppService.cs) | 标定照片服务，包含内外参计算逻辑 | 修改 |
| [CalibPhotoDtos.cs](file:///d:/GitRepos/Aurora%20Application/Sources/AuroraStruct3D/AuroraStruct3D.Application.Contracts/Calibration/Dtos/CalibPhotoDtos.cs) | 标定相关 DTO | 修改 |

#### 前端文件
| 文件 | 说明 | 修改类型 |
|------|------|----------|
| [CalibWizardPage.vue](file:///d:/GitRepos/Aurora%20Application/Sources/AuroraStruct3D/AuroraStruct3D.Frontend/src/views/calibration/CalibWizardPage.vue) | 标定向导页 | 修改 |
| [CalibStep5CameraCalib.vue](file:///d:/GitRepos/Aurora%20Application/Sources/AuroraStruct3D/AuroraStruct3D.Frontend/src/views/calibration/CalibStep5CameraCalib.vue) | 相机标定步骤 | 修改 |
| [CalibStep3ProjectorConfig.vue](file:///d:/GitRepos/Aurora%20Application/Sources/AuroraStruct3D/AuroraStruct3D.Frontend/src/views/calibration/CalibStep3ProjectorConfig.vue) | 投影仪配置步骤 | 修改 |

### 2.2 需要移除的功能

#### 2.2.1 后端移除项
1. **投影仪内参相关字段**（`CalibCameraParam.cs`）：
   - `ProjectorIntrinsicMatrixJson`
   - `ProjectorDistCoeffsJson`
   - `CameraToProjectorRJson`
   - `CameraToProjectorTJson`
   - `ProjectorCalibReprojectionError`
   - `ProjectorReprojectionError`（相机外参中的投影仪误差字段）

2. **外参计算逻辑**（`CalibPhotoAppService.cs`）：
   - `ComputeExtrinsicAsync` 方法中投影仪内参计算部分（约第 1290-1407 行）
   - `ComputeCalibrationAsync` 方法中投影仪内参计算部分（约第 1736-1885 行）
   - `BuildProjectorObjectPointsCamAsync` 方法（用于投影仪标定的辅助方法）
   - `FindBestExtrinsicAsync` 方法中投影仪相关逻辑

3. **外参拍照方法**（`CalibPhotoAppService.cs`）：
   - `TakeExtrinsicPhotoAsync`（在 `TwoCamera1Light` 模式下禁用）
   - `TakeExtrinsicDotPhotoAsync`（在 `TwoCamera1Light` 模式下禁用）
   - `TakeExtrinsicCheckerboardPhotoAsync`（在 `TwoCamera1Light` 模式下禁用）

4. **相关 DTO 字段**（`CalibPhotoDtos.cs`）：
   - `CalibComputeResultDto` 中的投影仪标定字段

#### 2.2.2 前端移除项
1. **外参计算按钮**（`CalibStep5CameraCalib.vue`）：
   - 在 `TwoCamera1Light` 模式下隐藏外参计算按钮
   - 隐藏投影仪标定结果显示区域

2. **投影外参拍照按钮**（`CalibStep5CameraCalib.vue`）：
   - 在 `TwoCamera1Light` 模式下隐藏圆点标定板拍照和棋盘格拍照按钮

3. **外参照片列表**（`CalibStep5CameraCalib.vue`）：
   - 在 `TwoCamera1Light` 模式下隐藏外参照片相关 UI

## 三、实施步骤

### 步骤 1：修改设备类型枚举和判断逻辑

**文件**: `CalibConsts.cs`

**修改内容**:
- 保持现有 `CalibDeviceType` 枚举不变
- 在 `DeviceSeries` 或新增字段中增加判断逻辑，区分"结构光参与标定"和"结构光仅作为发生器"

**方案**: 新增枚举或标志位来区分模式

```csharp
// 方案：新增标定模式枚举
public enum CalibMode
{
    /// <summary>普通模式（结构光参与标定）</summary>
    Standard = 0,
    
    /// <summary>双目结构光模式（结构光仅作为发生器）</summary>
    StereoStructuredLight = 1,
}
```

**更优方案**: 在 `CalibProject` 中增加 `ProjectorCalibMode` 字段，或通过 `DeviceType` 直接判断：
- `OneCamera1Light` → 投影仪参与标定
- `TwoCamera1Light` → 投影仪不参与标定

### 步骤 2：修改 CalibProject 实体

**文件**: `CalibProject.cs`

**修改内容**:
- 保持现有结构不变
- 修改 `SetDeviceType` 方法，确保 `TwoCamera1Light` 模式下 `ProjectorCount = 1`（仍需要投影仪作为发生器）
- 新增方法 `IsProjectorCalibrationRequired()` 返回是否需要投影仪标定

### 步骤 3：修改 CalibCameraParam 实体

**文件**: `CalibCameraParam.cs`

**修改内容**:
- **移除投影仪标定相关字段**：
  - `ProjectorIntrinsicMatrixJson`
  - `ProjectorDistCoeffsJson`
  - `CameraToProjectorRJson`
  - `CameraToProjectorTJson`
  - `ProjectorCalibReprojectionError`
  - `ProjectorReprojectionError`

- **修改 `SetCalibResult` 方法**：
  - 移除投影仪标定相关参数
  - 仅保留相机内参和外参相关参数

- **修改 `ClearCalibResult` 方法**：
  - 移除投影仪标定字段的清理逻辑

### 步骤 4：修改 CalibPhotoAppService

**文件**: `CalibPhotoAppService.cs`

**修改内容**:

#### 4.1 修改 `ComputeExtrinsicAsync`
- 在方法开头增加判断：如果是 `TwoCamera1Light` 模式，抛出异常提示"双目结构光模式不支持投影仪外参标定"

#### 4.2 修改 `ComputeCalibrationAsync`
- 在投影仪内参计算部分增加判断：如果是 `TwoCamera1Light` 模式，跳过投影仪标定逻辑

#### 4.3 修改外参拍照相关方法
- `TakeExtrinsicPhotoAsync`
- `TakeExtrinsicDotPhotoAsync`
- `TakeExtrinsicCheckerboardPhotoAsync`

在这些方法开头增加判断：如果是 `TwoCamera1Light` 模式，抛出异常提示"双目结构光模式不支持投影外参拍照"

### 步骤 5：修改前端 CalibStep5CameraCalib.vue

**文件**: `CalibStep5CameraCalib.vue`

**修改内容**:

#### 5.1 修改 `showProjectorSection` 计算属性
- 对于 `TwoCamera1Light` 模式，仍显示投影仪 LED 控制（作为发生器需要控制灯光）
- 但隐藏外参拍照和外参计算相关 UI

#### 5.2 修改 `canUseProjectorExtrinsic` 函数
- 返回 `false` 当设备类型为 `TwoCamera1Light`

#### 5.3 隐藏外参相关按钮
- 外参圆点拍照按钮
- 外参棋盘格拍照按钮
- 外参计算按钮

#### 5.4 隐藏投影仪标定结果显示区域

### 步骤 6：修改前端 CalibWizardPage.vue

**文件**: `CalibWizardPage.vue`

**修改内容**:
- 保持 Step 3（投影仪配置）可见，因为 `TwoCamera1Light` 仍需要配置投影仪参数作为发生器

### 步骤 7：修改 CalibStep3ProjectorConfig.vue

**文件**: `CalibStep3ProjectorConfig.vue`

**修改内容**:
- 保持现有功能不变，投影仪仍作为条纹/棋盘格发生器使用

### 步骤 8：数据库迁移

**说明**: 由于我们只是修改逻辑，不涉及数据库表结构变化，因此不需要创建新的 EF Core 迁移。

## 四、Git 分支管理

### 4.1 当前状态
- 当前工作分支：待确认

### 4.2 操作步骤
1. **合并当前分支到 dev**：
   ```bash
   git checkout dev
   git merge <当前分支>
   git push
   ```

2. **创建新的标定分支**：
   ```bash
   git checkout -b feature/stereo-structured-light-calibration
   ```

3. **在新分支上完成所有修改**

4. **提交并推送**：
   ```bash
   git add .
   git commit -m "feat(calibration): refactor stereo structured light calibration mode"
   git push -u origin feature/stereo-structured-light-calibration
   ```

## 五、风险评估

| 风险 | 影响 | 缓解措施 |
|------|------|----------|
| 测试覆盖 | 三种模式需要分别测试 | 确保每种模式都有完整的测试用例 |
| 数据库迁移 | 移除字段需要创建新迁移 | 执行 `dotnet ef migrations add` 创建迁移 |
| 功能回归 | 修改可能影响现有功能 | 编译后进行完整功能测试 |

## 六、验证计划

### 6.1 单目结构光模式（OneCamera1Light）
- [ ] 内参拍照正常
- [ ] 外参圆点拍照正常
- [ ] 外参棋盘格拍照正常
- [ ] 内参计算正常
- [ ] 外参计算正常（包含投影仪标定）

### 6.2 双目模式（TwoCamera0Light）
- [ ] 内参拍照正常（主/从相机）
- [ ] 双目外参成对拍照正常
- [ ] 内参计算正常（主/从相机）
- [ ] 双目外参计算正常

### 6.3 双目结构光模式（TwoCamera1Light）
- [ ] 内参拍照正常（主/从相机）
- [ ] 双目外参成对拍照正常
- [ ] 内参计算正常（主/从相机）
- [ ] 双目外参计算正常
- [ ] **外参圆点拍照不可用**（显示提示）
- [ ] **外参棋盘格拍照不可用**（显示提示）
- [ ] **外参计算不可用**（显示提示）
- [ ] 投影仪 LED 控制正常（作为发生器）
- [ ] 条纹/棋盘格生成正常（作为发生器）

## 七、任务清单

| 序号 | 任务 | 状态 |
|------|------|------|
| 1 | 修改 CalibConsts.cs，新增标定模式判断逻辑 | 待执行 |
| 2 | 修改 CalibProject.cs，新增 IsProjectorCalibrationRequired 方法 | 待执行 |
| 3 | 修改 CalibCameraParam.cs，更新 SetCalibResult 方法 | 待执行 |
| 4 | 修改 CalibPhotoAppService.cs - ComputeExtrinsicAsync | 待执行 |
| 5 | 修改 CalibPhotoAppService.cs - ComputeCalibrationAsync | 待执行 |
| 6 | 修改 CalibPhotoAppService.cs - 外参拍照方法 | 待执行 |
| 7 | 修改 CalibStep5CameraCalib.vue - 隐藏外参相关 UI | 待执行 |
| 8 | 修改 CalibWizardPage.vue - 保持 Step 3 可见 | 待执行 |
| 9 | 修改 CalibStep3ProjectorConfig.vue - 保持发生器功能 | 待执行 |
| 10 | 编译验证 | 待执行 |
| 11 | 测试验证三种模式 | 待执行 |