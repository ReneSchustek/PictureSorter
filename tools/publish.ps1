<#
.SYNOPSIS
    Baut die auslieferbare Fassung: den direkt startbaren Programmordner UND die
    Setup.exe zur Weitergabe.

.DESCRIPTION
    Ergebnis unter -OutputDir (Standard: F:\Entwicklung\publish\PictureSorter):

      * alle Programmdateien – ein Doppelklick auf PictureSorter.exe genügt, ohne
        Installation und ohne vorinstalliertes .NET oder Windows App Runtime
        (self-contained),
      * PictureSorter-Setup-v<Version>.exe – die Installation zum Weitergeben.

    Der Installer wird bewusst aus einem getrennten Zwischenordner gebaut
    (dist\publish im Repo): Läge seine Quelle im Ausgabeordner, packte ein zweiter
    Lauf die Setup.exe des ersten mit ein.

.PARAMETER Version
    Versionsnummer. Standard: der Wert aus der Datei VERSION.

.PARAMETER OutputDir
    Zielordner der Auslieferung.

.PARAMETER Runtime
    Ziel-Architektur (Standard: win-x64).

.EXAMPLE
    ./tools/publish.ps1
#>
[CmdletBinding()]
param(
    [string] $Version,
    [string] $OutputDir = 'F:\Entwicklung\publish\PictureSorter',
    [string] $Runtime = 'win-x64'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
if (-not $Version) {
    $Version = (Get-Content -LiteralPath (Join-Path $root 'VERSION') -Raw).Trim()
}

$platform = switch ($Runtime) {
    'win-x64' { 'x64' }
    'win-x86' { 'x86' }
    'win-arm64' { 'ARM64' }
    default { throw "Unbekannte Architektur: $Runtime" }
}

$project = Join-Path $root 'src\PictureSorter.App\PictureSorter.App.csproj'
$staging = Join-Path $root 'dist\publish'
$script = Join-Path $root 'installer\PictureSorter.iss'

$iscc = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $iscc) {
    throw 'Inno Setup 6 (ISCC.exe) nicht gefunden. Installation: winget install JRSoftware.InnoSetup'
}

Write-Host "PictureSorter $Version ($Runtime) veröffentlichen ..." -ForegroundColor Cyan
if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force }

& dotnet publish $project `
    -c Release `
    -r $Runtime `
    -p:Platform=$platform `
    -p:Version=$Version `
    -p:WindowsPackageType=None `
    --self-contained true `
    -o $staging
if ($LASTEXITCODE -ne 0) { throw "dotnet publish fehlgeschlagen ($LASTEXITCODE)." }

Write-Host 'Programmdateien in den Ausgabeordner kopieren ...' -ForegroundColor Cyan
if (Test-Path -LiteralPath $OutputDir) { Remove-Item -LiteralPath $OutputDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
Copy-Item -Path (Join-Path $staging '*') -Destination $OutputDir -Recurse -Force

Write-Host 'Setup.exe kompilieren ...' -ForegroundColor Cyan
& $iscc "/DMyAppVersion=$Version" "/DPublishDir=$staging" "/O$OutputDir" $script | Out-Null
if ($LASTEXITCODE -ne 0) { throw "ISCC fehlgeschlagen ($LASTEXITCODE)." }

$setup = Join-Path $OutputDir "PictureSorter-Setup-v$Version.exe"
$size = (Get-Item -LiteralPath $setup).Length / 1MB
$files = (Get-ChildItem -LiteralPath $OutputDir -Recurse -File | Measure-Object).Count

Write-Host ''
Write-Host "Fertig: $OutputDir" -ForegroundColor Green
Write-Host ("  Direkt startbar : {0}" -f (Join-Path $OutputDir 'PictureSorter.exe'))
Write-Host ("  Zum Weitergeben : {0} ({1:N1} MB)" -f $setup, $size)
Write-Host ("  Dateien gesamt  : {0}" -f $files)
