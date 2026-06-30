"""
高度差检测工作流自动创建脚本。

功能：
  1. 在指定服务上创建检测项目
  2. 为该项目创建标准高度差检测工作流

工作流流程：
  读取点云 → RANSAC 平面拟合（参考平面） → 裁剪区域A → 裁剪区域B
                                                    ↓              ↓
                                              高度统计A       高度统计B
  （通过比较区域A与区域B的平均高度，得出高度差）

用法：
  python create_height_diff_workflow.py

配置：修改下面的 BASE_URL 即可切换目标服务。
"""

import json
import uuid
import sys
import requests

# ============================================================
# 配置区
# ============================================================
BASE_URL = "http://10.180.199.143:5000"
USERNAME = "admin"
PASSWORD = "1q2w3E*"

# 项目信息
PROJECT = {
    "projectCode": "PRJ-HEIGHT-DIFF-001",
    "name": "高度差检测项目",
    "version": "1.0.0",
    "description": "标准高度差检测流程——通过 RANSAC 拟合参考平面，裁剪两个 ROI 区域，分别统计高度并比较",
}

# 工作流名称
WORKFLOW_NAME = "标准高度差检测"

# 点云文件路径（部署时按实际路径修改）
POINT_CLOUD_PATH = "/data/pointclouds/sample.ply"

# 区域A 裁剪参数（包围盒，单位：毫米）
REGION_A = {"minX": -50, "maxX": 0, "minY": -50, "maxY": 50, "minZ": -100, "maxZ": 100}

# 区域B 裁剪参数（包围盒，单位：毫米）
REGION_B = {"minX": 0, "maxX": 50, "minY": -50, "maxY": 50, "minZ": -100, "maxZ": 100}

# RANSAC 平面拟合参数
RANSAC_PARAMS = {"distanceThreshold": 0.01, "maxIterations": 1000, "probability": 0.99}

# 全局 token 和 session
_access_token = None
_session = None


# ============================================================
# 算子 GUID（与后端 [Guid] 特性一致）
# ============================================================
OP_READ_POINT_CLOUD = "a1b2c3d4-e5f6-7890-abcd-ef1234567890"
OP_RANSAC_PLANE_FIT = "d3f12345-6789-0123-4567-89012345670c"
OP_POINT_CLOUD_CROP = "b1c2d3e4-0001-4000-8000-000000000101"
OP_Z_CHANNEL_STATS = "b1c2d3e4-0002-4000-8000-000000000102"


def new_uuid() -> str:
    """生成无连字符的小写 UUID。"""
    return uuid.uuid4().hex


def _get_session() -> requests.Session:
    """获取或创建带防伪令牌的 Session。"""
    global _session
    if _session is not None:
        return _session

    _session = requests.Session()

    # 先访问 ABP 配置端点，获取防伪令牌 Cookie
    config_url = f"{BASE_URL}/api/abp/application-configuration"
    resp = _session.get(config_url, timeout=30)
    resp.raise_for_status()

    # 从 Cookie 中提取防伪令牌
    for cookie in _session.cookies:
        if cookie.name.startswith(".AspNetCore.Antiforgery"):
            _session.headers["X-XSRF-TOKEN"] = cookie.value
            break

    # 从响应头中提取（某些部署方式）
    anti_forgery = resp.headers.get("X-XSRF-TOKEN") or resp.headers.get("Set-Cookie")
    if anti_forgery and "X-XSRF-TOKEN" not in _session.headers:
        _session.headers["X-XSRF-TOKEN"] = anti_forgery

    return _session


def login() -> None:
    """登录并获取 token。"""
    global _access_token
    print("正在进行身份认证...")
    session = _get_session()
    url = f"{BASE_URL}/api/app/account/login"
    payload = {
        "name": USERNAME,
        "password": PASSWORD,
    }
    resp = session.post(url, json=payload, timeout=30)
    resp.raise_for_status()
    result = resp.json()
    # 尝试多种可能的 token 字段名
    _access_token = (
        result.get("accessToken") or result.get("access_token") or result.get("token")
    )
    if not _access_token:
        raise RuntimeError("登录失败：未获取到 token")
    session.headers["Authorization"] = f"Bearer {_access_token}"
    print("✓ 身份认证成功")


def api_post(path: str, body: dict) -> dict:
    """发送 POST 请求并解析响应（复用已认证的 Session，自动携带防伪令牌）。"""
    global _access_token, _session
    if not _access_token:
        raise RuntimeError("未登录，无法发送请求")

    session = _get_session()
    url = f"{BASE_URL}{path}"
    resp = session.post(url, json=body, timeout=30)
    resp.raise_for_status()
    return resp.json()


