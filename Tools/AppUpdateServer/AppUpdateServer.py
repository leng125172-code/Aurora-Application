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
import time
import uuid
from pathlib import Path
from typing import Optional

# ── 常量 ────────────────────────────────────────────────────────────────────────
# socket 接收块大小（256 KB）
RECV_CHUNK = 256 * 1024

# UDP 发现服务端口（TCP 端口 - 1）
DISCOVERY_PORT = 9210

# 受管账号 —— sudo 运行时 Path.home() 会解析为 /root，这里硬编码为 linaro
_TARGET_USER = "linaro"
_USER_HOME = Path(f"/home/{_TARGET_USER}")

# 解压目标根目录——明确指向 /home/linaro/Publish
TARGET_ROOT = _USER_HOME / "Publish"

# 全局会话字典：session_id → session_info
_sessions: dict[str, dict] = {}
_sessions_lock = threading.Lock()

# ── 受管服务名称 ──────────────────────────────────────────────────────────
# 被本更新服务器管理的目标应用
_SERVICE_NAME = "AuroraStruct3D.HttpApi.Host"
_MIGRATOR_NAME = "AuroraStruct3D.DbMigrator"

# ── 本身的桌面/系统服务配置 ─────────────────────────────────────────────────────
_UPDATE_SERVER_SERVICE_NAME = "aurora-update-server"
_APP_INSTALL_DIR = _USER_HOME / "AppUpdate"
# GUI 管理界面（仅供桌面快捷方式手动启动，systemd 服务不要用它，它需要 X11）
_APP_GUI_ENTRY = _APP_INSTALL_DIR / "AppUpdateServerApp.py"
# systemd 服务真正的入口：纯网络服务，无 GUI 依赖
_APP_SERVER_ENTRY = _APP_INSTALL_DIR / "AppUpdateServer.py"
_APP_ICON_PATH = _APP_INSTALL_DIR / "Assets" / "Application.png"
_DESKTOP_FILE = (
    _USER_HOME
    / ".local"
    / "share"
    / "applications"
    / f"{_UPDATE_SERVER_SERVICE_NAME}.desktop"
)
# GNOME 登录后自动启动的 .desktop 文件（用于 GUI 管理界面，需 sudo）
_AUTOSTART_FILE = (
    _USER_HOME / ".config" / "autostart" / f"{_UPDATE_SERVER_SERVICE_NAME}.desktop"
)
_UPDATE_SERVER_SERVICE_FILE = (
    Path("/etc/systemd/system") / f"{_UPDATE_SERVER_SERVICE_NAME}.service"
)
_HTTPAPI_SERVICE_FILE = Path("/etc/systemd/system") / f"{_SERVICE_NAME}.service"
# HttpApi.Host 的 systemd drop-in 目录——里面的旧覆盖文件（如 skia-libuuid.conf）
# 会用 /root/Publish 等旧路径覆盖 ExecStart，必须清理
_HTTPAPI_DROPIN_DIR = Path("/etc/systemd/system") / f"{_SERVICE_NAME}.service.d"

# 桌面快捷方式模板
_DESKTOP_ENTRY_TEMPLATE = """[Desktop Entry]
Version=1.0
Type=Application
Name=Aurora 更新服务器
Name[en]=Aurora Update Server
Comment=RK3588 应用更新接收服务管理工具
Comment[en]=RK3588 Application Update Server Manager
Exec=sudo python3 {gui}
Path={work}
Icon={icon}
Terminal=false
Categories=Utility;System;
StartupNotify=true
"""

