namespace Lion.AbpPro.DynamicMenuManagement.Menus
{
    public class MenuDomainException : BusinessException
    {
        public MenuDomainException(
            string code = null,
            string message = null,
            string details = null,
            Exception innerException = null,
            LogLevel logLevel = LogLevel.Warning
        )
            : base(code, message, details, innerException, logLevel) { }
    }
}
