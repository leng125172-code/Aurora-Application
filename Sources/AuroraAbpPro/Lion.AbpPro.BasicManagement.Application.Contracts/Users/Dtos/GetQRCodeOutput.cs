namespace Lion.AbpPro.BasicManagement.Users.Dtos;

public class GetQRCodeOutput
{
    /// <summary>
    /// base64 二维码
    /// </summary>
    public byte[] QRCode { get; set; }

    /// <summary>
    /// 密钥
    /// </summary>
    public string Secret { get; set; }
}
