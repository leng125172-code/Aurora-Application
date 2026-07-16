import cv2
import numpy as np

image_path = r"D:\GitRepos\Aurora Application\Tools\chessboard_test.jpg"

print(f"加载图像: {image_path}")
img = cv2.imread(image_path)
if img is None:
    print("ERROR: 图像加载失败!")
    exit(1)

gray = cv2.cvtColor(img, cv2.COLOR_BGR2GRAY)
height, width = gray.shape

print(f"\n=== 提取ROI ===")
_, proj_mask = cv2.threshold(gray, 80, 255, cv2.THRESH_BINARY)
kernel = cv2.getStructuringElement(cv2.MORPH_RECT, (31, 31))
proj_mask_closed = cv2.morphologyEx(proj_mask, cv2.MORPH_CLOSE, kernel)
proj_mask_open = cv2.morphologyEx(proj_mask_closed, cv2.MORPH_OPEN, kernel)

contours, _ = cv2.findContours(proj_mask_open, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
if contours:
    largest_contour = max(contours, key=cv2.contourArea)
    x, y, w, h = cv2.boundingRect(largest_contour)
    margin = 30
    x = max(0, x - margin)
    y = max(0, y - margin)
    w = min(width - x, w + 2 * margin)
    h = min(height - y, h + 2 * margin)
    roi_gray = gray[y:y+h, x:x+w]
    print(f"  ROI尺寸: {w}x{h}")
else:
    roi_gray = gray

roi_w, roi_h = roi_gray.shape

print(f"\n=== 测试15x8规格 ===")
target_pattern = (15, 8)

variants = []
variants.append(("原图", roi_gray.copy()))

clahe = cv2.createCLAHE(clipLimit=2.0, tileGridSize=(8, 8))
variants.append(("CLAHE", clahe.apply(roi_gray)))

variants.append(("均衡化", cv2.equalizeHist(roi_gray)))

variants.append(("高斯模糊", cv2.GaussianBlur(roi_gray, (3, 3), 0)))

adaptive = cv2.adaptiveThreshold(roi_gray, 255, cv2.ADAPTIVE_THRESH_GAUSSIAN_C, cv2.THRESH_BINARY, 15, 2)
variants.append(("自适应阈值", adaptive))

_, otsu = cv2.threshold(roi_gray, 0, 255, cv2.THRESH_BINARY + cv2.THRESH_OTSU)
variants.append(("Otsu二值化", otsu))

clahe_img = variants[1][1]
variants.append(("CLAHE+自适应", cv2.adaptiveThreshold(clahe_img, 255, cv2.ADAPTIVE_THRESH_GAUSSIAN_C, cv2.THRESH_BINARY, 15, 2)))

_, clahe_otsu = cv2.threshold(clahe_img, 0, 255, cv2.THRESH_BINARY + cv2.THRESH_OTSU)
variants.append(("CLAHE+Otsu", clahe_otsu))

sharpened = cv2.filter2D(roi_gray, -1, np.array([[-1,-1,-1], [-1,9,-1], [-1,-1,-1]]))
variants.append(("锐化", sharpened))

flags_list = [
    cv2.CALIB_CB_ADAPTIVE_THRESH + cv2.CALIB_CB_NORMALIZE_IMAGE,
    cv2.CALIB_CB_ADAPTIVE_THRESH + cv2.CALIB_CB_NORMALIZE_IMAGE + cv2.CALIB_CB_FILTER_QUADS,
    cv2.CALIB_CB_ADAPTIVE_THRESH + cv2.CALIB_CB_NORMALIZE_IMAGE + cv2.CALIB_CB_FAST_CHECK,
]

print(f"\n--- 全分辨率检测 {target_pattern} ---")
for flag in flags_list:
    for name, variant in variants:
        print(f"  Testing {name} with flag={flag}...", end=" ")
        ret, corners = cv2.findChessboardCorners(variant, target_pattern, flag)
        if ret:
            print(f"SUCCESS! Corners={len(corners)}")
        else:
            print("FAILED")

print(f"\n--- 0.75分辨率检测 {target_pattern} ---")
w75, h75 = int(roi_w * 0.75), int(roi_h * 0.75)
for flag in flags_list:
    for name, variant in variants:
        scaled = cv2.resize(variant, (w75, h75), interpolation=cv2.INTER_AREA)
        print(f"  Testing {name}...", end=" ")
        ret, corners = cv2.findChessboardCorners(scaled, target_pattern, flag)
        if ret:
            print(f"SUCCESS! Corners={len(corners)}")
        else:
            print("FAILED")

print(f"\n=== 测试接近规格 ===")
near_patterns = [(15, 8), (8, 15), (14, 8), (8, 14), (15, 9), (9, 15), (16, 8), (8, 16)]

for pattern in near_patterns:
    print(f"\n--- 全分辨率检测 {pattern} ---")
    for name, variant in variants[:3]:
        print(f"  Testing {name}...", end=" ")
        ret, corners = cv2.findChessboardCorners(variant, pattern, flags_list[0])
        if ret:
            print(f"SUCCESS! Corners={len(corners)}")
        else:
            print("FAILED")

print(f"\n=== SB算法测试 ===")
for pattern in near_patterns[:4]:
    print(f"\n--- SB检测 {pattern} ---")
    for name, variant in variants[:3]:
        print(f"  Testing {name}...", end=" ")
        try:
            ret, corners = cv2.findChessboardCornersSB(variant, pattern)
            if ret:
                print(f"SUCCESS! Corners={len(corners)}")
            else:
                print("FAILED")
        except Exception as e:
            print(f"ERROR: {e}")

print(f"\n=== 分析完成 ===")