# HttpApi.Host systemd 服务模板（内置 SkiaSharp 所需的 libuuid LD_PRELOAD）
_HTTPAPI_UNIT_TEMPLATE = """[Unit]
Description=Aurora Struct3D HTTP API Host Service
After=network.target network-online.target
Wants=network-online.target
Requires=network.target
StartLimitIntervalSec=300
StartLimitBurst=5

[Service]
Type=simple
WorkingDirectory={work}
ExecStart=/usr/bin/dotnet {dll} --urls "http://0.0.0.0:5000;https://0.0.0.0:5001"
Environment=DOTNET_CLI_HOME=/tmp
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=AURORA_AI_CONVERTER_PYTHON=/usr/bin/python3
Environment=AURORA_AI_CONVERTER_SCRIPT={python_script}
Environment=LD_PRELOAD={libuuid}

Restart=on-failure
RestartSec=5
RestartPreventExitStatus=0

TimeoutStopSec=30
KillMode=mixed
KillSignal=SIGTERM
FinalKillSignal=SIGKILL

StandardOutput=journal+console
StandardError=journal+console
SyslogIdentifier=aurora-struct3d-api

[Install]
WantedBy=multi-user.target
"""

# SkiaSharp 在 Ubuntu ARM64 上编码 JPEG 时需要的系统原生依赖。
_NATIVE_DEPENDENCY_PACKAGES = ("libuuid1", "libfontconfig1")
_PYTHON_SYSTEM_PACKAGES = ("python3",)
_PYTHON_REQUIRED_MODULES = ("onnx", "rknn")
_PYTHON_ASSET_DIRNAME = "Python"
_PYTHON_CONVERTER_SCRIPT_FILENAME = "AiModelConvert.py"

# WiFi 自动连接配置：当设备未连接 WiFi 时，每 5 秒扫描并尝试连接目标热点。
_WIFI_TARGET_SSID = "OnePlus 15 02D0"
_WIFI_TARGET_PASSWORD = "Youzidczk125"
_WIFI_CHECK_INTERVAL_SECONDS = 5
_WIFI_CONNECT_COOLDOWN_SECONDS = 15


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


def _safe_send(conn: socket.socket | None, data: dict) -> bool:
    """向 conn 发送消息，如果连接已断开则返回 False（不抛异常）。"""
    if conn is None:
        return False
    try:
        _send_msg(conn, data)
        return True
    except (OSError, ConnectionError, BrokenPipeError):
        return False


_deployment_log_context = threading.local()


def _set_deployment_log_conn(conn: socket.socket | None) -> None:
    _deployment_log_context.conn = conn


def _clear_deployment_log_conn() -> None:
    _deployment_log_context.conn = None


def _get_deployment_log_conn() -> socket.socket | None:
    return getattr(_deployment_log_context, "conn", None)


def _push_deployment_log(message: str) -> None:
    conn = _get_deployment_log_conn()
    if conn is not None and not _safe_send(
        conn, {"stage": "deploy_log", "message": message}
    ):
        _clear_deployment_log_conn()


