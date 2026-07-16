#!/usr/bin/env python3
"""
分析相机图像直方图，提供滤光片选择建议
"""

import os
import sys
import numpy as np
import cv2
import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt


def plot_histogram(hist, bins, title, filename, color='gray'):
    """绘制直方图"""
    plt.figure(figsize=(12, 4))
    plt.plot(bins[:-1], hist, color=color)
    plt.title(title)
    plt.xlabel('像素值 (0-4095)')
    plt.ylabel('像素数量')
    plt.grid(True, alpha=0.3)
    plt.savefig(filename, dpi=150, bbox_inches='tight')
    plt.close()
    print(f"  直方图已保存: {filename}")


def analyze_histogram(img, filepath):
    """分析直方图"""
    print(f"\n=== 直方图分析 ===")
    
    # 分离通道
    if len(img.shape) == 3:
        b, g, r = cv2.split(img)
    else:
        gray = img
        b = g = r = img
    
    # 计算各通道直方图
    channels = {
        'B': b,
        'G': g,
        'R': r
    }
    
    histograms = {}
    for name, channel in channels.items():
        hist, bins = np.histogram(channel, bins=256, range=(0, 4096))
        histograms[name] = {'hist': hist, 'bins': bins}
        
        # 绘制直方图
        filename = filepath.replace('.png', f'_hist_{name}.png')
        plot_histogram(hist, bins, f'{name}通道直方图', filename, 
                       color='blue' if name == 'B' else 'green' if name == 'G' else 'red')
    
    # 联合直方图（三通道对比）
    plt.figure(figsize=(12, 4))
    for name, data in histograms.items():
        color = 'blue' if name == 'B' else 'green' if name == 'G' else 'red'
        plt.plot(data['bins'][:-1], data['hist'], color=color, label=f'{name}通道', alpha=0.7)
    plt.title('三通道直方图对比')
    plt.xlabel('像素值 (0-4095)')
    plt.ylabel('像素数量')
    plt.legend()
    plt.grid(True, alpha=0.3)
    filename = filepath.replace('.png', '_hist_rgb.png')
    plt.savefig(filename, dpi=150, bbox_inches='tight')
    plt.close()
    print(f"  RGB联合直方图已保存: {filename}")
    
    return histograms


def analyze_channel_balance(img):
    """分析通道平衡"""
    print(f"\n=== 通道平衡分析 ===")
    
    if len(img.shape) == 3:
        b, g, r = cv2.split(img)
    else:
        return None
    
    # 计算各通道统计
    stats = {}
    for name, channel in [('B', b), ('G', g), ('R', r)]:
        stats[name] = {
            'mean': channel.mean(),
            'std': channel.std(),
            'min': channel.min(),
            'max': channel.max(),
            'range': channel.max() - channel.min()
        }
    
    # 归一化到0-1
    max_mean = max(stats['B']['mean'], stats['G']['mean'], stats['R']['mean'])
    balance_ratio = {
        'B': stats['B']['mean'] / max_mean,
        'G': stats['G']['mean'] / max_mean,
        'R': stats['R']['mean'] / max_mean
    }
    
    print(f"通道均值: B={stats['B']['mean']:.1f}, G={stats['G']['mean']:.1f}, R={stats['R']['mean']:.1f}")
    print(f"通道占比: B={balance_ratio['B']:.2f}, G={balance_ratio['G']:.2f}, R={balance_ratio['R']:.2f}")
    print(f"通道动态范围: B={stats['B']['range']:.1f}, G={stats['G']['range']:.1f}, R={stats['R']['range']:.1f}")
    
    # 判断是否存在色偏
    threshold = 0.8
    if balance_ratio['B'] < threshold or balance_ratio['G'] < threshold or balance_ratio['R'] < threshold:
        weak_channel = min(balance_ratio, key=balance_ratio.get)
        print(f"\n⚠️ 注意: {weak_channel}通道相对较弱，可能存在色偏")
    
    return stats, balance_ratio


