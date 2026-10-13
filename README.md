# PictureSorter

PictureSorter ist eine Windows-Desktop-Anwendung (WinUI 3), die Fotos aus einem
ausgewählten Ordner in Unterordner sortiert, etwa „Familie" oder „Urlaub 14.07.25".
Das erledigt eine lokal laufende KI (Ollama), und zwar lernfähig. Alle Bildanalysen
laufen vollständig offline auf dem eigenen Rechner.

## Funktionen

- **Lernfähiges Sortieren**: Du beschreibst eine Kategorie in eigenen Worten,
  bestätigst einige Beispielbilder, und den Rest sortiert die App selbst. In die
  Bewertung fließen neben dem Bild auch die **Bildinformationen** ein: Aufnahmedatum,
  **Aufnahmeort (GPS)**, Kamera und Auflösung aus den EXIF-Daten.
- **Sortieren nach Aufnahmedatum, ganz ohne KI**: Bei einem Urlaub entscheidet der
  Zeitraum, nicht das Motiv. Im zweiten Schritt lässt sich deshalb *„Nur nach
  Aufnahmedatum sortieren"* wählen. Dann entfallen das Anlernen und jede Bildbewertung,
  gelesen werden nur die Bildinformationen, und der Lauf dauert Sekunden statt Minuten.
  Ein Zeitraum ist dabei Pflicht, sonst stünde der ganze Ordner zum Verschieben bereit.
- **Zeitraum-Filter mit Urlaubs-Vorschlägen**: Die App erkennt Zeiträume, in denen
  sich die Aufnahmen ballen, und bietet sie zur Übernahme an. Fotos außerhalb davon
  bekommt die KI gar nicht erst zu sehen.
- **Duplikat-Erkennung** mit Bildvorschau: Sie findet **bit-identische** Dateien und
  über einen Wahrnehmungs-Hash auch **visuell ähnliche** Bilder (skaliert oder neu
  komprimiert). In jeder Gruppe ist das beste Bild zum Behalten vorausgewählt, die
  übrigen zum Löschen. Gelöschte Dateien wandern in den **Papierkorb**.
- **Namen in Ordnung bringen**: Die App findet Dateien, deren Name sich nicht lesen
  lässt, etwa aus einem iCloud-Export, in dem Ersatzzeichen statt der ursprünglichen
  Zeichen stehen. Der neue Name kommt aus dem Aufnahmedatum, sofern eines im Dateikopf
  steht, sonst aus Ordnername und laufender Nummer. Die Endung bleibt unangetastet,
  jede Zeile lässt sich abwählen, und der Lauf ist zurücknehmbar.
- **Sicher**: Vor jeder Aktion gibt es eine Vorschau, bei großen Mengen eine Rückfrage.
  Verschoben wird kollisionssicher, Gelöschtes lässt sich wiederherstellen.
- **Rückgängig**: Ein Sortierlauf lässt sich vollständig zurücknehmen, auch nach
  einem Neustart und auch dann, wenn er mittendrin abgebrochen wurde. Am
  Ursprungsort wird dabei nie etwas überschrieben.
- **Nachvollziehbar**: Ein Protokoll in der Anwendung (*Einstellungen*) zeigt, was
  passiert ist. Es lässt sich durchsuchen und auf Warnungen und Fehler eingrenzen.

## Schnellstart

