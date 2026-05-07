using System.Security.Cryptography;

namespace Lion.AbpPro.Core.Security
{
    public class AESHelper
    {
        // 默认密钥长度（16字节 = 128位）
        private const int KeySize = 128;

        /// <summary>
        /// 使用AES算法加密文本（ECB模式，PKCS5Padding填充）
        /// </summary>
        /// <param name="plainText">待加密的明文</param>
        /// <param name="key">加密密钥（16字节）</param>
        /// <returns>Base64编码的密文</returns>
        public static string Encrypt(string plainText, string key)
        {
            if (string.IsNullOrEmpty(plainText))
                throw new ArgumentNullException(nameof(plainText));

            if (string.IsNullOrEmpty(key))
                throw new ArgumentNullException(nameof(key));

            // 验证密钥长度
            if (Encoding.UTF8.GetByteCount(key) != KeySize / 8)
                throw new ArgumentException($"密钥长度必须为{KeySize / 8}字节", nameof(key));

            using var aesAlg = Aes.Create();
            aesAlg.Mode = CipherMode.ECB;
            aesAlg.Padding = PaddingMode.PKCS7;
            aesAlg.Key = Encoding.UTF8.GetBytes(key);

            var encrypter = aesAlg.CreateEncryptor();

            using var msEncrypt = new MemoryStream();
            using var csEncrypt = new CryptoStream(msEncrypt, encrypter, CryptoStreamMode.Write);
            using var swEncrypt = new StreamWriter(csEncrypt);
            swEncrypt.Write(plainText);
            swEncrypt.Flush(); // 确保写入完成
            csEncrypt.FlushFinalBlock(); // 完成最终块加密
            var s = msEncrypt.ToArray();
            return Convert.ToBase64String(s);
        }

        /// <summary>
        /// 使用AES算法解密文本（ECB模式，PKCS5Padding填充）
        /// </summary>
        /// <param name="cipherText">Base64编码的密文</param>
        /// <param name="key">解密密钥（16字节）</param>
        /// <returns>解密后的明文</returns>
        public static string Decrypt(string cipherText, string key)
        {
            if (string.IsNullOrEmpty(cipherText))
                throw new ArgumentNullException(nameof(cipherText));

            if (string.IsNullOrEmpty(key))
                throw new ArgumentNullException(nameof(key));

            // 验证密钥长度
            if (Encoding.UTF8.GetByteCount(key) != KeySize / 8)
                throw new ArgumentException($"密钥长度必须为{KeySize / 8}字节", nameof(key));

            // 清理Base64字符串
            var cleanText = cipherText.Replace(" ", "+").Replace('-', '+').Replace('_', '/');

            // 验证是否为有效Base64
            if (!IsBase64String(cleanText))
            {
                throw new ArgumentException("输入不是有效的Base64编码", nameof(cipherText));
            }

            var cipherBytes = Convert.FromBase64String(cleanText);

            using Aes aesAlg = Aes.Create();
            aesAlg.Mode = CipherMode.ECB;
            aesAlg.Padding = PaddingMode.PKCS7;
            aesAlg.Key = Encoding.UTF8.GetBytes(key);

            var decrypter = aesAlg.CreateDecryptor();

            using var msDecrypt = new MemoryStream(cipherBytes);
            using var csDecrypt = new CryptoStream(msDecrypt, decrypter, CryptoStreamMode.Read);
            using var srDecrypt = new StreamReader(csDecrypt);
            return srDecrypt.ReadToEnd();
        }

        private static bool IsBase64String(string input)
        {
            input = input.Trim();
            return (input.Length % 4 == 0) && Regex.IsMatch(input, @"^[a-zA-Z0-9\+/]*={0,2}$");
        }

        /// <summary>
        /// 生成随机密钥
        /// </summary>
        /// <param name="length">密钥长度（字节），默认16字节</param>
        /// <returns>随机密钥字符串</returns>
        public static string GenerateKey(int length = 16)
        {
            const string charset = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
            var key = new System.Text.StringBuilder();

            var random = new Random();
            for (int i = 0; i < length; i++)
            {
                int randomIndex = random.Next(charset.Length);
                key.Append(charset[randomIndex]);
            }

            return key.ToString();
        }
    }
}
