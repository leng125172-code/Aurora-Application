#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
简单的语法测试脚本，不依赖第三方库
"""

import py_compile
import sys
import os

script_dir = os.path.dirname(os.path.abspath(__file__))
operator_demo_dir = os.path.join(script_dir, "operator_demo")

print("Testing syntax of all files...")

# Test all Python files
files_to_test = [
    os.path.join(operator_demo_dir, "constants.py"),
    os.path.join(operator_demo_dir, "utils.py"),
    os.path.join(operator_demo_dir, "api_client.py"),
    os.path.join(operator_demo_dir, "main.py"),
    os.path.join(operator_demo_dir, "workflow_builders", "image_processing.py"),
    os.path.join(operator_demo_dir, "workflow_builders", "point_cloud_processing.py"),
    os.path.join(operator_demo_dir, "workflow_builders", "registration.py"),
    os.path.join(operator_demo_dir, "workflow_builders", "height_diff.py"),
    os.path.join(operator_demo_dir, "workflow_builders", "installation_angle.py"),
]

all_passed = True

for file_path in files_to_test:
    if not os.path.exists(file_path):
        print("[FAIL] File not found:", file_path)
        all_passed = False
        continue

    try:
        py_compile.compile(file_path, doraise=True)
        print("[OK] Syntax OK:", os.path.basename(file_path))
    except py_compile.PyCompileError as e:
        print("[FAIL] Syntax error in", os.path.basename(file_path), ":", e)
        all_passed = False
    except Exception as e:
        print("[FAIL] Error testing", os.path.basename(file_path), ":", e)
        all_passed = False

if all_passed:
    # 新增工作流再做一次无需 API、无需第三方包的变量绑定完整性校验。
    sys.path.insert(0, operator_demo_dir)
    try:
        from workflow_builders.installation_angle import (
            build_installation_angle_graph,
        )

        graph = build_installation_angle_graph("/data/pointclouds/sample.ply")
        nodes = graph.get("nodes", [])
        node_ids = [item.get("id") for item in nodes]
        if len(node_ids) != len(set(node_ids)):
            raise ValueError("安装角度工作流包含重复节点 ID")

        produced = set()
        for item in nodes:
            properties = item.get("properties") or {}
            produced.update((properties.get("outputBindings") or {}).values())

        missing = []
        for item in nodes:
            if item.get("type") in ("start-node", "end-node"):
                continue
            properties = item.get("properties") or {}
            bindings = properties.get("inputBindings") or {}
            sources = properties.get("inputBindingSources") or {}
            for port, variable in bindings.items():
                if sources.get(port) == "variable" and variable not in produced:
                    missing.append(
                        f"{(item.get('text') or {}).get('value')}:{port}={variable}"
                    )
        if missing:
            raise ValueError("安装角度工作流存在未生成的输入变量：" + ", ".join(missing))

        end_nodes = [item for item in nodes if item.get("type") == "end-node"]
        if len(end_nodes) != 1:
            raise ValueError("安装角度工作流必须包含唯一结束节点")
        end_bindings = (end_nodes[0].get("properties") or {}).get(
            "inputBindings"
        ) or {}
        if not end_bindings:
            raise ValueError("安装角度工作流未配置最终输出")
        missing_outputs = [
            variable for variable in end_bindings.values() if variable not in produced
        ]
        if missing_outputs:
            raise ValueError(
                "安装角度工作流最终输出变量不存在：" + ", ".join(missing_outputs)
            )
        print(
            "[OK] Installation graph:",
            len(nodes),
            "nodes,",
            len(graph.get("edges", [])),
            "edges,",
            len(end_bindings),
            "outputs",
        )
    except Exception as e:
        print("[FAIL] Installation graph validation:", e)
        all_passed = False

if all_passed:
    print("\nAll files passed syntax and graph validation!")
    sys.exit(0)
else:
    print("\nSome files failed syntax validation!")
    sys.exit(1)