def _deploy_print(message: str) -> None:
    print(message, flush=True)
    _push_deployment_log(message)


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
        f"[会话] 创建 {session_id[:8]}  分块数={msg['total_chunks']}  大小={size_mb:.1f} MB",
        flush=True,
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
        f"\r[分块] 已接收 {count}/{total}  chunk_{chunk_id:05d} ✓",
        end="",
        flush=True,
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
    print(f"\n[合并] 合并 {total_chunks} 个分块...", flush=True)
    _safe_send(
        conn, {"stage": "merge", "message": f"正在合并 {total_chunks} 个分块..."}
    )

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
        _safe_send(
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
    print(f"[合并] MD5 校验通过  {size_mb:.1f} MB", flush=True)
    _safe_send(
        conn, {"stage": "merge_done", "message": f"合并校验通过（{size_mb:.1f} MB）"}
    )
    # ── 停止目标服务（解压前确保服务已停止）──────────────────────────
    _safe_send(conn, {"stage": "stopping", "message": f"正在停止 {_SERVICE_NAME}..."})
    _stop_managed_service()
    # ── 解压 ──────────────────────────────────────────────────────────────────
    target_dir = TARGET_ROOT
    target_dir.mkdir(parents=True, exist_ok=True)
    arm64_dir = target_dir / "linux-arm64"
    if arm64_dir.exists():
        shutil.rmtree(arm64_dir)
    arm64_dir.mkdir(parents=True, exist_ok=True)

    print(f"[解压] 目标: {target_dir}", flush=True)
    client_alive = True
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
            # 客户端可能在任意时刻断开，一旦推送失败就不再尝试，避免拖慢部署流程
            if client_alive:
                ok = _safe_send(
                    conn,
                    {
                        "stage": "extract",
                        "current": idx,
                        "total": total_files,
                        "name": member.name,
                    },
                )
                if not ok:
                    client_alive = False
                    print(
                        "\n[解压] 客户端连接已断开，后续部署仍会继续在服务端进行",
                        flush=True,
                    )

    print(flush=True)
    _fix_exec_permissions(target_dir)
    _fix_opencv_symlink(target_dir)

    # ── 清理会话 ──────────────────────────────────────────────────────────────
    shutil.rmtree(session_dir, ignore_errors=True)
    with _sessions_lock:
        _sessions.pop(session_id, None)

    extract_done_msg = f"已解压 {total_files} 个文件到 {target_dir}"
    _safe_send(conn, {"stage": "extract_done", "message": extract_done_msg})
    print(f"[完成] {extract_done_msg}", flush=True)

    # ── 部署后流程：原生依赖 → DbMigrator → 确保服务注册 → 启动 ────────────────
    # 以下步骤会继续在服务端执行；若客户端仍在线，则同步推送日志回显
    _set_deployment_log_conn(conn)
    try:
        _deploy_print("[部署] 开始部署后流程...")
        _ensure_native_dependencies()
        _ensure_python_dependencies()
        _run_migrator()
        _ensure_system_service()
        _start_managed_service()
        _deploy_print("[部署] ✓ 部署后流程全部完成")
        if not _safe_send(
            conn,
            {
                "status": "done",
                "message": f"{extract_done_msg}；部署后流程全部完成。",
            },
        ):
            print("[部署] ! 部署已完成，但最终成功状态未送达客户端", flush=True)
    except Exception as exc:
        error_msg = f"部署后流程异常：{exc}"
        _deploy_print(f"[部署] ✗ {error_msg}")
        if not _safe_send(conn, {"status": "error", "message": error_msg}):
            print("[部署] ! 部署失败，但最终错误状态未送达客户端", flush=True)
    finally:
        _clear_deployment_log_conn()


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


def _fix_opencv_symlink(base_dir: Path) -> None:
    """为 OpenCvSharp 4.13+ 创建兼容软链接。

    OpenCvSharp 4.13.x 发布的原生库名称为 libOpenCvSharpExtern.so，
    但 OpenCvSharp.dll 的 DllImport 默认搜索 OpenCvSharpExtern（无 lib 前缀）。
    在每个发布目录中创建符号链接 OpenCvSharpExtern.so → libOpenCvSharpExtern.so
    以解决 DllNotFoundException。
    """
    arm64 = base_dir / "linux-arm64"
    if not arm64.exists():
        return
    count = 0
    for proj_dir in arm64.iterdir():
        if not proj_dir.is_dir():
            continue
        src = proj_dir / "libOpenCvSharpExtern.so"
        link = proj_dir / "OpenCvSharpExtern.so"
        if src.exists() and not link.exists():
            try:
                link.symlink_to(src.name)
                print(f"[OpenCV] 创建符号链接: {link} → {src.name}")
                count += 1
            except OSError as e:
                print(f"[OpenCV] 创建符号链接失败: {e}")
    if count:
        print(f"[OpenCV] 已为 {count} 个目录创建 OpenCvSharpExtern.so 符号链接")


# ── 受管服务部署辅助 ───────────────────────────────────────────────────────────────────


def _run_cmd(
    cmd: list, desc: str, timeout: int = 120, cwd: Optional[str] = None
) -> int:
    """执行外部命令，将 stdout/stderr 逐行打印，返回退出码。"""
    _deploy_print(f"[部署] {desc}...")
    try:
        result = subprocess.run(
            cmd,
            capture_output=True,
            text=True,
            timeout=timeout,
            cwd=cwd,
        )
        for line in (result.stdout + result.stderr).strip().splitlines():
            if line:
                _deploy_print(f"[部署]   {line}")
        if result.returncode == 0:
            _deploy_print(f"[部署] ✓ {desc}")
        else:
            _deploy_print(f"[部署] ✗ {desc}（退出码 {result.returncode}）")
        return result.returncode
    except subprocess.TimeoutExpired:
        _deploy_print(f"[部署] ✗ {desc} 超时（{timeout}s）")
        return -1
    except Exception as exc:
        _deploy_print(f"[部署] ✗ {desc} 异常: {exc}")
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
        _deploy_print("[部署] 当前系统不支持 apt/dpkg，跳过原生依赖检查")
        return

    missing = [
        package_name
        for package_name in _NATIVE_DEPENDENCY_PACKAGES
        if not _is_debian_package_installed(package_name)
    ]
    if not missing:
        _deploy_print("[部署] SkiaSharp 原生依赖已安装")
        return

    _deploy_print(f"[部署] 缺少原生依赖：{', '.join(missing)}")
    if _run_cmd(["sudo", "apt-get", "update"], "刷新 apt 索引", timeout=180) != 0:
        _deploy_print("[部署] apt 索引刷新失败，原生依赖可能仍缺失")
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


def _get_httpapi_python_asset_dir() -> Path:
    return TARGET_ROOT / "linux-arm64" / _SERVICE_NAME / _PYTHON_ASSET_DIRNAME


def _get_httpapi_python_script() -> Path:
    return _get_httpapi_python_asset_dir() / _PYTHON_CONVERTER_SCRIPT_FILENAME


def _find_python_executable() -> Optional[str]:
    candidates: list[str] = []
    for candidate in (
        "/usr/bin/python3",
        shutil.which("python3"),
        shutil.which("python"),
    ):
        if candidate and candidate not in candidates:
            candidates.append(candidate)
    return next(
        (candidate for candidate in candidates if Path(candidate).exists()), None
    )


def _ensure_python_runtime() -> str:
    python_executable = _find_python_executable()
    if python_executable:
        _deploy_print(f"[部署] 检测到 Python：{python_executable}")
        return python_executable

    if shutil.which("apt-get") is None or shutil.which("dpkg-query") is None:
        raise RuntimeError("当前系统不支持 apt/dpkg，无法自动安装 Python 环境。")

    missing = [
        package_name
        for package_name in _PYTHON_SYSTEM_PACKAGES
        if not _is_debian_package_installed(package_name)
    ]
    if missing:
        _deploy_print(f"[部署] 缺少 Python 系统依赖：{', '.join(missing)}")
        if (
            _run_cmd(
                ["sudo", "apt-get", "update"],
                "刷新 apt 索引（Python 环境）",
                timeout=180,
            )
            != 0
        ):
            raise RuntimeError("apt 索引刷新失败，无法安装 Python 环境。")

        if (
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
                "安装 Python 环境",
                timeout=300,
            )
            != 0
        ):
            raise RuntimeError("Python 环境安装失败。")

    python_executable = _find_python_executable()
    if not python_executable:
        raise RuntimeError("Python 安装后仍未找到可执行文件。")

    _deploy_print(f"[部署] 已准备 Python：{python_executable}")
    return python_executable


