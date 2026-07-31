# Debug Session: workflow-create-500

- **Status**: [OPEN]
- **Issue**: `create_all_operators_demo.py` 创建工作流时，请求 `/api/app/workflow` 返回 500，当前在创建 `2D图像处理演示` 时失败。
- **Debug Server**: <http://127.0.0.1:7777/event>
- **Log File**: `.dbg/trae-debug-log-workflow-create-500.ndjson`

## Reproduction Steps

1. 激活虚拟环境 `d:\GitRepos\Aurora Application\.conda`
2. 在 [Tools/create_all_operators_demo.py](file:///D:/GitRepos/Aurora%20Application/Tools/create_all_operators_demo.py) 运行：
   `python create_all_operators_demo.py --password 1q2w3E*`
3. 观察首次创建工作流 `2D图像处理演示` 时的返回结果

## Hypotheses & Verification

| ID | Hypothesis | Likelihood | Effort | Evidence |
|----|------------|------------|--------|----------|
| A | 2D 工作流中的算子端口名与后端定义不一致，导致服务端校验/编译异常 | High | Low | Confirmed |
| B | 2D 工作流中的参数名与算子配置定义不一致，导致反射绑定失败 | High | Low | Inconclusive |
| C | 请求体 JSON 结构满足 DTO，但某些节点类型或输出绑定名不被注册表接受 | Medium | Low | Rejected |
| D | 服务端抛出了用户友好异常之外的内部异常，脚本未打印完整响应体 | Medium | Low | Confirmed |

## Log Evidence

- `pre-fix` 日志显示 `/api/app/workflow` 返回 500，并带有明确错误正文：
  `节点 3094b1f736664ad29477da508ed6dcdf 的输出端口 output_image 缺少类型元数据，无法自动生成变量声明。`
- 同次请求的工作流绑定摘要显示：
  - `read_image` 节点输入绑定为 `image_path`
  - `read_image` 节点输出绑定为 `output_image`
  - `color_to_grayscale` 节点输入绑定为 `input_image`
  - `color_to_grayscale` 节点输出绑定为 `output_image`
- 结合算子定义可知，上述端口名与真实元数据不一致，至少 `read_image` 的真实端口为 `img_path` / `output_mat`。
- Pending

- `A` 已确认：2D 工作流端口名错误。
- `D` 已确认：脚本之前缺少对 500 响应正文的结构化采集。
- 下一步：修复 2D 工作流节点端口绑定，并继续复现，确认是否还有后续节点错误。
