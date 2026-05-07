namespace Lion.AbpPro.SignalR;

public class ForceOutDto
{
    public ForceOutDto(string message)
    {
        Message = message;
    }

    /// <summary>
    /// 下线提示信息
    /// </summary>
    public string Message { get; set; }
}
