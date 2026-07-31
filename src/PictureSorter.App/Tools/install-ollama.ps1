<#
    PictureSorter – Einrichtung der lokalen KI (Ollama)

    Dieses Skript installiert Ollama und lädt die beiden benötigten KI-Modelle
    herunter. Alles läuft lokal auf diesem PC; es werden keine Fotos ins Internet
    geladen. Der Download der Modelle kann je nach Internetverbindung einige
    Minuten dauern (mehrere Gigabyte).

    Du kannst dieses Skript einfach per Doppelklick starten oder in PictureSorter
    auf „Jetzt einrichten" klicken.
#>

$ErrorActionPreference = 'Stop'

# Diese beiden Modelle benötigt PictureSorter (siehe appsettings.json).
$models = @('nomic-embed-text', 'llava')

function Write-Step($text) {
    Write-Host ''
    Write-Host "==> $text" -ForegroundColor Cyan
}

function Find-Ollama {
    $cmd = Get-Command ollama -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    $candidate = Join-Path $env:LOCALAPPDATA 'Programs\Ollama\ollama.exe'
    if (Test-Path $candidate) { return $candidate }
    return $null
}

try {
    Write-Host '======================================================' -ForegroundColor White
    Write-Host '  PictureSorter – Einrichtung der lokalen KI (Ollama)' -ForegroundColor White
    Write-Host '======================================================' -ForegroundColor White

    # 1) Ollama installieren (falls noch nicht vorhanden)
    Write-Step 'Prüfe, ob Ollama installiert ist…'
    $ollama = Find-Ollama
    if (-not $ollama) {
        Write-Host 'Ollama ist noch nicht installiert. Es wird jetzt über winget installiert.'
        Write-Host 'Bitte bestätige eventuelle Windows-Sicherheitsabfragen mit „Ja".'

        if (-not (Get-Command winget -ErrorAction SilentlyContinue)) {
            Write-Host ''
            Write-Host 'FEHLER: „winget" ist auf diesem PC nicht verfügbar.' -ForegroundColor Red
            Write-Host 'Bitte installiere Ollama einmalig manuell von https://ollama.com/download'
            Write-Host 'und starte dieses Skript danach erneut.'
            Read-Host 'Zum Beenden Enter drücken'
            exit 1
        }

        winget install --exact --id Ollama.Ollama --accept-source-agreements --accept-package-agreements
        Write-Host 'Ollama wurde installiert.' -ForegroundColor Green

        # PATH der aktuellen Sitzung aktualisieren, damit „ollama" gefunden wird.
        $env:Path = [System.Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' +
                    [System.Environment]::GetEnvironmentVariable('Path', 'User')
        $ollama = Find-Ollama
    }
    else {
        Write-Host "Ollama ist bereits installiert: $ollama" -ForegroundColor Green
    }

    if (-not $ollama) {
        Write-Host ''
        Write-Host 'FEHLER: Ollama wurde installiert, konnte aber nicht gefunden werden.' -ForegroundColor Red
        Write-Host 'Bitte starte den PC neu und führe dieses Skript erneut aus.'
        Read-Host 'Zum Beenden Enter drücken'
        exit 1
    }

    # 2) Ollama-Dienst starten (lädt im Hintergrund, ignorieren falls bereits aktiv)
    Write-Step 'Starte den Ollama-Dienst…'
    try { Start-Process -FilePath $ollama -ArgumentList 'serve' -WindowStyle Hidden } catch { }
    Start-Sleep -Seconds 3

    # 3) Modelle herunterladen
    foreach ($model in $models) {
        Write-Step "Lade KI-Modell „$model" herunter (kann einige Minuten dauern)…"
        & $ollama pull $model
        Write-Host "Modell „$model" ist bereit." -ForegroundColor Green
    }

    Write-Host ''
    Write-Host '======================================================' -ForegroundColor Green
    Write-Host '  Fertig! Die KI ist eingerichtet.' -ForegroundColor Green
    Write-Host '  Wechsle zu PictureSorter und klicke auf „Erneut prüfen".' -ForegroundColor Green
    Write-Host '======================================================' -ForegroundColor Green
}
catch {
    Write-Host ''
    Write-Host "FEHLER bei der Einrichtung: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host 'Bitte versuche es erneut oder installiere Ollama manuell von https://ollama.com/download'
}
finally {
    Write-Host ''
    Read-Host 'Zum Schließen dieses Fensters Enter drücken'
}
