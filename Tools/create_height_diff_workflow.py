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
    "description": "固定 ROI 的高度差检测流程——含点云预处理、分割着色、Blob 可视化、ROI 掩膜裁剪、阈值判定与结果图导出",
}

# 工作流名称
WORKFLOW_NAME = "固定ROI高度差检测"

# 点云文件路径（部署时按实际路径修改）
POINT_CLOUD_PATH = "/data/pointclouds/sample.ply"

PROJECTION_BOUNDS = {"minX": -80.0, "maxX": 80.0, "minY": -60.0, "maxY": 60.0}
# 投影图最长边像素数（与后端 colored_point_cloud_to_image 的 imageResolution 一致）
IMAGE_RESOLUTION = 512


def compute_preview_size(bounds: dict, long_side: int) -> tuple[int, int]:
    """按后端渲染器同一规则，根据投影边界长宽比推算实际输出图像宽高。

    与 PointCloudProjectionRenderer.ComputeImageSize 保持一致：长边=long_side，
    短边按长宽比缩放。避免 ROI 底图尺寸与实际投影不一致导致校验失败。
    """
    range_x = float(bounds["maxX"]) - float(bounds["minX"])
    range_y = float(bounds["maxY"]) - float(bounds["minY"])
    if range_x <= 0:
        range_x = 1.0
    if range_y <= 0:
        range_y = 1.0
    if range_x >= range_y:
        return long_side, max(1, round(long_side * range_y / range_x))
    return max(1, round(long_side * range_x / range_y)), long_side


PREVIEW_WIDTH, PREVIEW_HEIGHT = compute_preview_size(
    PROJECTION_BOUNDS, IMAGE_RESOLUTION
)

ROIS_JSON = [
    json.dumps(
        {
            "rois": [
                {
                    "name": "BM",
                    "type": "Rect",
                    "x": 100,
                    "y": 140,
                    "width": 100,
                    "height": 80,
                    "rotation": 0,
                }
            ],
            "baseImage": {
                "selectedBlobName": "workflow-image/preview_xy.png",
                "projectionMapping": {
                    "viewLabel": "XY",
                    "worldMinX": PROJECTION_BOUNDS["minX"],
                    "worldMaxX": PROJECTION_BOUNDS["maxX"],
                    "worldMinY": PROJECTION_BOUNDS["minY"],
                    "worldMaxY": PROJECTION_BOUNDS["maxY"],
                    "imageWidth": PREVIEW_WIDTH,
                    "imageHeight": PREVIEW_HEIGHT,
                },
            },
        },
        ensure_ascii=False,
    ),
    json.dumps(
        {
            "rois": [
                {
                    "name": "A",
                    "type": "Rect",
                    "x": 300,
                    "y": 140,
                    "width": 100,
                    "height": 80,
                    "rotation": 0,
                }
            ],
            "baseImage": {
                "selectedBlobName": "workflow-image/preview_xy.png",
                "projectionMapping": {
                    "viewLabel": "XY",
                    "worldMinX": PROJECTION_BOUNDS["minX"],
                    "worldMaxX": PROJECTION_BOUNDS["maxX"],
                    "worldMinY": PROJECTION_BOUNDS["minY"],
                    "worldMaxY": PROJECTION_BOUNDS["maxY"],
                    "imageWidth": PREVIEW_WIDTH,
                    "imageHeight": PREVIEW_HEIGHT,
                },
            },
        },
        ensure_ascii=False,
    ),
    json.dumps(
        {
            "rois": [
                {
                    "name": "B",
                    "type": "Rect",
                    "x": 100,
                    "y": 300,
                    "width": 100,
                    "height": 80,
                    "rotation": 0,
                }
            ],
            "baseImage": {
                "selectedBlobName": "workflow-image/preview_xy.png",
                "projectionMapping": {
                    "viewLabel": "XY",
                    "worldMinX": PROJECTION_BOUNDS["minX"],
                    "worldMaxX": PROJECTION_BOUNDS["maxX"],
                    "worldMinY": PROJECTION_BOUNDS["minY"],
                    "worldMaxY": PROJECTION_BOUNDS["maxY"],
                    "imageWidth": PREVIEW_WIDTH,
                    "imageHeight": PREVIEW_HEIGHT,
                },
            },
        },
        ensure_ascii=False,
    ),
    json.dumps(
        {
            "rois": [
                {
                    "name": "C",
                    "type": "Rect",
                    "x": 300,
                    "y": 300,
                    "width": 100,
                    "height": 80,
                    "rotation": 0,
                }
            ],
            "baseImage": {
                "selectedBlobName": "workflow-image/preview_xy.png",
                "projectionMapping": {
                    "viewLabel": "XY",
                    "worldMinX": PROJECTION_BOUNDS["minX"],
                    "worldMaxX": PROJECTION_BOUNDS["maxX"],
                    "worldMinY": PROJECTION_BOUNDS["minY"],
                    "worldMaxY": PROJECTION_BOUNDS["maxY"],
                    "imageWidth": PREVIEW_WIDTH,
                    "imageHeight": PREVIEW_HEIGHT,
                },
            },
        },
        ensure_ascii=False,
    ),
]

