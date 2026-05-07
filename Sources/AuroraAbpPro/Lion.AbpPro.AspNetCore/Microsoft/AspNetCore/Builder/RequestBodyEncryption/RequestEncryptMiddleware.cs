namespace Microsoft.AspNetCore.Builder.RequestBodyEncryption;

public class RequestEncryptMiddleware : IMiddleware, ITransientDependency
{
    private readonly IConfiguration _configuration;
    private readonly IJsonSerializer _jsonSerializer;
    private readonly ILogger<RequestEncryptMiddleware> _logger;

    public RequestEncryptMiddleware(
        IConfiguration configuration,
        IJsonSerializer jsonSerializer,
        ILogger<RequestEncryptMiddleware> logger
    )
    {
        _configuration = configuration;
        _jsonSerializer = jsonSerializer;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        try
        {
            // 判断是否需要跳过加密
            if (ShouldSkipEncryption(context))
            {
                await next(context);
                return;
            }

            await DecryptRequestBodyAsync(context);
            await next(context);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "接口请求参数加密中间件异常");
            throw new UserFriendlyException("接口请求参数加密中间件异常", details: e.Message);
        }
    }

    public async Task DecryptRequestBodyAsync(HttpContext context)
    {
        if (context.Request.ContentLength == null || context.Request.ContentLength == 0)
        {
            return; // Skip empty requests
        }

        // 1. 读取原始请求体
        var buffer = new byte[Convert.ToInt32(context.Request.ContentLength)];
        await context.Request.Body.ReadExactlyAsync(buffer, 0, buffer.Length);
        var encryptRequestBody = Encoding.UTF8.GetString(buffer);
        // 2. 反序列化加密数据
        var body = _jsonSerializer.Deserialize<RequestBodyEncrypt>(encryptRequestBody);

        // 3 验证加密密钥
        if (body.Key.IsNullOrWhiteSpace())
        {
            throw new UserFriendlyException("未找到加密Key");
        }

        // 4. 获取并验证私钥
        var privateKey = _configuration.GetValue<string>("Encryption:PrivateKey");
        if (privateKey.IsNullOrWhiteSpace())
        {
            throw new UserFriendlyException("未找到私钥");
        }

        // 5. 解密AES密钥
        var aesKey = RSAHelper.Decrypt(body.Key, privateKey);

        // 6. 解密请求体
        var decryptRequestBody = AESHelper.Decrypt(body.Data, aesKey);

        // 7. 替换请求体
        ReplaceRequestBody(context, decryptRequestBody);
    }

    private void ReplaceRequestBody(HttpContext context, string plainText)
    {
        var newBodyBytes = Encoding.UTF8.GetBytes(plainText);
        var newBodyStream = new MemoryStream(newBodyBytes);
        context.Request.Body = newBodyStream;
        context.Request.ContentLength = newBodyBytes.Length;
    }

    /// <summary>
    /// 判断当前请求是否应跳过加密处理
    /// </summary>
    private bool ShouldSkipEncryption(HttpContext context)
    {
        if (!_configuration.GetValue<bool>("Encryption:Enabled"))
        {
            return true;
        }

        // 如果不是post 请求直接跳过
        if (context.Request.Method.ToUpper() != "POST")
        {
            return true;
        }

        return false;
    }
}
