namespace AuroraStruct3D.Permissions
{
    /// <summary>
    /// 应用级访问权限。访客不授予权限；操作员授予 Operation；管理员同时授予
    /// Operation 和 Management。
    /// </summary>
    public static class AuroraStruct3DPermissions
    {
        public const string GroupName = AuroraStruct3DAccessPermissions.GroupName;

        public const string Operation = AuroraStruct3DAccessPermissions.Operation;

        public const string Management = AuroraStruct3DAccessPermissions.Management;
    }
}