# RANSAC 平面拟合参数
RANSAC_PARAMS = {
    "distanceThreshold": 0.01,
    "maxIterations": 1000,
    "probability": 0.99,
    "refinePlane": True,
    "normalConstraint": "z-up",
    "seed": 0,
}
VOXEL_SIZE = 0.2
THRESHOLD_MIN = -0.2
THRESHOLD_MAX = 0.2

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
    parser.add_argument("--voxel-size", type=float, default=VOXEL_SIZE)
    parser.add_argument(
        "--projection-min-x", type=float, default=PROJECTION_BOUNDS["minX"]
    )
    parser.add_argument(
        "--projection-max-x", type=float, default=PROJECTION_BOUNDS["maxX"]
    )
    parser.add_argument(
        "--projection-min-y", type=float, default=PROJECTION_BOUNDS["minY"]
    )
    parser.add_argument(
        "--projection-max-y", type=float, default=PROJECTION_BOUNDS["maxY"]
    )
    parser.add_argument(
        "--roi-json",
        action="append",
        default=[],
        help="ROI配置JSON，可多次指定以添加多个区域",
    )
    parser.add_argument("--threshold-min", type=float, default=THRESHOLD_MIN)
    parser.add_argument("--threshold-max", type=float, default=THRESHOLD_MAX)
    return parser.parse_args()


# ============================================================
# 算子 GUID（与后端 [Guid] 特性一致）
# ============================================================
OP_READ_POINT_CLOUD = "a1b2c3d4-e5f6-7890-abcd-ef1234567890"
OP_VOXEL_DOWNSAMPLE = "b1d01234-5678-9012-4567-89012345670a"
OP_Z_COLORIZE = "de31c1ab-9ef0-43ab-a30f-0dce0e1d8201"
OP_RANSAC_PLANE_FIT = "d3f12345-6789-0123-4567-89012345670c"
OP_STATISTICAL_OUTLIER_REMOVAL = "8a708912-3456-7890-1234-567890123407"
OP_SAVE_POINT_CLOUD_BLOB = "a1b2c3d4-e5f6-7890-abcd-ef1234567891"
OP_COLORED_CLOUD_TO_IMAGE = "1dd66555-a882-436b-bf30-dab8c680a201"
OP_ROI_PARTITION = "a1b2c3d4-0001-4000-8000-000000000001"
OP_POINT_CLOUD_CROP = "b1c2d3e4-0001-4000-8000-000000000101"
OP_Z_CHANNEL_STATS = "b1c2d3e4-0002-4000-8000-000000000102"
OP_COLORIZE_CLOUD_BY_ROI = "c8b5e2a1-3d4f-5c6d-7e8f-9a0b1c2d3e4f"
OP_HEIGHT_DIFF_EVAL = "8c3dd5d9-8234-49fb-8d1f-1d7c7e0b4201"
OP_ANNOTATE_HEIGHT_DIFF = "1d6498c5-8d95-41ab-8801-c3f8e20d7301"
OP_SAVE_IMAGE_BLOB = "4b8af0f5-c0fb-45df-97a6-5ab2462cfd01"

