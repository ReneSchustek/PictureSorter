<#
.SYNOPSIS
    Signiert ein Release-Paket und schreibt die losgelöste Signatur als <Paket>.sig daneben.

.DESCRIPTION
    Die Anwendung lädt zu jedem Update-Paket dessen Signatur mit und prüft sie gegen
    den einkompilierten öffentlichen Schlüssel, bevor irgendetwas entpackt wird
    (fail-closed). Dieses Skript erzeugt genau diese Signatur:

      * Verfahren: ECDSA P-256 über SHA-256 der gesamten Paketdatei.
      * Ausgabe: die rohen Signaturbytes in <Paket>.sig.

    Die erzeugte Signatur wird sofort gegengeprüft; schlägt das fehl, bricht das
    Skript ab, statt ein Paket auszuliefern, das kein Client annehmen kann.

.PARAMETER PackagePath
    Das zu signierende Paket (z. B. PictureSorter-v1.3.0-win-x64.zip).

.PARAMETER KeyPath
    Der private Schlüssel (PKCS#8-PEM), siehe new-signing-key.ps1.

.PARAMETER OutPath
    Zielpfad der Signatur. Standard: <PackagePath>.sig

.EXAMPLE
    ./tools/sign-release.ps1 -PackagePath PictureSorter-v1.3.0-win-x64.zip -KeyPath ..\private-key.pem
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $PackagePath,
    [Parameter(Mandatory)][string] $KeyPath,
    [string] $OutPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $PackagePath)) { throw "Paket nicht gefunden: $PackagePath" }
if (-not (Test-Path -LiteralPath $KeyPath)) { throw "Schlüssel nicht gefunden: $KeyPath" }
if (-not $OutPath) { $OutPath = "$PackagePath.sig" }

$ecdsa = [System.Security.Cryptography.ECDsa]::Create()
try {
    $ecdsa.ImportFromPem((Get-Content -LiteralPath $KeyPath -Raw))

    $bytes = [System.IO.File]::ReadAllBytes((Resolve-Path -LiteralPath $PackagePath))
    $signature = $ecdsa.SignData($bytes, [System.Security.Cryptography.HashAlgorithmName]::SHA256)

    # Selbstkontrolle: Eine Signatur, die hier nicht verifiziert, verifiziert auch
    # beim Nutzer nicht - dann lieber kein Release als ein unbrauchbares.
    if (-not $ecdsa.VerifyData($bytes, $signature, [System.Security.Cryptography.HashAlgorithmName]::SHA256)) {
        throw 'Selbstprüfung der erzeugten Signatur fehlgeschlagen.'
    }

    [System.IO.File]::WriteAllBytes($OutPath, $signature)
    $publicSpki = [Convert]::ToBase64String($ecdsa.ExportSubjectPublicKeyInfo())
}
finally {
    $ecdsa.Dispose()
}

Write-Host "Signatur geschrieben: $OutPath" -ForegroundColor Green
Write-Host 'Öffentlicher Schlüssel (muss dem einkompilierten entsprechen):'
Write-Host $publicSpki
