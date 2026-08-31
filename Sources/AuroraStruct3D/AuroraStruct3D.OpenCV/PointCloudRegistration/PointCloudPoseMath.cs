namespace AuroraStruct3D.OpenCV.PointCloudRegistration;

internal static class PointCloudPoseMath
{
    public static double[] RotationFromEulerZyxDegrees(double rxDeg, double ryDeg, double rzDeg)
    {
        double rx = rxDeg * Math.PI / 180.0;
        double ry = ryDeg * Math.PI / 180.0;
        double rz = rzDeg * Math.PI / 180.0;
        double sx = Math.Sin(rx), cx = Math.Cos(rx);
        double sy = Math.Sin(ry), cy = Math.Cos(ry);
        double sz = Math.Sin(rz), cz = Math.Cos(rz);

        return
        [
            cz * cy,
            cz * sy * sx - sz * cx,
            cz * sy * cx + sz * sx,
            sz * cy,
            sz * sy * sx + cz * cx,
            sz * sy * cx - cz * sx,
            -sy,
            cy * sx,
            cy * cx,
        ];
    }

    public static (double rxDeg, double ryDeg, double rzDeg) EulerZyxDegreesFromRotation(
        double[] r
    )
    {
        double ry = Math.Asin(Math.Clamp(-r[6], -1.0, 1.0));
        double cy = Math.Cos(ry);
        double rx;
        double rz;
        if (Math.Abs(cy) > 1e-9)
        {
            rx = Math.Atan2(r[7], r[8]);
            rz = Math.Atan2(r[3], r[0]);
        }
        else
        {
            rx = 0;
            rz = Math.Atan2(-r[1], r[4]);
        }
        const double toDeg = 180.0 / Math.PI;
        return (rx * toDeg, ry * toDeg, rz * toDeg);
    }

    public static Mat CreateTransformMatrix(double[] rotation, double[] translation)
    {
        Mat matrix = Mat.Eye(4, 4, MatType.CV_64FC1);
        for (int row = 0; row < 3; row++)
        {
            for (int col = 0; col < 3; col++)
                matrix.Set(row, col, rotation[row * 3 + col]);
            matrix.Set(row, 3, translation[row]);
        }
        return matrix;
    }

    public static Mat TransformCloud(Mat cloud, double[] rotation, double[] translation)
    {
        int columns = cloud.Cols;
        bool hasNormals = columns >= 6;
        Mat output = new(cloud.Rows, columns, MatType.CV_32FC1);
        Parallel.For(
            0,
            cloud.Rows,
            index =>
            {
                double x = cloud.Get<float>(index, 0);
                double y = cloud.Get<float>(index, 1);
                double z = cloud.Get<float>(index, 2);
                output.Set(index, 0, (float)(rotation[0] * x + rotation[1] * y + rotation[2] * z + translation[0]));
                output.Set(index, 1, (float)(rotation[3] * x + rotation[4] * y + rotation[5] * z + translation[1]));
                output.Set(index, 2, (float)(rotation[6] * x + rotation[7] * y + rotation[8] * z + translation[2]));

                if (hasNormals)
                {
                    double nx = cloud.Get<float>(index, 3);
                    double ny = cloud.Get<float>(index, 4);
                    double nz = cloud.Get<float>(index, 5);
                    output.Set(index, 3, (float)(rotation[0] * nx + rotation[1] * ny + rotation[2] * nz));
                    output.Set(index, 4, (float)(rotation[3] * nx + rotation[4] * ny + rotation[5] * nz));
                    output.Set(index, 5, (float)(rotation[6] * nx + rotation[7] * ny + rotation[8] * nz));
                }
                for (int col = hasNormals ? 6 : 3; col < columns; col++)
                    output.Set(index, col, cloud.Get<float>(index, col));
            }
        );
        return output;
    }

    public static double[] MultiplyRotation(double[] left, double[] right)
    {
        var result = new double[9];
        for (int row = 0; row < 3; row++)
        for (int col = 0; col < 3; col++)
            result[row * 3 + col] =
                left[row * 3] * right[col]
                + left[row * 3 + 1] * right[3 + col]
                + left[row * 3 + 2] * right[6 + col];
        return result;
    }

    public static double[] ComposeTranslation(double[] leftR, double[] leftT, double[] rightT) =>
    [
        leftR[0] * rightT[0] + leftR[1] * rightT[1] + leftR[2] * rightT[2] + leftT[0],
        leftR[3] * rightT[0] + leftR[4] * rightT[1] + leftR[5] * rightT[2] + leftT[1],
        leftR[6] * rightT[0] + leftR[7] * rightT[1] + leftR[8] * rightT[2] + leftT[2],
    ];
}