OP_COLORED_CLOUD_TO_TILTED_IMAGE = "7e3a2b1c-9d4e-4f5a-8b6c-1d2e3f4a5b6c"
OP_ANNOTATE_TILTED_VIEW = "9f4b2c3d-8e5a-4b6c-9d1e-2f3a4b5c6d7e"
OP_PLANE_HEIGHT_DIFF = "f1a2b3c4-d5e6-7890-abcd-ef0123456789"
OP_RENDER_PLANE_OUTLINES = "e1f2a3b4-c5d6-7890-abcd-ef0123456789"


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


def node(
    node_id: str, node_type: str, x: int, y: int, title: str, properties: dict
) -> dict:
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
    params: dict | None = None,
    param_sources: dict | None = None,
    input_bindings: dict | None = None,
    input_sources: dict | None = None,
    output_bindings: dict | None = None,
    output_sources: dict | None = None,
) -> dict:
    return {
        "params": params or {},
        "paramSources": param_sources or {},
        "inputBindings": input_bindings or {},
        "inputBindingSources": input_sources or {},
        "outputBindings": output_bindings or {},
        "outputBindingSources": output_sources or {},
    }


def edge(source_id: str, target_id: str) -> dict:
    return {
        "id": new_uuid(),
        "type": "polyline",
        "sourceNodeId": source_id,
        "targetNodeId": target_id,
        "sourceAnchorIndex": 2,
        "targetAnchorIndex": 0,
        "properties": {},
    }


COLORS = [
    {"r": 255, "g": 180, "b": 0},
    {"r": 0, "g": 220, "b": 255},
    {"r": 180, "g": 255, "b": 0},
    {"r": 255, "g": 80, "b": 200},
    {"r": 0, "g": 255, "b": 180},
    {"r": 255, "g": 255, "b": 0},
    {"r": 200, "g": 80, "b": 255},
    {"r": 80, "g": 200, "b": 255},
]


