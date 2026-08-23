<#
.SYNOPSIS
    Nimmt die Bildschirmfotos für die Webseite auf - die ganze Serie mit einem Aufruf.

.DESCRIPTION
    Erzeugt einen Demo-Bestand, startet die Anwendung, geht jede Ansicht an und legt
    die Bilder unter docs\bilder ab. Zweimal: einmal hell, einmal dunkel.

    Warum ein Werkzeug und keine Handarbeit: Nach jeder Fassung sehen die Bilder
    anders aus, und eine von Hand aufgenommene Serie ist beim nächsten Mal nicht
    wieder herzustellen. Hier genügt ein Aufruf.

    Drei Dinge, an denen der naheliegende Weg scheitert:

    * Ein Bildabzug über CopyFromScreen liefert bei WinUI eine weiße Fläche, solange
      das Fenster nicht wirklich im Vordergrund liegt. PrintWindow mit dem Kennwert
      PW_RENDERFULLCONTENT dagegen zeichnet das Fenster aus dem Hintergrund - der
      Rechner bleibt währenddessen benutzbar, und nichts Fremdes gerät ins Bild.
    * Der Fensterbereich aus GetWindowRect enthält den unsichtbaren Griffrand.
      Zugeschnitten wird deshalb auf die Maße, die der Fensterverwalter meldet.
    * Ein leeres Bild sieht wie ein richtiges aus. Jede Aufnahme wird deshalb
      nachgezählt: Wie viele verschiedene Farben stecken darin? Bei einer weißen
      Fläche ist es genau eine.

    Die Anwendung legt ihre Daten im Benutzerprofil ab. Damit auf keinem Bild ein
    fremder Ordner auftaucht, wird dieser Datenordner vor dem Lauf beiseitegelegt und
    danach zurückgeholt - auch dann, wenn der Lauf mittendrin scheitert.

.PARAMETER Ordner
    Ordner des Demo-Bestands. Der Pfad steht auf den Bildern; er ist bewusst neutral.

.PARAMETER NamensOrdner
    Der zweite, kleine Bestand mit unlesbaren Dateinamen. Getrennt vom Hauptbestand,
    weil solcher Zeichensalat auf den übrigen Bildern nichts verloren hat.

.PARAMETER Ziel
    Ablage der Bilder. Standard: docs\bilder im Projekt.

.PARAMETER Exe
    Die zu fotografierende Anwendung. Standard: der Release-Build des Projekts.

.PARAMETER OhneNeubau
    Überspringt das Bauen und nimmt die vorhandene Programmdatei.

.PARAMETER NurHell
    Nimmt nur die helle Reihe auf.

.EXAMPLE
    ./tools/bildschirmfotos.ps1
