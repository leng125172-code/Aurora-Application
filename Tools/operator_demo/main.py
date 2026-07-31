#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
全算子演示工作流自动创建脚本

功能：
  1. 在指定服务上创建测试项目
  2. 为该项目创建多个工作流，覆盖所有算子类别：
     - 2D图像处理工作流
     - 按功能拆分的 3D 点云预处理、平面度、面积、角度和圆形检测工作流
     - 3D配准比较工作流
     - 高度差检测工作流
     - 安装角度检测工作流（平面、轴线、Twist）

用法：
  python main.py --password 1q2w3E*
  python main.py --host 10.40.154.143 --api-port 5000 --password 1q2w3E*

配置优先级：
  1. --base-url / --host
  2. 环境变量 AURORA_BASE_URL / AURORA_HOST
  3. UDP 自动发现
"""

import argparse
import sys
import os
import re

# Add current directory to path
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from constants import (
    DEFAULT_SCHEME,
    DEFAULT_API_PORT,
    DEFAULT_DISCOVERY_PORT,
    DEFAULT_DISCOVERY_TIMEOUT,
    PROJECT,
    POINT_CLOUD_PATH,
    TARGET_POINT_CLOUD_PATH,
    IMAGE_PATH,
)
from utils import resolve_base_url, new_uuid
import api_client
from workflow_builders.image_processing import build_2d_image_processing_graph
from workflow_builders.point_cloud_processing import (
    build_split_3d_point_cloud_graphs,
)
from workflow_builders.registration import build_3d_registration_graph
from workflow_builders.workflow_inspection import build_workflow_inspection_graph
from workflow_builders.height_diff import build_height_diff_graph
from workflow_builders.installation_angle import build_installation_angle_graph
from constants import (
    OP_ANNOTATE_HEIGHT_DIFF,
    OP_DEFINE_PLANE_ROI,
    OP_EXTRACT_POINTS_IN_PLANE_ROI,
    OP_FITTED_PLANE_MESH,
    OP_INSTALLATION_AXIS_TO_AXIS,
    OP_INSTALLATION_AXIS_TO_PLANE,
    OP_INSTALLATION_PLANE_ANGLE,
    OP_INSTALLATION_TWIST,
    OP_INSTALLATION_ANGLE_FEATURE,
    OP_COLORED_CLOUD_TO_TILTED_IMAGE,
    OP_Z_COLORIZE_POINT_CLOUD,
    OP_SAVE_IMAGE_BLOB,
    OP_READ_POINT_CLOUD,
    OP_SELECT_FITTED_PLANE,
    OP_SHAPE_INSPECTION,
    OP_DIMENSION_ANGLE_INSPECTION,
    OP_FLATNESS_INSPECTION,
)


def get_server_operator_contracts():
    """读取当前服务实际部署的算子端口，避免本地源码与服务器版本不一致。"""
    palette = api_client.api_get("/api/app/workflow-node-palette")
    contracts = {}
    for category in palette.get("categories") or []:
        for item in category.get("nodes") or []:
            operator_id = str(item.get("id") or "").lower()
            if not operator_id:
                continue
            contracts[operator_id] = {
                "inputs": {
                    port.get("name")
                    for port in (item.get("inputPorts") or [])
                    if port.get("name")
                },
                "outputs": {
                    port.get("name")
                    for port in (item.get("outputPorts") or [])
                    if port.get("name")
                },
                "configs": {
                    field.get("name")
                    for field in (item.get("configFields") or [])
                    if field.get("name")
                },
                "overlayMode": item.get("overlayMode"),
            }
    return contracts


def get_output_variables(graph_data):
    """
    从唯一的 end-node 输入绑定中提取工作流对外输出变量。

    end-node 的输出结构为：
        inputBindings: { "稳定端口键": "运行时变量名" }
        inputBindingDisplayNames: { "稳定端口键": "Display 名称" }

    后端在保存整个工作流时直接从这里推导输出，不再调用 /output-config PUT。
    Display 名称为空时，接口回退为运行时变量名。
    """
    end_nodes = [
        node for node in graph_data.get("nodes", []) if node.get("type") == "end-node"
    ]
    if len(end_nodes) != 1:
        raise ValueError(
            f"工作流必须且只能包含一个 end-node，当前数量：{len(end_nodes)}"
        )

    properties = end_nodes[0].get("properties") or {}
    input_bindings = properties.get("inputBindings") or {}
    if not isinstance(input_bindings, dict):
        raise ValueError("end-node.properties.inputBindings 必须是对象")

    # dict 保持构建器声明顺序；去重可避免多个对外端口意外绑定同一变量。
    output_variables = list(
        dict.fromkeys(
            value.strip()
            for value in input_bindings.values()
            if isinstance(value, str) and value.strip()
        )
    )
    if not output_variables:
        raise ValueError("end-node 尚未绑定任何输出变量")

    return output_variables


def validate_graph_contracts(graph_data, contracts):
    """在发送前按服务端节点面板校验算子端口和配置键。"""
    errors = []
    for graph_node in graph_data.get("nodes", []):
        operator_id = str(graph_node.get("type") or "").lower()
        if operator_id in {"start-node", "end-node"} or operator_id.startswith(
            "builtin::"
        ):
            continue
        contract = contracts.get(operator_id)
        title = (graph_node.get("text") or {}).get("value") or graph_node.get("id")
        if contract is None:
            errors.append(f"{title}: 服务端不存在算子 {operator_id}")
            continue

        properties = graph_node.get("properties") or {}
        checks = (
            ("输入", properties.get("inputBindings") or {}, contract["inputs"]),
            ("输出", properties.get("outputBindings") or {}, contract["outputs"]),
            ("配置", properties.get("params") or {}, contract["configs"]),
        )
        for label, bindings, allowed in checks:
            unknown = sorted(set(bindings) - allowed)
            if unknown:
                errors.append(f"{title}: 未知{label}键 {', '.join(unknown)}")

        input_bindings = properties.get("inputBindings") or {}
        invalid_input_types = sorted(
            name
            for name, value in input_bindings.items()
            if not isinstance(value, str)
        )
        if invalid_input_types:
            errors.append(
                f"{title}: 输入绑定值必须是字符串，类型错误端口 "
                + ", ".join(invalid_input_types)
            )

        source_maps = (
            ("配置", properties.get("params") or {}, properties.get("paramSources") or {}),
            (
                "输入",
                properties.get("inputBindings") or {},
                properties.get("inputBindingSources") or {},
            ),
            (
                "输出",
                properties.get("outputBindings") or {},
                properties.get("outputBindingSources") or {},
            ),
        )
        for label, bindings, sources in source_maps:
            missing_sources = sorted(set(bindings) - set(sources))
            unknown_sources = sorted(set(sources) - set(bindings))
            if missing_sources:
                errors.append(
                    f"{title}: {label} source map 缺少键 {', '.join(missing_sources)}"
                )
            if unknown_sources:
                errors.append(
                    f"{title}: {label} source map 存在多余键 {', '.join(unknown_sources)}"
                )

        output_bindings = properties.get("outputBindings") or {}
        missing_outputs = sorted(contract["outputs"] - set(output_bindings))
        if missing_outputs:
            errors.append(f"{title}: 缺少输出绑定 {', '.join(missing_outputs)}")

    if errors:
        raise ValueError("工作流与当前服务算子契约不一致：\n  - " + "\n  - ".join(errors))


def complete_graph_output_bindings(graph_data, contracts):
    """为服务端新增但 demo 尚未使用的输出端口补充稳定变量。"""
    used_variables = set()
    for graph_node in graph_data.get("nodes", []):
        properties = graph_node.get("properties") or {}
        used_variables.update(
            value
            for value in (properties.get("outputBindings") or {}).values()
            if isinstance(value, str) and value
        )

    additions = []
    operator_occurrences = {}
    for graph_node in graph_data.get("nodes", []):
        operator_id = str(graph_node.get("type") or "").lower()
        contract = contracts.get(operator_id)
        if contract is None:
            continue

        properties = graph_node.setdefault("properties", {})
        output_bindings = properties.setdefault("outputBindings", {})
        output_sources = properties.setdefault("outputBindingSources", {})
        occurrence = operator_occurrences.get(operator_id, 0) + 1
        operator_occurrences[operator_id] = occurrence
        operator_suffix = re.sub(r"[^A-Za-z0-9_]", "_", operator_id).strip("_")
        node_suffix = f"{operator_suffix[:8] or 'operator'}_{occurrence}"
        title = (graph_node.get("text") or {}).get("value") or graph_node.get("id")

        for output_name in sorted(contract["outputs"] - set(output_bindings)):
            variable_base = re.sub(r"[^A-Za-z0-9_]", "_", output_name).strip("_")
            if not variable_base or variable_base[0].isdigit():
                variable_base = f"output_{variable_base}"
            variable_name = f"{variable_base}_{node_suffix}"
            sequence = 2
            while variable_name in used_variables:
                variable_name = f"{variable_base}_{node_suffix}_{sequence}"
                sequence += 1

            output_bindings[output_name] = variable_name
            output_sources[output_name] = "variable"
            used_variables.add(variable_name)
            additions.append(f"{title}.{output_name} → {variable_name}")

    if additions:
        print(f"      ✓ 自动补齐 {len(additions)} 个新增输出绑定")
        for addition in additions:
            print(f"        - {addition}")


def report_output_variables(graph_data):
    """输出变量由结束节点绑定声明，并在工作流保存时由后端自动同步。"""
    output_variables = get_output_variables(graph_data)
    print(f"      ✓ 结束节点输出：{', '.join(output_variables)}")


def generate_and_validate_v2_source(workflow_name, graph_data):
    """使用目标服务的正式生成器生成 V2 源码，并在保存前阻止旧语法进入数据库。"""
    source = api_client.api_post(
        "/api/app/workflow/graph/to-source",
        {
            "name": workflow_name,
            "graphData": graph_data,
        },
    )
    if not isinstance(source, str) or not source.strip():
        raise RuntimeError(f"{workflow_name}: 服务端未返回工作流源码")

    problems = []
    if not re.search(
        r'^\s*Workflow\("(?:\\.|[^"\\])*",\s*2\);\s*$',
        source,
        re.MULTILINE,
    ):
        problems.append("缺少 Workflow(name, 2) 声明")
    if "GraphBegin(" in source or "\n    Node(" in source or "\n    Edge(" in source:
        problems.append("仍包含 V1 Graph/Node/Edge 序列化语句")
    if "_null" in source:
        problems.append("包含旧版错误占位变量 _null")
    if "Return(" not in source:
        problems.append("缺少 Return(...) 输出声明")
    if problems:
        raise RuntimeError(
            f"{workflow_name}: 服务端生成的脚本不是有效 V2："
            + "；".join(problems)
        )

    print(
        f"      ✓ V2 脚本预检通过："
        f"{len(source.splitlines())} 行，{len(source.encode('utf-8'))} bytes"
    )
    return source


def verify_saved_workflow_source(workflow_id, workflow_name):
    """保存后回读源码，确认旧 demo 已真正被 V2 覆盖。"""
    saved = api_client.api_get(f"/api/app/workflow/{workflow_id}/source")
    source = saved.get("sourceCode") or ""
    language_version = saved.get("languageVersion")
    if language_version != 2 or "_null" in source or "GraphBegin(" in source:
        raise RuntimeError(
            f"{workflow_name}: 保存后源码校验失败，"
            f"languageVersion={language_version}"
        )
    print(f"      ✓ 保存后 V2 源码回读确认，revision={saved.get('revision')}")


def create_project():
    print(f"[1/2] 创建项目：{PROJECT['name']} ...")

    try:
        result = api_client.api_post("/api/app/project-info", PROJECT)
        project_id = result["id"]
        print(f"      ✓ 项目创建成功，ID：{project_id}")
        return project_id
    except Exception as post_err:
        existed = api_client.try_find_project_by_code(PROJECT["projectCode"])
        if existed is not None:
            project_id = existed["id"]
            print(f"      ✓ 创建失败后命中已有项目，自动复用 ID：{project_id}")
            return project_id

        fallback_project = dict(PROJECT)
        fallback_project_code = api_client.build_fallback_project_code(
            PROJECT["projectCode"]
        )
        fallback_project["projectCode"] = fallback_project_code
        print(f"      ! 未找到可复用项目，改用新项目编号重试：{fallback_project_code}")

        try:
            result = api_client.api_post("/api/app/project-info", fallback_project)
            project_id = result["id"]
            PROJECT["projectCode"] = fallback_project_code
            print(f"      ✓ 项目创建成功，ID：{project_id}")
            return project_id
        except Exception:
            pass

        raise post_err


def create_workflow(project_id, workflow_name, graph_data, contracts):
    print(f"      创建工作流：{workflow_name} ...")
    complete_graph_output_bindings(graph_data, contracts)
    validate_graph_contracts(graph_data, contracts)
    generate_and_validate_v2_source(workflow_name, graph_data)

    payload = {
        "projectId": project_id,
        "name": workflow_name,
        "graphData": graph_data,
    }

    # 添加详细的调试信息
    print("      发送的请求内容：")
    import json

    print(json.dumps(payload, ensure_ascii=False, indent=2))

    read_node = next(
        (
            node
            for node in graph_data.get("nodes", [])
            if (node.get("text") or {}).get("value") in ("读取图像", "读取图片", "读取点云")
        ),
        None,
    )
    gray_node = next(
        (
            node
            for node in graph_data.get("nodes", [])
            if (node.get("text") or {}).get("value") == "转灰度图"
        ),
        None,
    )

    # #region debug-point A:workflow-payload-summary
    api_client._debug_event(
        "A",
        "main.py:create_workflow:summary",
        "准备创建工作流",
        {
            "workflowName": workflow_name,
            "nodeCount": len(graph_data.get("nodes", [])),
            "edgeCount": len(graph_data.get("edges", [])),
            "firstNodeTypes": [node.get("type") for node in graph_data.get("nodes", [])[:6]],
        },
    )
    # #endregion

    # #region debug-point B:suspicious-node-bindings
    api_client._debug_event(
        "B",
        "main.py:create_workflow:bindings",
        "记录关键节点绑定",
        {
            "workflowName": workflow_name,
            "readNode": {
                "type": (read_node or {}).get("type"),
                "title": ((read_node or {}).get("text") or {}).get("value"),
                "inputBindings": ((read_node or {}).get("properties") or {}).get(
                    "inputBindings", {}
                ),
                "outputBindings": ((read_node or {}).get("properties") or {}).get(
                    "outputBindings", {}
                ),
            },
            "grayNode": {
                "type": (gray_node or {}).get("type"),
                "title": ((gray_node or {}).get("text") or {}).get("value"),
                "inputBindings": ((gray_node or {}).get("properties") or {}).get(
                    "inputBindings", {}
                ),
                "outputBindings": ((gray_node or {}).get("properties") or {}).get(
                    "outputBindings", {}
                ),
            },
        },
    )
    # #endregion

    existed = api_client.find_workflow_by_name(project_id, workflow_name)
    if existed is not None:
        workflow_id = existed.get("id")
        if not workflow_id:
            raise RuntimeError("命中已存在工作流但缺少 id，无法更新")
        result = api_client.api_put(f"/api/app/workflow/{workflow_id}", payload)
        print(f"      ✓ 命中已有工作流，已更新，ID：{workflow_id}")
        verify_saved_workflow_source(workflow_id, workflow_name)
        report_output_variables(graph_data)
        return result

    result = api_client.api_post("/api/app/workflow", payload)
    workflow_id = result.get("id")
    if not workflow_id:
        raise RuntimeError("工作流创建响应缺少 id")
    print(f"      ✓ 工作流创建成功，ID：{workflow_id}")
    verify_saved_workflow_source(workflow_id, workflow_name)
    report_output_variables(graph_data)
    return result


def parse_args():
    parser = argparse.ArgumentParser(description="全算子演示工作流自动创建")
    parser.add_argument(
        "--base-url", default="", help="完整服务地址，如 http://10.40.154.143:5000"
    )
    parser.add_argument("--host", default="", help="服务 IP 或域名")
    parser.add_argument(
        "--scheme",
        default=DEFAULT_SCHEME,
        choices=["http", "https"],
        help="协议，默认 http",
    )
    parser.add_argument(
        "--api-port", type=int, default=DEFAULT_API_PORT, help="API 端口"
    )
    parser.add_argument(
        "--discovery-port",
        type=int,
        default=DEFAULT_DISCOVERY_PORT,
        help="UDP 发现端口，默认 9210",
    )
    parser.add_argument(
        "--discover-timeout",
        type=float,
        default=DEFAULT_DISCOVERY_TIMEOUT,
        help="UDP 自动发现超时时间（秒）",
    )
    parser.add_argument("--username", default=os.getenv("AURORA_USERNAME", "admin"))
    parser.add_argument("--password", default=os.getenv("AURORA_PASSWORD", ""))
    parser.add_argument("--point-cloud-path", default=POINT_CLOUD_PATH)
    parser.add_argument(
        "--upload-point-cloud",
        default="",
        help="本地点云文件；先通过 operator-file/upload 上传，再用返回 BlobName 生成工作流",
    )
    parser.add_argument(
        "--target-point-cloud-path",
        default=TARGET_POINT_CLOUD_PATH,
        help="3D 配准演示的目标点云路径，必须与参考点云不同",
    )
    parser.add_argument("--image-path", default=IMAGE_PATH)
    return parser.parse_args()


def main():
    args = parse_args()
    base_url = resolve_base_url(args)
    username = args.username
    password = args.password
    point_cloud_path = args.point_cloud_path
    target_point_cloud_path = args.target_point_cloud_path
    image_path = args.image_path

    if not password:
        print(
            "✗ 缺少密码，请通过 --password 或 AURORA_PASSWORD 提供。", file=sys.stderr
        )
        sys.exit(2)

    print("=" * 60)
    print("  全算子演示工作流自动创建")
    print(f"  目标服务：{base_url}")
    print(f"  登录用户：{username}")
    print("=" * 60)
    print()

    try:
        api_client.set_base_url(base_url)
        api_client.login(username, password)
        print()
        project_id = create_project()
        uploaded_point_cloud = None
        if args.upload_point_cloud:
            uploaded_point_cloud = api_client.upload_operator_file(
                project_id,
                OP_READ_POINT_CLOUD,
                os.path.abspath(args.upload_point_cloud),
            )
            point_cloud_path = uploaded_point_cloud["blobName"]
        print()
        print(f"[2/2] 创建工作流...")
        contracts = get_server_operator_contracts()
        expected_overlay_modes = {
            OP_SELECT_FITTED_PLANE: "plane",
            OP_DEFINE_PLANE_ROI: "region",
            OP_EXTRACT_POINTS_IN_PLANE_ROI: "none",
        }
        for operator_id, expected_mode in expected_overlay_modes.items():
            actual_mode = contracts.get(operator_id, {}).get("overlayMode")
            if actual_mode != expected_mode:
                print(
                    f"      ! ROI叠加语义不匹配：{operator_id} "
                    f"期望 {expected_mode}，服务返回 {actual_mode or '<空>'}"
                )
        annotate_inputs = contracts.get(OP_ANNOTATE_HEIGHT_DIFF, {}).get(
            "inputs", set()
        )
        extended_height_annotation = {
            "input_mat",
            "roi_metadata_a",
            "roi_metadata_b",
            "height_a",
            "height_b",
            "signed_diff",
            "is_ok",
        }.issubset(annotate_inputs)
        if not extended_height_annotation:
            print(
                "      ! 当前服务的高度差标注算子为旧版端口，"
                "自动使用 input_mat + is_ok 兼容模式。"
            )

        angle_feature_available = OP_INSTALLATION_ANGLE_FEATURE in contracts
        create_workflow(
            project_id,
            "2D图像处理演示",
            build_2d_image_processing_graph(
                image_path,
                include_angle_feature=angle_feature_available,
            ),
            contracts,
        )
        if not angle_feature_available:
            print("      ! 服务端尚未部署安装角度特征检测算子，2D Demo 已使用兼容模式。")
        split_3d_graphs = build_split_3d_point_cloud_graphs(point_cloud_path)
        for workflow_name, graph_data in split_3d_graphs.items():
            create_workflow(project_id, workflow_name, graph_data, contracts)
        create_workflow(
            project_id,
            "3D配准演示",
            build_3d_registration_graph(point_cloud_path, target_point_cloud_path),
            contracts,
        )
        inspection_operator_ids = {
            OP_SHAPE_INSPECTION,
            OP_DIMENSION_ANGLE_INSPECTION,
            OP_FLATNESS_INSPECTION,
        }
        inspection_demo_created = inspection_operator_ids.issubset(contracts)
        if inspection_demo_created:
            create_workflow(
                project_id,
                "工作流检测判定演示",
                build_workflow_inspection_graph(),
                contracts,
            )
        else:
            print(
                "      ! 当前服务尚未部署全部工作流检测判定算子，"
                "已跳过形状、尺寸角度和平面度判定演示。"
            )
        create_workflow(
            project_id,
            "高度差检测演示",
            build_height_diff_graph(
                point_cloud_path,
                extended_annotation=extended_height_annotation,
            ),
            contracts,
        )
        required_installation_operators = {
            OP_SELECT_FITTED_PLANE,
            OP_DEFINE_PLANE_ROI,
            OP_EXTRACT_POINTS_IN_PLANE_ROI,
            OP_INSTALLATION_PLANE_ANGLE,
            OP_INSTALLATION_AXIS_TO_PLANE,
            OP_INSTALLATION_AXIS_TO_AXIS,
            OP_INSTALLATION_TWIST,
            OP_FITTED_PLANE_MESH,
            OP_COLORED_CLOUD_TO_TILTED_IMAGE,
            OP_Z_COLORIZE_POINT_CLOUD,
            OP_SAVE_IMAGE_BLOB,
        }
        missing_installation_operators = sorted(
            operator_id
            for operator_id in required_installation_operators
            if operator_id.lower() not in contracts
        )
        tilted_configs = contracts.get(OP_COLORED_CLOUD_TO_TILTED_IMAGE, {}).get(
            "configs", set()
        )
        required_tilted_configs = {
            "tiltAngleX",
            "tiltAngleY",
            "tiltAngleZ",
            "outputWidth",
            "outputHeight",
            "backgroundColor",
            "autoFit",
            "imageResolution",
        }
        missing_tilted_configs = sorted(required_tilted_configs - tilted_configs)
        installation_created = (
            not missing_installation_operators and not missing_tilted_configs
        )
        if installation_created:
            create_workflow(
                project_id,
                "安装角度检测演示（平面、轴线、Twist）",
                build_installation_angle_graph(point_cloud_path),
                contracts,
            )
        else:
            details = []
            if missing_installation_operators:
                details.append("缺少算子：" + ", ".join(missing_installation_operators))
            if missing_tilted_configs:
                details.append("斜视图缺少配置：" + ", ".join(missing_tilted_configs))
            print(
                "      ! 当前服务尚未部署新版安装角度/平面ROI算子，"
                "已跳过安装角度检测演示。"
                + "；".join(details)
            )

        if uploaded_point_cloud is not None:
            api_client.confirm_operator_file(
                project_id,
                OP_READ_POINT_CLOUD,
                uploaded_point_cloud["blobName"],
            )

        print()
        print("=" * 60)
        print("  创建完成！")
        print(f"  项目 ID：{project_id}")
        workflow_count = (
            3
            + len(split_3d_graphs)
            + int(inspection_demo_created)
            + int(installation_created)
        )
        print(f"  已创建/更新 {workflow_count} 个工作流：")
        print("  - 2D图像处理演示")
        for workflow_name in split_3d_graphs:
            print(f"  - {workflow_name}")
        print("  - 3D配准演示")
        if inspection_demo_created:
            print("  - 工作流检测判定演示")
        else:
            print("  - 工作流检测判定演示：已跳过（服务端算子版本较旧）")
        print("  - 高度差检测演示")
        if installation_created:
            print("  - 安装角度检测演示（平面、轴线、Twist）")
        else:
            print("  - 安装角度检测演示：已跳过（服务端算子版本较旧）")
        print("=" * 60)
    except Exception as e:
        print(f"\n✗ 执行失败：{e}", file=sys.stderr)
        import traceback

        traceback.print_exc()
        sys.exit(1)


if __name__ == "__main__":
    main()
