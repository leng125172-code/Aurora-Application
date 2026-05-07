namespace Lion.AbpPro.Core;

public class EmailHelper
{
    private static readonly Random random = new Random();
    private static readonly string[] domains =
    {
        "gmail.com",
        "yahoo.com",
        "hotmail.com",
        "outlook.com",
        "qq.com",
        "168.com",
        "126.com",
        "sina.com",
    };

    /// <summary>
    /// 生成随机邮箱的方法
    /// </summary>
    /// <param name="length">邮箱前缀的长度，默认为8</param>
    /// <returns>生成的随机邮箱地址</returns>
    public static string GenerateRandomEmail(int length = 8)
    {
        var emailBuilder = new StringBuilder();
        // 生成随机的邮箱前缀
        for (var i = 0; i < length; i++)
        {
            var c = (char)random.Next(97, 123); // 生成小写字母
            emailBuilder.Append(c);
        }
        // 从预定义的域名数组中随机选择一个域名
        var domain = domains[random.Next(domains.Length)];
        // 组合邮箱地址
        var email = emailBuilder.ToString() + "@" + domain;
        return email;
    }
}