def create_project() -> str:
    """创建项目并返回项目 ID。"""
    print(f"[1/2] 创建项目：{PROJECT['name']} ...")
    result = api_post("/api/app/project-info", PROJECT)
    project_id = result["id"]
    print(f"      ✓ 项目创建成功，ID：{project_id}")
    return project_id


def build_graph_data() -> dict:
    """
    构建高度差检测工作流的 graphData。

    节点布局（从上到下）：
      - 开始节点
      - 读取点云
      - RANSAC 平面拟合
      - 裁剪区域A    裁剪区域B
      - 高度统计A    高度统计B
      - 结束节点
    """
    # 预生成所有节点 ID
    id_start = new_uuid()
    id_read = new_uuid()
    id_ransac = new_uuid()
    id_crop_a = new_uuid()
    id_crop_b = new_uuid()
    id_stats_a = new_uuid()
    id_stats_b = new_uuid()
    id_end = new_uuid()

    nodes = [
        # ── 开始节点 ──
        {
            "id": id_start,
            "type": "start-node",
            "x": 400,
            "y": 50,
            "text": {"x": 400, "y": 50, "value": "开始"},
            "properties": {"params": {}, "inputBindings": {}, "outputBindings": {}},
        },
        # ── 读取点云 ──
        {
            "id": id_read,
            "type": OP_READ_POINT_CLOUD,
            "x": 400,
            "y": 150,
            "text": {"x": 400, "y": 150, "value": "读取点云"},
            "properties": {
                "params": {},
                "inputBindings": {
                    "point_cloud_path": json.dumps(POINT_CLOUD_PATH),
                },
                "outputBindings": {
                    "output_point_cloud": "cloud",
                },
            },
        },
        # ── RANSAC 平面拟合 ──
        {
            "id": id_ransac,
            "type": OP_RANSAC_PLANE_FIT,
            "x": 400,
            "y": 280,
            "text": {"x": 400, "y": 280, "value": "RANSAC 平面拟合"},
            "properties": {
                "params": {
                    "distanceThreshold": RANSAC_PARAMS["distanceThreshold"],
                    "maxIterations": RANSAC_PARAMS["maxIterations"],
                    "probability": RANSAC_PARAMS["probability"],
                },
                "inputBindings": {
                    "input_point_cloud": "cloud",
                },
                "outputBindings": {
                    "plane_params": "ref_plane",
                    "inlier_points": "inlier_cloud",
                },
            },
        },
        # ── 裁剪区域A ──
        {
            "id": id_crop_a,
            "type": OP_POINT_CLOUD_CROP,
            "x": 200,
            "y": 420,
            "text": {"x": 200, "y": 420, "value": "裁剪区域A（左半）"},
            "properties": {
                "params": {
                    "cropMode": "box",
                    "minX": REGION_A["minX"],
                    "maxX": REGION_A["maxX"],
                    "minY": REGION_A["minY"],
                    "maxY": REGION_A["maxY"],
                    "minZ": REGION_A["minZ"],
                    "maxZ": REGION_A["maxZ"],
                },
                "inputBindings": {
                    "input_point_cloud": "inlier_cloud",
                },
                "outputBindings": {
                    "output_point_cloud": "region_a",
                },
            },
        },
        # ── 裁剪区域B ──
        {
            "id": id_crop_b,
            "type": OP_POINT_CLOUD_CROP,
            "x": 600,
            "y": 420,
            "text": {"x": 600, "y": 420, "value": "裁剪区域B（右半）"},
            "properties": {
                "params": {
                    "cropMode": "box",
                    "minX": REGION_B["minX"],
                    "maxX": REGION_B["maxX"],
                    "minY": REGION_B["minY"],
                    "maxY": REGION_B["maxY"],
                    "minZ": REGION_B["minZ"],
                    "maxZ": REGION_B["maxZ"],
                },
                "inputBindings": {
                    "input_point_cloud": "inlier_cloud",
                },
                "outputBindings": {
                    "output_point_cloud": "region_b",
                },
            },
        },
        # ── 高度统计A ──
        {
            "id": id_stats_a,
            "type": OP_Z_CHANNEL_STATS,
            "x": 200,
            "y": 560,
            "text": {"x": 200, "y": 560, "value": "高度统计A"},
            "properties": {
                "params": {},
                "inputBindings": {
                    "input_point_cloud": "region_a",
                    "plane_params": "ref_plane",
                },
                "outputBindings": {
                    "avg_height": "avg_h_a",
                    "max_height": "max_h_a",
                    "min_height": "min_h_a",
                    "std_height": "std_h_a",
                },
            },
        },
        # ── 高度统计B ──
        {
            "id": id_stats_b,
            "type": OP_Z_CHANNEL_STATS,
            "x": 600,
            "y": 560,
            "text": {"x": 600, "y": 560, "value": "高度统计B"},
            "properties": {
                "params": {},
                "inputBindings": {
                    "input_point_cloud": "region_b",
                    "plane_params": "ref_plane",
                },
                "outputBindings": {
                    "avg_height": "avg_h_b",
                    "max_height": "max_h_b",
                    "min_height": "min_h_b",
                    "std_height": "std_h_b",
                },
            },
        },
        # ── 结束节点 ──
        {
            "id": id_end,
            "type": "end-node",
            "x": 400,
            "y": 700,
            "text": {"x": 400, "y": 700, "value": "结束"},
            "properties": {"params": {}, "inputBindings": {}, "outputBindings": {}},
        },
    ]

    edges = [
        # 开始 → 读取点云
        {
            "id": new_uuid(),
            "type": "polyline",
            "sourceNodeId": id_start,
            "targetNodeId": id_read,
            "sourceAnchorIndex": 2,
            "targetAnchorIndex": 0,
            "properties": {},
        },
        # 读取点云 → RANSAC
        {
            "id": new_uuid(),
            "type": "polyline",
            "sourceNodeId": id_read,
            "targetNodeId": id_ransac,
            "sourceAnchorIndex": 2,
            "targetAnchorIndex": 0,
            "properties": {},
        },
        # RANSAC → 裁剪A
        {
            "id": new_uuid(),
            "type": "polyline",
            "sourceNodeId": id_ransac,
            "targetNodeId": id_crop_a,
            "sourceAnchorIndex": 2,
            "targetAnchorIndex": 0,
            "properties": {},
        },
        # RANSAC → 裁剪B
        {
            "id": new_uuid(),
            "type": "polyline",
            "sourceNodeId": id_ransac,
            "targetNodeId": id_crop_b,
            "sourceAnchorIndex": 2,
            "targetAnchorIndex": 0,
            "properties": {},
        },
        # 裁剪A → 高度统计A
        {
            "id": new_uuid(),
            "type": "polyline",
            "sourceNodeId": id_crop_a,
            "targetNodeId": id_stats_a,
            "sourceAnchorIndex": 2,
            "targetAnchorIndex": 0,
            "properties": {},
        },
        # 裁剪B → 高度统计B
        {
            "id": new_uuid(),
            "type": "polyline",
            "sourceNodeId": id_crop_b,
            "targetNodeId": id_stats_b,
            "sourceAnchorIndex": 2,
            "targetAnchorIndex": 0,
            "properties": {},
        },
        # 高度统计A → 结束
        {
            "id": new_uuid(),
            "type": "polyline",
            "sourceNodeId": id_stats_a,
            "targetNodeId": id_end,
            "sourceAnchorIndex": 2,
            "targetAnchorIndex": 0,
            "properties": {},
        },
        # 高度统计B → 结束
        {
            "id": new_uuid(),
            "type": "polyline",
            "sourceNodeId": id_stats_b,
            "targetNodeId": id_end,
            "sourceAnchorIndex": 2,
            "targetAnchorIndex": 0,
            "properties": {},
        },
    ]

    return {"nodes": nodes, "edges": edges}


