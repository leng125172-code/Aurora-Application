namespace AuroraStruct3D.OpenCV.Common;

/// <summary>
/// 面向静态三维点云的平衡 KD-tree。构建完成后只读，可安全并行查询精确最近邻。
/// </summary>
public sealed class KdTree3D
{
    private readonly float[] _x;
    private readonly float[] _y;
    private readonly float[] _z;
    private readonly int[] _indices;
    private readonly int[] _left;
    private readonly int[] _right;
    private readonly byte[] _axis;
    private readonly int _root;

    public KdTree3D(float[] x, float[] y, float[] z, int pointCount = -1)
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(y);
        ArgumentNullException.ThrowIfNull(z);
        int count = pointCount < 0 ? x.Length : pointCount;
        if (count <= 0 || count > x.Length || count > y.Length || count > z.Length)
            throw new ArgumentOutOfRangeException(nameof(pointCount));

        _x = x;
        _y = y;
        _z = z;
        _indices = new int[count];
        _left = new int[count];
        _right = new int[count];
        _axis = new byte[count];
        Array.Fill(_left, -1);
        Array.Fill(_right, -1);
        for (int i = 0; i < count; i++)
        {
            if (!float.IsFinite(x[i]) || !float.IsFinite(y[i]) || !float.IsFinite(z[i]))
                throw new ArgumentException("KD-tree 不接受 NaN 或无穷坐标。");
            _indices[i] = i;
        }

        _root = Build(0, count, 0);
    }

    public int FindNearestNeighborWithin(
        float queryX,
        float queryY,
        float queryZ,
        double maxDistance,
        out double bestDistanceSquared
    )
    {
        if (!double.IsFinite(maxDistance) || maxDistance <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxDistance));

        int bestIndex = -1;
        double best = maxDistance * maxDistance;
        Search(_root, queryX, queryY, queryZ, ref bestIndex, ref best);
        bestDistanceSquared = bestIndex >= 0 ? best : double.MaxValue;
        return bestIndex;
    }

    private int Build(int start, int end, int depth)
    {
        if (start >= end)
            return -1;

        int axis = depth % 3;
        int median = start + (end - start) / 2;
        SelectKth(start, end - 1, median, axis);
        int node = _indices[median];
        _axis[node] = (byte)axis;
        _left[node] = Build(start, median, depth + 1);
        _right[node] = Build(median + 1, end, depth + 1);
        return node;
    }

    private void SelectKth(int left, int right, int kth, int axis)
    {
        while (left < right)
        {
            int pivot = MedianOfThree(left, left + (right - left) / 2, right, axis);
            pivot = Partition(left, right, pivot, axis);
            if (pivot == kth)
                return;
            if (kth < pivot)
                right = pivot - 1;
            else
                left = pivot + 1;
        }
    }

    private int Partition(int left, int right, int pivotPosition, int axis)
    {
        int pivotPoint = _indices[pivotPosition];
        Swap(pivotPosition, right);
        int store = left;
        for (int i = left; i < right; i++)
        {
            if (Compare(_indices[i], pivotPoint, axis) < 0)
            {
                Swap(i, store);
                store++;
            }
        }
        Swap(store, right);
        return store;
    }

    private int MedianOfThree(int a, int b, int c, int axis)
    {
        if (Compare(_indices[a], _indices[b], axis) > 0)
            (a, b) = (b, a);
        if (Compare(_indices[b], _indices[c], axis) > 0)
            (b, c) = (c, b);
        if (Compare(_indices[a], _indices[b], axis) > 0)
            (a, b) = (b, a);
        return b;
    }

    private int Compare(int first, int second, int axis)
    {
        int comparison = Coordinate(first, axis).CompareTo(Coordinate(second, axis));
        return comparison != 0 ? comparison : first.CompareTo(second);
    }

    private float Coordinate(int point, int axis) =>
        axis switch
        {
            0 => _x[point],
            1 => _y[point],
            _ => _z[point],
        };

    private void Swap(int first, int second)
    {
        if (first == second)
            return;
        (_indices[first], _indices[second]) = (_indices[second], _indices[first]);
    }

    private void Search(
        int node,
        float queryX,
        float queryY,
        float queryZ,
        ref int bestIndex,
        ref double bestDistanceSquared
    )
    {
        if (node < 0)
            return;

        double dx = _x[node] - queryX;
        double dy = _y[node] - queryY;
        double dz = _z[node] - queryZ;
        double distanceSquared = dx * dx + dy * dy + dz * dz;
        if (distanceSquared <= bestDistanceSquared)
        {
            bestDistanceSquared = distanceSquared;
            bestIndex = node;
            if (distanceSquared == 0)
                return;
        }

        int axis = _axis[node];
        double delta = axis switch
        {
            0 => queryX - _x[node],
            1 => queryY - _y[node],
            _ => queryZ - _z[node],
        };
        int near = delta <= 0 ? _left[node] : _right[node];
        int far = delta <= 0 ? _right[node] : _left[node];
        Search(near, queryX, queryY, queryZ, ref bestIndex, ref bestDistanceSquared);
        if (delta * delta <= bestDistanceSquared)
            Search(far, queryX, queryY, queryZ, ref bestIndex, ref bestDistanceSquared);
    }
}