def _verify_python_modules(python_executable: str) -> None:
    for module_name in _PYTHON_REQUIRED_MODULES:
        if (
            _run_cmd(
                [
                    python_executable,
                    "-c",
                    f"import {module_name}; print({module_name}.__name__)",
                ],
                f"验证 Python 模块 {module_name}",
                timeout=60,
            )
            != 0
        ):
            raise RuntimeError(
                f"Python 模块 {module_name} 验证失败，请先手动安装所需依赖后重试。"
            )


def _ensure_python_dependencies() -> None:
    script_path = _get_httpapi_python_script()
    if not script_path.exists():
        _deploy_print(
            f"[部署] 未找到 AI 转换脚本（{script_path}），跳过 Python 部署。",
        )
        return

    python_executable = _ensure_python_runtime()
    _deploy_print("[部署] 已禁用自动 pip 安装，改为校验已手动安装的 AI Python 依赖。")
    _verify_python_modules(python_executable)


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
    """解压前可靠地停止被管理的目标服务：systemctl stop → pkill → 轮询验证（总超时 30s）。"""
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

    # 无论 systemctl 是否成功，都额外干掉所有 dotnet 进程（包括 dotnet run 启动的）
    _run_cmd(
        ["sudo", "pkill", "-f", f"dotnet.*{_SERVICE_NAME}"],
        f"pkill dotnet 进程（{_SERVICE_NAME}）",
        timeout=10,
    )

    # 轮询确认进程已退出，最多 30s
    deadline = time.time() + 30
    while time.time() < deadline:
        check = subprocess.run(
            ["pgrep", "-f", f"dotnet.*{_SERVICE_NAME}"],
            capture_output=True,
            text=True,
        )
        if check.returncode != 0:
            _deploy_print(f"[部署] ✓ {_SERVICE_NAME} 进程已完全退出")
            return
        time.sleep(1)

    # 最后一步强杀
    _deploy_print(f"[部署] ⚠ {_SERVICE_NAME} 30s 内未退出，发送 SIGKILL")
    _run_cmd(
        ["sudo", "pkill", "-9", "-f", f"dotnet.*{_SERVICE_NAME}"],
        f"强杀 dotnet 进程（{_SERVICE_NAME}）",
        timeout=5,
    )
    time.sleep(2)


