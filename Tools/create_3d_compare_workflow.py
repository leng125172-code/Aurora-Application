"""
3D 比较工作流自动创建脚本。

功能：
  1. 在指定服务上创建检测项目
  2. 创建“真实扫描 + 模型库模型”或“固定 PLY + 模型库模型”的3D比较检测工作流

工作流流程：
  生产3D扫描 / 读取测试 PLY ─┐
                             ├→ 3D比较（配准+距离计算）→ 距离热力图 + 结果统计
  读取工艺模型 ──────────────┘

用法：
    python create_3d_compare_workflow.py --calib-project-id <标定项目ID> --product-model-id <工艺模型ID>
    python create_3d_compare_workflow.py --input-ply <本地PLY路径> --product-model-id <工艺模型ID>
    python create_3d_compare_workflow.py --host 10.127.135.143 --api-port 5000 --calib-project-id <标定项目ID> --product-model-id <工艺模型ID>

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

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
if hasattr(sys.stderr, "reconfigure"):
    sys.stderr.reconfigure(encoding="utf-8", errors="replace")

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

IMAGE_RESOLUTION = 512

REGISTRATION_METHOD = "point_to_plane"
# 模型与扫描点云通常不在同一初始姿态；默认先粗配准，避免 ICP
# 因旋转角度或平移差异较大而落入错误的局部最优。
USE_COARSE_REGISTRATION = True
MAX_ITERATIONS = 50
MAX_CORRESPONDENCE_DISTANCE = 0.0
DISTANCE_THRESHOLD = 0.05
VOXEL_SIZE = 0.0
MAX_DEFECT_RATIO = 0.05
MAX_MEAN_DISTANCE = 0.05
MAX_MISSING_RATIO = 0.05
# 生产扫描只覆盖相机可见表面。保留反向缺失率作为诊断信息，但默认不把
# 数模中未被相机看到的底面/侧面判为产品缺失。
CHECK_MISSING_SURFACE = False
MIN_COARSE_INLIER_RATIO = 0.1
MIN_FINE_CORRESPONDENCE_RATIO = 0.15
MAX_REGISTRATION_RMSE = 0.0

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
    source_group = parser.add_mutually_exclusive_group(required=True)
    source_group.add_argument("--calib-project-id", help="真实扫描使用的标定项目 ID")
    source_group.add_argument(
        "--input-ply",
        help="跳过拍照，上传并固定使用本地 PLY 点云文件",
    )
    reference_group = parser.add_mutually_exclusive_group(required=True)
    reference_group.add_argument("--product-model-id", help="模型库中已就绪的完整工艺模型 ID")
    reference_group.add_argument(
        "--reference-ply",
        help="合格品局部/顶面参考 PLY；适合固定相机与治具下的局部扫描",
    )
    parser.add_argument("--scan-cycles", type=int, default=1, help="每次运行的扫描周期数（1-20）")
    parser.add_argument("--scan-timeout", type=int, default=300, help="扫描超时秒数（10-3600）")
    parser.add_argument("--no-table-filter", dest="enable_table_filter", action="store_false")
    parser.add_argument("--table-clearance-mm", type=float, default=3.0)
    parser.set_defaults(enable_table_filter=True)
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
    parser.add_argument("--max-defect-ratio", type=float, default=MAX_DEFECT_RATIO)
    parser.add_argument("--max-mean-distance", type=float, default=MAX_MEAN_DISTANCE)
    parser.add_argument("--max-missing-ratio", type=float, default=MAX_MISSING_RATIO)
    missing_group = parser.add_mutually_exclusive_group()
    missing_group.add_argument(
        "--check-missing-surface",
        dest="check_missing_surface",
        action="store_true",
        help="完整扫描时启用模型反向缺失面判定",
    )
    missing_group.add_argument(
        "--partial-surface",
        dest="check_missing_surface",
        action="store_false",
        help="局部/顶面扫描：不把未扫描的模型表面判为缺失（默认）",
    )
    parser.set_defaults(check_missing_surface=CHECK_MISSING_SURFACE)
    parser.add_argument("--min-coarse-inlier-ratio", type=float, default=MIN_COARSE_INLIER_RATIO)
    parser.add_argument("--min-fine-correspondence-ratio", type=float, default=MIN_FINE_CORRESPONDENCE_RATIO)
    parser.add_argument("--max-registration-rmse", type=float, default=MAX_REGISTRATION_RMSE)
    args = parser.parse_args()
    uuid_options = []
    if args.product_model_id:
        uuid_options.append(("--product-model-id", args.product_model_id))
    if args.calib_project_id:
        uuid_options.append(("--calib-project-id", args.calib_project_id))
    for option, value in uuid_options:
        try:
            if uuid.UUID(value).int == 0:
                raise ValueError
        except (ValueError, AttributeError):
            parser.error(f"{option} 必须是非空 UUID")

    if args.input_ply:
        args.input_ply = os.path.abspath(args.input_ply)
        if not os.path.isfile(args.input_ply):
            parser.error(f"--input-ply 文件不存在：{args.input_ply}")
        if os.path.splitext(args.input_ply)[1].lower() != ".ply":
            parser.error("--input-ply 当前只接受 .ply 文件")
    if args.reference_ply:
        args.reference_ply = os.path.abspath(args.reference_ply)
        if not os.path.isfile(args.reference_ply):
            parser.error(f"--reference-ply 文件不存在：{args.reference_ply}")
        if os.path.splitext(args.reference_ply)[1].lower() != ".ply":
            parser.error("--reference-ply 当前只接受 .ply 文件")

    if not 1 <= args.scan_cycles <= 20:
        parser.error("--scan-cycles 必须在 1 到 20 之间")
    if not 10 <= args.scan_timeout <= 3600:
        parser.error("--scan-timeout 必须在 10 到 3600 之间")
    if not 0 <= args.table_clearance_mm <= 50:
        parser.error("--table-clearance-mm 必须在 0 到 50 之间")
    for option, value in (
        ("--max-defect-ratio", args.max_defect_ratio),
        ("--max-missing-ratio", args.max_missing_ratio),
        ("--min-coarse-inlier-ratio", args.min_coarse_inlier_ratio),
        ("--min-fine-correspondence-ratio", args.min_fine_correspondence_ratio),
    ):
        if not 0 <= value <= 1:
            parser.error(f"{option} 必须在 0 到 1 之间")
    if args.min_fine_correspondence_ratio == 0:
        parser.error("--min-fine-correspondence-ratio 必须大于 0")
    if args.max_mean_distance <= 0:
        parser.error("--max-mean-distance 必须大于 0")
    if args.max_registration_rmse < 0:
        parser.error("--max-registration-rmse 不能小于 0")
    return args


OP_SCAN_POINT_CLOUD = "3a15ec1c-6978-4b72-9e6a-e749387b3f11"
OP_READ_POINT_CLOUD = "a1b2c3d4-e5f6-7890-abcd-ef1234567890"
OP_READ_PRODUCT_MODEL = "ec67f5da-e934-4e0e-9eb5-46e7f8e02101"
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
    # Aurora 服务通过局域网直连；系统 HTTP 代理常会拦截私网地址并导致超时。
    _session.trust_env = False
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
    return resp.json() if resp.content else {}


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
    return resp.json() if resp.content else {}


def upload_operator_file(project_id: str, operator_id: str, file_path: str) -> dict:
    session = _get_session()
    url = f"{BASE_URL}/api/app/operator-file/upload"
    file_name = os.path.basename(file_path)
    file_size = os.path.getsize(file_path)
    print(f"      上传测试点云：{file_name} ({file_size} bytes) ...")
    with open(file_path, "rb") as stream:
        resp = session.post(
            url,
            params={"projectId": project_id, "operatorId": operator_id},
            files={"file": (file_name, stream, "application/octet-stream")},
            timeout=300,
        )
    resp.raise_for_status()
    result = resp.json()
    if not result.get("success") or not result.get("blobName"):
        raise RuntimeError(f"测试点云上传响应无效：{result}")
    print(f"      ✓ 测试点云上传成功，BlobName：{result['blobName']}")
    return result


def confirm_operator_file(project_id: str, operator_id: str, blob_name: str) -> None:
    api_post(
        "/api/app/operator-file/confirm",
        {
            "projectId": project_id,
            "operatorId": operator_id,
            "blobName": blob_name,
        },
    )
    print("      ✓ 测试点云已绑定到工作流")


def validate_product_model(product_model_id: str) -> dict:
    model = api_get(f"/api/app/product-model/{product_model_id}")
    if not model.get("isReady"):
        status = model.get("conversionStatus")
        error = model.get("conversionErrorMessage") or "无"
        raise RuntimeError(
            f"工艺模型尚未就绪：conversionStatus={status}，error={error}"
        )
    print(
        f"✓ 工艺模型已就绪：{model.get('name') or product_model_id} "
        f"({model.get('originalFileName') or '未知文件'})"
    )
    return model


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


def build_graph_data(
    args: argparse.Namespace,
    input_point_cloud_blob: str | None = None,
    reference_point_cloud_blob: str | None = None,
) -> dict:
    id_start = new_uuid()
    id_scan = new_uuid()
    id_read_model = new_uuid()
    id_compare = new_uuid()
    id_preview = new_uuid()
    id_export_cloud = new_uuid()
    id_export_image = new_uuid()
    id_export_anomaly = new_uuid()
    id_end = new_uuid()

    if input_point_cloud_blob:
        input_cloud_node = node(
            id_scan,
            OP_READ_POINT_CLOUD,
            380,
            120,
            "读取测试点云（跳过拍照）",
            make_properties(
                input_bindings={"point_cloud_path": input_point_cloud_blob},
                input_sources={"point_cloud_path": "literal"},
                output_bindings={"output_point_cloud": "scanned_cloud"},
                output_sources={"output_point_cloud": "variable"},
            ),
        )
    else:
        input_cloud_node = node(
            id_scan,
            OP_SCAN_POINT_CLOUD,
            380,
            120,
            "生产3D扫描",
            make_properties(
                params={
                    "calibProjectId": args.calib_project_id,
                    "cycleCount": args.scan_cycles,
                    "timeoutSeconds": args.scan_timeout,
                    "enableTableFilter": args.enable_table_filter,
                    "tableClearanceMm": args.table_clearance_mm,
                },
                param_sources={name: "literal" for name in (
                    "calibProjectId", "cycleCount", "timeoutSeconds",
                    "enableTableFilter", "tableClearanceMm",
                )},
                output_bindings={
                    "output_point_cloud": "scanned_cloud",
                    "point_count": "scan_point_count",
                    "completed_cycles": "scan_completed_cycles",
                    "duration_ms": "scan_duration_ms",
                },
                output_sources={name: "variable" for name in (
                    "output_point_cloud", "point_count", "completed_cycles", "duration_ms",
                )},
            ),
        )

    end_input_bindings = {
        "alignedPointCloudUrl": "aligned_cloud_download_url",
        "distanceImageUrl": "distance_image_download_url",
        "anomalyImageUrl": "anomaly_image_download_url",
        "inspectionResult": "inspection_result",
    }
    if not reference_point_cloud_blob:
        end_input_bindings["modelId"] = "selected_model_id"
    if not input_point_cloud_blob:
        end_input_bindings.update(
            {
                "scanPointCount": "scan_point_count",
                "scanCompletedCycles": "scan_completed_cycles",
                "scanDurationMs": "scan_duration_ms",
            }
        )
    end_input_sources = {name: "variable" for name in end_input_bindings}

    if reference_point_cloud_blob:
        reference_cloud_node = node(
            id_read_model,
            OP_READ_POINT_CLOUD,
            740,
            120,
            "读取合格顶面参考",
            make_properties(
                input_bindings={"point_cloud_path": reference_point_cloud_blob},
                input_sources={"point_cloud_path": "literal"},
                output_bindings={"output_point_cloud": "model_cloud"},
                output_sources={"output_point_cloud": "variable"},
            ),
        )
    else:
        reference_cloud_node = node(
            id_read_model,
            OP_READ_PRODUCT_MODEL,
            740,
            120,
            "读取工艺模型",
            make_properties(
                params={"productModelId": args.product_model_id},
                param_sources={"productModelId": "literal"},
                output_bindings={
                    "model_point_cloud": "model_cloud",
                    "model_id": "selected_model_id",
                },
                output_sources={
                    "model_point_cloud": "variable",
                    "model_id": "variable",
                },
            ),
        )

    nodes = [
        node(id_start, "start-node", 560, 40, "开始", make_properties()),
        input_cloud_node,
        reference_cloud_node,
        node(
            id_compare,
            OP_CLOUD_COMPARE,
            560,
            240,
            "3D比较",
            make_properties(
                params={
                    # 同一相机/治具生成的合格参考云与生产扫描已在同一坐标系，
                    # 直接精配准比局部面到完整模型的全局粗配准更可靠。
                    "useCoarseRegistration": str(
                        args.use_coarse and not reference_point_cloud_blob
                    ).lower(),
                    "registrationMethod": args.registration_method,
                    "maxIterations": args.max_iterations,
                    "maxCorrespondenceDistance": args.max_correspondence_distance,
                    "distanceThreshold": args.distance_threshold,
                    "imageResolution": IMAGE_RESOLUTION,
                    "voxelSize": args.voxel_size,
                    "maxDefectRatio": args.max_defect_ratio,
                    "maxMeanDistance": args.max_mean_distance,
                    "maxMissingRatio": args.max_missing_ratio,
                    # 合格顶面参考与扫描覆盖范围一致，可以正常检查该可见面的缺失；
                    # 完整数模 + 局部扫描时才需要关闭反向缺失判定。
                    "checkMissingSurface": str(
                        True if reference_point_cloud_blob else args.check_missing_surface
                    ).lower(),
                    "minCoarseInlierRatio": args.min_coarse_inlier_ratio,
                    "minFineCorrespondenceRatio": args.min_fine_correspondence_ratio,
                    "maxRegistrationRmse": args.max_registration_rmse,
                },
                param_sources={
                    "useCoarseRegistration": "literal",
                    "registrationMethod": "literal",
                    "maxIterations": "literal",
                    "maxCorrespondenceDistance": "literal",
                    "distanceThreshold": "literal",
                    "imageResolution": "literal",
                    "voxelSize": "literal",
                    "maxDefectRatio": "literal",
                    "maxMeanDistance": "literal",
                    "maxMissingRatio": "literal",
                    "checkMissingSurface": "literal",
                    "minCoarseInlierRatio": "literal",
                    "minFineCorrespondenceRatio": "literal",
                    "maxRegistrationRmse": "literal",
                },
                input_bindings={
                    "source_cloud": "scanned_cloud",
                    "target_cloud": "model_cloud",
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
                    "anomaly_image": "anomaly_image",
                    "result": "inspection_result",
                },
                output_sources={
                    "aligned_cloud": "variable",
                    "transform_matrix": "variable",
                    "distance_mat": "variable",
                    "distance_image": "variable",
                    "anomaly_image": "variable",
                    "result": "variable",
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
                output_bindings={
                    "blob_name": "aligned_cloud_blob_name",
                    "download_url": "aligned_cloud_download_url",
                },
                output_sources={
                    "blob_name": "variable",
                    "download_url": "variable",
                },
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
                output_bindings={
                    "blob_name": "distance_image_blob_name",
                    "download_url": "distance_image_download_url",
                },
                output_sources={
                    "blob_name": "variable",
                    "download_url": "variable",
                },
            ),
        ),
        node(
            id_end,
            "end-node",
            560,
            520,
            "结束",
            make_properties(
                input_bindings=end_input_bindings,
                input_sources=end_input_sources,
            ),
        ),
        node(
            id_export_anomaly,
            OP_SAVE_IMAGE_BLOB,
            900,
            400,
            "异常标注图存Blob",
            make_properties(
                params={"fileName": "3d-compare-anomaly.png"},
                param_sources={"fileName": "literal"},
                input_bindings={"input_mat": "anomaly_image"},
                input_sources={"input_mat": "variable"},
                output_bindings={
                    "blob_name": "anomaly_image_blob_name",
                    "download_url": "anomaly_image_download_url",
                },
                output_sources={
                    "blob_name": "variable",
                    "download_url": "variable",
                },
            ),
        ),
    ]

    edges = [
        edge(id_start, id_scan),
        edge(id_start, id_read_model),
        edge(id_scan, id_compare),
        edge(id_read_model, id_compare),
        edge(id_compare, id_preview),
        edge(id_compare, id_export_cloud),
        edge(id_compare, id_export_image),
        edge(id_compare, id_export_anomaly),
        edge(id_preview, id_end),
        edge(id_export_cloud, id_end),
        edge(id_export_image, id_end),
        edge(id_export_anomaly, id_end),
    ]

    return {"nodes": nodes, "edges": edges}


def create_workflow(
    project_id: str,
    args: argparse.Namespace,
    input_point_cloud_blob: str | None = None,
    reference_point_cloud_blob: str | None = None,
) -> dict:
    print(f"[2/2] 创建工作流：{WORKFLOW_NAME} ...")

    graph_data = build_graph_data(
        args,
        input_point_cloud_blob,
        reference_point_cloud_blob,
    )
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
    if args.input_ply:
        print(f"  测试点云：{args.input_ply}")
        print("  输入模式：固定 PLY（跳过拍照）")
    else:
        print(f"  标定项目：{args.calib_project_id}")
        print(f"  扫描周期：{args.scan_cycles}")
    if args.reference_ply:
        print(f"  合格顶面参考：{args.reference_ply}")
    else:
        print(f"  工艺模型：{args.product_model_id}")
    print(f"  配准方法：{args.registration_method}")
    print(f"  启用粗配准：{args.use_coarse}")
    print("=" * 60)
    print()

    try:
        login()
        print()
        if args.product_model_id:
            validate_product_model(args.product_model_id)
            print()
        project_id = create_project()
        uploaded_point_cloud = None
        uploaded_reference_cloud = None
        if args.input_ply:
            uploaded_point_cloud = upload_operator_file(
                project_id,
                OP_READ_POINT_CLOUD,
                args.input_ply,
            )
            print()
        if args.reference_ply:
            if args.input_ply and os.path.samefile(args.input_ply, args.reference_ply):
                uploaded_reference_cloud = uploaded_point_cloud
            else:
                uploaded_reference_cloud = upload_operator_file(
                    project_id,
                    OP_READ_POINT_CLOUD,
                    args.reference_ply,
                )
                print()
        workflow = create_workflow(
            project_id,
            args,
            uploaded_point_cloud["blobName"] if uploaded_point_cloud else None,
            uploaded_reference_cloud["blobName"] if uploaded_reference_cloud else None,
        )
        if uploaded_point_cloud:
            confirm_operator_file(
                project_id,
                OP_READ_POINT_CLOUD,
                uploaded_point_cloud["blobName"],
            )
        if uploaded_reference_cloud and uploaded_reference_cloud is not uploaded_point_cloud:
            confirm_operator_file(
                project_id,
                OP_READ_POINT_CLOUD,
                uploaded_reference_cloud["blobName"],
            )
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
