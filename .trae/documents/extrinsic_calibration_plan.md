# 外参标定流程优化实现计划

## 一、需求分析

根据用户描述，当前外参标定流程存在问题，需要按照以下步骤进行系统性优化：

### 1.1 首次拍摄阶段
- 开启投影仪灯光系统
- 投射 S1 纯白屏图案
- 对圆点标定板进行高质量图像采集

### 1.2 模式切换与二次拍摄
- 将投影仪灯光模式切换至 S3（棋盘格模式）
- 加载并应用投影棋盘格参数配置文件
- 对投影棋盘格与圆点标定板组合场景进行二次图像采集

### 1.3 图像质量验证流程
- 执行图像差分算法，验证前后两次拍摄图像的差异性
- 进行角点提取操作，确保关键特征点可被准确识别
- 根据上述结果快速判断当前采集图片的有效性

### 1.4 数据采集与外参计算
- 重复步骤 1-3，直至完成 10 组合格图像的拍摄
- 对所有采集图像进行批量图像差分分析
- 执行角点提取与匹配操作
- 基于上述处理结果进行相机-投影仪外参计算
- 输出标定结果，包括转换矩阵、重投影误差等关键参数

### 1.5 与当前实现的差异分析

| 环节 | 当前实现 | 需求实现 |
|------|---------|---------|
| 标定板类型 | 支持棋盘格/圆点 | 使用圆点标定板（MarkedSymmetricCircleGrid） |
| 首次拍摄 | 白屏模式拍摄实体板 | S1 白屏模式拍摄圆点标定板 |
| 二次拍摄 | 条纹投影模式拍摄 | S3 棋盘格模式拍摄投影棋盘格+圆点标定板组合场景 |
| 质量验证 | 仅角点检测 | 图像差分算法 + 角点提取 |
| 外参计算 | 使用条纹帧 | 使用投影棋盘格图像 |

---

## 二、技术方案

### 2.1 新增数据结构

#### 2.1.1 扩展外参照片阶段枚举
当前 `ExtrinsicPhotoPhase` 仅有 `ProjectorOff` 和 `ProjectorOn`，需要新增：
- `WhiteScreen`（S1 白屏模式拍摄圆点标定板）
- `Checkerboard`（S3 棋盘格模式拍摄投影棋盘格）

#### 2.1.2 新增图像差分结果数据结构
用于存储图像差分分析结果：
```csharp
public class ImageDiffResult
{
    public bool IsSignificant;       // 差分是否显著（两次拍摄有差异）
    public double DiffScore;         // 差分分数（0-1）
    public int ChangedPixelCount;    // 变化像素数量
    public double ChangedPixelRatio; // 变化像素比例
}
```

### 2.2 后端修改

#### 2.2.1 修改 CalibPhotoAppService.cs
**文件路径**: `Sources/AuroraStruct3D/AuroraStruct3D.Application/Calibration/CalibPhotoAppService.cs`

主要修改点：
1. **重写 `TakeExtrinsicPhotoAsync` 方法**：
   - 第一步：开启 LED → 设置 S1 白屏模式 → 拍摄圆点标定板
   - 第二步：切换 S3 棋盘格模式 → 设置棋盘格像素尺寸 → 拍摄投影棋盘格+圆点标定板场景
   - 执行图像差分验证两次拍摄的差异性
   - 执行角点检测验证标定板有效性

2. **修改 `ComputeExtrinsicAsync` 方法**：
   - 支持使用投影棋盘格图像进行外参计算
   - 确保使用 10 组合格图像

#### 2.2.2 新增图像处理服务
**文件路径**: `Sources/AuroraStruct3D/AuroraStruct3D.OpenCV/ImageOps/ImageDiffAnalyzer.cs`

实现图像差分算法：
```csharp
public class ImageDiffAnalyzer
{
    public ImageDiffResult ComputeDiff(byte[] image1Bytes, byte[] image2Bytes);
}
```

#### 2.2.3 修改投影仪协议命令
**文件路径**: `Sources/AuroraStruct3D/AuroraStruct3D.Projectors/Protocol/TjProjectorCommands.cs`

确认 S3 棋盘格模式命令（已有 `SetModePrefixByte1` + mode=3）

#### 2.2.4 修改 CalibPhotoRecord 实体
**文件路径**: `Sources/AuroraStruct3D/AuroraStruct3D.Domain/Calibration/CalibPhotoRecord.cs`

添加 `ImageDiffScore` 和 `ImageDiffSignificant` 字段，用于存储图像差分结果

### 2.3 前端修改

