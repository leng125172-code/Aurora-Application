# 检查：平面/曲面拟合 + ROI 分区筛选

## Context

全面检查项目中所有拟合算子和 ROI 分区逻辑，确认是否存在问题。

## 检查结果

### 一、平面/曲面拟合 — 无问题

除了上一轮已修复的 `ransac_plane_fit.cs` 的 `RefinePlaneWithInliers`，其余所有拟合算子均使用正确的数学方法：

| 算子 | 拟合方法 | 状态 |
|------|----------|------|
| `svd_plane_fit` | `Math3D.SolvePlaneByCovariance`（Jacobi SVD） | OK |
| `ransac_svd_plane_fit` | RANSAC + `Math3D.SolvePlaneByCovariance` | OK |
| `ransac_plane_fit` | 已修复为 `Math3D.SolvePlaneByCovariance` | OK |
| `circle_fit_3d` | RANSAC + `Math3D.SolvePlaneByCovariance` + Kåsa 代数圆拟合 | OK |
| `line_fit_3d` | RANSAC + `Math3D.Jacobi3x3` PCA | OK |
| `sphere_fit` | RANSAC + `Cv2.Solve(SVD)` 最小二乘 | OK |
| `cylinder_fit` | 两点法向射线求交（几何构造，无需特征分解） | OK |

所有算子的协方差计算、法向量归一化、退化情况处理（共线/共面/零半径）均正确。

### 二、ROI 分区筛选 — 发现 1 个 Bug

**文件**: `Sources/AuroraStruct3D/AuroraStruct3D.OpenCV/RoiOps/roi_partition.cs`

**位置**: `ExtractRoiMetadata` 方法，第 643 行

**Bug**: 使用 `c.Length`（轮廓顶点数）筛选最大轮廓，而非实际面积

```csharp
// 当前代码（错误）
Point[] largest = contours.OrderByDescending(c => c.Length).First();
```

- `c.Length` 是 `ApproxSimple` 近似后的轮廓**顶点数**，不代表实际面积
- 一个细长但有少量顶点的轮廓，顶点数可能比紧凑大面积的轮廓还多
- 应改为 `Cv2.ContourArea(c)` 按实际面积排序

**缺少"中心位置分区"逻辑**：
- 当前代码只有"最大分区"选择，没有"优先选择靠近掩膜中心位置的分区"的逻辑
- `RoiMetadata` 中已有 `BoundingRect` 和 `ContourPoints`，可用于计算中心距离
- 如果需要新增此功能，需要加配置参数（如 `partitionStrategy`: `"largest"` / `"center"`）

## 改动计划

### 改动 1：修复 ROI 分区面积筛选 Bug

**文件**: `Sources/AuroraStruct3D/AuroraStruct3D.OpenCV/RoiOps/roi_partition.cs`

**改动**: 第 643 行，将 `c.Length` 改为 `Cv2.ContourArea(c)`

```csharp
// 修改前
Point[] largest = contours.OrderByDescending(c => c.Length).First();

// 修改后
Point[] largest = contours.OrderByDescending(c => Cv2.ContourArea(c)).First();
```

**Why**: 用实际像素面积筛选最大连通区域，比用顶点数准确

### 改动 2：新增"中心位置分区"策略

**文件**: `Sources/AuroraStruct3D/AuroraStruct3D.OpenCV/RoiOps/roi_partition.cs`

新增配置参数 `partitionStrategy`，默认 `"center"`（优先靠近选区中心，面积占比为次要）。

**How**:
1. `ConfigParameters` 中新增：
   ```csharp
   new ConfigParameter
   {
       Name = "partitionStrategy",
       DisplayName = "分区选择策略",
       ParameterType = typeof(string),
       DefaultValue = "center",
       ValueLimit = new[] { "center", "largest" },
       Required = false,
       ControlType = PortControlType.Select,
   }
   ```
2. 构造函数新增 `string partitionStrategy = "center"` 参数，保存为 `_partitionStrategy`
3. `ExtractRoiMetadata` 新增 `imageWidth`、`imageHeight` 参数，筛选轮廓时根据策略：
   - `"largest"`: `OrderByDescending(c => Cv2.ContourArea(c)).First()`
   - `"center"`: 计算每个轮廓的质心（`Cv2.Moments`），按离图像中心 `(imageWidth/2, imageHeight/2)` 的距离升序排序，距离相同时按面积降序作为 tiebreaker
4. 调用处 `ExtractRoiMetadata(mask, roi.Name, roi.Type.ToString(), i)` 改为 `ExtractRoiMetadata(mask, roi.Name, roi.Type.ToString(), i, imageWidth, imageHeight)`

## 验证

1. 编译：`dotnet build "Aurora Application.slnx"`
2. 最大分区：确认 ROI 掩膜中有多个连通区域时，正确选择**面积最大**的分区
3. 中心分区：确认选择离掩膜图像**中心最近**的分区，多个距离相同时选面积大的