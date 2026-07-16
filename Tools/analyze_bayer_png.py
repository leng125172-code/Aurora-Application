#!/usr/bin/env python3
"""
分析相机拍摄的 BAYGB12Packed 格式 PNG 图片
BAYGB12Packed 是 12-bit Bayer 格式，通常存储在 PNG 的自定义 chunk 中
"""

import os
import sys
import struct
import numpy as np
import cv2


def read_png_chunks(filepath):
    """读取 PNG 文件的所有 chunk"""
    chunks = []
    with open(filepath, 'rb') as f:
        # 读取 PNG 签名
        signature = f.read(8)
        if signature != b'\x89PNG\r\n\x1a\n':
            print(f"ERROR: 不是有效的 PNG 文件: {filepath}")
            return []
        
        while True:
            # 读取 chunk 长度 (4 bytes)
            len_data = f.read(4)
            if len(len_data) < 4:
                break
            chunk_len = struct.unpack('>I', len_data)[0]
            
            # 读取 chunk 类型 (4 bytes)
            chunk_type = f.read(4).decode('ascii')
            
            # 读取 chunk 数据
            chunk_data = f.read(chunk_len)
            
            # 读取 CRC (4 bytes)
            crc_data = f.read(4)
            
            chunks.append({
                'type': chunk_type,
                'length': chunk_len,
                'data': chunk_data
            })
            
            if chunk_type == 'IEND':
                break
    
    return chunks


def decode_baygb12packed(raw_data, width, height):
    """
    解码 BAYGB12Packed 格式
    BAYGB12Packed 是 12-bit Bayer 格式，每个像素 12 bits
    排列方式: B, A(Y), Y, G 交替
    """
    # 计算预期数据长度
    expected_len = (width * height * 12) // 8
    if len(raw_data) < expected_len:
        print(f"WARNING: 数据长度不足! 预期={expected_len}, 实际={len(raw_data)}")
        return None
    
    # 12-bit packed: 每 3 字节存储 2 个像素
    # Pixel 0: bits 0-11 of bytes 0-1
    # Pixel 1: bits 4-7 of byte 1 + byte 2
    
    num_pixels = width * height
    raw_array = np.frombuffer(raw_data, dtype=np.uint8)
    
    # 创建 12-bit 数组
    pixels_12bit = np.zeros(num_pixels, dtype=np.uint16)
    
    # 前半部分: 每 3 字节 = 2 个像素
    full_groups = num_pixels // 2
    bytes_used = full_groups * 3
    
    # 处理完整组
    for i in range(full_groups):
        base = i * 3
        # Pixel 2i: byte[base] + high 4 bits of byte[base+1]
        pixels_12bit[2*i] = (raw_array[base] << 4) | ((raw_array[base+1] >> 4) & 0x0F)
        # Pixel 2i+1: low 4 bits of byte[base+1] + byte[base+2]
        pixels_12bit[2*i+1] = ((raw_array[base+1] & 0x0F) << 8) | raw_array[base+2]
    
    # 处理剩余像素（如果像素数是奇数）
    if num_pixels % 2 == 1:
        if bytes_used + 2 <= len(raw_array):
            pixels_12bit[-1] = (raw_array[bytes_used] << 4) | ((raw_array[bytes_used+1] >> 4) & 0x0F)
    
    # 转换为 2D 数组
    bayer_12bit = pixels_12bit.reshape((height, width))
    
    return bayer_12bit


def debayer(bayer_12bit, pattern='BAYGB'):
    """
    Debayer 12-bit Bayer 图像
    pattern: BAYGB 表示 Bayer 排列顺序
    """
    # 转换为 8-bit 用于 OpenCV
    bayer_8bit = (bayer_12bit >> 4).astype(np.uint8)
    
    # 根据 pattern 确定 OpenCV 码
    # BAYGB 排列类似于 BGGR 或其他模式，需要根据实际排列确定
    # 这里假设是 BGGR (最常见的模式)
    rgb = cv2.cvtColor(bayer_8bit, cv2.COLOR_BayerBG2BGR)
    
    return rgb


