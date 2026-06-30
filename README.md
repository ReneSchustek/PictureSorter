# PictureSorter

PictureSorter ist eine Windows-Desktop-Anwendung (WinUI 3), die Fotos aus einem
ausgewählten Ordner mithilfe einer lokal laufenden KI (Ollama) lernfähig in
Unterordner sortiert — zum Beispiel „Familie" oder „Urlaub 14.07.25". Alle
Bildanalysen laufen vollständig offline auf dem eigenen Rechner.

## Funktionen

- **Lernfähiges Sortieren**: Kategorie in eigenen Worten beschreiben, einige
  Beispielbilder bestätigen, den Rest sortiert die App selbst. Bei der Bewertung
  fließen neben dem Bild selbst auch die **Bildinformationen** ein – Aufnahmedatum,
  **Aufnahmeort (GPS)**, Kamera und Auflösung aus den EXIF-Daten.
- **Duplikat-Erkennung** mit Bildvorschau: findet **bit-identische** Dateien und –
  über einen Wahrnehmungs-Hash – auch **visuell ähnliche** Bilder (skaliert oder
  neu komprimiert). In jeder Gruppe ist das beste Bild vorausgewählt zum Behalten,
  die übrigen zum Löschen. Gelöschte Dateien wandern in den **Papierkorb**.
- **Sicher**: Vorschau vor jeder Aktion, Rückfrage bei großen Mengen, Verschieben
  kollisionssicher, Löschen wiederherstellbar.

## Voraussetzungen

- Windows 11 (Build 10.0.17763.0 oder neuer)
- [.NET SDK 10.0](https://dotnet.microsoft.com/download) (zum Bauen)
- [Ollama](https://ollama.com/) lokal installiert und laufend (`http://localhost:11434`)
- **Benötigte Ollama-Modelle** (einmalig laden) — ohne sie ist nur die
  Duplikat-Erkennung nutzbar:
  - `ollama pull llava` — Vision-Modell: beschreibt das Bild und prüft Grenzfälle
  - `ollama pull nomic-embed-text` — Embedding-Modell: wandelt die Beschreibung in
    einen Vergleichsvektor für das Ähnlichkeitslernen

> Die App prüft **beim Start**, ob Ollama erreichbar ist und beide Modelle
> installiert sind. Fehlt etwas, erscheint auf der Startseite ein Hinweis mit dem
> passenden `ollama pull`-Befehl. Die Duplikat-Erkennung funktioniert auch ohne
> Ollama, da sie rein lokal über Datei- und Wahrnehmungs-Hashes arbeitet.

## Installation

1. Repository klonen oder entpacken.
2. Ollama starten und die oben genannten Modelle laden.
3. Im Projektverzeichnis die Solution wiederherstellen und bauen:

   ```pwsh
   dotnet restore PictureSorter.slnx
   dotnet build PictureSorter.slnx -c Release
   ```

4. Anwendung starten:

   ```pwsh
   dotnet run --project src/PictureSorter.App -c Release
   ```

## Entwicklung

- Solution-Datei: `PictureSorter.slnx` (Visual Studio 2022/2026 oder `dotnet`).
- Schichten: `src/PictureSorter.App` (WinUI 3), `src/PictureSorter.Application`
  (Use Cases, ViewModels), `src/PictureSorter.Core` (Domäne), `src/PictureSorter.Data`
  (Persistenz, Ollama-Anbindung).
- Tests: `tests/PictureSorter.Tests.Unit`, `tests/PictureSorter.Tests.Integration`.
- Qualitäts-Gate vor jedem Abschluss:

  ```pwsh
  dotnet build PictureSorter.slnx -c Release
  dotnet format PictureSorter.slnx --verify-no-changes
  dotnet test PictureSorter.slnx
  ```

- Die Schichten sind oben unter „Entwicklung" beschrieben; Abhängigkeiten zeigen
  stets nach innen zur Domäne (`Core`).

## MSIX-Paket erstellen (Sideload)

Standardmäßig läuft die App **unpaketiert** (F5/`dotnet run` ohne Deploy). Für ein
installierbares MSIX-Paket ist eine **Signatur** nötig, deren Zertifikats-Subjekt
exakt zum `Publisher` im `Package.appxmanifest` passt (`CN=AppPublisher`).

Das Projekt ist dafür vorbereitet (`AppxPackageSigningEnabled` + Verweis auf
`PictureSorter.App_TemporaryKey.pfx`, nur für paketierte Builds). Der private
Testschlüssel ist bewusst **nicht** im Repository (`.gitignore`). So wird er einmalig
erzeugt:

```pwsh
$cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject "CN=AppPublisher" `
  -CertStoreLocation "Cert:\CurrentUser\My" -KeyExportPolicy Exportable -KeyUsage DigitalSignature `
  -NotAfter (Get-Date).AddYears(5)
$bytes = $cert.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Pfx)
[System.IO.File]::WriteAllBytes("src\PictureSorter.App\PictureSorter.App_TemporaryKey.pfx", $bytes)
# Damit sich das Paket installieren lässt, das Zertifikat als vertrauenswürdig hinterlegen:
$cer = "$env:TEMP\ps_test.cer"; Export-Certificate -Cert $cert -FilePath $cer -Type CERT | Out-Null
Import-Certificate -FilePath $cer -CertStoreLocation "Cert:\CurrentUser\TrustedPeople" | Out-Null
```

Paket bauen:

```pwsh
dotnet build src/PictureSorter.App/PictureSorter.App.csproj -c Release -p:Platform=x64 `
  -p:WindowsPackageType=MSIX -p:GenerateAppxPackageOnBuild=true `
  -p:UapAppxPackageBuildMode=SideloadOnly -p:AppxBundle=Never
```

Das `.msix` liegt anschließend unter
`src/PictureSorter.App/AppPackages/…/PictureSorter.App_<Version>_x64.msix` und lässt
sich per Doppelklick (App Installer) oder `Add-AppxPackage` installieren.

> In **Visual Studio** entsteht derselbe Fehler, wenn kein Zertifikat hinterlegt
> ist: Manifest-Designer → Reiter **Paketerstellung** → **Zertifikat auswählen…**
> → **Testzertifikat erstellen…** (Subjekt `CN=AppPublisher`). Für die **Verteilung**
> ist das selbstsignierte Testzertifikat zu ersetzen und die Manifest-Identität
> (`Name`, `Publisher`) auf die echten Store-/Signaturwerte zu setzen.

## Konfiguration

Die Ollama-Anbindung wird über `appsettings.json` im App-Projekt konfiguriert
(Basis-URL, Modellnamen, Zeitlimits).

## Lizenz

Proprietär — alle Rechte vorbehalten.