def _run_migrator() -> None:
    """运行 DbMigrator 更新数据库及迁移。"""
    work_dir = TARGET_ROOT / "linux-arm64" / _MIGRATOR_NAME
    dll = work_dir / f"{_MIGRATOR_NAME}.dll"
    if not dll.exists():
        _deploy_print(f"[部署] DbMigrator 未找到（{dll}），跳过")
        return
    if (
        _run_cmd(
            ["sudo", "/usr/bin/dotnet", str(dll)],
            "DbMigrator 更新数据库",
            timeout=180,
            cwd=str(work_dir),
        )
        != 0
    ):
        raise RuntimeError("DbMigrator 更新数据库失败。")


def _write_root_file(path: Path, content: str, desc: str) -> bool:
    """使用 sudo 将 content 写入需要 root 权限的 path，返回是否成功。"""
    with tempfile.NamedTemporaryFile("w", encoding="utf-8", delete=False) as tmp:
        tmp.write(content)
        tmp_path = tmp.name
    try:
        rc = _run_cmd(
            ["sudo", "install", "-m", "0644", tmp_path, str(path)],
            desc,
            timeout=15,
        )
        return rc == 0
    finally:
        try:
            os.unlink(tmp_path)
        except OSError:
            pass


def _content_equal(path: Path, expected: str) -> bool:
    """检查文件现有内容是否与期望一致（去除末尾空白后比对）。"""
    try:
        actual = path.read_text(encoding="utf-8")
    except (OSError, UnicodeDecodeError):
        return False
    return actual.rstrip() == expected.rstrip()


