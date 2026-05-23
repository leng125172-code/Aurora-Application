"""
AppUpdateServer.py
功能：运行在 RK3588 上的更新接收服务（端口 9211）。
      支持分块并行上传、逐块 MD5 校验、合并后全文件 MD5 校验、解压部署。

协议（JSON + 4 字节长度前缀）：
    init    → 创建上传会话，返回 session_id
    chunk   → 接收单个分块数据，校验 MD5 后应答
    finalize→ 合并所有分块 → 校验总 MD5 → 解压 → 推送进度

用法：
    python3 AppUpdateServer.py [--port 9211] [--force]

部署：
    nohup python3 AppUpdateServer.py --force &
"""

import argparse
import hashlib
import json
import os
import re
import shutil
import signal
import socket
import stat
import struct
import subprocess
import sys
import tarfile
import tempfile
import threading
import uuid
from pathlib import Path

# ── 常量 ────────────────────────────────────────────────────────────────────────
# socket 接收块大小（256 KB）
RECV_CHUNK = 256 * 1024

# UDP 发现服务端口（TCP 端口 - 1）
DISCOVERY_PORT = 9210

# 解压目标根目录（~/Publish/）
TARGET_ROOT = Path.home() / "Publish"

# 全局会话字典：session_id → session_info
_sessions: dict[str, dict] = {}
_sessions_lock = threading.Lock()

# ── 受管服务名称 ───────────────────────────────────────────────────────────────────
# 被本更新服务器管理的目标应用
_SERVICE_NAME = "AuroraStruct3D.HttpApi.Host"
_MIGRATOR_NAME = "AuroraStruct3D.DbMigrator"

# SkiaSharp 在 Ubuntu ARM64 上编码 JPEG 时需要的系统原生依赖。
_NATIVE_DEPENDENCY_PACKAGES = ("libuuid1", "libfontconfig1")


# ── 消息帧工具 ──────────────────────────────────────────────────────────────────


def _send_msg(sock: socket.socket, data: dict) -> None:
    """将字典序列化为 JSON，加 4 字节长度前缀后发送。"""
    payload = json.dumps(data, ensure_ascii=False).encode("utf-8")
    sock.sendall(struct.pack(">I", len(payload)) + payload)


def _recv_exactly(sock: socket.socket, n: int) -> bytes:
    """从 socket 中精确读取 n 字节，连接断开时抛出 ConnectionError。"""
    buf = bytearray()
    while len(buf) < n:
        chunk = sock.recv(n - len(buf))
        if not chunk:
            raise ConnectionError("连接意外关闭")
        buf.extend(chunk)
    return bytes(buf)


def _recv_msg(sock: socket.socket) -> dict:
    """读取一条带长度前缀的 JSON 消息，返回字典。"""
    raw_len = _recv_exactly(sock, 4)
    msg_len = struct.unpack(">I", raw_len)[0]
    return json.loads(_recv_exactly(sock, msg_len).decode("utf-8"))


# ── action 处理器 ───────────────────────────────────────────────────────────────


def _action_init(conn: socket.socket, msg: dict) -> None:
    """创建新的上传会话，向客户端返回 session_id。"""
    session_id = str(uuid.uuid4())
    session_dir = Path(tempfile.mkdtemp(prefix=f"aurora_{session_id[:8]}_"))
    with _sessions_lock:
        _sessions[session_id] = {
            "dir": session_dir,
            "total_chunks": int(msg["total_chunks"]),
            "total_size": int(msg["total_size"]),
            "total_md5": msg["total_md5"],
            "received": set(),
            "lock": threading.Lock(),
        }
    size_mb = int(msg["total_size"]) / 1024 / 1024
    print(
        f"[会话] 创建 {session_id[:8]}  分块数={msg['total_chunks']}  大小={size_mb:.1f} MB"
    )
    _send_msg(conn, {"status": "ready", "session_id": session_id})


