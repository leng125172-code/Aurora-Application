#!/bin/bash
# ============================================================================
# Aurora Application 开发环境安装脚本
# 支持系统: Ubuntu/Debian, CentOS/RHEL/Fedora
# 安装内容: .NET 10 SDK, C++ 编译工具链, Python 3 开发环境
# ============================================================================

set -e

# 颜色定义
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
NC='\033[0m'

# 日志函数
log_info()  { echo -e "${CYAN}[信息]${NC} $1"; }
log_ok()    { echo -e "${GREEN}[完成]${NC} $1"; }
log_warn()  { echo -e "${YELLOW}[警告]${NC} $1"; }
log_error() { echo -e "${RED}[错误]${NC} $1"; }

# 检测操作系统类型
detect_os() {
    if [ -f /etc/os-release ]; then
        . /etc/os-release
        OS_ID="$ID"
        OS_VERSION="$VERSION_ID"
    else
        log_error "无法检测操作系统类型，请确认系统支持 /etc/os-release"
        exit 1
    fi
}

# 检查是否以 root 或 sudo 运行
check_privileges() {
    if [ "$(id -u)" -ne 0 ]; then
        log_error "请使用 sudo 运行此脚本: sudo bash $0"
        exit 1
    fi
}

# ============================================================================
# .NET 10 SDK 安装
# ============================================================================
install_dotnet() {
    log_info "开始安装 .NET 10 SDK ..."

    if command -v dotnet &> /dev/null; then
        local current_version
        current_version=$(dotnet --version 2>/dev/null || echo "未知")
        log_warn ".NET SDK 已安装，当前版本: ${current_version}"
    fi

    case "$OS_ID" in
        ubuntu|debian)
            # 添加 Microsoft 包源
            apt-get update -qq
            apt-get install -y -qq wget apt-transport-https software-properties-common

            # 安装 Microsoft 包签名密钥和源
            wget -q "https://packages.microsoft.com/config/${OS_ID}/${OS_VERSION}/packages-microsoft-prod.deb" -O packages-microsoft-prod.deb
            dpkg -i packages-microsoft-prod.deb
            rm -f packages-microsoft-prod.deb

            apt-get update -qq
            apt-get install -y -qq dotnet-sdk-10.0
            ;;
        centos|rhel|rocky|almalinux)
            dnf install -y dotnet-sdk-10.0
            ;;
        fedora)
            dnf install -y dotnet-sdk-10.0
            ;;
        *)
            log_warn "未识别的发行版 ${OS_ID}，尝试使用官方安装脚本 ..."
            wget -q https://dot.net/v1/dotnet-install.sh -O dotnet-install.sh
            chmod +x dotnet-install.sh
            ./dotnet-install.sh --channel 10.0
            rm -f dotnet-install.sh

            # 配置环境变量
            local dotnet_root="$HOME/.dotnet"
            if ! grep -q "DOTNET_ROOT" /etc/profile.d/dotnet.sh 2>/dev/null; then
                cat > /etc/profile.d/dotnet.sh <<EOF
export DOTNET_ROOT=${dotnet_root}
export PATH=\$PATH:${dotnet_root}:${dotnet_root}/tools
EOF
            fi
            ;;
    esac

    log_ok ".NET SDK 安装完成"
}

# ============================================================================
# C++ 编译工具链安装 (gcc, g++, cmake, make, gdb)
# ============================================================================
install_cpp() {
    log_info "开始安装 C++ 编译工具链 ..."

    case "$OS_ID" in
        ubuntu|debian)
            apt-get update -qq
            apt-get install -y -qq \
                build-essential \
                gcc \
                g++ \
                cmake \
                make \
                gdb \
                clang \
                lldb \
                pkg-config \
                autoconf \
                automake \
                libtool
            ;;
        centos|rhel|rocky|almalinux)
            dnf groupinstall -y "Development Tools"
            dnf install -y \
                gcc \
                gcc-c++ \
                cmake \
                make \
                gdb \
                clang \
                pkg-config \
                autoconf \
                automake \
                libtool
            ;;
        fedora)
            dnf groupinstall -y "Development Tools"
            dnf install -y \
                gcc \
                gcc-c++ \
                cmake \
                make \
                gdb \
                clang \
                pkg-config
            ;;
        *)
            log_error "不支持的发行版: ${OS_ID}，请手动安装 C++ 工具链"
            return 1
            ;;
    esac

    log_ok "C++ 编译工具链安装完成"
}

