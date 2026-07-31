#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
测试脚本，对比 create_3d_compare_workflow.py 和我们的脚本在数据结构
"""
import json
import sys
import os

# 首先看看原始脚本所在的目录
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

# 复制一份简化后的关键函数
def new_uuid():
    import uuid
    return uuid.uuid4().hex


def node(node_id, node_type, x, y, title, properties):
    return {
        "id": node_id,
        "type": node_type,
        "x": x,
        "y": y,
        "text": {"x": x, "y": y, "value": title},
        "properties": properties,
    }


def make_properties(
    *,
    params=None,
    param_sources=None,
    input_bindings=None,
    input_sources=None,
    output_bindings=None,
    output_sources=None,
):
    return {
        "params": params or {},
        "paramSources": param_sources or {},
        "inputBindings": input_bindings or {},
        "inputBindingSources": input_sources or {},
        "outputBindings": output_bindings or {},
        "outputBindingSources": output_sources or {},
    }


def edge(source_id, target_id):
    return {
        "id": new_uuid(),
        "type": "polyline",
        "sourceNodeId": source_id,
        "targetNodeId": target_id,
        "sourceAnchorIndex": 2,
        "targetAnchorIndex": 0,
        "properties": {},
    }


# 构建一个简单的 graph，类似 2D 图像
def build_test_graph():
    id_start = new_uuid()
    id_read = new_uuid()
    id_end = new_uuid()

    nodes = [
        node(id_start, "start-node", 560, 40, "开始", make_properties()),
        node(
            id_read,
            "fake-op",  # 随便一个不存在的，先不管
            560,
            120,
            "读取图像",
            make_properties(
                input_bindings={"image_path": "/test/path.jpg"},
                input_sources={"image_path": "literal"},
                output_bindings={"output_image": "raw_image"},
                output_sources={"output_image": "variable"},
            ),
        ),
        node(
            id_end,
            "end-node",
            560,
            220,
            "结束",
            make_properties(),
        ),
    ]

    edges = [edge(id_start, id_read), edge(id_read, id_end)]
    return {"nodes": nodes, "edges": edges}


# 打印出来看看
print("Test graph data:")
graph = build_test_graph()
print(json.dumps(graph, ensure_ascii=False, indent=2))
print("="*80)

# 现在让我们看看我们的 2D 图像工作流的构建函数
sys.path.insert(0, os.path.join(os.path.dirname(__file__), "operator_demo"))
from operator_demo.workflow_builders.image_processing import build_2d_image_processing_graph

print("\nOur 2D image graph:")
our_graph = build_2d_image_processing_graph("/test/path.jpg")
print(json.dumps(our_graph, ensure_ascii=False, indent=2))

print("\nDone!")
