#!/usr/bin/env python3
"""
分析相机拍摄的 PNG 图片（12-bit RGB 格式）
检查亮度、对比度、噪声、标定板检测等指标
"""

import os
import sys
import numpy as np
import cv2


def analyze_image_quality(filepath):
    """分析图像质量指标"""
    print(f"\n{'='*60}")
    print(f"分析文件: {os.path.basename(filepath)}")
    print(f"文件大小: {os.path.getsize(filepath):,} 字节")
    print('='*60)
    
    # 读取图像（16-bit）
    img = cv2.imread(filepath, cv2.IMREAD_UNCHANGED)
    if img is None:
        print("ERROR: 无法读取图像")
        return
    
    height, width = img.shape[:2]
    channels = 1 if len(img.shape) == 2 else img.shape[2]
    dtype = img.dtype
    
    print(f"\n=== 基本信息 ===")
    print(f"尺寸: {width} x {height}")
    print(f"通道: {channels}")
    print(f"数据类型: {dtype}")
    
    # 转换为 float 进行计算
    img_float = img.astype(np.float32)
    
    print(f"\n=== 像素值统计 ===")
    print(f"全局范围: [{img.min()}, {img.max()}]")
    
    # 各通道统计
    if channels == 3:
        for i, name in enumerate(['B', 'G', 'R']):
            channel = img_float[:, :, i]
            print(f"\n{name} 通道:")
            print(f"  均值: {channel.mean():.2f}")
            print(f"  标准差: {channel.std():.2f}")
            print(f"  最小值: {channel.min():.2f}")
            print(f"  最大值: {channel.max():.2f}")
            print(f"  动态范围: {channel.max() - channel.min():.2f}")
            print(f"  占满度: {(channel.max() / 4095 * 100):.2f}%")
    else:
        print(f"均值: {img_float.mean():.2f}")
        print(f"标准差: {img_float.std():.2f}")
    
    # 亮度统计
    print(f"\n=== 亮度分析 ===")
    if channels == 3:
        # 计算灰度图
        gray = cv2.cvtColor(img, cv2.COLOR_BGR2GRAY)
    else:
        gray = img
    
    gray_float = gray.astype(np.float32)
    print(f"灰度均值: {gray_float.mean():.2f}")
    print(f"灰度标准差: {gray_float.std():.2f}")
    
    # 直方图分析
    hist, bins = np.histogram(gray, bins=256, range=(0, 4096))
    total_pixels = width * height
    cdf = hist.cumsum() / total_pixels
    
    # 找到有效范围
    lower_idx = np.argmax(cdf > 0.01)
    upper_idx = np.argmax(cdf >= 0.99)
    lower_bound = bins[lower_idx]
    upper_bound = bins[upper_idx]
    
    print(f"有效动态范围: [{lower_bound:.0f}, {upper_bound:.0f}]")
    print(f"对比度(标准差/均值): {gray_float.std() / max(gray_float.mean(), 1):.4f}")
    
    # 噪声估计（使用暗区）
    print(f"\n=== 噪声分析 ===")
    dark_region = gray_float[gray_float < gray_float.mean() * 0.3]
    if len(dark_region) > 100:
        noise_std = dark_region.std()
        noise_mean = dark_region.mean()
        print(f"暗区噪声标准差: {noise_std:.2f}")
        print(f"暗区噪声均值: {noise_mean:.2f}")
        print(f"SNR(信号/噪声): {gray_float.mean() / max(noise_std, 1):.2f}")
    
    # 边缘检测
    print(f"\n=== 边缘分析 ===")
    gray_8bit = (gray / 16).astype(np.uint8)
    edges = cv2.Canny(gray_8bit, 50, 150)
    edge_ratio = edges.sum() / (width * height * 255)
    print(f"边缘像素占比: {edge_ratio * 100:.2f}%")
    
    # 保存预览图（转换为8-bit）
    img_8bit = (img / 16).astype(np.uint8)
    preview_path = filepath.replace('.png', '_preview_8bit.png')
    cv2.imwrite(preview_path, img_8bit)
    print(f"\n预览图已保存: {preview_path}")
    
    return img, img_8bit, gray_8bit


def detect_calibration_board(img_8bit, filepath):
    """尝试检测标定板"""
    print(f"\n=== 标定板检测 ===")
    
    # 尝试检测圆点标定板 (27x27)
    print("检测圆点标定板 (27x27)...")
    pattern_size = (27, 27)
    try:
        found, centers = cv2.findCirclesGrid(img_8bit, pattern_size, 
                                              flags=cv2.CALIB_CB_SYMMETRIC_GRID)
        if found:
            print(f"  ✓ 成功! 检测到 {len(centers)} 个圆点")
            # 绘制检测结果
            result = cv2.drawChessboardCorners(img_8bit.copy(), pattern_size, centers, found)
            save_path = filepath.replace('.png', '_circle_detection.png')
            cv2.imwrite(save_path, result)
            print(f"  检测结果已保存: {save_path}")
        else:
            print(f"  ✗ 未检测到圆点标定板")
    except Exception as e:
        print(f"  ✗ 检测失败: {e}")
    
    # 尝试检测棋盘格标定板 (15x8)
    print("检测棋盘格标定板 (15x8)...")
    pattern_size = (15, 8)
    try:
        found, corners = cv2.findChessboardCorners(img_8bit, pattern_size,
                                                   flags=cv2.CALIB_CB_ADAPTIVE_THRESH |
                                                         cv2.CALIB_CB_NORMALIZE_IMAGE)
        if found:
            print(f"  ✓ 成功! 检测到 {len(corners)} 个角点")
            # 绘制检测结果
            result = cv2.drawChessboardCorners(img_8bit.copy(), pattern_size, corners, found)
            save_path = filepath.replace('.png', '_chessboard_detection.png')
            cv2.imwrite(save_path, result)
            print(f"  检测结果已保存: {save_path}")
        else:
            print(f"  ✗ 未检测到棋盘格")
    except Exception as e:
        print(f"  ✗ 检测失败: {e}")


def main():
    """主函数"""
    photo_dir = r"D:\GitRepos\Aurora Application\Documents\ClientPhoto"
    
    if not os.path.exists(photo_dir):
        print(f"ERROR: 目录不存在: {photo_dir}")
        sys.exit(1)
    
    png_files = sorted([f for f in os.listdir(photo_dir) if f.lower().endswith('.png')])
    
    if not png_files:
        print(f"ERROR: 目录中没有 PNG 文件")
        sys.exit(1)
    
    print(f"找到 {len(png_files)} 个 PNG 文件")
    
    for filename in png_files:
        filepath = os.path.join(photo_dir, filename)
        img, img_8bit, gray_8bit = analyze_image_quality(filepath)
        detect_calibration_board(gray_8bit, filepath)


if __name__ == '__main__':
    main()