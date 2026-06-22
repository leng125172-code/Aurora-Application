namespace AuroraStruct3D.OpenCV.Attributes;

/// <summary>
/// 标记矩阵类型的特性，用于区分 2D 图像矩阵和 3D 点云坐标矩阵。
/// 应用于 <see cref="IVisionParameter"/> 实现类或属性上。
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class MatTypeAttribute : Attribute
{
    public PortMatType MatType { get; }

    public MatTypeAttribute(PortMatType matType)
    {
        MatType = matType;
    }
}