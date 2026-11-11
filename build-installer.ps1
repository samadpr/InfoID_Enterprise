param([Parameter(Mandatory)][string]$Version)   # e.g.  .\build-installer.ps1 -Version 1.0.1
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

# Find Inno Setup's compiler (version 7 or 6, 64-bit / 32-bit / per-user install)
$iscc = @(
    "C:\Program Files\Inno Setup 7\ISCC.exe",
    "C:\Program Files (x86)\Inno Setup 7\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 7\ISCC.exe",
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw "Inno Setup (ISCC.exe) not found. Install Inno Setup or add its path to this script." }

# 1) Publish the app
Remove-Item .\Release\app -Recurse -Force -ErrorAction SilentlyContinue
dotnet publish .\InfoID.Desktop\InfoID.Desktop.csproj -c Release -r win-x64 --self-contained true `
    -p:Version=$Version -o .\Release\app
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (see the errors above)." }

# 2) Build the installer
& $iscc "/DAppVersion=$Version" .\Installer\InfoID.iss
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed to build the installer (see the errors above)." }

$setup = ".\Installer\Output\InfoID_Setup_$Version.exe"
if (-not (Test-Path $setup)) { throw "Installer file was not created: $setup" }
Write-Host "Done: $((Resolve-Path $setup).Path)" -ForegroundColor Green