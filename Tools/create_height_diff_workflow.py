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
    python create_height_diff_workflow.py --host 10.127.135.143 --api-port 5000
    python create_height_diff_workflow.py --discover-timeout 2.0

配置优先级：
    1) --base-url / --host
    2) 环境变量 AURORA_BASE_URL / AURORA_HOST
    3) UDP 自动发现（参考 AppUpdateClient.py）
"""

import argparse
import json
import os
import socket
import uuid
import sys
import requests

# ============================================================
# 配置区
# ============================================================
DEFAULT_SCHEME = "http"
DEFAULT_API_PORT = 5000
DEFAULT_HOST = "10.127.135.143"
DEFAULT_DISCOVERY_PORT = 9210
DEFAULT_DISCOVERY_TIMEOUT = 1.5
BASE_URL = ""
USERNAME = ""
PASSWORD = ""

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


def _get_local_ips() -> list[str]:
    """获取本机 IPv4 地址（排除 127.*）。"""
    ips: list[str] = []
    try:
        for info in socket.getaddrinfo(socket.gethostname(), None, socket.AF_INET):
            ip = info[4][0]
            if isinstance(ip, str) and not ip.startswith("127."):
                ips.append(ip)
    except Exception:
        pass
    return sorted(set(ips))


def discover_server_hosts(
    discovery_port: int = DEFAULT_DISCOVERY_PORT,
    timeout: float = DEFAULT_DISCOVERY_TIMEOUT,
) -> list[str]:
    """通过 UDP 广播发现局域网中运行 AppUpdateServer 的主机地址。"""
    results: list[str] = []
    seen: set[str] = set()

    broadcast_addrs: set[str] = {"255.255.255.255"}
    for ip in _get_local_ips():
        parts = ip.rsplit(".", 1)
        if len(parts) == 2:
            broadcast_addrs.add(f"{parts[0]}.255")

    req = json.dumps({"action": "discover"}).encode("utf-8")
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    sock.setsockopt(socket.SOL_SOCKET, socket.SO_BROADCAST, 1)
    sock.settimeout(0.2)
    sock.bind(("", 0))

    try:
        for addr in broadcast_addrs:
            try:
                sock.sendto(req, (addr, discovery_port))
            except Exception:
                pass

        import time

        end_at = time.monotonic() + timeout
        while time.monotonic() < end_at:
            try:
                data, (host, _) = sock.recvfrom(4096)
                if host in seen:
                    continue
                seen.add(host)
                resp = json.loads(data.decode("utf-8"))
                if resp.get("status") == "ok":
                    results.append(host)
            except socket.timeout:
                pass
            except Exception:
                pass
    finally:
        sock.close()

    return results


def resolve_base_url(args: argparse.Namespace) -> str:
    """解析目标服务地址。"""
    if args.base_url:
        return args.base_url.rstrip("/")

    env_base_url = os.getenv("AURORA_BASE_URL", "").strip()
    if env_base_url:
        return env_base_url.rstrip("/")

    host = (args.host or os.getenv("AURORA_HOST", "")).strip()
    if host:
        return f"{args.scheme}://{host}:{args.api_port}"

    discovered_hosts = discover_server_hosts(
        discovery_port=args.discovery_port,
        timeout=getattr(
            args,
            "discover_timeout",
            getattr(args, "discovery_timeout", DEFAULT_DISCOVERY_TIMEOUT),
        ),
    )
    if discovered_hosts:
        return f"{args.scheme}://{discovered_hosts[0]}:{args.api_port}"

    return f"{args.scheme}://{DEFAULT_HOST}:{args.api_port}"


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="高度差检测工作流自动创建")
    parser.add_argument(
        "--base-url", default="", help="完整服务地址，如 http://10.0.0.1:5000"
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
    parser.add_argument("--workflow-name", default=WORKFLOW_NAME)
    parser.add_argument("--project-name", default=PROJECT["name"])
    parser.add_argument("--project-code", default=PROJECT["projectCode"])
    return parser.parse_args()


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
            if cookie.value:
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


def api_get(path: str, params: dict | None = None) -> dict:
    """发送 GET 请求并解析响应。"""
    global _access_token, _session
    if not _access_token:
        raise RuntimeError("未登录，无法发送请求")

    session = _get_session()
    url = f"{BASE_URL}{path}"
    resp = session.get(url, params=params, timeout=30)
    resp.raise_for_status()
    return resp.json()


def api_put(path: str, body: dict) -> dict:
    """发送 PUT 请求并解析响应。"""
    global _access_token, _session
    if not _access_token:
        raise RuntimeError("未登录，无法发送请求")

    session = _get_session()
    url = f"{BASE_URL}{path}"
    resp = session.put(url, json=body, timeout=30)
    resp.raise_for_status()
    return resp.json()


def find_project_by_code(project_code: str) -> dict | None:
    """按项目编号查询已存在项目；命中则返回项目对象。"""

    def _norm(value: str | None) -> str:
        return (value or "").strip().casefold()

    result = api_get(
        "/api/app/project-info",
        {
            "filter": project_code,
            "skipCount": 0,
            "maxResultCount": 200,
        },
    )

    items = result.get("items") or []
    target_code = _norm(project_code)

    for item in items:
        item_code = (
            item.get("projectCode") or item.get("project_code") or item.get("code")
        )
        if _norm(item_code) == target_code:
            return item

    # 后端 filter 已经按 projectCode 过滤，若只返回一条则直接复用，避免大小写/空格差异导致漏匹配。
    if len(items) == 1:
        return items[0]

    return None


def try_find_project_by_code(project_code: str) -> dict | None:
    """安全查询项目：查询失败时返回 None，不中断主流程。"""
    try:
        return find_project_by_code(project_code)
    except requests.exceptions.RequestException as ex:
        print(f"      ! 查询已有项目失败，将继续创建：{ex}")
        return None


def build_fallback_project_code(base_code: str) -> str:
    """生成一个新的项目编号，用于唯一约束冲突时重试。"""
    suffix = uuid.uuid4().hex[:6].upper()
    return f"{base_code}-{suffix}"


def create_project() -> str:
    """创建项目并返回项目 ID。"""
    print(f"[1/2] 创建项目：{PROJECT['name']} ...")

    try:
        result = api_post("/api/app/project-info", PROJECT)
        project_id = result["id"]
        print(f"      ✓ 项目创建成功，ID：{project_id}")
        return project_id
    except requests.exceptions.HTTPError as post_err:
        # 兜底：部分服务端会把唯一键冲突包装成 500，这里再查一次并复用。
        existed = try_find_project_by_code(PROJECT["projectCode"])
        if existed is not None:
            project_id = existed["id"]
            print(f"      ✓ 创建失败后命中已有项目，自动复用 ID：{project_id}")
            return project_id

        # 列表接口没有返回可复用项目时，说明可能是软删除/历史残留占用了唯一索引。
        # 为了让脚本继续完成工作流创建，这里自动换一个 projectCode 重试一次。
        fallback_project = dict(PROJECT)
        fallback_project_code = build_fallback_project_code(PROJECT["projectCode"])
        fallback_project["projectCode"] = fallback_project_code
        print(f"      ! 未找到可复用项目，改用新项目编号重试：{fallback_project_code}")

        try:
            result = api_post("/api/app/project-info", fallback_project)
            project_id = result["id"]
            PROJECT["projectCode"] = fallback_project_code
            print(f"      ✓ 项目创建成功，ID：{project_id}")
            return project_id
        except requests.exceptions.HTTPError:
            pass

        # 保持抛出原始 POST 异常，避免被回退查询错误覆盖。
        raise post_err


def find_workflow_by_name(project_id: str, workflow_name: str) -> dict | None:
    """按项目 ID 与工作流名称查询已存在工作流；命中则返回工作流对象。"""

    def _norm(value: str | None) -> str:
        return (value or "").strip().casefold()

    result = api_get(
        "/api/app/workflow",
        {
            "projectId": project_id,
        },
    )

    if isinstance(result, list):
        items = result
    else:
        items = result.get("items") or []

    target_name = _norm(workflow_name)
    for item in items:
        item_name = item.get("name")
        if _norm(item_name) == target_name:
            return item

    return None


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
            "properties": {
                "params": {},
                "paramSources": {},
                "inputBindings": {},
                "inputBindingSources": {},
                "outputBindings": {},
                "outputBindingSources": {},
            },
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
                "paramSources": {},
                "inputBindings": {
                    "point_cloud_path": POINT_CLOUD_PATH,
                },
                "inputBindingSources": {
                    "point_cloud_path": "literal",
                },
                "outputBindings": {
                    "output_point_cloud": "cloud",
                },
                "outputBindingSources": {
                    "output_point_cloud": "variable",
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
                "paramSources": {
                    "distanceThreshold": "literal",
                    "maxIterations": "literal",
                    "probability": "literal",
                },
                "inputBindings": {
                    "input_point_cloud": "cloud",
                },
                "inputBindingSources": {
                    "input_point_cloud": "variable",
                },
                "outputBindings": {
                    "plane_params": "ref_plane",
                    "inlier_points": "inlier_cloud",
                },
                "outputBindingSources": {
                    "plane_params": "variable",
                    "inlier_points": "variable",
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
                "paramSources": {
                    "cropMode": "literal",
                    "minX": "literal",
                    "maxX": "literal",
                    "minY": "literal",
                    "maxY": "literal",
                    "minZ": "literal",
                    "maxZ": "literal",
                },
                "inputBindings": {
                    "input_point_cloud": "inlier_cloud",
                },
                "inputBindingSources": {
                    "input_point_cloud": "variable",
                },
                "outputBindings": {
                    "output_point_cloud": "region_a",
                },
                "outputBindingSources": {
                    "output_point_cloud": "variable",
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
                "paramSources": {
                    "cropMode": "literal",
                    "minX": "literal",
                    "maxX": "literal",
                    "minY": "literal",
                    "maxY": "literal",
                    "minZ": "literal",
                    "maxZ": "literal",
                },
                "inputBindings": {
                    "input_point_cloud": "inlier_cloud",
                },
                "inputBindingSources": {
                    "input_point_cloud": "variable",
                },
                "outputBindings": {
                    "output_point_cloud": "region_b",
                },
                "outputBindingSources": {
                    "output_point_cloud": "variable",
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
                "paramSources": {},
                "inputBindings": {
                    "input_point_cloud": "region_a",
                    "plane_params": "ref_plane",
                },
                "inputBindingSources": {
                    "input_point_cloud": "variable",
                    "plane_params": "variable",
                },
                "outputBindings": {
                    "avg_height": "avg_h_a",
                    "max_height": "max_h_a",
                    "min_height": "min_h_a",
                    "std_height": "std_h_a",
                },
                "outputBindingSources": {
                    "avg_height": "variable",
                    "max_height": "variable",
                    "min_height": "variable",
                    "std_height": "variable",
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
                "paramSources": {},
                "inputBindings": {
                    "input_point_cloud": "region_b",
                    "plane_params": "ref_plane",
                },
                "inputBindingSources": {
                    "input_point_cloud": "variable",
                    "plane_params": "variable",
                },
                "outputBindings": {
                    "avg_height": "avg_h_b",
                    "max_height": "max_h_b",
                    "min_height": "min_h_b",
                    "std_height": "std_h_b",
                },
                "outputBindingSources": {
                    "avg_height": "variable",
                    "max_height": "variable",
                    "min_height": "variable",
                    "std_height": "variable",
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
            "properties": {
                "params": {},
                "paramSources": {},
                "inputBindings": {},
                "inputBindingSources": {},
                "outputBindings": {},
                "outputBindingSources": {},
            },
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
    """为指定项目创建或更新高度差检测工作流。"""
    print(f"[2/2] 创建工作流：{WORKFLOW_NAME} ...")

    graph_data = build_graph_data()
    payload = {
        "projectId": project_id,
        "name": WORKFLOW_NAME,
        "graphData": graph_data,
    }

    print("      生成的请求 JSON：")
    print(json.dumps(payload, ensure_ascii=False, indent=2))

    existed = find_workflow_by_name(project_id, WORKFLOW_NAME)
    if existed is not None:
        workflow_id = existed.get("id")
        if not workflow_id:
            raise RuntimeError("命中已存在工作流但缺少 id，无法更新")
        result = api_put(f"/api/app/workflow/{workflow_id}", payload)
        print(f"      ✓ 命中已有工作流，已更新，ID：{workflow_id}")
        return result

    result = api_post("/api/app/workflow", payload)
    workflow_id = result.get("id")
    print(f"      ✓ 工作流创建成功，ID：{workflow_id}")
    return result


def main():
    global BASE_URL, USERNAME, PASSWORD, POINT_CLOUD_PATH, WORKFLOW_NAME

    args = parse_args()
    BASE_URL = resolve_base_url(args)
    USERNAME = args.username
    PASSWORD = args.password
    POINT_CLOUD_PATH = args.point_cloud_path
    WORKFLOW_NAME = args.workflow_name
    PROJECT["name"] = args.project_name
    PROJECT["projectCode"] = args.project_code

    if not PASSWORD:
        print(
            "✗ 缺少密码，请通过 --password 或 AURORA_PASSWORD 提供。", file=sys.stderr
        )
        sys.exit(2)

    print("=" * 60)
    print("  高度差检测工作流自动创建")
    print(f"  目标服务：{BASE_URL}")
    print(f"  登录用户：{USERNAME}")
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