#### 2.3.1 修改标定 API
**文件路径**: `Sources/AuroraStruct3D/AuroraStruct3D.Frontend/src/api/calib-photo.ts`

更新 DTO 类型，添加图像差分相关字段

#### 2.3.2 修改标定页面
**文件路径**: `Sources/AuroraStruct3D/AuroraStruct3D.Frontend/src/views/calibration/CalibStep5CameraCalib.vue`

更新外参拍照逻辑和结果展示：
- 显示图像差分分数
- 显示两次拍摄的对比预览
- 添加图像质量验证提示

#### 2.3.3 修改投影仪 API
**文件路径**: `Sources/AuroraStruct3D/AuroraStruct3D.Frontend/src/api/projectors.ts`

确保棋盘格模式和像素尺寸设置功能可用

---

## 三、实施步骤

### 步骤 1：扩展外参照片阶段枚举
- 修改 `CalibPhotoType.cs` 或相关枚举文件
- 新增 `WhiteScreen` 和 `Checkerboard` 阶段

### 步骤 2：修改 CalibPhotoRecord 实体
- 添加图像差分相关字段
- 创建 EF Core 迁移

### 步骤 3：实现图像差分分析器
- 创建 `ImageDiffAnalyzer.cs`
- 实现差分算法（灰度差异 + 阈值判断）

### 步骤 4：修改外参拍照流程
- 重写 `TakeExtrinsicPhotoAsync` 方法
- 实现 S1 → S3 模式切换流程
- 添加图像差分验证逻辑

### 步骤 5：修改外参计算逻辑
- 更新 `ComputeExtrinsicAsync` 方法
- 支持使用投影棋盘格图像

### 步骤 6：更新前端 API 类型
- 更新 DTO 定义，添加图像差分字段

### 步骤 7：更新前端标定页面
- 更新拍照结果展示
- 添加图像差分结果显示
- 添加质量验证状态提示

### 步骤 8：测试验证
- 构建并测试后端 API
- 测试前端界面交互
- 验证完整标定流程

---

## 四、关键文件清单

| 文件路径 | 修改类型 | 说明 |
|---------|---------|------|
| `Sources/AuroraStruct3D/AuroraStruct3D.Domain/Calibration/CalibPhotoRecord.cs` | 修改 | 添加图像差分字段 |
| `Sources/AuroraStruct3D/AuroraStruct3D.Domain/Calibration/CalibConsts.cs` | 修改 | 新增外参照片阶段枚举 |
| `Sources/AuroraStruct3D/AuroraStruct3D.Application/Calibration/CalibPhotoAppService.cs` | 修改 | 重写外参拍照和计算方法 |
| `Sources/AuroraStruct3D/AuroraStruct3D.OpenCV/ImageOps/ImageDiffAnalyzer.cs` | 新建 | 图像差分分析器 |
| `Sources/AuroraStruct3D/AuroraStruct3D.Frontend/src/api/calib-photo.ts` | 修改 | 更新 API 类型定义 |
| `Sources/AuroraStruct3D/AuroraStruct3D.Frontend/src/views/calibration/CalibStep5CameraCalib.vue` | 修改 | 更新前端标定页面 |

---

## 五、潜在风险与注意事项

### 5.1 投影仪协议兼容性
- 确保 S3 棋盘格模式命令正确（`S3\r\n`）
- 棋盘格像素尺寸设置命令需正确传递参数

### 5.2 图像差分算法参数
- 差分阈值需要根据实际场景调整
- 需要处理不同光照条件下的差异

### 5.3 数据库迁移
- 添加新字段需要创建 EF Core 迁移
- 确保迁移脚本正确执行

### 5.4 性能考虑
- 图像差分计算需要在合理时间内完成
- 考虑使用异步处理避免阻塞 UI

### 5.5 向后兼容性
- 原有条纹外参标定流程需要保留（作为可选模式）
- 新流程应作为默认流程

---

## 六、验证标准

### 功能验证
1. 外参拍照流程正确执行 S1 → S3 模式切换
2. 图像差分算法能正确检测两次拍摄的差异
3. 角点提取能正确识别圆点标定板和投影棋盘格
4. 外参计算使用投影棋盘格图像
5. 输出结果包含转换矩阵和重投影误差

### 质量验证
1. 图像差分分数合理（有投影变化时 > 阈值）
2. 无效图像能被正确识别并提示用户重拍
3. 10 组合格图像能正确计算外参
4. 重投影误差在合理范围内

### 接口验证
1. API 响应包含图像差分结果字段
2. 前端能正确展示差分结果
3. 用户操作流程清晰直观
