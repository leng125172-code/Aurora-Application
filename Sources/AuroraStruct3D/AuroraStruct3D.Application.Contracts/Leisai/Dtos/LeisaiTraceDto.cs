namespace AuroraStruct3D.Leisai.Dtos;

/// <summary>
/// 雷赛 iCL-RS 电机位置-速度滚动轨迹 DTO。
/// 后端为每个轴维护一个最多 <c>1200</c> 点的滑动窗口；
/// 每次成功采样后追加新点、移除超出最大值的旧点，再将完整序列通过 SignalR 推送给前端。
/// </summary>
public class LeisaiTraceDto
{
    /// <summary>电机轴 ID。</summary>
    public Guid AxisId { get; set; }

    /// <summary>
    /// 实际位置序列（脉冲），从旧到新排列，最多 1200 点。
    /// 与 <see cref="CommandPositions"/>、<see cref="Speeds"/> 等长。
    /// </summary>
    public int[] ActualPositions { get; set; } = [];

    /// <summary>
    /// 指令位置序列（脉冲），从旧到新排列，与 <see cref="ActualPositions"/> 等长。
    /// </summary>
    public int[] CommandPositions { get; set; } = [];

    /// <summary>
    /// 有效速度序列（rpm），从旧到新排列，与 <see cref="ActualPositions"/> 等长。
    /// </summary>
    public int[] Speeds { get; set; } = [];
}
