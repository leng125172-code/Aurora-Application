# 斜视图移除 ROI 选区叠加

## Context

`render_plane_outlines.cs` 在斜视图（tilted view）中仍然渲染了平面的半透明填充色和轮廓线，这在 3D 倾斜视角下不合适——斜视图应该展示 3D 点云的真实形态，不应叠加 2D 的平面选区。

## 检查结果

`render_plane_outlines.cs` 的 `Execute` 方法分 4 步：
1. 收集 Z 值范围（用于 Jet 色彩映射）
2. 计算轮廓点（俯视图用 ContourPoints，斜视图用 ComputeTiltedViewOutline）
3. **填充平面表面**（FillPoly + AddWeighted 半透明叠加）← 斜视图不需要
4. **画轮廓线和标签**（Polylines + PutText）← 斜视图不需要

问题：第 3、4 步在两种视图类型下都执行了，斜视图也被叠加了平面填充和轮廓线。

## 改动计划

**文件**: `Sources/AuroraStruct3D/AuroraStruct3D.OpenCV/RoiOps/render_plane_outlines.cs`

**改动**: 在 `Execute` 方法中，将第 3、4 步（平面填充 + 轮廓线绘制）限制为仅俯视图执行

**How**: 在 Step 3 和 Step 4 的 `foreach` 循环外层加上 `if (_viewType != "tilted")` 条件判断

具体改动：
- 第 279-290 行（Step 3 填充平面表面）：包裹在 `if (_viewType != "tilted")` 中
- 第 292 行开始（Step 4 画轮廓线和标签）：包裹在 `if (_viewType != "tilted")` 中

斜视图模式下，`render_plane_outlines` 只做 BGR→BGRA 转换然后直接输出，不做任何叠加。

## 验证

1. 编译：`dotnet build "Aurora Application.slnx"`
2. 前端运行工作流，切换到斜视图，确认不再显示平面填充色和轮廓线