#>
[CmdletBinding()]
param(
    [string] $Ordner = 'F:\Fotos',
    [string] $NamensOrdner = 'F:\Fotos aus iCloud',
    [string] $Ziel,
    [string] $Exe,
    [switch] $OhneNeubau,
    [switch] $NurHell,
    [int] $Width = 1400,
    [int] $Height = 1000
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

Add-Type -Namespace Native -Name Win -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);
[DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);
[DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
[DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out RECT val, int size);
[StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
'@

$root = Split-Path -Parent $PSScriptRoot
if (-not $Ziel) { $Ziel = Join-Path $root 'docs\bilder' }
if (-not $Exe) {
    $Exe = Join-Path $root 'src\PictureSorter.App\bin\x64\Release\net10.0-windows10.0.26100.0\win-x64\PictureSorter.exe'
}

$datenOrdner = Join-Path $env:LOCALAPPDATA 'PictureSorter'

# PW_RENDERFULLCONTENT: ohne diesen Kennwert bleibt der von DirectX gezeichnete
# Inhalt eines WinUI-Fensters leer.
$RenderFullContent = 2
# DWMWA_EXTENDED_FRAME_BOUNDS: der sichtbare Fensterbereich ohne Griffrand.
$ExtendedFrameBounds = 9

$Automation = [System.Windows.Automation.AutomationElement]
$Scope = [System.Windows.Automation.TreeScope]

# ---------------------------------------------------------------- Hilfsmittel

function Write-Schritt([string]$text) {
    Write-Host $text -ForegroundColor Cyan
}

function Wait-Until([scriptblock]$bedingung, [string]$was, [int]$sekunden = 30) {
    $ende = [datetime]::UtcNow.AddSeconds($sekunden)
    while ([datetime]::UtcNow -lt $ende) {
        if (& $bedingung) { return $true }
        Start-Sleep -Milliseconds 400
    }
    throw "Zeit abgelaufen beim Warten auf: $was"
}

function Find-Element($eltern, [string]$name, [string]$typ, [int]$sekunden = 20) {
    $bedingungen = [System.Collections.Generic.List[System.Windows.Automation.Condition]]::new()
    if ($name) {
        $bedingungen.Add((New-Object System.Windows.Automation.PropertyCondition($Automation::NameProperty, $name)))
    }
    if ($typ) {
        $ct = [System.Windows.Automation.ControlType]::$typ
        $bedingungen.Add((New-Object System.Windows.Automation.PropertyCondition($Automation::ControlTypeProperty, $ct)))
    }
    $bedingung = if ($bedingungen.Count -eq 1) { $bedingungen[0] } else {
        New-Object System.Windows.Automation.AndCondition ([System.Windows.Automation.Condition[]]$bedingungen.ToArray())
    }

    $gefunden = $null
    $ende = [datetime]::UtcNow.AddSeconds($sekunden)
    while ([datetime]::UtcNow -lt $ende) {
        $gefunden = $eltern.FindFirst($Scope::Descendants, $bedingung)
        if ($gefunden) { return $gefunden }
        Start-Sleep -Milliseconds 300
    }

    # Die Fehlermeldung nennt, was stattdessen da ist. Sonst steht man vor einem
    # „nicht gefunden" und muss den Baum von Hand nachschlagen.
    $vorhanden = ''
    if ($typ) {
        $ct = [System.Windows.Automation.ControlType]::$typ
        $treffer = $eltern.FindAll($Scope::Descendants,
            (New-Object System.Windows.Automation.PropertyCondition($Automation::ControlTypeProperty, $ct)))
        $namen = @()
        foreach ($t in $treffer) { $namen += "'" + $t.Current.Name + "'" }
        $vorhanden = " Vorhanden vom Typ ${typ}: " + ($namen -join ', ')
    }
    throw "Steuerelement nicht gefunden: name='$name' typ='$typ'.$vorhanden"
}

function Test-Element($eltern, [string]$name, [string]$typ) {
    try { return $null -ne (Find-Element $eltern $name $typ 1) } catch { return $false }
}

function Set-Text($element, [string]$wert) {
    $muster = $element.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
    $muster.SetValue($wert)
    Start-Sleep -Milliseconds 300
}

function Invoke-Element($element) {
    # Ein gesperrter Knopf meldet beim Auslösen nur „Unbekannter Fehler". Das Warten
    # davor macht aus der Meldung eine Aussage: welcher Knopf, und dass er gesperrt war.
    $name = $element.Current.Name
    # Keine typografischen Anführungszeichen in Zeichenketten: PowerShell nimmt
    # sie als Begrenzer wie ein gerades Anführungszeichen. Mitten in einem Text
    # bricht der Aufruf dadurch auseinander, und die Meldung zeigt auf einen ganz
    # anderen Parameter. In Kommentaren sind sie unbedenklich.
    Wait-Until { $element.Current.IsEnabled } "den freigegebenen Knopf: $name" 45 | Out-Null
    $muster = $element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    $muster.Invoke()
    Start-Sleep -Milliseconds 600
}

function Select-NavItem($fenster, [string]$name) {
    $eintrag = Find-Element $fenster $name 'ListItem'
    $muster = $eintrag.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
    $muster.Select()
    Start-Sleep -Milliseconds 1200
}

function Set-ScrollPercent($eltern, [double]$prozent) {
    # Über das Scroll-Muster der Bedienhilfen, aber mit SetScrollPercent statt Scroll:
    # Ein „kleiner Schritt" bewegt nur wenige Punkte, und man weiß hinterher nicht,
    # wo man steht. Der Anteil dagegen ist von Lauf zu Lauf derselbe.
    $bedingung = New-Object System.Windows.Automation.PropertyCondition(
        $Automation::IsScrollPatternAvailableProperty, $true)
    $treffer = $eltern.FindAll($Scope::Descendants, $bedingung)
    $breiteste = $null
    foreach ($t in $treffer) {
        $muster = $t.GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern)
        if (-not $muster.Current.VerticallyScrollable) { continue }
        if (-not $breiteste -or $t.Current.BoundingRectangle.Width -gt $breiteste.Current.BoundingRectangle.Width) {
            $breiteste = $t
        }
    }
    if (-not $breiteste) { return }
    $muster = $breiteste.GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern)
    $muster.SetScrollPercent([System.Windows.Automation.ScrollPattern]::NoScroll, $prozent)
    Start-Sleep -Milliseconds 900
}

