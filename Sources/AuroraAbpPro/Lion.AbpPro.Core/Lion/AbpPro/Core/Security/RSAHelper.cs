using System.Security.Cryptography;

namespace Lion.AbpPro.Core.Security;

public class RSAHelper
{
    // 生成 RSA 密钥对并保存为 PEM 格式
    public static void GenerateKeys(out string publicKeyPem, out string privateKeyPem)
    {
        using (RSA rsa = RSA.Create(2048))
        {
            publicKeyPem = ExportPublicKey(rsa);
            privateKeyPem = ExportPrivateKey(rsa);
        }
    }

    private static string ExportPublicKey(RSA rsa)
    {
        return PemEncode("PUBLIC KEY", rsa.ExportSubjectPublicKeyInfo());
    }

    private static string ExportPrivateKey(RSA rsa)
    {
        return PemEncode("PRIVATE KEY", rsa.ExportPkcs8PrivateKey());
    }

    private static string PemEncode(string header, byte[] data)
    {
        var base64 = Convert.ToBase64String(data, Base64FormattingOptions.None);
        var sb = new StringBuilder();
        sb.AppendLine($"-----BEGIN {header}-----");
        for (int i = 0; i < base64.Length; i += 64)
        {
            int length = Math.Min(64, base64.Length - i);
            sb.AppendLine(base64.Substring(i, length));
        }

        sb.AppendLine($"-----END {header}-----");
        return sb.ToString();
    }

    public static string Encrypt(string plainText, string publicKeyPem)
    {
        byte[] dataBytes = Encoding.UTF8.GetBytes(plainText);
        int keySize = 2048;
        int blockSize = keySize / 8 - 11; // PKCS1Padding 占用 11 字节

        using (RSA rsa = RSA.Create())
        {
            rsa.ImportFromPem(publicKeyPem);
            using (MemoryStream ms = new MemoryStream())
            {
                for (int i = 0; i < dataBytes.Length; i += blockSize)
                {
                    int chunkSize = Math.Min(blockSize, dataBytes.Length - i);
                    byte[] block = new byte[chunkSize];
                    Array.Copy(dataBytes, i, block, 0, chunkSize);
                    byte[] encryptedBlock = rsa.Encrypt(block, RSAEncryptionPadding.Pkcs1);
                    ms.Write(encryptedBlock, 0, encryptedBlock.Length);
                }

                return Convert.ToBase64String(ms.ToArray());
            }
        }
    }

    // 使用私钥解密 Base64 数据
    public static string Decrypt(string base64Encrypted, string privateKeyPem)
    {
        byte[] cipherBytes = Convert.FromBase64String(base64Encrypted);

        using (RSA rsa = RSA.Create())
        {
            rsa.ImportFromPem(privateKeyPem);
            byte[] decryptedBytes = rsa.Decrypt(cipherBytes, RSAEncryptionPadding.Pkcs1);
            return Encoding.UTF8.GetString(decryptedBytes);
        }
    }

    /// <summary>
    /// 使用私钥对数据进行签名
    /// </summary>
    /// <param name="data">要签名的数据</param>
    /// <param name="privateKeyPem">私钥（PEM格式）</param>
    /// <param name="hashAlgorithm">哈希算法，默认为SHA256</param>
    /// <returns>Base64编码的签名值</returns>
    public static string Sign(string data, string privateKeyPem, string hashAlgorithm = "SHA256")
    {
        if (string.IsNullOrEmpty(data))
            throw new ArgumentNullException(nameof(data));

        if (string.IsNullOrEmpty(privateKeyPem))
            throw new ArgumentNullException(nameof(privateKeyPem));

        using (RSA rsa = RSA.Create())
        {
            rsa.ImportFromPem(privateKeyPem);

            byte[] dataBytes = Encoding.UTF8.GetBytes(data);
            byte[] hash = ComputeHash(dataBytes, hashAlgorithm);

            byte[] signatureBytes = rsa.SignHash(
                hash,
                GetHashAlgorithmName(hashAlgorithm),
                RSASignaturePadding.Pkcs1
            );

            return Convert.ToBase64String(signatureBytes);
        }
    }

    /// <summary>
    /// 验证签名
    /// </summary>
    /// <param name="data">原始数据</param>
    /// <param name="signature">Base64编码的签名值</param>
    /// <param name="publicKeyPem">公钥（PEM格式）</param>
    /// <param name="hashAlgorithm">哈希算法</param>
    /// <returns>验证结果</returns>
    public static bool Verify(
        string data,
        string signature,
        string publicKeyPem,
        string hashAlgorithm = "SHA256"
    )
    {
        if (string.IsNullOrEmpty(data))
            throw new ArgumentNullException(nameof(data));

        if (string.IsNullOrEmpty(signature))
            throw new ArgumentNullException(nameof(signature));

        if (string.IsNullOrEmpty(publicKeyPem))
            throw new ArgumentNullException(nameof(publicKeyPem));

        try
        {
            byte[] dataBytes = Encoding.UTF8.GetBytes(data);
            byte[] hash = ComputeHash(dataBytes, hashAlgorithm);

            byte[] signatureBytes = Convert.FromBase64String(signature);

            using (RSA rsa = RSA.Create())
            {
                rsa.ImportFromPem(publicKeyPem);
                return rsa.VerifyHash(
                    hash,
                    signatureBytes,
                    GetHashAlgorithmName(hashAlgorithm),
                    RSASignaturePadding.Pkcs1
                );
            }
        }
        catch (Exception ex)
        {
            throw new ArgumentException("Error verifying signature", ex);
        }
    }

    private static byte[] ComputeHash(byte[] data, string hashAlgorithm)
    {
        using (HashAlgorithm algorithm = HashAlgorithm.Create(hashAlgorithm))
        {
            if (algorithm == null)
                throw new ArgumentException($"Unsupported hash algorithm: {hashAlgorithm}");

            return algorithm.ComputeHash(data);
        }
    }

    private static HashAlgorithmName GetHashAlgorithmName(string algorithm)
    {
        switch (algorithm.ToUpperInvariant())
        {
            case "SHA1":
                return HashAlgorithmName.SHA1;
            case "SHA256":
                return HashAlgorithmName.SHA256;
            case "SHA384":
                return HashAlgorithmName.SHA384;
            case "SHA512":
                return HashAlgorithmName.SHA512;
            default:
                throw new ArgumentException($"Unsupported hash algorithm: {algorithm}");
        }
    }
}
