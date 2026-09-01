# 本地运行

依赖：Rust 1.98、.NET SDK 10、Node.js 20 以上。Rust 构建使用 vendored `protoc`，开发机不必单独安装 Protocol Buffers 编译器。

```powershell
cargo test --workspace
dotnet build Sources/Aurora2/Aurora.DeviceVision.Host/Aurora.DeviceVision.Host.csproj
dotnet build Sources/Aurora2/Aurora.Hmi.Host/Aurora.Hmi.Host.csproj
```

依次启动 Device/Vision Host、Core、HMI Host：

```powershell
dotnet run --project Sources/Aurora2/Aurora.DeviceVision.Host/Aurora.DeviceVision.Host.csproj
cargo run -p aurora-core-service
dotnet run --project Sources/Aurora2/Aurora.Hmi.Host/Aurora.Hmi.Host.csproj
```

浏览器打开 `http://127.0.0.1:5080`。Core gRPC 默认是 `127.0.0.1:50051`，不允许配置为外网地址。
Device/Vision Host 默认是 `127.0.0.1:50052`，同样强制仅监听回环地址。

远程调试默认关闭。需要从局域网或 VPN 访问时，同时配置 HMI Host 监听地址、HTTPS 证书、允许来源和 `Aurora__RemoteApiKey`。生产环境应由设备身份/用户认证替换共享 API Key。

PostgreSQL 是可选投影：设置 `AURORA_POSTGRES_URL` 后，后台任务每 5 秒冲刷 redb outbox；
未设置、暂时不可达或连接中断时，控制命令只等待本地持久化，不等待网络数据库。
连接恢复后投影任务自动重连并按事件 ID 幂等补传。