def _action_chunk(conn: socket.socket, msg: dict) -> None:
    """接收单个分块，校验 MD5 后保存到会话临时目录。"""
    session_id = msg["session_id"]
    chunk_id = int(msg["chunk_id"])
    chunk_size = int(msg["size"])
    expected_md5 = msg["md5"]

    with _sessions_lock:
        session = _sessions.get(session_id)
    if not session:
        _send_msg(conn, {"status": "error", "message": f"会话不存在: {session_id[:8]}"})
        return

    # 告知客户端可以开始发送数据
    _send_msg(conn, {"status": "ready"})

    chunk_path = session["dir"] / f"chunk_{chunk_id:05d}.bin"
    received = 0
    h = hashlib.md5()
    with chunk_path.open("wb") as f:
        while received < chunk_size:
            want = min(RECV_CHUNK, chunk_size - received)
            data = conn.recv(want)
            if not data:
                raise ConnectionError(
                    f"分块 {chunk_id} 接收中断（已收 {received}/{chunk_size}）"
                )
            f.write(data)
            h.update(data)
            received += len(data)

    actual_md5 = h.hexdigest()
    if actual_md5 != expected_md5:
        chunk_path.unlink(missing_ok=True)
        _send_msg(
            conn,
            {
                "status": "error",
                "message": f"分块 {chunk_id} MD5 校验失败: 期望 {expected_md5}，实际 {actual_md5}",
            },
        )
        return

    with session["lock"]:
        session["received"].add(chunk_id)
        count = len(session["received"])
        total = session["total_chunks"]
    print(
        f"\r[分块] 已接收 {count}/{total}  chunk_{chunk_id:05d} ✓", end="", flush=True
    )
    _send_msg(conn, {"status": "ok", "chunk_id": chunk_id})


def _action_finalize(conn: socket.socket, msg: dict) -> None:
    """合并所有分块 → 校验全文件 MD5 → 解压 → 推送进度消息。"""
    session_id = msg["session_id"]
    expected_total_md5 = msg["total_md5"]

    with _sessions_lock:
        session = _sessions.get(session_id)
    if not session:
        _send_msg(conn, {"status": "error", "message": "会话不存在"})
        return

    total_chunks = session["total_chunks"]
    with session["lock"]:
        received_count = len(session["received"])
    if received_count != total_chunks:
        missing = sorted(set(range(total_chunks)) - session["received"])
        _send_msg(
            conn,
            {
                "status": "error",
                "message": f"分块不完整: 已收 {received_count}/{total_chunks}，缺失: {missing[:10]}",
            },
        )
        return

    session_dir: Path = session["dir"]
    merged_path = session_dir / "merged.tar.gz"

    # ── 合并分块 ──────────────────────────────────────────────────────────────
    print(f"\n[合并] 合并 {total_chunks} 个分块...")
    _send_msg(conn, {"stage": "merge", "message": f"正在合并 {total_chunks} 个分块..."})

    h = hashlib.md5()
    with merged_path.open("wb") as out:
        for i in range(total_chunks):
            chunk_path = session_dir / f"chunk_{i:05d}.bin"
            with chunk_path.open("rb") as f:
                for block in iter(lambda: f.read(65536), b""):
                    out.write(block)
                    h.update(block)

    actual_total_md5 = h.hexdigest()
    if actual_total_md5 != expected_total_md5:
        _send_msg(
            conn,
            {
                "status": "error",
                "message": (
                    f"合并后全文件 MD5 校验失败！\n"
                    f"  期望: {expected_total_md5}\n"
                    f"  实际: {actual_total_md5}"
                ),
            },
        )
        shutil.rmtree(session_dir, ignore_errors=True)
        with _sessions_lock:
            _sessions.pop(session_id, None)
        return

    size_mb = merged_path.stat().st_size / 1024 / 1024
    print(f"[合并] MD5 校验通过  {size_mb:.1f} MB")
    _send_msg(
        conn, {"stage": "merge_done", "message": f"合并校验通过（{size_mb:.1f} MB）"}
    )
    # ── 停止目标服务（解压前确保服务已停止）──────────────────────────
    _send_msg(conn, {"stage": "stopping", "message": f"正在停止 {_SERVICE_NAME}..."})
    _stop_managed_service()
    # ── 解压 ──────────────────────────────────────────────────────────────────
    target_dir = TARGET_ROOT
    target_dir.mkdir(parents=True, exist_ok=True)
    arm64_dir = target_dir / "linux-arm64"
    if arm64_dir.exists():
        shutil.rmtree(arm64_dir)
    arm64_dir.mkdir(parents=True, exist_ok=True)

    print(f"[解压] 目标: {target_dir}")
    with tarfile.open(merged_path, mode="r:gz") as tar:
        members = tar.getmembers()
        total_files = len(members)
        for idx, member in enumerate(members, 1):
            if sys.version_info >= (3, 12):
                tar.extract(member, path=target_dir, filter="data")
            else:
                tar.extract(member, path=target_dir)
            if idx % 100 == 0 or idx == total_files:
                print(
                    f"\r[解压] {idx}/{total_files}  {member.name[:50]}",
                    end="",
                    flush=True,
                )
            _send_msg(
                conn,
                {
                    "stage": "extract",
                    "current": idx,
                    "total": total_files,
                    "name": member.name,
                },
            )

    print()
    _fix_exec_permissions(target_dir)

    # ── 清理会话 ──────────────────────────────────────────────────────────────
    shutil.rmtree(session_dir, ignore_errors=True)
    with _sessions_lock:
        _sessions.pop(session_id, None)

    done_msg = f"已解压 {total_files} 个文件到 {target_dir}"
    _send_msg(conn, {"status": "done", "message": done_msg})
    print(f"[完成] {done_msg}")

    # ── 部署后流程：原生依赖 → DbMigrator → 确保服务注册 → 启动 ────────────────
    _ensure_native_dependencies()
    _run_migrator()
    _ensure_system_service()
    _start_managed_service()