def _ensure_desktop_entry() -> None:
    """检查/创建桌面快捷方式及 GNOME 自启条目。

    两个 .desktop 文件使用同一模板，位置不同：
    - ~/.local/share/applications/...   → 应用菜单可点击
    - ~/.config/autostart/...           → GNOME 登录后自动拉起
    """
    content = _DESKTOP_ENTRY_TEMPLATE.format(
        gui=_APP_GUI_ENTRY,
        work=_APP_INSTALL_DIR,
        icon=_APP_ICON_PATH,
    )
    for target, label in (
        (_DESKTOP_FILE, "桌面快捷方式"),
        (_AUTOSTART_FILE, "GNOME 自启条目"),
    ):
        if target.exists() and _content_equal(target, content):
            print(f"[启动] {label}已是最新：{target}", flush=True)
            continue
        try:
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_text(content, encoding="utf-8")
            target.chmod(0o755)
            # 修正 sudo 运行时 owner 变 root 的问题
            subprocess.run(
                [
                    "sudo",
                    "chown",
                    f"{_TARGET_USER}:{_TARGET_USER}",
                    str(target),
                ],
                capture_output=True,
            )
            print(f"[启动] 已写入{label}：{target}", flush=True)
        except OSError as exc:
            print(f"[启动] ⚠ 写入{label}失败：{exc}", flush=True)


def _cleanup_httpapi_dropins() -> None:
    """删除 HttpApi.Host 的 systemd drop-in 覆盖目录（如残留的 skia-libuuid.conf）。

    这些历史 drop-in 文件可能用 /root/Publish 等错误路径覆盖 ExecStart，
    本模板已经把 LD_PRELOAD 整合进主 unit，drop-in 不再需要。
    """
    if not _HTTPAPI_DROPIN_DIR.exists():
        return
    _deploy_print(f"[部署] 清理历史 drop-in 目录：{_HTTPAPI_DROPIN_DIR}")
    _run_cmd(
        ["sudo", "rm", "-rf", str(_HTTPAPI_DROPIN_DIR)],
        f"删除 {_HTTPAPI_DROPIN_DIR}",
        timeout=10,
    )


def _ensure_system_service() -> None:
    """检查/创建 HttpApi.Host 系统服务文件（不一致则重写），并 daemon-reload + enable。"""
    dll = TARGET_ROOT / "linux-arm64" / _SERVICE_NAME / f"{_SERVICE_NAME}.dll"
    work_dir = dll.parent
    libuuid = _find_libuuid_preload_path() or "/lib/aarch64-linux-gnu/libuuid.so.1"
    content = _HTTPAPI_UNIT_TEMPLATE.format(
        work=work_dir,
        dll=dll,
        libuuid=libuuid,
        python_script=_get_httpapi_python_script(),
    )

    # 清理可能存在的历史 drop-in 覆盖文件（如 skia-libuuid.conf 用了 /root/Publish）
    _cleanup_httpapi_dropins()

    if _HTTPAPI_SERVICE_FILE.exists() and _content_equal(
        _HTTPAPI_SERVICE_FILE, content
    ):
        _deploy_print(f"[部署] 系统服务 {_SERVICE_NAME} 已是最新")
    else:
        _deploy_print(f"[部署] 写入系统服务文件 {_HTTPAPI_SERVICE_FILE.name}")
        if not _write_root_file(
            _HTTPAPI_SERVICE_FILE, content, f"写入 {_SERVICE_NAME}.service"
        ):
            raise RuntimeError(f"写入 {_SERVICE_NAME}.service 失败。")
        if (
            _run_cmd(
                ["sudo", "systemctl", "daemon-reload"], "daemon-reload", timeout=15
            )
            != 0
        ):
            raise RuntimeError("systemd daemon-reload 失败。")

    if (
        _run_cmd(
            ["sudo", "systemctl", "enable", _SERVICE_NAME],
            f"enable {_SERVICE_NAME}",
            timeout=15,
        )
        != 0
    ):
        raise RuntimeError(f"enable {_SERVICE_NAME} 失败。")


