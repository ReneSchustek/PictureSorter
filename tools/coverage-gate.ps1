<#
.SYNOPSIS
    Wertet die Coverage-Berichte aller Testprojekte aus und bricht ab, wenn die
    Zeilenabdeckung unter den Schwellwert fällt.

.DESCRIPTION
    Jedes Testprojekt schreibt einen eigenen Cobertura-Bericht, und mehrere davon
    decken dieselben Dateien ab (etwa die Core-Schicht). Ein blosses Aufsummieren
    zaehlte diese Zeilen doppelt und beschoenigte das Ergebnis. Das Skript
    vereinigt die Berichte deshalb je Datei und Zeile: Eine Zeile gilt als
    abgedeckt, sobald irgendein Testprojekt sie erreicht.

    Der Schwellwert sichert den erreichten Stand ab; er ist keine Zielmarke. Sinkt
    die Abdeckung darunter, wurde ungetesteter Code hinzugefuegt.

    Nicht gemessen wird die App-Schicht (WinUI): Ihr Testprojekt laeuft als
    self-contained x64-Testhost, den der Coverage-Kollektor nicht instrumentieren
    kann. Ihre ViewModels sind getestet, tauchen in dieser Zahl aber nicht auf. Das
    Skript sagt das bei jedem Lauf dazu, damit die Zahl nicht mehr verspricht, als
    sie deckt.

.PARAMETER ResultsDirectory
    Verzeichnis mit den Cobertura-Berichten (rekursiv).

.PARAMETER MinimumLineCoverage
    Geforderte Zeilenabdeckung in Prozent.
#>
[CmdletBinding()]
param(
    [string] $ResultsDirectory = 'TestResults',
    [double] $MinimumLineCoverage = 75.0
)

$ErrorActionPreference = 'Stop'

$reports = @(Get-ChildItem -Path $ResultsDirectory -Filter 'coverage.cobertura.xml' -Recurse -ErrorAction SilentlyContinue)
if ($reports.Count -eq 0) {
    Write-Error "Keine Coverage-Berichte unter '$ResultsDirectory' gefunden."
}

# Schluessel: "Datei|Zeile" -> wurde die Zeile von irgendeinem Testprojekt erreicht?
$lines = @{}
# Zuordnung Datei -> Assembly, damit die Ausgabe je Schicht aufschluesseln kann.
$assemblyOfFile = @{}

foreach ($report in $reports) {
    [xml] $xml = Get-Content -LiteralPath $report.FullName -Raw

    foreach ($package in $xml.coverage.packages.package) {
        foreach ($class in $package.classes.class) {
            $file = $class.filename
            if (-not $file) { continue }

            $assemblyOfFile[$file] = $package.name

            foreach ($line in $class.lines.line) {
                $key = "$file|$($line.number)"
                $hit = [int] $line.hits -gt 0
                if (-not $lines.ContainsKey($key)) {
                    $lines[$key] = $hit
                }
                elseif ($hit) {
                    $lines[$key] = $true
                }
            }
        }
    }
}

if ($lines.Count -eq 0) {
    Write-Error 'Die Coverage-Berichte enthalten keine Zeilen.'
}

# Je Assembly aufschluesseln.
$perAssembly = @{}
foreach ($entry in $lines.GetEnumerator()) {
    $file = $entry.Key.Split('|')[0]
    $assembly = $assemblyOfFile[$file]
    if (-not $perAssembly.ContainsKey($assembly)) {
        $perAssembly[$assembly] = @{ Total = 0; Covered = 0 }
    }

    $perAssembly[$assembly].Total++
    if ($entry.Value) { $perAssembly[$assembly].Covered++ }
}

$total = $lines.Count
$covered = @($lines.Values | Where-Object { $_ }).Count
$coverage = [math]::Round(100.0 * $covered / $total, 1)

Write-Output 'Zeilenabdeckung je Schicht:'
foreach ($name in ($perAssembly.Keys | Sort-Object)) {
    $stats = $perAssembly[$name]
    $percent = [math]::Round(100.0 * $stats.Covered / $stats.Total, 1)
    Write-Output ("  {0,-32} {1,5:N1} %  ({2}/{3})" -f $name, $percent, $stats.Covered, $stats.Total)
}

Write-Output ''
Write-Output ("Gesamt: {0} % ({1}/{2} Zeilen), gefordert: {3} %" -f $coverage, $covered, $total, $MinimumLineCoverage)
Write-Output 'Nicht enthalten: PictureSorter.App (WinUI) – der self-contained Testhost laesst sich nicht instrumentieren; die ViewModels sind getestet, zaehlen hier aber nicht mit.'

if ($coverage -lt $MinimumLineCoverage) {
    Write-Output ''
    Write-Error ("Die Zeilenabdeckung liegt mit {0} % unter den geforderten {1} %." -f $coverage, $MinimumLineCoverage)
    exit 1
}

Write-Output 'Coverage-Gate bestanden.'
