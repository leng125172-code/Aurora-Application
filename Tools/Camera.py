#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
双远心镜头参数计算器
Bi-Telecentric Lens Parameter Calculator

已知量：CMOS 尺寸、物体尺寸（mm）
计算量：放大倍率、工作距离(WD)、景深(DOF)、数值孔径(NA)、分辨率等
"""

import math


def calculate_telecentric_params(
    cmos_width_mm: float,
    cmos_height_mm: float,
    object_width_mm: float,
    object_height_mm: float,
    pixel_size_um: float = None,
    f_number: float = None,
    wavelength_nm: float = 550.0,
    lens_focal_length_mm: float = None,
):
    """
    计算双远心镜头核心参数。

    参数说明
    --------
    cmos_width_mm   : CMOS 宽度 (mm)
    cmos_height_mm  : CMOS 高度 (mm)
    object_width_mm : 被测物体宽度 (mm)
    object_height_mm: 被测物体高度 (mm)
    pixel_size_um   : 像素尺寸 (μm)，可选，用于计算像素分辨率
    f_number        : 镜头 F# (光圈数)，可选，用于计算景深与 NA
    wavelength_nm   : 工作波长 (nm)，默认 550 nm（可见光中心）
    lens_focal_length_mm : 像方焦距 (mm)，可选，用于估算 WD

    返回
    ----
    dict，包含所有计算结果
    """

    results = {}

    # ── 1. 放大倍率 (Magnification) ──────────────────────────────────────────
    # 取宽度和高度各自的倍率，取最小值（限制轴）作为实际工作倍率
    mag_w = cmos_width_mm / object_width_mm
    mag_h = cmos_height_mm / object_height_mm
    magnification = min(mag_w, mag_h)  # 以最紧约束轴为准
    magnification_w = mag_w
    magnification_h = mag_h

    results["magnification_width_axis"] = round(mag_w, 4)
    results["magnification_height_axis"] = round(mag_h, 4)
    results["recommended_magnification"] = round(magnification, 4)

    # 实际 CMOS 利用率（FOV vs CMOS）
    fov_w = object_width_mm
    fov_h = object_height_mm
    actual_cmos_w = fov_w * magnification
    actual_cmos_h = fov_h * magnification
    results["effective_sensor_width_mm"] = round(actual_cmos_w, 3)
    results["effective_sensor_height_mm"] = round(actual_cmos_h, 3)
    results["sensor_utilization_pct"] = round(
        (actual_cmos_w * actual_cmos_h) / (cmos_width_mm * cmos_height_mm) * 100, 1
    )

    # ── 2. 像素分辨率（Ground Sampling Distance, GSD） ──────────────────────
    if pixel_size_um is not None:
        gsd_um = pixel_size_um / magnification  # 物方每像素尺寸 (μm)
        gsd_mm = gsd_um / 1000.0
        results["pixel_size_um"] = pixel_size_um
        results["gsd_um_per_pixel"] = round(gsd_um, 4)  # 物方分辨率 μm/pixel
        results["gsd_mm_per_pixel"] = round(gsd_mm, 6)

    # ── 3. 数值孔径 NA 与分辨率 ─────────────────────────────────────────────
    if f_number is not None:
        # 像方 NA（像侧）
        na_image = 1.0 / (2.0 * f_number)
        # 物方 NA（物侧）
        na_object = na_image / magnification
        results["f_number"] = f_number
        results["na_image_side"] = round(na_image, 4)
        results["na_object_side"] = round(na_object, 4)

        # 瑞利分辨率（物方）
        wavelength_mm = wavelength_nm / 1e6
        rayleigh_resolution_mm = 0.61 * wavelength_mm / na_object
        results["rayleigh_resolution_um"] = round(rayleigh_resolution_mm * 1000, 3)

    # ── 4. 景深 DOF (Depth of Field) ────────────────────────────────────────
    #
    # 双远心镜头景深公式（物方）：
    #   DOF = ±  (pixel_size / magnification) / na_object
    #       = ± pixel_size / (magnification * na_object)
    #
    # 若无像素尺寸，则用衍射极限模糊圆直径 d = wavelength / NA 代替
    #
    if f_number is not None:
        na_object = (1.0 / (2.0 * f_number)) / magnification
        wavelength_mm = wavelength_nm / 1e6

        if pixel_size_um is not None:
            blur_circle_mm = (pixel_size_um / 1000.0) / magnification
        else:
            blur_circle_mm = wavelength_mm / na_object  # 衍射极限

        dof_mm = blur_circle_mm / na_object
        results["dof_total_mm"] = round(dof_mm, 4)  # 总景深（±DOF/2）
        results["dof_half_mm"] = round(dof_mm / 2, 4)  # 单侧景深

    # ── 5. 工作距离估算 WD ──────────────────────────────────────────────────
    #
    # 双远心镜头结构：物方焦距 f_obj = f_image / magnification
    # WD ≈ 物方焦距（远心条件下，物在前焦面附近）
    # 实际 WD 由镜头机械设计决定，这里给出估算值。
    #
    if lens_focal_length_mm is not None:
        f_obj_mm = lens_focal_length_mm / magnification
        wd_estimated_mm = f_obj_mm  # 近似等于物方焦距
        results["image_focal_length_mm"] = round(lens_focal_length_mm, 2)
        results["object_focal_length_mm"] = round(f_obj_mm, 2)
        results["wd_estimated_mm"] = round(wd_estimated_mm, 2)

    # ── 6. 汇总输入参数 ──────────────────────────────────────────────────────
    results["_input"] = {
        "cmos_width_mm": cmos_width_mm,
        "cmos_height_mm": cmos_height_mm,
        "object_width_mm": object_width_mm,
        "object_height_mm": object_height_mm,
        "pixel_size_um": pixel_size_um,
        "f_number": f_number,
        "wavelength_nm": wavelength_nm,
        "lens_focal_length_mm": lens_focal_length_mm,
    }

    return results


def print_report(results: dict):
    """格式化输出计算报告"""
    inp = results.get("_input", {})

    print("=" * 60)
    print("       双远心镜头参数计算报告")
    print("       Bi-Telecentric Lens Parameter Report")
    print("=" * 60)

    print("\n【输入参数 / Inputs】")
    print(
        f"  CMOS 尺寸            : {inp['cmos_width_mm']} × {inp['cmos_height_mm']} mm"
    )
    print(
        f"  物体尺寸 (FOV)       : {inp['object_width_mm']} × {inp['object_height_mm']} mm"
    )
    if inp.get("pixel_size_um"):
        print(f"  像素尺寸             : {inp['pixel_size_um']} μm")
    if inp.get("f_number"):
        print(f"  F#                   : f/{inp['f_number']}")
    print(f"  工作波长             : {inp['wavelength_nm']} nm")
    if inp.get("lens_focal_length_mm"):
        print(f"  像方焦距 (参考)      : {inp['lens_focal_length_mm']} mm")

    print("\n【放大倍率 / Magnification】")
    print(f"  宽度轴倍率           : {results['magnification_width_axis']}×")
    print(f"  高度轴倍率           : {results['magnification_height_axis']}×")
    print(f"  推荐工作倍率 (取小值) : {results['recommended_magnification']}×")

    print("\n【传感器利用率 / Sensor Utilization】")
    print(
        f"  有效成像区域         : {results['effective_sensor_width_mm']} × {results['effective_sensor_height_mm']} mm"
    )
    print(f"  CMOS 利用率          : {results['sensor_utilization_pct']} %")

    if "gsd_um_per_pixel" in results:
        print("\n【像素分辨率 / Pixel Resolution (GSD)】")
        print(f"  物方像素尺寸 (GSD)   : {results['gsd_um_per_pixel']} μm/pixel")
        print(f"                         ({results['gsd_mm_per_pixel']} mm/pixel)")

    if "na_object_side" in results:
        print("\n【数值孔径 / Numerical Aperture】")
        print(f"  像方 NA              : {results['na_image_side']}")
        print(f"  物方 NA              : {results['na_object_side']}")

    if "rayleigh_resolution_um" in results:
        print("\n【光学分辨率 / Optical Resolution】")
        print(f"  瑞利分辨率 (物方)    : {results['rayleigh_resolution_um']} μm")

    if "dof_total_mm" in results:
        print("\n【景深 / Depth of Field】")
        print(f"  总景深 (DOF)         : {results['dof_total_mm']} mm")
        print(f"  单侧景深 (±DOF/2)    : ±{results['dof_half_mm']} mm")

    if "wd_estimated_mm" in results:
        print("\n【工作距离估算 / Working Distance】")
        print(f"  像方焦距             : {results['image_focal_length_mm']} mm")
        print(f"  物方焦距             : {results['object_focal_length_mm']} mm")
        print(f"  估算 WD              : {results['wd_estimated_mm']} mm")

    print("\n" + "=" * 60)


# ── 示例用法 / Example Usage ─────────────────────────────────────────────────
if __name__ == "__main__":
    import argparse

    parser = argparse.ArgumentParser(
        description="双远心镜头参数计算器 / Bi-Telecentric Lens Calculator"
    )
    parser.add_argument("--cmos_w", type=float, required=True, help="CMOS 宽度 (mm)")
    parser.add_argument("--cmos_h", type=float, required=True, help="CMOS 高度 (mm)")
    parser.add_argument("--obj_w", type=float, required=True, help="物体宽度 (mm)")
    parser.add_argument("--obj_h", type=float, required=True, help="物体高度 (mm)")
    parser.add_argument("--pixel", type=float, default=None, help="像素尺寸 (μm)，可选")
    parser.add_argument("--fnum", type=float, default=None, help="F# 光圈数，可选")
    parser.add_argument("--wl", type=float, default=550.0, help="波长 (nm)，默认 550")
    parser.add_argument("--focal", type=float, default=None, help="像方焦距 (mm)，可选")

    args = parser.parse_args()

    res = calculate_telecentric_params(
        cmos_width_mm=args.cmos_w,
        cmos_height_mm=args.cmos_h,
        object_width_mm=args.obj_w,
        object_height_mm=args.obj_h,
        pixel_size_um=args.pixel,
        f_number=args.fnum,
        wavelength_nm=args.wl,
        lens_focal_length_mm=args.focal,
    )

    print_report(res)

    # ── 内置演示（无命令行参数时执行） ──────────────────────────────────────
    # 运行示例：
    # python telecentric_lens_calculator.py \
    #     --cmos_w 17.6 --cmos_h 13.2 \
    #     --obj_w 50 --obj_h 37.5 \
    #     --pixel 4.5 --fnum 8 --focal 75
