<#
.SYNOPSIS
    Builds the portable version of Radio: one self-contained RadioPlayer.exe (no .NET install
    needed on the target PC) plus stations.json, zipped as dist\Radio-<runtime>.zip.

.EXAMPLE
    .\build.ps1
    .\build.ps1 -Runtime win-arm64
#>
param(
    [string]$Runtime = 'win-x64'
)

$ErrorActionPreference = 'Stop'

$project = Join-Path $PSScriptRoot 'src\RadioPlayer.csproj'
$out = Join-Path $PSScriptRoot "dist\Radio-$Runtime"
$zip = "$out.zip"

if (Test-Path $out) { Remove-Item $out -Recurse -Force }
if (Test-Path $zip) { Remove-Item $zip -Force }

dotnet publish $project -c Release -r $Runtime --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none `
    -o $out --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit code $LASTEXITCODE)" }

Compress-Archive -Path (Join-Path $out '*') -DestinationPath $zip
Write-Host "Built $zip ($([math]::Round((Get-Item $zip).Length / 1MB, 1)) MB)"
