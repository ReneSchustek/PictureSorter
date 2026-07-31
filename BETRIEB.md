# Betriebshandbuch

Was im Betrieb zu tun ist, wenn etwas schiefgeht: wo die Daten liegen, wie man sie
sichert, wie man einen fehlgeschlagenen Sortierlauf oder ein misslungenes Update
zurücknimmt.

Zielgruppe ist, wer die Anwendung betreut – nicht die Nutzerin selbst. Für den
normalen Gebrauch genügt die [README](README.md).

---

## 1. Wo liegt was

Alle veränderlichen Daten liegen im Benutzerprofil, nicht im Programmordner. Die
Anwendung braucht deshalb keine Administratorrechte.

| Inhalt | Ort |
|---|---|
| Datenbank (Sortier-Gedächtnis, Protokoll der Sortierläufe) | `%LOCALAPPDATA%\PictureSorter\picturesorter.db` |
| Sicherungen vor einer Migration | `%LOCALAPPDATA%\PictureSorter\picturesorter.vor-<Migration>.bak` |
| Tägliche Protokolldateien | `%LOCALAPPDATA%\PictureSorter\logs\picturesorter-JJJJ-MM-TT.log` |
| Einstellungen (Design, Update-Verhalten) | `%LOCALAPPDATA%\PictureSorter\ui-settings.json` |
| Zwischenstand eines vorbereiteten Updates | `%LOCALAPPDATA%\PictureSorter\pending-update.json` |
| Embedding-Zwischenspeicher | `%LOCALAPPDATA%\PictureSorter\embedding-cache.jsonl` |

Die Fotos selbst berührt die Anwendung nur an einer Stelle: beim Anwenden eines
Sortiervorschlags. Alles andere ist lesend.

---

## 2. Zustand feststellen

**Erste Anlaufstelle ist die Anwendung selbst:** *Einstellungen → Protokoll
(Fehlerdiagnose)*. Der Schalter „Nur Warnungen und Fehler" blendet die
Routinemeldungen aus, das Suchfeld findet einen Begriff auch in der
Stapelüberwachung eines Fehlers.

Für die Ursachensuche außerhalb der Anwendung: *Log-Ordner öffnen*. Die
Protokolldateien sind reiner Text, eine je Tag, 30 Tage lang aufbewahrt.

Der Aufbau einer Zeile:

```
2026-07-26 10:00:02.123 [ERROR] PictureSorter.Sorting (3006) => Sortieren 4f2c: Meldung
└ Zeitstempel          └ Stufe └ Kategorie        └ Ereignis └ Vorgang
```

