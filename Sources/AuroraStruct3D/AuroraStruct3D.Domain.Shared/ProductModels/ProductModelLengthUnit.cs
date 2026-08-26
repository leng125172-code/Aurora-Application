using System.ComponentModel;

namespace AuroraStruct3D.ProductModels;

/// <summary>
/// 产品数模文件中坐标使用的长度单位。工作流点云统一换算为毫米。
/// </summary>
public enum ProductModelLengthUnit
{
    [Description("毫米 (mm)")]
    Millimeter = 0,

    [Description("厘米 (cm)")]
    Centimeter = 1,

    [Description("米 (m)")]
    Meter = 2,

    [Description("英寸 (in)")]
    Inch = 3,
}

public static class ProductModelLengthUnitExtensions
{
    public static double ToMillimeterScale(this ProductModelLengthUnit unit) =>
        unit switch
        {
            ProductModelLengthUnit.Millimeter => 1d,
            ProductModelLengthUnit.Centimeter => 10d,
            ProductModelLengthUnit.Meter => 1000d,
            ProductModelLengthUnit.Inch => 25.4d,
            _ => throw new ArgumentOutOfRangeException(nameof(unit), unit, "不支持的产品数模长度单位。"),
        };

    public static string GetDisplayName(this ProductModelLengthUnit unit) =>
        unit switch
        {
            ProductModelLengthUnit.Millimeter => "毫米 (mm)",
            ProductModelLengthUnit.Centimeter => "厘米 (cm)",
            ProductModelLengthUnit.Meter => "米 (m)",
            ProductModelLengthUnit.Inch => "英寸 (in)",
            _ => unit.ToString(),
        };
}