def _start_managed_service() -> None:
    """启动被管理的目标服务。"""
    if (
        _run_cmd(
            ["sudo", "systemctl", "start", _SERVICE_NAME],
            f"启动服务 {_SERVICE_NAME}",
            timeout=30,
        )
        != 0
    ):
        raise RuntimeError(f"启动服务 {_SERVICE_NAME} 失败。")


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


def _can_manage_wifi() -> bool:
    """判断当前环境是否支持 WiFi 自动连接。"""
    return sys.platform.startswith("linux") and shutil.which("nmcli") is not None


def _get_active_wifi_ssid() -> Optional[str]:
    """获取当前 WiFi 连接名称；若未连接则返回 None。"""
    if not _can_manage_wifi():
        return None

    try:
        result = subprocess.run(
            ["nmcli", "-t", "-f", "TYPE,STATE,CONNECTION", "device", "status"],
            capture_output=True,
            text=True,
            timeout=10,
            check=False,
        )
    except (OSError, subprocess.TimeoutExpired):
        return None

    if result.returncode != 0:
        return None

    for line in result.stdout.splitlines():
        parts = line.split(":", 2)
        if len(parts) != 3:
            continue
        device_type, state, connection_name = parts
        if device_type != "wifi":
            continue

        normalized_state = state.strip().lower()
        if normalized_state in {"connected", "connecting"}:
            connection_name = connection_name.strip()
            return (
                connection_name
                if connection_name and connection_name != "--"
                else "<unknown>"
            )

    return None


def _scan_wifi_ssids() -> set[str]:
    """扫描当前可见的 WiFi SSID 集合。"""
    if not _can_manage_wifi():
        return set()

    try:
        result = subprocess.run(
            ["nmcli", "-t", "-f", "SSID", "device", "wifi", "list", "--rescan", "auto"],
            capture_output=True,
            text=True,
            timeout=15,
            check=False,
        )
    except (OSError, subprocess.TimeoutExpired) as exc:
        print(f"[WiFi] 扫描失败：{exc}", flush=True)
        return set()

    if result.returncode != 0:
        error_text = (result.stderr or result.stdout).strip()
        if error_text:
            print(f"[WiFi] 扫描失败：{error_text}", flush=True)
        return set()

    return {line.strip() for line in result.stdout.splitlines() if line.strip()}


def _connect_wifi(ssid: str, password: str) -> bool:
    """尝试连接指定 WiFi。"""
    try:
        result = subprocess.run(
            ["nmcli", "device", "wifi", "connect", ssid, "password", password],
            capture_output=True,
            text=True,
            timeout=30,
            check=False,
        )
    except (OSError, subprocess.TimeoutExpired) as exc:
        print(f"[WiFi] 连接 {ssid} 失败：{exc}", flush=True)
        return False

    output = (result.stdout or result.stderr).strip()
    if result.returncode == 0:
        print(f"[WiFi] 已连接到 {ssid}", flush=True)
        return True

    if output:
        print(f"[WiFi] 连接 {ssid} 失败：{output}", flush=True)
    else:
        print(f"[WiFi] 连接 {ssid} 失败，退出码 {result.returncode}", flush=True)
    return False