def suggest_filters(stats, balance_ratio):
    """根据直方图提供滤光片选择建议"""
    print(f"\n=== 滤光片选择建议 ===")
    
    # 分析各通道状态
    b_ratio = balance_ratio['B']
    g_ratio = balance_ratio['G']
    r_ratio = balance_ratio['R']
    
    suggestions = []
    
    # 1. 判断是否需要中性密度滤光片 (ND)
    max_value = max(stats['B']['max'], stats['G']['max'], stats['R']['max'])
    saturation_percent = max_value / 4095 * 100
    if saturation_percent > 95:
        suggestions.append(f"中性密度滤光片 (ND): 图像接近饱和({saturation_percent:.1f}%)，建议使用 ND2-ND8 降低进光量")
    elif saturation_percent < 50:
        suggestions.append(f"增透膜 (AR): 图像动态范围未充分利用({saturation_percent:.1f}%)，建议使用增透膜提高透光率")
    
    # 2. 判断是否需要色温校正滤光片
    # R > G > B: 偏暖（红过多）
    # B > G > R: 偏冷（蓝过多）
    if r_ratio > g_ratio * 1.2 and r_ratio > b_ratio * 1.2:
        suggestions.append(f"色温校正滤光片: R通道过强(r={r_ratio:.2f}, g={g_ratio:.2f}, b={b_ratio:.2f})，建议使用蓝色色温片(80A/82)")
    elif b_ratio > g_ratio * 1.2 and b_ratio > r_ratio * 1.2:
        suggestions.append(f"色温校正滤光片: B通道过强(r={r_ratio:.2f}, g={g_ratio:.2f}, b={b_ratio:.2f})，建议使用橙色色温片(81A/85)")
    
    # 3. 判断是否需要带通滤光片
    # 如果某通道占比过低，可能需要增强该波段
    min_ratio = min(b_ratio, g_ratio, r_ratio)
    if min_ratio < 0.7:
        weak_channel = min(balance_ratio, key=balance_ratio.get)
        wavelength = {'B': '450-500nm', 'G': '500-570nm', 'R': '620-700nm'}[weak_channel]
        suggestions.append(f"带通滤光片: {weak_channel}通道较弱({min_ratio:.2f})，建议使用 {wavelength} 带通滤光片增强该波段")
    
    # 4. 判断是否需要偏振片
    # 分析高光区域
    high_light_ratio = (stats['G']['max'] - stats['G']['mean']) / stats['G']['max']
    if high_light_ratio > 0.6:
        suggestions.append(f"偏振片: 高光区域占比较大({high_light_ratio:.2f})，建议使用线性偏振片减少反光")
    
    # 5. 判断是否需要UV滤光片
    # 通常UV滤光片用于保护镜头，但如果B通道噪声过大可能需要
    b_noise_ratio = stats['B']['std'] / stats['B']['mean']
    if b_noise_ratio > 0.5:
        suggestions.append(f"UV滤光片: B通道噪声相对较大({b_noise_ratio:.2f})，建议使用UV截止滤光片减少紫外噪声")
    
    # 输出建议
    if suggestions:
        for i, suggestion in enumerate(suggestions, 1):
            print(f"  {i}. {suggestion}")
    else:
        print("  当前图像质量良好，暂不需要额外滤光片")
    
    return suggestions


def analyze_noise_profiles(img):
    """分析噪声分布"""
    print(f"\n=== 噪声分布分析 ===")
    
    if len(img.shape) == 3:
        b, g, r = cv2.split(img)
    else:
        gray = img
        b = g = r = img
    
    for name, channel in [('B', b), ('G', g), ('R', r)]:
        # 计算暗区和亮区的噪声
        dark_region = channel[channel < channel.mean() * 0.3]
        bright_region = channel[channel > channel.mean() * 0.7]
        
        if len(dark_region) > 100:
            dark_noise = dark_region.std()
            dark_mean = dark_region.mean()
        else:
            dark_noise = 0
            dark_mean = 0
        
        if len(bright_region) > 100:
            bright_noise = bright_region.std()
            bright_mean = bright_region.mean()
        else:
            bright_noise = 0
            bright_mean = 0
        
        print(f"{name}通道:")
        print(f"  暗区(均值<30%): 均值={dark_mean:.1f}, 噪声={dark_noise:.1f}")
        print(f"  亮区(均值>70%): 均值={bright_mean:.1f}, 噪声={bright_noise:.1f}")


def main():
    """主函数"""
    photo_dir = r"D:\GitRepos\Aurora Application\Documents\ClientPhoto"
    
    if not os.path.exists(photo_dir):
        print(f"ERROR: 目录不存在: {photo_dir}")
        sys.exit(1)
    
    png_files = sorted([f for f in os.listdir(photo_dir) if f.lower().endswith('.png') and '_preview' not in f])
    
    if not png_files:
        print(f"ERROR: 目录中没有 PNG 文件")
        sys.exit(1)
    
    print(f"找到 {len(png_files)} 个 PNG 文件")
    
    for filename in png_files:
        filepath = os.path.join(photo_dir, filename)
        print(f"\n{'='*60}")
        print(f"分析文件: {filename}")
        print('='*60)
        
        # 读取图像
        img = cv2.imread(filepath, cv2.IMREAD_UNCHANGED)
        if img is None:
            print("ERROR: 无法读取图像")
            continue
        
        # 分析直方图
        analyze_histogram(img, filepath)
        
        # 分析通道平衡
        stats, balance_ratio = analyze_channel_balance(img)
        
        # 噪声分析
        analyze_noise_profiles(img)
        
        # 滤光片建议
        suggest_filters(stats, balance_ratio)


if __name__ == '__main__':
    main()