"""
AppUpdateClient.py
功能：在 Windows 开发机上将发布文件部署到 RK3588。
      默认直接推送已有 Publish 目录；可选重新发布后再推送。

特性：
    - 分块上传（默认每块 8 MB），多线程并行发送
    - 每块独立 MD5 校验，上传后服务端验证
    - 合并后全文件 MD5 二次校验
    - 四阶段进度条：发布 / 压缩 / 上传 / 解压
    - remotelog / rl 自动发现设备并彩色跟随 .NET systemd 日志

用法：
    python AppUpdateClient.py [-dr | -rr]
                              [--host 10.127.135.143] [--port 9211]
                              [--projects AuroraStruct3D.HttpApi.Host,AuroraStruct3D.DbMigrator]
                              [--workers 4] [--chunk-mb 8]
    python AppUpdateClient.py remotelog [--host 10.127.135.143]
                              [--ssh-user linaro] [--service AuroraStruct3D.HttpApi.Host.service]
    python AppUpdateClient.py rl

发布模式：
    无参数  不重新发布，直接打包并推送已有 Publish 目录
    -dr     以 Debug 配置重新发布，然后推送
    -rr     以 Release 配置重新发布，然后推送

依赖：
    pip install tqdm
"""

import argparse
import concurrent.futures
import hashlib
import json
import os
import re
import shutil
import socket
import struct
import subprocess
import sys
import tarfile
import tempfile
import threading
import time
from collections import Counter
from pathlib import Path

# 自动安装 tqdm
try:
    from tqdm import tqdm
except ImportError:
    print("[提示] 正在安装 tqdm...")
    subprocess.run([sys.executable, "-m", "pip", "install", "tqdm", "-q"], check=True)
    from tqdm import tqdm  # type: ignore

# 常量
WORKSPACE_ROOT = Path(__file__).resolve().parent.parent.parent
RUNTIME = "linux-arm64"
FRAMEWORK = "net10.0"
DEFAULT_PROJECTS = [
    "AuroraStruct3D.HttpApi.Host",
    "AuroraStruct3D.DbMigrator",
]
DEFAULT_HOST = "10.127.135.143"
DEFAULT_PORT = 9211
DEFAULT_WORKERS = 4
DEFAULT_CHUNK_MB = 8
DEFAULT_SSH_USER = "linaro"
DEFAULT_SSH_PORT = 22
DEFAULT_LOG_SERVICE = "AuroraStruct3D.HttpApi.Host.service"
# UDP 发现端口 = TCP 端口 - 1
DISCOVERY_PORT = DEFAULT_PORT - 1


class _Ansi:
    RESET = "\033[0m"
    DIM = "\033[2m"
    GRAY = "\033[90m"
    RED = "\033[31m"
    BRIGHT_RED = "\033[91m"
    YELLOW = "\033[33m"
    GREEN = "\033[32m"
    CYAN = "\033[36m"
    MAGENTA = "\033[35m"


_LEVEL_RE = re.compile(
    r"\[(?P<bracket>VRB|DBG|INF|WRN|ERR|FTL)\]"
    r"|\b(?P<word>Trace|Debug|Information|Warning|Error|Critical)\b",
    re.IGNORECASE,
)
_LEVEL_ALIASES = {
    "TRACE": "VRB",
    "DEBUG": "DBG",
    "INFORMATION": "INF",
    "WARNING": "WRN",
    "ERROR": "ERR",
    "CRITICAL": "FTL",
}
_LEVEL_COLORS = {
    "VRB": _Ansi.DIM,
    "DBG": _Ansi.GRAY,
    "INF": _Ansi.GREEN,
    "WRN": _Ansi.YELLOW,
    "ERR": _Ansi.RED,
    "FTL": _Ansi.BRIGHT_RED,
}


def _color_enabled() -> bool:
    """当前终端是否支持 ANSI 颜色；NO_COLOR 环境变量可显式关闭。"""
    if "NO_COLOR" in os.environ or not sys.stdout.isatty():
        return False
    if sys.platform == "win32":
        os.system("")
    return True


