param([string]$SpecSharpBin = 'C:\Program Files (x86)\Microsoft\Spec#\bin', [int]$Port = 8085)
$ErrorActionPreference = 'Stop'
Push-Location (Split-Path $PSScriptRoot -Parent)
$server = $null
try {
    & "$PSScriptRoot\Build.ps1" -SpecSharpBin $SpecSharpBin
    & "$PSScriptRoot\Verify.ps1" -SpecSharpBin $SpecSharpBin
    & "$PSScriptRoot\Test.ps1"
    $address = "http://localhost:$Port"
    $server = Start-Process -FilePath (Join-Path $pwd 'build\BankApi.exe') -ArgumentList $address `
        -WorkingDirectory (Join-Path $pwd 'build') -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput (Join-Path $pwd 'artifacts\server.log') `
        -RedirectStandardError (Join-Path $pwd 'artifacts\server-error.log')
    $ready = $false
    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        if ($server.HasExited) { throw 'Server exited; inspect artifacts/server-error.log' }
        try {
            $r = Invoke-WebRequest "$address/api/account/balance?accountId=1" -UseBasicParsing -TimeoutSec 1
            if ($r.StatusCode -eq 200) { $ready = $true; break }
        } catch { Start-Sleep -Milliseconds 100 }
    }
    if (!$ready) { throw 'Server startup timeout' }
    & "$PSScriptRoot\Test.ps1" -BaseUrl $address
    Write-Output 'ALL CHECKS PASSED'
} finally {
    if ($server -and !$server.HasExited) { $server.Kill(); $server.WaitForExit() }
    Pop-Location
}
