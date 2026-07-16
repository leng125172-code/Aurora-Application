# 修复平面拟合 refine 公式 + ROI 轮廓渲染

## Context

两个问题需要修复：

1. **`ransac_plane_fit.cs` 的 refine 步骤用了错误公式** — `RefinePlaneWithInliers` 使用简化公式计算法向量，不是真正的 Jacobi SVD 特征分解。而 `ransac_svd_plane_fit.cs` 和 `svd_plane_fit.cs` 都正确调用了 `Math3D.SolvePlaneByCovariance`。项目记忆要求 `refinePlane: true`，所以这个错误公式会在实际使用中被触发。

2. **`render_plane_outlines.cs` 的 `ComputeTopViewOutline` 总是画矩形** — 当前工作区未提交改动把原来基于 `OutlinePoints`（保留实际 ROI 形状）的轮廓渲染改为了总是计算内点云的 min/max 矩形包围盒，导致圆形 ROI 被渲染成矩形。

## 改动清单

### 改动 1：修复 `ransac_plane_fit.cs` 的 `RefinePlaneWithInliers`

**文件**: `Sources/AuroraStruct3D/AuroraStruct3D.OpenCV/PointCloudFit/ransac_plane_fit.cs`

**改动位置**: 第 322-380 行的 `RefinePlaneWithInliers` 方法

**What**: 将简化公式替换为 `Math3D.SolvePlaneByCovariance` 调用

**Why**: 当前简化公式 `a = 2*cov[0,1]*cov[1,2] - cov[0,2]*cov[1,1]` 等不是正确的协方差矩阵特征分解，与 `ransac_svd_plane_fit.cs` 和 `svd_plane_fit.cs` 中使用的 Jacobi SVD 方法不一致

**How**:
- 当前代码已计算了质心 `(cx, cy, cz)` 和协方差矩阵元素（`cov[0,0]` ~ `cov[2,2]`，但注意：当前代码的 `cov` 是未除以点数的累积和）
- `Math3D.SolvePlaneByCovariance` 接受的参数是**已除以点数的协方差**（即除以 `n` 后的值），所以在调用前需要将 `cov` 各元素除以 `n`
- 替换后的代码结构：
  ```csharp
  // 将 cov 除以点数得到真正的协方差矩阵
  double s00 = cov[0, 0] / n;
  double s01 = cov[0, 1] / n;
  double s02 = cov[0, 2] / n;
  double s11 = cov[1, 1] / n;
  double s12 = cov[1, 2] / n;
  double s22 = cov[2, 2] / n;
  
  double[] refined = Math3D.SolvePlaneByCovariance(s00, s01, s02, s11, s12, s22, cx, cy, cz);
  return ApplyNormalConstraint(refined, normalConstraint);
  ```
- 删除原有的手动法向量计算代码（a, b, c, d 的计算和归一化），因为 `Math3D.SolvePlaneByCovariance` 已包含归一化和 z-up 纠正

### 改动 2：修复 `render_plane_outlines.cs` 的 `ComputeTopViewOutline`

**文件**: `Sources/AuroraStruct3D/AuroraStruct3D.OpenCV/RoiOps/render_plane_outlines.cs`

**改动位置**: 第 324-408 行的 `ComputeTopViewOutline` 方法

**What**: 使用 `region.Roi.ContourPoints`（ROI 的实际轮廓）来生成 outline，而非总是计算矩形包围盒

**Why**: 当前代码总是计算内点云的 min/max 矩形包围盒，导致圆形 ROI 被渲染成矩形。`RoiMetadata.ContourPoints` 中包含 ROI 掩膜的实际轮廓多边形顶点，能保留圆形、多边形等原始形状

**How**:
- `ContourPoints` 是原始掩膜图像（`mapping.ImageWidth × mapping.ImageHeight`）中的像素坐标
- 需要缩放到输出图像（`imageWidth × imageHeight`）的坐标系
- 缩放比例：`scaleX = imageWidth / mapping.ImageWidth`, `scaleY = imageHeight / mapping.ImageHeight`
- 改动后的 `ComputeTopViewOutline`：
  ```csharp
  private Point[] ComputeTopViewOutline(RegionInfo region, int imageWidth, int imageHeight)
  {
      if (region.ProjectionMapping is null)
          return Array.Empty<Point>();
      
      var contourPoints = region.Roi.ContourPoints;
      if (contourPoints is null || contourPoints.Count < 3)
          return Array.Empty<Point>();
      
      var mapping = region.ProjectionMapping;
      double scaleX = (double)imageWidth / mapping.ImageWidth;
      double scaleY = (double)imageHeight / mapping.ImageHeight;
      
      return contourPoints
          .Select(p => new Point(
              (int)Math.Round(p.X * scaleX),
              (int)Math.Round(p.Y * scaleY)
          ))
          .ToArray();
  }
  ```
- 删除对 `region.InlierCloud` 的依赖（不再需要从内点云计算包围盒）

## 验证

1. **平面拟合**：编译项目 `dotnet build "Aurora Application.slnx"`，确认无编译错误
2. **ROI 轮廓**：通过前端运行一个包含圆形 ROI 的工作流，观察渲染结果中圆形 ROI 是否以圆形轮廓显示而非矩形