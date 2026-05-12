"""
检查缺少 common.props 引用的 csproj 项目，并同步 Assets 资源文件夹
功能：
  1. 扫描指定源码目录下所有 *.csproj 文件，输出未引用 common.props 的项目列表
  2. 确保每个 csproj 所在目录存在 Assets 文件夹（不存在则创建）
  3. 清空 Assets 文件夹内容，然后将全局 Assets 目录中的所有文件复制进去
"""

import os
import re
import shutil

# 源码根目录
SOURCE_ROOT = r"D:\GitRepos\Aurora Application\Sources"

# 全局 Assets 目录（复制来源）
GLOBAL_ASSETS = r"D:\GitRepos\Aurora Application\Assets"


def sync_assets(csproj_dir: str, global_assets: str) -> None:
    """
    同步全局 Assets 文件夹到 csproj 所在目录的 Assets 子目录

    :param csproj_dir: csproj 文件所在目录
    :param global_assets: 全局 Assets 来源目录
    """
    assets_dir = os.path.join(csproj_dir, "Assets")

    # 不存在则创建
    if not os.path.exists(assets_dir):
        os.makedirs(assets_dir)
        print(f"    [创建] Assets 目录：{assets_dir}")
    else:
        # 清空现有内容
        for item in os.listdir(assets_dir):
            item_path = os.path.join(assets_dir, item)
            if os.path.isfile(item_path) or os.path.islink(item_path):
                os.remove(item_path)
            elif os.path.isdir(item_path):
                shutil.rmtree(item_path)
        print(f"    [清空] Assets 目录：{assets_dir}")

    # 复制全局 Assets 中的所有文件（仅文件，不递归子目录）
    copied = 0
    for item in os.listdir(global_assets):
        src = os.path.join(global_assets, item)
        dst = os.path.join(assets_dir, item)
        if os.path.isfile(src):
            shutil.copy2(src, dst)
            copied += 1
    print(f"    [复制] {copied} 个文件 -> {assets_dir}")


def check_csproj_files(source_root: str, global_assets: str) -> None:
    """
    扫描源码目录，检查所有 csproj 文件是否引用了 common.props，
    并同步 Assets 资源文件夹到每个 csproj 所在目录

    :param source_root: 源码根目录路径
    :param global_assets: 全局 Assets 来源目录
    """
    # 匹配 <Import Project="...common.props" /> 的正则
    pattern = re.compile(r'<Import\s+Project="[^"]*common\.props"', re.IGNORECASE)

    missing_list = []
    found_list = []

    for root, _, files in os.walk(source_root):
        for file in files:
            if not file.endswith(".csproj"):
                continue

            csproj_path = os.path.join(root, file)
            try:
                with open(csproj_path, "r", encoding="utf-8") as f:
                    content = f.read()
            except UnicodeDecodeError:
                # 尝试 gbk 编码
                with open(csproj_path, "r", encoding="gbk") as f:
                    content = f.read()

            rel_path = os.path.relpath(csproj_path, source_root)
            if pattern.search(content):
                found_list.append(rel_path)
            else:
                missing_list.append(rel_path)

            # 同步 Assets 到 csproj 所在目录
            print(f"  处理：{rel_path}")
            sync_assets(root, global_assets)

    # 输出统计
    total = len(found_list) + len(missing_list)
    print()
    print(f"扫描完成，共找到 {total} 个 csproj 文件")
    print(f"  [OK] 已引用 common.props：{len(found_list)} 个")
    print(f"  [缺失] 未引用 common.props：{len(missing_list)} 个")
    print()

    if missing_list:
        print("以下项目未引用 common.props：")
        print("-" * 60)
        for path in sorted(missing_list):
            print(f"  {path}")
    else:
        print("所有项目均已引用 common.props，无需处理。")


if __name__ == "__main__":
    if not os.path.isdir(SOURCE_ROOT):
        print(f"错误：源码目录不存在 -> {SOURCE_ROOT}")
        exit(1)

    if not os.path.isdir(GLOBAL_ASSETS):
        print(f"错误：全局 Assets 目录不存在 -> {GLOBAL_ASSETS}")
        exit(1)

    check_csproj_files(SOURCE_ROOT, GLOBAL_ASSETS)
