<#
.SYNOPSIS
    Erzeugt das ECDSA-P-256-Schlüsselpaar, mit dem die Release-Pakete signiert werden.

.DESCRIPTION
    PictureSorter prüft die Echtheit eines heruntergeladenen Update-Pakets über eine
    losgelöste Signatur (siehe ReleaseSignatureVerifier) und akzeptiert genau einen
    Unterzeichner: den, dessen öffentlicher Schlüssel in der Anwendung einkompiliert
    ist. Ein übernommener Release-Kanal nützt einem Angreifer damit nichts – ohne den
    privaten Schlüssel entsteht keine gültige Signatur.

    Dieses Skript erzeugt das Schlüsselpaar einmalig:

      * Der PRIVATE Schlüssel (PKCS#8-PEM) landet unter -KeyPath und MUSS geheim
        bleiben. Er gehört nicht ins Repository, sondern offline und – für die CI –
        als Secret (PICTURESORTER_SIGNING_KEY).
      * Der ÖFFENTLICHE Schlüssel (SubjectPublicKeyInfo, Base64) wird ausgegeben und
        muss in ReleaseSignatureVerifier.PublicKeySpkiBase64 eingetragen werden.

    Ein bestehender Schlüssel wird nicht überschrieben. Eine Rotation macht bereits
    ausgelieferte Anwendungen update-unfähig: Sie kennen nur den alten öffentlichen
    Schlüssel und lehnen jedes neu signierte Paket ab. Deshalb -Force nur bewusst.

.PARAMETER KeyPath
    Zielpfad des privaten Schlüssels (PEM). Muss außerhalb des Repos liegen.

.PARAMETER Force
    Überschreibt einen bestehenden Schlüssel (Rotation).

.EXAMPLE
    ./tools/new-signing-key.ps1 -KeyPath F:\Entwicklung\PictureSorter-signing\private-key.pem
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $KeyPath,
    [switch] $Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ((Test-Path -LiteralPath $KeyPath) -and -not $Force) {
    throw "Schlüssel existiert bereits: $KeyPath. Zum bewussten Rotieren -Force angeben."
}

$directory = Split-Path -Parent $KeyPath
if ($directory -and -not (Test-Path -LiteralPath $directory)) {
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
}

# ECDSA P-256: kurze Signaturen, vollständig in der .NET-Basisbibliothek – keine
# Fremdbibliothek, kein Zertifikat, kein Ablaufdatum.
$ecdsa = [System.Security.Cryptography.ECDsa]::Create(
    [System.Security.Cryptography.ECCurve+NamedCurves]::nistP256)
try {
    Set-Content -LiteralPath $KeyPath -Value $ecdsa.ExportPkcs8PrivateKeyPem() -NoNewline
    $publicSpki = [Convert]::ToBase64String($ecdsa.ExportSubjectPublicKeyInfo())
}
finally {
    $ecdsa.Dispose()
}

Write-Host 'Privater Schlüssel geschrieben (GEHEIM HALTEN, NICHT committen):' -ForegroundColor Yellow
Write-Host "  $KeyPath"
Write-Host ''
Write-Host 'Öffentlichen Schlüssel in ReleaseSignatureVerifier.PublicKeySpkiBase64 eintragen:' -ForegroundColor Cyan
Write-Host $publicSpki