def _fix_exec_permissions(base_dir: Path) -> None:
    """为 linux-arm64 下的主程序二进制文件补充可执行权限。"""
    arm64 = base_dir / "linux-arm64"
    if not arm64.exists():
        return
    count = 0
    for proj_dir in arm64.iterdir():
        if not proj_dir.is_dir():
            continue
        for f in proj_dir.iterdir():
            if f.is_file() and f.suffix == "" and f.stat().st_size > 100 * 1024:
                f.chmod(f.stat().st_mode | stat.S_IXUSR | stat.S_IXGRP | stat.S_IXOTH)
                count += 1
    if count:
        print(f"[权限] 已为 {count} 个文件添加可执行权限")


# ── 受管服务部署辅助 ───────────────────────────────────────────────────────────────────


def _run_cmd(cmd: list, desc: str, timeout: int = 120) -> int:
    """执行外部命令，将 stdout/stderr 逐行打印，返回退出码。"""
    print(f"[部署] {desc}...")
    try:
        result = subprocess.run(
            cmd,
            capture_output=True,
            text=True,
            timeout=timeout,
        )
        for line in (result.stdout + result.stderr).strip().splitlines():
            if line:
                print(f"[部署]   {line}")
        if result.returncode == 0:
            print(f"[部署] ✓ {desc}")
        else:
            print(f"[部署] ✗ {desc}（退出码 {result.returncode}）")
        return result.returncode
    except subprocess.TimeoutExpired:
        print(f"[部署] ✗ {desc} 超时（{timeout}s）")
        return -1
    except Exception as exc:
        print(f"[部署] ✗ {desc} 异常: {exc}")
        return -1


def _is_debian_package_installed(package_name: str) -> bool:
    """检查 Debian/Ubuntu 软件包是否已安装。"""
    result = subprocess.run(
        ["dpkg-query", "-W", "-f=${Status}", package_name],
        capture_output=True,
        text=True,
    )
    return result.returncode == 0 and "install ok installed" in result.stdout


