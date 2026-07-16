#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
外参标定图片质量检测工具

功能：
1. 图像差分分析 - 验证白屏帧与棋盘格帧的差异性
2. 圆点标定板检测 - 检测物理标定板角点
3. 棋盘格角点检测 - 检测投影棋盘格角点
4. 图像质量评估 - 曝光、对比度、清晰度
5. 可视化展示检测结果

使用方法：
    python calib_photo_analyzer.py --input_dir <图片目录>
    python calib_photo_analyzer.py --white <白屏图片> --pattern <棋盘格图片>
    
图片命名约定（自动分组）：
    *_white.jpg / *_white.png     - 白屏帧
    *_pattern.jpg / *_pattern.png - 棋盘格帧
    *_0.jpg / *_0.png             - 白屏帧（备用）
    *_1.jpg / *_1.png             - 棋盘格帧（备用）
"""

import argparse
import os
import sys
import json
from pathlib import Path
from typing import List, Tuple, Dict, Optional

import cv2
import numpy as np
from matplotlib import pyplot as plt


class CalibPhotoAnalyzer:
    """外参标定图片分析器"""

    def __init__(self, circle_pattern_size: Tuple[int, int] = (11, 8),
                 chessboard_pattern_size: Tuple[int, int] = (11, 8),
                 circle_spacing_mm: float = 40.0,
                 chessboard_pixel_size: int = 100):
        """
        初始化分析器
        
        Args:
            circle_pattern_size: 圆点标定板模式尺寸 (cols, rows)
            chessboard_pattern_size: 棋盘格模式尺寸 (cols, rows)
            circle_spacing_mm: 圆点间距 (mm)
            chessboard_pixel_size: 棋盘格像素大小
        """
        self.circle_pattern_size = circle_pattern_size
        self.chessboard_pattern_size = chessboard_pattern_size
        self.circle_spacing_mm = circle_spacing_mm
        self.chessboard_pixel_size = chessboard_pixel_size

    def load_image(self, path: str) -> Optional[np.ndarray]:
        """加载图片，返回 RGB 格式"""
        img = cv2.imread(path)
        if img is None:
            return None
        return cv2.cvtColor(img, cv2.COLOR_BGR2RGB)

    def compute_image_diff(self, img1: np.ndarray, img2: np.ndarray) -> Dict:
        """
        计算两幅图像的差异
        
        Returns:
            包含差分分数、显著像素比例等信息的字典
        """
        # 转换为灰度图
        gray1 = cv2.cvtColor(img1, cv2.COLOR_RGB2GRAY)
        gray2 = cv2.cvtColor(img2, cv2.COLOR_RGB2GRAY)

        # 确保尺寸一致
        if gray1.shape != gray2.shape:
            gray2 = cv2.resize(gray2, (gray1.shape[1], gray1.shape[0]))

        # 计算绝对差异
        diff = cv2.absdiff(gray1, gray2)
        
        # 计算差分统计
        mean_diff = float(np.mean(diff))
        std_diff = float(np.std(diff))
        max_diff = int(np.max(diff))
        
        # 计算显著差异像素比例（差异 > 阈值的像素占比）
        threshold = 30
        significant_mask = diff > threshold
        significant_ratio = float(np.sum(significant_mask) / diff.size)
        
        # 计算结构化差异（使用 Sobel 边缘检测）
        sobel1 = cv2.Sobel(gray1, cv2.CV_64F, 1, 1, ksize=3)
        sobel2 = cv2.Sobel(gray2, cv2.CV_64F, 1, 1, ksize=3)
        edge_diff = cv2.absdiff(np.abs(sobel1), np.abs(sobel2))
        edge_diff_ratio = float(np.sum(edge_diff > 20) / edge_diff.size)

        return {
            'mean_diff': round(mean_diff, 2),
            'std_diff': round(std_diff, 2),
            'max_diff': max_diff,
            'significant_ratio': round(significant_ratio * 100, 2),
            'edge_diff_ratio': round(edge_diff_ratio * 100, 2),
            'is_significant': significant_ratio > 0.05,
            'diff_image': diff,
        }

    def detect_circle_board(self, img: np.ndarray) -> Dict:
        """
        检测圆点标定板
        
        Returns:
            检测结果字典
        """
        gray = cv2.cvtColor(img, cv2.COLOR_RGB2GRAY)
        
        # 尝试多种检测模式
        flags = cv2.CALIB_CB_ASYMMETRIC_GRID
        corners = None
        detected = False
        
        try:
            detected, corners = cv2.findCirclesGrid(
                gray,
                self.circle_pattern_size,
                flags=flags
            )
        except Exception:
            pass
        
        # 如果不对称网格失败，尝试对称网格
        if not detected:
            try:
                flags = cv2.CALIB_CB_SYMMETRIC_GRID
                detected, corners = cv2.findCirclesGrid(
                    gray,
                    self.circle_pattern_size,
                    flags=flags
                )
            except Exception:
                pass
        
        # 如果仍失败，尝试带自适应阈值的检测
        if not detected:
            try:
                flags = cv2.CALIB_CB_SYMMETRIC_GRID | cv2.CALIB_CB_CLUSTERING
                detected, corners = cv2.findCirclesGrid(
                    gray,
                    self.circle_pattern_size,
                    flags=flags
                )
            except Exception:
                pass

        corner_count = 0
        if detected and corners is not None:
            corner_count = len(corners)
            # 绘制检测结果
            result_img = img.copy()
            cv2.drawChessboardCorners(result_img, self.circle_pattern_size, corners, detected)
        else:
            result_img = img.copy()

        expected_count = self.circle_pattern_size[0] * self.circle_pattern_size[1]
        coverage_ratio = round(corner_count / expected_count * 100, 2) if expected_count > 0 else 0

        return {
            'detected': detected,
            'corner_count': corner_count,
            'expected_count': expected_count,
            'coverage_ratio': coverage_ratio,
            'is_valid': detected and corner_count == expected_count,
            'result_image': result_img,
        }

    def detect_chessboard(self, img: np.ndarray) -> Dict:
        """
        检测棋盘格角点
        
        Returns:
            检测结果字典
        """
        gray = cv2.cvtColor(img, cv2.COLOR_RGB2GRAY)
        
        # 尝试检测
        detected, corners = cv2.findChessboardCorners(
            gray,
            self.chessboard_pattern_size,
            flags=cv2.CALIB_CB_ADAPTIVE_THRESH | cv2.CALIB_CB_NORMALIZE_IMAGE
        )
        
        # 亚像素细化
        if detected:
            criteria = (cv2.TERM_CRITERIA_EPS + cv2.TERM_CRITERIA_MAX_ITER, 30, 0.001)
            corners = cv2.cornerSubPix(gray, corners, (11, 11), (-1, -1), criteria)

        corner_count = 0
        if detected and corners is not None:
            corner_count = len(corners)
            # 绘制检测结果
            result_img = img.copy()
            cv2.drawChessboardCorners(result_img, self.chessboard_pattern_size, corners, detected)
        else:
            result_img = img.copy()

        expected_count = (self.chessboard_pattern_size[0] - 1) * (self.chessboard_pattern_size[1] - 1)
        coverage_ratio = round(corner_count / expected_count * 100, 2) if expected_count > 0 else 0

        return {
            'detected': detected,
            'corner_count': corner_count,
            'expected_count': expected_count,
            'coverage_ratio': coverage_ratio,
            'is_valid': detected and corner_count == expected_count,
            'result_image': result_img,
        }

    def analyze_image_quality(self, img: np.ndarray) -> Dict:
        """
        分析图像质量（曝光、对比度、清晰度）
        
        Returns:
            质量分析结果字典
        """
        gray = cv2.cvtColor(img, cv2.COLOR_RGB2GRAY)
        
        # 曝光评估
        histogram = cv2.calcHist([gray], [0], None, [256], [0, 256])
        total_pixels = gray.size
        
        # 暗像素比例（< 20）
        dark_ratio = float(np.sum(histogram[:20]) / total_pixels * 100)
        # 亮像素比例（> 235）
        bright_ratio = float(np.sum(histogram[235:]) / total_pixels * 100)
        # 平均亮度
        mean_brightness = float(np.mean(gray))
        
        # 对比度评估
        contrast = float(np.std(gray))
        
        # 清晰度评估（使用 Laplacian 方差）
        laplacian = cv2.Laplacian(gray, cv2.CV_64F)
        sharpness = float(np.var(laplacian))
        
        # 质量评分
        exposure_score = self._score_exposure(mean_brightness, dark_ratio, bright_ratio)
        contrast_score = self._score_contrast(contrast)
        sharpness_score = self._score_sharpness(sharpness)
        overall_score = round((exposure_score + contrast_score + sharpness_score) / 3, 2)

        return {
            'mean_brightness': round(mean_brightness, 2),
            'dark_ratio': round(dark_ratio, 2),
            'bright_ratio': round(bright_ratio, 2),
            'contrast': round(contrast, 2),
            'sharpness': round(sharpness, 2),
            'exposure_score': exposure_score,
            'contrast_score': contrast_score,
            'sharpness_score': sharpness_score,
            'overall_score': overall_score,
            'is_good_quality': overall_score >= 60,
        }

    def _score_exposure(self, mean_brightness: float, dark_ratio: float, bright_ratio: float) -> int:
        """评估曝光分数（0-100）"""
        if mean_brightness < 50 or mean_brightness > 200:
            return 30
        if dark_ratio > 20 or bright_ratio > 20:
            return 50
        if mean_brightness < 80 or mean_brightness > 170:
            return 70
        return 90

    def _score_contrast(self, contrast: float) -> int:
        """评估对比度分数（0-100）"""
        if contrast < 10:
            return 20
        if contrast < 20:
            return 40
        if contrast < 35:
            return 70
        if contrast > 80:
            return 80
        return 95

    def _score_sharpness(self, sharpness: float) -> int:
        """评估清晰度分数（0-100）"""
        if sharpness < 10:
            return 20
        if sharpness < 30:
            return 40
        if sharpness < 100:
            return 60
        if sharpness < 500:
            return 80
        return 95

    def analyze_pair(self, white_path: str, pattern_path: str) -> Dict:
        """
        分析一组外参标定图片（白屏帧 + 棋盘格帧）
        
        Returns:
            完整分析结果
        """
        print(f"\n{'='*60}")
        print(f"分析图片组:")
        print(f"  白屏帧: {os.path.basename(white_path)}")
        print(f"  棋盘格帧: {os.path.basename(pattern_path)}")
        print(f"{'='*60}")

        # 加载图片
        white_img = self.load_image(white_path)
        pattern_img = self.load_image(pattern_path)
        
        if white_img is None:
            print(f"  ❌ 无法加载白屏帧: {white_path}")
            return {'error': f'无法加载白屏帧: {white_path}'}
        if pattern_img is None:
            print(f"  ❌ 无法加载棋盘格帧: {pattern_path}")
            return {'error': f'无法加载棋盘格帧: {pattern_path}'}

        print(f"  ✅ 图片加载成功")
        print(f"  图像尺寸: {white_img.shape[1]} x {white_img.shape[0]}")

        results = {
            'white_image': os.path.basename(white_path),
            'pattern_image': os.path.basename(pattern_path),
            'image_size': f"{white_img.shape[1]}x{white_img.shape[0]}",
        }

        # 1. 图像差分分析
        print(f"\n  [1/4] 图像差分分析...")
        diff_result = self.compute_image_diff(white_img, pattern_img)
        results['diff_analysis'] = {k: v for k, v in diff_result.items() if k != 'diff_image'}
        print(f"    均值差异: {diff_result['mean_diff']:.2f}")
        print(f"    显著像素比例: {diff_result['significant_ratio']:.2f}%")
        print(f"    边缘差异比例: {diff_result['edge_diff_ratio']:.2f}%")
        print(f"    {'✅' if diff_result['is_significant'] else '❌'} 差分显著: {diff_result['is_significant']}")

        # 2. 白屏帧圆点标定板检测
        print(f"\n  [2/4] 白屏帧圆点标定板检测...")
        white_circle_result = self.detect_circle_board(white_img)
        results['white_circle_detection'] = {k: v for k, v in white_circle_result.items() if k != 'result_image'}
        print(f"    检测状态: {'✅' if white_circle_result['detected'] else '❌'}")
        print(f"    检测角点数: {white_circle_result['corner_count']}/{white_circle_result['expected_count']}")
        print(f"    覆盖率: {white_circle_result['coverage_ratio']:.2f}%")
        print(f"    {'✅' if white_circle_result['is_valid'] else '❌'} 有效: {white_circle_result['is_valid']}")

        # 3. 棋盘格帧检测（圆点标定板 + 投影棋盘格）
        print(f"\n  [3/4] 棋盘格帧检测...")
        
        # 检测圆点标定板
        pattern_circle_result = self.detect_circle_board(pattern_img)
        results['pattern_circle_detection'] = {k: v for k, v in pattern_circle_result.items() if k != 'result_image'}
        print(f"    圆点标定板:")
        print(f"      检测状态: {'✅' if pattern_circle_result['detected'] else '❌'}")
        print(f"      检测角点数: {pattern_circle_result['corner_count']}/{pattern_circle_result['expected_count']}")
        print(f"      覆盖率: {pattern_circle_result['coverage_ratio']:.2f}%")
        
        # 检测投影棋盘格
        pattern_chess_result = self.detect_chessboard(pattern_img)
        results['pattern_chessboard_detection'] = {k: v for k, v in pattern_chess_result.items() if k != 'result_image'}
        print(f"    投影棋盘格:")
        print(f"      检测状态: {'✅' if pattern_chess_result['detected'] else '❌'}")
        print(f"      检测角点数: {pattern_chess_result['corner_count']}/{pattern_chess_result['expected_count']}")
        print(f"      覆盖率: {pattern_chess_result['coverage_ratio']:.2f}%")

        # 4. 图像质量分析
        print(f"\n  [4/4] 图像质量分析...")
        white_quality = self.analyze_image_quality(white_img)
        pattern_quality = self.analyze_image_quality(pattern_img)
        results['white_quality'] = white_quality
        results['pattern_quality'] = pattern_quality
        print(f"    白屏帧质量分数: {white_quality['overall_score']}/100")
        print(f"      曝光: {white_quality['exposure_score']} 对比度: {white_quality['contrast_score']} 清晰度: {white_quality['sharpness_score']}")
        print(f"    棋盘格帧质量分数: {pattern_quality['overall_score']}/100")
        print(f"      曝光: {pattern_quality['exposure_score']} 对比度: {pattern_quality['contrast_score']} 清晰度: {pattern_quality['sharpness_score']}")

        # 综合评估
        print(f"\n  {'='*60}")
        is_valid = all([
            diff_result['is_significant'],
            white_circle_result['is_valid'],
            pattern_circle_result['is_valid'],
            pattern_chess_result['is_valid'],
            white_quality['is_good_quality'],
            pattern_quality['is_good_quality'],
        ])
        results['is_valid'] = is_valid
        
        if is_valid:
            print(f"  ✅ 图片组验证通过！")
        else:
            print(f"  ❌ 图片组验证失败！")
            print(f"    失败原因:")
            if not diff_result['is_significant']:
                print(f"      - 图像差分不显著（投影仪模式切换可能失败）")
            if not white_circle_result['is_valid']:
                print(f"      - 白屏帧未检测到完整圆点标定板")
            if not pattern_circle_result['is_valid']:
                print(f"      - 棋盘格帧未检测到完整圆点标定板")
            if not pattern_chess_result['is_valid']:
                print(f"      - 棋盘格帧未检测到完整投影棋盘格")
            if not white_quality['is_good_quality']:
                print(f"      - 白屏帧图像质量不佳")
            if not pattern_quality['is_good_quality']:
                print(f"      - 棋盘格帧图像质量不佳")

        # 生成可视化结果
        results['visualization'] = self._generate_visualization(
            white_img, pattern_img, diff_result['diff_image'],
            white_circle_result['result_image'],
            pattern_circle_result['result_image'],
            pattern_chess_result['result_image'],
            white_quality, pattern_quality
        )

        return results

    def _generate_visualization(self, white_img, pattern_img, diff_img,
                               white_circle_result, pattern_circle_result,
                               pattern_chess_result, white_quality, pattern_quality):
        """生成可视化展示"""
        fig, axes = plt.subplots(2, 3, figsize=(18, 10))
        
        # 原图对比
        axes[0, 0].imshow(white_img)
        axes[0, 0].set_title(f'白屏帧\n质量分数: {white_quality["overall_score"]}/100')
        axes[0, 0].axis('off')
        
        axes[0, 1].imshow(pattern_img)
        axes[0, 1].set_title(f'棋盘格帧\n质量分数: {pattern_quality["overall_score"]}/100')
        axes[0, 1].axis('off')
        
        # 差分图
        axes[0, 2].imshow(diff_img, cmap='gray')
        axes[0, 2].set_title(f'图像差分\n显著像素: {white_quality["sharpness"]:.2f}')
        axes[0, 2].axis('off')
        
        # 检测结果
        axes[1, 0].imshow(white_circle_result)
        axes[1, 0].set_title(f'白屏帧圆点检测\n{white_circle_result.shape[1]}x{white_circle_result.shape[0]}')
        axes[1, 0].axis('off')
        
        axes[1, 1].imshow(pattern_circle_result)
        axes[1, 1].set_title(f'棋盘格帧圆点检测')
        axes[1, 1].axis('off')
        
        axes[1, 2].imshow(pattern_chess_result)
        axes[1, 2].set_title(f'棋盘格帧棋盘格检测')
        axes[1, 2].axis('off')
        
        plt.tight_layout()
        return fig

    def analyze_directory(self, input_dir: str, output_dir: Optional[str] = None) -> List[Dict]:
        """
        分析目录中的所有图片组
        
        Args:
            input_dir: 包含图片的目录
            output_dir: 输出结果目录
        
        Returns:
            所有图片组的分析结果列表
        """
        if output_dir:
            os.makedirs(output_dir, exist_ok=True)

        # 自动分组图片
        pairs = self._find_pairs(input_dir)
        
        if not pairs:
            print(f"未找到任何图片组，请检查目录: {input_dir}")
            print("图片命名约定:")
            print("  *_white.jpg / *_white.png     - 白屏帧")
            print("  *_pattern.jpg / *_pattern.png - 棋盘格帧")
            print("  *_0.jpg / *_0.png             - 白屏帧（备用）")
            print("  *_1.jpg / *_1.png             - 棋盘格帧（备用）")
            return []

        print(f"找到 {len(pairs)} 组图片")
        all_results = []
        
        for i, (white_path, pattern_path) in enumerate(pairs, 1):
            print(f"\n{'#'*70}")
            print(f"  第 {i}/{len(pairs)} 组")
            print(f"{'#'*70}")
            
            result = self.analyze_pair(white_path, pattern_path)
            all_results.append(result)
            
            # 保存可视化结果
            if output_dir and 'visualization' in result:
                fig = result['visualization']
                base_name = os.path.splitext(os.path.basename(white_path))[0].replace('_white', '')
                output_path = os.path.join(output_dir, f'analysis_{base_name}.png')
                fig.savefig(output_path, dpi=150, bbox_inches='tight')
                plt.close(fig)
                print(f"  可视化结果已保存: {output_path}")

        # 保存汇总报告
        summary = self._generate_summary(all_results)
        if output_dir:
            summary_path = os.path.join(output_dir, 'analysis_summary.json')
            with open(summary_path, 'w', encoding='utf-8') as f:
                json.dump(summary, f, indent=2, ensure_ascii=False)
            print(f"\n汇总报告已保存: {summary_path}")

        # 打印汇总
        self._print_summary(summary)

        return all_results

    def _find_pairs(self, input_dir: str) -> List[Tuple[str, str]]:
        """在目录中查找图片对"""
        files = os.listdir(input_dir)
        white_files = []
        pattern_files = []

        for f in files:
            lower = f.lower()
            if lower.endswith(('.jpg', '.jpeg', '.png')):
                if '_white' in lower or lower.endswith('_0.jpg') or lower.endswith('_0.png'):
                    white_files.append(f)
                elif '_pattern' in lower or lower.endswith('_1.jpg') or lower.endswith('_1.png'):
                    pattern_files.append(f)

        # 按前缀匹配
        pairs = []
        for white in white_files:
            # 提取前缀（去掉 _white, _0, 扩展名）
            base = white.lower()
            for suffix in ['_white.jpg', '_white.jpeg', '_white.png', '_0.jpg', '_0.png']:
                if base.endswith(suffix):
                    base = base[:-len(suffix)]
                    break
            
            # 查找对应的棋盘格文件
            for pattern in pattern_files:
                p_base = pattern.lower()
                if p_base.startswith(base) and (
                    '_pattern' in p_base or p_base.endswith('_1.jpg') or p_base.endswith('_1.png')
                ):
                    pairs.append((
                        os.path.join(input_dir, white),
                        os.path.join(input_dir, pattern)
                    ))
                    break

        return pairs

    def _generate_summary(self, results: List[Dict]) -> Dict:
        """生成汇总报告"""
        valid_count = sum(1 for r in results if r.get('is_valid', False))
        total_count = len(results)
        
        issues = []
        for i, r in enumerate(results, 1):
            if not r.get('is_valid', True):
                issue = {
                    'group': i,
                    'white_image': r.get('white_image'),
                    'pattern_image': r.get('pattern_image'),
                    'problems': []
                }
                if not r.get('diff_analysis', {}).get('is_significant'):
                    issue['problems'].append('图像差分不显著')
                if not r.get('white_circle_detection', {}).get('is_valid'):
                    issue['problems'].append('白屏帧圆点标定板检测失败')
                if not r.get('pattern_circle_detection', {}).get('is_valid'):
                    issue['problems'].append('棋盘格帧圆点标定板检测失败')
                if not r.get('pattern_chessboard_detection', {}).get('is_valid'):
                    issue['problems'].append('投影棋盘格检测失败')
                if not r.get('white_quality', {}).get('is_good_quality'):
                    issue['problems'].append('白屏帧图像质量不佳')
                if not r.get('pattern_quality', {}).get('is_good_quality'):
                    issue['problems'].append('棋盘格帧图像质量不佳')
                issues.append(issue)

        return {
            'total_groups': total_count,
            'valid_groups': valid_count,
            'invalid_groups': total_count - valid_count,
            'valid_rate': round(valid_count / total_count * 100, 2) if total_count > 0 else 0,
            'issues': issues,
            'groups': results,
        }

    def _print_summary(self, summary: Dict):
        """打印汇总报告"""
        print(f"\n{'='*70}")
        print(f"                      外参标定图片分析汇总")
        print(f"{'='*70}")
        print(f"  总图片组: {summary['total_groups']}")
        print(f"  有效组:   {summary['valid_groups']}")
        print(f"  无效组:   {summary['invalid_groups']}")
        print(f"  有效率:   {summary['valid_rate']:.2f}%")
        
        if summary['issues']:
            print(f"\n  问题详情:")
            for issue in summary['issues']:
                print(f"    组 {issue['group']}: {issue['white_image']} + {issue['pattern_image']}")
                for problem in issue['problems']:
                    print(f"      - {problem}")


def main():
    parser = argparse.ArgumentParser(description='外参标定图片质量检测工具')
    parser.add_argument('--input_dir', type=str, help='包含图片的目录')
    parser.add_argument('--white', type=str, help='白屏帧图片路径')
    parser.add_argument('--pattern', type=str, help='棋盘格帧图片路径')
    parser.add_argument('--output_dir', type=str, default='calib_analysis_output',
                        help='输出结果目录（默认: calib_analysis_output）')
    parser.add_argument('--circle_cols', type=int, default=11, help='圆点标定板列数')
    parser.add_argument('--circle_rows', type=int, default=8, help='圆点标定板行数')
    parser.add_argument('--chess_cols', type=int, default=11, help='棋盘格列数')
    parser.add_argument('--chess_rows', type=int, default=8, help='棋盘格行数')
    
    args = parser.parse_args()

    # 验证参数
    if not args.input_dir and not (args.white and args.pattern):
        parser.error('必须指定 --input_dir 或同时指定 --white 和 --pattern')

    # 创建分析器
    analyzer = CalibPhotoAnalyzer(
        circle_pattern_size=(args.circle_cols, args.circle_rows),
        chessboard_pattern_size=(args.chess_cols, args.chess_rows)
    )

    # 执行分析
    if args.input_dir:
        analyzer.analyze_directory(args.input_dir, args.output_dir)
    else:
        result = analyzer.analyze_pair(args.white, args.pattern)
        # 显示可视化结果
        if 'visualization' in result:
            plt.show()


if __name__ == '__main__':
    main()
