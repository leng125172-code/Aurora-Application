def analyze_ply_z_axis(file_path):
    print(f"正在读取并分析文件: {file_path}")

    header_ended = False
    z_coords = []

    # 1. 流式读取 PLY 文件
    with open(file_path, "r", encoding="utf-8", errors="ignore") as f:
        for line in f:
            line = line.strip()
            if not header_ended:
                if line == "end_header":
                    header_ended = True
                continue

            # 2. 解析数据行
            parts = line.split()
            if len(parts) >= 3:
                try:
                    # PLY 格式前三列固定为 X, Y, Z
                    z = float(parts[2])
                    # 过滤掉相机默认生成的绝对 0.0 点（未识别区域）
                    if abs(z) > 1e-5:
                        z_coords.append(z)
                except ValueError:
                    continue

    if not z_coords:
        print("❌ 未在文件中找到有效的 Z 轴点数据！")
        return

    # 3. 计算统计学特征
    z_coords.sort()
    total_points = len(z_coords)

    z_min = z_coords[0]
    z_max = z_coords[-1]

    # 采用分位数（去掉上下 1% 的极端飞点噪声，精准定位工件表面）
    p1_index = int(total_points * 0.01)
    p99_index = int(total_points * 0.99)

    # 💡【这里已修正拼写错误】全部统一为小写的 suggested_min
    suggested_min = z_coords[p1_index]
    suggested_max = z_coords[p99_index]

    # 4. 打印分析报告
    print("\n" + "=" * 40)
    print("📊 PLY 点云 Z 轴统计报告")
    print("=" * 40)
    print(f"有效点总数 (已剔除0点): {total_points} 个")
    print(f"Z 轴绝对范围: {z_min:.4f} 至 {z_max:.4f}")
    print("-" * 40)
    print("💡 HALCON 过滤范围终极建议 (已去除 1% 噪声飞点):")
    print(f"👉 select_points_object_model_3d 范围应设置为:")
    print(f"   Min: {suggested_min - 1.0:.2f}")
    print(f"   Max: {suggested_max + 1.0:.2f}")
    print("=" * 40)


# 使用你的文件路径运行
file_path = r"C:/Users/zhengkai/Desktop/a1b2c3d4e5f67890abcdef1234567890_20260630135025_126_SizectorS_PointCloudExport (1).ply"
analyze_ply_z_axis(file_path)
