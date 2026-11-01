# PictureSorter.Benchmarks

Misst die Wege, die bei einem großen Fotobestand ohne KI Zeit kosten: das Erkennen eines Fotos am
Inhalt und den Vektor-Zwischenspeicher. Gemessen wird mit BenchmarkDotNet über die echten Klassen.

Das Projekt ist kein Testprojekt. `dotnet test` führt es nicht aus, weil ein Lauf Minuten dauert und
eine ruhige Maschine braucht. Die Solution baut es mit, damit es nicht veraltet.

## Starten

```powershell
dotnet run -c Release --project tests/PictureSorter.Benchmarks -- --filter * --artifacts <ordner>
```

Während einer Messung sollte nichts anderes rechnen; ein paralleler Build verfälscht die Zahlen.

## Ergebnisse

Die Ergebnisse gehören nicht ins Repository, denn sie hängen am Rechner. Erst die Reihe mehrerer
Läufe auf demselben Rechner zeigt, ob etwas langsamer wird.

## Nicht gemessen

Die Antwortzeit der Bildmodelle hängt an Ollama und am Rechner, nicht am eigenen Code. Sie steht als
Tabelle bei der Modellauswahl und wird auf dem langsamsten Zielrechner erhoben.

## Wächter im normalen Testlauf

`FileFingerprinterTests.Compute_ForAVeryLargeFile_StaysFastBecauseItOnlySamples` prüft grob, dass die
Dauer des Inhaltsschlüssels nicht mit der Dateigröße wächst.