function Get-StatusText($fenster) {
    $bedingung = New-Object System.Windows.Automation.PropertyCondition($Automation::AutomationIdProperty, 'Message')
    $treffer = $fenster.FindAll($Scope::Descendants, $bedingung)
    if ($treffer.Count -eq 0) { return '' }
    return $treffer[$treffer.Count - 1].Current.Name
}

# ------------------------------------------------------------------- Aufnahme

function Save-WindowShot([IntPtr]$fenster, [string]$pfad) {
    $rahmen = New-Object Native.Win+RECT
    [void][Native.Win]::GetWindowRect($fenster, [ref]$rahmen)
    $sichtbar = New-Object Native.Win+RECT
    [void][Native.Win]::DwmGetWindowAttribute($fenster, $ExtendedFrameBounds, [ref]$sichtbar, 16)

    $ganzB = $rahmen.Right - $rahmen.Left
    $ganzH = $rahmen.Bottom - $rahmen.Top

    $ganz = New-Object System.Drawing.Bitmap $ganzB, $ganzH, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $zeichnung = [System.Drawing.Graphics]::FromImage($ganz)
    $hdc = $zeichnung.GetHdc()
    $erfolg = [Native.Win]::PrintWindow($fenster, $hdc, $RenderFullContent)
    $zeichnung.ReleaseHdc($hdc)
    $zeichnung.Dispose()
    if (-not $erfolg) { $ganz.Dispose(); throw "PrintWindow schlug fehl für $pfad." }

    # Zuschnitt auf den sichtbaren Bereich: der Griffrand gehört auf kein Werbebild.
    $crop = New-Object System.Drawing.Rectangle (
        ($sichtbar.Left - $rahmen.Left),
        ($sichtbar.Top - $rahmen.Top),
        ($sichtbar.Right - $sichtbar.Left),
        ($sichtbar.Bottom - $sichtbar.Top))
    $bild = $ganz.Clone($crop, $ganz.PixelFormat)
    $ganz.Dispose()

    Set-RoundedCorners $bild
    $bild.Save($pfad, [System.Drawing.Imaging.ImageFormat]::Png)

    $farben = Measure-Colors $bild
    $size = "$($bild.Width)x$($bild.Height)"
    $bild.Dispose()
    return [pscustomobject]@{ File = Split-Path -Leaf $pfad; Size = $size; Colors = $farben }
}

