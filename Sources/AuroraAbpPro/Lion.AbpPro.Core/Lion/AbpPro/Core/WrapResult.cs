namespace Lion.AbpPro.Core;

public class WrapResult<T>
{
    public bool Success { get; set; }

    public string Message { get; set; }

    public T Data { get; set; }

    public string Code { get; set; }

    public WrapResult()
    {
        Success = true;
        Message = "Success";
        Data = default;
        Code = "200";
    }

    public void SetSuccess(T data, string message = "Success", string code = "200")
    {
        Success = true;
        Data = data;
        Code = code;
    }

    public void SetFail(string message = "Fail", string code = "500")
    {
        Success = false;
        Message = message;
        Code = code;
    }
}
