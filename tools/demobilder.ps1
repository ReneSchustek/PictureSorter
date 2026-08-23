# demobilder.ps1 - erzeugt einen Demo-Bestand für Bildschirmfotos.
#
# Warum: Für Bilder auf einer Webseite braucht es einen Bestand, der etwas hergibt -
# und der keine privaten Fotos zeigt. Die Bilder hier sind gezeichnet, tragen aber ein
# Aufnahmedatum im Dateikopf, sodass die Ablage nach Datum echte Ordner bildet.
#
# Der Zielordner ist bewusst neutral: Auf einem Bildschirmfoto hat kein Benutzername
# etwas verloren.
#
# Erzeugt einen Demo-Bestand: Bilder, die auf einem Bildschirmfoto nach Fotos aussehen,
# mit Aufnahmedatum im Dateikopf - und zwei Paaren, die sich gleichen.
#
# Die Motive unterscheiden sich bewusst in der Helligkeitsverteilung von links nach
# rechts. Der Wahrnehmungs-Hash der Anwendung vergleicht jeden Bildpunkt eines 9x8-
# Rasters mit seinem rechten Nachbarn; Bilder, die nur einen Verlauf von oben nach
# unten tragen, sehen für ihn deshalb alle gleich aus. Genau das war der Grund, warum
# ein früherer Bestand fünf Duplikat-Gruppen ergab statt der zwei gewollten. Am Ende
# rechnet das Skript denselben Hash nach und meldet, ob der Abstand reicht.
param(
    [string]$Ordner = "F:\Fotos",
    [string]$NamensOrdner = "F:\Fotos aus iCloud",
    [int]$MindestAbstand = 12
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

if (Test-Path $Ordner) { Remove-Item $Ordner -Recurse -Force }
New-Item -ItemType Directory -Path $Ordner | Out-Null

# Jedes Motiv: Name, Aufnahmedatum, Himmel oben, Grund unten, Art und Lage der Formen.
#
#   Ort     - waagerechte Lage der Hauptform (0 = links, 1 = rechts)
#   Horizont- Höhe der Grundlinie (Anteil der Bildhöhe)
#   Hoch    - hochkant statt quer; der Bildbereich der Detailansicht ist hochkant
#             und zeigt ein Querformat mit breiten Rändern
$motifs = @(
    @{ Name = "strand-sonnenuntergang"; Datum = "2021:07:14 19:42:00"; Oben = "#FF7043"; Unten = "#FFCA28"; Form = "sonne";  Ort = 0.72; Horizont = 0.58; Hoch = $false },
    @{ Name = "strand-abends";          Datum = "2021:07:14 20:10:00"; Oben = "#4E342E"; Unten = "#FFB74D"; Form = "steg";   Ort = 0.18; Horizont = 0.66; Hoch = $false },
    @{ Name = "berge-morgens";          Datum = "2021:07:18 07:15:00"; Oben = "#64B5F6"; Unten = "#37474F"; Form = "berge";  Ort = 0.08; Horizont = 0.74; Hoch = $true },
    @{ Name = "berge-mittags";          Datum = "2021:07:18 12:30:00"; Oben = "#42A5F5"; Unten = "#455A64"; Form = "berge";  Ort = 0.24; Horizont = 0.62; Hoch = $false },
    @{ Name = "wald-im-nebel";          Datum = "2021:09:03 08:05:00"; Oben = "#B0BEC5"; Unten = "#2E7D32"; Form = "trees";  Ort = 0.12; Horizont = 0.82; Hoch = $true },
    @{ Name = "see-spiegelung";         Datum = "2021:09:03 17:20:00"; Oben = "#4FC3F7"; Unten = "#0277BD"; Form = "sonne";  Ort = 0.22; Horizont = 0.42; Hoch = $false },
    @{ Name = "stadt-bei-nacht";        Datum = "2021:12:24 21:00:00"; Oben = "#1A237E"; Unten = "#212121"; Form = "stadt";  Ort = 0.55; Horizont = 0.46; Hoch = $false },
    @{ Name = "weihnachtsmarkt";        Datum = "2021:12:24 18:30:00"; Oben = "#311B92"; Unten = "#4E342E"; Form = "lichter";Ort = 0.50; Horizont = 0.70; Hoch = $true },
    @{ Name = "fruehling-im-garten";    Datum = "2023:05:01 11:00:00"; Oben = "#81D4FA"; Unten = "#66BB6A"; Form = "trees";  Ort = 0.62; Horizont = 0.55; Hoch = $false },
    @{ Name = "picknick";               Datum = "2023:05:01 13:45:00"; Oben = "#90CAF9"; Unten = "#7CB342"; Form = "decke";  Ort = 0.38; Horizont = 0.62; Hoch = $false },
    @{ Name = "segeln";                 Datum = "2023:08:12 15:20:00"; Oben = "#29B6F6"; Unten = "#01579B"; Form = "segel";  Ort = 0.30; Horizont = 0.60; Hoch = $true },
    @{ Name = "hafen";                  Datum = "2023:08:12 16:05:00"; Oben = "#039BE5"; Unten = "#01579B"; Form = "stadt";  Ort = 0.15; Horizont = 0.68; Hoch = $false }
)

# Fester Startwert: Der Bestand muss bei jedem Aufruf gleich aussehen, sonst zeigen
# zwei Bildschirmfoto-Serien derselben Fassung verschiedene Bilder.
$dice = New-Object System.Random 20260823

function New-Brush([string]$html, [int]$alpha = 255) {
    $farbe = [System.Drawing.ColorTranslator]::FromHtml($html)
    return New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb($alpha, $farbe.R, $farbe.G, $farbe.B))
}

