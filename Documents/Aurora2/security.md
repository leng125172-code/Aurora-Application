# 控制与安全

Core 每次启动都将工站置为 `SafeStop`，并要求设备平面上的本机 HMI 人工确认。未确认前不能进入运行状态。软件安全停机不能替代 PLC、安全继电器、急停回路和硬件互锁。

远程控制遵循以下规则：

1. Core gRPC 只允许回环地址，远程用户不能直接连接 Core。
2. HMI Host 默认也只监听回环地址；开放远程访问必须使用局域网/VPN 和 HTTPS。
3. 远程操作先通过 HMI Host 凭证，再取得最长五分钟的独占控制租约。
4. 每条远程命令都校验操作员和租约 token。
5. 任一本机 HMI 命令都会立即撤销远程租约，本机优先。
6. 安全停机后的解除只能由本机 HMI 完成。

当前 API Key 是首个纵向切片的封闭网络保护措施。可通过
`Aurora__RemoteApiKeys__0`、`Aurora__RemoteApiKeys__1` 同时配置新旧密钥，完成无中断轮换；
旧的 `Aurora__RemoteApiKey` 仍兼容。除远程急停外，远程租约和控制接口默认按来源 IP
限制为每分钟 30 次，可用 `Aurora__RemoteRateLimitPerMinute` 调整。急停不参与限流，避免安全动作被阻断。
来自非回环地址的远程控制请求必须使用 HTTPS，否则 HMI Host 以
`HMI.REMOTE.HTTPS_REQUIRED` 拒绝；本机回环 HTTP 仅用于设备内调试。

Core 在 redb 中持久化每个工站的审计序号，服务每次启动都会以新序号记录
`CORE_SERVICE_STARTED -> SafeStop`，并在 PostgreSQL 可用时与历史最大序号对齐。
进入现场发布前仍需接入设备证书、用户身份和角色授权；共享 API Key 不作为公网认证方案。