def _parse_log_level(line: str) -> str | None:
    match = _LEVEL_RE.search(line)
    if not match:
        if "Exception" in line or line.lstrip().startswith("at "):
            return "ERR"
        return None
    raw = (match.group("bracket") or match.group("word")).upper()
    return _LEVEL_ALIASES.get(raw, raw)


def _format_remote_log_line(line: str, use_color: bool) -> tuple[str, str | None]:
    """解析一行 .NET 日志，返回（格式化文本，级别）。"""
    level = _parse_log_level(line)
    if not use_color:
        return line, level

    color = _LEVEL_COLORS.get(level or "", "")
    if not color:
        color = _Ansi.RED if "Exception" in line else _Ansi.RESET
    rendered = f"{color}{line}{_Ansi.RESET}"

    # 在整行级别色之上突出 HTTP 状态码和明显的慢请求。
    rendered = re.sub(
        r"(?i)(\bstatus(?:\s+code)?\s*[=:]?\s*|-\s+)([1-5]\d{2})(?=\s|$)",
        lambda m: m.group(1)
        + (
            f"{_Ansi.BRIGHT_RED}{m.group(2)}{color}"
            if m.group(2).startswith("5")
            else f"{_Ansi.YELLOW}{m.group(2)}{color}"
            if m.group(2).startswith("4")
            else f"{_Ansi.CYAN}{m.group(2)}{color}"
            if m.group(2).startswith("2")
            else m.group(2)
        ),
        rendered,
    )
    duration = re.search(r"\b(\d+(?:\.\d+)?)ms\b", line)
    if duration and float(duration.group(1)) >= 1000:
        token = duration.group(0)
        rendered = rendered.replace(token, f"{_Ansi.MAGENTA}{token}{color}", 1)
    return rendered, level


def stream_remote_logs(
    host: str,
    ssh_user: str,
    ssh_port: int,
    service: str,
    lines: int,
    follow: bool,
) -> None:
    """通过 SSH 跟随远端 systemd 日志，在本地解析并着色。"""
    ssh = shutil.which("ssh")
    if not ssh:
        print("[错误] 未找到 ssh，请安装或启用 Windows OpenSSH 客户端。", file=sys.stderr)
        sys.exit(1)
    if not re.fullmatch(r"[A-Za-z0-9_.@-]+", service):
        print(f"[错误] 非法的 systemd 服务名：{service}", file=sys.stderr)
        sys.exit(1)

    remote_command = f"journalctl -u {service} -n {lines} --no-pager -o cat"
    if follow:
        remote_command += " -f"
    target = f"{ssh_user}@{host}" if ssh_user else host
    command = [
        ssh,
        "-T",
        "-p",
        str(ssh_port),
        "-o",
        "ConnectTimeout=8",
        "-o",
        "ServerAliveInterval=15",
        target,
        remote_command,
    ]

    print("=" * 72)
    print(f"  远端设备 : {target}:{ssh_port}")
    print(f"  日志服务 : {service}")
    print(f"  日志模式 : 最近 {lines} 行" + ("，持续跟随" if follow else ""))
    print("  退出方式 : Ctrl+C")
    print("=" * 72)

    counts: Counter[str] = Counter()
    use_color = _color_enabled()
    process: subprocess.Popen[str] | None = None
    try:
        process = subprocess.Popen(
            command,
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            text=True,
            encoding="utf-8",
            errors="replace",
            bufsize=1,
        )
        assert process.stdout is not None
        for raw_line in process.stdout:
            rendered, level = _format_remote_log_line(raw_line.rstrip("\r\n"), use_color)
            if level:
                counts[level] += 1
            print(rendered, flush=True)
        return_code = process.wait()
        if return_code != 0:
            raise RuntimeError(f"ssh 退出码 {return_code}")
    except KeyboardInterrupt:
        print("\n已停止远端日志跟随。")
    finally:
        if process and process.poll() is None:
            process.terminate()
            try:
                process.wait(timeout=3)
            except subprocess.TimeoutExpired:
                process.kill()
        if counts:
            order = ("VRB", "DBG", "INF", "WRN", "ERR", "FTL")
            summary = "  ".join(f"{level}={counts[level]}" for level in order if counts[level])
            print(f"[日志统计] {summary}")


