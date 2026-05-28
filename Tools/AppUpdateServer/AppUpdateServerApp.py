#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
AppUpdateServerApp.py
Aurora 应用更新服务器 — 图形界面管理程序（tkinter 版）

功能：
    · 图形化启动 / 停止 AppUpdateServer 服务
    · 实时彩色日志显示
    · 开机自启管理（Linux GNOME autostart .desktop / Windows 注册表）
    · 侧边栏实时显示本机所有 IP 地址，IP 变化自动刷新
    · 一键构建 .deb 安装包（仅 Linux）

支持平台：Linux · Windows 10/11
版本：2.0.0
"""

import platform
import shutil
import socket
import subprocess
import sys
import textwrap
import threading
import tkinter as tk
import tkinter.filedialog as filedialog
import tkinter.messagebox as messagebox
from datetime import datetime
from pathlib import Path
from typing import Optional

# ── 应用常量 ────────────────────────────────────────────────────────────────────
APP_NAME = "Aurora 更新服务器"
APP_VERSION = "2.0.0"
APP_ID = "aurora-update-server"
APP_AUTHOR = "Aurora Team"
APP_DESCRIPTION = "RK3588 应用更新接收服务管理工具"
DEFAULT_PORT = 9211
DEFAULT_HOST = "0.0.0.0"

ASSETS_DIR = Path(__file__).parent / "Assets"
SERVER_SCRIPT = Path(__file__).parent / "AppUpdateServer.py"

# ── 深色主题配色 ─────────────────────────────────────────────────────────────────
THEME = {
    "bg": "#1e1e1e",
    "bg2": "#252526",
    "bg3": "#2d2d30",
    "fg": "#d4d4d4",
    "fg_dim": "#858585",
    "ok": "#4ec9b0",
    "warn": "#dcdcaa",
    "error": "#f44747",
    "accent": "#007acc",
    "btn_bg": "#3c3c3c",
    "btn_fg": "#d4d4d4",
    "entry_bg": "#3c3c3c",
    "entry_fg": "#d4d4d4",
    "select_bg": "#094771",
}

# GNOME autostart .desktop 模板（登录后由桌面会话拉起 GUI，需要 sudo 以便后续操作系统文件）
_AUTOSTART_DESKTOP_TEMPLATE = """[Desktop Entry]
Version=1.0
Type=Application
Name=Aurora 更新服务器
Name[en]=Aurora Update Server
Comment=RK3588 应用更新接收服务管理工具
Exec=sudo {python} {script}
Icon={icon}
Terminal=false
Categories=Utility;System;
StartupNotify=true
X-GNOME-Autostart-enabled=true
"""

_DESKTOP_TEMPLATE = """\
[Desktop Entry]
Version=1.0
Type=Application
Name=Aurora 更新服务器
Name[en]=Aurora Update Server
Comment=RK3588 应用更新接收服务管理工具
Exec={python} {script} %U
Icon={icon}
Terminal=false
Categories=Utility;System;
StartupNotify=true
"""


# ── 获取本机所有 IP ──────────────────────────────────────────────────────────────
def get_all_ips() -> list:
    """获取本机所有网卡的 IP 地址，返回 [(网卡名, IP), ...]。"""
    result = []
    try:
        if platform.system() == "Windows":
            out = subprocess.check_output(
                ["ipconfig"],
                text=True,
                encoding="gbk",
                errors="replace",
                creationflags=subprocess.CREATE_NO_WINDOW,
            )
            iface = ""
            for line in out.splitlines():
                line_r = line.rstrip()
                if line_r and not line_r.startswith(" "):
                    iface = line_r.rstrip(":")
                elif "IPv4" in line_r or "IPv6" in line_r or "IP Address" in line_r:
                    parts = line_r.split(":")
                    if len(parts) >= 2:
                        ip = parts[-1].strip()
                        if ip and ip != "127.0.0.1":
                            result.append((iface, ip))
        else:
            out = subprocess.check_output(["ip", "addr"], text=True, errors="replace")
            iface = ""
            for line in out.splitlines():
                line = line.strip()
                if line and line[0].isdigit():
                    iface = line.split(":")[1].strip().split("@")[0]
                elif line.startswith("inet ") or line.startswith("inet6 "):
                    parts = line.split()
                    if len(parts) >= 2:
                        ip = parts[1].split("/")[0]
                        if ip != "127.0.0.1" and ip != "::1":
                            result.append((iface, ip))
    except Exception as exc:
        result.append(("错误", str(exc)))
    return result


# ── 开机启动管理 ─────────────────────────────────────────────────────────────────
class AutostartManager:
    """跨平台开机启动管理（Linux GNOME autostart .desktop / Windows 注册表）。"""

    # RK3588 目标用户固定为 linaro，autostart 目录硬编码避免 sudo 下 Path.home() 指向 /root
    _TARGET_USER = "linaro"
    _AUTOSTART_DIR = Path(f"/home/{_TARGET_USER}/.config/autostart")
    _AUTOSTART_FILE = _AUTOSTART_DIR / f"{APP_ID}.desktop"
    # 历史遗留路径，启用/禁用时顺带清理
    _LEGACY_SYSTEM_SVC_FILE = Path("/etc/systemd/system") / f"{APP_ID}.service"
    _LEGACY_USER_SVC_FILE = (
        Path.home() / ".config" / "systemd" / "user" / f"{APP_ID}.service"
    )
    _WIN_KEY = r"Software\Microsoft\Windows\CurrentVersion\Run"
    _WIN_NAME = "AuroraUpdateServer"

    @classmethod
    def is_enabled(cls) -> bool:
        if platform.system() == "Linux":
            return cls._AUTOSTART_FILE.exists()
        if platform.system() == "Windows":
            try:
                import winreg

                with winreg.OpenKey(winreg.HKEY_CURRENT_USER, cls._WIN_KEY) as k:
                    winreg.QueryValueEx(k, cls._WIN_NAME)
                return True
            except (FileNotFoundError, OSError):
                return False
        return False

    @classmethod
    def enable(cls) -> tuple:
        if platform.system() == "Linux":
            return cls._enable_linux()
        if platform.system() == "Windows":
            return cls._enable_windows()
        return False, "当前平台不支持"

    @classmethod
    def disable(cls) -> tuple:
        if platform.system() == "Linux":
            return cls._disable_linux()
        if platform.system() == "Windows":
            return cls._disable_windows()
        return False, "当前平台不支持"

    @classmethod
    def _enable_linux(cls) -> tuple:
        try:
            # 兼容旧版本：清理遗留的 systemd 用户/系统服务
            cls._cleanup_legacy_systemd()

            icon_path = ASSETS_DIR / "Application.png"
            content = _AUTOSTART_DESKTOP_TEMPLATE.format(
                python=sys.executable,
                script=str(Path(__file__).resolve()),
                icon=str(icon_path),
            )
            tmp_file = Path("/tmp") / f"{APP_ID}.desktop"
            tmp_file.write_text(content, encoding="utf-8")

            # 确保 autostart 目录存在且归属目标用户
            subprocess.run(
                [
                    "sudo",
                    "install",
                    "-d",
                    "-o",
                    cls._TARGET_USER,
                    "-g",
                    cls._TARGET_USER,
                    "-m",
                    "0755",
                    str(cls._AUTOSTART_DIR),
                ],
                check=True,
                capture_output=True,
            )
            subprocess.run(
                [
                    "sudo",
                    "install",
                    "-o",
                    cls._TARGET_USER,
                    "-g",
                    cls._TARGET_USER,
                    "-m",
                    "0644",
                    str(tmp_file),
                    str(cls._AUTOSTART_FILE),
                ],
                check=True,
                capture_output=True,
            )
            subprocess.run(
                ["sudo", "rm", "-f", str(tmp_file)],
                check=False,
                capture_output=True,
            )
            return True, f"已写入 GNOME 自启条目:\n{cls._AUTOSTART_FILE}"
        except subprocess.CalledProcessError as exc:
            err = exc.stderr.decode(errors="replace") if exc.stderr else str(exc)
            return False, f"启用失败: {err}"
        except Exception as exc:
            return False, f"启用失败: {exc}"

    @classmethod
    def _disable_linux(cls) -> tuple:
        try:
            # 顺带清理遗留的 systemd 服务
            cls._cleanup_legacy_systemd()
            if cls._AUTOSTART_FILE.exists():
                subprocess.run(
                    ["sudo", "rm", "-f", str(cls._AUTOSTART_FILE)],
                    check=False,
                    capture_output=True,
                )
            return True, "已删除 GNOME 自启条目"
        except Exception as exc:
            return False, f"禁用失败: {exc}"

    @classmethod
    def _cleanup_legacy_systemd(cls) -> None:
        """清理旧版本残留的 systemd 服务，幂等。"""
        subprocess.run(
            ["systemctl", "--user", "disable", "--now", APP_ID],
            check=False,
            capture_output=True,
        )
        if cls._LEGACY_USER_SVC_FILE.exists():
            try:
                cls._LEGACY_USER_SVC_FILE.unlink()
            except OSError:
                pass
            subprocess.run(
                ["systemctl", "--user", "daemon-reload"],
                check=False,
                capture_output=True,
            )
        subprocess.run(
            ["sudo", "systemctl", "disable", "--now", APP_ID],
            check=False,
            capture_output=True,
        )
        if cls._LEGACY_SYSTEM_SVC_FILE.exists():
            subprocess.run(
                ["sudo", "rm", "-f", str(cls._LEGACY_SYSTEM_SVC_FILE)],
                check=False,
                capture_output=True,
            )
            subprocess.run(
                ["sudo", "systemctl", "daemon-reload"],
                check=False,
                capture_output=True,
            )

    @classmethod
    def _enable_windows(cls) -> tuple:
        try:
            import winreg

            cmd = f'"{sys.executable}" "{Path(__file__).resolve()}"'
            with winreg.OpenKey(
                winreg.HKEY_CURRENT_USER, cls._WIN_KEY, 0, winreg.KEY_SET_VALUE
            ) as k:
                winreg.SetValueEx(k, cls._WIN_NAME, 0, winreg.REG_SZ, cmd)
            return True, f"已添加到 Windows 启动项:\n{cmd}"
        except Exception as exc:
            return False, f"启用失败: {exc}"

    @classmethod
    def _disable_windows(cls) -> tuple:
        try:
            import winreg

            with winreg.OpenKey(
                winreg.HKEY_CURRENT_USER, cls._WIN_KEY, 0, winreg.KEY_SET_VALUE
            ) as k:
                winreg.DeleteValue(k, cls._WIN_NAME)
            return True, "已从 Windows 启动项中移除"
        except FileNotFoundError:
            return True, "启动项不存在（无需操作）"
        except Exception as exc:
            return False, f"禁用失败: {exc}"


# ── 服务器后台线程 ───────────────────────────────────────────────────────────────
class ServerThread(threading.Thread):
    """在后台线程中运行 AppUpdateServer 子进程，日志通过回调传递。"""

    def __init__(self, host: str, port: int, on_log, on_state):
        super().__init__(daemon=True)
        self.host = host
        self.port = port
        self._on_log = on_log
        self._on_state = on_state
        self._proc: Optional[subprocess.Popen] = None

    def run(self):
        self._on_state(True)
        self._on_log(f"正在启动服务 {self.host}:{self.port}...", "info")
        kwargs = dict(
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            text=True,
            encoding="utf-8",
            errors="replace",
            bufsize=1,
        )
        if platform.system() == "Windows":
            kwargs["creationflags"] = subprocess.CREATE_NO_WINDOW
        try:
            self._proc = subprocess.Popen(
                [
                    sys.executable,
                    str(SERVER_SCRIPT),
                    "--host",
                    self.host,
                    "--port",
                    str(self.port),
                    "--force",
                ],
                **kwargs,
            )
            for raw in self._proc.stdout:
                line = raw.rstrip("\r\n").lstrip("\r")
                if line:
                    self._on_log(line, self._level(line))
            self._proc.wait()
            rc = self._proc.returncode
            self._on_log(f"服务进程已退出（返回码 {rc}）", "warn" if rc else "info")
        except FileNotFoundError:
            self._on_log(f"未找到服务脚本: {SERVER_SCRIPT}", "error")
        except Exception as exc:
            self._on_log(f"服务异常: {exc}", "error")
        finally:
            self._proc = None
            self._on_state(False)

    def stop(self):
        if self._proc and self._proc.poll() is None:
            self._on_log("正在停止服务...", "warn")
            self._proc.terminate()
            try:
                self._proc.wait(timeout=5)
            except subprocess.TimeoutExpired:
                self._proc.kill()

    @staticmethod
    def _level(line: str) -> str:
        low = line.lower()
        if any(
            k in low
            for k in ("错误", "error", "失败", "fail", "exception", "traceback")
        ):
            return "error"
        if any(k in low for k in ("警告", "warn", "注意", "强制")):
            return "warn"
        if any(k in low for k in ("完成", "done", "✓", "成功", "校验通过", "已解压")):
            return "ok"
        return "info"


# ── deb 构建后台线程 ─────────────────────────────────────────────────────────────
class DebBuildThread(threading.Thread):

    def __init__(self, version: str, out_dir: Path, on_log, on_done):
        super().__init__(daemon=True)
        self.version = version
        self.out_dir = out_dir
        self._on_log = on_log
        self._on_done = on_done

    def run(self):
        if not shutil.which("dpkg-deb"):
            self._on_log("错误: 未找到 dpkg-deb，请先安装 dpkg 工具链", "error")
            self._on_done("")
            return
        import tempfile

        with tempfile.TemporaryDirectory(prefix="aurora_deb_") as tmp:
            tmp_path = Path(tmp)
            pkg_name = f"{APP_ID}_{self.version}_all"
            pkg_dir = tmp_path / pkg_name
            lib_dir = pkg_dir / "usr" / "lib" / APP_ID
            app_dir = pkg_dir / "usr" / "share" / "applications"
            ico_dir = pkg_dir / "usr" / "share" / "pixmaps"
            svc_dir = pkg_dir / "lib" / "systemd" / "user"
            debian_dir = pkg_dir / "DEBIAN"
            for d in (lib_dir, app_dir, ico_dir, svc_dir, debian_dir):
                d.mkdir(parents=True, exist_ok=True)
            src_dir = Path(__file__).parent
            for f in (SERVER_SCRIPT, Path(__file__)):
                if f.exists():
                    shutil.copy2(f, lib_dir / f.name)
            assets_src = src_dir / "Assets"
            if assets_src.exists():
                shutil.copytree(assets_src, lib_dir / "Assets", dirs_exist_ok=True)
            png = ASSETS_DIR / "Application.png"
            if png.exists():
                shutil.copy2(png, ico_dir / f"{APP_ID}.png")
            (svc_dir / f"{APP_ID}.service").write_text(
                _SYSTEMD_TEMPLATE.format(
                    python="/usr/bin/python3",
                    script=f"/usr/lib/{APP_ID}/AppUpdateServerApp.py",
                ),
                encoding="utf-8",
            )
            (app_dir / f"{APP_ID}.desktop").write_text(
                _DESKTOP_TEMPLATE.format(
                    python="/usr/bin/python3",
                    script=f"/usr/lib/{APP_ID}/AppUpdateServerApp.py",
                    icon=f"/usr/share/pixmaps/{APP_ID}.png",
                ),
                encoding="utf-8",
            )
            control = textwrap.dedent(f"""\
                Package: {APP_ID}
                Version: {self.version}
                Section: utils
                Priority: optional
                Architecture: all
                Depends: python3 (>= 3.10), python3-tk
                Maintainer: {APP_AUTHOR}
                Description: {APP_DESCRIPTION}
                 Aurora 框架应用更新服务管理工具。
            """)
            (debian_dir / "control").write_text(control, encoding="utf-8")
            postinst = textwrap.dedent(f"""\
                #!/bin/sh
                set -e
                chmod +x /usr/lib/{APP_ID}/AppUpdateServer.py
                chmod +x /usr/lib/{APP_ID}/AppUpdateServerApp.py
                update-desktop-database -q 2>/dev/null || true
                echo "Aurora Update Server {self.version} 安装完成。"
            """)
            postinst_f = debian_dir / "postinst"
            postinst_f.write_text(postinst, encoding="utf-8")
            postinst_f.chmod(0o755)
            prerm_f = debian_dir / "prerm"
            prerm_f.write_text(
                textwrap.dedent(f"""\
                #!/bin/sh
                set -e
                sudo rm -f /home/linaro/.config/autostart/{APP_ID}.desktop 2>/dev/null || true
                sudo systemctl disable --now {APP_ID} 2>/dev/null || true
                sudo rm -f /etc/systemd/system/{APP_ID}.service 2>/dev/null || true
                echo "Aurora Update Server 已卸载。"
            """),
                encoding="utf-8",
            )
            prerm_f.chmod(0o755)
            for pyf in lib_dir.rglob("*.py"):
                pyf.chmod(0o644)
            self.out_dir.mkdir(parents=True, exist_ok=True)
            deb_out = self.out_dir / f"{pkg_name}.deb"
            self._on_log(f"正在打包 {deb_out.name}...", "info")
            result = subprocess.run(
                ["dpkg-deb", "--build", str(pkg_dir), str(deb_out)],
                capture_output=True,
                text=True,
            )
            if result.returncode != 0:
                self._on_log(f"dpkg-deb 错误:\n{result.stderr}", "error")
                self._on_done("")
                return
            size_kb = deb_out.stat().st_size // 1024
            self._on_log(f"构建完成: {deb_out}  ({size_kb} KB)", "ok")
            self._on_log(f"安装命令: sudo dpkg -i {deb_out}", "info")
            self._on_done(str(deb_out))


# ── 主窗口 ──────────────────────────────────────────────────────────────────────
class MainWindow:
    """主窗口，基于 tkinter 实现深色主题界面。"""

    _LOG_COLORS = {
        "info": THEME["fg"],
        "ok": THEME["ok"],
        "warn": THEME["warn"],
        "error": THEME["error"],
        "ts": THEME["fg_dim"],
    }

    def __init__(self):
        self._running = False
        self._server_thread: Optional[ServerThread] = None
        self._deb_thread: Optional[DebBuildThread] = None
        self._auto_scroll = True
        self._ip_cache: list = []

        self._root = tk.Tk()
        self._root.title(f"{APP_NAME}  v{APP_VERSION}")
        self._root.configure(bg=THEME["bg"])
        self._root.minsize(820, 560)
        self._root.geometry("980x640")
        self._root.protocol("WM_DELETE_WINDOW", self._on_close)

        # 设置窗口图标
        _ico = ASSETS_DIR / "Application.ico"
        if _ico.exists():
            try:
                self._root.iconbitmap(str(_ico))
            except Exception:
                pass

        self._build_ui()
        self._schedule_ip_refresh()
        self._load_settings()

        # 若勾选了"程序启动时自动启动服务"，延迟 500 ms 再启动，等界面渲染完毕
        if self._srv_autostart_var.get():
            self._root.after(500, self._start_server)

    # ── UI 构建 ────────────────────────────────────────────────────────────────

    def _build_ui(self):
        T = THEME

        # 顶部状态条
        top = tk.Frame(self._root, bg=T["bg2"], pady=6)
        top.pack(fill=tk.X, padx=8, pady=(8, 0))

        self._dot_lbl = tk.Label(
            top, text="●", font=("", 18), bg=T["bg2"], fg="#808080"
        )
        self._dot_lbl.pack(side=tk.LEFT, padx=(12, 4))

        self._status_lbl = tk.Label(
            top, text="服务未运行", font=("", 10), bg=T["bg2"], fg=T["fg"]
        )
        self._status_lbl.pack(side=tk.LEFT)

        self._btn_restart = tk.Button(
            top,
            text="↺ 重启",
            width=8,
            bg=T["btn_bg"],
            fg=T["btn_fg"],
            relief=tk.FLAT,
            state=tk.DISABLED,
            command=self._restart_server,
        )
        self._btn_restart.pack(side=tk.RIGHT, padx=(4, 12))

        self._btn_toggle = tk.Button(
            top,
            text="▶ 启动服务",
            width=12,
            bg=T["accent"],
            fg="#ffffff",
            relief=tk.FLAT,
            command=self._toggle_server,
        )
        self._btn_toggle.pack(side=tk.RIGHT, padx=4)

        # 主体：左边日志 + 分隔线 + 右边面板
        body = tk.Frame(self._root, bg=T["bg"])
        body.pack(fill=tk.BOTH, expand=True, padx=8, pady=6)

        left = tk.Frame(body, bg=T["bg"])
        left.pack(side=tk.LEFT, fill=tk.BOTH, expand=True)

        tk.Label(left, text="运行日志", bg=T["bg"], fg=T["fg_dim"], font=("", 9)).pack(
            anchor=tk.W
        )

        self._log_text = tk.Text(
            left,
            bg="#1e1e1e",
            fg=T["fg"],
            insertbackground=T["fg"],
            selectbackground=T["select_bg"],
            relief=tk.FLAT,
            font=("Monospace", 9),
            state=tk.DISABLED,
            wrap=tk.WORD,
        )
        self._log_text.pack(fill=tk.BOTH, expand=True)
        for tag, color in self._LOG_COLORS.items():
            self._log_text.tag_config(tag, foreground=color)

        log_bar = tk.Frame(left, bg=T["bg"])
        log_bar.pack(fill=tk.X, pady=(2, 0))
        self._auto_scroll_var = tk.BooleanVar(value=True)
        tk.Checkbutton(
            log_bar,
            text="自动滚动",
            variable=self._auto_scroll_var,
            bg=T["bg"],
            fg=T["fg"],
            selectcolor=T["bg3"],
            activebackground=T["bg"],
            activeforeground=T["fg"],
            command=lambda: setattr(self, "_auto_scroll", self._auto_scroll_var.get()),
        ).pack(side=tk.LEFT)
        tk.Button(
            log_bar,
            text="清除",
            width=6,
            bg=T["btn_bg"],
            fg=T["btn_fg"],
            relief=tk.FLAT,
            command=self._clear_log,
        ).pack(side=tk.RIGHT)

        tk.Frame(body, bg=T["bg3"], width=1).pack(side=tk.LEFT, fill=tk.Y, padx=8)

        right = tk.Frame(body, bg=T["bg"], width=280)
        right.pack(side=tk.LEFT, fill=tk.Y)
        right.pack_propagate(False)
        self._build_right_panel(right)

        # 底部状态栏
        self._sb_lbl = tk.Label(
            self._root,
            text="就绪",
            bg=T["bg3"],
            fg=T["fg_dim"],
            anchor=tk.W,
            padx=8,
            pady=3,
        )
        self._sb_lbl.pack(fill=tk.X, side=tk.BOTTOM)

    def _build_right_panel(self, parent: tk.Frame):
        T = THEME
        tab_bar = tk.Frame(parent, bg=T["bg2"])
        tab_bar.pack(fill=tk.X)
        self._tab_frames: dict = {}
        self._tab_btns: dict = {}

        tab_names = [
            ("service", "* 服务"),
            ("settings", "+ 设置"),
            ("about", "? 关于"),
        ]
        content_area = tk.Frame(parent, bg=T["bg"])
        content_area.pack(fill=tk.BOTH, expand=True)

        for key, label in tab_names:
            frame = tk.Frame(content_area, bg=T["bg"])
            self._tab_frames[key] = frame
            btn = tk.Button(
                tab_bar,
                text=label,
                relief=tk.FLAT,
                bg=T["bg2"],
                fg=T["fg_dim"],
                padx=4,
                pady=4,
                command=lambda k=key: self._switch_tab(k),
            )
            btn.pack(side=tk.LEFT, fill=tk.X, expand=True)
            self._tab_btns[key] = btn

        self._build_service_tab(self._tab_frames["service"])
        self._build_settings_tab(self._tab_frames["settings"])
        self._build_about_tab(self._tab_frames["about"])
        self._switch_tab("service")

    def _switch_tab(self, key: str):
        for frame in self._tab_frames.values():
            frame.pack_forget()
        self._tab_frames[key].pack(fill=tk.BOTH, expand=True, padx=8, pady=8)
        for k, btn in self._tab_btns.items():
            btn.configure(
                bg=THEME["bg3"] if k == key else THEME["bg2"],
                fg=THEME["fg"] if k == key else THEME["fg_dim"],
            )

    def _build_service_tab(self, parent: tk.Frame):
        T = THEME
        self._section_label(parent, "连接配置")

        for label, attr, default in [
            ("监听地址：", "_host_var", DEFAULT_HOST),
            ("监听端口：", "_port_var", str(DEFAULT_PORT)),
        ]:
            row = tk.Frame(parent, bg=T["bg"])
            row.pack(fill=tk.X, pady=3)
            tk.Label(
                row, text=label, bg=T["bg"], fg=T["fg"], width=10, anchor=tk.W
            ).pack(side=tk.LEFT)
            var = tk.StringVar(value=default)
            setattr(self, attr, var)
            entry = tk.Entry(
                row,
                textvariable=var,
                bg=T["entry_bg"],
                fg=T["entry_fg"],
                relief=tk.FLAT,
                insertbackground=T["fg"],
            )
            entry.pack(side=tk.LEFT, fill=tk.X, expand=True)
            if attr == "_host_var":
                self._host_entry = entry
            else:
                self._port_entry = entry

        row3 = tk.Frame(parent, bg=T["bg"])
        row3.pack(fill=tk.X, pady=3)
        tk.Label(
            row3, text="解压目录：", bg=T["bg"], fg=T["fg"], width=10, anchor=tk.W
        ).pack(side=tk.LEFT)
        tk.Label(
            row3, text=str(Path.home() / "Publish"), bg=T["bg"], fg=T["fg_dim"]
        ).pack(side=tk.LEFT)

        # ── 本机 IP 地址（内嵌在服务页，IP 变化时自动刷新，无手动刷新按钮）──
        self._section_label(parent, "本机 IP 地址")
        self._ip_time_lbl = tk.Label(
            parent, text="", bg=T["bg"], fg=T["fg_dim"], font=("", 8), anchor=tk.E
        )
        self._ip_time_lbl.pack(fill=tk.X)

        self._ip_text = tk.Text(
            parent,
            bg=T["bg2"],
            fg=T["fg"],
            relief=tk.FLAT,
            font=("Monospace", 9),
            state=tk.DISABLED,
            wrap=tk.NONE,
        )
        self._ip_text.pack(fill=tk.BOTH, expand=True)
        self._ip_text.tag_config("iface", foreground=T["fg_dim"])
        self._ip_text.tag_config("ip4", foreground=T["ok"])
        self._ip_text.tag_config("ip6", foreground=T["warn"])
        self._ip_text.tag_config("err", foreground=T["error"])
        self._refresh_ips()

    def _build_settings_tab(self, parent: tk.Frame):
        T = THEME
        self._section_label(parent, "开机启动")
        self._autostart_var = tk.BooleanVar(value=AutostartManager.is_enabled())
        tk.Checkbutton(
            parent,
            text="系统启动时自动运行",
            variable=self._autostart_var,
            bg=T["bg"],
            fg=T["fg"],
            selectcolor=T["bg3"],
            activebackground=T["bg"],
            activeforeground=T["fg"],
        ).pack(anchor=tk.W, pady=2)
        tk.Button(
            parent,
            text="应用开机启动设置",
            relief=tk.FLAT,
            bg=T["btn_bg"],
            fg=T["btn_fg"],
            command=self._apply_autostart,
        ).pack(anchor=tk.W, pady=4)
        if platform.system() == "Linux":
            hint = f"systemd: /etc/systemd/system/{APP_ID}.service"
        elif platform.system() == "Windows":
            hint = r"注册表 HKCU\...\Run"
        else:
            hint = "当前平台不支持"
        tk.Label(
            parent,
            text=hint,
            bg=T["bg"],
            fg=T["fg_dim"],
            wraplength=240,
            justify=tk.LEFT,
            font=("", 8),
        ).pack(anchor=tk.W)

        self._section_label(parent, "常规", pady_top=12)
        self._srv_autostart_var = tk.BooleanVar(value=False)
        tk.Checkbutton(
            parent,
            text="程序启动时自动启动服务",
            variable=self._srv_autostart_var,
            bg=T["bg"],
            fg=T["fg"],
            selectcolor=T["bg3"],
            activebackground=T["bg"],
            activeforeground=T["fg"],
        ).pack(anchor=tk.W, pady=2)

    def _build_about_tab(self, parent: tk.Frame):
        T = THEME
        for text, bold, size in [
            (APP_NAME, True, 13),
            (f"版本 {APP_VERSION}", False, 10),
            ("", False, 8),
            (APP_DESCRIPTION, False, 9),
            (f"作者: {APP_AUTHOR}", False, 9),
            (f"Python {sys.version.split()[0]}  +  tkinter", False, 8),
        ]:
            tk.Label(
                parent,
                text=text,
                bg=T["bg"],
                fg=T["fg"],
                font=("", size, "bold" if bold else "normal"),
                wraplength=230,
            ).pack(pady=1)
        if platform.system() == "Linux":
            tk.Frame(parent, bg=T["bg3"], height=1).pack(fill=tk.X, pady=8)
            tk.Label(
                parent, text="打包分发", bg=T["bg"], fg=T["fg"], font=("", 9, "bold")
            ).pack()
            tk.Button(
                parent,
                text="[deb] 构建 .deb 安装包",
                relief=tk.FLAT,
                bg=T["btn_bg"],
                fg=T["btn_fg"],
                command=self._build_deb,
            ).pack(pady=4)
            tk.Label(
                parent,
                text="需要: sudo apt install dpkg-dev",
                bg=T["bg"],
                fg=T["fg_dim"],
                font=("", 8),
            ).pack()

    # ── IP 刷新 ────────────────────────────────────────────────────────────────

    def _refresh_ips(self):
        """获取当前所有 IP 并更新显示。"""
        ips = get_all_ips()
        self._ip_text.configure(state=tk.NORMAL)
        self._ip_text.delete("1.0", tk.END)
        if not ips:
            self._ip_text.insert(tk.END, "  未检测到网络接口", "err")
        else:
            last_iface = None
            for iface, ip in ips:
                if iface != last_iface:
                    self._ip_text.insert(tk.END, f"\n  {iface}\n", "iface")
                    last_iface = iface
                tag = "ip4" if ":" not in ip else "ip6"
                self._ip_text.insert(tk.END, f"    {ip}\n", tag)
        self._ip_text.configure(state=tk.DISABLED)
        self._ip_time_lbl.configure(text=datetime.now().strftime("%H:%M:%S"))
        self._ip_cache = ips

    def _schedule_ip_refresh(self):
        """每 5 秒检测 IP 变化，有变化则自动刷新。"""

        def _check():
            current = get_all_ips()
            if current != self._ip_cache:
                self._refresh_ips()
            self._root.after(5000, _check)

        self._root.after(5000, _check)

    # ── 服务控制 ───────────────────────────────────────────────────────────────

    def _toggle_server(self):
        if self._running:
            self._stop_server()
        else:
            self._start_server()

    def _start_server(self):
        if self._running:
            return
        host = self._host_var.get().strip() or DEFAULT_HOST
        try:
            port = int(self._port_var.get())
        except ValueError:
            port = DEFAULT_PORT
        self._server_thread = ServerThread(
            host, port, self._on_log_ts, self._on_state_ts
        )
        self._server_thread.start()

    def _stop_server(self):
        if self._server_thread:
            self._server_thread.stop()

    def _restart_server(self):
        self._stop_server()
        self._root.after(900, self._start_server)

    def _on_state_ts(self, running: bool):
        self._root.after(0, self._on_state, running)

    def _on_log_ts(self, text: str, level: str):
        self._root.after(0, self._append_log, text, level)

    def _on_state(self, running: bool):
        self._running = running
        T = THEME
        if running:
            host = self._host_var.get().strip() or DEFAULT_HOST
            try:
                port = int(self._port_var.get())
            except ValueError:
                port = DEFAULT_PORT
            self._dot_lbl.configure(fg=T["ok"])
            self._status_lbl.configure(text=f"运行中  {host}:{port}")
            self._btn_toggle.configure(text="■ 停止服务", bg="#c0392b")
            self._btn_restart.configure(state=tk.NORMAL)
            self._host_entry.configure(state=tk.DISABLED)
            self._port_entry.configure(state=tk.DISABLED)
            self._sb_lbl.configure(text=f"服务运行中  {host}:{port}")
        else:
            self._dot_lbl.configure(fg="#808080")
            self._status_lbl.configure(text="服务未运行")
            self._btn_toggle.configure(text="▶ 启动服务", bg=T["accent"])
            self._btn_restart.configure(state=tk.DISABLED)
            self._host_entry.configure(state=tk.NORMAL)
            self._port_entry.configure(state=tk.NORMAL)
            self._sb_lbl.configure(text="就绪")

    # ── 日志 ───────────────────────────────────────────────────────────────────

    def _append_log(self, text: str, level: str = "info"):
        self._log_text.configure(state=tk.NORMAL)
        ts = datetime.now().strftime("%H:%M:%S")
        self._log_text.insert(tk.END, f"[{ts}] ", "ts")
        self._log_text.insert(tk.END, text + "\n", level)
        self._log_text.configure(state=tk.DISABLED)
        if self._auto_scroll:
            self._log_text.see(tk.END)

    def _clear_log(self):
        self._log_text.configure(state=tk.NORMAL)
        self._log_text.delete("1.0", tk.END)
        self._log_text.configure(state=tk.DISABLED)

    # ── 开机启动 ───────────────────────────────────────────────────────────────

    def _apply_autostart(self):
        if self._autostart_var.get():
            ok, msg = AutostartManager.enable()
        else:
            ok, msg = AutostartManager.disable()
        (messagebox.showinfo if ok else messagebox.showwarning)(
            "开机启动" if ok else "开机启动设置失败",
            msg,
            parent=self._root,
        )

    # ── deb 打包 ───────────────────────────────────────────────────────────────

    def _build_deb(self):
        out_dir = filedialog.askdirectory(
            title="选择输出目录", initialdir=str(Path.home()), parent=self._root
        )
        if not out_dir:
            return
        self._append_log(f"开始构建 .deb 包，输出到: {out_dir}", "info")
        self._deb_thread = DebBuildThread(
            APP_VERSION,
            Path(out_dir),
            self._on_log_ts,
            lambda p: self._root.after(0, self._on_deb_done, p),
        )
        self._deb_thread.start()

    def _on_deb_done(self, deb_path: str):
        if deb_path:
            messagebox.showinfo(
                "打包完成",
                f".deb 包已生成:\n{deb_path}\n\n安装命令:\n  sudo dpkg -i {deb_path}",
                parent=self._root,
            )

    # ── 辅助 ───────────────────────────────────────────────────────────────────

    def _section_label(self, parent: tk.Frame, title: str, pady_top: int = 0):
        T = THEME
        if pady_top:
            tk.Frame(parent, bg=T["bg"], height=pady_top).pack()
        tk.Label(
            parent,
            text=f"  {title}",
            bg=T["bg3"],
            fg=T["fg_dim"],
            anchor=tk.W,
            font=("", 8),
        ).pack(fill=tk.X, pady=(4, 2))

    def _load_settings(self):
        cfg = Path.home() / f".{APP_ID}.cfg"
        if not cfg.exists():
            return
        try:
            for line in cfg.read_text(encoding="utf-8").splitlines():
                k, _, v = line.partition("=")
                k, v = k.strip(), v.strip()
                if k == "host":
                    self._host_var.set(v)
                elif k == "port":
                    self._port_var.set(v)
                elif k == "autostart_server":
                    self._srv_autostart_var.set(v == "1")
                elif k == "win_geo":
                    try:
                        self._root.geometry(v)
                    except Exception:
                        pass
        except Exception:
            pass

    def _save_settings(self):
        cfg = Path.home() / f".{APP_ID}.cfg"
        try:
            cfg.write_text(
                "\n".join(
                    [
                        f"host={self._host_var.get()}",
                        f"port={self._port_var.get()}",
                        f"autostart_server={1 if self._srv_autostart_var.get() else 0}",
                        f"win_geo={self._root.geometry()}",
                    ]
                ),
                encoding="utf-8",
            )
        except Exception:
            pass

    # ── 窗口关闭 ───────────────────────────────────────────────────────────────

    def _on_close(self):
        self._save_settings()
        if self._running:
            self._stop_server()
        self._root.destroy()

    def run(self):
        self._root.mainloop()


# ── 主入口 ──────────────────────────────────────────────────────────────────────
def main():
    MainWindow().run()


if __name__ == "__main__":
    main()
