# GitHub Copilot Instructions for Aurora Framework
# IMPORTANT: Always communicate and generate code in **Chinese**.
# 重要提示：必须全程使用 **中文** 进行交互、生成代码、写注释、解释逻辑。

## Project Overview
This is an ABP-based modular development framework called Aurora.
It is used for rapid application development, module management, solution generation, and embedded resource extraction.

## 核心要求（必须遵守）
- 所有代码注释必须使用 **中文**
- 所有解释、说明、对话必须使用 **中文**
- 生成的代码、方法名、变量名保持英文，但注释必须中文
- 回答问题必须用中文，禁止使用英文回复

## Coding Rules
- 使用 **C#** 语言
- 遵循 **Microsoft C# 编码规范**
- 公共方法必须使用 **XML 中文注释**
- 使用有意义的英文名称
- 异步方法必须以 **Async 结尾**
- 类、方法、属性使用 PascalCase
- 私有字段使用 camelCase
- 私有只读字段以下划线开头：`_logger`, `_service`

## Architecture Rules
- 分层架构：Application、Domain、Shared、Web
- DDD 领域驱动设计
- 依赖注入
- ABP 框架规范
- 整洁的目录结构

## Project Structure
- Sources/
- Tools/
- Assets/
- Documents/

## Naming Conventions
- 解决方案：Aurora Application.slnx
- 项目文件：{Module}.{Layer}.csproj
- 接口以 **I** 开头
- 异步方法：**DoSomethingAsync()**

## Do NOT use
- 公共 API 中使用 var
- 无意义名称（a, b, c）
- 硬编码字符串
- 未使用的变量

## When generating code
- 代码保持整洁
- 优先使用异步
- 兼容 ABP 框架
- 符合 Aurora 风格


// OpenCV,分支拉去到本地
// OCR 文字识别分支拉去到本地
// OpenCV OCR，本地模型OCR图片识别
