<#
.SYNOPSIS
  Builds, tests and publishes the Windows receiver into artifacts\WatchPointer-win-x64.
.PARAMETER SelfContained
  Include the .NET runtime (no runtime install needed on the target PC; ~150 MB).
.PARAMETER SkipTests
  Skip the unit tests.
#>
param(
    [switch]$SelfContained,
    [switch]$SkipTests,
    [string]$Runtime = "win-x64"
)
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "The .NET 10 SDK is required: https://dotnet.microsoft.com/download"
}

dotnet build WatchPointer.slnx -c Release
if ($LASTEXITCODE -ne 0) { throw "Build failed" }

if (-not $SkipTests) {
    dotnet test Tests/WatchPointer.Core.Tests -c Release --no-build
    if ($LASTEXITCODE -ne 0) { throw "Tests failed" }
}

$out = Join-Path "artifacts" "WatchPointer-$Runtime"
if (Test-Path $out) { Remove-Item -Recurse -Force $out }
$sc = if ($SelfContained) { "true" } else { "false" }
dotnet publish WindowsApp/WatchPointer.App -c Release -r $Runtime --self-contained $sc -o $out
if ($LASTEXITCODE -ne 0) { throw "Publishing the app failed" }
dotnet publish WindowsApp/WatchPointer.Tool -c Release -r $Runtime --self-contained $sc -o $out
if ($LASTEXITCODE -ne 0) { throw "Publishing the tool failed" }

Write-Host ""
Write-Host "Done: $out\WatchPointer.exe  (CLI: $out\watchpointer-cli.exe ble-check)"