function Set-RoundedCorners($bild) {
    # Die Ecken des Fensters sind rund; ohne Freistellung stünde dort der
    # Schreibtisch-Hintergrund im Bild. Durchsichtig gemacht passen sie auf jeden
    # Untergrund der Webseite.
    $r = 8
    $b = $bild.Width
    $h = $bild.Height
    $pfad = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $pfad.AddArc(0, 0, $d, $d, 180, 90)
    $pfad.AddArc($b - $d - 1, 0, $d, $d, 270, 90)
    $pfad.AddArc($b - $d - 1, $h - $d - 1, $d, $d, 0, 90)
    $pfad.AddArc(0, $h - $d - 1, $d, $d, 90, 90)
    $pfad.CloseFigure()

    $bereich = New-Object System.Drawing.Region (New-Object System.Drawing.Rectangle 0, 0, $b, $h)
    $bereich.Exclude($pfad)
    $zeichnung = [System.Drawing.Graphics]::FromImage($bild)
    $zeichnung.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
    $zeichnung.FillRegion((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::Transparent)), $bereich)
    $zeichnung.Dispose()
    $bereich.Dispose()
    $pfad.Dispose()
}

function Measure-Colors($bild) {
    $farben = New-Object 'System.Collections.Generic.HashSet[int]'
    for ($y = 0; $y -lt $bild.Height; $y += 3) {
        for ($x = 0; $x -lt $bild.Width; $x += 3) {
            [void]$farben.Add($bild.GetPixel($x, $y).ToArgb())
        }
    }
    return $farben.Count
}

# --------------------------------------------------------- Anwendung steuern

function Start-App([bool]$dunkel) {
    # Die Einstellungsdatei wird vorgeschrieben statt in der Oberfläche umgeschaltet:
    # So sieht schon das erste Bild richtig aus, und die Update-Prüfung bleibt aus -
    # ihr Hinweisstreifen hätte sonst auf einem Bild gestanden und auf dem nächsten
    # nicht.
    $null = New-Item -ItemType Directory -Force -Path $datenOrdner
    $einstellungen = [ordered]@{ Dark = $dunkel; Guided = $true; CheckUpdates = $false; AutoUpdate = $false }
    $einstellungen | ConvertTo-Json -Compress | Set-Content -LiteralPath (Join-Path $datenOrdner 'ui-settings.json') -Encoding utf8

    $process = Start-Process -FilePath $Exe -PassThru

    $fenster = $null
    Wait-Until {
        $treffer = $Automation::RootElement.FindAll($Scope::Children,
            (New-Object System.Windows.Automation.PropertyCondition($Automation::ProcessIdProperty, $process.Id)))
        foreach ($w in $treffer) {
            if ($w.Current.Name -eq 'PictureSorter' -and $w.Current.BoundingRectangle.Width -gt 600) {
                $script:gefundenesFenster = $w
                return $true
            }
        }
        return $false
    } 'das Hauptfenster' 90 | Out-Null
    $fenster = $script:gefundenesFenster

    $handle = [IntPtr]$fenster.Current.NativeWindowHandle
    [void][Native.Win]::MoveWindow($handle, 40, 0, $Width, $Height, $true)
    Start-Sleep -Seconds 2

    return [pscustomobject]@{ Process = $process; Fenster = $fenster; Handle = $handle }
}

function Stop-App($app) {
    if ($app -and -not $app.Process.HasExited) {
        $app.Process.CloseMainWindow() | Out-Null
        if (-not $app.Process.WaitForExit(8000)) { $app.Process.Kill() }
    }
}

# ------------------------------------------------------------- Die Ansichten

