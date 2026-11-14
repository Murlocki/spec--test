param([string]$BaseUrl)
$ErrorActionPreference = 'Stop'
Push-Location (Split-Path $PSScriptRoot -Parent)
try {
    New-Item -ItemType Directory -Force artifacts | Out-Null
    $refs = @('System.Net.Http.dll','build\BankAccountService.dll','build\Newtonsoft.Json.dll')
    $argsCsc = @('/nologo','/debug','/checked+','/target:exe','/out:build\Tests.exe') +
        @($refs | ForEach-Object { '/reference:' + $_ }) + @('tests\Tests.cs')
    & "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe" @argsCsc
    if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed' }
    Copy-Item src\BankApi.exe.config build\Tests.exe.config
    if ($BaseUrl) {
        & .\build\Tests.exe $BaseUrl | Tee-Object artifacts\test-http.log
    } else {
        & .\build\Tests.exe | Tee-Object artifacts\test-core.log
    }
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed' }
} finally { Pop-Location }
