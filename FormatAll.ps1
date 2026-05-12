Write-Host "正在使用 CSharpier 格式化所有 C# 代码..." -ForegroundColor Green

csharpier format .

Write-Host "✅ C# 格式化完成！" -ForegroundColor Green

Write-Host "正在使用 Prettier 格式化前端代码（*.vue, *.ts, *.html, *.js）..." -ForegroundColor Green

$frontendPath = Join-Path $PSScriptRoot "Sources\AuroraStruct3D\AuroraStruct3D.Frontend"
Push-Location $frontendPath
try {
    npx prettier --write "src/**/*.{vue,ts,js,html}" --log-level warn
    Write-Host "✅ 前端代码格式化完成！" -ForegroundColor Green
}
finally {
    Pop-Location
}