def _send_msg(sock: socket.socket, data: dict) -> None:
    payload = json.dumps(data, ensure_ascii=False).encode("utf-8")
    sock.sendall(struct.pack(">I", len(payload)) + payload)


def _recv_exactly(sock: socket.socket, n: int) -> bytes:
    buf = bytearray()
    while len(buf) < n:
        chunk = sock.recv(n - len(buf))
        if not chunk:
            raise ConnectionError("连接意外关闭")
        buf.extend(chunk)
    return bytes(buf)


def _recv_msg(sock: socket.socket) -> dict:
    raw_len = _recv_exactly(sock, 4)
    msg_len = struct.unpack(">I", raw_len)[0]
    return json.loads(_recv_exactly(sock, msg_len).decode("utf-8"))


def _make_conn(host: str, port: int, timeout: float = 30.0) -> socket.socket:
    """创建并连接到服务端，连接失败时友好提示后退出。"""
    sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    sock.settimeout(timeout)
    try:
        sock.connect((host, port))
    except (ConnectionRefusedError, TimeoutError, OSError) as exc:
        print(f"\n[错误] 无法连接 {host}:{port} — {exc}", file=sys.stderr)
        print("       请确认 AppUpdateServer.py 已在 RK3588 上运行。", file=sys.stderr)
        sys.exit(1)
    return sock


def _md5_of_bytes(data: bytes) -> str:
    return hashlib.md5(data).hexdigest()


def _md5_of_file(path: Path) -> str:
    h = hashlib.md5()
    with path.open("rb") as f:
        for block in iter(lambda: f.read(65536), b""):
            h.update(block)
    return h.hexdigest()


def _get_local_ips() -> list[str]:
    """获取本机所有 IPv4 地址（排除 127.x.x.x）。"""
    ips: list[str] = []
    try:
        for info in socket.getaddrinfo(socket.gethostname(), None, socket.AF_INET):
            ip = info[4][0]
            if not ip.startswith("127."):
                ips.append(ip)
    except Exception:
        pass
    return ips or ["192.168.1.1"]


def scan_for_servers(timeout: float = 2.0) -> list[dict]:
    """
    UDP 广播扫描局域网内的 AppUpdateServer。
    返回 [{"host": ..., "port": ..., "name": ...}, ...]。
    """
    results: list[dict] = []
    seen: set[str] = set()

    # 构建广播地址列表：255.255.255.255 + 每个本机子网广播
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
                sock.sendto(req, (addr, DISCOVERY_PORT))
            except Exception:
                pass
        deadline = time.monotonic() + timeout
        while time.monotonic() < deadline:
            try:
                data, (host, _) = sock.recvfrom(4096)
                if host in seen:
                    continue
                seen.add(host)
                resp = json.loads(data.decode("utf-8"))
                if resp.get("status") == "ok":
                    results.append(
                        {
                            "host": host,
                            "port": resp.get("port", DEFAULT_PORT),
                            "name": resp.get("name", "Unknown"),
                        }
                    )
            except socket.timeout:
                pass
            except Exception:
                pass
    finally:
        sock.close()
    return results


