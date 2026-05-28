using System.ComponentModel;

namespace AuroraStruct3D.ProductModels;

/// <summary>
/// 产品三维数模支持的文件格式枚举。
/// 区分"无需转换"和"需异步转换为 PLY"两类格式。
/// </summary>
public enum ProductModelFormat
{
    // ── 无需转换（OpenCV 直接解析） ──────────────────────────────────────────

    /// <summary>PLY 点云/网格格式（直接解析，无需转换）</summary>
    [Description("PLY")]
    PLY = 0,

    /// <summary>单个 OBJ 文件（不含 MTL，直接解析，无需转换）</summary>
    [Description("OBJ")]
    OBJ = 1,

    // ── 推荐转换格式（稳定支持，推荐使用） ──────────────────────────────────

    /// <summary>STEP 工业标准三维格式（推荐，通过 OCC 转换为 PLY）</summary>
    [Description("STEP/STP")]
    STEP = 10,

    /// <summary>IGES 工业标准三维格式（推荐，通过 OCC 转换为 PLY）</summary>
    [Description("IGES/IGS")]
    IGES = 11,

    /// <summary>STL 三角面片格式（推荐，通过 OCC 转换为 PLY）</summary>
    [Description("STL")]
    STL = 12,

    // ── 有限支持格式 ─────────────────────────────────────────────────────────

    /// <summary>单个 GLB 二进制 glTF 文件（有限支持，通过转换引擎处理）</summary>
    [Description("GLB")]
    GLB = 20,

    /// <summary>完整 glTF+bin 组合（有限支持，需打包上传，通过转换引擎处理）</summary>
    [Description("GLTF")]
    GLTF = 21,

    /// <summary>PCD 点云数据格式（有限支持，通过 Open3D 转换为 PLY）</summary>
    [Description("PCD")]
    PCD = 22,
}
