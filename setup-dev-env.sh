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
# 检测 CPU 架构
# ============================================================================
ARCH=$(uname -m)

# ============================================================================
# 配置 .NET 环境变量（安装到 /usr/share/dotnet 供所有用户使用）
# ============================================================================
configure_dotnet_env() {
    local dotnet_root="$1"
    cat > /etc/profile.d/dotnet.sh <<EOF
export DOTNET_ROOT=${dotnet_root}
export PATH=\$PATH:${dotnet_root}:${dotnet_root}/tools
# 禁用全球化不变模式，确保多语言区域性支持正常工作
export DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=0
EOF
    # 立即在当前 shell 生效
    export DOTNET_ROOT="${dotnet_root}"
    export PATH="$PATH:${dotnet_root}:${dotnet_root}/tools"

    # 创建符号链接，使 dotnet 命令全局可用
    if [ ! -f /usr/local/bin/dotnet ] && [ -f "${dotnet_root}/dotnet" ]; then
        ln -sf "${dotnet_root}/dotnet" /usr/local/bin/dotnet
    fi
}

# ============================================================================
# 通过官方 dotnet-install.sh 脚本安装 .NET（适用于 ARM64 等架构）
# ============================================================================
install_dotnet_via_script() {
    local install_dir="/usr/share/dotnet"

    log_info "使用官方安装脚本安装 .NET 10 SDK（架构: ${ARCH}）..."
    apt-get install -y -qq wget libicu-dev libssl-dev 2>/dev/null || dnf install -y wget libicu-devel openssl-devel 2>/dev/null || true

    wget -q https://dot.net/v1/dotnet-install.sh -O /tmp/dotnet-install.sh
    chmod +x /tmp/dotnet-install.sh
    /tmp/dotnet-install.sh --channel 10.0 --install-dir "${install_dir}"
    rm -f /tmp/dotnet-install.sh

    configure_dotnet_env "${install_dir}"
}

# ============================================================================
# .NET 10 SDK 安装
# ============================================================================
install_dotnet() {
    log_info "开始安装 .NET 10 SDK ..."
    log_info "检测到 CPU 架构: ${ARCH}"

    if command -v dotnet &> /dev/null; then
        local current_version
        current_version=$(dotnet --version 2>/dev/null || echo "未知")
        log_warn ".NET SDK 已安装，当前版本: ${current_version}"
    fi

    # ARM64 架构（如 RK3588）：Microsoft apt 源不支持，使用安装脚本
    if [ "$ARCH" = "aarch64" ] || [ "$ARCH" = "arm64" ]; then
        log_info "ARM64 架构检测到，Microsoft apt 源暂不支持，使用官方安装脚本 ..."
        install_dotnet_via_script
        log_ok ".NET SDK 安装完成（ARM64 脚本方式）"
        return
    fi

    # x86_64 架构：优先使用包管理器
    case "$OS_ID" in
        ubuntu|debian)
            apt-get update -qq
            apt-get install -y -qq wget apt-transport-https software-properties-common

            # 安装 Microsoft 包签名密钥和源
            if wget -q "https://packages.microsoft.com/config/${OS_ID}/${OS_VERSION}/packages-microsoft-prod.deb" -O /tmp/packages-microsoft-prod.deb 2>/dev/null; then
                dpkg -i /tmp/packages-microsoft-prod.deb
                rm -f /tmp/packages-microsoft-prod.deb
                apt-get update -qq
                apt-get install -y -qq dotnet-sdk-10.0 && { log_ok ".NET SDK 安装完成"; return; }
            fi

            # apt 安装失败时回退到脚本安装
            log_warn "apt 安装失败，回退到官方安装脚本 ..."
            install_dotnet_via_script
            ;;
        centos|rhel|rocky|almalinux|fedora)
            dnf install -y dotnet-sdk-10.0 || {
                log_warn "dnf 安装失败，回退到官方安装脚本 ..."
                install_dotnet_via_script
            }
            ;;
        *)
            install_dotnet_via_script
            ;;
    esac

    log_ok ".NET SDK 安装完成"
}

# ============================================================================
# C++ 编译工具链安装 (gcc, g++, cmake, make, gdb, OpenCV, OCR 依赖)
# ============================================================================
install_cpp() {
    log_info "开始安装 C++ 编译工具链 ..."

    case "$OS_ID" in
        ubuntu|debian)
            apt-get update -qq
            # 基础编译工具链
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

            # OpenCV 和 OCR 相关依赖库（Aurora CV Engine 需要）
            log_info "安装 OpenCV / OCR 相关依赖库 ..."
            apt-get install -y -qq \
                libopencv-dev \
                libopencv-contrib-dev \
                libtesseract-dev \
                tesseract-ocr \
                tesseract-ocr-chi-sim \
                tesseract-ocr-chi-tra \
                tesseract-ocr-eng \
                libleptonica-dev \
                libgtk-3-dev \
                libavcodec-dev \
                libavformat-dev \
                libswscale-dev \
                libjpeg-dev \
                libpng-dev \
                libtiff-dev \
                2>/dev/null || log_warn "部分 OpenCV/OCR 包可能不可用，请根据需要手动安装"
            ;;
        centos|rhel|rocky|almalinux|fedora)
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
                libtool \
                opencv-devel \
                tesseract-devel \
                tesseract-langpack-chi-sim \
                leptonica-devel \
                2>/dev/null || log_warn "部分 OpenCV/OCR 包可能不可用"
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

    if pkg-config --modversion opencv4 &> /dev/null; then
        echo -e " OpenCV:      ${GREEN}$(pkg-config --modversion opencv4)${NC}"
    else
        echo -e " OpenCV:      ${YELLOW}未检测到（可选）${NC}"
    fi

    if command -v tesseract &> /dev/null; then
        echo -e " Tesseract:   ${GREEN}$(tesseract --version 2>&1 | head -n1)${NC}"
    else
        echo -e " Tesseract:   ${YELLOW}未检测到（可选）${NC}"
    fi

    echo -e " 架构:        ${CYAN}$(uname -m)${NC}"
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