def _ensure_native_dependencies() -> None:
    """安装 Host 在 RK3588 上运行所需的系统原生依赖。"""
    if shutil.which("apt-get") is None or shutil.which("dpkg-query") is None:
        print("[部署] 当前系统不支持 apt/dpkg，跳过原生依赖检查")
        return

    missing = [
        package_name
        for package_name in _NATIVE_DEPENDENCY_PACKAGES
        if not _is_debian_package_installed(package_name)
    ]
    if not missing:
        print("[部署] SkiaSharp 原生依赖已安装")
        return

    print(f"[部署] 缺少原生依赖：{', '.join(missing)}")
    if _run_cmd(["sudo", "apt-get", "update"], "刷新 apt 索引", timeout=180) != 0:
        print("[部署] apt 索引刷新失败，原生依赖可能仍缺失")
        return

    _run_cmd(
        [
            "sudo",
            "env",
            "DEBIAN_FRONTEND=noninteractive",
            "apt-get",
            "install",
            "-y",
            *missing,
        ],
        "安装 SkiaSharp 原生依赖",
        timeout=300,
    )


def _find_libuuid_preload_path():
    """查找 libuuid.so.1 路径，用于修复 SkiaSharp 的 uuid_parse 符号解析。"""
    candidates = (
        "/lib/aarch64-linux-gnu/libuuid.so.1",
        "/usr/lib/aarch64-linux-gnu/libuuid.so.1",
        "/lib/x86_64-linux-gnu/libuuid.so.1",
        "/usr/lib/x86_64-linux-gnu/libuuid.so.1",
    )
    for candidate in candidates:
        if Path(candidate).exists():
            return candidate

    try:
        output = subprocess.check_output(["ldconfig", "-p"], text=True)
    except (FileNotFoundError, subprocess.CalledProcessError):
        return None

    for line in output.splitlines():
        if "libuuid.so.1" in line and "=>" in line:
            return line.split("=>", 1)[1].strip()
    return None


def _stop_managed_service() -> None:
    """解压前停止被管理的目标服务（先尝试系统 systemctl，再 pkill）。"""
    import time

    # 检查系统级服务是否活跃
    rc = subprocess.run(
        ["sudo", "systemctl", "is-active", "--quiet", _SERVICE_NAME],
        capture_output=True,
    ).returncode
    if rc == 0:
        _run_cmd(
            ["sudo", "systemctl", "stop", _SERVICE_NAME],
            f"停止系统服务 {_SERVICE_NAME}",
            timeout=30,
        )
    else:
        # 服务不活跃，尝试按进程名 kill
        _run_cmd(
            ["sudo", "pkill", "-f", _SERVICE_NAME],
            f"Kill 进程 {_SERVICE_NAME}",
            timeout=10,
        )
    time.sleep(1)  # 等待进程完全退出


def _run_migrator() -> None:
    """运行 DbMigrator 更新数据库及迁移。"""
    dll = TARGET_ROOT / "linux-arm64" / _MIGRATOR_NAME / f"{_MIGRATOR_NAME}.dll"
    if not dll.exists():
        print(f"[部署] DbMigrator 未找到（{dll}），跳过")
        return
    _run_cmd(
        ["sudo", "dotnet", str(dll)],
        "DbMigrator 更新数据库",
        timeout=180,
    )