def analyze_image(filepath):
    """分析单个图像文件"""
    print(f"\n{'='*60}")
    print(f"分析文件: {filepath}")
    print(f"文件大小: {os.path.getsize(filepath):,} 字节")
    print('='*60)
    
    # 读取 PNG chunks
    chunks = read_png_chunks(filepath)
    print(f"\nPNG Chunks ({len(chunks)}):")
    for i, chunk in enumerate(chunks):
        print(f"  [{i}] {chunk['type']}: {chunk['length']:,} 字节")
    
    # 检查是否有自定义 chunk
    custom_chunks = [c for c in chunks if c['type'][0] == 'i']  # 小写字母开头的 chunk 是私有的
    if custom_chunks:
        print(f"\n自定义 Chunks ({len(custom_chunks)}):")
        for chunk in custom_chunks:
            print(f"  {chunk['type']}: {chunk['length']:,} 字节")
            # 打印前 32 字节的十六进制
            hex_data = ' '.join(f'{b:02X}' for b in chunk['data'][:32])
            print(f"    前32字节: {hex_data}")
    
    # 尝试用 OpenCV 读取
    print("\n尝试用 OpenCV 读取...")
    try:
        img = cv2.imread(filepath, cv2.IMREAD_UNCHANGED)
        if img is not None:
            print(f"  OpenCV 读取成功")
            print(f"  图像尺寸: {img.shape}")
            print(f"  数据类型: {img.dtype}")
            print(f"  像素值范围: [{img.min()}, {img.max()}]")
            
            # 计算统计信息
            if len(img.shape) == 2:
                # 灰度图
                mean_val = img.mean()
                std_val = img.std()
                print(f"  亮度均值: {mean_val:.2f}")
                print(f"  亮度标准差: {std_val:.2f}")
            elif len(img.shape) == 3:
                # 彩色图
                for channel, name in enumerate(['B', 'G', 'R']):
                    mean_val = img[:, :, channel].mean()
                    std_val = img[:, :, channel].std()
                    print(f"  {name}通道 - 均值: {mean_val:.2f}, 标准差: {std_val:.2f}")
            
            # 保存预览图
            preview_path = filepath.replace('.png', '_preview.png')
            cv2.imwrite(preview_path, img)
            print(f"  预览图已保存: {preview_path}")
        else:
            print("  OpenCV 读取失败")
    except Exception as e:
        print(f"  OpenCV 读取异常: {e}")
    
    # 尝试解析 BAYGB12Packed 数据
    print("\n尝试解析 BAYGB12Packed 数据...")
    
    # 检查是否有原始数据 chunk
    raw_chunk = None
    for chunk in chunks:
        # 可能的自定义 chunk 名称
        if chunk['type'].upper() in ['RAW', 'BAYR', 'BAYE', 'DATA']:
            raw_chunk = chunk
            break
        # 也可能在 IDAT 中存储原始数据
        if chunk['type'] == 'IDAT':
            # 检查数据是否像压缩的原始数据
            # IDAT 通常是 zlib 压缩的，尝试解压
            try:
                import zlib
                decompressed = zlib.decompress(chunk['data'])
                # 如果解压后数据长度符合 BAYGB12Packed 格式
                print(f"  IDAT 解压后: {len(decompressed):,} 字节")
                # 假设常见分辨率 2448x2048
                for w, h in [(2448, 2048), (1920, 1080), (1280, 720)]:
                    expected_len = (w * h * 12) // 8
                    if abs(len(decompressed) - expected_len) < 100:
                        print(f"  可能分辨率: {w}x{h} (预期数据长度: {expected_len:,})")
                        bayer_data = decode_baygb12packed(decompressed, w, h)
                        if bayer_data is not None:
                            rgb = debayer(bayer_data)
                            save_path = filepath.replace('.png', f'_debayer_{w}x{h}.png')
                            cv2.imwrite(save_path, rgb)
                            print(f"  Debayer 结果已保存: {save_path}")
            except Exception as e:
                print(f"  IDAT zlib 解压失败: {e}")
    
    if raw_chunk:
        print(f"  找到原始数据 chunk: {raw_chunk['type']}")
        # 尝试不同分辨率
        for w, h in [(2448, 2048), (1920, 1080), (1280, 720)]:
            expected_len = (w * h * 12) // 8
            if abs(len(raw_chunk['data']) - expected_len) < 100:
                print(f"  可能分辨率: {w}x{h}")
                bayer_data = decode_baygb12packed(raw_chunk['data'], w, h)
                if bayer_data is not None:
                    rgb = debayer(bayer_data)
                    save_path = filepath.replace('.png', f'_debayer_{w}x{h}.png')
                    cv2.imwrite(save_path, rgb)
                    print(f"  Debayer 结果已保存: {save_path}")


def main():
    """主函数"""
    photo_dir = r"D:\GitRepos\Aurora Application\Documents\ClientPhoto"
    
    if not os.path.exists(photo_dir):
        print(f"ERROR: 目录不存在: {photo_dir}")
        sys.exit(1)
    
    png_files = [f for f in os.listdir(photo_dir) if f.lower().endswith('.png')]
    
    if not png_files:
        print(f"ERROR: 目录中没有 PNG 文件: {photo_dir}")
        sys.exit(1)
    
    print(f"找到 {len(png_files)} 个 PNG 文件")
    
    for filename in sorted(png_files):
        filepath = os.path.join(photo_dir, filename)
        analyze_image(filepath)


if __name__ == '__main__':
    main()