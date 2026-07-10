"""临时探测：调用新的 ROI 底图预运行接口，验证是否已部署并能返回 blobName。

用法：
  python _probe_roi_base_image.py --password *** --project-id <projectId>
"""

import argparse
import json
import sys

import requests


def get_session(base_url: str) -> requests.Session:
    session = requests.Session()
    resp = session.get(f"{base_url}/api/abp/application-configuration", timeout=30)
    resp.raise_for_status()
    for cookie in session.cookies:
        if cookie.name.startswith(".AspNetCore.Antiforgery") and cookie.value:
            session.headers["X-XSRF-TOKEN"] = cookie.value
            break
    return session


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--base-url", default="http://10.154.214.143:5000")
    parser.add_argument("--username", default="admin")
    parser.add_argument("--password", required=True)
    parser.add_argument("--project-id", required=True)
    parser.add_argument("--workflow-name", default="固定ROI高度差检测")
    args = parser.parse_args()

    session = get_session(args.base_url)

    resp = session.post(
        f"{args.base_url}/api/app/account/login",
        json={"name": args.username, "password": args.password},
        timeout=30,
    )
    resp.raise_for_status()
    payload = resp.json()
    token = payload.get("accessToken") or payload.get("token")
    session.headers["Authorization"] = f"Bearer {token}"
    print("✓ 登录成功")

    # 列出项目下的工作流
    resp = session.get(
        f"{args.base_url}/api/app/workflow",
        params={"projectId": args.project_id},
        timeout=30,
    )
    resp.raise_for_status()
    data = resp.json()
    items = data if isinstance(data, list) else data.get("items", [])
    print(f"工作流数量：{len(items)}")

    target = next((w for w in items if w.get("name") == args.workflow_name), None)
    if target is None and items:
        target = items[0]
    if target is None:
        print("✗ 未找到工作流")
        sys.exit(1)

    workflow_id = target["id"]
    print(f"工作流 ID：{workflow_id}")

    # 拉取详情，找到 ROI 分区节点 ID
    resp = session.get(f"{args.base_url}/api/app/workflow/{workflow_id}", timeout=30)
    resp.raise_for_status()
    detail = resp.json()
    graph = detail.get("graphData")
    if isinstance(graph, str):
        graph = json.loads(graph)

    roi_node_id = None
    upload_path = None
    for n in graph.get("nodes", []):
        if (
            n.get("type") == "a1b2c3d4-0001-4000-8000-000000000001"
            and roi_node_id is None
        ):
            roi_node_id = n["id"]
        if n.get("type") == "a1b2c3d4-e5f6-7890-abcd-ef1234567890":
            binds = (n.get("properties") or {}).get("inputBindings") or {}
            upload_path = binds.get("point_cloud_path")

    print(f"ROI 节点 ID：{roi_node_id}")
    print(f"读取点云路径：{upload_path}")
    if not roi_node_id:
        print("✗ 未找到 ROI 分区节点")
        sys.exit(1)

    # 调用新接口
    body = {
        "projectId": args.project_id,
        "workflowId": workflow_id,
        "roiNodeId": roi_node_id,
    }
    print("\n调用 POST /api/app/workflow/roi-base-image ...")
    resp = session.post(
        f"{args.base_url}/api/app/workflow/roi-base-image",
        json=body,
        timeout=180,
    )
    print(f"HTTP {resp.status_code}")
    result = None
    try:
        result = resp.json()
        print(json.dumps(result, ensure_ascii=False, indent=2))
    except Exception:
        print(resp.text[:2000])

    # 验证写回：重新拉取工作流，确认 ROI 节点 roiJson.baseImage 已填 blobName + projectionMapping。
    if isinstance(result, dict) and not result.get("error"):
        resp = session.get(
            f"{args.base_url}/api/app/workflow/{workflow_id}", timeout=30
        )
        resp.raise_for_status()
        detail_after = resp.json()
        graph_after = detail_after.get("graphData")
        if isinstance(graph_after, str):
            graph_after = json.loads(graph_after)

        for n in graph_after.get("nodes", []):
            if n.get("id") == roi_node_id:
                roi_json_str = ((n.get("properties") or {}).get("params") or {}).get(
                    "roiJson"
                )
                print("\n=== 写回后 ROI 节点 roiJson（服务端持久化结果）===")
                try:
                    print(
                        json.dumps(
                            json.loads(roi_json_str), ensure_ascii=False, indent=2
                        )
                    )
                except Exception:
                    print(roi_json_str)
                break


if __name__ == "__main__":
    main()
