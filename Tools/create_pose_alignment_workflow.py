"""Create or update the dedicated one-time 3D pose calibration workflow.

The workflow reuses the scan-input and product-model nodes from the production
3D comparison workflow, but it has its own persistent workflow ID. It never
rewrites the production graph.
"""

from __future__ import annotations

import argparse
import copy
import json
import os
import time

import create_3d_compare_workflow as api


DEFAULT_PROJECT_ID = "3a234514-e887-bc78-8028-448a43d3d6b0"
DEFAULT_SOURCE_WORKFLOW_ID = "3a23451f-fc5a-7deb-0935-a30ad7ccf14f"
DEFAULT_WORKFLOW_NAME = "3D姿态对齐标定"
OP_PARTIAL_SURFACE_POSE_ALIGNMENT = "b1a1000a-000a-4000-8000-00000000003a"


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="创建独立的3D姿态对齐标定工作流")
    parser.add_argument("--base-url", default="http://10.24.1.143:5000")
    parser.add_argument("--project-id", default=DEFAULT_PROJECT_ID)
    parser.add_argument("--source-workflow-id", default=DEFAULT_SOURCE_WORKFLOW_ID)
    parser.add_argument("--workflow-name", default=DEFAULT_WORKFLOW_NAME)
    parser.add_argument("--username", default=os.getenv("AURORA_USERNAME", "admin"))
    parser.add_argument("--password", default=os.getenv("AURORA_PASSWORD", ""))
    parser.add_argument("--execute", action="store_true", help="保存后立即执行一次并校验输出")
    parser.add_argument("--timeout", type=int, default=900)
    parser.add_argument("--yaw-center", type=float, default=25.082030345386837)
    parser.add_argument("--yaw-range", type=float, default=5.0)
    return parser.parse_args()


def graph_object(workflow: dict) -> dict:
    graph = workflow.get("graphData") or {}
    return json.loads(graph) if isinstance(graph, str) else graph


def clone_source_node(source: dict, *, x: int, y: int) -> dict:
    result = copy.deepcopy(source)
    result["id"] = api.new_uuid()
    result["x"] = x
    result["y"] = y
    text = result.get("text") or {}
    text["x"] = x
    text["y"] = y
    result["text"] = text
    return result


def find_output_producer(graph: dict, variable_name: str) -> dict:
    for item in graph.get("nodes", []):
        outputs = item.get("properties", {}).get("outputBindings", {})
        if variable_name in outputs.values():
            return item
    raise RuntimeError(f"源工作流缺少变量生产节点：{variable_name}")


def build_graph(
    source_graph: dict,
    *,
    yaw_center_deg: float,
    yaw_search_range_deg: float,
) -> dict:
    scan_node = clone_source_node(
        find_output_producer(source_graph, "scanned_cloud"), x=340, y=140
    )
    model_node = clone_source_node(
        find_output_producer(source_graph, "model_cloud"), x=760, y=140
    )

    start_id = api.new_uuid()
    pose_id = api.new_uuid()
    save_id = api.new_uuid()
    end_id = api.new_uuid()

    start_node = api.node(start_id, "start-node", 550, 40, "开始", api.make_properties())
    pose_node = api.node(
        pose_id,
        OP_PARTIAL_SURFACE_POSE_ALIGNMENT,
        550,
        280,
        "合格品顶面姿态标定",
        api.make_properties(
            params={
                "sourceSampleCount": 5000,
                "targetSampleCount": 50000,
                "maxIterations": 50,
                "trimFraction": 0.90,
                "inlierDistance": 1.5,
                "minimumInlierRatio": 0.20,
                "preserveUpDirection": "true",
                "yawSearchStepDeg": 1,
                "yawCenterDeg": yaw_center_deg,
                "yawSearchRangeDeg": yaw_search_range_deg,
            },
            param_sources={
                "sourceSampleCount": "literal",
                "targetSampleCount": "literal",
                "maxIterations": "literal",
                "trimFraction": "literal",
                "inlierDistance": "literal",
                "minimumInlierRatio": "literal",
                "preserveUpDirection": "literal",
                "yawSearchStepDeg": "literal",
                "yawCenterDeg": "literal",
                "yawSearchRangeDeg": "literal",
            },
            input_bindings={
                "source_cloud": "scanned_cloud",
                "target_cloud": "model_cloud",
            },
            input_sources={"source_cloud": "variable", "target_cloud": "variable"},
            output_bindings={
                "aligned_cloud": "pose_aligned_cloud",
                "transform_matrix": "pose_transform_matrix",
                "pose_json": "pose_calibration_json",
            },
            output_sources={
                "aligned_cloud": "variable",
                "transform_matrix": "variable",
                "pose_json": "variable",
            },
        ),
    )
    save_node = api.node(
        save_id,
        api.OP_SAVE_POINT_CLOUD_BLOB,
        550,
        420,
        "姿态对齐点云存Blob",
        api.make_properties(
            params={"fileName": "pose-aligned-cloud.ply"},
            param_sources={"fileName": "literal"},
            input_bindings={"input_point_cloud": "pose_aligned_cloud"},
            input_sources={"input_point_cloud": "variable"},
            output_bindings={
                "blob_name": "pose_aligned_cloud_blob_name",
                "download_url": "pose_aligned_cloud_download_url",
            },
            output_sources={"blob_name": "variable", "download_url": "variable"},
        ),
    )
    end_properties = api.make_properties(
        input_bindings={
            "poseCalibration": "pose_calibration_json",
            "alignedPointCloudUrl": "pose_aligned_cloud_download_url",
            "modelId": "selected_model_id",
        },
        input_sources={
            "poseCalibration": "variable",
            "alignedPointCloudUrl": "variable",
            "modelId": "variable",
        },
    )
    end_properties["inputBindingDisplayNames"] = {
        "poseCalibration": "姿态标定参数",
        "alignedPointCloudUrl": "姿态对齐点云",
        "modelId": "产品模型ID",
    }
    end_node = api.node(end_id, "end-node", 550, 560, "结束", end_properties)

    return {
        "nodes": [start_node, scan_node, model_node, pose_node, save_node, end_node],
        "edges": [
            api.edge(start_id, scan_node["id"]),
            api.edge(start_id, model_node["id"]),
            api.edge(scan_node["id"], pose_id),
            api.edge(model_node["id"], pose_id),
            api.edge(pose_id, save_id),
            api.edge(save_id, end_id),
        ],
    }


