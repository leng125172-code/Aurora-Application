
import uuid
import socket
import json
from constants import (
    DEFAULT_DISCOVERY_PORT,
    DEFAULT_DISCOVERY_TIMEOUT,
    DEFAULT_SCHEME,
    DEFAULT_API_PORT,
    DEFAULT_HOST,
    PROJECTION_BOUNDS,
    IMAGE_RESOLUTION,
)


def new_uuid():
    return uuid.uuid4().hex


def compute_preview_size(bounds, long_side):
    range_x = float(bounds["maxX"]) - float(bounds["minX"])
    range_y = float(bounds["maxY"]) - float(bounds["minY"])
    if range_x <= 0:
        range_x = 1.0
    if range_y <= 0:
        range_y = 1.0
    if range_x >= range_y:
        return long_side, max(1, int(round(long_side * range_y / range_x)))
    return max(1, int(round(long_side * range_x / range_y))), long_side


PREVIEW_WIDTH, PREVIEW_HEIGHT = compute_preview_size(
    PROJECTION_BOUNDS, IMAGE_RESOLUTION
)


def _get_local_ips():
    ips = []
    try:
        for info in socket.getaddrinfo(socket.gethostname(), None, socket.AF_INET):
            ip = info[4][0]
            if isinstance(ip, str) and not ip.startswith("127."):
                ips.append(ip)
    except Exception:
        pass
    return sorted(set(ips))


def discover_server_hosts(
    discovery_port=DEFAULT_DISCOVERY_PORT,
    timeout=DEFAULT_DISCOVERY_TIMEOUT,
):
    results = []
    seen = set()

    broadcast_addrs = {"255.255.255.255"}
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
        import time

        for addr in broadcast_addrs:
            try:
                sock.sendto(req, (addr, discovery_port))
            except Exception:
                pass

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


def resolve_base_url(args):
    if args.base_url:
        return args.base_url.rstrip("/")

    import os

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


def make_properties(
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


def node(node_id, node_type, x, y, title, properties):
    return {
        "id": node_id,
        "type": node_type,
        "x": x,
        "y": y,
        "text": {"x": x, "y": y, "value": title},
        "properties": properties,
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

