"""
曲面/平面高度差工作流自动创建脚本。

目标效果：
  1) 读取 3D 点云
  2) 点云预处理（体素下采样 + 法向量）
  3) 区域生长分割 + 标签着色，并将彩色点云存 Blob（前端可查看）
  4) ROI 分区标注两块区域（当前后端端口限制下，使用两个 ROI 分区节点）
  5) 每个 ROI 内分别拟合平面与曲面（球面）
  6) ROI 分区通过 projectionMapping 映射到模型坐标
  7) 计算两个区域的平面高度差
  8) 阈值判定 OK/NG
  9) 输出带标注的 2D 结果图并存 Blob

用法示例：
  python create_surface_height_diff_workflow.py
  python create_surface_height_diff_workflow.py --host 10.127.135.143 --api-port 5000
  python create_surface_height_diff_workflow.py --point-cloud-path /data/pointclouds/sample.ply
"""

from __future__ import annotations

import argparse
import json
import os
import socket
import sys
import uuid

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

PROJECT = {
    "projectCode": "PRJ-SURFACE-HEIGHT-DIFF-001",
    "name": "平面曲面高度差检测项目",
    "version": "1.0.0",
    "description": "含点云分割着色、Blob缓存、ROI映射、平面/曲面拟合、高度差判定与结果标注输出。",
}

WORKFLOW_NAME = "平面曲面高度差检测"
POINT_CLOUD_PATH = "/data/pointclouds/sample.ply"

# 预览投影范围（与 ROI projectionMapping 一致）
PROJECTION_BOUNDS = {"minX": -80.0, "maxX": 80.0, "minY": -60.0, "maxY": 60.0}
PREVIEW_WIDTH = 512
PREVIEW_HEIGHT = 384

