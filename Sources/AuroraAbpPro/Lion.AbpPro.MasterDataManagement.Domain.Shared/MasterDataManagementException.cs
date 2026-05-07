namespace Lion.AbpPro.MasterDataManagement;

public class MasterDataManagementException : BusinessException
{
    public MasterDataManagementException(
        string code = null,
        string message = null,
        string details = null,
        Exception innerException = null,
        LogLevel logLevel = LogLevel.Warning
    )
        : base(code, message, details, innerException, logLevel) { }
}
