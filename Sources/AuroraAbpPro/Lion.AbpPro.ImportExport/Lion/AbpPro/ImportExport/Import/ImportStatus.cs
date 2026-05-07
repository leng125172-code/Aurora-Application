namespace Lion.AbpPro.ImportExport.Import;

public enum ImportStatus
{
    /// <summary>
    /// 进行中
    /// </summary>
    [Description("进行中")]
    Running = 10,

    /// <summary>
    /// 成功
    /// </summary>
    [Description("成功")]
    Success = 20,

    /// <summary>
    /// 失败
    /// </summary>
    [Description("失败")]
    Failed = 30,

    /// <summary>
    /// 异常
    /// </summary>
    [Description("异常")]
    Exception = 40,
}
