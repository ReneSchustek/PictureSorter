---
tags: anleitung, dotnet, oberfläche
---

# Bildschirmfotos

Sieben Ansichten der Anwendung, je einmal im hellen und im dunklen Erscheinungsbild.
Aufgenommen an einem Demo-Bestand: vierzehn gezeichnete Bilder mit Aufnahmedatum,
darunter zwei Paare, die sich gleichen — und daneben ein zweiter, kleiner Bestand mit
vier unlesbaren Dateinamen, wie ihn ein iCloud-Export mitbringt.

| Datei | Was zu sehen ist |
|---|---|
| `01-start.png` | Startseite mit dem Zustand der lokalen Bilderkennung |
| `02-nach-datum-ablegen.png` | Ablage nach Aufnahmedatum: Ordner, Stufe und die Vorschau mit Zielordnern |
| `03-sortieren.png` | Der Sortier-Assistent im letzten Schritt: Vorschläge samt Trefferquote |
| `04-duplikate.png` | Fundgruppen mit Bildkacheln, Suche und Filter |
| `05-detailansicht.png` | Detailansicht eines Bildes samt Angaben, Doppelgängern und Bearbeiten |
| `06-gedaechtnis.png` | Was sich die Anwendung gemerkt hat, mit Ordner- und Kategorie-Filter |
| `07-namen-richten.png` | Unlesbare Dateinamen mit ihren Vorschlägen — aus dem Aufnahmedatum oder aus Ordner und Nummer |

Die dunkle Reihe trägt dieselben Namen mit dem Zusatz `-dunkel`.

Die Pfade in den Bildern sind neutral gehalten (`F:\Fotos`) — auf einem Bildschirmfoto
für eine Webseite hat kein Benutzername etwas verloren.

## Neu aufnehmen

Nach jeder Fassung ändert sich das Aussehen. Die ganze Serie entsteht mit einem Aufruf
neu:

```powershell
./tools/bildschirmfotos.ps1
```

Das Werkzeug baut die Anwendung, erzeugt beide Demo-Bestände über `tools/demobilder.ps1`,
geht jede Ansicht an und legt die vierzehn Bilder hier ab. Es dauert einige Minuten, weil
für die Sortier-Ansicht wirklich eine Analyse läuft — jedes Foto wird der Bilderkennung
vorgelegt. Ollama muss dafür laufen.

Der Rechner bleibt währenddessen benutzbar: Aufgenommen wird über `PrintWindow` mit dem
Kennwert `PW_RENDERFULLCONTENT`, und der zeichnet das Fenster auch aus dem Hintergrund.
Der naheliegende Weg über `CopyFromScreen` kann das nicht — er liefert bei WinUI eine
weiße Fläche, solange das Fenster nicht vorn liegt, und fotografiert sonst mit, was
gerade darüber steht.

Die Anwendung darf beim Start des Werkzeugs nicht laufen. Ihr Datenordner im
Benutzerprofil wird für die Aufnahme beiseitegelegt und danach zurückgeholt, damit auf
keinem Bild ein privat sortierter Ordner auftaucht.

Aufgenommen mit Fassung 1.10.1 bei 1386 × 993 Punkten.