function Draw-Motif($motif, $width, $height) {
    $image = New-Object System.Drawing.Bitmap $width, $height
    $canvas = [System.Drawing.Graphics]::FromImage($image)
    $canvas.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias

    $horizont = [int]($height * $motif.Horizont)
    $ort = [int]($width * $motif.Ort)

    # Himmel und Grund als getrennte Flächen: Die Grundlinie liegt bei jedem Motiv
    # woanders und trägt damit schon einen Teil des Unterschieds.
    $himmel = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.Rectangle -ArgumentList 0, 0, $width, $horizont),
        [System.Drawing.ColorTranslator]::FromHtml($motif.Oben),
        [System.Drawing.ColorTranslator]::FromHtml($motif.Unten), 90.0)
    $canvas.FillRectangle($himmel, 0, 0, $width, $horizont)
    $himmel.Dispose()

    $grund = New-Brush $motif.Unten
    $canvas.FillRectangle($grund, 0, $horizont, $width, $height - $horizont)
    $grund.Dispose()

    switch ($motif.Form) {
        "sonne" {
            $sonne = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(240, 255, 241, 118))
            $d = [int]([Math]::Min($width, $height) * 0.30)
            $canvas.FillEllipse($sonne, $ort - [int]($d / 2), $horizont - [int]($d * 0.9), $d, $d)
            # Lichtspur auf dem Wasser: liegt genau unter der Sonne und verschiebt
            # damit den Helligkeitsschwerpunkt nach links oder rechts.
            $spur = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(90, 255, 255, 255))
            $canvas.FillRectangle($spur, $ort - [int]($d * 0.28), $horizont, [int]($d * 0.56), $height - $horizont)
            $sonne.Dispose(); $spur.Dispose()
        }
        "berge" {
            $fels = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(225, 38, 50, 56))
            $schnee = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(235, 236, 239, 241))
            foreach ($versatz in @(0.0, 0.22, 0.44)) {
                $links = $ort + [int]($width * $versatz)
                $spitze = $links + [int]($width * 0.16)
                $rechts = $links + [int]($width * 0.32)
                $gipfel = [int]($horizont * 0.30)
                $canvas.FillPolygon($fels, [System.Drawing.Point[]]@(
                    (New-Object System.Drawing.Point -ArgumentList $links, $horizont),
                    (New-Object System.Drawing.Point -ArgumentList $spitze, $gipfel),
                    (New-Object System.Drawing.Point -ArgumentList $rechts, $horizont)))
                $canvas.FillPolygon($schnee, [System.Drawing.Point[]]@(
                    (New-Object System.Drawing.Point -ArgumentList ($spitze - [int]($width * 0.05)), ($gipfel + [int]($horizont * 0.18))),
                    (New-Object System.Drawing.Point -ArgumentList $spitze, $gipfel),
                    (New-Object System.Drawing.Point -ArgumentList ($spitze + [int]($width * 0.05)), ($gipfel + [int]($horizont * 0.18)))))
            }
            $fels.Dispose(); $schnee.Dispose()
        }
        "trees" {
            $stamm = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(225, 78, 52, 46))
            $krone = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(225, 27, 94, 32))
            foreach ($versatz in @(0.0, 0.20, 0.40)) {
                $x = $ort + [int]($width * $versatz)
                $canvas.FillRectangle($stamm, $x, [int]($horizont * 0.62), [int]($width * 0.035), $horizont - [int]($horizont * 0.62))
                $canvas.FillEllipse($krone, $x - [int]($width * 0.08), [int]($horizont * 0.20), [int]($width * 0.20), [int]($horizont * 0.55))
            }
            $stamm.Dispose(); $krone.Dispose()
        }
        "stadt" {
            $haus = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(240, 33, 33, 33))
            $fenster = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(235, 255, 213, 79))
            $x = $ort - [int]($width * 0.30)
            while ($x -lt $ort + [int]($width * 0.34)) {
                $h = [int]($horizont * (0.35 + $dice.NextDouble() * 0.55))
                $b = [int]($width * (0.05 + $dice.NextDouble() * 0.06))
                if ($x + $b -gt 0) {
                    $canvas.FillRectangle($haus, $x, $horizont - $h, $b, $h)
                    for ($fy = $horizont - $h + 14; $fy -lt $horizont - 18; $fy += 28) {
                        for ($fx = $x + 10; $fx -lt $x + $b - 12; $fx += 22) {
                            if ($dice.Next(0, 3) -gt 0) { $canvas.FillRectangle($fenster, $fx, $fy, 9, 13) }
                        }
                    }
                }
                $x += $b + 7
            }
            $haus.Dispose(); $fenster.Dispose()
        }
        "steg" {
            # Ein Steg, der von links ins Bild läuft: eine helle Bahn quer über den
            # dunklen Grund, klar auf einer Seite.
            $holz = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(235, 141, 110, 99))
            $canvas.FillPolygon($holz, [System.Drawing.Point[]]@(
                (New-Object System.Drawing.Point -ArgumentList 0, $height),
                (New-Object System.Drawing.Point -ArgumentList ($ort + [int]($width * 0.22)), $horizont),
                (New-Object System.Drawing.Point -ArgumentList ($ort + [int]($width * 0.40)), $horizont),
                (New-Object System.Drawing.Point -ArgumentList ([int]($width * 0.42)), $height)))
            $pfosten = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(235, 62, 39, 35))
            foreach ($versatz in @(0.28, 0.55, 0.85)) {
                $px = [int]($width * 0.06 + $ort * $versatz)
                $canvas.FillRectangle($pfosten, $px, $horizont - [int]($height * 0.10), [int]($width * 0.022), [int]($height * 0.24))
            }
            $holz.Dispose(); $pfosten.Dispose()
        }
        "lichter" {
            # Lichterketten über einem Platz: helle Punkte in Reihen, die sich nach
            # rechts verdichten.
            $bude = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(235, 62, 39, 35))
            $canvas.FillRectangle($bude, [int]($width * 0.12), $horizont - [int]($height * 0.16), [int]($width * 0.34), [int]($height * 0.16))
            $canvas.FillRectangle($bude, [int]($width * 0.56), $horizont - [int]($height * 0.11), [int]($width * 0.28), [int]($height * 0.11))
            $licht = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(240, 255, 236, 179))
            for ($reihe = 0; $reihe -lt 3; $reihe++) {
                $y = [int]($horizont * (0.24 + $reihe * 0.16))
                for ($i = 0; $i -lt 14; $i++) {
                    $x = [int]($width * (0.08 + $i * 0.065))
                    $bogen = [int]([Math]::Sin($i / 2.2) * $height * 0.02)
                    $canvas.FillEllipse($licht, $x, $y + $bogen, [int]($width * 0.016), [int]($width * 0.016))
                }
            }
            $bude.Dispose(); $licht.Dispose()
        }
        "decke" {
            # Eine karierte Decke auf der Wiese - kräftige Streifen quer und längs.
            $tuch = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(235, 239, 154, 154))
            $links = $ort - [int]($width * 0.22)
            $width = [int]($width * 0.44)
            $oben = $horizont + [int](($height - $horizont) * 0.12)
            $height = [int](($height - $horizont) * 0.72)
            $canvas.FillRectangle($tuch, $links, $oben, $width, $height)
            $streifen = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(200, 255, 255, 255))
            for ($i = 1; $i -lt 6; $i++) {
                $canvas.FillRectangle($streifen, $links + [int]($width * $i / 6.0), $oben, [int]($width * 0.03), $height)
            }
            for ($i = 1; $i -lt 4; $i++) {
                $canvas.FillRectangle($streifen, $links, $oben + [int]($height * $i / 4.0), $width, [int]($height * 0.05))
            }
            $korb = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(235, 141, 110, 99))
            $canvas.FillRectangle($korb, $links + [int]($width * 0.72), $oben - [int]($height * 0.18), [int]($width * 0.20), [int]($height * 0.22))
            $tuch.Dispose(); $streifen.Dispose(); $korb.Dispose()
        }
        "segel" {
            # Zwei weiße Segel auf dunklem Wasser - der hellste Fleck sitzt weit links.
            $segel = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(240, 250, 250, 250))
            $rumpf = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(240, 33, 33, 33))
            foreach ($paar in @(@(0.0, 1.0), @(0.30, 0.62))) {
                $x = $ort + [int]($width * $paar[0])
                $scale = $paar[1]
                $spitze = $horizont - [int]($height * 0.34 * $scale)
                $canvas.FillPolygon($segel, [System.Drawing.Point[]]@(
                    (New-Object System.Drawing.Point -ArgumentList $x, $spitze),
                    (New-Object System.Drawing.Point -ArgumentList ($x + [int]($width * 0.13 * $scale)), $horizont),
                    (New-Object System.Drawing.Point -ArgumentList ($x - [int]($width * 0.04 * $scale)), $horizont)))
                $canvas.FillRectangle($rumpf, $x - [int]($width * 0.07 * $scale), $horizont, [int]($width * 0.22 * $scale), [int]($height * 0.035 * $scale))
            }
            $segel.Dispose(); $rumpf.Dispose()
        }
    }

    $canvas.Dispose()
    return $image
}

