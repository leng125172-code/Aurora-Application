namespace AuroraStruct3D.OpenCV.Common;

/// <summary>
/// 点云通用工具方法：子集提取、质心计算、法向量估计，供拟合/配准算子复用。
/// </summary>
public static class PointCloudUtils
{
    /// <summary>
    /// 按行索引从点云中提取子集，同时复制对应的颜色数据（若存在）。
    /// </summary>
    /// <param name="input">原始点云数据（用于读取颜色）。</param>
    /// <param name="indices">要提取的行索引集合。</param>
    /// <param name="pointCloud">点云坐标 Mat（N×C，CV_32FC1）。</param>
    /// <returns>(points, colors)：提取后的坐标 Mat 与颜色 Mat；无颜色时 colors 为 null；索引为空时返回空 Mat。</returns>
    public static (Mat points, Mat? colors) ExtractSubset(
        PointCloudData input,
        IReadOnlyList<int> indices,
        Mat pointCloud
    )
    {
        if (indices.Count == 0)
            return (new Mat(), null);

        int colCount = pointCloud.Cols;
        Mat result = new Mat(indices.Count, colCount, MatType.CV_32FC1);
        for (int i = 0; i < indices.Count; i++)
        {
            int srcIdx = indices[i];
            for (int c = 0; c < colCount; c++)
                result.Set(i, c, pointCloud.Get<float>(srcIdx, c));
        }

        Mat? colors = null;
        if (input.HasColors && input.Colors != null)
        {
            colors = new Mat(indices.Count, 3, MatType.CV_8UC3);
            for (int i = 0; i < indices.Count; i++)
            {
                int srcIdx = indices[i];
                for (int c = 0; c < 3; c++)
                    colors.Set<byte>(i, c, input.Colors.Get<byte>(srcIdx, c));
            }
        }

        return (result, colors);
    }

    /// <summary>
    /// 构建一个 <see cref="PointCloudData"/> 输出实例，封装坐标与可选颜色。
    /// </summary>
    public static PointCloudData BuildCloud(Mat points, Mat? colors)
    {
        var cloud = new PointCloudData { Value = points };
        if (colors != null)
            cloud.SetColors(colors);
        return cloud;
    }

    /// <summary>
    /// 计算点云前 <paramref name="count"/> 个点的 XYZ 质心。
    /// </summary>
    public static (double cx, double cy, double cz) Centroid(Mat pointCloud, int count)
    {
        double cx = 0,
            cy = 0,
            cz = 0;
        for (int i = 0; i < count; i++)
        {
            cx += pointCloud.Get<float>(i, 0);
            cy += pointCloud.Get<float>(i, 1);
            cz += pointCloud.Get<float>(i, 2);
        }
        if (count > 0)
        {
            cx /= count;
            cy /= count;
            cz /= count;
        }
        return (cx, cy, cz);
    }

    /// <summary>
    /// 为点云每个点估计法向量：基于 k 近邻邻域协方差的 PCA，取最小特征值对应特征向量。
    /// <para>
    /// 采用暴力 k 近邻搜索（O(N²)），适用于中小规模点云；方向默认指向远离点云质心一侧。
    /// </para>
    /// </summary>
    /// <param name="pointCloud">点云坐标 Mat（N×C，CV_32FC1，前三列为 XYZ）。</param>
    /// <param name="k">近邻点数（自动裁剪到 [3, N-1]）。</param>
    /// <returns>法向量 Mat（N×3，CV_32FC1），每行 (nx, ny, nz)。</returns>
    public static Mat EstimateNormals(Mat pointCloud, int k)
    {
        int n = pointCloud.Rows;
        if (n < 3)
            throw new InvalidOperationException("点云点数少于 3，无法估计法向量。");

        int knn = Math.Clamp(k, 3, n - 1);

        float[] xs = new float[n];
        float[] ys = new float[n];
        float[] zs = new float[n];
        for (int i = 0; i < n; i++)
        {
            xs[i] = pointCloud.Get<float>(i, 0);
            ys[i] = pointCloud.Get<float>(i, 1);
            zs[i] = pointCloud.Get<float>(i, 2);
        }

        // 整体质心，用于法向量方向一致化
        double gcx = 0,
            gcy = 0,
            gcz = 0;
        for (int i = 0; i < n; i++)
        {
            gcx += xs[i];
            gcy += ys[i];
            gcz += zs[i];
        }
        gcx /= n;
        gcy /= n;
        gcz /= n;

        Mat normals = new Mat(n, 3, MatType.CV_32FC1);

        // 复用缓冲，避免每点分配
        double[] nDist = new double[n];
        int[] nIdx = new int[n];

        for (int i = 0; i < n; i++)
        {
            // 暴力求 k 近邻（排除自身）
            for (int j = 0; j < n; j++)
            {
                double dx = xs[j] - xs[i];
                double dy = ys[j] - ys[i];
                double dz = zs[j] - zs[i];
                nDist[j] = j == i ? double.MaxValue : dx * dx + dy * dy + dz * dz;
                nIdx[j] = j;
            }
            Array.Sort(nDist, nIdx);

            double cx = 0,
                cy = 0,
                cz = 0;
            for (int j = 0; j < knn; j++)
            {
                int idx = nIdx[j];
                cx += xs[idx];
                cy += ys[idx];
                cz += zs[idx];
            }
            cx /= knn;
            cy /= knn;
            cz /= knn;

            double c00 = 0,
                c01 = 0,
                c02 = 0,
                c11 = 0,
                c12 = 0,
                c22 = 0;
            for (int j = 0; j < knn; j++)
            {
                int idx = nIdx[j];
                double dx = xs[idx] - cx;
                double dy = ys[idx] - cy;
                double dz = zs[idx] - cz;
                c00 += dx * dx;
                c01 += dx * dy;
                c02 += dx * dz;
                c11 += dy * dy;
                c12 += dy * dz;
                c22 += dz * dz;
            }

            var (_, V) = Math3D.Jacobi3x3(
                c00 / knn,
                c01 / knn,
                c02 / knn,
                c11 / knn,
                c12 / knn,
                c22 / knn
            );

            // 最小特征值位于索引 2（降序）
            double nx = V[0, 2];
            double ny = V[1, 2];
            double nz = V[2, 2];
            double norm = Math.Sqrt(nx * nx + ny * ny + nz * nz);
            if (norm > 1e-12)
            {
                nx /= norm;
                ny /= norm;
                nz /= norm;
            }

            // 方向一致化：指向远离整体质心一侧
            double vx = xs[i] - gcx;
            double vy = ys[i] - gcy;
            double vz = zs[i] - gcz;
            if (vx * nx + vy * ny + vz * nz < 0)
            {
                nx = -nx;
                ny = -ny;
                nz = -nz;
            }

            normals.Set(i, 0, (float)nx);
            normals.Set(i, 1, (float)ny);
            normals.Set(i, 2, (float)nz);
        }

        return normals;
    }
}