function Get-Series([string]$anhang) {
    $berichte = [System.Collections.Generic.List[object]]::new()

    Write-Schritt "  Demo-Bestand erzeugen ..."
    & (Join-Path $PSScriptRoot 'demobilder.ps1') -Ordner $Ordner -NamensOrdner $NamensOrdner | Write-Verbose

    Write-Schritt "  Anwendung starten ..."
    $app = Start-App ($anhang -eq '-dunkel')
    try {
        $fenster = $app.Fenster

        # --- Startseite
        Write-Schritt '    Startseite'
        Select-NavItem $fenster 'Start'
        $berichte.Add((Save-WindowShot $app.Handle (Join-Path $Ziel "01-start$anhang.png")))

        # --- Nach Datum ablegen, samt Vorschau
        Write-Schritt '    Nach Datum ablegen'
        Select-NavItem $fenster 'Nach Datum ablegen'
        Set-Text (Find-Element $fenster 'Bilder aus' 'Edit') $Ordner
        Set-Text (Find-Element $fenster 'Ordner anlegen unter' 'Edit') $Ordner
        Invoke-Element (Find-Element $fenster 'Vorschau erstellen' 'Button')
        Wait-Until { Test-Element $fenster 'Vorschläge' 'List' } 'die Vorschau der Ablage' 60 | Out-Null
        # Der interessante Teil - die Liste mit Bildern und Zielordnern - liegt unten.
        # Ein Stück gerollt, und beide Hälften der Seite stehen im Bild.
        Set-ScrollPercent $fenster 30
        Start-Sleep -Seconds 2
        $berichte.Add((Save-WindowShot $app.Handle (Join-Path $Ziel "02-nach-datum-ablegen$anhang.png")))

        # --- Duplikate
        Write-Schritt '    Duplikate'
        Select-NavItem $fenster 'Duplikate'
        Set-Text (Find-Element $fenster 'Ordner' 'Edit') $Ordner
        Invoke-Element (Find-Element $fenster 'Duplikate suchen' 'Button')
        Wait-Until { (Get-StatusText $fenster) -match 'Duplikat' } 'das Ergebnis der Duplikat-Suche' 120 | Out-Null
        Start-Sleep -Seconds 2
        $berichte.Add((Save-WindowShot $app.Handle (Join-Path $Ziel "04-duplikate$anhang.png")))

        # --- Detailansicht: die erste Kachel groß ansehen
        Write-Schritt '    Detailansicht'
        Invoke-Element (Find-Element $fenster 'Bild groß ansehen' 'Button')
        Start-Sleep -Seconds 3
        $berichte.Add((Save-WindowShot $app.Handle (Join-Path $Ziel "05-detailansicht$anhang.png")))
        Close-Dialog $fenster

        # --- Sortieren: der geführte Weg bis zur Vorschau
        Write-Schritt '    Sortieren'
        Select-NavItem $fenster 'Sortieren'
        Invoke-SortWizard $fenster
        $berichte.Add((Save-WindowShot $app.Handle (Join-Path $Ziel "03-sortieren$anhang.png")))

        # --- Gedächtnis: füllt sich erst, nachdem einmal sortiert wurde
        Write-Schritt '    Gedächtnis'
        Invoke-Element (Find-Element $fenster 'Sortieren' 'Button')
        Wait-Until { (Get-StatusText $fenster) -match 'sortiert|verschoben|kopiert' } 'den Abschluss des Sortierlaufs' 120 | Out-Null
        Start-Sleep -Seconds 2
        Select-NavItem $fenster 'Gedächtnis'
        Start-Sleep -Seconds 2
        $berichte.Add((Save-WindowShot $app.Handle (Join-Path $Ziel "06-gedaechtnis$anhang.png")))

        # --- Namen richten: eigener Bestand, weil unlesbare Namen auf den übrigen
        # --- Bildern nichts verloren haben
        Write-Schritt '    Namen richten'
        Select-NavItem $fenster 'Namen richten'
        Set-Text (Find-Element $fenster 'Ordner' 'Edit') $NamensOrdner
        Invoke-Element (Find-Element $fenster 'Namen prüfen' 'Button')
        Wait-Until { (Get-StatusText $fenster) -match 'unlesbaren Namen|lesbar' } 'das Ergebnis der Namensprüfung' 120 | Out-Null
        Start-Sleep -Seconds 2
        $berichte.Add((Save-WindowShot $app.Handle (Join-Path $Ziel "07-namen-richten$anhang.png")))
    }
    finally {
        Stop-App $app
    }

    return $berichte
}

function Close-Dialog($fenster) {
    $zu = Find-Element $fenster 'Schließen' 'Button' 5
    Invoke-Element $zu
    Start-Sleep -Seconds 1
}

