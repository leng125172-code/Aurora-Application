namespace Microsoft.AspNetCore.Builder.RequestBodyEncryption;

public class ResponseEncryptMiddleware : IMiddleware, ITransientDependency
{
    private readonly IConfiguration _configuration;
    private readonly IJsonSerializer _jsonSerializer;
    private readonly ILogger<ResponseEncryptMiddleware> _logger;

    public ResponseEncryptMiddleware(
        IConfiguration configuration,
        IJsonSerializer jsonSerializer,
        ILogger<ResponseEncryptMiddleware> logger
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

            // 拦截响应
            var originalBodyStream = context.Response.Body;

            using var responseBodyMemoryStream = new MemoryStream();
            context.Response.Body = responseBodyMemoryStream;

            await next(context);

            // 获取原始响应数据
            responseBodyMemoryStream.Seek(0, SeekOrigin.Begin);
            var responseBody = await new StreamReader(responseBodyMemoryStream).ReadToEndAsync();

            // 如果响应为空，直接返回
            if (string.IsNullOrEmpty(responseBody))
            {
                return;
            }

            // 加密响应数据
            var encryptedResponse = EncryptResponseAsync(responseBody, context);

            // 将加密后的数据写入响应体
            var responseBytes = Encoding.UTF8.GetBytes(encryptedResponse);
            context.Response.ContentLength = responseBytes.Length;

            context.Response.Body = originalBodyStream;
            await context.Response.Body.WriteAsync(responseBytes);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "接口响应参数加密中间件异常");
            throw new UserFriendlyException("接口响应参数加密中间件异常", details: e.Message);
        }
    }

    private string EncryptResponseAsync(string plainText, HttpContext context)
    {
        // 生成随机AES密钥
        var aesKey = AESHelper.GenerateKey();
        // 使用RSA公钥加密AES密钥
        var publicKey = _configuration.GetValue<string>("Encryption:PublicKey");
        var privateKey = _configuration.GetValue<string>("Encryption:PrivateKey");
        if (string.IsNullOrEmpty(publicKey))
        {
            throw new InvalidOperationException("公钥未找到");
        }

        // 使用AES加密实际数据
        var encryptedData = AESHelper.Encrypt(plainText, aesKey);

        var sign = RSAHelper.Sign(encryptedData, privateKey);
        // 构造加密响应体
        var encryptedResponse = new ResponseBodyEncrypt
        {
            Data = encryptedData,
            Key = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(aesKey)),
        };

        return _jsonSerializer.Serialize(encryptedResponse);
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
