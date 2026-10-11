<#
.SYNOPSIS
  Збирає інсталятор dist\SchoolBell-Setup-<версія>.exe на Windows.

  Потрібно:
    .NET 10 SDK:  winget install Microsoft.DotNet.SDK.10
    NSIS:         winget install NSIS.NSIS

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File .\scripts\build-installer.ps1
#>
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
Set-Location $root

function Step($text) { Write-Host "==> $text" -ForegroundColor Cyan }

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "Не знайдено .NET SDK. Встановіть: winget install Microsoft.DotNet.SDK.10"
}
if (-not (dotnet --list-sdks | Select-String '^10\.')) {
    throw "Потрібен .NET 10 SDK. Встановіть: winget install Microsoft.DotNet.SDK.10"
}

$makensis = (Get-Command makensis -ErrorAction SilentlyContinue).Source
if (-not $makensis) {
    foreach ($p in @("${env:ProgramFiles(x86)}\NSIS\makensis.exe", "$env:ProgramFiles\NSIS\makensis.exe")) {
        if (Test-Path $p) { $makensis = $p; break }
    }
}
if (-not $makensis) { throw "Не знайдено NSIS. Встановіть: winget install NSIS.NSIS" }

[xml]$csproj = Get-Content 'src\SchoolBell.App\SchoolBell.App.csproj'
$version = ($csproj.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1)
if (-not $version) { $version = '1.0.0' }

$publish = Join-Path $root 'publish'
$dist = Join-Path $root 'dist'
$out = Join-Path $dist "SchoolBell-Setup-$version.exe"

Step "Тести логіки розкладу і тривоги"
dotnet run --project tests\SchoolBell.Core.Tests -c Release
if ($LASTEXITCODE -ne 0) { throw "Тести не пройшли — збірку зупинено." }

Step "Збірка SchoolBell.exe (версія $version)"
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
dotnet publish src\SchoolBell.App\SchoolBell.App.csproj `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=none `
    -o $publish
if ($LASTEXITCODE -ne 0) { throw "Публікація не вдалася." }

Step "Пакування інсталятора"
New-Item -ItemType Directory -Force -Path $dist | Out-Null
& $makensis -V2 -INPUTCHARSET UTF8 "-DVERSION=$version" "-DPUBLISH_DIR=$publish" "-DOUT_FILE=$out" 'installer\SchoolBell.nsi'
if ($LASTEXITCODE -ne 0) { throw "NSIS не зміг зібрати інсталятор." }

Write-Host ""
Write-Host "Готово: $out" -ForegroundColor Green
