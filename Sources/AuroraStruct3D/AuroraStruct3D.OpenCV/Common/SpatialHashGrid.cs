namespace AuroraStruct3D.OpenCV.Common;

/// <summary>
/// 3D 空间哈希网格，用于加速点云最近邻搜索。
/// <para>
/// 将点按固定边长的立方体单元格哈希存储，查询时从查询点所在单元格向外
/// 逐层扩张（半径 0/1/2）收集候选点，再精确比较距离取最近者。
/// 适用于 ICP 配准、点云间距离等需要大量最近邻查询的场景。
/// </para>
/// </summary>
public sealed class SpatialHashGrid
{
    private readonly float[] _xs;
    private readonly float[] _ys;
    private readonly float[] _zs;
    private readonly float _invCellSize;
    private readonly float _minX,
        _minY,
        _minZ;
    private readonly int _gridSizeX,
        _gridSizeY,
        _gridSizeZ;
    private readonly Dictionary<int, List<int>> _cells;

    /// <summary>
    /// 用点云坐标数组构建空间哈希网格。网格持有数组引用（不复制），
    /// 调用方在网格生命周期内不应修改这些数组。
    /// </summary>
    /// <param name="xs">X 坐标数组。</param>
    /// <param name="ys">Y 坐标数组。</param>
    /// <param name="zs">Z 坐标数组。</param>
    /// <param name="cellSize">单元格边长，通常取最大对应距离或搜索半径。</param>
    /// <param name="pointCount">参与构建的点数，默认使用 <paramref name="xs"/> 全长。</param>
    public SpatialHashGrid(float[] xs, float[] ys, float[] zs, double cellSize, int pointCount = -1)
    {
        _xs = xs;
        _ys = ys;
        _zs = zs;
        int count = pointCount < 0 ? xs.Length : pointCount;

        if (cellSize <= 0)
            throw new ArgumentException("单元格边长必须为正数。", nameof(cellSize));

        _invCellSize = 1.0f / (float)cellSize;

        _minX = float.MaxValue;
        _minY = float.MaxValue;
        _minZ = float.MaxValue;
        float maxX = float.MinValue,
            maxY = float.MinValue,
            maxZ = float.MinValue;
        for (int i = 0; i < count; i++)
        {
            if (xs[i] < _minX)
                _minX = xs[i];
            if (ys[i] < _minY)
                _minY = ys[i];
            if (zs[i] < _minZ)
                _minZ = zs[i];
            if (xs[i] > maxX)
                maxX = xs[i];
            if (ys[i] > maxY)
                maxY = ys[i];
            if (zs[i] > maxZ)
                maxZ = zs[i];
        }

        _gridSizeX = Math.Max(1, (int)((maxX - _minX) * _invCellSize) + 1);
        _gridSizeY = Math.Max(1, (int)((maxY - _minY) * _invCellSize) + 1);
        _gridSizeZ = Math.Max(1, (int)((maxZ - _minZ) * _invCellSize) + 1);

        _cells = new Dictionary<int, List<int>>();
        for (int i = 0; i < count; i++)
        {
            int key = GetCellKey(xs[i], ys[i], zs[i]);
            if (!_cells.TryGetValue(key, out var list))
            {
                list = new List<int>();
                _cells[key] = list;
            }
            list.Add(i);
        }
    }

    /// <summary>
    /// 查找距离查询点最近的点索引；若无候选点返回 -1。
    /// </summary>
    /// <param name="qx">查询点 X。</param>
    /// <param name="qy">查询点 Y。</param>
    /// <param name="qz">查询点 Z。</param>
    /// <param name="bestDistSq">输出：到最近点的平方距离（未找到时为 <see cref="double.MaxValue"/>）。</param>
    /// <returns>最近点索引，或 -1。</returns>
    public int FindNearestNeighbor(float qx, float qy, float qz, out double bestDistSq)
    {
        int cx = (int)((qx - _minX) * _invCellSize);
        int cy = (int)((qy - _minY) * _invCellSize);
        int cz = (int)((qz - _minZ) * _invCellSize);

        int bestIdx = -1;
        float best = float.MaxValue;

        for (int radius = 0; radius < 3; radius++)
        {
            bool found = false;

            for (int dx = -radius; dx <= radius; dx++)
            for (int dy = -radius; dy <= radius; dy++)
            for (int dz = -radius; dz <= radius; dz++)
            {
                // 仅遍历当前半径的“壳层”，避免重复扫描内层
                if (
                    radius > 0
                    && Math.Abs(dx) < radius
                    && Math.Abs(dy) < radius
                    && Math.Abs(dz) < radius
                )
                    continue;

                int nx = cx + dx;
                int ny = cy + dy;
                int nz = cz + dz;
                if (
                    nx < 0
                    || nx >= _gridSizeX
                    || ny < 0
                    || ny >= _gridSizeY
                    || nz < 0
                    || nz >= _gridSizeZ
                )
                    continue;

                int key = nz * _gridSizeY * _gridSizeX + ny * _gridSizeX + nx;
                if (!_cells.TryGetValue(key, out var cell))
                    continue;

                found = true;
                foreach (int idx in cell)
                {
                    float ex = _xs[idx] - qx;
                    float ey = _ys[idx] - qy;
                    float ez = _zs[idx] - qz;
                    float distSq = ex * ex + ey * ey + ez * ez;
                    if (distSq < best)
                    {
                        best = distSq;
                        bestIdx = idx;
                    }
                }
            }

            if (found)
                break;
        }

        bestDistSq = bestIdx < 0 ? double.MaxValue : best;
        return bestIdx;
    }

    /// <summary>
    /// 收集查询点指定半径范围内（曼哈顿单元格层数）的所有候选点索引。
    /// 用于半径搜索/特征计算等需要邻域点集的场景。
    /// </summary>
    /// <param name="qx">查询点 X。</param>
    /// <param name="qy">查询点 Y。</param>
    /// <param name="qz">查询点 Z。</param>
    /// <param name="cellRadius">向外扩张的单元格层数。</param>
    /// <returns>候选点索引列表（未按距离过滤，调用方需自行判定）。</returns>
    public List<int> CollectCandidates(float qx, float qy, float qz, int cellRadius)
    {
        var result = new List<int>();
        int cx = (int)((qx - _minX) * _invCellSize);
        int cy = (int)((qy - _minY) * _invCellSize);
        int cz = (int)((qz - _minZ) * _invCellSize);

        for (int dx = -cellRadius; dx <= cellRadius; dx++)
        for (int dy = -cellRadius; dy <= cellRadius; dy++)
        for (int dz = -cellRadius; dz <= cellRadius; dz++)
        {
            int nx = cx + dx;
            int ny = cy + dy;
            int nz = cz + dz;
            if (
                nx < 0
                || nx >= _gridSizeX
                || ny < 0
                || ny >= _gridSizeY
                || nz < 0
                || nz >= _gridSizeZ
            )
                continue;

            int key = nz * _gridSizeY * _gridSizeX + ny * _gridSizeX + nx;
            if (_cells.TryGetValue(key, out var cell))
                result.AddRange(cell);
        }

        return result;
    }

    private int GetCellKey(float x, float y, float z)
    {
        int cx = Math.Clamp((int)((x - _minX) * _invCellSize), 0, _gridSizeX - 1);
        int cy = Math.Clamp((int)((y - _minY) * _invCellSize), 0, _gridSizeY - 1);
        int cz = Math.Clamp((int)((z - _minZ) * _invCellSize), 0, _gridSizeZ - 1);
        return cz * _gridSizeY * _gridSizeX + cy * _gridSizeX + cx;
    }
}
