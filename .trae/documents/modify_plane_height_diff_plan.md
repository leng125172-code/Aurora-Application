# 修改 plane_height_diff 算子输出格式及工作流拓扑

## 需求分析

用户要求：
1. 删除 `aggregate_regions` 算子（已删除）
2. 修改 `plane_height_diff` 算子，输出包含基准区域和目标区域信息的 JSON 结构体（`valueType: string`, `scalarValue: JSON`）
3. 修改工作流脚本，移除 `aggregate_regions` 节点，调整拓扑连接

## 当前状态

- `aggregate_regions.cs` 已删除 ✓
- `plane_height_diff.cs` 已有完整实现，输出包含 `refRegion` 和 `targetRegion` 的 JSON
- `create_height_diff_workflow.py` 仍包含 `aggregate_regions` 节点，需要移除
- `plane_height_diff` 节点缺少 `refRegionName`、`targetRegionName` 参数和 `ref_cloud` 输入
- `plane_height_diff` 节点缺少 `result_json` 输出绑定

## 修改计划

### 1. 修改 `create_height_diff_workflow.py`

#### 1.1 删除 `OP_AGGREGATE_REGIONS` 常量（第381行）

#### 1.2 修改 `plane_height_diff` 节点配置
- 添加 `refRegionName`、`targetRegionName` 参数
- 添加 `ref_cloud` 输入绑定
- 添加 `result_json` 输出绑定

#### 1.3 删除 `aggregate_regions` 节点（第998-1021行）

#### 1.4 修改 `render_plane_outlines` 节点
- 移除 `regions_json` 输入依赖
- 添加 `regionCount` 和 `referenceRegionIndex` 参数

#### 1.5 修改 `annotate_height_diff` 和结束节点
- 调整 `is_ok` 输入来源，改为使用第一个 `plane_height_diff` 节点的输出

#### 1.6 修改边连接
- 删除连接到 `aggregate_regions` 的边
- 调整边连接，跳过 `aggregate_regions`

### 2. 构建验证

运行构建命令验证修改是否正确。

## 风险评估

- `render_plane_outlines` 已改为直接从上下文读取区域数据，不依赖 `regions_json`，修改后应能正常工作
- 需要确保 `plane_height_diff` 的 `ref_cloud` 输入正确绑定到 `region_1_cloud`
- 需要确保结束节点的 `result_json` 和 `is_ok` 输入正确绑定

## 输出格式

`plane_height_diff` 输出的 `result_json` 格式：
```json
{
    "refRegion": {
        "name": "A",
        "x": -16.9643,
        "y": 5.5807,
        "z": -0.0044
    },
    "targetRegion": {
        "name": "B",
        "x": -18.9003,
        "y": -2.3410,
        "z": 2.2178
    },
    "signedDiff": 2.2218,
    "absDiff": 2.2218,
    "minDiff": -0.5,
    "maxDiff": 0.5,
    "isOk": false
}
```
