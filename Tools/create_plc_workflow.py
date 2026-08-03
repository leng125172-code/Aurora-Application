#!/usr/bin/env python
"""独立创建 PLC 读取工作流，不参与全算子 Demo。"""

import argparse
import json
import os
import sys
import uuid

TOOLS_DIR = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(TOOLS_DIR, "operator_demo"))
import api_client

OP_PLC_READ = "c7297012-ef3e-45c4-ae7b-2b8a2d454101"
OP_PLC_BATCH_READ = "c7297012-ef3e-45c4-ae7b-2b8a2d454103"
DEFAULT_PROJECT_CODE = "PRJ-ALL-OPERATORS-001"
# KEYENCE OPC UA 自定义结构体点位。它们会返回 ExtensionObject/Base64，
# 不适合作为普通数值读取演示的默认输入；显式传 --tag-code 时仍允许使用。
STRUCTURED_TAG_CODES = {"MESData"}


def uid():
    return uuid.uuid4().hex


def properties(params=None, outputs=None, inputs=None):
    params = params or {}
    outputs = outputs or {}
    inputs = inputs or {}
    return {
        "params": params,
        "paramSources": {key: "literal" for key in params},
        "inputBindings": inputs,
        "inputBindingSources": {key: "variable" for key in inputs},
        "outputBindings": outputs,
        "outputBindingSources": {key: "variable" for key in outputs},
    }


def node(node_id, node_type, x, y, title, props):
    if node_type == "end-node":
        props["inputBindingDisplayNames"] = dict(props["inputBindings"])
    return {
        "id": node_id,
        "type": node_type,
        "x": x,
        "y": y,
        "text": {"x": x, "y": y, "value": title},
        "properties": props,
    }


def edge(source, target):
    return {
        "id": uid(),
        "type": "polyline",
        "sourceNodeId": source,
        "targetNodeId": target,
        "sourceAnchorIndex": 2,
        "targetAnchorIndex": 0,
        "properties": {},
    }


def build_graph(device_id, tag_codes):
    start, read, batch, end = uid(), uid(), uid(), uid()
    return {
        "nodes": [
            node(start, "start-node", 500, 40, "开始", properties()),
            node(
                read,
                OP_PLC_READ,
                500,
                160,
                "PLC 单点读取",
                properties(
                    params={"plcDeviceId": device_id, "tagCode": tag_codes[0]},
                    outputs={"value": "plc_value", "quality": "plc_quality", "error": "plc_error"},
                ),
            ),
            node(
                batch,
                OP_PLC_BATCH_READ,
                500,
                300,
                "PLC 批量读取",
                properties(
                    params={"plcDeviceId": device_id, "tagCodesJson": json.dumps(tag_codes, ensure_ascii=False)},
                    outputs={"values_json": "plc_values_json"},
                ),
            ),
            node(
                end,
                "end-node",
                500,
                440,
                "结束",
                properties(inputs={
                    "value": "plc_value",
                    "quality": "plc_quality",
                    "error": "plc_error",
                    "valuesJson": "plc_values_json",
                }),
            ),
        ],
        "edges": [edge(start, read), edge(read, batch), edge(batch, end)],
    }


def select_project(project_code):
    project = api_client.find_project_by_code(project_code)
    if not project:
        raise RuntimeError(f"未找到项目：{project_code}")
    return project


def select_plc(device_id, device_name, requested_codes):
    devices = (api_client.api_get("/api/app/plc-device") or {}).get("items") or []
    if not devices:
        raise RuntimeError("服务端尚未配置有效 PLC 设备")
    if device_id:
        device = next((item for item in devices if item.get("id") == device_id), None)
    elif device_name:
        expected = device_name.strip().casefold()
        device = next(
            (item for item in devices if str(item.get("name") or "").strip().casefold() == expected),
            None,
        )
    else:
        device = devices[0]
    if not device:
        raise RuntimeError(f"未找到 PLC 设备：{device_id or device_name}")
    tags = (api_client.api_get(f"/api/app/plc-device/{device['id']}/tags") or {}).get("items") or []
    # 清理由本脚本早期版本误登记的 OPC UA Server 系统变量。
    obsolete = [tag for tag in tags if tag.get("code") in {"NamespaceArray", "ServerArray"}]
    for tag in obsolete:
        response = api_client._get_session().delete(
            f"{args_base_url()}/api/app/plc-device/{device['id']}/tag/{tag['id']}",
            timeout=30,
        )
        response.raise_for_status()
    if obsolete:
        print("✓ 已清理误登记的 OPC UA 系统点位")
        tags = [tag for tag in tags if tag not in obsolete]
    if not tags:
        tags = discover_and_register_tags(device["id"])
    available = {str(tag.get("code")): tag for tag in tags if tag.get("code")}
    codes = requested_codes or [
        code for code in available if code not in STRUCTURED_TAG_CODES
    ][:8]
    missing = [code for code in codes if code not in available]
    if missing:
        raise RuntimeError("PLC 点位不存在：" + ", ".join(missing))
    if not codes:
        raise RuntimeError("PLC 设备尚未配置点位")
    return device, codes


