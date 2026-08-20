#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""创建并试运行完整产品 3D 检查 Demo。

默认上传桌面的 ``最新点云.ply``，创建一个完整产品检查工作流。
服务端返回唯一的强类型 ``inspection_result`` 根对象；成员由工作流语言直接访问。
"""

from __future__ import annotations

import argparse
import json
import os
import sys
import time
import uuid
from pathlib import Path
from typing import Any

TOOLS_DIR = Path(__file__).resolve().parent
OPERATOR_DEMO_DIR = TOOLS_DIR / "operator_demo"
sys.path.insert(0, str(OPERATOR_DEMO_DIR))

import api_client  # noqa: E402
from constants import (  # noqa: E402
    DEFAULT_API_PORT,
    DEFAULT_DISCOVERY_PORT,
    DEFAULT_DISCOVERY_TIMEOUT,
    DEFAULT_SCHEME,
    OP_READ_POINT_CLOUD,
)
from main import (  # noqa: E402
    complete_graph_output_bindings,
    generate_and_validate_v3_source,
    get_server_operator_contracts,
    validate_graph_contracts,
    verify_saved_workflow_source,
)
from utils import resolve_base_url  # noqa: E402
from workflow_builders.installation_angle import (  # noqa: E402
    build_installation_angle_graph,
)
from workflow_builders.point_cloud_processing import (  # noqa: E402
    build_3d_point_cloud_processing_graph,
)


PROJECT = {
    "projectCode": "PRJ-PRODUCT-INSPECTION-DEMO-001",
    "name": "完整产品检查演示项目",
    "version": "1.0.0",
    "description": "完整3D综合检测与安装角检测 Python Demo",
}
PRODUCT_WORKFLOW = "完整产品3D检查"
OP_BUILD_PRODUCT_RESULT = "7f3a2c10-6b95-4e8c-9a41-2d53f670c902"
DEFAULT_POINT_CLOUD = r"C:\Users\zhengkai\Desktop\最新点云.ply"


def validate_ply(path: Path) -> None:
    if not path.is_file():
        raise FileNotFoundError(f"点云文件不存在：{path}")
    with path.open("rb") as stream:
        header = stream.read(4096)
    if not header.startswith(b"ply\n") and not header.startswith(b"ply\r\n"):
        raise ValueError(f"文件不是 PLY 点云：{path}")
    if b"end_header" not in header:
        raise ValueError("PLY 文件头超过 4096 bytes 或缺少 end_header")


def validate_graph_shape(name: str, graph: dict[str, Any]) -> None:
    nodes = graph.get("nodes") or []
    edges = graph.get("edges") or []
    node_ids = [item.get("id") for item in nodes]
    if len(node_ids) != len(set(node_ids)):
        raise ValueError(f"{name}: 存在重复节点 ID")
    starts = [item for item in nodes if item.get("type") == "start-node"]
    ends = [item for item in nodes if item.get("type") == "end-node"]
    if len(starts) != 1 or len(ends) != 1:
        raise ValueError(f"{name}: 必须且只能包含一个开始节点和一个结束节点")
    known = set(node_ids)
    for item in edges:
        if item.get("sourceNodeId") not in known or item.get("targetNodeId") not in known:
            raise ValueError(f"{name}: 边引用了不存在的节点")


def get_or_create_project() -> str:
    existing = api_client.try_find_project_by_code(PROJECT["projectCode"])
    if existing and existing.get("id"):
        print(f"✓ 复用项目：{existing['id']}")
        return existing["id"]
    result = api_client.api_post("/api/app/project-info", PROJECT)
    project_id = (result or {}).get("id")
    if not project_id:
        raise RuntimeError("项目创建响应缺少 id")
    print(f"✓ 创建项目：{project_id}")
    return project_id


def save_workflow(
    project_id: str,
    name: str,
    graph: dict[str, Any],
    contracts: dict[str, Any],
) -> str:
    validate_graph_shape(name, graph)
    complete_graph_output_bindings(graph, contracts)
    validate_graph_contracts(graph, contracts)
    generate_and_validate_v3_source(name, graph)
    payload = {"projectId": project_id, "name": name, "graphData": graph}
    existing = api_client.find_workflow_by_name(project_id, name)
    if existing:
        workflow_id = existing.get("id")
        if not workflow_id:
            raise RuntimeError(f"{name}: 已存在工作流缺少 id")
        api_client.api_put(f"/api/app/workflow/{workflow_id}", payload)
        action = "更新"
    else:
        result = api_client.api_post("/api/app/workflow", payload)
        workflow_id = (result or {}).get("id")
        if not workflow_id:
            raise RuntimeError(f"{name}: 创建响应缺少 id")
        action = "创建"
    verify_saved_workflow_source(workflow_id, name)
    print(f"✓ {action}工作流：{name} ({workflow_id})")
    return workflow_id


def _is_terminal(status: dict[str, Any]) -> bool:
    if status.get("isTerminal") is True:
        return True
    return status.get("status") in (2, 3, 4, "Completed", "Faulted", "Stopped")


def execute_workflow(
    project_id: str, workflow_id: str, name: str, timeout_seconds: int
) -> dict[str, Any]:
    trigger = api_client.api_post(
        "/api/app/workflow/executions",
        {
            "projectId": project_id,
            "workflowId": workflow_id,
            "mode": 0,
            "loopCount": 1,
            "inputVariableKeys": [],
            "outputVariableNames": [],
        },
        timeout=timeout_seconds,
    )
    if not isinstance(trigger, dict) or trigger.get("error"):
        raise RuntimeError(f"{name}: 启动失败：{trigger}")
    execution_id = trigger.get("executionId")
    if not execution_id:
        raise RuntimeError(f"{name}: 启动响应缺少 executionId")

    status = trigger.get("status") or {}
    if _is_terminal(status):
        if status.get("status") not in (2, "Completed") and status.get("debugState") != "completed":
            raise RuntimeError(
                f"{name}: 执行未成功，状态={status.get('status')}，"
                f"错误={status.get('errorMessage') or trigger.get('message') or '<无>'}"
            )
        outputs = trigger.get("variables") or []
        if not isinstance(outputs, list):
            raise RuntimeError(f"{name}: RunOnce 正式输出响应无效")
        print(f"✓ 执行完成：{name} ({execution_id})，耗时 {status.get('durationMs', 0)} ms")
        return {"executionId": execution_id, "status": status, "outputs": outputs}

    deadline = time.monotonic() + timeout_seconds
    while not _is_terminal(status):
        if time.monotonic() >= deadline:
            raise TimeoutError(f"{name}: 执行超过 {timeout_seconds} 秒")
        time.sleep(1)
        status = api_client.api_get(
            f"/api/app/workflow/executions/{execution_id}",
            {"includeVariables": "false"},
        )
        if not isinstance(status, dict):
            raise RuntimeError(f"{name}: 执行状态响应无效")

    if status.get("status") not in (2, "Completed") and status.get("debugState") != "completed":
        raise RuntimeError(
            f"{name}: 执行未成功，状态={status.get('status')}，"
            f"错误={status.get('errorMessage') or '<无>'}"
        )
    result = api_client.api_get(f"/api/app/workflow/executions/{execution_id}/result")
    outputs = result if isinstance(result, list) else (result or {}).get("outputs") or []
    if not isinstance(outputs, list):
        raise RuntimeError(f"{name}: 正式输出响应无效")
    print(f"✓ 执行完成：{name} ({execution_id})，耗时 {status.get('durationMs', 0)} ms")
    return {"executionId": execution_id, "status": status, "outputs": outputs}


def build_product_graph(blob_name: str) -> dict[str, Any]:
    """Merge both inspection branches and add one typed product-result exit."""
    comprehensive = build_3d_point_cloud_processing_graph(blob_name, relaxed_demo=True)
    installation = build_installation_angle_graph(blob_name, relaxed_demo=True)
    for item in comprehensive["nodes"]:
        properties = item.get("properties") or {}
        for section in ("inputBindings", "outputBindings"):
            bindings = properties.get(section) or {}
            for port, value in tuple(bindings.items()):
                if value == "inspection_result":
                    bindings[port] = "comprehensive_result"
    comprehensive_variables = {
        value for item in comprehensive["nodes"]
        for value in ((item.get("properties") or {}).get("outputBindings") or {}).values()
    }
    installation_variables = {
        value for item in installation["nodes"]
        for value in ((item.get("properties") or {}).get("outputBindings") or {}).values()
    }
    renamed = {name: f"installation_{name}" for name in installation_variables & comprehensive_variables
               if name != "installation_result"}
    for item in installation["nodes"]:
        properties = item.get("properties") or {}
        for section in ("inputBindings", "outputBindings"):
            bindings = properties.get(section) or {}
            for port, value in tuple(bindings.items()):
                root, dot, suffix = value.partition(".") if isinstance(value, str) else (value, "", "")
                if root in renamed:
                    bindings[port] = renamed[root] + (dot + suffix if dot else "")
        for value in (properties.get("params") or {}).values():
            if isinstance(value, dict) and value.get("$var") in renamed:
                value["$var"] = renamed[value["$var"]]
    nodes = [n for n in comprehensive["nodes"] if n.get("type") not in ("start-node", "end-node")]
    nodes += [n for n in installation["nodes"] if n.get("type") not in ("start-node", "end-node")]
    valid_ids = {n["id"] for n in nodes}
    edges = [e for e in comprehensive["edges"] + installation["edges"]
             if e.get("sourceNodeId") in valid_ids and e.get("targetNodeId") in valid_ids]
    start_id, product_id, end_id = (str(uuid.uuid4()) for _ in range(3))
    nodes.insert(0, {"id": start_id, "type": "start-node", "x": 40, "y": 40,
                     "text": {"value": "开始"}})
    nodes.append({
        "id": product_id, "type": OP_BUILD_PRODUCT_RESULT, "x": 1040, "y": 1320,
        "text": {"value": "产品检查结果"},
        "properties": {
            "inputBindings": {
                "comprehensive": "comprehensive_result", "installation": "installation_result",
                "point_cloud_url": "cloud_download_url", "projection_image_url": "image_download_url",
                "flatness_heatmap_url": "flatness_heatmap_url", "installation_image_url": "tilted_result_url",
            },
            "inputBindingSources": {
                "comprehensive": "variable", "installation": "variable",
                "point_cloud_url": "variable", "projection_image_url": "variable",
                "flatness_heatmap_url": "variable", "installation_image_url": "variable",
            },
            "outputBindings": {"result": "inspection_result"},
            "outputBindingSources": {"result": "variable"}, "params": {}, "paramSources": {},
        },
    })
    nodes.append({
        "id": end_id, "type": "end-node", "x": 1040, "y": 1420,
        "text": {"value": "结束"},
        "properties": {
            "inputBindings": {"inspection_result": "inspection_result"},
            "inputBindingSources": {"inspection_result": "variable"},
            "inputBindingDisplayNames": {"inspection_result": "产品检查结果"},
        },
    })
    producer_by_variable = {}
    for item in nodes:
        for variable in (item.get("properties") or {}).get("outputBindings", {}).values():
            producer_by_variable[variable] = item["id"]
    consumers = {e.get("targetNodeId") for e in edges}
    for item in nodes:
        if item["id"] not in consumers and item["id"] not in (start_id, product_id, end_id):
            edges.append({"id": str(uuid.uuid4()), "sourceNodeId": start_id, "targetNodeId": item["id"]})
    edges += [
        {"id": str(uuid.uuid4()), "sourceNodeId": producer_by_variable["comprehensive_result"], "targetNodeId": product_id},
        {"id": str(uuid.uuid4()), "sourceNodeId": producer_by_variable["installation_result"], "targetNodeId": product_id},
        {"id": str(uuid.uuid4()), "sourceNodeId": producer_by_variable["cloud_download_url"], "targetNodeId": product_id},
        {"id": str(uuid.uuid4()), "sourceNodeId": producer_by_variable["image_download_url"], "targetNodeId": product_id},
        {"id": str(uuid.uuid4()), "sourceNodeId": producer_by_variable["flatness_heatmap_url"], "targetNodeId": product_id},
        {"id": str(uuid.uuid4()), "sourceNodeId": producer_by_variable["tilted_result_url"], "targetNodeId": product_id},
        {"id": str(uuid.uuid4()), "sourceNodeId": product_id, "targetNodeId": end_id},
    ]
    return {"nodes": nodes, "edges": edges}


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="创建并试运行完整产品 3D 检查 Demo")
    parser.add_argument("--base-url", default="", help="完整服务地址")
    parser.add_argument("--host", default="", help="服务 IP 或域名")
    parser.add_argument("--scheme", default=DEFAULT_SCHEME, choices=["http", "https"])
    parser.add_argument("--api-port", type=int, default=DEFAULT_API_PORT)
    parser.add_argument("--discovery-port", type=int, default=DEFAULT_DISCOVERY_PORT)
    parser.add_argument("--discover-timeout", type=float, default=DEFAULT_DISCOVERY_TIMEOUT)
    parser.add_argument("--username", default=os.getenv("AURORA_USERNAME", "admin"))
    parser.add_argument("--password", default=os.getenv("AURORA_PASSWORD", ""))
    parser.add_argument("--point-cloud-file", default=DEFAULT_POINT_CLOUD)
    parser.add_argument("--execution-timeout", type=int, default=300)
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    if not args.password:
        print("✗ 请通过 --password 或 AURORA_PASSWORD 提供密码。", file=sys.stderr)
        return 1
    point_cloud_file = Path(args.point_cloud_file).expanduser().resolve()
    try:
        validate_ply(point_cloud_file)
        base_url = resolve_base_url(args)
        print(f"目标服务：{base_url}")
        print(f"点云文件：{point_cloud_file} ({point_cloud_file.stat().st_size} bytes)")
        api_client.set_base_url(base_url)
        api_client.login(args.username, args.password)
        project_id = get_or_create_project()
        uploaded = api_client.upload_operator_file(
            project_id, OP_READ_POINT_CLOUD, str(point_cloud_file)
        )
        blob_name = uploaded["blobName"]
        contracts = get_server_operator_contracts()

        product_graph = build_product_graph(blob_name)
        workflow_id = save_workflow(project_id, PRODUCT_WORKFLOW, product_graph, contracts)
        api_client.confirm_operator_file(project_id, OP_READ_POINT_CLOUD, blob_name)

        product_run = execute_workflow(project_id, workflow_id, PRODUCT_WORKFLOW, args.execution_timeout)
        roots = [item for item in product_run["outputs"]
                 if isinstance(item, dict) and item.get("name") == "inspection_result"]
        if len(roots) != 1:
            raise RuntimeError(f"工作流必须且只能返回 inspection_result，实际：{product_run['outputs']!r}")
        report = {
            "projectId": project_id,
            "pointCloudBlobName": blob_name,
            "workflowId": workflow_id,
            "executionId": product_run["executionId"],
            "inspection_result": roots[0].get("value"),
        }
        print("\n" + json.dumps(report, ensure_ascii=False, indent=2))
        return 0
    except Exception as error:
        print(f"\n✗ Demo 执行失败：{error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
