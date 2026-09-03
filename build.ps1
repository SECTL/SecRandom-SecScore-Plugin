# Build the SecScoreLauncher SecRandom plugin and produce the .srpx package.
#
# Why the DOTNET_* env vars / CscTool* overrides:
#   This machine's Windows 10 build (ntdll < 10.0.19041.5007) cannot run the .NET SDK's native
#   compiler apphost (csc.exe) because CET shadow stacks are enabled in hardware but the OS is too old
#   for the runtime's safe handling. Running the compiler via the `dotnet` muxer (dotnet exec csc.dll)
#   works. build.ps1 points the MSBuild Csc task at a small csc.cmd wrapper that does exactly that.
#
# Prereqs: a .NET 10 SDK on PATH, and the SecRandom source checkout (set $SecRandomSourceDir below).

$ErrorActionPreference = "Stop"

# Point at the .NET 10 SDK if it is not already on PATH (repo-local install).
$env:PATH = "D:\code\.dotnet;$env:PATH"
$env:DOTNET_ROOT = "D:\code\.dotnet"
# Disable CET shadow stacks for the compiler running under `dotnet exec`.
$env:DOTNET_EnableCETShadowStacks = "0"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"

# csc.cmd wrapper -> `dotnet exec <sdk>\Roslyn\bincore\csc.dll` (adapt paths to your SDK).
$sdkDotnet = "D:\code\.dotnet\dotnet.exe"
$sdkCsc    = "D:\code\.dotnet\sdk\10.0.400\Roslyn\bincore\csc.dll"
$wrapDir   = "D:\code\cscwrap"
New-Item -ItemType Directory -Force -Path $wrapDir | Out-Null
@"
@echo off
"$sdkDotnet" exec "$sdkCsc" %*
"@ | Set-Content -Path "$wrapDir\csc.cmd" -Encoding Ascii

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
dotnet build "$root\src\SecScoreLauncher\SecScoreLauncher.csproj" -c Release $args `
    -p:UseSharedCompilation=false `
    -p:CscToolPath=$wrapDir `
    -p:CscToolExe=csc.cmd

if ($LASTEXITCODE -ne 0) { throw "Build failed." }

Write-Host ""
Write-Host "Plugin package: $root\src\SecScoreLauncher\srpx\SecScoreLauncher.srpx"
