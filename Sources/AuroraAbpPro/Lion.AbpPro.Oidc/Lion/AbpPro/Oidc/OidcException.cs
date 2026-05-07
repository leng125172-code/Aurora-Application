using Microsoft.Extensions.Logging;
using Volo.Abp;

namespace Lion.AbpPro.Oidc;

public class OidcException : BusinessException
{
    public OidcException(
        string code = null,
        string message = null,
        string details = null,
        Exception innerException = null,
        LogLevel logLevel = LogLevel.Warning
    )
        : base(code, message, details, innerException, logLevel) { }
}
