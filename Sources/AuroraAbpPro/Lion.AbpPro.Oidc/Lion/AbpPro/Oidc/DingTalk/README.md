# 钉钉OAuth2登录配置说明

## 概述
本模块提供了钉钉OAuth2登录功能，允许用户通过钉钉账号登录系统。

## 配置步骤

### 1. 钉钉开发者平台配置
1. 登录[钉钉开放平台](https://open.dingtalk.com/)
2. 创建企业内部应用或第三方应用
3. 获取以下信息：
   - AppKey (对应ClientId)
   - AppSecret (对应ClientSecret)
   - 回调地址 (RedirectUrl)

### 2. 系统配置
在系统的设置管理中配置以下参数：

#### 基础配置
- **是否启用**: 设置为true启用钉钉登录
- **应用名称**: 填写钉钉应用的名称
- **ClientId**: 填写钉钉应用的AppKey
- **ClientSecret**: 填写钉钉应用的AppSecret
- **重定向Url**: 填写回调地址，需要与钉钉平台配置一致
- **HostUrl**: https://api.dingtalk.com (默认值)
- **图标**: mdi:dingding (默认值)
- **认证地址**: OAuth2授权地址模板

### 3. 前端集成示例

#### 方式一：网页跳转方式
```javascript
// 构造钉钉授权链接
const authUrl = `https://login.dingtalk.com/oauth2/auth?client_id=${yourAppKey}&response_type=code&scope=openid&state=DingTalk&redirect_uri=${encodeURIComponent(yourRedirectUri)}`;

// 跳转到钉钉授权页面
window.location.href = authUrl;
```

#### 方式二：二维码扫描方式
```javascript
// 内嵌二维码方式
const qrCodeUrl = `https://login.dingtalk.com/oauth2/qrcode?client_id=${yourAppKey}&response_type=code&scope=openid&state=DingTalk&redirect_uri=${encodeURIComponent(yourRedirectUri)}`;

// 在页面中显示二维码
document.getElementById('qrcode-container').innerHTML = `<img src="${qrCodeUrl}" alt="钉钉登录二维码" />`;
```

### 4. 后端处理流程

#### 接收授权码
前端跳转回回调地址时会携带code参数：
```
GET /auth/oidc-login?code=AUTHORIZATION_CODE&state=DingTalk
```

#### 调用登录接口
```csharp
// 使用钉钉授权码登录
var loginInput = new LoginOidcInput
{
    Code = authorizationCode,
    State = "DingTalk"
};

var result = await accountAppService.LoginOidcAsync(loginInput);
```

## API说明

### 获取访问令牌
- **Endpoint**: `POST https://api.dingtalk.com/v1.0/oauth2/userAccessToken`
- **参数**:
  - clientId: 应用的AppKey
  - clientSecret: 应用的AppSecret
  - code: 授权码
  - grantType: authorization_code

### 获取用户信息
- **Endpoint**: `GET https://api.dingtalk.com/v1.0/contact/users/me`
- **Header**: `x-acs-dingtalk-access-token: {access_token}`

## 注意事项

1. **回调地址**: 必须在钉钉开发者平台中预先配置
2. **HTTPS**: 生产环境中建议使用HTTPS协议
3. **域名白名单**: 确保回调域名在钉钉平台的白名单中
4. **用户信息**: 钉钉可能不返回用户邮箱，系统会自动生成随机邮箱
5. **权限.scope**: 默认使用openid，如需获取组织信息可使用"openid corpid"

## 错误处理

系统会抛出相应的业务异常：
- `ErrorCode100001`: HostUrl配置为空
- `ErrorCode100002`: HostUrl格式错误
- `ErrorCode100003`: ClientId配置为空
- `ErrorCode100004`: ClientSecret配置为空
- `ErrorCode100005`: 获取access_token失败
- `ErrorCode100006`: ClientName配置为空
- `ErrorCode100007`: 获取用户信息失败
- `ErrorCode100008`: RedirectUrl配置为空

## 调试建议

1. 检查钉钉开发者平台的应用配置是否正确
2. 验证回调地址是否一致
3. 查看系统日志中的详细错误信息
4. 使用钉钉提供的调试工具进行测试