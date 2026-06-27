namespace AuroraStruct3D.OpenCV.Common;

/// <summary>
/// 3D 几何与线性代数工具方法，供点云拟合/配准算子复用。
/// <para>
/// 统一项目中重复实现的 3×3 对称矩阵 Jacobi 特征值分解、协方差平面求解，
/// 以及 Kabsch 刚体变换求解（SVD 求最优旋转），避免各算子内部重复造轮子。
/// </para>
/// </summary>
public static class Math3D
{
    /// <summary>
    /// 对 3×3 对称矩阵执行 Jacobi 特征值分解。
    /// </summary>
    /// <param name="s00">M[0,0]</param>
    /// <param name="s01">M[0,1] = M[1,0]</param>
    /// <param name="s02">M[0,2] = M[2,0]</param>
    /// <param name="s11">M[1,1]</param>
    /// <param name="s12">M[1,2] = M[2,1]</param>
    /// <param name="s22">M[2,2]</param>
    /// <returns>
    /// (eigenvalues, eigenvectors)：特征值按<b>降序</b>排列，特征向量按<b>列</b>存储且与特征值一一对应
    /// （即 eigenvectors[:, i] 对应 eigenvalues[i]）。最大特征值在索引 0，最小在索引 2。
    /// </returns>
    public static (double[] eigenvalues, double[,] eigenvectors) Jacobi3x3(
        double s00,
        double s01,
        double s02,
        double s11,
        double s12,
        double s22
    )
    {
        double[,] M = new double[3, 3]
        {
            { s00, s01, s02 },
            { s01, s11, s12 },
            { s02, s12, s22 },
        };

        double[,] V = new double[3, 3];
        for (int i = 0; i < 3; i++)
            V[i, i] = 1.0;

        for (int iter = 0; iter < 100; iter++)
        {
            // 找最大非对角元素
            int p = 0,
                q = 1;
            double maxOff = Math.Abs(M[0, 1]);
            if (Math.Abs(M[0, 2]) > maxOff)
            {
                maxOff = Math.Abs(M[0, 2]);
                p = 0;
                q = 2;
            }
            if (Math.Abs(M[1, 2]) > maxOff)
            {
                maxOff = Math.Abs(M[1, 2]);
                p = 1;
                q = 2;
            }

            if (maxOff < 1e-15)
                break;

            double theta = (M[q, q] - M[p, p]) / (2 * M[p, q]);
            double t =
                theta >= 0
                    ? 1.0 / (theta + Math.Sqrt(theta * theta + 1))
                    : 1.0 / (theta - Math.Sqrt(theta * theta + 1));
            double cosA = 1.0 / Math.Sqrt(t * t + 1);
            double sinA = t * cosA;

            double mpp = M[p, p];
            double mqq = M[q, q];
            double mpq = M[p, q];

            M[p, p] = cosA * cosA * mpp + sinA * sinA * mqq - 2 * sinA * cosA * mpq;
            M[q, q] = sinA * sinA * mpp + cosA * cosA * mqq + 2 * sinA * cosA * mpq;
            M[p, q] = M[q, p] = (cosA * cosA - sinA * sinA) * mpq + sinA * cosA * (mpp - mqq);

            for (int r = 0; r < 3; r++)
            {
                if (r == p || r == q)
                    continue;
                double mrp = M[r, p];
                double mrq = M[r, q];
                M[r, p] = M[p, r] = cosA * mrp - sinA * mrq;
                M[r, q] = M[q, r] = sinA * mrp + cosA * mrq;
            }

            for (int r = 0; r < 3; r++)
            {
                double vrp = V[r, p];
                double vrq = V[r, q];
                V[r, p] = cosA * vrp - sinA * vrq;
                V[r, q] = sinA * vrp + cosA * vrq;
            }
        }

        double[] rawEigen = { M[0, 0], M[1, 1], M[2, 2] };

        // 按特征值降序对列重排，保持特征值与特征向量列对应
        int[] order = { 0, 1, 2 };
        Array.Sort(order, (a, b) => rawEigen[b].CompareTo(rawEigen[a]));

        double[] eigenvalues = new double[3];
        double[,] eigenvectors = new double[3, 3];
        for (int c = 0; c < 3; c++)
        {
            eigenvalues[c] = rawEigen[order[c]];
            for (int r = 0; r < 3; r++)
                eigenvectors[r, c] = V[r, order[c]];
        }

        return (eigenvalues, eigenvectors);
    }

