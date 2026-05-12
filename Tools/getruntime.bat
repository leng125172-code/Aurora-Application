@echo off
chcp 65001 >nul
title .NET 运行时检测工具
cls

echo ==============================================
echo        本机 .NET 运行时检测工具
echo ==============================================
echo.

echo 【1】.NET Framework 4.x 运行时版本
echo ----------------------------------------------
:: 从注册表读取，不依赖wmic
reg query "HKLM\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full" /s /v Release 2>nul | findstr /i Release
echo.

echo 【2】.NET Core / .NET 5~10 运行时
echo ----------------------------------------------
dotnet --list-runtimes 2>nul
if %errorlevel% neq 0 (
    echo 未检测到 .NET 运行时 或 未配置环境变量
)
echo.

echo ==============================================
echo 检测完成，按任意键退出
echo ==============================================
pause >nul