function Save-Motif($image, $pfad, $datum) {
    # Zwischenschritt über eine Datei, weil ein frisch gezeichnetes Bitmap keine
    # Eigenschaftsliste hat, in die sich das Aufnahmedatum eintragen ließe.
    $roh = [System.IO.Path]::GetTempFileName() + ".jpg"
    $image.Save($roh, [System.Drawing.Imaging.ImageFormat]::Jpeg)

    $mit = [System.Drawing.Image]::FromFile($roh)
    $eigenschaft = $mit.PropertyItems[0]
    $eigenschaft.Id = 36867      # DateTimeOriginal
    $eigenschaft.Type = 2
    $bytes = [System.Text.Encoding]::ASCII.GetBytes($datum + [char]0)
    $eigenschaft.Len = $bytes.Length
    $eigenschaft.Value = $bytes
    $mit.SetPropertyItem($eigenschaft)
    $mit.Save($pfad, [System.Drawing.Imaging.ImageFormat]::Jpeg)
    $mit.Dispose()
    Remove-Item $roh -Force
}

# Der Wahrnehmungs-Hash der Anwendung, hier nachgerechnet: 9x8-Raster, jeder Punkt
# gegen seinen rechten Nachbarn. Die Gegenprobe unten braucht ihn, damit sich ein
# neuer Bestand nicht erst in der Oberfläche als zu ähnlich herausstellt.
function Get-DifferenceHash([string]$pfad) {
    # Wichtig ist die Mittelung über die Fläche: Die Anwendung verkleinert mit Fant,
    # und wer hier stattdessen einzelne Punkte abtastet, misst etwas anderes als das,
    # was die Duplikat-Suche später sieht. Der Weg über ein Zwischenraster von 90x80
    # und anschließendes Mitteln kommt dem nahe, ohne jeden Bildpunkt einzeln lesen zu
    # müssen - das wäre in PowerShell zu langsam.
    $original = [System.Drawing.Image]::FromFile($pfad)
    $zwischen = New-Object System.Drawing.Bitmap 90, 80
    $g = [System.Drawing.Graphics]::FromImage($zwischen)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.DrawImage($original, 0, 0, 90, 80)
    $g.Dispose(); $original.Dispose()

    $raster = New-Object 'double[]' 72
    for ($zy = 0; $zy -lt 8; $zy++) {
        for ($zx = 0; $zx -lt 9; $zx++) {
            $summe = 0.0
            for ($y = $zy * 10; $y -lt ($zy + 1) * 10; $y++) {
                for ($x = $zx * 10; $x -lt ($zx + 1) * 10; $x++) {
                    $p = $zwischen.GetPixel($x, $y)
                    $summe += 0.299 * $p.R + 0.587 * $p.G + 0.114 * $p.B
                }
            }
            $raster[$zy * 9 + $zx] = $summe / 100.0
        }
    }
    $zwischen.Dispose()

    $hash = [uint64]0
    $bit = 0
    for ($y = 0; $y -lt 8; $y++) {
        for ($x = 0; $x -lt 8; $x++) {
            if ([int]$raster[$y * 9 + $x] -gt [int]$raster[$y * 9 + $x + 1]) {
                $hash = $hash -bor ([uint64]1 -shl $bit)
            }
            $bit++
        }
    }
    return $hash
}

