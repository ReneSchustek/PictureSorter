# analyse-auswerten.ps1 — sagt, wo die Zeit eines Analyselaufs hingeht.
#
# Warum es dieses Werkzeug gibt
# -----------------------------
# Seit Fassung 1.7.0 hält die Anwendung je Foto fest, auf welchem Weg entschieden wurde
# (Embedding oder Bild-Modell) und wann. Damit stecken die Antworten auf die Fragen des
# Briefs bereits in der Datenbank — nur konnte sie niemand herausholen, ohne SQL zu
# schreiben.
#
# Gemessen wird nichts hinzu: Das Werkzeug liest nur, was ein Lauf ohnehin geschrieben
# hat. Es darf deshalb auf jedem Rechner laufen, ohne dass dort etwas installiert oder
# verändert werden muss.
#
# Aufruf:
#   pwsh tools/analyse-auswerten.ps1 [-Datenbank <pfad>] [-Lauf <kennung>]
#
# Ohne Angaben nimmt es die Datenbank der laufenden Installation und den letzten Lauf.

param(
    [string]$Datenbank = "$env:LOCALAPPDATA\PictureSorter\picturesorter.db",
    [string]$Lauf = ""
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $Datenbank)) {
    Write-Output "Keine Datenbank unter $Datenbank."
    exit 2
}

# Die Anwendung hält die Datei offen, solange sie läuft. Gelesen wird deshalb aus einer
# Kopie — sonst scheitert der Zugriff oder, schlimmer, er stört den laufenden Lauf.
$kopie = Join-Path ([System.IO.Path]::GetTempPath()) ("picturesorter-messung-" + [Guid]::NewGuid().ToString('N') + ".db")
Copy-Item -LiteralPath $Datenbank -Destination $kopie -Force

try {
    # Die Bausteine kommen aus dem Ausgabeordner der Anwendung: Dort liegen die
    # verwaltete und die native Bibliothek beieinander. Aus dem NuGet-Zwischenspeicher
    # geladen fehlt die native Datei, und der Fehler nennt nur einen Typinitialisierer.
    $ausgabe = Get-ChildItem (Join-Path $PSScriptRoot '../src/PictureSorter.App/bin') -Recurse -Filter 'Microsoft.Data.Sqlite.dll' -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1

    if (-not $ausgabe) {
        Write-Output "Die Anwendung wurde hier noch nicht gebaut. Einmal 'dotnet build' genügt."
        exit 2
    }

    # Der Loader sucht die native Datei im Arbeitsverzeichnis.
    Push-Location $ausgabe.DirectoryName
    Add-Type -Path $ausgabe.FullName

    $verbindung = New-Object Microsoft.Data.Sqlite.SqliteConnection("Data Source=$kopie;Mode=ReadOnly")
    $verbindung.Open()

    function Frage($sql) {
        $befehl = $verbindung.CreateCommand()
        $befehl.CommandText = $sql
        $leser = $befehl.ExecuteReader()
        $zeilen = @()
        while ($leser.Read()) {
            $zeile = @{}
            for ($i = 0; $i -lt $leser.FieldCount; $i++) { $zeile[$leser.GetName($i)] = $leser.GetValue($i) }
            $zeilen += [pscustomobject]$zeile
        }
        $leser.Close()
        return $zeilen
    }

    $laufFilter = if ($Lauf) { "WHERE r.RunId = '$Lauf'" } else { "" }
    $laeufe = Frage "SELECT r.Id, r.RunId, r.CategoryName, r.StartedAtUtc, r.FinishedAtUtc, r.TotalPhotos, r.State FROM AnalysisRun r $laufFilter ORDER BY r.StartedAtUtc DESC LIMIT 1"

    if ($laeufe.Count -eq 0) {
        Write-Output "Kein protokollierter Lauf gefunden. Erst einen Lauf über echte Fotos fahren."
        exit 1
    }

    $l = $laeufe[0]
    Write-Output "Lauf $($l.RunId)  Kategorie '$($l.CategoryName)'  Zustand $($l.State)"
    Write-Output "  Fotos gesamt laut Kopf: $($l.TotalPhotos)"

    # Wie viele Bilder landen im Grenzbereich? Nur sie gehen ans
    # Bild-Modell. Die Werte von ClassificationMethod: 0 Embedding, 1 Bild-Modell.
    $verfahren = Frage "SELECT Method, COUNT(*) AS Anzahl FROM AnalysisRunItem WHERE AnalysisRunId = $($l.Id) GROUP BY Method"
    Write-Output ""
    Write-Output "Verfahren je Foto:"
    $gesamt = 0
    foreach ($v in $verfahren) { $gesamt += [int]$v.Anzahl }
    foreach ($v in $verfahren) {
        $anteil = if ($gesamt -gt 0) { 100.0 * [int]$v.Anzahl / $gesamt } else { 0 }
        Write-Output ("  Methode {0}: {1} ({2:N1} %)" -f $v.Method, $v.Anzahl, $anteil)
    }

    # Wo geht die Zeit hin? Die Abstände zwischen den Entscheidungen sagen es —
    # getrennt nach Verfahren, sonst mittelt sich der teure Fall unter den billigen weg.
    $zeiten = Frage @"
SELECT Method,
       COUNT(*) AS Anzahl,
       AVG(Abstand) AS Mittel,
       MAX(Abstand) AS Largest
FROM (
  SELECT Method,
         (julianday(DecidedAtUtc) - julianday(LAG(DecidedAtUtc) OVER (ORDER BY DecidedAtUtc))) * 86400.0 AS Abstand
  FROM AnalysisRunItem WHERE AnalysisRunId = $($l.Id)
)
WHERE Abstand IS NOT NULL
GROUP BY Method
"@
    Write-Output ""
    Write-Output "Sekunden je Foto (Abstand der Entscheidungen):"
    foreach ($z in $zeiten) {
        Write-Output ("  Methode {0}: {1:N2} s im Mittel, größter Abstand {2:N1} s ({3} Fotos)" -f $z.Method, $z.Mittel, $z.Largest, $z.Anzahl)
    }

    # Wie viele Anfragen laufen ins Zeitlimit? Ein Foto, das dabei aufgegeben
    # wird, steht als 'NotEvaluated' im Protokoll.
    $aufgegeben = Frage "SELECT COUNT(*) AS Anzahl FROM AnalysisRunItem WHERE AnalysisRunId = $($l.Id) AND Outcome = 4"
    Write-Output ""
    Write-Output "Aufgegeben (Zeitlimit oder Fehler): $($aufgegeben[0].Anzahl)"

    Write-Output ""
    Write-Output "Ob Ollama die Grafikeinheit nutzt, zeigt 'ollama ps'."
}
finally {
    if ($verbindung) { $verbindung.Close() }
    Remove-Item -LiteralPath $kopie -Force -ErrorAction SilentlyContinue
}