# ============================================================================
# Python 3 开发环境安装 (python3, pip, venv)
# ============================================================================
install_python() {
    log_info "开始安装 Python 3 开发环境 ..."

    case "$OS_ID" in
        ubuntu|debian)
            apt-get update -qq
            apt-get install -y -qq \
                python3 \
                python3-pip \
                python3-venv \
                python3-dev \
                python3-setuptools \
                python3-wheel
            ;;
        centos|rhel|rocky|almalinux)
            dnf install -y \
                python3 \
                python3-pip \
                python3-devel \
                python3-setuptools \
                python3-wheel
            ;;
        fedora)
            dnf install -y \
                python3 \
                python3-pip \
                python3-devel \
                python3-setuptools \
                python3-wheel
            ;;
        *)
            log_error "不支持的发行版: ${OS_ID}，请手动安装 Python 3"
            return 1
            ;;
    esac

    # 升级 pip 到最新版本
    python3 -m pip install --upgrade pip 2>/dev/null || true

    log_ok "Python 3 开发环境安装完成"
}

# ============================================================================
# 打印安装结果摘要
# ============================================================================
print_summary() {
    echo ""
    echo "============================================"
    echo " Aurora Application 开发环境安装摘要"
    echo "============================================"

    if command -v dotnet &> /dev/null; then
        echo -e " .NET SDK:    ${GREEN}$(dotnet --version 2>/dev/null)${NC}"
    else
        echo -e " .NET SDK:    ${RED}未找到（请重新登录终端后重试）${NC}"
    fi

    if command -v gcc &> /dev/null; then
        echo -e " GCC:         ${GREEN}$(gcc --version | head -n1)${NC}"
    else
        echo -e " GCC:         ${RED}未安装${NC}"
    fi

    if command -v g++ &> /dev/null; then
        echo -e " G++:         ${GREEN}$(g++ --version | head -n1)${NC}"
    else
        echo -e " G++:         ${RED}未安装${NC}"
    fi

    if command -v cmake &> /dev/null; then
        echo -e " CMake:       ${GREEN}$(cmake --version | head -n1)${NC}"
    else
        echo -e " CMake:       ${RED}未安装${NC}"
    fi

    if command -v python3 &> /dev/null; then
        echo -e " Python:      ${GREEN}$(python3 --version 2>&1)${NC}"
    else
        echo -e " Python:      ${RED}未安装${NC}"
    fi

    if command -v pip3 &> /dev/null; then
        echo -e " Pip:         ${GREEN}$(pip3 --version 2>&1)${NC}"
    else
        echo -e " Pip:         ${RED}未安装${NC}"
    fi

    echo "============================================"
    echo ""
    log_info "如果 dotnet 命令未生效，请执行: source /etc/profile.d/dotnet.sh 或重新登录终端"
}

# ============================================================================
# 主流程
# ============================================================================
main() {
    echo "============================================"
    echo " Aurora Application 开发环境安装脚本"
    echo " .NET 10 + C++ + Python 3"
    echo "============================================"
    echo ""

    check_privileges
    detect_os
    log_info "检测到操作系统: ${OS_ID} ${OS_VERSION}"

    install_dotnet
    install_cpp
    install_python
    print_summary

    log_ok "所有开发环境安装完成！"
}

main "$@"