function Get-HammingDistance([uint64]$links, [uint64]$rechts) {
    $rest = $links -bxor $rechts
    $anzahl = 0
    while ($rest -ne 0) { $anzahl += [int]($rest -band 1); $rest = $rest -shr 1 }
    return $anzahl
}

$nummer = 2041
$dateien = @()
foreach ($m in $motifs) {
    if ($m.Hoch) { $width = 1067; $height = 1600 } else { $width = 1600; $height = 1067 }
    $image = Draw-Motif $m $width $height
    $pfad = Join-Path $Ordner ("IMG_{0}.jpg" -f $nummer)
    Save-Motif $image $pfad $m.Datum
    $image.Dispose()
    $dateien += [pscustomobject]@{ Name = $m.Name; Pfad = $pfad }
    $nummer++
}

# Zwei Paare, die sich gleichen - damit die Duplikat-Suche etwas zu zeigen hat.
Copy-Item (Join-Path $Ordner "IMG_2041.jpg") (Join-Path $Ordner "IMG_2041 - Kopie.jpg")
Copy-Item (Join-Path $Ordner "IMG_2047.jpg") (Join-Path $Ordner "IMG_2047 - Kopie.jpg")

# Ein zweiter, kleiner Bestand mit unlesbaren Namen - so, wie ein aus iCloud geladener
# Ordner sie mitbringt. Bewusst getrennt vom Hauptbestand: Auf einem Werbebild der
# Sortierung oder der Duplikat-Suche hat solcher Zeichensalat nichts verloren.
#
# Zwei der vier Dateien tragen ein Aufnahmedatum, zwei nicht. Damit zeigt die Vorschau
# beide Quellen eines Vorschlags nebeneinander: das Datum, wo eines dasteht, sonst
# Ordnername und laufende Nummer.
if (Test-Path $NamensOrdner) { Remove-Item $NamensOrdner -Recurse -Force }
New-Item -ItemType Directory -Path $NamensOrdner | Out-Null

