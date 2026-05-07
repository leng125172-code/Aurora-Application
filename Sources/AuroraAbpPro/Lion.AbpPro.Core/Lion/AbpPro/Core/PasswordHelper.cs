namespace Lion.AbpPro.Core;

public static class PasswordHelper
{
    /// <summary>
    /// 随机生成一个包含数字，字母，符号三种字符的密码
    /// </summary>
    /// <returns></returns>
    public static string RandomPassword(string prefix = "")
    {
        var password = GeneratePassword();
        return password;
    }

    private static string GeneratePassword(string prefix = "")
    {
        var chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ1234567890!@#$%^&*()";
        var sb = new StringBuilder();
        var random = new Random();

        // 随机选择一个字符作为密码的第一个字符
        sb.Append(chars[random.Next(chars.Length)]);

        // 随机选择一个数字作为密码的第二个字符
        sb.Append(random.Next(10));

        // 随机选择一个大写字母作为密码的第三个字符
        sb.Append(chars[random.Next(26, 52)]);

        // 随机选择一个特殊符号作为密码的第四个字符
        sb.Append(chars[random.Next(52, chars.Length)]);

        // 生成剩余的字符
        for (var i = 4; i < 14; i++)
        {
            sb.Append(chars[random.Next(chars.Length)]);
        }

        return sb.ToString();
    }
}