    /// <summary>
    /// 接受 3×3 矩阵的 Jacobi 分解重载（仅读取上三角，按对称处理）。
    /// </summary>
    public static (double[] eigenvalues, double[,] eigenvectors) Jacobi3x3(double[,] m) =>
        Jacobi3x3(m[0, 0], m[0, 1], m[0, 2], m[1, 1], m[1, 2], m[2, 2]);

    /// <summary>
    /// 由中心化协方差矩阵（除以点数后的 3×3 对称矩阵分量）与质心求最小二乘平面。
    /// 最小特征值对应的特征向量即为平面法向量。
    /// </summary>
    /// <returns>平面参数 [a, b, c, d]，满足 a²+b²+c²=1，法向量 z 分量非负，d = -(a·cx+b·cy+c·cz)。</returns>
    public static double[] SolvePlaneByCovariance(
        double s00,
        double s01,
        double s02,
        double s11,
        double s12,
        double s22,
        double cx,
        double cy,
        double cz
    )
    {
        var (_, V) = Jacobi3x3(s00, s01, s02, s11, s12, s22);

        // 降序排列后，最小特征值位于索引 2
        double a = V[0, 2];
        double b = V[1, 2];
        double c = V[2, 2];

        double norm = Math.Sqrt(a * a + b * b + c * c);
        if (norm < 1e-12)
            return new[] { 0.0, 0.0, 1.0, -cz };

        a /= norm;
        b /= norm;
        c /= norm;

        if (c < 0)
        {
            a = -a;
            b = -b;
            c = -c;
        }

        double d = -(a * cx + b * cy + c * cz);
        return new[] { a, b, c, d };
    }

    /// <summary>
    /// 由 3×3 互协方差矩阵 H = Σ (src-中心) (tgt-中心)ᵀ 通过 SVD 求最优旋转矩阵。
    /// H = U·Σ·Vᵀ，R = V·Uᵀ，并强制 det(R) = +1 以避免反射。
    /// </summary>
    /// <returns>行主序 3×3 旋转矩阵（长度 9 数组），使 tgt ≈ R·src。</returns>
    public static double[] RotationFromCrossCovariance(
        double h00,
        double h01,
        double h02,
        double h10,
        double h11,
        double h12,
        double h20,
        double h21,
        double h22
    )
    {
        // HᵀH（3×3 对称）
        double s00 = h00 * h00 + h10 * h10 + h20 * h20;
        double s01 = h00 * h01 + h10 * h11 + h20 * h21;
        double s02 = h00 * h02 + h10 * h12 + h20 * h22;
        double s11 = h01 * h01 + h11 * h11 + h21 * h21;
        double s12 = h01 * h02 + h11 * h12 + h21 * h22;
        double s22 = h02 * h02 + h12 * h12 + h22 * h22;

        // HᵀH = V·Σ²·Vᵀ，特征值降序，V 列与之对应
        var (eigenvalues, V) = Jacobi3x3(s00, s01, s02, s11, s12, s22);

        double[] sigma = new double[3];
        for (int i = 0; i < 3; i++)
            sigma[i] = eigenvalues[i] > 0 ? Math.Sqrt(eigenvalues[i]) : 0;

        // U = H·V·Σ⁻¹（逐列）
        double[,] U = new double[3, 3];
        for (int c = 0; c < 3; c++)
        {
            double inv = sigma[c] > 1e-12 ? 1.0 / sigma[c] : 0;
            U[0, c] = (h00 * V[0, c] + h01 * V[1, c] + h02 * V[2, c]) * inv;
            U[1, c] = (h10 * V[0, c] + h11 * V[1, c] + h12 * V[2, c]) * inv;
            U[2, c] = (h20 * V[0, c] + h21 * V[1, c] + h22 * V[2, c]) * inv;
        }

        FixDegenerateColumns(U);

        // R = V·Uᵀ
        double[] R = new double[9];
        for (int r = 0; r < 3; r++)
        for (int c = 0; c < 3; c++)
            R[r * 3 + c] = V[r, 0] * U[c, 0] + V[r, 1] * U[c, 1] + V[r, 2] * U[c, 2];

        // 强制 det(R) = +1
        double det =
            R[0] * (R[4] * R[8] - R[5] * R[7])
            - R[1] * (R[3] * R[8] - R[5] * R[6])
            + R[2] * (R[3] * R[7] - R[4] * R[6]);

        if (det < 0)
        {
            R[2] = -R[2];
            R[5] = -R[5];
            R[8] = -R[8];
        }

        return R;
    }