Die Vorgangskennung („Sortieren 4f2c") verbindet alle Zeilen eines Laufs – danach
suchen, wenn man einen einzelnen Vorgang nachvollziehen will.

Pfade sind im Protokoll gekürzt (`…\Fotos\Urlaub`), damit keine Benutzernamen in
weitergegebenen Dateien landen.

---

## 3. Daten sichern

Die Datenbank ist die einzige Datei, deren Verlust weh tut: In ihr stehen das
Sortier-Gedächtnis und das Protokoll der Sortierläufe – die Grundlage des
Rückgängigmachens.

**Vor einer Schemaänderung sichert die Anwendung selbst.** Beim Start prüft sie, ob
eine Migration ansteht; ist das der Fall, legt sie vorher
`picturesorter.vor-<Migration>.bak` an. Eine bereits vorhandene Sicherung wird nie
überschrieben – nach einem Fehlversuch bleibt so die unbeschädigte Fassung erhalten.
Lässt sich nicht sichern, unterbleibt die Migration: Die Anwendung läuft dann ohne
Gedächtnis weiter und schreibt den Grund ins Protokoll.

**Sicherung von Hand** (Anwendung vorher beenden):

```powershell
Copy-Item "$env:LOCALAPPDATA\PictureSorter\picturesorter.db" `
          "$env:LOCALAPPDATA\PictureSorter\picturesorter.manuell.bak"
```

**Eine Sicherung zurückspielen** (Anwendung beenden, dann):

```powershell
$ordner = "$env:LOCALAPPDATA\PictureSorter"
Remove-Item "$ordner\picturesorter.db-wal","$ordner\picturesorter.db-shm" -ErrorAction SilentlyContinue
Copy-Item "$ordner\picturesorter.vor-<Migration>.bak" "$ordner\picturesorter.db" -Force
```

Die Dateien `-wal` und `-shm` müssen weg: Sie gehören zur ersetzten Datenbank und
würden sonst auf einen Stand zeigen, den es nicht mehr gibt.

**Notfall ohne Sicherung:** Die Datenbank löschen. Die Anwendung legt sie beim
nächsten Start neu an. Verloren gehen dabei das Gedächtnis (bereits einsortierte
Fotos werden erneut vorgeschlagen) und die Möglichkeit, ältere Läufe
zurückzunehmen. **Fotos gehen dabei nicht verloren** – sie liegen im Dateisystem.

---

## 4. Einen Sortierlauf zurücknehmen

Der reguläre Weg führt über die Anwendung: *Fotos sortieren* → Hinweisleiste oben →
**Rückgängig**. Der Hinweis erscheint auch nach einem Neustart, solange der Lauf
noch nicht zurückgenommen wurde.

Was dabei gilt:

* Zurückgeholt wird nur, was noch dort liegt, wo der Lauf es hingelegt hat.
* Am Ursprungsort wird **nie** überschrieben. Liegt dort wieder eine Datei, bleibt
  das Foto im Kategorie-Ordner und wird als „übersprungen" gemeldet.
* Leere Kategorie-Ordner räumt die Anwendung anschließend weg.
* Auch ein **abgebrochener** Lauf ist rücknehmbar: Was bis zum Abbruch verschoben
  wurde, steht im Protokoll.

Ein Lauf gilt nach dem Rückgängigmachen als erledigt – auch wenn einzelne Dateien
nicht zurückkonnten. Ein zweiter Versuch würde an denselben Hindernissen scheitern.

**Wenn das Rückgängigmachen nicht angeboten wird**, obwohl sortiert wurde: Im
Protokoll nach Ereignis `3700` suchen („Der Sortierlauf … konnte nicht protokolliert
werden"). Dann ist die Datenbank beim Schreiben nicht erreichbar gewesen; die Fotos
liegen im Kategorie-Ordner und müssen von Hand zurückgeschoben werden.

---

## 5. Update zurücknehmen

Die Anwendung ersetzt sich beim Update selbst: Sie lädt das Paket, prüft dessen
Signatur, entpackt es und startet sich mit `--apply-update` neu, um die
Programmdateien zu tauschen.

* **Während des Tauschs** wird jede Datei vorher gesichert (`*.bak-update`).
  Scheitert eine, rollt der Vorgang alles Bisherige zurück – eine halb ersetzte
  Installation entsteht nicht.
* **Bleiben `.bak-update`-Dateien im Programmordner liegen**, wurde der Tausch
  unterbrochen. Sie enthalten den vorherigen Stand: Datei ohne die Endung
  wiederherstellen, dann die Sicherung löschen.
* **Auf eine ältere Fassung zurück**: Das Setup der gewünschten Version erneut
  ausführen. Die Daten im Benutzerprofil bleiben unangetastet.

Ein Paket ohne gültige Signatur wird abgelehnt (fail-closed). Das gilt auch für
unsignierte ältere Releases – sie lassen sich nicht über die Update-Funktion
einspielen, sondern nur über ihr Setup.

**Schlüsselwechsel:** Der öffentliche Schlüssel ist in die Anwendung einkompiliert.
Ein Wechsel des Signaturschlüssels macht neue Releases für alte Installationen
unbrauchbar – sie lehnen die neue Signatur ab und melden das im Protokoll. Ein
Wechsel erfordert deshalb eine Zwischenfassung, die beide Schlüssel akzeptiert,
bevor der alte abgeschaltet wird. Der private Schlüssel liegt außerhalb des
Repositories; in der CI als Secret `PICTURESORTER_SIGNING_KEY`.

---

## 6. Wenn die lokale KI nicht läuft

Ohne Ollama sortiert die Anwendung nicht – sie stürzt aber auch nicht ab, sondern
meldet den Zustand auf der Startseite und unter *Einstellungen → Lokale KI*.

| Meldung | Bedeutung | Abhilfe |
|---|---|---|
| „nicht eingerichtet" | Ollama antwortet nicht auf `127.0.0.1:11434` | Ollama installieren/starten, dann *Status prüfen* |
| „Modelle fehlen" | Ollama läuft, die benannten Modelle fehlen | *Jetzt einrichten* – lädt sie nach (dauert beim ersten Mal) |

Fällt die KI **mitten im Lauf** aus, wird das betroffene Foto übersprungen und der
Lauf fortgesetzt. Das Foto wird dabei bewusst nicht als „passt nicht" gemerkt – der
Ausfall ist kein Urteil über das Bild, und der nächste Lauf versucht es erneut.

---

## 7. Regelmäßige Pflege

Es gibt keine Pflichtaufgaben. Die Anwendung räumt selbst auf:

* Protokolldateien älter als 30 Tage werden beim Start gelöscht.
* Eine Tagesdatei über 100 MB wird einmalig zur Seite gelegt (`.log.1`).
* Der Embedding-Zwischenspeicher wird beim Schreiben verdichtet.

Was **nicht** automatisch verschwindet: die Sicherungen vor Migrationen. Sie sind
klein und bleiben absichtlich liegen. Wer aufräumen will, löscht sie, sobald die
neue Fassung im Alltag bestätigt ist.