# Aus welchen Motiven des Demo-Bestands die Gruppe gelernt wird. Die Namen stammen
# aus demobilder.ps1 und gehören zu dieser Reihenfolge; wird der Bestand dort
# geändert, meldet Select-Examples die fehlende Datei beim Namen.
# Gewählt, weil der Bestand diese Gruppe sauber hergibt: Zwei Beispiele genügen, und
# die Erkennung findet ein drittes Bild dazu, das nicht vorgegeben war (den Hafen bei
# Nacht). Eine Gruppe, in der am Ende nur die Beispiele stehen, zeigt nichts.
$MatchingExamples = @('IMG_2047.jpg', 'IMG_2048.jpg')                 # Stadt bei Nacht, Lichter
$CounterExamples = @('IMG_2041.jpg', 'IMG_2043.jpg', 'IMG_2045.jpg')  # Sonnenuntergang, Berge, Wald

function Get-ExampleName($eintrag) {
    # Der Dateiname hängt am Vorschaubild der Kachel; die Kachel selbst trägt ihn seit
    # der Ergänzung in PhotoTile ebenfalls, aber nicht in jeder Ansicht.
    $bild = $eintrag.FindFirst($Scope::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition(
            $Automation::AutomationIdProperty, 'Preview')))
    if ($bild) { return $bild.Current.Name }
    return $eintrag.Current.Name
}

function Select-Examples($fenster, [string]$liste, [string[]]$behalten) {
    # Ein Schwung Vorschläge holt bis zu fünfzehn Bilder - beim Demo-Bestand also den
    # ganzen Ordner. Als Beispiele taugt das nicht: Wer jedes Bild als passend anlernt,
    # bekommt jedes Bild vorgeschlagen, und das Werbebild zeigte vierzehn Treffer zu
    # hundert Prozent. Behalten wird deshalb nur eine Handvoll.
    $container = Find-Element $fenster $liste 'List'
    Wait-Until {
        $container.FindAll($Scope::Children, [System.Windows.Automation.Condition]::TrueCondition).Count -gt 0
    } "die Vorschläge in der Liste $liste" 120 | Out-Null

    $vorhanden = @()
    foreach ($eintrag in $container.FindAll($Scope::Children, [System.Windows.Automation.Condition]::TrueCondition)) {
        $vorhanden += Get-ExampleName $eintrag
    }
    foreach ($wanted in $behalten) {
        if ($vorhanden -notcontains $wanted) {
            throw "Beispielbild '$wanted' steht nicht unter den Vorschlägen. Vorhanden: $($vorhanden -join ', ')"
        }
    }

    # Nach jedem Entfernen baut sich die Liste neu auf - die alten Verweise zeigen dann
    # ins Leere. Deshalb je Durchgang genau einer, und danach neu nachgesehen.
    while ($true) {
        $surplus = $null
        foreach ($eintrag in $container.FindAll($Scope::Children, [System.Windows.Automation.Condition]::TrueCondition)) {
            if ($behalten -notcontains (Get-ExampleName $eintrag)) { $surplus = $eintrag; break }
        }
        if (-not $surplus) { break }
        Invoke-Element (Find-Element $surplus 'Entfernen' 'Button' 5)
    }
}

