namespace AuroraStruct3D.Permissions;

/// <summary>
/// 产品三维数模管理权限常量定义
/// </summary>
public static class ProductModelPermissions
{
    /// <summary>权限组名称</summary>
    public const string GroupName = "AuroraStruct3D.ProductModel";

    /// <summary>查看数模列表/详情</summary>
    public const string Default = "AuroraStruct3D.ProductModel";

    /// <summary>上传数模文件</summary>
    public const string Upload = "AuroraStruct3D.ProductModel.Upload";

    /// <summary>修改数模名称</summary>
    public const string Rename = "AuroraStruct3D.ProductModel.Rename";

    /// <summary>删除数模（同时删除 BLOB 文件和数据库记录）</summary>
    public const string Delete = "AuroraStruct3D.ProductModel.Delete";

    /// <summary>下载数模文件</summary>
    public const string Download = "AuroraStruct3D.ProductModel.Download";

    /// <summary>手动重试格式转换（仅对 Failed 状态有效）</summary>
    public const string RetryConversion = "AuroraStruct3D.ProductModel.RetryConversion";

    /// <summary>清理文件已丢失的孤立数模记录</summary>
    public const string CleanUp = "AuroraStruct3D.ProductModel.CleanUp";
}