1. `PictureSorter-Setup-v<Version>.exe` aus dem
   [neuesten Release](https://github.com/ReneSchustek/PictureSorter/releases) ausführen.
   Adminrechte braucht es nicht. Beim ersten Start warnt Windows vor einem unbekannten
   Herausgeber; weiter geht es über *Weitere Informationen* → *Trotzdem ausführen*.
2. Anwendung starten. Fehlt die lokale KI, steht auf der Startseite ein Hinweis mit einer
   Schaltfläche, die sie einrichtet. Die Duplikat-Suche funktioniert auch ohne KI.
3. *Fotos sortieren* öffnen und dem Assistenten folgen. Bevor er etwas verschiebt, zeigt
   er eine Vorschau, und jeder Lauf lässt sich hinterher vollständig zurücknehmen.

> **Vor dem ersten Sortierlauf eine Sicherungskopie der Fotos anlegen.**
> PictureSorter verschiebt Dateien und kann jeden Lauf vollständig zurücknehmen.
> Gegen einen Plattenausfall oder ein versehentliches Löschen außerhalb der
> Anwendung hilft aber nur eine Kopie. Wie sich die Daten der Anwendung sichern
> lassen, steht im [Betriebshandbuch](BETRIEB.md).

## Voraussetzungen

- Windows 10 oder 11 (Build 10.0.17763.0 oder neuer)
- [.NET SDK 10.0](https://dotnet.microsoft.com/download) (zum Bauen)
- [Ollama](https://ollama.com/) lokal installiert und laufend (`http://127.0.0.1:11434`)
- **Benötigte Ollama-Modelle** (einmalig laden). Ohne sie ist nur die
  Duplikat-Erkennung nutzbar:
  - `ollama pull qwen2.5vl:7b`: Vision-Modell, beschreibt das Bild und prüft
    Grenzfälle (rund 6 GB)
  - `ollama pull nomic-embed-text`: Embedding-Modell, wandelt die Beschreibung in
    einen Vergleichsvektor für das Ähnlichkeitslernen

> Die App prüft **beim Start**, ob Ollama erreichbar ist und beide Modelle
> installiert sind. Fehlt etwas, erscheint auf der Startseite ein Hinweis mit dem
> passenden `ollama pull`-Befehl. Die Duplikat-Erkennung kommt ohne Ollama aus,
> weil sie rein lokal über Datei- und Wahrnehmungs-Hashes arbeitet.

## Installation

**Zum Benutzen** (Weitergabe): `PictureSorter-Setup-v<Version>.exe` aus dem
[neuesten Release](https://github.com/ReneSchustek/PictureSorter/releases) ausführen,
Adminrechte sind nicht nötig. Wer nichts installieren will, entpackt stattdessen das ZIP
der passenden Architektur und startet `PictureSorter.exe`. Ein vorinstalliertes .NET
oder eine Windows App Runtime braucht es nicht (self-contained).

Selbst bauen, Programmordner **und** Setup.exe in einem Schritt:

```pwsh
./tools/publish.ps1        # -> F:\Entwicklung\publish\PictureSorter
```

> Hinweis: Die Builds tragen keine Signatur einer Zertifizierungsstelle, deshalb kann
> Windows SmartScreen beim ersten Start warnen. Für die **Echtheit der Updates** spielt
> das keine Rolle: Die Anwendung spielt ein Update nur ein, wenn es mit dem privaten
> Schlüssel des Herausgebers unterschrieben ist (siehe „Aktualisierung").

## Aktualisierung

Beim Start prüft die Anwendung, ob es eine neuere Version gibt, und spielt sie auf
Wunsch selbst ein. Vertrauensanker ist eine **losgelöste ECDSA-Signatur** über das
Release-Paket. Geprüft wird sie gegen den öffentlichen Schlüssel, der in die Anwendung
einkompiliert ist (`ReleaseSignatureVerifier`):

* **Fail-closed**: Ohne gültige Signatur wird nichts entpackt und nichts gestartet.
* Selbst wer den Release-Kanal übernimmt, kommt nicht durch, denn ohne den privaten
  Schlüssel entsteht keine gültige Signatur. Eine Authenticode-Signatur leistet das
  *nicht*. Sie bestätigt nur, dass *irgendein* Zertifikat unterschrieben hat, und ein
  Zertifikat kann sich jeder besorgen.
* Ein eigenes Updater-Programm gibt es nicht. Die geprüfte neue Fassung startet sich
  selbst im Helfer-Modus (`--apply-update`), ersetzt die Installation (mit Sicherung
  und Rollback) und startet sie neu. Seinen Aufrufparametern traut der Helfer dabei
  nicht, er gleicht sie gegen einen Vermerk des geprüften Hauptprozesses ab.

Schlüssel erzeugen (einmalig und privat halten; er gehört als Secret
`PICTURESORTER_SIGNING_KEY` in die CI, nicht ins Repository):

```pwsh
./tools/new-signing-key.ps1 -KeyPath ..\PictureSorter-signing\private-key.pem
```

Der ausgegebene öffentliche Schlüssel gehört in
`ReleaseSignatureVerifier.PublicKeySpkiBase64`. **Eine Rotation macht ausgelieferte
Anwendungen update-unfähig**, weil sie nur den alten Schlüssel kennen.

## Aus dem Quellcode bauen

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
- Schichten (sieben Projekte, die Abhängigkeiten zeigen stets nach innen zur Domäne `Core`):
  - `src/PictureSorter.App`: WinUI 3, Views **und** ViewModels, Composition Root
  - `src/PictureSorter.Application`: Use Cases / Sortierlogik, UI-frei
  - `src/PictureSorter.Core`: Domäne (Entitäten, Wertobjekte, Schnittstellen)
  - `src/PictureSorter.Data`: Persistenz (EF Core / SQLite, Sortier-Gedächtnis)
  - `src/PictureSorter.Imaging`: EXIF-Auslesen und Wahrnehmungs-Hash (WinRT)
  - `src/PictureSorter.Infrastructure`: Dateisystem, Update, Cache, JSON
  - `src/PictureSorter.Ollama`: HTTP-Anbindung der lokalen KI
- Tests: zu jedem `src`-Projekt gibt es ein Testprojekt unter `tests/`
  (`PictureSorter.App.Tests`, `.Application.Tests`, `.Core.Tests`, `.Data.Tests`,
  `.Infrastructure.Tests`, `.Ollama.Tests`).
- Bildschirmfotos für die Webseite: `tools/bildschirmfotos.ps1` nimmt die ganze Serie mit
  einem Aufruf an einem Demo-Bestand neu auf. Sie liegt unter `docs/bilder/` und ist dort
  beschrieben.
- Qualitäts-Gate vor jedem Abschluss:

  ```pwsh
  dotnet build PictureSorter.slnx -c Release
  dotnet format PictureSorter.slnx --verify-no-changes
  dotnet test PictureSorter.slnx
  ```

## MSIX-Paket erstellen (Sideload)

Ohne weitere Schritte läuft die App **unpaketiert** (F5/`dotnet run` ohne Deploy). Ein
installierbares MSIX-Paket braucht eine **Signatur**, deren Zertifikats-Subjekt exakt
zum `Publisher` im `Package.appxmanifest` passt (`CN=Rene Schustek`).

Das Projekt ist darauf vorbereitet (`AppxPackageSigningEnabled` und ein Verweis auf
`PictureSorter.App_TemporaryKey.pfx`, nur für paketierte Builds). Der private
Testschlüssel liegt bewusst **nicht** im Repository (`.gitignore`). Einmalig erzeugt
wird er so:

```pwsh
$cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject "CN=Rene Schustek" `
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

Das `.msix` liegt danach unter
`src/PictureSorter.App/AppPackages/…/PictureSorter.App_<Version>_x64.msix` und lässt
sich per Doppelklick (App Installer) oder mit `Add-AppxPackage` installieren.

> In **Visual Studio** tritt derselbe Fehler auf, wenn kein Zertifikat hinterlegt
> ist: Manifest-Designer → Reiter **Paketerstellung** → **Zertifikat auswählen…**
> → **Testzertifikat erstellen…** (Subjekt `CN=Rene Schustek`). Für die **Verteilung**
> muss das selbstsignierte Testzertifikat ersetzt und die Manifest-Identität
> (`Name`, `Publisher`) auf die echten Store- bzw. Signaturwerte gesetzt werden.

## Konfiguration

Die Ollama-Anbindung wird über `appsettings.json` im App-Projekt eingestellt
(Basis-URL, Modellnamen, Zeitlimits).

## Betrieb

Wo die Daten liegen, wie man sie sichert und wie sich ein Sortierlauf oder ein
Update zurücknehmen lässt, steht im [Betriebshandbuch](BETRIEB.md).

## Lizenz

Proprietär, alle Rechte vorbehalten.
