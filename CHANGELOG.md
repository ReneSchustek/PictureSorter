# Changelog

Alle nennenswerten Änderungen an diesem Projekt werden hier dokumentiert.
Das Format orientiert sich an [Keep a Changelog](https://keepachangelog.com/de/1.1.0/),
die Versionierung folgt [Semantic Versioning](https://semver.org/lang/de/).

## [Unveröffentlicht]

### Geändert

- **Die Beispiele werden nicht mehr vorab geladen, sondern in zwei getrennten Schritten
  selbst zusammengestellt.** Vorher standen dreißig Bilder des Ordners bereits in der
  Auswahl, von denen zu einem bestimmten Thema oft kaum eines passte — der Platz war
  trotzdem belegt, und jedes Bild musste einzeln als passend oder nicht passend markiert
  werden. Jetzt gibt es je einen Schritt für die passenden Bilder und für die
  Gegenbeispiele, beide leer beginnend, mit eigener Obergrenze und ständig ablesbarem
  Stand. Auf jeder Seite lassen sich Vorschläge holen (jeder Griff greift weiter hinten
  im Ordner), eigene Bilder wählen, Bilder aus dem Explorer hereinziehen und einzeln
  wieder entfernen. Dasselbe Foto kann nicht mehr gleichzeitig als passend und als
  Gegenbeispiel dastehen.

### Behoben

- **Der letzte Schritt des Assistenten trug die Überschrift des vorletzten.** Über der
  Vorschau stand „5. Analyse starten", obwohl dort sortiert wird.
- **Die Erklärung im letzten Schritt blieb in der englischen Fassung deutsch.** Für
  diesen einen Text fehlte die Übersetzung; angezeigt wurde ersatzweise das deutsche
  Original.
- **Der Hinweis am Häkchen sagte das Gegenteil dessen, was er meinte.** „Abhaken heißt:
  nicht verschieben" — gemeint war das Entfernen des Häkchens.
- **„Vorschläge holen" meldete „keine Bilder im Ordner", obwohl welche da waren.** Waren
  alle gefundenen Bilder bereits gewählt oder lagen auf der anderen Seite, ließ die
  Meldung den Fehler im Ordner vermuten statt in der eigenen Auswahl.
- **„Selbst auswählen" ließ sich mitten in einem laufenden Vorgang öffnen.** Der Dialog
  ist jetzt gesperrt, solange gelernt, analysiert oder sortiert wird.
- **Ein hereingezogener Eintrag, den das Dateisystem nicht annimmt, konnte die Auswahl
  abbrechen lassen.** Aus dem Explorer lässt sich alles hereinziehen; unbrauchbare
  Einträge werden jetzt gezählt und benannt statt durchgereicht.
- **Eine einzelne unlesbare Datei beendete den ganzen Durchlauf.** Zieht ein
  Virenscanner ein Foto während der Arbeit weg, ist es in einem Cloud-Ordner noch nicht
  heruntergeladen oder fehlen die Rechte, stand am Ende eine Fehlermeldung statt eines
  Ergebnisses — nach der vollen Wartezeit. Solche Fotos werden jetzt übersprungen und
  im Protokoll benannt, der Rest wird zu Ende bearbeitet.
- **Der Screenreader nannte mehrere Listen nur „Liste".** Das Protokollfeld, die
  Bereichsauswahl auf der Startseite, das Gedächtnis, die Beispielbilder und die
  Vorschläge haben jetzt einen sprechenden Namen.

## [1.4.6] - 2026-07-31

### Behoben

- **Beim Anlernen war nicht zu erkennen, ob noch etwas passiert.** Für jedes
  Beispielbild läuft ein vollständiger Aufruf der Bilderkennung; angezeigt wurde nur
  eine unveränderte Zeile. Jetzt steht dort „Bild 7 von 15" samt Balken.
- **Das Anlernen dauerte unnötig lange.** Die Bilderkennung beschreibt jedes Bild in
  Worten, und die Dauer hängt fast unmittelbar an der Länge dieser Beschreibung. Sie
  ist jetzt begrenzt — für den Vergleich genügen ein bis zwei Sätze, geschrieben wurden
  oft deutlich mehr.

### Geändert

- **Für die Beispiele gilt eine einstellbare Obergrenze, ab Werk fünfzehn je Seite.**
  Entschieden wird über die höchste Ähnlichkeit zu einem einzelnen Beispiel: Ein
  halbpassendes Beispiel zieht deshalb eine ganze Nachbarschaft falscher Fotos mit
  herein. Wenige eindeutige Beispiele wirken besser als viele mittelmäßige — und jedes
  zusätzliche kostet Wartezeit beim Anlernen.

## [1.4.5] - 2026-07-31

### Hinzugefügt

- **Beispielbilder lassen sich jetzt selbst bestimmen.** „Andere Beispiele" holt den
  nächsten Schwung aus dem Ordner, „Eigene Bilder wählen" öffnet einen Auswahldialog,
  und Bilder lassen sich auch direkt aus dem Explorer in die Auswahl ziehen — auch aus
  mehreren Ordnern nacheinander. Zuvor waren es immer dieselben ersten dreißig Bilder;
  bei einem gemischten Ordner war darunter oft kaum eines, das zum gesuchten Thema
  passte, und damit stand das ganze Anlernen auf einem einzigen Beispiel.

### Behoben

- **Bilder, die „passt nicht" markiert waren, hatten keinerlei Wirkung.** Sie wurden
  erfasst und gespeichert, bei der Sortierung aber nie herangezogen. Ein Foto, das
  einem Gegenbeispiel ähnlicher ist als jedem Beispiel, wird jetzt nicht mehr
  einsortiert. Das trennt vor allem die Fälle, die dem gesuchten Motiv nahe kommen,
  aber nicht gemeint sind.
- **Eine gescheiterte Aktualisierung verschwand spurlos.** Ließen sich die Dateien
  nicht ersetzen, startete das Programm einfach mit der alten Fassung neu — ohne
  Meldung, ohne Protokolleintrag. Der Grund wird jetzt festgehalten und nach dem
  Neustart angezeigt. Ist der Programmordner gar nicht beschreibbar (Installation
  „für alle Benutzer"), wird das vor dem Herunterladen erkannt und erklärt, statt
  hundert Megabyte umsonst zu laden.

## [1.4.4] - 2026-07-31

### Hinzugefügt

- **Die laufende Version steht jetzt dauerhaft im Fußbereich.** Bei einer Rückfrage
  war bisher nicht zu erkennen, welche Fassung gerade läuft.

### Behoben

- **„Jetzt aktualisieren" sah aus, als bewirke es nichts.** Die neue Fassung ist rund
  hundert Megabyte groß; während sie geladen wurde, blieb der Bildschirm unverändert.
  Jetzt zeigt der Fußbereich einen Balken mit Prozentangabe und benennt jeden Schritt
  — Laden, Echtheitsprüfung, Entpacken, Neustart. Abbrechen ist über „Stopp" möglich,
  und ein Fehlschlag wird gemeldet, statt still zu enden. Das gilt ebenso für die
  Aktualisierung über die Einstellungen und für die automatische beim Programmstart.
- **Beim Laden der Beispiele fehlte der Fortschrittsbalken.** Angezeigt wurde nur ein
  Text, der bei einem großen Ordner minutenlang unverändert stehenblieb und nicht von
  einem Absturz zu unterscheiden war. Der Vorgang lässt sich jetzt auch abbrechen.

## [1.4.3] - 2026-07-31

### Behoben

- **Das Laden der Beispiele dauerte bei großen Ordnern endlos.** Für die dreißig
  Bilder, die zur Auswahl stehen, wurde zuvor der gesamte Ordner eingelesen und erst
  danach abgeschnitten. Da für jedes Foto die Datei geöffnet werden muss, traf es
  Ordner besonders hart, deren Bilder erst aus der Cloud geholt werden — bei den
  iCloud-Fotos unter Windows lud die Anwendung so die halbe Mediathek herunter, um
  eine Handvoll Vorschauen zu zeigen. Jetzt werden nur noch so viele Bilder
  eingelesen, wie auch angezeigt werden. Das Sortieren und die Duplikat-Suche lesen
  weiterhin den ganzen Ordner — dort ist es ihre Aufgabe.
- **Der Hinweis auf eine neue Fassung konnte beim Start ausbleiben.** Die Suche nach
  Aktualisierungen lief erst, nachdem die Prüfung der lokalen KI fertig war. Antwortete
  die KI nicht, verzögerte das den Hinweis um deren gesamte Wartezeit; endete sie mit
  einem Fehler, blieb er ganz aus. Beide Prüfungen laufen jetzt unabhängig
  voneinander, und keine kann die andere mehr aufhalten oder verhindern.

## [1.4.2] - 2026-07-31

### Behoben

- **„Zustand der KI wird geprüft" blieb minutenlang oder dauerhaft stehen.** Die
  Erreichbarkeitsprüfung lief mit demselben Zeitlimit wie eine vollständige
  Bildbeschreibung und wurde zudem mehrfach wiederholt. Nahm die KI die Verbindung
  zwar an, antwortete aber nicht – etwa während sie selbst gerade aktualisiert wurde –,
  wartete die Anwendung über Minuten, ohne dass auf dem Bildschirm etwas darauf
  hindeutete. Die Prüfung hat jetzt ein eigenes, kurzes Zeitlimit und endet immer mit
  einer klaren Aussage statt im Ungewissen.
- **Eine laufende KI wurde auf manchen Rechnern nicht gefunden.** Die Anwendung sprach
  die KI über den Namen „localhost" an. Unter Windows führt dieser Weg zuerst über
  IPv6, während die KI nur über IPv4 lauscht; blockt eine Sicherheitssoftware den
  ersten Versuch stillschweigend, gilt eine einwandfrei laufende KI als nicht
  vorhanden. Angesprochen wird jetzt direkt die IPv4-Adresse.
- **„Jetzt einrichten" scheiterte in jeder ausgelieferten Fassung.** Das
  Einrichtungsskript für die KI wurde nie mitgeliefert: Es lag nur auf dem
  Entwicklungsrechner, weil die Vorgaben der Versionsverwaltung sämtliche Skripte
  ausschlossen. Auf dem Entwicklungsrechner war deshalb nichts zu bemerken, während
  jeder weitergegebene Stand nur „Die Einrichtung konnte nicht gestartet werden"
  meldete. Das Skript gehört jetzt dazu, und die Veröffentlichung bricht ab, wenn es
  einmal fehlen sollte. Die Ursache wird zusätzlich protokolliert, statt spurlos zu
  verschwinden.
- **Ein Proxy im System konnte die lokale KI unerreichbar machen.** Die Anfrage an den
  eigenen Rechner folgte den Proxy-Einstellungen von Windows. Setzt eine Sicherheits-
  oder VPN-Software dort einen Proxy ohne Ausnahme für lokale Adressen, lief sie ins
  Leere. Der Weg zur lokalen KI umgeht den Proxy jetzt grundsätzlich.

## [1.4.1] - 2026-07-31

### Behoben

- **Die ausgelieferte Fassung ließ sich nicht starten.** Der Veröffentlichungsschritt
  gab die eigene Ressourcendatei der Anwendung nicht mit aus. Ohne sie findet die
  Oberflächenbibliothek keine einzige Ansicht und die Anwendung bricht schon beim
  ersten Fenster ab. Betroffen war jedes weitergegebene Paket – Setup wie portable
  Fassung –, während die aus den Quellen gebaute Fassung einwandfrei lief; deshalb
  ist es so lange unbemerkt geblieben. Die Datei wird jetzt mit ausgegeben, und die
  Veröffentlichung bricht ab, wenn sie einmal fehlen sollte, statt ein Paket zu
  erzeugen, das sich nicht öffnen lässt.

## [1.4.0] - 2026-07-31

### Hinzugefügt

- **Kopieren statt Verschieben**: Direkt über dem Sortieren-Knopf lässt sich für den
  einzelnen Lauf wählen, ob die Fotos verschoben oder kopiert werden. Die
  Voreinstellung bleibt das Verschieben. Beim Kopieren bleiben die Originale im
  Quellordner liegen; das Rückgängigmachen entfernt dann die Kopien, statt Dateien
  zurückzuholen – und nur die Kopien, die seit dem Lauf unverändert geblieben sind.
  Eine nachträglich bearbeitete Kopie bleibt stehen und wird als übersprungen
  gemeldet.

### Behoben

- **Erstelldatum ging beim Sortieren über eine Laufwerksgrenze verloren.** Wurde aus
  einem Ordner auf `C:` in ein Archiv auf einem anderen Laufwerk sortiert, machte
  Windows aus dem Verschieben intern ein Kopieren mit anschließendem Löschen – die
  Zieldatei trug danach den heutigen Tag als Erstelldatum. Bei einer Fotosammlung ist
  genau dieses Datum die Ordnung. Das Datum wird jetzt in beiden Betriebsarten aus
  der Quelle übernommen. Innerhalb desselben Laufwerks bestand das Problem nicht.
  Aufnahmedaten in den Bildern selbst (EXIF) waren nie betroffen – sie stehen in den
  Dateibytes und überstehen beide Vorgänge unverändert.

## [1.3.1] - 2026-07-30

### Hinzugefügt

- **Automatische Sicherung vor Schemaänderungen**: Steht beim Start eine Migration
  an, legt die Anwendung vorher eine Kopie der Datenbank an
  (`picturesorter.vor-<Migration>.bak`) – über die Online-Backup-Schnittstelle von
  SQLite, damit auch noch nicht verdichtete Schreibvorgänge mitkommen. Eine
  vorhandene Sicherung wird nie überschrieben. Lässt sich nicht sichern, unterbleibt
  die Migration.
- **Protokoll-Ansicht mit Filter und Suche**: Der Bereich *Einstellungen →
  Protokoll* lässt sich auf Warnungen und Fehler eingrenzen und durchsuchen.
  Mehrzeilige Einträge bleiben dabei zusammen – eine Stapelüberwachung erscheint
  weder ohne ihre Fehlermeldung noch verschwindet sie mit ihr.
- **Betriebshandbuch** (`BETRIEB.md`): Ablageorte, Sicherung und
  Wiederherstellung, Rücknahme von Sortierlauf und Update, Schlüsselwechsel.

### Behoben

- **iPhone-Fotos (HEIC) wurden nicht wirklich angesehen**: Die Bilder wurden zwar
  gefunden und einsortiert, an die Bilderkennung ging aber die Originaldatei – und
  die kann sie nicht öffnen. Beurteilt wurde damit nichts, obwohl ein Urteil
  herauskam. Jeder Vorschlag beruhte auf einer Beschreibung ohne Bild. Fotos werden
  jetzt vor der Beurteilung umgewandelt und dabei auf eine sinnvolle Größe gebracht;
  das spart nebenbei Speicher und Wartezeit. Lässt sich ein Bild nicht öffnen – etwa
  weil unter Windows die HEIF-/HEVC-Erweiterung fehlt –, wird es übersprungen und
  nicht als beurteilt gemerkt, statt still falsch einsortiert zu werden.
- **Update: Eine misslungene Ersetzung ließ eine Datei beschädigt zurück**: Schlug
  das Ersetzen einer einzelnen Programmdatei fehl, wurden alle übrigen sauber
  zurückgeholt – ausgerechnet die betroffene nicht. Jetzt kommt auch sie zurück.
- **Update-Prüfung sagt jetzt, wenn sie nichts sagen kann**: Eine Prüfung ohne
  Netz oder ohne erreichbare Quelle wurde wie „Sie sind auf dem neuesten Stand"
  behandelt. Sie wird nun als solche gemeldet.
- **Kategorienamen, die Windows für Geräte hält** (`CON`, `NUL`, `COM1` …), führten
  beim Anlegen des Zielordners zu einer unverständlichen Fehlermeldung. Sie bekommen
  jetzt einen Zusatz und funktionieren.
- **Abgebrochener Sortierlauf war nicht mehr rücknehmbar**: Wurde das Verschieben
  mittendrin abgebrochen, lagen die bis dahin verschobenen Fotos im Zielordner und
  galten als einsortiert – protokolliert wurde der Lauf aber nie. „Rückgängig" bot
  für sie nichts an. Der Lauf wird jetzt auch bei Abbruch mit dem bereits
  Verschobenen protokolliert.
- **Beschädigte Datenbank beendete den Programmstart**: Ein SQLite-Fehler beim
  Initialisieren wurde nicht abgefangen. Die Anwendung läuft in diesem Fall jetzt
  ohne Sortier-Gedächtnis weiter und schreibt den Grund ins Protokoll.
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

- Der Inhalts-Hash der Duplikat-Suche liest die Datei jetzt strömend, statt sie am
  Stück in den Speicher zu holen. Bei tausenden großen Fotos sank damit der
  Speicherbedarf von „Größe der jeweiligen Datei" auf einen festen Puffer.
- Die Logik der Einstellungsseite liegt in einem `SettingsViewModel` und ist damit
  ohne WinUI-Laufzeit testbar; das Code-Behind reicht nur noch durch.
- Die App bringt die Windows App Runtime jetzt selbst mit (self-contained); sie
  startet damit auf Rechnern ohne vorinstallierte Runtime.
- Die Application-Schicht ist plattformneutral (`net10.0`).
- Die Einstellung `Ollama.MaxParallelRequests` ist entfallen. Sie stand in der
  Konfiguration, hatte aber keine Wirkung – die Bilder werden nacheinander bewertet.
- Update-Prüfung und -Installation der Einstellungsseite liegen im ViewModel und sind
  damit ohne laufende Oberfläche prüfbar.

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