def build_graph_data() -> dict:
    """构建支持动态数量ROI的高度差检测工作流（独立平面拟合 + 平面高度差）。"""
    id_start = new_uuid()
    id_read = new_uuid()
    id_downsample = new_uuid()
    id_denoise = new_uuid()
    id_colorize = new_uuid()
    id_export_cloud = new_uuid()
    id_preview = new_uuid()
    id_annotate = new_uuid()
    id_export_image = new_uuid()
    id_tilted_view = new_uuid()
    id_render_outlines_top = new_uuid()
    id_render_outlines_tilted = new_uuid()
    id_annotate_tilted = new_uuid()
    id_export_tilted = new_uuid()
    id_end = new_uuid()

    bounds = PROJECTION_BOUNDS
    region_count = len(ROIS_JSON)

    roi_nodes = []
    crop_nodes = []
    ransac_nodes = []
    color_nodes = []
    stats_nodes = []
    plane_diff_nodes = []

    base_x = 560 - (region_count - 1) * 160
    for i in range(region_count):
        idx = i + 1
        color = COLORS[i % len(COLORS)]
        x = base_x + i * 320

        id_roi = new_uuid()
        id_crop = new_uuid()
        id_ransac = new_uuid()
        id_color = new_uuid()
        id_stats = new_uuid()

        # ROI 分区
        roi_nodes.append(
            node(
                id_roi,
                OP_ROI_PARTITION,
                x,
                620,
                f"ROI {idx}",
                make_properties(
                    params={"roiJson": ROIS_JSON[i]},
                    param_sources={"roiJson": "literal"},
                    input_bindings={
                        "input_mat": "preview_image",
                        "projection_mapping": "projection_mapping",
                    },
                    input_sources={
                        "input_mat": "variable",
                        "projection_mapping": "variable",
                    },
                    output_bindings={
                        "primary_mask": f"roi_{idx}_mask",
                        "roi_metadata": f"roi_{idx}_metadata",
                    },
                    output_sources={
                        "primary_mask": "variable",
                        "roi_metadata": "variable",
                    },
                ),
            )
        )

        # 掩膜裁剪
        crop_nodes.append(
            node(
                id_crop,
                OP_POINT_CLOUD_CROP,
                x,
                700,
                f"掩膜裁剪{idx}",
                make_properties(
                    params={
                        "cropMode": "mask",
                        "maskWorldMinX": bounds["minX"],
                        "maskWorldMaxX": bounds["maxX"],
                        "maskWorldMinY": bounds["minY"],
                        "maskWorldMaxY": bounds["maxY"],
                    },
                    param_sources={
                        "cropMode": "literal",
                        "maskWorldMinX": "literal",
                        "maskWorldMaxX": "literal",
                        "maskWorldMinY": "literal",
                        "maskWorldMaxY": "literal",
                    },
                    input_bindings={
                        "input_point_cloud": "filtered_cloud",
                        "roi_mask": f"roi_{idx}_mask",
                        "roi_metadata": f"roi_{idx}_metadata",
                    },
                    input_sources={
                        "input_point_cloud": "variable",
                        "roi_mask": "variable",
                        "roi_metadata": "variable",
                    },
                    output_bindings={"output_point_cloud": f"region_{idx}_cloud"},
                    output_sources={"output_point_cloud": "variable"},
                ),
            )
        )

        # 独立 RANSAC 平面拟合（每个区域拟合自己的平面）
        ransac_nodes.append(
            node(
                id_ransac,
                OP_RANSAC_PLANE_FIT,
                x,
                780,
                f"拟合平面{idx}",
                make_properties(
                    params=RANSAC_PARAMS,
                    param_sources={
                        "distanceThreshold": "literal",
                        "maxIterations": "literal",
                        "probability": "literal",
                        "refinePlane": "literal",
                        "normalConstraint": "literal",
                        "seed": "literal",
                    },
                    input_bindings={"input_point_cloud": f"region_{idx}_cloud"},
                    input_sources={"input_point_cloud": "variable"},
                    output_bindings={
                        "plane_params": f"plane_{idx}_params",
                        "inlier_points": f"region_{idx}_inlier_cloud",
                    },
                    output_sources={
                        "plane_params": "variable",
                        "inlier_points": "variable",
                    },
                ),
            )
        )

        # 区域着色
        color_nodes.append(
            node(
                id_color,
                OP_COLORIZE_CLOUD_BY_ROI,
                x,
                860,
                f"区域{idx}着色",
                make_properties(
                    params={
                        "colorR": color["r"],
                        "colorG": color["g"],
                        "colorB": color["b"],
                        "blendMode": "replace",
                    },
                    param_sources={
                        "colorR": "literal",
                        "colorG": "literal",
                        "colorB": "literal",
                        "blendMode": "literal",
                    },
                    input_bindings={
                        "input_point_cloud": "colored_segment_cloud",
                        "roi_mask": f"roi_{idx}_mask",
                        "roi_metadata": f"roi_{idx}_metadata",
                    },
                    input_sources={
                        "input_point_cloud": "variable",
                        "roi_mask": "variable",
                        "roi_metadata": "variable",
                    },
                    output_bindings={"output_point_cloud": "colored_segment_cloud"},
                    output_sources={"output_point_cloud": "variable"},
                ),
            )
        )

        # 高度统计（直接使用原始 Z 坐标，不传入自身平面）
        stats_nodes.append(
            node(
                id_stats,
                OP_Z_CHANNEL_STATS,
                x,
                940,
                f"高度统计{idx}",
                make_properties(
                    input_bindings={
                        "input_point_cloud": f"region_{idx}_cloud",
                    },
                    input_sources={
                        "input_point_cloud": "variable",
                    },
                    output_bindings={"centroid_z": f"height_{idx}"},
                    output_sources={"centroid_z": "variable"},
                ),
            )
        )

    # 平面高度差节点（A-BM、B-BM、C-BM）
    diff_region_names = ["A", "B", "C"]
    for j in range(1, region_count):
        id_diff = new_uuid()
        target_idx = j + 1  # 2, 3, 4
        diff_name = f"{diff_region_names[j - 1]}-BM"

        plane_diff_nodes.append(
            node(
                id_diff,
                OP_PLANE_HEIGHT_DIFF,
                560,
                980 + j * 80,
                f"平面高度差({diff_name})",
                make_properties(
                    params={
                        "minDiff": THRESHOLD_MIN,
                        "maxDiff": THRESHOLD_MAX,
                        "refRegionName": "BM",
                        "targetRegionName": diff_region_names[j - 1],
                    },
                    param_sources={
                        "minDiff": "literal",
                        "maxDiff": "literal",
                        "refRegionName": "literal",
                        "targetRegionName": "literal",
                    },
                    input_bindings={
                        "ref_plane_params": "plane_1_params",
                        "target_plane_params": f"plane_{target_idx}_params",
                        "ref_cloud": "region_1_cloud",
                        "target_cloud": f"region_{target_idx}_cloud",
                    },
                    input_sources={
                        "ref_plane_params": "variable",
                        "target_plane_params": "variable",
                        "ref_cloud": "variable",
                        "target_cloud": "variable",
                    },
                    output_bindings={
                        "result": f"inspection_result_{target_idx}",
                    },
                    output_sources={
                        "result": "variable",
                    },
                ),
            )
        )

    nodes = [
        node(id_start, "start-node", 560, 40, "开始", make_properties()),
        node(
            id_read,
            OP_READ_POINT_CLOUD,
            560,
            120,
            "读取点云",
            make_properties(
                input_bindings={"point_cloud_path": POINT_CLOUD_PATH},
                input_sources={"point_cloud_path": "literal"},
                output_bindings={"output_point_cloud": "raw_cloud"},
                output_sources={"output_point_cloud": "variable"},
            ),
        ),
        node(
            id_downsample,
            OP_VOXEL_DOWNSAMPLE,
            560,
            200,
            "体素下采样",
            make_properties(
                params={"voxelSize": VOXEL_SIZE},
                param_sources={"voxelSize": "literal"},
                input_bindings={"input_point_cloud": "raw_cloud"},
                input_sources={"input_point_cloud": "variable"},
                output_bindings={"output_point_cloud": "downsampled_cloud"},
                output_sources={"output_point_cloud": "variable"},
            ),
        ),
        node(
            id_denoise,
            OP_STATISTICAL_OUTLIER_REMOVAL,
            560,
            280,
            "统计滤波去噪",
            make_properties(
                params={"k": 12, "stddevMultiplier": 3.0},
                param_sources={"k": "literal", "stddevMultiplier": "literal"},
                input_bindings={"input_point_cloud": "downsampled_cloud"},
                input_sources={"input_point_cloud": "variable"},
                output_bindings={"output_point_cloud": "filtered_cloud"},
                output_sources={"output_point_cloud": "variable"},
            ),
        ),
        node(
            id_colorize,
            OP_Z_COLORIZE,
            560,
            360,
            "Z轴着色点云",
            make_properties(
                input_bindings={"input_point_cloud": "filtered_cloud"},
                input_sources={"input_point_cloud": "variable"},
                output_bindings={"output_point_cloud": "colored_segment_cloud"},
                output_sources={"output_point_cloud": "variable"},
            ),
        ),
        node(
            id_export_cloud,
            OP_SAVE_POINT_CLOUD_BLOB,
            280,
            520,
            "彩色点云存Blob",
            make_properties(
                params={"fileName": "height-diff-segments.ply"},
                param_sources={"fileName": "literal"},
                input_bindings={"input_point_cloud": "colored_segment_cloud"},
                input_sources={"input_point_cloud": "variable"},
                output_bindings={"download_url": "colored_cloud_download_url"},
                output_sources={"download_url": "variable"},
            ),
        ),
        node(
            id_preview,
            OP_COLORED_CLOUD_TO_IMAGE,
            560,
            520,
            "彩色点云转图像",
            make_properties(
                params={
                    "autoBounds": True,
                    "imageResolution": IMAGE_RESOLUTION,
                },
                param_sources={
                    "autoBounds": "literal",
                    "imageResolution": "literal",
                },
                input_bindings={"input_point_cloud": "colored_segment_cloud"},
                input_sources={"input_point_cloud": "variable"},
                output_bindings={
                    "output_image": "preview_image",
                    "projection_mapping": "projection_mapping",
                },
                output_sources={
                    "output_image": "variable",
                    "projection_mapping": "variable",
                },
            ),
        ),
        *roi_nodes,
        *crop_nodes,
        *ransac_nodes,
        *color_nodes,
        *stats_nodes,
        *plane_diff_nodes,
        node(
            id_render_outlines_top,
            OP_RENDER_PLANE_OUTLINES,
            560,
            980 + (region_count - 1) * 80,
            "平面轮廓渲染(俯视)",
            make_properties(
                params={
                    "viewType": "top",
                    "regionCount": region_count,
                    "referenceRegionIndex": 0,
                },
                param_sources={
                    "viewType": "literal",
                    "regionCount": "literal",
                    "referenceRegionIndex": "literal",
                },
                input_bindings={
                    "input_mat": "preview_image",
                },
                input_sources={
                    "input_mat": "variable",
                },
                output_bindings={"output_mat": "outlined_top_image"},
                output_sources={"output_mat": "variable"},
            ),
        ),
        node(
            id_annotate,
            OP_ANNOTATE_TILTED_VIEW,
            560,
            1060 + (region_count - 1) * 80,
            "结果图标注",
            make_properties(
                input_bindings={
                    "input_mat": "outlined_top_image",
                    "inspection_result": "inspection_result_2",
                },
                input_sources={
                    "input_mat": "variable",
                    "inspection_result": "variable",
                },
                output_bindings={"output_mat": "annotated_result_image"},
                output_sources={"output_mat": "variable"},
            ),
        ),
        node(
            id_export_image,
            OP_SAVE_IMAGE_BLOB,
            560,
            1140 + (region_count - 1) * 80,
            "结果图存Blob",
            make_properties(
                params={"fileName": "height-diff-result.png"},
                param_sources={"fileName": "literal"},
                input_bindings={"input_mat": "annotated_result_image"},
                input_sources={"input_mat": "variable"},
                output_bindings={"download_url": "result_image_download_url"},
                output_sources={"download_url": "variable"},
            ),
        ),
        node(
            id_tilted_view,
            OP_COLORED_CLOUD_TO_TILTED_IMAGE,
            860,
            980 + (region_count - 1) * 80,
            "倾斜视图生成",
            make_properties(
                params={
                    "tiltAngleY": 45,
                    "tiltAngleX": 45,
                    "tiltAngleZ": -30,
                    "imageResolution": IMAGE_RESOLUTION,
                },
                param_sources={
                    "tiltAngleY": "literal",
                    "tiltAngleX": "literal",
                    "tiltAngleZ": "literal",
                    "imageResolution": "literal",
                },
                input_bindings={"input_point_cloud": "colored_segment_cloud"},
                input_sources={"input_point_cloud": "variable"},
                output_bindings={"output_image": "tilted_preview_image"},
                output_sources={"output_image": "variable"},
            ),
        ),
        node(
            id_render_outlines_tilted,
            OP_RENDER_PLANE_OUTLINES,
            860,
            1060 + (region_count - 1) * 80,
            "平面轮廓渲染(倾斜)",
            make_properties(
                params={
                    "viewType": "tilted",
                    "tiltAngleY": 45,
                    "tiltAngleX": 45,
                    "tiltAngleZ": -30,
                    "regionCount": region_count,
                    "referenceRegionIndex": 0,
                },
                param_sources={
                    "viewType": "literal",
                    "tiltAngleY": "literal",
                    "tiltAngleX": "literal",
                    "tiltAngleZ": "literal",
                    "regionCount": "literal",
                    "referenceRegionIndex": "literal",
                },
                input_bindings={
                    "input_mat": "tilted_preview_image",
                    "input_point_cloud": "colored_segment_cloud",
                },
                input_sources={
                    "input_mat": "variable",
                    "input_point_cloud": "variable",
                },
                output_bindings={"output_mat": "outlined_tilted_image"},
                output_sources={"output_mat": "variable"},
            ),
        ),
        node(
            id_annotate_tilted,
            OP_ANNOTATE_TILTED_VIEW,
            860,
            1140 + (region_count - 1) * 80,
            "倾斜视图标注",
            make_properties(
                input_bindings={
                    "input_mat": "outlined_tilted_image",
                    "inspection_result": "inspection_result_2",
                },
                input_sources={
                    "input_mat": "variable",
                    "inspection_result": "variable",
                },
                output_bindings={"output_mat": "annotated_tilted_image"},
                output_sources={"output_mat": "variable"},
            ),
        ),
        node(
            id_export_tilted,
            OP_SAVE_IMAGE_BLOB,
            860,
            1220 + (region_count - 1) * 80,
            "倾斜视图存Blob",
            make_properties(
                params={"fileName": "height-diff-tilted.png"},
                param_sources={"fileName": "literal"},
                input_bindings={"input_mat": "annotated_tilted_image"},
                input_sources={"input_mat": "variable"},
                output_bindings={"download_url": "tilted_image_download_url"},
                output_sources={"download_url": "variable"},
            ),
        ),
        node(
            id_end,
            "end-node",
            560,
            1300 + (region_count - 1) * 80,
            "结束",
            make_properties(
                input_bindings={
                    "coloredPointCloudUrl": "colored_cloud_download_url",
                    "resultImageUrl": "result_image_download_url",
                    "tiltedImageUrl": "tilted_image_download_url",
                    "inspectionResult": "inspection_result_2",
                },
                input_sources={
                    "coloredPointCloudUrl": "variable",
                    "resultImageUrl": "variable",
                    "tiltedImageUrl": "variable",
                    "inspectionResult": "variable",
                },
            ),
        ),
    ]

    edges = [
        edge(id_start, id_read),
        edge(id_read, id_downsample),
        edge(id_downsample, id_denoise),
        edge(id_denoise, id_colorize),
        edge(id_colorize, id_export_cloud),
        edge(id_colorize, id_preview),
    ]

    for i in range(region_count):
        edges.append(edge(id_preview, roi_nodes[i]["id"]))
        edges.append(edge(roi_nodes[i]["id"], crop_nodes[i]["id"]))
        edges.append(edge(crop_nodes[i]["id"], ransac_nodes[i]["id"]))
        edges.append(edge(ransac_nodes[i]["id"], color_nodes[i]["id"]))
        edges.append(edge(ransac_nodes[i]["id"], stats_nodes[i]["id"]))

    # plane_height_diff 节点连接到 render_outlines_top（确保所有区域处理完成后执行轮廓渲染）
    if plane_diff_nodes:
        edges.append(edge(plane_diff_nodes[-1]["id"], id_render_outlines_top))

    edges.extend(
        [
            edge(id_render_outlines_top, id_annotate),
            edge(id_annotate, id_export_image),
            edge(id_colorize, id_tilted_view),
            edge(id_tilted_view, id_render_outlines_tilted),
            edge(id_render_outlines_tilted, id_annotate_tilted),
            edge(id_annotate_tilted, id_export_tilted),
            edge(id_export_cloud, id_end),
            edge(id_export_image, id_end),
            edge(id_export_tilted, id_end),
        ]
    )

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

    if os.getenv("AURORA_VERBOSE_PAYLOAD", "").strip().lower() in {
        "1",
        "true",
        "yes",
    }:
        print("      生成的请求 JSON：")
        print(json.dumps(payload, ensure_ascii=False, indent=2))

    region_count = len(ROIS_JSON)
    output_variables = [f"inspection_result_{i}" for i in range(2, region_count + 1)]

    existed = find_workflow_by_name(project_id, WORKFLOW_NAME)
    if existed is not None:
        workflow_id = existed.get("id")
        if not workflow_id:
            raise RuntimeError("命中已存在工作流但缺少 id，无法更新")
        result = api_put(f"/api/app/workflow/{workflow_id}", payload)
        print(f"      ✓ 命中已有工作流，已更新，ID：{workflow_id}")

        # 配置输出变量：将 abs_diff_2、abs_diff_3、abs_diff_4 提级到接口返回的 variables 字段
        output_config = {
            "workflowId": workflow_id,
            "outputVariables": output_variables,
        }
        api_put(f"/api/app/workflow/{workflow_id}/output-config", output_config)
        print(f"      ✓ 输出变量已配置：{', '.join(output_variables)}")

        return result

    result = api_post("/api/app/workflow", payload)
    workflow_id = result.get("id")
    print(f"      ✓ 工作流创建成功，ID：{workflow_id}")

    # 配置输出变量：将 abs_diff_2、abs_diff_3、abs_diff_4 提级到接口返回的 variables 字段
    output_config = {
        "workflowId": workflow_id,
        "outputVariables": output_variables,
    }
    api_put(f"/api/app/workflow/{workflow_id}/output-config", output_config)
    print(f"      ✓ 输出变量已配置：{', '.join(output_variables)}")

    return result