def publish_project(
    project_name: str, index: int, total: int, configuration: str
) -> None:
    """对指定项目执行 dotnet publish，实时输出编译日志，并按类别统计错误/警告。"""
    csproj = (
        WORKSPACE_ROOT
        / "Sources"
        / "AuroraStruct3D"
        / project_name
        / f"{project_name}.csproj"
    )
    if not csproj.exists():
        print(f"[错误] 找不到项目文件：{csproj}", file=sys.stderr)
        sys.exit(1)

    print(f"\n{'─' * 60}")
    print(f"  [发布 {index}/{total}] {project_name}")
    print(f"{'─' * 60}")
    cmd = [
        "dotnet",
        "publish",
        str(csproj),
        "-c",
        configuration,
        "-r",
        RUNTIME,
        "--self-contained",
        "false",
        "-f",
        FRAMEWORK,
        "-v",
        "minimal",
        "-p:BuildInParallel=false",
        "-p:UseSharedCompilation=false",
    ]
    print(f">>> {' '.join(cmd)}\n")

    # 同时匹配英文 "error CS1234:" / "warning CS1234:" 与中文 "错误 CS1234:" / "警告 CS1234:"
    # MSBuild 错误（MSB1234）和 NuGet 警告（NU1234）也一并捕获
    diag_re = re.compile(
        r"\b(?P<level>error|warning|错误|警告)\s+" r"(?P<code>[A-Z]{1,5}\d{3,5})\s*:",
        re.IGNORECASE,
    )
    error_codes: Counter[str] = Counter()
    warning_codes: Counter[str] = Counter()
    # 记录每个 code 的首条消息样例，便于排查
    sample_lines: dict[str, str] = {}
    # 行去重：避免同一警告在多 TFM/多次引用中被重复统计
    seen_signatures: set[tuple[str, str, str]] = set()

    proc = subprocess.Popen(
        cmd,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        text=True,
        encoding="utf-8",
        errors="replace",
        bufsize=1,
    )
    assert proc.stdout is not None
    try:
        for raw_line in proc.stdout:
            line = raw_line.rstrip()
            # 透传原始日志
            print(line)
            m = diag_re.search(line)
            if not m:
                continue
            code = m.group("code").upper()
            level = m.group("level").lower()
            is_error = level in ("error", "错误")
            # 提取诊断消息中位置（文件:行:列）做去重签名
            loc_match = re.search(r"([^\s(]+)\((\d+),(\d+)\)", line)
            signature = (
                code,
                loc_match.group(0) if loc_match else line.strip(),
                "E" if is_error else "W",
            )
            if signature in seen_signatures:
                continue
            seen_signatures.add(signature)
            if is_error:
                error_codes[code] += 1
            else:
                warning_codes[code] += 1
            sample_lines.setdefault(code, line.strip())
    finally:
        return_code = proc.wait()

    # 输出分类统计
    total_errors = sum(error_codes.values())
    total_warnings = sum(warning_codes.values())
    print(f"\n{'─' * 60}")
    print(f"  [统计] {project_name}  错误: {total_errors}  警告: {total_warnings}")
    print(f"{'─' * 60}")
    if error_codes:
        print("  错误分类：")
        for code, count in error_codes.most_common():
            print(f"    {code:<10} × {count}")
            sample = sample_lines.get(code, "")
            if sample:
                print(f"      ↳ {sample[:120]}")
    if warning_codes:
        print("  警告分类：")
        for code, count in warning_codes.most_common():
            print(f"    {code:<10} × {count}")
    if not error_codes and not warning_codes:
        print("  ✔ 无错误、无警告")

    if return_code != 0:
        print(
            f"\n[错误] dotnet publish 失败（退出码 {return_code}）",
            file=sys.stderr,
        )
        sys.exit(return_code)
    print(f"[完成] {project_name} 发布成功")


def create_archive(publish_dir: Path) -> Path:
    """
    将 publish_dir 下所有文件打包为 tar.gz。
    归档路径相对于 publish_dir，服务端解压到 ~/Publish/ 后
    得到 ~/Publish/linux-arm64/...
    返回临时文件路径（调用方负责删除）。
    """
    all_files = [f for f in publish_dir.rglob("*") if f.is_file()]
    print(f"\n{'─' * 60}")
    print(f"  [压缩] 共 {len(all_files)} 个文件")
    print(f"{'─' * 60}")

    tmp = tempfile.NamedTemporaryFile(
        suffix=".tar.gz", delete=False, prefix="aurora_publish_"
    )
    tmp.close()
    tmp_path = Path(tmp.name)

    with tarfile.open(tmp_path, mode="w:gz") as tar:
        with tqdm(
            total=len(all_files),
            desc="压缩进度",
            unit="文件",
            ncols=72,
            colour="cyan",
        ) as pbar:
            for file in all_files:
                arc_name = file.relative_to(publish_dir).as_posix()
                tar.add(file, arcname=arc_name)
                pbar.update(1)
                pbar.set_postfix_str(arc_name[-38:] if len(arc_name) > 38 else arc_name)

    size_mb = tmp_path.stat().st_size / 1024 / 1024
    print(f"[压缩] 完成，大小: {size_mb:.1f} MB")
    return tmp_path


