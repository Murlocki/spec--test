param([string]$PackageRoot = (Join-Path $PSScriptRoot '..\packages'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$versions = @{
    'Microsoft.AspNet.WebApi.Core' = '5.2.9'
    'Microsoft.AspNet.WebApi.Client' = '5.2.9'
    'Microsoft.AspNet.WebApi.SelfHost' = '5.2.9'
    'Newtonsoft.Json' = '13.0.3'
    'Microsoft.NETFramework.ReferenceAssemblies.net48' = '1.0.3'
}
New-Item -ItemType Directory -Force $PackageRoot | Out-Null
foreach ($id in ($versions.Keys | Sort-Object)) {
    $version = $versions[$id]
    $destination = Join-Path $PackageRoot "$id.$version"
    if (Test-Path (Join-Path $destination "$id.nuspec")) { continue }
    $name = $id.ToLowerInvariant()
    $archive = Join-Path $PackageRoot "$id.$version.nupkg"
    Invoke-WebRequest "https://api.nuget.org/v3-flatcontainer/$name/$version/$name.$version.nupkg" -UseBasicParsing -OutFile $archive
    [IO.Compression.ZipFile]::ExtractToDirectory($archive, $destination)
}
Get-ChildItem $PackageRoot -Filter *.nupkg | Get-FileHash -Algorithm SHA256 |
    Select-Object @{N='Package';E={Split-Path $_.Path -Leaf}},Hash |
    ConvertTo-Json | Set-Content -Encoding UTF8 (Join-Path $PackageRoot 'sha256.json')
