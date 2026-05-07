using System.Security.Cryptography;
using System.Text;

namespace Lion.AbpPro.Core.Security;

/// <summary>
/// 签名帮助类
/// </summary>
public static class SignatureHelper
{
    /// <summary>
    /// 生成SHA256签名
    /// </summary>
    /// <param name="parameters">签名参数字典</param>
    /// <param name="secret">密钥</param>
    /// <param name="secretKeyName">密钥参数名称，默认为"appSecret"</param>
    /// <returns>大写的SHA256签名</returns>
    public static string GenerateSha256Signature(
        IDictionary<string, string> parameters,
        string secret,
        string secretKeyName = "appSecret"
    )
    {
        if (parameters == null || parameters.Count == 0)
        {
            throw new ArgumentException("签名参数不能为空", nameof(parameters));
        }

        if (string.IsNullOrWhiteSpace(secret))
        {
            throw new ArgumentException("密钥不能为空", nameof(secret));
        }

        // 按Key字典升序排序
        var sortedParams = parameters
            .OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // 拼接签名原文
        var signBuilder = new StringBuilder();
        foreach (var kv in sortedParams)
        {
            signBuilder.Append($"{kv.Key}={kv.Value}&");
        }

        // 末尾追加密钥
        signBuilder.Append($"{secretKeyName}={secret}");
        string signSource = signBuilder.ToString();

        // SHA256加密，转大写
        using var sha256 = SHA256.Create();
        byte[] hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(signSource));
        return BitConverter.ToString(hashBytes).Replace("-", "").ToUpper();
    }

    /// <summary>
    /// 验证签名
    /// </summary>
    /// <param name="parameters">签名参数字典</param>
    /// <param name="secret">密钥</param>
    /// <param name="signature">待验证的签名</param>
    /// <param name="secretKeyName">密钥参数名称，默认为"appSecret"</param>
    /// <returns>签名是否有效</returns>
    public static bool VerifySha256Signature(
        IDictionary<string, string> parameters,
        string secret,
        string signature,
        string secretKeyName = "appSecret"
    )
    {
        if (string.IsNullOrWhiteSpace(signature))
        {
            return false;
        }

        string generatedSignature = GenerateSha256Signature(parameters, secret, secretKeyName);
        return generatedSignature.Equals(signature, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 生成开放平台SHA256签名
    /// </summary>
    /// <param name="appKey">应用Key</param>
    /// <param name="timestamp">时间戳</param>
    /// <param name="nonce">随机数</param>
    /// <param name="bodyJson">请求体JSON</param>
    /// <param name="appSecret">应用密钥</param>
    /// <returns>大写的SHA256签名</returns>
    public static string GenerateOpenPlatformSignature(
        string appKey,
        string timestamp,
        string nonce,
        string bodyJson,
        string appSecret
    )
    {
        var paramDict = new Dictionary<string, string>
        {
            ["AppKey"] = appKey,
            ["Timestamp"] = timestamp,
            ["Nonce"] = nonce,
        };

        // Body原样作为单个参数参与签名（传什么用什么，不解析、不处理）
        if (!string.IsNullOrWhiteSpace(bodyJson))
        {
            paramDict["body"] = bodyJson;
        }

        return GenerateSha256Signature(paramDict, appSecret, "appSecret");
    }
}