def validate_roi_json(roi_json: str, arg_name: str, index: int) -> str:
    try:
        parsed = json.loads(roi_json)
    except json.JSONDecodeError as ex:
        raise ValueError(f"{arg_name}[{index}] 不是合法 JSON：{ex}") from ex

    rois = parsed.get("rois") if isinstance(parsed, dict) else None
    if not isinstance(rois, list) or len(rois) != 1:
        raise ValueError(f"{arg_name}[{index}] 必须是且仅包含 1 个 ROI 的 JSON。")

    base_image = parsed.get("baseImage")
    if not isinstance(base_image, dict):
        base_image = {}
        parsed["baseImage"] = base_image

    base_image.pop("selectedLabel", None)
    base_image.pop("previewImages", None)

    projection_mapping = base_image.get("projectionMapping")
    if not isinstance(projection_mapping, dict):
        projection_mapping = {}
        base_image["projectionMapping"] = projection_mapping

    projection_mapping["viewLabel"] = "XY"
    projection_mapping["worldMinX"] = float(PROJECTION_BOUNDS["minX"])
    projection_mapping["worldMaxX"] = float(PROJECTION_BOUNDS["maxX"])
    projection_mapping["worldMinY"] = float(PROJECTION_BOUNDS["minY"])
    projection_mapping["worldMaxY"] = float(PROJECTION_BOUNDS["maxY"])
    projection_mapping["imageWidth"] = int(PREVIEW_WIDTH)
    projection_mapping["imageHeight"] = int(PREVIEW_HEIGHT)

    return json.dumps(parsed, ensure_ascii=False)


