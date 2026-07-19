#requires -Version 5.1
<#
.SYNOPSIS
    Builds cuo.dll (NativeAOT shared library) for the Phoenix BootstrapHost use case.

.DESCRIPTION
    Publishes src/ClassicUO.Client with -p:BootstrapHostMode=true, which flips the
    project to:
        OutputType = Library
        NativeLib  = Shared
        PublishAot = true
        AssemblyName = cuo
    producing a NativeAOT shared library (cuo.dll) whose [UnmanagedCallersOnly]
    'Initialize' export publishes the HostBindings/ClientBindings function-pointer
    tables - including the WalkToFn / StopWalkFn / WalkProgressFn delegates wired in
    PluginHost.cs. No extra step is needed to "expose" those delegates: they are part
    of the committed client source, so any BootstrapHostMode build bakes them in.

    Output goes to bin/dist (same location the existing build uses), alongside the
    native runtime deps (SDL3.dll, FAudio.dll, FNA3D.dll, zlib.dll, ...).

.PARAMETER Rid
    Runtime identifier. Default win-x64.

.PARAMETER Configuration
    Build configuration. Default Release. (NativeAOT is only meaningful in Release.)

.PARAMETER DeployTo
    Optional. If given, copies the freshly built cuo.dll (and cuo.pdb) into this
    directory after the build - e.g. the Phoenix bundle's plugin folder.

.PARAMETER ClassicLogin
    Build the original UO login screen instead of the custom gothic
    login/realm/character scene. The custom scene is built in by default.

.NOTES
    Prerequisites for NativeAOT on Windows: the Visual Studio C++ build tools
    (Desktop C++ workload - provides link.exe and the MSVC libs). Without them the
    publish fails at the native link step.

.EXAMPLE
    ./build-cuo.ps1

.EXAMPLE
    ./build-cuo.ps1 -DeployTo "E:\Projects\phoenix\out\classicuo-bundle-net10"
#>
[CmdletBinding()]
param(
    [string]$Rid = "win-x64",
    [string]$Configuration = "Release",
    [string]$DeployTo,
    # The custom gothic login/realm/character scene is built in by default.
    # Pass -ClassicLogin to build the original UO login instead.
    [switch]$ClassicLogin
)

$ErrorActionPreference = "Stop"

$repoRoot      = $PSScriptRoot
$clientProject = Join-Path $repoRoot "src/ClassicUO.Client/ClassicUO.Client.csproj"
$outputDir     = Join-Path $repoRoot "bin/dist"

if (-not (Test-Path $clientProject)) {
    throw "Client project not found at $clientProject - run this script from the ClassicUO repo root."
}

Write-Host "==> Building cuo.dll (BootstrapHostMode)" -ForegroundColor Cyan
Write-Host "    project : $clientProject"
Write-Host "    rid     : $Rid"
Write-Host "    config  : $Configuration"
Write-Host "    output  : $outputDir"

$cuoDll = Join-Path $outputDir "cuo.dll"
$before = if (Test-Path $cuoDll) { (Get-Item $cuoDll).LastWriteTime } else { $null }

$customLogin = -not $ClassicLogin
Write-Host ("    login   : {0} (CustomLoginScene={1})" -f $(if ($customLogin) { "custom gothic scene" } else { "classic UO" }), $customLogin.ToString().ToLower())

$publishArgs = @(
    $clientProject
    "-c", $Configuration
    "-r", $Rid
    "-p:BootstrapHostMode=true"
    "-p:StripSymbols=true"
    "-p:CustomLoginScene=$($customLogin.ToString().ToLower())"
    "-o", $outputDir
)

& dotnet publish @publishArgs

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

if (-not (Test-Path $cuoDll)) {
    throw "Build reported success but $cuoDll was not produced."
}

$item = Get-Item $cuoDll
if ($before -and $item.LastWriteTime -le $before) {
    Write-Warning "cuo.dll timestamp did not advance ($($item.LastWriteTime)); the publish may have been a no-op."
}

Write-Host ""
Write-Host "==> cuo.dll built" -ForegroundColor Green
Write-Host ("    {0}  ({1:N0} bytes, {2:yyyy-MM-dd HH:mm:ss})" -f $cuoDll, $item.Length, $item.LastWriteTime)

if ($DeployTo) {
    if (-not (Test-Path $DeployTo)) {
        New-Item -ItemType Directory -Force -Path $DeployTo | Out-Null
    }
    Copy-Item $cuoDll -Destination $DeployTo -Force
    $cuoPdb = Join-Path $outputDir "cuo.pdb"
    if (Test-Path $cuoPdb) { Copy-Item $cuoPdb -Destination $DeployTo -Force }
    Write-Host "==> Deployed cuo.dll -> $DeployTo" -ForegroundColor Green
}