def _wifi_auto_connect_worker() -> None:
    """当设备未连接 WiFi 时，周期性扫描并自动连接指定热点。"""
    if not _can_manage_wifi():
        print("[WiFi] 当前环境不支持 nmcli，跳过自动连接功能", flush=True)
        return

    print(
        f"[WiFi] 自动连接已启用：目标 SSID={_WIFI_TARGET_SSID}，检查间隔={_WIFI_CHECK_INTERVAL_SECONDS}s",
        flush=True,
    )

    while True:
        try:
            active_ssid = _get_active_wifi_ssid()
            if active_ssid == _WIFI_TARGET_SSID:
                time.sleep(_WIFI_CHECK_INTERVAL_SECONDS)
                continue

            if active_ssid:
                print(
                    f"[WiFi] 当前已连接 WiFi：{active_ssid}，跳过自动连接", flush=True
                )
                time.sleep(_WIFI_CHECK_INTERVAL_SECONDS)
                continue

            available_ssids = _scan_wifi_ssids()
            if _WIFI_TARGET_SSID not in available_ssids:
                print(
                    f"[WiFi] 未发现目标热点 {_WIFI_TARGET_SSID}，继续等待", flush=True
                )
                time.sleep(_WIFI_CHECK_INTERVAL_SECONDS)
                continue

            if _connect_wifi(_WIFI_TARGET_SSID, _WIFI_TARGET_PASSWORD):
                time.sleep(_WIFI_CONNECT_COOLDOWN_SECONDS)
                continue
        except Exception as exc:
            print(f"[WiFi] 自动连接线程异常：{exc}", flush=True)

        time.sleep(_WIFI_CHECK_INTERVAL_SECONDS)


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
    # 行缓冲，确保 systemd journal 实时可见
    try:
        sys.stdout.reconfigure(line_buffering=True)
        sys.stderr.reconfigure(line_buffering=True)
    except AttributeError:
        pass

    parser = argparse.ArgumentParser(description="RK3588 应用更新接收服务")
    parser.add_argument("--host", default="0.0.0.0", help="监听地址（默认：0.0.0.0）")
    parser.add_argument("--port", type=int, default=9211, help="监听端口（默认：9211）")
    parser.add_argument(
        "--force",
        action="store_true",
        help="端口被占用时自动终止旧进程并重新绑定",
    )
    args = parser.parse_args()

    # ── 启动前自检：桌面快捷方式 / GNOME 自启条目 ─────────────────
    _ensure_desktop_entry()
    # 如果 HttpApi.Host 的 dll 已存在，也顺带刷新它的 unit 文件，
    # 避免历史遗留的旧路径（如 /root/Publish）持续报错
    _httpapi_dll = TARGET_ROOT / "linux-arm64" / _SERVICE_NAME / f"{_SERVICE_NAME}.dll"
    if _httpapi_dll.exists():
        _ensure_system_service()
    else:
        print(
            f"[启动] 跳过 HttpApi unit 自检：{_httpapi_dll} 暂不存在",
            flush=True,
        )

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
                    print(f"[强制] 旧进程已终止，重新绑定端口 {args.port}", flush=True)
                else:
                    print(
                        f"[警告] 端口 {args.port} 被占用但未能定位进程，请手动释放",
                        flush=True,
                    )
                    sys.exit(1)
                srv.bind((args.host, args.port))
            else:
                pid_hint = f"PID: {pids}" if pids else "PID 未知"
                print(
                    f"[错误] 端口 {args.port} 已被占用（{pid_hint}）。\n"
                    f"       手动终止: kill {' '.join(str(p) for p in pids)}\n"
                    f"       或使用 --force 参数自动终止旧进程并重启。",
                    file=sys.stderr,
                    flush=True,
                )
                sys.exit(1)

        srv.listen(20)  # 并行分块上传需要更大的 backlog
        print(f"[服务器] 监听 {args.host}:{args.port}，等待连接...", flush=True)
        print(f"[服务器] 解压目标根目录: {TARGET_ROOT}", flush=True)

        # 启动 UDP 发现服务（守护线程）
        threading.Thread(
            target=_discovery_worker, args=(args.port,), daemon=True
        ).start()
        threading.Thread(target=_wifi_auto_connect_worker, daemon=True).start()

        while True:
            try:
                conn, addr = srv.accept()
                # 每个客户端开一个线程处理，避免阻塞主循环
                t = threading.Thread(
                    target=_handle_client, args=(conn, addr), daemon=True
                )
                t.start()
            except KeyboardInterrupt:
                print("\n[服务器] 收到中断信号，退出。", flush=True)
                break


if __name__ == "__main__":
    main()