$ersatz = [char]0xFFFD
$kaputteNamen = @(
    @{ Name = ($ersatz + "4" + $ersatz + [char]0x04DD + "___.JPEG"); Motiv = 0; MitDatum = $true },
    @{ Name = ("_V" + $ersatz + "zx" + $ersatz + "z.jpg");           Motiv = 5; MitDatum = $true },
    @{ Name = ($ersatz + $ersatz + "'" + $ersatz + "_`.jpg");        Motiv = 8; MitDatum = $false },
    @{ Name = ($ersatz + "4" + $ersatz + [char]0x04DD + ";__.JPEG"); Motiv = 10; MitDatum = $false }
)

foreach ($k in $kaputteNamen) {
    $m = $motifs[$k.Motiv]
    if ($m.Hoch) { $breite = 1067; $hoehe = 1600 } else { $breite = 1600; $hoehe = 1067 }
    $image = Draw-Motif $m $breite $hoehe
    $ziel = Join-Path $NamensOrdner $k.Name
    if ($k.MitDatum) {
        Save-Motif $image $ziel $m.Datum
    }
    else {
        # Ohne Aufnahmedatum: Ein frisch gezeichnetes Bitmap hat keine
        # Eigenschaftsliste, das Speichern schreibt also auch keine.
        $image.Save($ziel, [System.Drawing.Imaging.ImageFormat]::Jpeg)
    }
    $image.Dispose()
}