ROI_A_JSON = json.dumps(
    {
        "rois": [
            {
                "name": "PlaneA",
                "type": "Rect",
                "x": 120,
                "y": 150,
                "width": 120,
                "height": 100,
                "rotation": 0,
            }
        ],
        "baseImage": {
            "selectedBlobName": "workflow-image/preview_xy.png",
            "selectedLabel": "XY",
            "previewImages": [
                {"label": "XY", "blobName": "workflow-image/preview_xy.png"},
                {"label": "XZ", "blobName": "workflow-image/preview_xz.png"},
                {"label": "YZ", "blobName": "workflow-image/preview_yz.png"},
            ],
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
)

ROI_B_JSON = json.dumps(
    {
        "rois": [
            {
                "name": "PlaneB",
                "type": "Rect",
                "x": 280,
                "y": 150,
                "width": 120,
                "height": 100,
                "rotation": 0,
            }
        ],
        "baseImage": {
            "selectedBlobName": "workflow-image/preview_xy.png",
            "selectedLabel": "XY",
            "previewImages": [
                {"label": "XY", "blobName": "workflow-image/preview_xy.png"},
                {"label": "XZ", "blobName": "workflow-image/preview_xz.png"},
                {"label": "YZ", "blobName": "workflow-image/preview_yz.png"},
            ],
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
)

VOXEL_SIZE = 0.2
THRESHOLD_MIN = -0.2
THRESHOLD_MAX = 0.2

_access_token: str | None = None
_session: requests.Session | None = None

# ============================================================
# 算子 GUID
# ============================================================
OP_READ_POINT_CLOUD = "a1b2c3d4-e5f6-7890-abcd-ef1234567890"
OP_VOXEL_DOWNSAMPLE = "b1d01234-5678-9012-4567-89012345670a"
OP_COMPUTE_NORMALS = "a1b2c3d4-0004-4000-8000-000000000011"
OP_REGION_GROWING = "a1b2c3d4-0007-4000-8000-000000000028"
OP_LABEL_COLORIZE = "6f8be0a9-9951-4d58-b4ec-1aab2baf5101"
OP_SAVE_POINT_CLOUD_BLOB = "a1b2c3d4-e5f6-7890-abcd-ef1234567891"
OP_COLORED_CLOUD_TO_IMAGE = "1dd66555-a882-436b-bf30-dab8c680a201"
OP_ROI_PARTITION = "a1b2c3d4-0001-4000-8000-000000000001"
OP_POINT_CLOUD_CROP = "b1c2d3e4-0001-4000-8000-000000000101"
OP_RANSAC_SVD_PLANE_FIT = "f5a34567-8901-2345-6789-01234567890e"
OP_SPHERE_FIT = "b1a10001-0001-4000-8000-000000000031"
OP_Z_CHANNEL_STATS = "b1c2d3e4-0002-4000-8000-000000000102"
OP_HEIGHT_DIFF_EVAL = "8c3dd5d9-8234-49fb-8d1f-1d7c7e0b4201"
OP_ANNOTATE_HEIGHT_DIFF = "1d6498c5-8d95-41ab-8801-c3f8e20d7301"
OP_SAVE_IMAGE_BLOB = "4b8af0f5-c0fb-45df-97a6-5ab2462cfd01"


# ============================================================
# 通用工具
# ============================================================
def new_uuid() -> str:
    return uuid.uuid4().hex


def _get_local_ips() -> list[str]:
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
        timeout=getattr(args, "discover_timeout", DEFAULT_DISCOVERY_TIMEOUT),
    )
    if discovered_hosts:
        return f"{args.scheme}://{discovered_hosts[0]}:{args.api_port}"

    return f"{args.scheme}://{DEFAULT_HOST}:{args.api_port}"


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="平面曲面高度差工作流自动创建")
    parser.add_argument(
        "--base-url", default="", help="完整服务地址，如 http://10.0.0.1:5000"
    )
    parser.add_argument("--host", default="", help="服务 IP 或域名")
    parser.add_argument("--scheme", default=DEFAULT_SCHEME, choices=["http", "https"])
    parser.add_argument("--api-port", type=int, default=DEFAULT_API_PORT)
    parser.add_argument("--discovery-port", type=int, default=DEFAULT_DISCOVERY_PORT)
    parser.add_argument(
        "--discover-timeout", type=float, default=DEFAULT_DISCOVERY_TIMEOUT
    )
    parser.add_argument("--username", default=os.getenv("AURORA_USERNAME", "admin"))
    parser.add_argument("--password", default=os.getenv("AURORA_PASSWORD", ""))

    parser.add_argument("--project-name", default=PROJECT["name"])
    parser.add_argument("--project-code", default=PROJECT["projectCode"])
    parser.add_argument("--workflow-name", default=WORKFLOW_NAME)
    parser.add_argument("--point-cloud-path", default=POINT_CLOUD_PATH)

    parser.add_argument("--voxel-size", type=float, default=VOXEL_SIZE)
    parser.add_argument("--threshold-min", type=float, default=THRESHOLD_MIN)
    parser.add_argument("--threshold-max", type=float, default=THRESHOLD_MAX)

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
    parser.add_argument("--preview-width", type=int, default=PREVIEW_WIDTH)
    parser.add_argument("--preview-height", type=int, default=PREVIEW_HEIGHT)

    parser.add_argument("--roi-a-json", default=ROI_A_JSON)
    parser.add_argument("--roi-b-json", default=ROI_B_JSON)
    return parser.parse_args()


def _get_session() -> requests.Session:
    global _session
    if _session is not None:
        return _session

    _session = requests.Session()

    config_url = f"{BASE_URL}/api/abp/application-configuration"
    resp = _session.get(config_url, timeout=30)
    resp.raise_for_status()

    for cookie in _session.cookies:
        if cookie.name.startswith(".AspNetCore.Antiforgery") and cookie.value:
            _session.headers["X-XSRF-TOKEN"] = cookie.value
            break

    anti_forgery = resp.headers.get("X-XSRF-TOKEN") or resp.headers.get("Set-Cookie")
    if anti_forgery and "X-XSRF-TOKEN" not in _session.headers:
        _session.headers["X-XSRF-TOKEN"] = anti_forgery

    return _session


def login() -> None:
    global _access_token
    print("正在进行身份认证...")
    session = _get_session()
    url = f"{BASE_URL}/api/app/account/login"
    payload = {"name": USERNAME, "password": PASSWORD}
    resp = session.post(url, json=payload, timeout=30)
    resp.raise_for_status()
    result = resp.json()

    _access_token = (
        result.get("accessToken") or result.get("access_token") or result.get("token")
    )
    if not _access_token:
        raise RuntimeError("登录失败：未获取到 token")

    session.headers["Authorization"] = f"Bearer {_access_token}"
    print("✓ 身份认证成功")


def api_post(path: str, body: dict) -> dict:
    if not _access_token:
        raise RuntimeError("未登录，无法发送请求")
    session = _get_session()
    url = f"{BASE_URL}{path}"
    resp = session.post(url, json=body, timeout=30)
    resp.raise_for_status()
    return resp.json()


def api_get(path: str, params: dict | None = None) -> dict:
    if not _access_token:
        raise RuntimeError("未登录，无法发送请求")
    session = _get_session()
    url = f"{BASE_URL}{path}"
    resp = session.get(url, params=params, timeout=30)
    resp.raise_for_status()
    return resp.json()


def api_put(path: str, body: dict) -> dict:
    if not _access_token:
        raise RuntimeError("未登录，无法发送请求")
    session = _get_session()
    url = f"{BASE_URL}{path}"
    resp = session.put(url, json=body, timeout=30)
    resp.raise_for_status()
    return resp.json()


def find_project_by_code(project_code: str) -> dict | None:
    def _norm(v: str | None) -> str:
        return (v or "").strip().casefold()

    result = api_get(
        "/api/app/project-info",
        {"filter": project_code, "skipCount": 0, "maxResultCount": 200},
    )

    items = result.get("items") or []
    target = _norm(project_code)
    for item in items:
        code = item.get("projectCode") or item.get("project_code") or item.get("code")
        if _norm(code) == target:
            return item
    if len(items) == 1:
        return items[0]
    return None


def try_find_project_by_code(project_code: str) -> dict | None:
    try:
        return find_project_by_code(project_code)
    except requests.exceptions.RequestException:
        return None


def build_fallback_project_code(base_code: str) -> str:
    return f"{base_code}-{uuid.uuid4().hex[:6].upper()}"


def create_project() -> str:
    print(f"[1/2] 创建项目：{PROJECT['name']} ...")
    try:
        result = api_post("/api/app/project-info", PROJECT)
        project_id = result["id"]
        print(f"      ✓ 项目创建成功，ID：{project_id}")
        return project_id
    except requests.exceptions.HTTPError as post_err:
        existed = try_find_project_by_code(PROJECT["projectCode"])
        if existed is not None:
            project_id = existed["id"]
            print(f"      ✓ 复用已有项目，ID：{project_id}")
            return project_id

        fallback = dict(PROJECT)
        fallback_code = build_fallback_project_code(PROJECT["projectCode"])
        fallback["projectCode"] = fallback_code
        print(f"      ! 项目编号冲突，改用：{fallback_code}")
        try:
            result = api_post("/api/app/project-info", fallback)
            project_id = result["id"]
            PROJECT["projectCode"] = fallback_code
            print(f"      ✓ 项目创建成功，ID：{project_id}")
            return project_id
        except requests.exceptions.HTTPError:
            raise post_err


def find_workflow_by_name(project_id: str, workflow_name: str) -> dict | None:
    def _norm(v: str | None) -> str:
        return (v or "").strip().casefold()

    result = api_get("/api/app/workflow", {"projectId": project_id})
    items = result if isinstance(result, list) else (result.get("items") or [])

    target = _norm(workflow_name)
    for item in items:
        if _norm(item.get("name")) == target:
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


def validate_roi_json(roi_json: str, arg_name: str) -> str:
    try:
        parsed = json.loads(roi_json)
    except json.JSONDecodeError as ex:
        raise ValueError(f"{arg_name} 不是合法 JSON：{ex}") from ex

    rois = parsed.get("rois") if isinstance(parsed, dict) else None
    if not isinstance(rois, list) or len(rois) != 1:
        raise ValueError(f"{arg_name} 必须是且仅包含 1 个 ROI 的 JSON。")

    return json.dumps(parsed, ensure_ascii=False)


def build_graph_data() -> dict:
    bounds = PROJECTION_BOUNDS

    id_start = new_uuid()
    id_read = new_uuid()
    id_downsample = new_uuid()
    id_normals = new_uuid()
    id_segment = new_uuid()
    id_label_color = new_uuid()
    id_export_cloud = new_uuid()
    id_preview = new_uuid()

    id_roi_a = new_uuid()
    id_roi_b = new_uuid()
    id_crop_a = new_uuid()
    id_crop_b = new_uuid()

    id_plane_a = new_uuid()
    id_plane_b = new_uuid()
    id_sphere_a = new_uuid()
    id_sphere_b = new_uuid()

    id_stats_a = new_uuid()
    id_stats_b = new_uuid()
    id_eval = new_uuid()
    id_annotate = new_uuid()
    id_export_image = new_uuid()
    id_end = new_uuid()

    nodes = [
        node(id_start, "start-node", 620, 40, "开始", make_properties()),
        node(
            id_read,
            OP_READ_POINT_CLOUD,
            620,
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
            620,
            200,
            "体素下采样",
            make_properties(
                params={"voxelSize": VOXEL_SIZE},
                param_sources={"voxelSize": "literal"},
                input_bindings={"input_point_cloud": "raw_cloud"},
                input_sources={"input_point_cloud": "variable"},
                output_bindings={"output_point_cloud": "filtered_cloud"},
                output_sources={"output_point_cloud": "variable"},
            ),
        ),
        node(
            id_normals,
            OP_COMPUTE_NORMALS,
            620,
            280,
            "法向量计算",
            make_properties(
                params={"knnCount": 20, "consistentOrientation": True},
                param_sources={
                    "knnCount": "literal",
                    "consistentOrientation": "literal",
                },
                input_bindings={"input_point_cloud": "filtered_cloud"},
                input_sources={"input_point_cloud": "variable"},
                output_bindings={
                    "normal_cloud": "normal_cloud",
                    "curvature_mat": "curvature_mat",
                },
                output_sources={
                    "normal_cloud": "variable",
                    "curvature_mat": "variable",
                },
            ),
        ),
        node(
            id_segment,
            OP_REGION_GROWING,
            620,
            360,
            "区域生长分割",
            make_properties(
                params={
                    "smoothnessThreshold": 5.0,
                    "curvatureThreshold": 0.05,
                    "minSegmentSize": 50,
                    "maxSegmentSize": 100000,
                    "neighborCount": 30,
                    "imageResolution": PREVIEW_WIDTH,
                },
                param_sources={
                    "smoothnessThreshold": "literal",
                    "curvatureThreshold": "literal",
                    "minSegmentSize": "literal",
                    "maxSegmentSize": "literal",
                    "neighborCount": "literal",
                    "imageResolution": "literal",
                },
                input_bindings={
                    "input_point_cloud": "filtered_cloud",
                    "normals": "normal_cloud",
                    "curvature": "curvature_mat",
                },
                input_sources={
                    "input_point_cloud": "variable",
                    "normals": "variable",
                    "curvature": "variable",
                },
                output_bindings={"segment_cloud": "segment_cloud"},
                output_sources={"segment_cloud": "variable"},
            ),
        ),
        node(
            id_label_color,
            OP_LABEL_COLORIZE,
            620,
            440,
            "标签着色点云",
            make_properties(
                params={"noiseAsBlack": True},
                param_sources={"noiseAsBlack": "literal"},
                input_bindings={"input_point_cloud": "segment_cloud"},
                input_sources={"input_point_cloud": "variable"},
                output_bindings={"output_point_cloud": "colored_segment_cloud"},
                output_sources={"output_point_cloud": "variable"},
            ),
        ),
        node(
            id_export_cloud,
            OP_SAVE_POINT_CLOUD_BLOB,
            300,
            520,
            "分割点云存Blob",
            make_properties(
                params={"fileName": "surface-segmented-color.ply"},
                param_sources={"fileName": "literal"},
                input_bindings={"input_point_cloud": "colored_segment_cloud"},
                input_sources={"input_point_cloud": "variable"},
                output_bindings={"download_url": "segmented_cloud_download_url"},
                output_sources={"download_url": "variable"},
            ),
        ),
        node(
            id_preview,
            OP_COLORED_CLOUD_TO_IMAGE,
            620,
            520,
            "彩色点云转图像",
            make_properties(
                params={
                    "autoBounds": False,
                    "imageResolution": PREVIEW_WIDTH,
                    "minX": bounds["minX"],
                    "maxX": bounds["maxX"],
                    "minY": bounds["minY"],
                    "maxY": bounds["maxY"],
                },
                param_sources={
                    "autoBounds": "literal",
                    "imageResolution": "literal",
                    "minX": "literal",
                    "maxX": "literal",
                    "minY": "literal",
                    "maxY": "literal",
                },
                input_bindings={"input_point_cloud": "colored_segment_cloud"},
                input_sources={"input_point_cloud": "variable"},
                output_bindings={"output_image": "preview_image"},
                output_sources={"output_image": "variable"},
            ),
        ),
        # 说明：当前 point_cloud_crop 以单个 Mat 掩膜输入，
        # 为了稳定链路，这里拆成两个 roi_partition 节点（每个节点 1 个 ROI）。
        node(
            id_roi_a,
            OP_ROI_PARTITION,
            450,
            620,
            "ROI A",
            make_properties(
                params={"roiJson": ROI_A_JSON},
                param_sources={"roiJson": "literal"},
                input_bindings={"input_mat": "preview_image"},
                input_sources={"input_mat": "variable"},
                output_bindings={
                    "primary_mask": "roi_a_mask",
                    "roi_metadata": "roi_a_metadata",
                },
                output_sources={
                    "primary_mask": "variable",
                    "roi_metadata": "variable",
                },
            ),
        ),
        node(
            id_roi_b,
            OP_ROI_PARTITION,
            790,
            620,
            "ROI B",
            make_properties(
                params={"roiJson": ROI_B_JSON},
                param_sources={"roiJson": "literal"},
                input_bindings={"input_mat": "preview_image"},
                input_sources={"input_mat": "variable"},
                output_bindings={
                    "primary_mask": "roi_b_mask",
                    "roi_metadata": "roi_b_metadata",
                },
                output_sources={
                    "primary_mask": "variable",
                    "roi_metadata": "variable",
                },
            ),
        ),
        node(
            id_crop_a,
            OP_POINT_CLOUD_CROP,
            450,
            720,
            "掩膜裁剪A",
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
                    "roi_mask": "roi_a_mask",
                    "roi_metadata": "roi_a_metadata",
                },
                input_sources={
                    "input_point_cloud": "variable",
                    "roi_mask": "variable",
                    "roi_metadata": "variable",
                },
                output_bindings={"output_point_cloud": "region_a_cloud"},
                output_sources={"output_point_cloud": "variable"},
            ),
        ),
        node(
            id_crop_b,
            OP_POINT_CLOUD_CROP,
            790,
            720,
            "掩膜裁剪B",
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
                    "roi_mask": "roi_b_mask",
                    "roi_metadata": "roi_b_metadata",
                },
                input_sources={
                    "input_point_cloud": "variable",
                    "roi_mask": "variable",
                    "roi_metadata": "variable",
                },
                output_bindings={"output_point_cloud": "region_b_cloud"},
                output_sources={"output_point_cloud": "variable"},
            ),
        ),
        node(
            id_plane_a,
            OP_RANSAC_SVD_PLANE_FIT,
            330,
            820,
            "A平面拟合",
            make_properties(
                params={
                    "distanceThreshold": 0.01,
                    "maxIterations": 1200,
                    "probability": 0.99,
                },
                param_sources={
                    "distanceThreshold": "literal",
                    "maxIterations": "literal",
                    "probability": "literal",
                },
                input_bindings={"input_point_cloud": "region_a_cloud"},
                input_sources={"input_point_cloud": "variable"},
                output_bindings={
                    "plane_params": "plane_a_params",
                    "inlier_points": "plane_a_inliers",
                    "fitting_error": "plane_a_error",
                },
                output_sources={
                    "plane_params": "variable",
                    "inlier_points": "variable",
                    "fitting_error": "variable",
                },
            ),
        ),
        node(
            id_plane_b,
            OP_RANSAC_SVD_PLANE_FIT,
            910,
            820,
            "B平面拟合",
            make_properties(
                params={
                    "distanceThreshold": 0.01,
                    "maxIterations": 1200,
                    "probability": 0.99,
                },
                param_sources={
                    "distanceThreshold": "literal",
                    "maxIterations": "literal",
                    "probability": "literal",
                },
                input_bindings={"input_point_cloud": "region_b_cloud"},
                input_sources={"input_point_cloud": "variable"},
                output_bindings={
                    "plane_params": "plane_b_params",
                    "inlier_points": "plane_b_inliers",
                    "fitting_error": "plane_b_error",
                },
                output_sources={
                    "plane_params": "variable",
                    "inlier_points": "variable",
                    "fitting_error": "variable",
                },
            ),
        ),
        node(
            id_sphere_a,
            OP_SPHERE_FIT,
            450,
            920,
            "A曲面拟合",
            make_properties(
                params={
                    "distanceThreshold": 0.01,
                    "maxIterations": 1200,
                    "probability": 0.99,
                },
                param_sources={
                    "distanceThreshold": "literal",
                    "maxIterations": "literal",
                    "probability": "literal",
                },
                input_bindings={"input_point_cloud": "region_a_cloud"},
                input_sources={"input_point_cloud": "variable"},
                output_bindings={
                    "sphere_params": "sphere_a_params",
                    "fitting_error": "sphere_a_error",
                },
                output_sources={
                    "sphere_params": "variable",
                    "fitting_error": "variable",
                },
            ),
        ),
        node(
            id_sphere_b,
            OP_SPHERE_FIT,
            790,
            920,
            "B曲面拟合",
            make_properties(
                params={
                    "distanceThreshold": 0.01,
                    "maxIterations": 1200,
                    "probability": 0.99,
                },
                param_sources={
                    "distanceThreshold": "literal",
                    "maxIterations": "literal",
                    "probability": "literal",
                },
                input_bindings={"input_point_cloud": "region_b_cloud"},
                input_sources={"input_point_cloud": "variable"},
                output_bindings={
                    "sphere_params": "sphere_b_params",
                    "fitting_error": "sphere_b_error",
                },
                output_sources={
                    "sphere_params": "variable",
                    "fitting_error": "variable",
                },
            ),
        ),
        node(
            id_stats_a,
            OP_Z_CHANNEL_STATS,
            450,
            1020,
            "A高度统计",
            make_properties(
                input_bindings={
                    "input_point_cloud": "plane_a_inliers",
                    "plane_params": "plane_a_params",
                },
                input_sources={
                    "input_point_cloud": "variable",
                    "plane_params": "variable",
                },
                output_bindings={"avg_height": "height_a"},
                output_sources={"avg_height": "variable"},
            ),
        ),
        node(
            id_stats_b,
            OP_Z_CHANNEL_STATS,
            790,
            1020,
            "B高度统计",
            make_properties(
                input_bindings={
                    "input_point_cloud": "plane_b_inliers",
                    "plane_params": "plane_b_params",
                },
                input_sources={
                    "input_point_cloud": "variable",
                    "plane_params": "variable",
                },
                output_bindings={"avg_height": "height_b"},
                output_sources={"avg_height": "variable"},
            ),
        ),
        node(
            id_eval,
            OP_HEIGHT_DIFF_EVAL,
            620,
            1120,
            "高度差判定",
            make_properties(
                params={"minDiff": THRESHOLD_MIN, "maxDiff": THRESHOLD_MAX},
                param_sources={"minDiff": "literal", "maxDiff": "literal"},
                input_bindings={"height_a": "height_a", "height_b": "height_b"},
                input_sources={"height_a": "variable", "height_b": "variable"},
                output_bindings={"signed_diff": "signed_diff", "is_ok": "is_ok"},
                output_sources={"signed_diff": "variable", "is_ok": "variable"},
            ),
        ),
        node(
            id_annotate,
            OP_ANNOTATE_HEIGHT_DIFF,
            620,
            1220,
            "结果图标注",
            make_properties(
                input_bindings={
                    "input_mat": "preview_image",
                    "roi_metadata_a": "roi_a_metadata",
                    "roi_metadata_b": "roi_b_metadata",
                    "height_a": "height_a",
                    "height_b": "height_b",
                    "signed_diff": "signed_diff",
                    "is_ok": "is_ok",
                },
                input_sources={
                    "input_mat": "variable",
                    "roi_metadata_a": "variable",
                    "roi_metadata_b": "variable",
                    "height_a": "variable",
                    "height_b": "variable",
                    "signed_diff": "variable",
                    "is_ok": "variable",
                },
                output_bindings={"output_mat": "annotated_result_image"},
                output_sources={"output_mat": "variable"},
            ),
        ),
        node(
            id_export_image,
            OP_SAVE_IMAGE_BLOB,
            620,
            1300,
            "结果图存Blob",
            make_properties(
                params={"fileName": "surface-height-diff-result.png"},
                param_sources={"fileName": "literal"},
                input_bindings={"input_mat": "annotated_result_image"},
                input_sources={"input_mat": "variable"},
                output_bindings={"download_url": "result_image_download_url"},
                output_sources={"download_url": "variable"},
            ),
        ),
        node(
            id_end,
            "end-node",
            620,
            1380,
            "结束",
            make_properties(
                input_bindings={
                    "segmentedCloudUrl": "segmented_cloud_download_url",
                    "resultImageUrl": "result_image_download_url",
                    "signedDiff": "signed_diff",
                    "isOk": "is_ok",
                    "planeAError": "plane_a_error",
                    "planeBError": "plane_b_error",
                    "sphereAError": "sphere_a_error",
                    "sphereBError": "sphere_b_error",
                },
                input_sources={
                    "segmentedCloudUrl": "variable",
                    "resultImageUrl": "variable",
                    "signedDiff": "variable",
                    "isOk": "variable",
                    "planeAError": "variable",
                    "planeBError": "variable",
                    "sphereAError": "variable",
                    "sphereBError": "variable",
                },
            ),
        ),
    ]

    edges = [
        edge(id_start, id_read),
        edge(id_read, id_downsample),
        edge(id_downsample, id_normals),
        edge(id_normals, id_segment),
        edge(id_segment, id_label_color),
        edge(id_label_color, id_export_cloud),
        edge(id_label_color, id_preview),
        edge(id_preview, id_roi_a),
        edge(id_preview, id_roi_b),
        edge(id_roi_a, id_crop_a),
        edge(id_roi_b, id_crop_b),
        edge(id_crop_a, id_plane_a),
        edge(id_crop_b, id_plane_b),
        edge(id_crop_a, id_sphere_a),
        edge(id_crop_b, id_sphere_b),
        edge(id_plane_a, id_stats_a),
        edge(id_plane_b, id_stats_b),
        edge(id_stats_a, id_eval),
        edge(id_stats_b, id_eval),
        edge(id_eval, id_annotate),
        edge(id_annotate, id_export_image),
        edge(id_export_cloud, id_end),
        edge(id_export_image, id_end),
        edge(id_plane_a, id_end),
        edge(id_plane_b, id_end),
        edge(id_sphere_a, id_end),
        edge(id_sphere_b, id_end),
    ]

    return {"nodes": nodes, "edges": edges}


