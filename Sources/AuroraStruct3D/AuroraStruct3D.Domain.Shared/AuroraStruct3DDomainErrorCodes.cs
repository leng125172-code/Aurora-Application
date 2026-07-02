namespace AuroraStruct3D
{
    public static class AuroraStruct3DDomainErrorCodes
    {
        /// <summary>设备被其他客户端占用，拒绝操作（HTTP 409）</summary>
        public const string DeviceOccupied = "AuroraStruct3D:DeviceOccupied";

        /// <summary>变量重复定义。</summary>
        public const string VariableDuplicateDefinition = "VAR1001";

        /// <summary>变量引用未定义。</summary>
        public const string VariableUndefinedReference = "VAR1002";

        /// <summary>变量类型不一致。</summary>
        public const string VariableTypeMismatch = "VAR1003";

        /// <summary>只读变量写入被禁止。</summary>
        public const string VariableReadonlyWriteForbidden = "VAR1004";

        /// <summary>跨工作流写入被禁止。</summary>
        public const string VariableCrossWorkflowWriteForbidden = "VAR1005";

        /// <summary>读取未初始化变量。</summary>
        public const string VariableUninitializedRead = "VAR1006";

        /// <summary>变量等待超时。</summary>
        public const string VariableWaitTimeout = "VAR1007";

        /// <summary>变量并发版本冲突。</summary>
        public const string VariableConcurrencyConflict = "VAR1008";

        /// <summary>变量可见性拒绝访问。</summary>
        public const string VariableVisibilityDenied = "VAR1009";

        /// <summary>变量访问请求参数非法。</summary>
        public const string VariableBadRequest = "VAR1011";
    }
}