def validate_rois_json(rois_json_list: list[str]) -> list[str]:
    """验证并处理多个ROI配置JSON列表。"""
    if not rois_json_list:
        raise ValueError("至少需要指定一个ROI区域（--roi-json）。")

    validated = []
    for i, roi_json in enumerate(rois_json_list):
        validated.append(validate_roi_json(roi_json, "--roi-json", i))

    return validated


def main():
    global BASE_URL, USERNAME, PASSWORD, POINT_CLOUD_PATH, WORKFLOW_NAME, VOXEL_SIZE
    global ROIS_JSON, THRESHOLD_MIN, THRESHOLD_MAX, PROJECTION_BOUNDS
    global PREVIEW_WIDTH, PREVIEW_HEIGHT

    args = parse_args()
    BASE_URL = resolve_base_url(args)
    USERNAME = args.username
    PASSWORD = args.password
    POINT_CLOUD_PATH = args.point_cloud_path
    WORKFLOW_NAME = args.workflow_name
    PROJECT["name"] = args.project_name
    PROJECT["projectCode"] = args.project_code
    VOXEL_SIZE = args.voxel_size
    THRESHOLD_MIN = args.threshold_min
    THRESHOLD_MAX = args.threshold_max
    PROJECTION_BOUNDS = {
        "minX": args.projection_min_x,
        "maxX": args.projection_max_x,
        "minY": args.projection_min_y,
        "maxY": args.projection_max_y,
    }
    PREVIEW_WIDTH, PREVIEW_HEIGHT = compute_preview_size(
        PROJECTION_BOUNDS, IMAGE_RESOLUTION
    )
    if args.roi_json:
        ROIS_JSON = validate_rois_json(args.roi_json)
    else:
        ROIS_JSON = validate_rois_json(ROIS_JSON)

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
