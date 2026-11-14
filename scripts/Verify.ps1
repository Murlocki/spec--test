param([string]$SpecSharpBin = 'C:\Program Files (x86)\Microsoft\Spec#\bin')
$ErrorActionPreference = 'Stop'
Push-Location (Split-Path $PSScriptRoot -Parent)
try {
    New-Item -ItemType Directory -Force artifacts | Out-Null
    if (!(Test-Path build\BankAccountService.dll)) { throw 'Run scripts/Build.ps1 first' }
    $good = & "$SpecSharpBin/SscBoogie.exe" /timeLimit:30 build\BankAccountService.dll 2>&1
    $good | Tee-Object artifacts/verify-correct.log
    if (($good -join "`n") -notmatch '10 verified, 0 errors' -or
        ($good -join "`n") -match 'timed out|inconclusive') { throw 'Positive verification failed' }
    & "$SpecSharpBin/ssc.exe" /t:library /debug /nn /checked+ /out:build\WithdrawBroken.dll verification\WithdrawBroken.ssc
    if ($LASTEXITCODE -ne 0) { throw 'Negative sample did not compile' }
    $bad = & "$SpecSharpBin/SscBoogie.exe" /timeLimit:30 build\WithdrawBroken.dll 2>&1
    $bad | Tee-Object artifacts/verify-broken.log
    if (($bad -join "`n") -notmatch 'postcondition' -or ($bad -join "`n") -notmatch '1 error') {
        throw 'Verifier did not detect the intended defect'
    }
    Write-Output 'VERIFICATION PASSED: correct code accepted; intentional defect rejected'
} finally { Pop-Location }