def create_workflow(project_id: str) -> dict:
    """为指定项目创建高度差检测工作流。"""
    print(f"[2/2] 创建工作流：{WORKFLOW_NAME} ...")

    graph_data = build_graph_data()
    payload = {
        "projectId": project_id,
        "name": WORKFLOW_NAME,
        "graphData": graph_data,
    }

    result = api_post("/api/app/workflow", payload)
    workflow_id = result["id"]
    print(f"      ✓ 工作流创建成功，ID：{workflow_id}")
    return result


def main():
    print("=" * 60)
    print("  高度差检测工作流自动创建")
    print(f"  目标服务：{BASE_URL}")
    print("=" * 60)
    print()

    try:
        login()
        print()
        project_id = create_project()
        workflow = create_workflow(project_id)
        print()
        print("=" * 60)
        print("  创建完成！")
        print(f"  项目 ID：{project_id}")
        print(f"  工作流 ID：{workflow['id']}")
        print("=" * 60)
    except requests.exceptions.ConnectionError:
        print(f"\n✗ 无法连接到 {BASE_URL}，请确认服务已启动。", file=sys.stderr)
        sys.exit(1)
    except requests.exceptions.HTTPError as e:
        print(f"\n✗ HTTP 请求失败：{e}", file=sys.stderr)
        if e.response is not None:
            try:
                print(f"  响应内容：{e.response.text}", file=sys.stderr)
            except Exception:
                pass
        sys.exit(1)
    except Exception as e:
        print(f"\n✗ 执行失败：{e}", file=sys.stderr)
        sys.exit(1)


if __name__ == "__main__":
    main()
