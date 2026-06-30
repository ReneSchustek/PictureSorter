# Changelog

Alle nennenswerten Änderungen an diesem Projekt werden hier dokumentiert.
Das Format orientiert sich an [Keep a Changelog](https://keepachangelog.com/de/1.1.0/),
die Versionierung folgt [Semantic Versioning](https://semver.org/lang/de/).

## [1.2.0]

### Hinzugefügt

- **App-Icon und Logo**: gut sichtbares Icon (blaues Kachel-Logo) für Taskleiste
  und EXE; einheitliche Kachel-/Splash-Assets.
- **Helles und dunkles Design**, umschaltbar auf der Einstellungsseite; die Wahl
  wird gemerkt. Akzentfarbe passend zum Logo.
- **Geführter Sortier-Assistent** (5 Schritte mit je einer Aktion) wahlweise neben
  der klassischen Ein-Seiten-Ansicht; klickbare Schrittleiste und „Neu starten".
- **Global angedockte Statusleiste** mit Fortschritt und Stopp-Knopf, die auch
  beim Seitenwechsel sichtbar bleibt; die Foto-Analyse zeigt „x von y" als
  Prozent-Fortschritt. Farbige Schweregrade (Warnung/Fehler/Erfolg).
- **Geführte Ollama-Einrichtung**: Skript plus „Jetzt einrichten"-Knopf.
- **Bildinformationen als Mouse-Over** an jeder Vorschau (Name, Größe, Auflösung,
  Aufnahmedatum, Kamera, Ort, Pfad).
- **Embedding-Cache**: wiederholte Läufe überspringen unveränderte Fotos und sind
  dadurch deutlich schneller; die Cache-Datei wird bei Bedarf kompaktiert.
- **Persistentes, täglich rollierendes Logging** unter
  `%LocalAppData%\PictureSorter\logs\` (30 Tage), inklusive vollständiger
  Fehler-Stacktraces und Korrelations-IDs je Vorgang; integrierter Log-Viewer auf
  der Einstellungsseite.
- **CI-Pipeline** (GitHub Actions): Build, Whitespace-Prüfung, Tests und
  Sicherheits-Audit bei jedem Push und Pull Request.

### Behoben

- Laufende Vorgänge brechen beim Wechsel ins Menü nicht mehr ab (Seiten-Caching).
- Klarere Rückmeldung bei leerem oder nicht lesbarem Ordner.

## [1.1.0]

### Hinzugefügt

- **Duplikat-Erkennung** mit Bildvorschau (neue Seite „Duplikate"): erkennt
  bit-identische Dateien (SHA-256) und visuell ähnliche Bilder (Difference-Hash
  über die Windows-Bild-API). Auswahl je Bild, Löschen in den Papierkorb mit
  Rückfrage; das beste Bild je Gruppe ist zum Behalten vorausgewählt.
- **Metadaten beim Sortieren**: Aufnahmedatum, Aufnahmeort (GPS), Kamera und
  Auflösung werden aus den EXIF-Daten gelesen und fließen – zusätzlich zum Bild –
  in Embedding- und Vision-Bewertung ein.
- **Beispiel-Lernen in der Oberfläche**: Beispielbilder laden, als „gehört dazu" /
  „Gegenbeispiel" markieren, Profil lernen und persistieren.
- **Ordnerauswahl-Dialog** statt manueller Pfadeingabe.
- **Modell-Prüfung beim Start**: Hinweis, wenn Ollama nicht erreichbar ist oder
  Modelle fehlen, inklusive passendem `ollama pull`-Befehl.
- **Bulk-Bestätigung** vor dem Verschieben großer Mengen (Safe-Write).

### Geändert

- Die Data-Schicht zielt nun auf `net10.0-windows10.0.26100.0`, um die eingebaute
  Windows-Bild-API (EXIF, Bilddekodierung) ohne Fremd-Bibliothek zu nutzen.

## [1.0.0]

### Hinzugefügt

- Erste Fassung: lernfähiges Sortieren von Fotos per lokaler KI (Ollama),
  Clean-Architecture-Skelett (App/Application/Core/Data), SplashScreen,
  JSON-Persistenz der Kategorien.