def save_workflow(project_id: str, name: str, graph: dict) -> dict:
    payload = {"projectId": project_id, "name": name, "graphData": graph}
    existing = api.find_workflow_by_name(project_id, name)
    if existing:
        workflow_id = existing.get("id")
        if not workflow_id:
            raise RuntimeError("已有姿态标定工作流缺少 ID")
        result = api.api_put(f"/api/app/workflow/{workflow_id}", payload)
        print(f"✓ 已更新姿态标定工作流：{workflow_id}")
        return result
    result = api.api_post("/api/app/workflow", payload)
    print(f"✓ 已创建姿态标定工作流：{result.get('id')}")
    return result


def decode_string_output(value: object) -> object:
    if not isinstance(value, str):
        return value
    try:
        decoded = json.loads(value)
        return decoded if isinstance(decoded, str) else value
    except json.JSONDecodeError:
        return value


def execute_once(project_id: str, workflow_id: str, timeout: int) -> dict:
    response = api._get_session().post(
        f"{api.BASE_URL}/api/app/workflow/executions",
        json={
            "projectId": project_id,
            "workflowId": workflow_id,
            "mode": 0,
            "loopCount": 1,
            "inputVariableKeys": [],
            "outputVariableNames": [
                "pose_calibration_json",
                "pose_aligned_cloud_download_url",
                "selected_model_id",
            ],
        },
        timeout=timeout,
    )
    response.raise_for_status()
    trigger = response.json()
    if trigger.get("error"):
        raise RuntimeError(trigger.get("message") or trigger.get("errorCode"))
    status = trigger.get("status") or {}
    execution_id = trigger["executionId"]
    deadline = time.monotonic() + timeout
    while not status.get("isTerminal"):
        if time.monotonic() >= deadline:
            raise TimeoutError(f"姿态标定执行超过 {timeout} 秒")
        time.sleep(1)
        status = api.api_get(
            f"/api/app/workflow/executions/{execution_id}",
            {"includeVariables": "true"},
        )
    if status.get("debugState") != "completed":
        raise RuntimeError(status.get("errorMessage") or f"执行状态：{status.get('debugState')}")
    outputs = {
        item.get("name"): decode_string_output(item.get("value"))
        for item in status.get("outputs", [])
    }
    pose_value = outputs.get("pose_calibration_json")
    if isinstance(pose_value, str):
        pose_value = json.loads(pose_value)
        outputs["pose_calibration_json"] = pose_value
    print("POSE_ALIGNMENT_RESULT=" + json.dumps(outputs, ensure_ascii=False))
    return status


def main() -> None:
    args = parse_args()
    if not args.password:
        raise RuntimeError("请通过 AURORA_PASSWORD 或 --password 提供登录密码")
    api.BASE_URL = args.base_url.rstrip("/")
    api.USERNAME = args.username
    api.PASSWORD = args.password
    api.login()

    source = api.api_get(f"/api/app/workflow/{args.source_workflow_id}")
    graph = build_graph(
        graph_object(source),
        yaw_center_deg=args.yaw_center,
        yaw_search_range_deg=args.yaw_range,
    )
    result = save_workflow(args.project_id, args.workflow_name, graph)
    workflow_id = result.get("id")
    if not workflow_id:
        existing = api.find_workflow_by_name(args.project_id, args.workflow_name)
        workflow_id = (existing or {}).get("id")
    if not workflow_id:
        raise RuntimeError("保存后未获得姿态标定工作流 ID")
    print(f"POSE_ALIGNMENT_WORKFLOW_ID={workflow_id}")
    if args.execute:
        execute_once(args.project_id, workflow_id, args.timeout)


if __name__ == "__main__":
    main()
