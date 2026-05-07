# Aurora.RK3588NPU

这个项目用于保存 RK3588 板端 AI 服务相关的 Python 脚本，方便在 Visual Studio 的 Python 项目里统一管理、查看和后续集成。

## 当前内容

| 文件 | 说明 |
| --- | --- |
| `Aurora.RK3588NPU.py` | 项目入口示例，输出当前 RK3588 连接目标和接口地址 |
| `rk3588_env.py` | 环境配置，集中定义主板地址、模型名、服务端口、接口路径等 |
| `rkllama_server.py` | 从板子复制回来的 RKLLama 服务端脚本，包含 Dashboard 相关逻辑 |
| `rkllama_server_utils.py` | 从板子复制回来的 RKLLama 工具脚本，包含已修正的问题代码 |

## 对应板端环境

- 设备：RK3588
- 用户：`linaro`
- 主机：`10.127.135.143`
- RKLLama 根目录：`/home/linaro/rkllama-src`
- 模型文件：`/home/linaro/Llama-3.2-3B-Instruct_w8a8_g128_rk3588.rkllm`
- 已注册模型：`llama3.2:3b`
- 服务地址：`http://10.127.135.143:8080`
- Dashboard：`http://10.127.135.143:8080/dashboard`
- 状态接口：`http://10.127.135.143:8080/api/dashboard/status`
- 推理接口：`http://10.127.135.143:8080/api/generate`

## 环境变量

`rk3588_env.py` 提供了默认值，也支持通过环境变量覆盖：

- `AURORA_RK3588_HOST`
- `AURORA_RK3588_USER`
- `AURORA_RK3588_PORT`
- `AURORA_RK3588_MODEL`
- `AURORA_RK3588_RKLLAMA_ROOT`
- `AURORA_RK3588_MODEL_FILE`
- `AURORA_RK3588_MODELS_DIR`
- `AURORA_RK3588_DASHBOARD_PATH`
- `AURORA_RK3588_STATUS_API_PATH`
- `AURORA_RK3588_GENERATE_API_PATH`
- `AURORA_RK3588_PASSWORD`

说明：当前密码没有写入代码，建议仅通过环境变量或外部安全配置注入。

## 使用方式

### 1. 查看当前配置

运行项目入口：

```python
python Aurora.RK3588NPU.py
```

会输出当前 SSH 目标、模型名、Dashboard 地址和推理接口地址。

### 2. 在代码中读取配置

```python
from rk3588_env import RK3588_ENV

print(RK3588_ENV.ssh_target)
print(RK3588_ENV.dashboard_url)
print(RK3588_ENV.generate_url)
```

### 3. 对接板端服务

板端 RKLLama 服务已经部署完成，并带有 Linux Dashboard，可用于：

- 查看模型列表
- 查看设备状态
- 查看 NPU 状态
- 测试模型调用
- 查看请求/响应 JSON 和执行耗时

## 备注

- `rkllama_server.py` 和 `rkllama_server_utils.py` 是从板端回收的实际脚本快照，便于本地留档和后续移植。
- 如果板端脚本再次修改，建议重新同步覆盖本项目中的对应文件。
