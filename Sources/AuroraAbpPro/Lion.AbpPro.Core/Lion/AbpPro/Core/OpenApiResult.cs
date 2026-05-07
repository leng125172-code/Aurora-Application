namespace Lion.AbpPro.Core;

public class OpenApiResult<T> : WrapResult<T>
{
    public string RequestId { get; set; }
}
