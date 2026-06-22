namespace AuroraStruct3D.Workflow;

/// <summary>
/// 端口矩阵类型，用于区分 2D 图像矩阵和 3D 点云坐标矩阵。
/// </summary>
public enum PortMatType
{
    /// <summary>
    /// 2D 图像矩阵，如彩色图像 Mat(H, W, CV_8UC3)。
    /// 通常具有多个通道（BGR），用于图像处理。
    /// </summary>
    Image2D,

    /// <summary>
    /// 3D 点云坐标矩阵，如 N×3 的点云坐标 Mat(N, 3, CV_32FC1)。
    /// 通常为单通道，每列代表一个坐标维度（X, Y, Z）。
    /// </summary>
    PointCloud3D,

    /// <summary>
    /// 通用矩阵，未明确指定类型。
    /// 算子应根据实际数据结构自动判断。
    /// </summary>
    Generic,
}