def _ensure_system_service() -> None:
    """若系统级 systemd 服务不存在则创建并 enable。服务文件放于 /etc/systemd/system/。"""
    service_dir = Path("/etc/systemd/system")
    service_file = service_dir / f"{_SERVICE_NAME}.service"
    dll = TARGET_ROOT / "linux-arm64" / _SERVICE_NAME / f"{_SERVICE_NAME}.dll"
    work_dir = dll.parent
    libuuid_path = _find_libuuid_preload_path()
    preload_line = f"Environment=LD_PRELOAD={libuuid_path}\n" if libuuid_path else ""

    if service_file.exists():
        print(f"[部署] 系统服务 {_SERVICE_NAME} 已存在，跳过创建")

    print(f"[部署] 创建系统服务 {_SERVICE_NAME}...")
    unit = (
        "[Unit]\n"
        f"Description={_SERVICE_NAME}\n"
        "After=network.target\n\n"
        "[Service]\n"
        "Type=simple\n"
        f"WorkingDirectory={work_dir}\n"
        f"{preload_line}"
        f"ExecStart=sudo dotnet {dll}"
        f' --urls "http://0.0.0.0:5000;https://0.0.0.0:5001"\n'
        "Restart=on-failure\n"
        "RestartSec=5\n"
        "StandardOutput=journal\n"
        "StandardError=journal\n\n"
        "[Install]\n"
        "WantedBy=multi-user.target\n"
    )
    with tempfile.NamedTemporaryFile("w", encoding="utf-8", delete=False) as tmp:
        tmp.write(unit)
        tmp_path = tmp.name

    _run_cmd(
        ["sudo", "install", "-m", "0644", tmp_path, str(service_file)],
        f"写入系统服务文件 {service_file.name}",
        timeout=15,
    )
    _run_cmd(["sudo", "rm", "-f", tmp_path], "清理临时服务文件", timeout=10)
    _run_cmd(["sudo", "systemctl", "daemon-reload"], "daemon-reload", timeout=15)
    _run_cmd(
        ["sudo", "systemctl", "enable", _SERVICE_NAME],
        f"enable {_SERVICE_NAME}",
        timeout=15,
    )


def _start_managed_service() -> None:
    """启动被管理的目标服务。"""
    _run_cmd(
        ["sudo", "systemctl", "start", _SERVICE_NAME],
        f"启动服务 {_SERVICE_NAME}",
        timeout=30,
    )


def _discovery_worker(tcp_port: int) -> None:
    """
    UDP 发现服务：监听 DISCOVERY_PORT，响应客户端广播扫描。
    收到 {"action": "discover"} 后回复服务端信息。
    """
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    try:
        sock.bind(("0.0.0.0", DISCOVERY_PORT))
        print(f"[发现] UDP 监听端口 {DISCOVERY_PORT}")
        while True:
            try:
                data, addr = sock.recvfrom(1024)
                msg = json.loads(data.decode("utf-8"))
                if msg.get("action") == "discover":
                    resp = json.dumps(
                        {
                            "status": "ok",
                            "name": "Aurora Update Server",
                            "port": tcp_port,
                        },
                        ensure_ascii=False,
                    ).encode("utf-8")
                    sock.sendto(resp, addr)
            except (json.JSONDecodeError, OSError):
                pass
    except OSError as exc:
        print(f"[发现] UDP 端口 {DISCOVERY_PORT} 绑定失败: {exc}")
    finally:
        sock.close()


def _handle_client(conn: socket.socket, addr: tuple) -> None:
    """分发客户端请求到对应的 action 处理器。"""
    peer = f"{addr[0]}:{addr[1]}"
    try:
        conn.settimeout(600)
        msg = _recv_msg(conn)
        action = msg.get("action")
        if action == "init":
            _action_init(conn, msg)
        elif action == "chunk":
            _action_chunk(conn, msg)
        elif action == "finalize":
            conn.settimeout(None)  # 解压期间不超时
            _action_finalize(conn, msg)
        else:
            _send_msg(conn, {"status": "error", "message": f"未知 action: {action}"})
    except Exception as exc:
        print(f"\n[错误] {peer}: {exc}")
        try:
            _send_msg(conn, {"status": "error", "message": str(exc)})
        except Exception:
            pass
    finally:
        conn.close()


# ── 端口占用辅助 ────────────────────────────────────────────────────────────────