def discover_and_register_tags(device_id):
    """设备尚无点位时，从 OPC UA 地址空间登记可读变量节点。"""
    # a1 的业务 PLC 节点；优先从业务命名空间开始，避免遍历整个 OPC UA Server 树。
    queue = ["ns=4;i=1013"]
    visited = set()
    variables = []
    while queue and len(visited) < 200 and len(variables) < 8:
        parent = queue.pop(0)
        key = parent or "<root>"
        if key in visited:
            continue
        visited.add(key)
        result = api_client.api_post(
            f"/api/app/plc-device/{device_id}/browse",
            {"parentAddress": parent, "maxResults": 500},
        ) or {}
        for item in result.get("items") or []:
            if str(item.get("nodeClass") or "").casefold() == "variable":
                # 排除 i=225x 等 OPC UA Server 自身变量，只登记业务命名空间节点。
                if str(item.get("address") or "").startswith("ns=") and int(item.get("access") or 0) & 1:
                    variables.append(item)
            elif item.get("hasChildren") and item.get("address"):
                queue.append(item["address"])

    if not variables:
        raise RuntimeError("PLC 设备没有可登记的可读变量节点")

    created = []
    used_codes = set()
    for index, item in enumerate(variables[:8], start=1):
        base_code = str(item.get("browseName") or f"PLC_TAG_{index}").strip()
        code = "".join(char if char.isalnum() or char == "_" else "_" for char in base_code)
        code = code or f"PLC_TAG_{index}"
        if code in used_codes:
            code = f"{code}_{index}"
        used_codes.add(code)
        created.append(
            api_client.api_post(
                f"/api/app/plc-device/{device_id}/tag",
                {
                    "code": code,
                    "name": str(item.get("displayName") or base_code),
                    "address": item["address"],
                    # 浏览服务未返回具体类型时按字符串登记，适合当前 MES/测试节点。
                    "dataType": item.get("dataType") if item.get("dataType") is not None else 11,
                    "access": int(item.get("access") or 1),
                    "isEnabled": True,
                    "samplingIntervalMs": 1000,
                    "scale": 1,
                    "offset": 0,
                },
            )
        )
    print("✓ 自动登记 PLC 点位：" + ", ".join(tag["code"] for tag in created))
    return created


def args_base_url():
    return getattr(args_base_url, "value", "http://10.40.154.143:5000")


def ensure_operators_available():
    palette = api_client.api_get("/api/app/workflow-node-palette") or {}
    ids = {
        str(item.get("id") or "").lower()
        for category in palette.get("categories") or []
        for item in category.get("nodes") or []
    }
    missing = [value for value in (OP_PLC_READ, OP_PLC_BATCH_READ) if value not in ids]
    if missing:
        raise RuntimeError("服务端未部署 PLC 工作流算子：" + ", ".join(missing))


def save_workflow(project_id, name, graph):
    api_client.api_post("/api/app/workflow/graph/to-source", {"name": name, "graphData": graph})
    payload = {"projectId": project_id, "name": name, "graphData": graph}
    existing = api_client.find_workflow_by_name(project_id, name)
    if existing:
        result = api_client.api_put(f"/api/app/workflow/{existing['id']}", payload)
        action = "更新"
    else:
        result = api_client.api_post("/api/app/workflow", payload)
        action = "创建"
    print(f"✓ 工作流{action}成功：{result.get('id')}")
    return result


def parse_args():
    parser = argparse.ArgumentParser(description="独立 PLC 读取工作流创建器")
    parser.add_argument("--base-url", default="http://10.40.154.143:5000")
    parser.add_argument("--username", default=os.getenv("AURORA_USERNAME", "admin"))
    parser.add_argument("--password", default=os.getenv("AURORA_PASSWORD", ""))
    parser.add_argument("--project-code", default=DEFAULT_PROJECT_CODE)
    parser.add_argument("--plc-device-id", default="", help="留空时选择第一个 PLC")
    parser.add_argument("--plc-device-name", default="", help="按设备名称选择 PLC")
    parser.add_argument(
        "--tag-code",
        action="append",
        default=[],
        help="可重复；留空时选择普通标量点位，并排除 MESData 等结构体点位",
    )
    parser.add_argument("--workflow-name", default="PLC读取演示")
    return parser.parse_args()


def main():
    args = parse_args()
    if not args.password:
        raise SystemExit("缺少密码，请通过 --password 或 AURORA_PASSWORD 提供")
    api_client.set_base_url(args.base_url.rstrip("/"))
    args_base_url.value = args.base_url.rstrip("/")
    api_client.login(args.username, args.password)
    project = select_project(args.project_code)
    device, codes = select_plc(args.plc_device_id, args.plc_device_name, args.tag_code)
    ensure_operators_available()
    result = save_workflow(project["id"], args.workflow_name, build_graph(device["id"], codes))
    print(f"项目：{project.get('name')} ({project['id']})")
    print(f"PLC：{device.get('name')} ({device['id']})")
    print("点位：" + ", ".join(codes))
    print(f"工作流：{args.workflow_name} ({result.get('id')})")


if __name__ == "__main__":
    main()