function Invoke-SortWizard($fenster) {
    # Schritt 1: Ordner
    Set-Text (Find-Element $fenster 'Ordner mit Fotos' 'Edit') $Ordner
    Invoke-Element (Find-Element $fenster 'Weiter ▶' 'Button')

    # Schritt 2: Kategorie beschreiben
    Set-Text (Find-Element $fenster 'Name der Gruppe' 'Edit') 'Stadt bei Nacht'
    Set-Text (Find-Element $fenster 'Beschreibung (in eigenen Worten)' 'Edit') `
        'Aufnahmen einer Stadt bei Nacht, mit beleuchteten Fenstern und Lichtern.'
    Invoke-Element (Find-Element $fenster 'Weiter ▶' 'Button')

    # Schritt 3: passende Beispiele - die Vorschläge kommen aus dem Quellordner
    Invoke-Element (Find-Element $fenster 'Vorschläge holen' 'Button')
    Select-Examples $fenster 'Passende Beispielbilder' $MatchingExamples
    Invoke-Element (Find-Element $fenster 'Weiter ▶' 'Button')

    # Schritt 4: Gegenbeispiele, dann das Profil lernen
    Invoke-Element (Find-Element $fenster 'Vorschläge holen' 'Button')
    Select-Examples $fenster 'Gegenbeispiele' $CounterExamples
    Invoke-Element (Find-Element $fenster 'Profil lernen ▶' 'Button')
    Wait-Until { Test-Element $fenster 'Analyse starten ▶' 'Button' } 'den Schritt Analyse' 300 | Out-Null

    # Schritt 5: Analyse - jedes Foto wird der Bilderkennung vorgelegt. Das dauert:
    # Erst beschreibt das Bild-Modell jedes Foto, dann wird die Beschreibung mit den
    # angelernten Beispielen verglichen.
    Invoke-Element (Find-Element $fenster 'Analyse starten ▶' 'Button')
    Wait-Until { Test-Element $fenster 'Vorschläge' 'List' } 'das Ergebnis der Analyse' 1800 | Out-Null
    Start-Sleep -Seconds 3
}

# ----------------------------------------------------------------- Hauptlauf

if (-not $OhneNeubau) {
    Write-Schritt 'Anwendung bauen ...'
    & dotnet build (Join-Path $root 'src\PictureSorter.App\PictureSorter.App.csproj') -c Release -p:Platform=x64 -v q --nologo
    if ($LASTEXITCODE -ne 0) { throw "dotnet build fehlgeschlagen ($LASTEXITCODE)." }
}
if (-not (Test-Path -LiteralPath $Exe)) { throw "Programmdatei nicht gefunden: $Exe" }
$null = New-Item -ItemType Directory -Force -Path $Ziel

if (Get-Process -Name 'PictureSorter' -ErrorAction SilentlyContinue) {
    throw 'PictureSorter läuft bereits. Bitte schließen - sonst fotografiert das Werkzeug das falsche Fenster.'
}

# Der Datenordner der Anwendung wandert zur Seite. Auf keinem Bild darf ein Ordner
# stehen, den jemand privat sortiert hat.
$beiseite = $null
if (Test-Path -LiteralPath $datenOrdner) {
    $beiseite = "$datenOrdner.beiseite-{0:yyyyMMddHHmmss}" -f (Get-Date)
    Move-Item -LiteralPath $datenOrdner -Destination $beiseite
    Write-Host "Vorhandene Daten liegen solange unter: $beiseite"
}

$alle = [System.Collections.Generic.List[object]]::new()
try {
    Write-Schritt 'Helle Reihe ...'
    Get-Series '' | ForEach-Object { $alle.Add($_) }

    if (-not $NurHell) {
        Write-Schritt 'Dunkle Reihe ...'
        Remove-Item -LiteralPath $datenOrdner -Recurse -Force -ErrorAction SilentlyContinue
        Get-Series '-dunkel' | ForEach-Object { $alle.Add($_) }
    }
}
finally {
    if ($beiseite) {
        Remove-Item -LiteralPath $datenOrdner -Recurse -Force -ErrorAction SilentlyContinue
        Move-Item -LiteralPath $beiseite -Destination $datenOrdner
        Write-Host 'Vorhandene Daten zurückgeholt.'
    }
}

Write-Host ''
$alle | Format-Table -AutoSize
$leer = $alle | Where-Object { $_.Colors -lt 20 }
if ($leer) {
    Write-Warning 'Diese Aufnahmen sind fast einfarbig - da steht vermutlich eine leere Fläche im Bild:'
    $leer | ForEach-Object { Write-Warning ("  " + $_.Datei) }
    exit 1
}
Write-Host "Fertig: $Ziel" -ForegroundColor Green