    /// <summary>
    /// Kabsch 算法：由两组一一对应的点求最优刚体变换（旋转 R + 平移 t），使 tgt ≈ R·src + t。
    /// </summary>
    /// <param name="src">源点集 (x,y,z)。</param>
    /// <param name="tgt">目标点集，与 <paramref name="src"/> 一一对应、数量相同。</param>
    /// <returns>(R, t)：R 为行主序 3×3（长度 9），t 为长度 3 平移向量。</returns>
    public static (double[] R, double[] t) ComputeRigidTransform(
        IReadOnlyList<(double x, double y, double z)> src,
        IReadOnlyList<(double x, double y, double z)> tgt
    )
    {
        if (src.Count != tgt.Count)
            throw new ArgumentException("源点集与目标点集数量不一致，无法计算刚体变换。");
        if (src.Count < 3)
            throw new ArgumentException("对应点对少于 3，无法稳定求解刚体变换。");

        int n = src.Count;
        double sCx = 0,
            sCy = 0,
            sCz = 0,
            tCx = 0,
            tCy = 0,
            tCz = 0;
        for (int i = 0; i < n; i++)
        {
            sCx += src[i].x;
            sCy += src[i].y;
            sCz += src[i].z;
            tCx += tgt[i].x;
            tCy += tgt[i].y;
            tCz += tgt[i].z;
        }
        sCx /= n;
        sCy /= n;
        sCz /= n;
        tCx /= n;
        tCy /= n;
        tCz /= n;

        double h00 = 0,
            h01 = 0,
            h02 = 0,
            h10 = 0,
            h11 = 0,
            h12 = 0,
            h20 = 0,
            h21 = 0,
            h22 = 0;
        for (int i = 0; i < n; i++)
        {
            double sdx = src[i].x - sCx;
            double sdy = src[i].y - sCy;
            double sdz = src[i].z - sCz;
            double tdx = tgt[i].x - tCx;
            double tdy = tgt[i].y - tCy;
            double tdz = tgt[i].z - tCz;

            h00 += sdx * tdx;
            h01 += sdx * tdy;
            h02 += sdx * tdz;
            h10 += sdy * tdx;
            h11 += sdy * tdy;
            h12 += sdy * tdz;
            h20 += sdz * tdx;
            h21 += sdz * tdy;
            h22 += sdz * tdz;
        }

        double[] R = RotationFromCrossCovariance(h00, h01, h02, h10, h11, h12, h20, h21, h22);

        double[] t = new double[3];
        t[0] = tCx - (R[0] * sCx + R[1] * sCy + R[2] * sCz);
        t[1] = tCy - (R[3] * sCx + R[4] * sCy + R[5] * sCz);
        t[2] = tCz - (R[6] * sCx + R[7] * sCy + R[8] * sCz);

        return (R, t);
    }

    /// <summary>
    /// 处理 SVD 退化情况：若 U 某列为接近零向量，用其余两列叉积补全为正交向量。
    /// </summary>
    private static void FixDegenerateColumns(double[,] U)
    {
        for (int c = 0; c < 3; c++)
        {
            double norm = Math.Sqrt(U[0, c] * U[0, c] + U[1, c] * U[1, c] + U[2, c] * U[2, c]);
            if (norm >= 1e-10)
                continue;

            int c1 = (c + 1) % 3;
            int c2 = (c + 2) % 3;

            U[0, c] = U[1, c1] * U[2, c2] - U[2, c1] * U[1, c2];
            U[1, c] = U[2, c1] * U[0, c2] - U[0, c1] * U[2, c2];
            U[2, c] = U[0, c1] * U[1, c2] - U[1, c1] * U[0, c2];

            double n2 = Math.Sqrt(U[0, c] * U[0, c] + U[1, c] * U[1, c] + U[2, c] * U[2, c]);
            if (n2 > 1e-10)
            {
                U[0, c] /= n2;
                U[1, c] /= n2;
                U[2, c] /= n2;
            }
            else
            {
                U[0, c] = c == 0 ? 1.0 : 0;
                U[1, c] = c == 1 ? 1.0 : 0;
                U[2, c] = c == 2 ? 1.0 : 0;
            }
        }
    }
}
