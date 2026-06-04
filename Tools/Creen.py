#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
结构光条纹区域生成与验证工具
基于单行/单列存储+复制投射的生成方式
支持竖条纹(W方向)和横条纹(H方向)
"""


def generate_and_validate_stripes(direction, size, period, num_images, phase_shift):
    """
    生成条纹区域并进行验证

    参数:
        direction: 条纹方向，'W'表示竖条纹，'H'表示横条纹
        size: 条纹方向的尺寸，竖条纹为宽度W，横条纹为高度H
        period: 单个周期的像素数T
        num_images: 相移图的数量N
        phase_shift: 相邻两张图的像素相移步长Δ

    返回:
        所有图片的区域信息列表
    """
    # 参数验证
    if direction not in ["W", "H"]:
        raise ValueError("方向必须是'W'(竖条纹)或'H'(横条纹)")

    if size % period != 0:
        raise ValueError(f"尺寸{size}必须是周期{period}的整数倍")

    if phase_shift <= 0 or phase_shift >= period:
        raise ValueError(f"相移必须满足 0 < Δ < {period}")

    if num_images < 2:
        raise ValueError("图片数量至少为2张")

    num_regions = size // period
    print(f"{'='*60}")
    print(f"条纹方向: {direction}")
    print(f"尺寸: {size} 像素")
    print(f"周期: {period} 像素/周期")
    print(f"总区域数: {num_regions} 个")
    print(f"图片数量: {num_images} 张")
    print(f"相移步长: {phase_shift} 像素")
    print(f"{'='*60}\n")

    all_images_regions = []

    for n in range(1, num_images + 1):
        total_shift = (n - 1) * phase_shift
        effective_shift = total_shift % period  # 取模得到有效相移

        print(
            f"第{n}张图 (总相移: {total_shift} 像素, 有效相移: {effective_shift} 像素)"
        )
        print(f"{'-'*60}")

        regions = []
        total_length = 0
        all_valid = True

        for k in range(1, num_regions + 1):
            # 计算原始起点和终点（1开始索引）
            start = (k - 1) * period + 1 - effective_shift
            end = k * period - effective_shift

            region_info = {
                "region_num": k,
                "original_start": start,
                "original_end": end,
                "segments": [],
            }

            if start >= 1:
                # 正常区域，不跨边界
                length = end - start + 1
                region_info["segments"].append((start, end, length))
                total_length += length

                # 验证区域长度
                if length != period:
                    all_valid = False
                    print(f"  区域{k}: 错误! 长度{length} ≠ 周期{period}")
                else:
                    print(f"  区域{k}: [{start}, {end}] 长度: {length} ✓")
            else:
                # 跨边界区域，拆分为两段
                # 第一段: [1, end]
                seg1_start = 1
                seg1_end = end
                seg1_length = seg1_end - seg1_start + 1

                # 第二段: [size + start, size]
                seg2_start = size + start
                seg2_end = size
                seg2_length = seg2_end - seg2_start + 1

                total_length += seg1_length + seg2_length
                region_info["segments"].append((seg1_start, seg1_end, seg1_length))
                region_info["segments"].append((seg2_start, seg2_end, seg2_length))

                # 验证区域总长度
                total_region_length = seg1_length + seg2_length
                if total_region_length != period:
                    all_valid = False
                    print(
                        f"  区域{k}: 错误! 总长度{total_region_length} ≠ 周期{period}"
                    )
                else:
                    print(
                        f"  区域{k}: [{seg1_start}, {seg1_end}] + [{seg2_start}, {seg2_end}] 总长度: {total_region_length} ✓"
                    )

        regions.append(region_info)

        # 验证总长度
        print(
            f"\n  总长度验证: {total_length} {'=' if total_length == size else '≠'} {size}"
        )
        if total_length != size:
            all_valid = False
            print(f"  错误! 所有区域长度之和{total_length} ≠ 尺寸{size}")
        else:
            print(f"  总长度验证通过 ✓")

        if all_valid:
            print(f"  第{n}张图所有验证通过 ✅")
        else:
            print(f"  第{n}张图存在验证错误 ❌")

        print(f"{'-'*60}\n")
        all_images_regions.append(regions)

    print(f"{'='*60}")
    print("所有图片生成与验证完成")
    print(f"{'='*60}")

    return all_images_regions


def main():
    print("结构光条纹区域生成与验证工具")
    print("----------------------------")

    # 获取用户输入
    while True:
        direction = input("请选择条纹方向 (W-竖条纹/H-横条纹): ").strip().upper()
        if direction in ["W", "H"]:
            break
        print("输入错误，请输入'W'或'H'")

    while True:
        try:
            size = int(
                input(f"请输入{direction}方向尺寸 (默认1280/720): ").strip()
                or (1280 if direction == "W" else 720)
            )
            if size > 0:
                break
            print("尺寸必须大于0")
        except ValueError:
            print("请输入有效的整数")

    while True:
        try:
            period = int(input("请输入周期T (像素/周期): ").strip())
            if period >= 2:
                if size % period == 0:
                    break
                else:
                    print(f"错误: 尺寸{size}必须是周期{period}的整数倍")
                    print(
                        f"建议周期值: {[t for t in range(2, size//2+1) if size % t == 0]}"
                    )
            else:
                print("周期必须大于等于2")
        except ValueError:
            print("请输入有效的整数")

    while True:
        try:
            num_images = int(input("请输入图片数量 (默认4): ").strip() or 4)
            if num_images >= 2:
                break
            print("图片数量至少为2张")
        except ValueError:
            print("请输入有效的整数")

    while True:
        try:
            phase_shift = int(
                input(
                    f"请输入相移步长Δ (像素, 1~{period-1}, 默认{period//4}): "
                ).strip()
                or (period // 4)
            )
            if 0 < phase_shift < period:
                break
            print(f"相移必须在1到{period-1}之间")
        except ValueError:
            print("请输入有效的整数")

    # 生成并验证
    generate_and_validate_stripes(direction, size, period, num_images, phase_shift)


if __name__ == "__main__":
    main()
