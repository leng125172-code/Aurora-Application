[CmdletBinding()]
param(
    [string]$SwaggerUrl = "http://localhost:44315/swagger/AbpPro/swagger.json",
    [string]$BaseUrl = "http://localhost:44315",
    [string]$LoginPath = "/api/app/account/login",
    [string]$UserName = "admin",
    [Parameter(Mandatory = $true)]
    [string]$Password,
    [string]$Language = "zh-Hans",
    [string]$Phases = "examples,fuzzing",
    [int]$MaxExamples = 1,
    [int]$Workers = 1,
    [int]$RequestTimeoutSeconds = 20,
    [string]$Checks = "not_a_server_error,status_code_conformance,response_schema_conformance",
    [string]$ReportRoot = "",
    [switch]$SkipInstall
)

<#
.SYNOPSIS
批量测试 Swagger/OpenAPI 接口并输出测试报告。

.DESCRIPTION
脚本会先使用账号密码登录目标系统，获取 JWT Token 后调用 Schemathesis
对 Swagger 文档中的接口进行批量测试，并输出 JUnit 与 NDJSON 报告。
同时会临时清理本机代理环境变量，避免 localhost 请求被代理工具拦截。

.EXAMPLE
.\Test-SwaggerApi.ps1 -Password "1q2w3E*"

.EXAMPLE
.\Test-SwaggerApi.ps1 `
    -SwaggerUrl "http://localhost:44315/swagger/AbpPro/swagger.json" `
    -BaseUrl "http://localhost:44315" `
    -UserName "admin" `
    -Password "1q2w3E*" `
    -Workers 1 `
    -MaxExamples 1
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Get-DefaultReportRoot {
    return Join-Path $PSScriptRoot "swagger-test-reports"
}

function Ensure-Python {
    $pythonCommand = Get-Command python -ErrorAction SilentlyContinue
    if ($null -eq $pythonCommand) {
        throw "未找到 python 命令，请先安装 Python 3。"
    }

    return $pythonCommand.Source
}

function Ensure-Schemathesis {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PythonPath,

        [Parameter(Mandatory = $true)]
        [bool]$AllowInstall
    )

    & $PythonPath -m pip show schemathesis *> $null
    if ($LASTEXITCODE -eq 0) {
        return
    }

    if (-not $AllowInstall) {
        throw "未安装 schemathesis，请先执行 python -m pip install schemathesis。"
    }

    Write-Host "未检测到 schemathesis，正在自动安装..."
    & $PythonPath -m pip install schemathesis --quiet

    if ($LASTEXITCODE -ne 0) {
        throw "schemathesis 安装失败。"
    }
}

function Invoke-LocalLogin {
    param(
        [Parameter(Mandatory = $true)]
        [string]$LoginUrl,

        [Parameter(Mandatory = $true)]
        [string]$Account,

        [Parameter(Mandatory = $true)]
        [string]$AccountPassword
    )

    $payload = @{
        name = $Account
        password = $AccountPassword
    } | ConvertTo-Json -Compress

    return Invoke-RestMethod -Uri $LoginUrl -Method Post -ContentType "application/json" -Body $payload
}

function New-ReportDirectory {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RootPath
    )

    $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $directory = Join-Path $RootPath $timestamp
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
    return $directory
}

function Write-ReportSummary {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ReportDirectory
    )

    $junitFile = Get-ChildItem -Path $ReportDirectory -Filter "junit-*.xml" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($null -eq $junitFile) {
        Write-Warning "未找到 JUnit 报告，无法输出汇总信息。"
        return
    }

    [xml]$junit = Get-Content -Path $junitFile.FullName
    $testSuite = $junit.testsuites.testsuite
    $total = [int]$testSuite.tests
    $failed = [int]$testSuite.failures
    $passed = $total - $failed

    Write-Host ""
    Write-Host "测试完成："
    Write-Host "  总接口数：$total"
    Write-Host "  通过：$passed"
    Write-Host "  失败：$failed"
    Write-Host "  报告目录：$ReportDirectory"
}

if ([string]::IsNullOrWhiteSpace($ReportRoot)) {
    $ReportRoot = Get-DefaultReportRoot
}

$pythonPath = Ensure-Python
Ensure-Schemathesis -PythonPath $pythonPath -AllowInstall (-not $SkipInstall.IsPresent)

$loginUrl = [Uri]::new([Uri]$BaseUrl, $LoginPath).AbsoluteUri
$reportDirectory = New-ReportDirectory -RootPath $ReportRoot

$originalProxy = @{
    HTTP_PROXY = $env:HTTP_PROXY
    HTTPS_PROXY = $env:HTTPS_PROXY
    ALL_PROXY = $env:ALL_PROXY
    NO_PROXY = $env:NO_PROXY
}

try {
    # 本地接口测试时强制绕过代理，避免 localhost 被代理软件拦截成 502。
    $env:HTTP_PROXY = $null
    $env:HTTPS_PROXY = $null
    $env:ALL_PROXY = $null
    $env:NO_PROXY = "localhost,127.0.0.1,::1"

    Write-Host "正在登录：$loginUrl"
    $loginResult = Invoke-LocalLogin -LoginUrl $loginUrl -Account $UserName -AccountPassword $Password
    if ([string]::IsNullOrWhiteSpace($loginResult.token)) {
        throw "登录成功，但响应中未包含 token。"
    }

    Write-Host "正在测试 Swagger：$SwaggerUrl"

    $arguments = @(
        "-m", "schemathesis",
        "run",
        $SwaggerUrl,
        "-H", "Authorization: Bearer $($loginResult.token)",
        "-H", "Accept-Language: $Language",
        "--phases", $Phases,
        "--checks", $Checks,
        "--mode", "positive",
        "--max-examples", $MaxExamples,
        "--workers", $Workers,
        "--request-timeout", $RequestTimeoutSeconds,
        "--continue-on-failure",
        "--report", "junit,ndjson",
        "--report-dir", $reportDirectory,
        "--generation-database", ":memory:",
        "--no-color"
    )

    & $pythonPath @arguments
    $exitCode = $LASTEXITCODE

    Write-ReportSummary -ReportDirectory $reportDirectory
    exit $exitCode
}
finally {
    $env:HTTP_PROXY = $originalProxy.HTTP_PROXY
    $env:HTTPS_PROXY = $originalProxy.HTTPS_PROXY
    $env:ALL_PROXY = $originalProxy.ALL_PROXY
    $env:NO_PROXY = $originalProxy.NO_PROXY
}
