# 本地运行

依赖：Rust 1.98、.NET SDK 10、Node.js 20 以上。Rust 构建使用 vendored `protoc`，开发机不必单独安装 Protocol Buffers 编译器。

```powershell
cargo test --workspace
dotnet build Sources/Aurora2/Aurora.Hmi.Host/Aurora.Hmi.Host.csproj
```

先启动 Core，再启动 HMI Host：

```powershell
cargo run -p aurora-core-service
dotnet run --project Sources/Aurora2/Aurora.Hmi.Host/Aurora.Hmi.Host.csproj
```

浏览器打开 `http://127.0.0.1:5080`。Core gRPC 默认是 `127.0.0.1:50051`，不允许配置为外网地址。

远程调试默认关闭。需要从局域网或 VPN 访问时，同时配置 HMI Host 监听地址、HTTPS 证书、允许来源和 `Aurora__RemoteApiKey`。生产环境应由设备身份/用户认证替换共享 API Key。

PostgreSQL 是可选投影：设置 `AURORA_POSTGRES_URL` 后，启动时会创建事件表并冲刷 redb outbox；未设置或暂时不可达时，现场控制仍使用本地持久化继续运行。
