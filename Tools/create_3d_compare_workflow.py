"""
3D 比较工作流自动创建脚本。

功能：
  1. 在指定服务上创建检测项目
  2. 为该项目创建3D比较检测工作流

工作流流程：
  读取参考点云 → 读取待检测点云 → 3D比较（配准+距离计算）
                                          ↓
                                   距离热力图 + 结果统计

用法：
    python create_3d_compare_workflow.py
    python create_3d_compare_workflow.py --host 10.127.135.143 --api-port 5000
    python create_3d_compare_workflow.py --reference-cloud /data/pointclouds/reference.ply --target-cloud /data/pointclouds/target.ply

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

DEFAULT_SCHEME = "http"
DEFAULT_API_PORT = 5000
DEFAULT_HOST = "10.127.135.143"
DEFAULT_DISCOVERY_PORT = 9210
DEFAULT_DISCOVERY_TIMEOUT = 1.5
BASE_URL = ""
USERNAME = ""
PASSWORD = ""

PROJECT = {
    "projectCode": "PRJ-3D-COMPARE-001",
    "name": "3D比较检测项目",
    "version": "1.0.0",
    "description": "3D点云比较检测流程——导入合格品3D数模与被检查产品做比较，输出高度差值和位置差值",
}

WORKFLOW_NAME = "3D比较检测"

REFERENCE_CLOUD_PATH = "/data/pointclouds/reference.ply"
TARGET_CLOUD_PATH = "/data/pointclouds/target.ply"

PROJECTION_BOUNDS = {"minX": -80.0, "maxX": 80.0, "minY": -60.0, "maxY": 60.0}
IMAGE_RESOLUTION = 512

REGISTRATION_METHOD = "point_to_plane"
# 模型与扫描点云通常不在同一初始姿态；默认先粗配准，避免 ICP
# 因旋转角度或平移差异较大而落入错误的局部最优。
USE_COARSE_REGISTRATION = True
MAX_ITERATIONS = 50
MAX_CORRESPONDENCE_DISTANCE = 0.1
DISTANCE_THRESHOLD = 0.05
VOXEL_SIZE = 0.2

_access_token = None
_session = None


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
    parser = argparse.ArgumentParser(description="3D比较检测工作流自动创建")
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
    parser.add_argument("--reference-cloud", default=REFERENCE_CLOUD_PATH)
    parser.add_argument("--target-cloud", default=TARGET_CLOUD_PATH)
    parser.add_argument("--workflow-name", default=WORKFLOW_NAME)
    parser.add_argument("--project-name", default=PROJECT["name"])
    parser.add_argument("--project-code", default=PROJECT["projectCode"])
    parser.add_argument("--registration-method", default=REGISTRATION_METHOD)
    coarse_group = parser.add_mutually_exclusive_group()
    coarse_group.add_argument(
        "--use-coarse",
        dest="use_coarse",
        action="store_true",
        help="启用粗配准（默认），用于模型与扫描点云初始角度不一致的情况",
    )
    coarse_group.add_argument(
        "--no-coarse",
        dest="use_coarse",
        action="store_false",
        help="关闭粗配准，仅在两片点云初始姿态已基本一致时使用",
    )
    parser.set_defaults(use_coarse=USE_COARSE_REGISTRATION)
    parser.add_argument("--max-iterations", type=int, default=MAX_ITERATIONS)
    parser.add_argument("--max-correspondence-distance", type=float, default=MAX_CORRESPONDENCE_DISTANCE)
    parser.add_argument("--distance-threshold", type=float, default=DISTANCE_THRESHOLD)
    parser.add_argument("--voxel-size", type=float, default=VOXEL_SIZE)
    return parser.parse_args()


OP_READ_POINT_CLOUD = "a1b2c3d4-e5f6-7890-abcd-ef1234567890"
OP_CLOUD_COMPARE = "a1b2c3d4-0003-4000-8000-000000000015"
OP_COLORED_CLOUD_TO_IMAGE = "1dd66555-a882-436b-bf30-dab8c680a201"
OP_SAVE_POINT_CLOUD_BLOB = "a1b2c3d4-e5f6-7890-abcd-ef1234567891"
OP_SAVE_IMAGE_BLOB = "4b8af0f5-c0fb-45df-97a6-5ab2462cfd01"


def new_uuid() -> str:
    return uuid.uuid4().hex


def _get_session() -> requests.Session:
    global _session
    if _session is not None:
        return _session

    _session = requests.Session()
    config_url = f"{BASE_URL}/api/abp/application-configuration"
    resp = _session.get(config_url, timeout=30)
    resp.raise_for_status()

    for cookie in _session.cookies:
        if cookie.name.startswith(".AspNetCore.Antiforgery"):
            if cookie.value:
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
    payload = {
        "name": USERNAME,
        "password": PASSWORD,
    }
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
    global _access_token, _session
    if not _access_token:
        raise RuntimeError("未登录，无法发送请求")
    session = _get_session()
    url = f"{BASE_URL}{path}"
    resp = session.post(url, json=body, timeout=30)
    resp.raise_for_status()
    return resp.json()


def api_get(path: str, params: dict | None = None) -> dict:
    global _access_token, _session
    if not _access_token:
        raise RuntimeError("未登录，无法发送请求")
    session = _get_session()
    url = f"{BASE_URL}{path}"
    resp = session.get(url, params=params, timeout=30)
    resp.raise_for_status()
    return resp.json()


def api_put(path: str, body: dict) -> dict:
    global _access_token, _session
    if not _access_token:
        raise RuntimeError("未登录，无法发送请求")
    session = _get_session()
    url = f"{BASE_URL}{path}"
    resp = session.put(url, json=body, timeout=30)
    resp.raise_for_status()
    return resp.json()


def find_project_by_code(project_code: str) -> dict | None:
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

    if len(items) == 1:
        return items[0]

    return None


def try_find_project_by_code(project_code: str) -> dict | None:
    try:
        return find_project_by_code(project_code)
    except requests.exceptions.RequestException as ex:
        print(f"      ! 查询已有项目失败，将继续创建：{ex}")
        return None


def build_fallback_project_code(base_code: str) -> str:
    suffix = uuid.uuid4().hex[:6].upper()
    return f"{base_code}-{suffix}"


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
            print(f"      ✓ 创建失败后命中已有项目，自动复用 ID：{project_id}")
            return project_id

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

        raise post_err


def find_workflow_by_name(project_id: str, workflow_name: str) -> dict | None:
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


def build_graph_data(reference_cloud_path: str, target_cloud_path: str, args) -> dict:
    id_start = new_uuid()
    id_read_reference = new_uuid()
    id_read_target = new_uuid()
    id_compare = new_uuid()
    id_preview = new_uuid()
    id_export_cloud = new_uuid()
    id_export_image = new_uuid()
    id_end = new_uuid()

    nodes = [
        node(id_start, "start-node", 560, 40, "开始", make_properties()),
        node(
            id_read_reference,
            OP_READ_POINT_CLOUD,
            380,
            120,
            "读取参考点云",
            make_properties(
                input_bindings={"point_cloud_path": reference_cloud_path},
                input_sources={"point_cloud_path": "literal"},
                output_bindings={"output_point_cloud": "reference_cloud"},
                output_sources={"output_point_cloud": "variable"},
            ),
        ),
        node(
            id_read_target,
            OP_READ_POINT_CLOUD,
            740,
            120,
            "读取待检测点云",
            make_properties(
                input_bindings={"point_cloud_path": target_cloud_path},
                input_sources={"point_cloud_path": "literal"},
                output_bindings={"output_point_cloud": "target_cloud"},
                output_sources={"output_point_cloud": "variable"},
            ),
        ),
        node(
            id_compare,
            OP_CLOUD_COMPARE,
            560,
            240,
            "3D比较",
            make_properties(
                params={
                    "useCoarseRegistration": str(args.use_coarse).lower(),
                    "registrationMethod": args.registration_method,
                    "maxIterations": args.max_iterations,
                    "maxCorrespondenceDistance": args.max_correspondence_distance,
                    "distanceThreshold": args.distance_threshold,
                    "imageResolution": IMAGE_RESOLUTION,
                    "voxelSize": args.voxel_size,
                },
                param_sources={
                    "useCoarseRegistration": "literal",
                    "registrationMethod": "literal",
                    "maxIterations": "literal",
                    "maxCorrespondenceDistance": "literal",
                    "distanceThreshold": "literal",
                    "imageResolution": "literal",
                    "voxelSize": "literal",
                },
                input_bindings={
                    "source_cloud": "target_cloud",
                    "target_cloud": "reference_cloud",
                },
                input_sources={
                    "source_cloud": "variable",
                    "target_cloud": "variable",
                },
                output_bindings={
                    "aligned_cloud": "aligned_cloud",
                    "transform_matrix": "transform_matrix",
                    "distance_mat": "distance_mat",
                    "distance_image": "distance_image",
                    "result_json": "result_json",
                },
                output_sources={
                    "aligned_cloud": "variable",
                    "transform_matrix": "variable",
                    "distance_mat": "variable",
                    "distance_image": "variable",
                    "result_json": "variable",
                },
            ),
        ),
        node(
            id_preview,
            OP_COLORED_CLOUD_TO_IMAGE,
            380,
            400,
            "配准点云可视化",
            make_properties(
                params={
                    "autoBounds": True,
                    "imageResolution": IMAGE_RESOLUTION,
                },
                param_sources={
                    "autoBounds": "literal",
                    "imageResolution": "literal",
                },
                input_bindings={"input_point_cloud": "aligned_cloud"},
                input_sources={"input_point_cloud": "variable"},
                output_bindings={
                    "output_image": "aligned_image",
                    "projection_mapping": "projection_mapping",
                },
                output_sources={
                    "output_image": "variable",
                    "projection_mapping": "variable",
                },
            ),
        ),
        node(
            id_export_cloud,
            OP_SAVE_POINT_CLOUD_BLOB,
            560,
            400,
            "配准点云存Blob",
            make_properties(
                params={"fileName": "3d-compare-aligned.ply"},
                param_sources={"fileName": "literal"},
                input_bindings={"input_point_cloud": "aligned_cloud"},
                input_sources={"input_point_cloud": "variable"},
                output_bindings={"download_url": "aligned_cloud_download_url"},
                output_sources={"download_url": "variable"},
            ),
        ),
        node(
            id_export_image,
            OP_SAVE_IMAGE_BLOB,
            740,
            400,
            "距离图像存Blob",
            make_properties(
                params={"fileName": "3d-compare-distance.png"},
                param_sources={"fileName": "literal"},
                input_bindings={"input_mat": "distance_image"},
                input_sources={"input_mat": "variable"},
                output_bindings={"download_url": "distance_image_download_url"},
                output_sources={"download_url": "variable"},
            ),
        ),
        node(
            id_end,
            "end-node",
            560,
            520,
            "结束",
            make_properties(
                input_bindings={
                    "alignedPointCloudUrl": "aligned_cloud_download_url",
                    "distanceImageUrl": "distance_image_download_url",
                    "resultJson": "result_json",
                },
                input_sources={
                    "alignedPointCloudUrl": "variable",
                    "distanceImageUrl": "variable",
                    "resultJson": "variable",
                },
            ),
        ),
    ]

    edges = [
        edge(id_start, id_read_reference),
        edge(id_start, id_read_target),
        edge(id_read_reference, id_compare),
        edge(id_read_target, id_compare),
        edge(id_compare, id_preview),
        edge(id_compare, id_export_cloud),
        edge(id_compare, id_export_image),
        edge(id_preview, id_end),
        edge(id_export_cloud, id_end),
        edge(id_export_image, id_end),
    ]

    return {"nodes": nodes, "edges": edges}


def create_workflow(project_id: str, reference_cloud_path: str, target_cloud_path: str, args) -> dict:
    print(f"[2/2] 创建工作流：{WORKFLOW_NAME} ...")

    graph_data = build_graph_data(reference_cloud_path, target_cloud_path, args)
    payload = {
        "projectId": project_id,
        "name": WORKFLOW_NAME,
        "graphData": graph_data,
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


def main():
    global BASE_URL, USERNAME, PASSWORD, PROJECT, WORKFLOW_NAME

    args = parse_args()
    BASE_URL = resolve_base_url(args)
    USERNAME = args.username
    PASSWORD = args.password
    PROJECT["name"] = args.project_name
    PROJECT["projectCode"] = args.project_code
    WORKFLOW_NAME = args.workflow_name

    if not PASSWORD:
        print(
            "✗ 缺少密码，请通过 --password 或 AURORA_PASSWORD 提供。", file=sys.stderr
        )
        sys.exit(2)

    print("=" * 60)
    print("  3D比较检测工作流自动创建")
    print(f"  目标服务：{BASE_URL}")
    print(f"  登录用户：{USERNAME}")
    print(f"  参考点云：{args.reference_cloud}")
    print(f"  待检测点云：{args.target_cloud}")
    print(f"  配准方法：{args.registration_method}")
    print(f"  启用粗配准：{args.use_coarse}")
    print("=" * 60)
    print()

    try:
        login()
        print()
        project_id = create_project()
        workflow = create_workflow(project_id, args.reference_cloud, args.target_cloud, args)
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