# Gegenprobe: Die zwölf Motive müssen sich für den Wahrnehmungs-Hash deutlich
# unterscheiden. Sonst meldet die Duplikat-Suche mehr Gruppen als die zwei gewollten,
# und die Zahl in der Statuszeile führt auf einem Werbebild in die Irre.
$hashes = @{}
foreach ($d in $dateien) { $hashes[$d.Name] = Get-DifferenceHash $d.Pfad }

$zuNah = @()
for ($i = 0; $i -lt $dateien.Count; $i++) {
    for ($j = $i + 1; $j -lt $dateien.Count; $j++) {
        $abstand = Get-HammingDistance $hashes[$dateien[$i].Name] $hashes[$dateien[$j].Name]
        if ($abstand -lt $MindestAbstand) {
            $zuNah += ("{0} / {1}: Abstand {2}" -f $dateien[$i].Name, $dateien[$j].Name, $abstand)
        }
    }
}

Write-Output ("{0} Bilder in {1}" -f (Get-ChildItem $Ordner).Count, $Ordner)
Write-Output ("{0} Bilder mit unlesbarem Namen in {1}" -f (Get-ChildItem $NamensOrdner).Count, $NamensOrdner)
if ($zuNah.Count -gt 0) {
    Write-Warning ("Motive liegen für den Wahrnehmungs-Hash zu dicht beieinander (Mindestabstand {0}):" -f $MindestAbstand)
    $zuNah | ForEach-Object { Write-Warning ("  " + $_) }
} else {
    Write-Output ("Gegenprobe: alle Motive liegen mindestens {0} Bit auseinander." -f $MindestAbstand)
}
