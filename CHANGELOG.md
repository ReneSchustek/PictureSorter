# Changelog

Alle nennenswerten Änderungen an diesem Projekt werden hier dokumentiert.
Das Format orientiert sich an [Keep a Changelog](https://keepachangelog.com/de/1.1.0/),
die Versionierung folgt [Semantic Versioning](https://semver.org/lang/de/).

## [Unveröffentlicht]

### Behoben

- **Datenverlust beim Verschieben**: Ein bereits einsortiertes Foto sah sich bei
  erneutem Lauf (mit „Unterordner einschließen") als Kollision mit sich selbst und
  wurde fortlaufend umbenannt (`a.jpg` → `a (1).jpg` → `a (1) (1).jpg`). Der
  Datei-Organizer erkennt jetzt „Quelle == Ziel" und lässt die Datei unangetastet.
- **Absturzsicherheit**: Prozessweite und nicht beobachtete Task-Ausnahmen werden
  jetzt protokolliert (bisher nur UI-Dispatcher); der Startvorgang ist abgesichert.
  Ein zweiter Klick auf ein Vorschau-Bild löst keinen Absturz mehr aus.
- **Barrierefreiheit**: Screenreader-Bezug an der Duplikat-Löschauswahl, höhere
  Textkontraste, angesagte Statusmeldungen und laienverständlicher KI-Hinweis.

### Geändert

- Die App bringt die Windows App Runtime jetzt selbst mit (self-contained); sie
  startet damit auf Rechnern ohne vorinstallierte Runtime.
- Die Application-Schicht ist plattformneutral (`net10.0`).

## [1.3.0] - 2026-07-11

### Hinzugefügt

- **Sortier-Gedächtnis**: PictureSorter merkt sich dauerhaft (SQLite), was zu welchem
  Foto entschieden wurde. Ein zweiter Lauf überspringt bereits einsortierte, abgewählte
  und von der KI abgelehnte Fotos – das spart die teuren KI-Aufrufe. Dasselbe Foto darf
  je Kategorie unterschiedlich beurteilt werden.
- **Seite „Gedächtnis"**: zeigt alle gemerkten Entscheidungen, filterbar nach Ordner und
  Kategorie. Einzelne Einträge oder ein ganzes Ordner-Gedächtnis lassen sich verwerfen –
  die betroffenen Fotos werden dann wieder neu bewertet.
- **Vorschau: Bilder abwählen** – jeder Vorschlag hat eine Auswahl-Box, dazu
  „Alle aus-/abwählen" und die Anzeige „x von y ausgewählt". Nur ausgewählte Bilder
  werden verschoben; abgewählte werden gemerkt und nicht erneut vorgeschlagen.
- **Großansicht**: Klick auf ein Vorschaubild zeigt das Foto in voller Größe samt allen
  Bildinformationen.
- **Startseite (Dashboard)** mit Kacheln in die drei Bereiche und dem Zustand der lokalen KI.
- **Neues Erscheinungsbild**: dunkle Navigationsleiste, farbige Kopfbereiche je Seite,
  Leerzustände mit Hinweis, was als Nächstes zu tun ist.

### Geändert

- Die ViewModels liegen jetzt in der App-Schicht (MVVM nach Vorlage); die
  Application-Schicht ist damit vollständig UI-frei.
- Je Projekt ein eigenes Testprojekt (App, Application, Core, Data, Infrastructure, Ollama).

### Sicherheit

- **Aktualisierung nur aus vertrauenswürdiger Quelle**: Der Updater wird ausschließlich
  über HTTPS von GitHub geladen und vor dem Start auf eine gültige, von Windows als
  vertrauenswürdig eingestufte Signatur geprüft. Ohne gültige Signatur wird die Datei
  verworfen. Der Download läuft in einem eigenen, frisch angelegten Ordner.
- Die native SQLite-Bibliothek wurde auf eine Version ohne bekannte Sicherheitslücke
  angehoben (GHSA-2m69-gcr7-jv3q).

### Behoben

- Ein einzelner Dateifehler (z. B. gesperrte Datei) brach bisher den gesamten
  Sortiervorgang ab. Jetzt werden die übrigen Dateien weiter verschoben; die
  fehlgeschlagene bleibt ungemerkt und wird erneut vorgeschlagen.
- Ein KI-Ausfall wurde nicht mehr als „Foto passt nicht" gemerkt – nur tatsächlich
  gefällte Urteile landen im Gedächtnis.
- Die Protokolldatei ist auf 100 MB je Tag begrenzt; der Log-Viewer lädt nur noch das
  Dateiende statt der kompletten Datei.
- Fremd-Protokolle (Datenbank, HTTP) fluten die Logdatei nicht mehr auf „Information".

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