def _upload_chunk(
    host: str,
    port: int,
    session_id: str,
    chunk_id: int,
    data: bytes,
    pbar: tqdm,
    error_event: threading.Event,
) -> None:
    """在独立 TCP 连接中上传单个分块，校验服务端 MD5 确认。"""
    if error_event.is_set():
        return

    chunk_md5 = _md5_of_bytes(data)
    sock = _make_conn(host, port, timeout=60.0)
    try:
        sock.settimeout(120.0)
        _send_msg(
            sock,
            {
                "action": "chunk",
                "session_id": session_id,
                "chunk_id": chunk_id,
                "size": len(data),
                "md5": chunk_md5,
            },
        )
        resp = _recv_msg(sock)
        if resp.get("status") != "ready":
            raise RuntimeError(f"服务端拒绝: {resp}")
        sock.sendall(data)
        ack = _recv_msg(sock)
        if ack.get("status") != "ok":
            raise RuntimeError(f"MD5 校验失败: {ack.get('message')}")
    except Exception as exc:
        error_event.set()
        raise RuntimeError(f"分块 {chunk_id} 上传失败: {exc}") from exc
    finally:
        sock.close()

    pbar.update(len(data))


def upload_and_deploy(
    host: str,
    port: int,
    archive_path: Path,
    workers: int,
    chunk_size: int,
) -> None:
    """分块并行上传压缩包，完成后触发服务端合并/校验/解压。"""
    total_size = archive_path.stat().st_size
    total_md5 = _md5_of_file(archive_path)

    offsets: list[tuple[int, int]] = []
    offset = 0
    while offset < total_size:
        sz = min(chunk_size, total_size - offset)
        offsets.append((offset, sz))
        offset += sz
    total_chunks = len(offsets)

    size_mb = total_size / 1024 / 1024
    chunk_mb = chunk_size / 1024 / 1024
    print(f"\n{'─' * 60}")
    print(
        f"  [上传] {host}:{port}  大小: {size_mb:.1f} MB  "
        f"分块: {total_chunks} × {chunk_mb:.0f} MB  线程: {workers}"
    )
    print(f"{'─' * 60}")

    # 初始化会话
    init_sock = _make_conn(host, port, timeout=30.0)
    try:
        _send_msg(
            init_sock,
            {
                "action": "init",
                "total_chunks": total_chunks,
                "total_size": total_size,
                "total_md5": total_md5,
            },
        )
        resp = _recv_msg(init_sock)
        if resp.get("status") != "ready":
            print(f"[错误] 初始化失败: {resp}", file=sys.stderr)
            sys.exit(1)
        session_id: str = resp["session_id"]
        print(f"[会话] session_id = {session_id[:8]}...")
    finally:
        init_sock.close()

    # 并行上传所有分块
    error_event = threading.Event()
    errors: list[str] = []

    with tqdm(
        total=total_size,
        desc="上传进度",
        unit="B",
        unit_scale=True,
        unit_divisor=1024,
        ncols=72,
        colour="green",
    ) as pbar:
        with concurrent.futures.ThreadPoolExecutor(max_workers=workers) as pool:
            futures: list[concurrent.futures.Future] = []
            with archive_path.open("rb") as f:
                for chunk_id, (off, sz) in enumerate(offsets):
                    f.seek(off)
                    data = f.read(sz)
                    fut = pool.submit(
                        _upload_chunk,
                        host,
                        port,
                        session_id,
                        chunk_id,
                        data,
                        pbar,
                        error_event,
                    )
                    futures.append(fut)

            for fut in concurrent.futures.as_completed(futures):
                exc = fut.exception()
                if exc:
                    errors.append(str(exc))

    if errors:
        for e in errors:
            print(f"[错误] {e}", file=sys.stderr)
        sys.exit(1)

    print(f"\n[上传] 全部 {total_chunks} 个分块完成，MD5={total_md5}")

    # 触发服务端合并 + 解压
    print(f"\n{'─' * 60}")
    print(f"  [部署] 等待服务端合并 / 校验 / 解压...")
    print(f"{'─' * 60}")

    finalize_sock = _make_conn(host, port, timeout=30.0)
    try:
        finalize_sock.settimeout(None)
        _send_msg(
            finalize_sock,
            {
                "action": "finalize",
                "session_id": session_id,
                "total_md5": total_md5,
            },
        )

        extract_pbar: tqdm | None = None
        final_status_received = False
        while True:
            try:
                msg = _recv_msg(finalize_sock)
            except ConnectionError as exc:
                if extract_pbar:
                    extract_pbar.close()
                    extract_pbar = None
                if final_status_received:
                    raise
                raise RuntimeError(
                    "服务端在返回最终状态前关闭了连接，请检查服务端部署日志。"
                ) from exc

            stage = msg.get("stage")
            status = msg.get("status")

            if status == "done":
                final_status_received = True
                if extract_pbar:
                    extract_pbar.close()
                    extract_pbar = None
                print(f"\n  ✔ {msg.get('message', '部署完成')}")
                break

            if status == "error":
                final_status_received = True
                if extract_pbar:
                    extract_pbar.close()
                    extract_pbar = None
                print(f"\n[错误] 服务端报告: {msg.get('message')}", file=sys.stderr)
                sys.exit(1)

            if stage == "merge":
                print(f"  ▶ {msg.get('message', '合并中...')}")

            elif stage == "merge_done":
                print(f"  ✔ {msg.get('message', 'MD5 校验通过')}")

            elif stage == "extract":
                total_files = msg.get("total", 0)
                current = msg.get("current", 0)
                name = msg.get("name", "")
                if extract_pbar is None:
                    extract_pbar = tqdm(
                        total=total_files,
                        desc="解压进度",
                        unit="文件",
                        ncols=72,
                        colour="yellow",
                    )
                extract_pbar.n = current
                extract_pbar.set_postfix_str(name[-38:] if len(name) > 38 else name)
                extract_pbar.refresh()

            elif stage == "extract_done":
                if extract_pbar:
                    extract_pbar.close()
                    extract_pbar = None
                print(f"\n  ✔ {msg.get('message', '解压完成')}")

            elif stage == "deploy_log":
                if extract_pbar:
                    extract_pbar.close()
                    extract_pbar = None
                print(f"  {msg.get('message', '')}")

            elif msg.get("message"):
                if extract_pbar:
                    extract_pbar.close()
                    extract_pbar = None
                print(f"  ▶ {msg.get('message')}")

    finally:
        finalize_sock.close()


