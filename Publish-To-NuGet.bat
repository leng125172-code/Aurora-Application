@echo off
set API_KEY=%NUGET_API_KEY%
set NUGET_SOURCE=https://api.nuget.org/v3/index.json

echo ==============================================
echo  开始编译 Release 版本并自动上传 NuGet.org
echo ==============================================
echo.

:: 编译所有项目
dotnet build -c Release

echo.
echo ==============================================
echo  开始上传所有 nupkg 包...
echo ==============================================
echo.

:: 递归上传所有生成的 NuGet 包
for /r %%f in (*.nupkg) do (
    echo 正在上传: %%~nf.nupkg
    dotnet nuget push "%%f" --api-key %API_KEY% --source %NUGET_SOURCE% --skip-duplicate
)

echo.
echo ✅ 全部上传完成！
pause
