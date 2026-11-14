param([string]$SpecSharpBin = 'C:\Program Files (x86)\Microsoft\Spec#\bin')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    New-Item -ItemType Directory -Force build,artifacts | Out-Null
    foreach ($tool in @('ssc.exe','SscBoogie.exe','Z3.exe','Microsoft.SpecSharp.Runtime.dll','System.Compiler.Runtime.dll')) {
        if (!(Test-Path (Join-Path $SpecSharpBin $tool))) { throw "Missing Spec# tool: $tool. Set -SpecSharpBin to your Spec# bin directory." }
    }
    & "$PSScriptRoot\Restore.ps1"
    & "$SpecSharpBin/ssc.exe" /t:library /debug /nn /checked+ /out:build\BankAccountService.dll BankAccountService.ssc 2>&1 |
        Tee-Object artifacts/compile-specsharp.log
    if ($LASTEXITCODE -ne 0) { throw 'Spec# compilation failed' }
    Copy-Item "$SpecSharpBin/Microsoft.SpecSharp.Runtime.dll","$SpecSharpBin/System.Compiler.Runtime.dll" build
    $dependencies = @(
        'packages\Microsoft.AspNet.WebApi.Core.5.2.9\lib\net45/System.Web.Http.dll',
        'packages\Microsoft.AspNet.WebApi.Client.5.2.9\lib\net45/System.Net.Http.Formatting.dll',
        'packages\Microsoft.AspNet.WebApi.SelfHost.5.2.9\lib\net45/System.Web.Http.SelfHost.dll',
        'packages\Newtonsoft.Json.13.0.3\lib\net45/Newtonsoft.Json.dll'
    )
    $dependencies | ForEach-Object { Copy-Item $_ build }
    $refs = @('System.Net.Http.dll','System.ServiceModel.dll','System.Runtime.Serialization.dll','build\BankAccountService.dll') + $dependencies
    $compilerArgs = @('/nologo','/debug','/checked+','/target:exe','/out:build\BankApi.exe') +
        @($refs | ForEach-Object { '/reference:' + $_ }) + @('src\BankApi.cs','src\Program.cs')
    & "$env:WINDIR/Microsoft.NET/Framework/v4.0.30319/csc.exe" @compilerArgs 2>&1 |
        Tee-Object artifacts/compile-api.log
    if ($LASTEXITCODE -ne 0) { throw 'C# compilation failed' }
    Copy-Item src\BankApi.exe.config build
    Write-Output 'BUILD PASSED (.NET Framework 4.8 + Spec#)'
} finally { Pop-Location }
