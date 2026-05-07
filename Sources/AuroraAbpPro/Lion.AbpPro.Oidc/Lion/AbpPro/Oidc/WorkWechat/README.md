# 企业微信登录集成

## 概述

企业微信登录提供商实现了ABP框架的外部登录接口，支持通过企业微信OAuth2进行用户身份验证。

## 功能特性

- ✅ 获取企业微信access_token
- ✅ 通过code获取用户基本信息
- ✅ 通过user_ticket获取用户详细信息
- ✅ 完整的错误处理和日志记录
- ✅ 支持配置化的企业微信参数

## API文档参考

- [企业微信OAuth2接入](https://developer.work.weixin.qq.com/document/path/91335)
- [获取access_token](https://developer.work.weixin.qq.com/document/path/91039)
- [获取用户信息](https://developer.work.weixin.qq.com/document/path/91335)

## 核心组件

### WorkWechatExternalLoginProvider

主要的登录提供者实现类，实现了`IExternalLoginProvider`接口。

#### 主要方法

```csharp
// 获取访问令牌
public virtual async Task<GetAccessTokenResult> GetAccessTokenAsync(string code)

// 获取用户信息
public virtual async Task<GetUserInfoResult> GetUserInfoAsync(string accessToken)

// 获取用户基本信息
public virtual async Task<GetWorkWechatUserInfoResponse> GetWorkWechatUserInfoAsync(string accessToken, string code)

// 获取用户详细信息
public virtual async Task<GetUserInfoResult> GetWorkWechatUserDetailAsync(string accessToken, string userTicket)
```

### 响应模型

#### GetWorkWechatAccessTokenResponse
```csharp
public class GetWorkWechatAccessTokenResponse
{
    public int errcode { get; set; }
    public string errmsg { get; set; }
    public string access_token { get; set; }
    public int expires_in { get; set; }
}
```

#### GetWorkWechatUserInfoResponse
```csharp
public class GetWorkWechatUserInfoResponse
{
    public int errcode { get; set; }
    public string errmsg { get; set; }
    public string userid { get; set; }
    public string user_ticket { get; set; }
    public int expires_in { get; set; }
}
```

#### GetWorkWechatUserDetailResponse
```csharp
public class GetWorkWechatUserDetailResponse
{
    public int errcode { get; set; }
    public string errmsg { get; set; }
    public string userid { get; set; }
    public string name { get; set; }
    public string mobile { get; set; }
    public string email { get; set; }
    public string biz_mail { get; set; }
    public string avatar { get; set; }
    public string qr_code { get; set; }
    public string address { get; set; }
    public string gender { get; set; }
}
```

## 配置说明

### 必需配置项

在`appsettings.json`中添加以下配置：

```json
{
  "Settings": {
    "AbpPro.Oidc.WorkWechat.HostUrl": "https://qyapi.weixin.qq.com",
    "AbpPro.Oidc.WorkWechat.ClientId": "your_corp_id",
    "AbpPro.Oidc.WorkWechat.ClientSecret": "your_corp_secret",
    "AbpPro.Oidc.WorkWechat.ClientName": "your_app_name",
    "AbpPro.Oidc.WorkWechat.RedirectUrl": "https://your-domain.com/callback",
    "AbpPro.Oidc.WorkWechat.AgentId": "your_agent_id"
  }
}
```

### 配置项说明

| 配置项 | 说明 | 示例 |
|--------|------|------|
| HostUrl | 企业微信API基础地址 | https://qyapi.weixin.qq.com |
| ClientId | 企业ID(corpid) | wx1234567890abcdef |
| ClientSecret | 应用Secret(corpsecret) | secret_key |
| ClientName | 应用名称 | 企业微信登录 |
| RedirectUrl | 回调地址 | https://domain.com/callback |
| AgentId | 应用AgentId | 1000001 |

## 使用示例

### 1. 注册服务

```csharp
// 在模块中注册
public override void ConfigureServices(ServiceConfigurationContext context)
{
    context.Services.AddTransient<IExternalLoginProvider, WorkWechatExternalLoginProvider>();
}
```

### 2. 获取访问令牌

```csharp
var provider = serviceProvider.GetRequiredService<IExternalLoginProvider>();
var accessTokenResult = await provider.GetAccessTokenAsync("auth_code");
var accessToken = accessTokenResult.AccessToken;
```

### 3. 获取用户信息

```csharp
var userInfo = await provider.GetUserInfoAsync(accessToken);
Console.WriteLine($"用户ID: {userInfo.UserId}");
Console.WriteLine($"用户名: {userInfo.UserName}");
Console.WriteLine($"手机号: {userInfo.Mobile}");
```

## 错误处理

### 错误码定义

```csharp
public static class AbpProOidcErrorCodes
{
    // 通用错误
    public const string ErrorCode100001 = Consts.NameSpace + ":100001"; // HostUrl未配置
    public const string ErrorCode100002 = Consts.NameSpace + ":100002"; // HostUrl格式错误
    public const string ErrorCode100003 = Consts.NameSpace + ":100003"; // ClientId未配置
    public const string ErrorCode100004 = Consts.NameSpace + ":100004"; // ClientSecret未配置
    public const string ErrorCode100006 = Consts.NameSpace + ":100006"; // ClientName未配置
    public const string ErrorCode100008 = Consts.NameSpace + ":100008"; // RedirectUrl未配置
    
    // 企业微信专用错误
    public const string WorkWechatErrorCode100016 = Consts.NameSpace + ":100016"; // 获取access_token失败
    public const string WorkWechatErrorCode100017 = Consts.NameSpace + ":100017"; // 获取用户信息失败
    public const string WorkWechatErrorCode100018 = Consts.NameSpace + ":100018"; // userTicket为空
}
```

### 异常处理示例

```csharp
try
{
    var userInfo = await provider.GetUserInfoAsync(accessToken);
}
catch (OidcException ex) when (ex.Code == AbpProOidcErrorCodes.WorkWechatErrorCode100017)
{
    _logger.LogError("获取企业微信用户信息失败: {Message}", ex.Message);
    // 处理用户信息获取失败
}
catch (OidcException ex) when (ex.Code == AbpProOidcErrorCodes.WorkWechatErrorCode100016)
{
    _logger.LogError("获取企业微信access_token失败: {Message}", ex.Message);
    // 处理access_token获取失败
}
```

## 日志记录

组件内置了详细的日志记录功能：

- 记录API调用结果
- 记录错误码和错误信息
- 记录HTTP状态码
- 记录完整的错误堆栈

## 注意事项

1. **安全性**：确保ClientSecret等敏感信息妥善保管，不要硬编码在代码中
2. **回调地址**：RedirectUrl必须在企业微信后台正确配置
3. **网络环境**：确保服务器能够访问企业微信API地址
4. **时间同步**：企业微信对时间戳敏感，确保服务器时间准确
5. **频率限制**：注意企业微信API调用频率限制

## 版本兼容性

- ABP Framework v7.0+
- .NET 6.0+
- 企业微信API v3

## 贡献指南

欢迎提交Issue和Pull Request来改进这个组件。