def main() -> None:
    parser = argparse.ArgumentParser(
        description="发布部署到 RK3588，或查看远端 .NET 运行日志"
    )
    parser.add_argument(
        "command",
        nargs="?",
        choices=("remotelog", "rl"),
        help="remotelog/rl：自动发现设备并跟随远端 .NET 日志",
    )
    parser.add_argument(
        "--host", default="", help="RK3588 IP 地址（不指定则自动扫描局域网）"
    )
    parser.add_argument(
        "--port",
        type=int,
        default=DEFAULT_PORT,
        help=f"AppUpdateServer 端口（默认：{DEFAULT_PORT}）",
    )
    parser.add_argument(
        "--projects",
        default=",".join(DEFAULT_PROJECTS),
        help="要发布的项目名，逗号分隔",
    )
    publish_mode = parser.add_mutually_exclusive_group()
    publish_mode.add_argument(
        "-dr",
        dest="configuration",
        action="store_const",
        const="Debug",
        help="以 Debug 配置重新发布，然后推送",
    )
    publish_mode.add_argument(
        "-rr",
        dest="configuration",
        action="store_const",
        const="Release",
        help="以 Release 配置重新发布，然后推送",
    )
    publish_mode.add_argument(
        "--sync-only",
        action="store_true",
        help=argparse.SUPPRESS,
    )
    parser.add_argument(
        "--workers",
        type=int,
        default=DEFAULT_WORKERS,
        help=f"并行上传线程数（默认：{DEFAULT_WORKERS}）",
    )
    parser.add_argument(
        "--chunk-mb",
        type=int,
        default=DEFAULT_CHUNK_MB,
        help=f"每个分块大小 MB（默认：{DEFAULT_CHUNK_MB}）",
    )
    parser.add_argument(
        "--ssh-user",
        default=DEFAULT_SSH_USER,
        help=f"远端 SSH 用户（默认：{DEFAULT_SSH_USER}）",
    )
    parser.add_argument(
        "--ssh-port",
        type=int,
        default=DEFAULT_SSH_PORT,
        help=f"远端 SSH 端口（默认：{DEFAULT_SSH_PORT}）",
    )
    parser.add_argument(
        "--service",
        default=DEFAULT_LOG_SERVICE,
        help=f"systemd 服务名（默认：{DEFAULT_LOG_SERVICE}）",
    )
    parser.add_argument(
        "--lines",
        type=int,
        default=200,
        help="首次读取的历史日志行数（默认：200）",
    )
    parser.add_argument(
        "--no-follow",
        action="store_true",
        help="仅读取历史日志，不持续跟随",
    )
    args = parser.parse_args()

    if args.lines < 0:
        parser.error("--lines 不能小于 0")
    if not 1 <= args.ssh_port <= 65535:
        parser.error("--ssh-port 必须在 1..65535 之间")

    # 自动发现服务端
    host = args.host.strip()
    port = args.port
    if not host:
        print(f"正在扫描局域网（UDP 广播，最多 2 秒）...")
        servers = scan_for_servers(timeout=2.0)
        if not servers:
            print(
                f"[错误] 未找到任何 AppUpdateServer，请用 --host 手动指定 IP",
                file=sys.stderr,
            )
            sys.exit(1)
        if len(servers) == 1:
            host = servers[0]["host"]
            port = servers[0]["port"]
            print(f"自动选择服务器: {servers[0]['name']}  {host}:{port}")
        else:
            print("\n发现多个服务器，请选择:")
            for i, s in enumerate(servers, 1):
                print(f"  [{i}] {s['name']}  {s['host']}:{s['port']}")
            while True:
                try:
                    choice = int(input("\n输入编号：").strip())
                    if 1 <= choice <= len(servers):
                        host = servers[choice - 1]["host"]
                        port = servers[choice - 1]["port"]
                        break
                except (ValueError, KeyboardInterrupt):
                    pass

    if args.command in ("remotelog", "rl"):
        try:
            stream_remote_logs(
                host=host,
                ssh_user=args.ssh_user.strip(),
                ssh_port=args.ssh_port,
                service=args.service.strip(),
                lines=args.lines,
                follow=not args.no_follow,
            )
        except RuntimeError as exc:
            print(f"[错误] 远端日志读取失败：{exc}", file=sys.stderr)
            sys.exit(1)
        return

    projects = [p.strip() for p in args.projects.split(",") if p.strip()]
    publish_dir = WORKSPACE_ROOT / "Publish"
    chunk_size = args.chunk_mb * 1024 * 1024

    print("=" * 60)
    print(f"  目标服务器 : {host}:{port}")
    if args.configuration is None:
        print("  模式       : 直接推送（不执行 dotnet publish）")
    else:
        print(f"  模式       : {args.configuration} 重新发布并推送")
        print(f"  发布项目   : {', '.join(projects)}")
        print(
            f"  运行时     : {RUNTIME} / {FRAMEWORK} / "
            f"{args.configuration} / framework-dependent"
        )
    print(f"  上传线程   : {args.workers}  分块大小: {args.chunk_mb} MB")
    print("=" * 60)

    if args.configuration is not None:
        for idx, project in enumerate(projects, start=1):
            publish_project(
                project,
                idx,
                len(projects),
                args.configuration,
            )

    if not publish_dir.exists():
        print(f"[错误] Publish 目录不存在：{publish_dir}", file=sys.stderr)
        sys.exit(1)

    archive_path = create_archive(publish_dir)
    try:
        upload_and_deploy(host, port, archive_path, args.workers, chunk_size)
    finally:
        archive_path.unlink(missing_ok=True)

    print("\n" + "=" * 60)
    print("  所有步骤完成！")
    print("=" * 60)


if __name__ == "__main__":
    main()