def create_workflow(project_id: str) -> dict:
    print(f"[2/2] 创建工作流：{WORKFLOW_NAME} ...")

    payload = {
        "projectId": project_id,
        "name": WORKFLOW_NAME,
        "graphData": build_graph_data(),
    }

    if os.getenv("AURORA_VERBOSE_PAYLOAD", "").strip().lower() in {"1", "true", "yes"}:
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


def main() -> None:
    global BASE_URL, USERNAME, PASSWORD, POINT_CLOUD_PATH, WORKFLOW_NAME, VOXEL_SIZE
    global ROI_A_JSON, ROI_B_JSON, THRESHOLD_MIN, THRESHOLD_MAX, PROJECTION_BOUNDS
    global PREVIEW_WIDTH, PREVIEW_HEIGHT

    args = parse_args()

    BASE_URL = resolve_base_url(args)
    USERNAME = args.username
    PASSWORD = args.password

    PROJECT["name"] = args.project_name
    PROJECT["projectCode"] = args.project_code
    WORKFLOW_NAME = args.workflow_name
    POINT_CLOUD_PATH = args.point_cloud_path
    VOXEL_SIZE = args.voxel_size
    THRESHOLD_MIN = args.threshold_min
    THRESHOLD_MAX = args.threshold_max

    PROJECTION_BOUNDS = {
        "minX": args.projection_min_x,
        "maxX": args.projection_max_x,
        "minY": args.projection_min_y,
        "maxY": args.projection_max_y,
    }
    PREVIEW_WIDTH = args.preview_width
    PREVIEW_HEIGHT = args.preview_height

    ROI_A_JSON = validate_roi_json(args.roi_a_json, "--roi-a-json")
    ROI_B_JSON = validate_roi_json(args.roi_b_json, "--roi-b-json")

    if not PASSWORD:
        print(
            "✗ 缺少密码，请通过 --password 或 AURORA_PASSWORD 提供。", file=sys.stderr
        )
        sys.exit(2)

    print("=" * 60)
    print("  平面曲面高度差工作流自动创建")
    print(f"  目标服务：{BASE_URL}")
    print("=" * 60)

    login()
    project_id = create_project()
    workflow = create_workflow(project_id)

    print("\n=== 完成 ===")
    print(f"项目ID：{project_id}")
    print(f"工作流ID：{workflow.get('id')}")


if __name__ == "__main__":
    try:
        main()
    except requests.exceptions.HTTPError as ex:
        print("\n✗ HTTP 请求失败")
        if ex.response is not None:
            print(f"状态码：{ex.response.status_code}")
            try:
                print(
                    "响应：",
                    json.dumps(ex.response.json(), ensure_ascii=False, indent=2),
                )
            except Exception:
                print("响应文本：", ex.response.text)
        sys.exit(1)
    except Exception as ex:
        print(f"\n✗ 执行失败：{ex}")
        sys.exit(1)