def _find_pid_on_port(port: int) -> list[int]:
    """
    通过 ss/netstat 查找占用指定端口的进程 PID 列表。
    仅在 Linux 上可用（RK3588 目标平台）。
    """
    pids: list[int] = []
    # 优先使用 ss（iproute2，大多数现代 Linux 均有）
    for tool, args in [
        ("ss", ["-tlnp", f"sport = :{port}"]),
        ("netstat", ["-tlnp"]),
    ]:
        try:
            out = subprocess.check_output(
                [tool] + args, stderr=subprocess.DEVNULL, text=True
            )
            for line in out.splitlines():
                if f":{port}" in line or f",{port}" in line:
                    # ss 输出格式：...  users:(("python3",pid=1234,...
                    # netstat 格式：  ... 1234/python3
                    import re

                    for m in re.finditer(r"pid=(\d+)|\s(\d+)/python", line):
                        pid_str = m.group(1) or m.group(2)
                        if pid_str:
                            pids.append(int(pid_str))
            if pids:
                break
        except (FileNotFoundError, subprocess.CalledProcessError):
            continue
    return list(set(pids))


def _kill_pids(pids: list[int]) -> None:
    """向 PID 列表发送 SIGTERM，等待 2 秒后若仍存活则 SIGKILL。"""
    import time

    for pid in pids:
        try:
            os.kill(pid, signal.SIGTERM)
            print(f"[强制] 已向进程 {pid} 发送 SIGTERM")
        except ProcessLookupError:
            pass
    time.sleep(2)
    for pid in pids:
        try:
            os.kill(pid, signal.SIGKILL)
            print(f"[强制] 进程 {pid} 未退出，发送 SIGKILL")
        except ProcessLookupError:
            pass  # 已正常退出


# ── 主入口 ──────────────────────────────────────────────────────────────────────


def main() -> None:
    parser = argparse.ArgumentParser(description="RK3588 应用更新接收服务")
    parser.add_argument("--host", default="0.0.0.0", help="监听地址（默认：0.0.0.0）")
    parser.add_argument("--port", type=int, default=9211, help="监听端口（默认：9211）")
    parser.add_argument(
        "--force",
        action="store_true",
        help="端口被占用时自动终止旧进程并重新绑定",
    )
    args = parser.parse_args()

    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as srv:
        srv.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        try:
            srv.bind((args.host, args.port))
        except OSError as exc:
            if exc.errno != 98:  # 98 = EADDRINUSE
                raise
            pids = _find_pid_on_port(args.port)
            if args.force:
                if pids:
                    _kill_pids(pids)
                    print(f"[强制] 旧进程已终止，重新绑定端口 {args.port}")
                else:
                    print(f"[警告] 端口 {args.port} 被占用但未能定位进程，请手动释放")
                    sys.exit(1)
                srv.bind((args.host, args.port))
            else:
                pid_hint = f"PID: {pids}" if pids else "PID 未知"
                print(
                    f"[错误] 端口 {args.port} 已被占用（{pid_hint}）。\n"
                    f"       手动终止: kill {' '.join(str(p) for p in pids)}\n"
                    f"       或使用 --force 参数自动终止旧进程并重启。",
                    file=sys.stderr,
                )
                sys.exit(1)

        srv.listen(20)  # 并行分块上传需要更大的 backlog
        print(f"[服务器] 监听 {args.host}:{args.port}，等待连接...")
        print(f"[服务器] 解压目标根目录: {TARGET_ROOT}")

        # 启动 UDP 发现服务（守护线程）
        threading.Thread(
            target=_discovery_worker, args=(args.port,), daemon=True
        ).start()

        while True:
            try:
                conn, addr = srv.accept()
                # 每个客户端开一个线程处理，避免阻塞主循环
                t = threading.Thread(
                    target=_handle_client, args=(conn, addr), daemon=True
                )
                t.start()
            except KeyboardInterrupt:
                print("\n[服务器] 收到中断信号，退出。")
                break


if __name__ == "__main__":
    main()
