<#
    PictureSorter - Einrichtung der lokalen KI (Ollama)

    Dieses Skript installiert Ollama und lädt die benötigten KI-Modelle herunter.
    Alles läuft lokal auf diesem PC; es werden keine Fotos ins Internet geladen. Der
    Download der Modelle kann je nach Internetverbindung einige Minuten dauern
    (mehrere Gigabyte).

    Gestartet wird es in PictureSorter über "Jetzt einrichten".

    Die Datei muss als UTF-8 mit BOM gespeichert bleiben: Windows PowerShell 5.1 liest
    eine Datei ohne BOM als Windows-1252. Dann zerfallen nicht nur die Umlaute,
    typografische Anführungszeichen verschieben auch die Stringgrenzen, und Teile einer
    Meldung werden als Befehl ausgeführt. Solche Anführungszeichen gehören deshalb gar
    nicht in diese Datei. Beides sichert ein Test ab.
#>
param(
    # Ersetzt die Suche nach Ollama, damit sich das Skript ohne echte Installation
    # prüfen lässt.
    [string]$OllamaPath = '',

    [int]$ReadyTimeoutSeconds = 60,

    # Ohne wartende Eingabe am Ende, sonst hinge ein automatischer Lauf für immer.
    [switch]$NoPause
)

$ErrorActionPreference = 'Stop'

# ollama pull schreibt seinen Fortschritt als UTF-8. Ohne diese Einstellung zeigt die
# Konsole statt der Balken Zeichensalat.
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

function Write-Step($text) {
    Write-Host ''
    Write-Host "==> $text" -ForegroundColor Cyan
}

function Find-Ollama {
    if ($OllamaPath) { return $OllamaPath }
    $cmd = Get-Command ollama -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    $candidate = Join-Path $env:LOCALAPPDATA 'Programs\Ollama\ollama.exe'
    if (Test-Path $candidate) { return $candidate }
    return $null
}

function Install-Ollama {
    Write-Host 'Ollama ist noch nicht installiert. Es wird jetzt über winget installiert.'
    Write-Host 'Bitte bestätige eventuelle Windows-Sicherheitsabfragen mit "Ja".'

    if (-not (Get-Command winget -ErrorAction SilentlyContinue)) {
        throw 'Das Programm "winget" ist auf diesem PC nicht verfügbar.'
    }

    winget install --exact --id Ollama.Ollama --accept-source-agreements --accept-package-agreements
    $wingetExitCode = $LASTEXITCODE

    # Der Installer trägt Ollama in den PATH ein, die laufende Sitzung sieht das aber nicht.
    $env:Path = [System.Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' +
                [System.Environment]::GetEnvironmentVariable('Path', 'User')

    # Entscheidend ist, ob Ollama danach da ist, nicht der Rückgabewert: winget meldet
    # auch für "bereits installiert" einen Wert ungleich null.
    $found = Find-Ollama
    if (-not $found) {
        throw "Die Installation hat Ollama nicht bereitgestellt (winget meldete $wingetExitCode). Ein Neustart des PCs kann helfen."
    }

    Write-Host 'Ollama wurde installiert.' -ForegroundColor Green
    return $found
}

function Test-OllamaReady($ollama) {
    # Ein laufendes Ollama beantwortet "list" mit 0. Antwortet es nicht, schreibt der
    # Aufruf nach stderr, und unter 'Stop' würde die Umleitung daraus einen Abbruch machen.
    $ErrorActionPreference = 'Continue'
    & $ollama list *> $null
    return ($LASTEXITCODE -eq 0)
}

function Start-OllamaService($ollama) {
    if (Test-OllamaReady $ollama) {
        Write-Host 'Der Ollama-Dienst läuft bereits.' -ForegroundColor Green
        return
    }

    Start-Process -FilePath $ollama -ArgumentList 'serve' -WindowStyle Hidden

    $deadline = (Get-Date).AddSeconds($ReadyTimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        if (Test-OllamaReady $ollama) {
            Write-Host 'Der Ollama-Dienst läuft.' -ForegroundColor Green
            return
        }
        Start-Sleep -Seconds 1
    }

    throw "Der Ollama-Dienst antwortet auch nach $ReadyTimeoutSeconds Sekunden nicht."
}

# Die Namen stehen nur in der appsettings.json. Eine zweite Liste hier liefe früher oder
# später auseinander: Die Einrichtung meldete dann "fertig", und die App vermisste trotzdem
# ein Modell.
function Get-RequiredModels {
    $settingsPath = Join-Path $PSScriptRoot '..\appsettings.json'
    $settings = Get-Content -LiteralPath $settingsPath -Raw -Encoding UTF8 | ConvertFrom-Json
    return @($settings.Ollama.EmbeddingModel, $settings.Ollama.VisionModel)
}

function Save-Models($ollama, $models) {
    $failed = @()
    foreach ($model in $models) {
        Write-Step "Lade KI-Modell '$model' herunter (kann einige Minuten dauern)…"
        & $ollama pull $model

        # $ErrorActionPreference greift bei externen Programmen nicht, nur der Rückgabewert
        # verrät einen Fehlschlag.
        if ($LASTEXITCODE -eq 0) {
            Write-Host "Modell '$model' ist bereit." -ForegroundColor Green
        }
        else {
            Write-Host "Modell '$model' konnte nicht geladen werden (Rückgabewert $LASTEXITCODE)." -ForegroundColor Red
            $failed += $model
        }
    }

    if ($failed.Count -gt 0) {
        throw "Nicht geladen: $($failed -join ', '). Prüfe die Internetverbindung und starte die Einrichtung erneut."
    }
}

$exitCode = 0
try {
    Write-Host '======================================================' -ForegroundColor White
    Write-Host '  PictureSorter - Einrichtung der lokalen KI (Ollama)' -ForegroundColor White
    Write-Host '======================================================' -ForegroundColor White

    Write-Step 'Prüfe, ob Ollama installiert ist…'
    $ollama = Find-Ollama
    if ($ollama) {
        Write-Host "Ollama ist bereits installiert: $ollama" -ForegroundColor Green
    }
    else {
        $ollama = Install-Ollama
    }

    Write-Step 'Starte den Ollama-Dienst…'
    Start-OllamaService $ollama

    Save-Models $ollama (Get-RequiredModels)

    Write-Host ''
    Write-Host '======================================================' -ForegroundColor Green
    Write-Host '  Fertig! Die KI ist eingerichtet.' -ForegroundColor Green
    Write-Host '  Wechsle zu PictureSorter und klicke auf "Status prüfen".' -ForegroundColor Green
    Write-Host '======================================================' -ForegroundColor Green
}
catch {
    $exitCode = 1
    Write-Host ''
    Write-Host "FEHLER bei der Einrichtung: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host 'Bitte versuche es erneut oder installiere Ollama manuell von https://ollama.com/download'
}

if (-not $NoPause) {
    Write-Host ''
    Read-Host 'Zum Schließen dieses Fensters Enter drücken' | Out-Null
}

exit $exitCode
