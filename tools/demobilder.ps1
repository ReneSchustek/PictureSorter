# demobilder.ps1 - stellt den Demo-Bestand für Bildschirmfotos bereit.
#
# Warum: Für Bilder auf einer Webseite braucht es einen Bestand, der etwas hergibt -
# und der keine privaten Fotos zeigt. Der Bestand besteht deshalb aus veröffentlichten
# Aufnahmen des Entwicklers: Watt und Marsch an der Nordsee, ohne Personen, ohne
# fremde Rechte. Sie stehen ohnehin öffentlich im Netz.
#
# Gezeichnete Bilder trügen für sechs der sieben Ansichten, für die Sortier-Seite
# nicht: Das Bild-Modell kann gezeichnete Motive nicht lesen und antwortet mit
# Erfundenem - auf einem Werbebild wirkt dann ein Vorschlag willkürlich, obwohl die
# Anwendung nicht der Grund ist. Echte Fotos beheben das an der Wurzel, ohne an der
# Erkennung selbst zu drehen.
#
# Die Fotos liegen nicht im Repository - sie sind groß und gehören dort nicht hin.
# Hier steht nur der Weg zu ihnen. Einmal geladen, bleiben sie im Vorrat liegen; ein
# zweiter Aufruf kommt ohne Netz aus.
#
# Der Zielordner ist bewusst neutral: Auf einem Bildschirmfoto hat kein Benutzername
# etwas verloren.
param(
    [string]$Ordner = "F:\Fotos",
    [string]$NamensOrdner = "F:\Fotos aus iCloud",
    [string]$Vorrat = (Join-Path $env:LOCALAPPDATA "PictureSorter-Demobestand"),
    [int]$MindestAbstand = 12
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName PresentationCore

# Woher die Bilder stammen. Der Bestand ist eine öffentliche Galerie; die Dateinamen
# dort tragen Ortsangaben, die hier nirgends auftauchen - gespeichert wird unter
# IMG_20xx.jpg, so wie eine Kamera es täte.
$Bezugsquelle = "https://ruhrcoder.de/wp-content/uploads/2026/03/2026_{0}-2400x1551.webp"

# Die Bilder der Galerie stecken in einem weißen Rahmen mit Titel und Signatur. Auf
# einem Bildschirmfoto, das Fotos einer Kamera zeigen soll, hat der nichts verloren.
# Der Rahmen ist bei allen Dateien gleich breit; gemessen wurde die Kante über alle
# 33 Bilder der Galerie.
$Rahmen = @{ Left = 152; Top = 152; Width = 2096; Height = 1170 }
$Zielbreite = 1600

# Die zwölf Motive in der Reihenfolge, in der sie zu IMG_2041 bis IMG_2052 werden.
# Drei Rollen hängen an dieser Reihenfolge, und bildschirmfotos.ps1 nennt sie beim
# Dateinamen:
#
#   IMG_2047, IMG_2048  die beiden Beispiele, aus denen die Gruppe gelernt wird
#   IMG_2052            das Bild, das die Erkennung von sich aus dazufindet
#   IMG_2041, 2043, 2045  Gegenbeispiele - weite Landschaft statt Fund am Boden
#
# Gewählt ist als Gruppe der Fund am Boden: Muscheln im Schlick. Das ist ein Motiv,
# das ein Bild-Modell sicher benennt, und es unterscheidet sich für den Betrachter
# unmissverständlich von Deich, Marsch und Sonnenuntergang. Wer das Werbebild ansieht,
# soll jeden Vorschlag nachvollziehen können.
#
# Die Aufnahmedaten liegen über vier Tage, damit die Ablage nach Datum echte Ordner
# bildet.
$motive = @(
    @{ Quelle = "Deichlinie_Tossens";             Datum = "2026:03:12 08:40:00" },
    @{ Quelle = "Gezeitenpause_Fedderwardersiel"; Datum = "2026:03:12 10:15:00" },
    @{ Quelle = "Marschweite_Fedderwardersiel";   Datum = "2026:03:12 16:30:00" },
    @{ Quelle = "Seezeichen_Fedderwardersiel";    Datum = "2026:03:13 09:05:00" },
    @{ Quelle = "Industriefeuer_Ruhwarden";       Datum = "2026:03:13 18:55:00" },
    @{ Quelle = "Lachmoewe_Fedderwardersiel";     Datum = "2026:03:13 11:20:00" },
    @{ Quelle = "Kalk-Mosaik_Tossens";            Datum = "2026:03:14 10:40:00" },
    @{ Quelle = "Kalk-Schlick_Tossens";           Datum = "2026:03:14 11:05:00" },
    @{ Quelle = "SalzwiesenPfad_Fedderwardersiel"; Datum = "2026:03:14 15:20:00" },
    @{ Quelle = "Rotkehlchen_Ruhwarden";          Datum = "2026:03:15 08:30:00" },
    @{ Quelle = "Waechter_des_Watts_Fedderwardersiel"; Datum = "2026:03:15 17:45:00" },
    @{ Quelle = "Herzmuschel_Fedderwardersiel";   Datum = "2026:03:15 12:10:00" }
)

function Get-Vorratsdatei([string]$quelle) {
    # Einmal geladen, bleibt die Datei liegen. Das hält den zweiten Lauf schnell und
    # macht ihn vom Netz unabhängig - eine Bildschirmfoto-Serie soll sich nicht daran
    # entscheiden, ob gerade eine Verbindung steht.
    $ziel = Join-Path $Vorrat ($quelle + ".webp")
    if (Test-Path $ziel) { return $ziel }

    $adresse = $Bezugsquelle -f $quelle
    Write-Verbose "Lade $quelle"
    Invoke-WebRequest -Uri $adresse -OutFile $ziel -UseBasicParsing
    return $ziel
}

function Save-Foto([string]$vorratsdatei, [string]$pfad, [string]$datum) {
    # Der ganze Weg läuft über die Windows-Bildverwaltung: Sie liest WebP, während
    # System.Drawing das nicht kann, und sie schreibt das Aufnahmedatum gleich mit.
    $quelle = [System.IO.File]::OpenRead($vorratsdatei)
    try {
        $frame = ([System.Windows.Media.Imaging.BitmapDecoder]::Create(
            $quelle, 'None', 'OnLoad')).Frames[0]

        $box = New-Object System.Windows.Int32Rect `
            $Rahmen.Left, $Rahmen.Top, $Rahmen.Width, $Rahmen.Height
        $ohneRahmen = New-Object System.Windows.Media.Imaging.CroppedBitmap $frame, $box

        $faktor = $Zielbreite / $Rahmen.Width
        $skaliert = New-Object System.Windows.Media.Imaging.TransformedBitmap `
            $ohneRahmen, (New-Object System.Windows.Media.ScaleTransform $faktor, $faktor)

        $meta = New-Object System.Windows.Media.Imaging.BitmapMetadata "jpg"
        $meta.SetQuery("/app1/ifd/exif/{ushort=36867}", $datum)   # DateTimeOriginal

        $encoder = New-Object System.Windows.Media.Imaging.JpegBitmapEncoder
        $encoder.QualityLevel = 92
        $encoder.Frames.Add(
            [System.Windows.Media.Imaging.BitmapFrame]::Create($skaliert, $null, $meta, $null))

        $ziel = [System.IO.File]::Create($pfad)
        try { $encoder.Save($ziel) } finally { $ziel.Dispose() }
    }
    finally { $quelle.Dispose() }
}

function Save-FotoOhneDatum([string]$vorratsdatei, [string]$pfad) {
    # Ohne Aufnahmedatum: Die Seite „Namen richten" soll beide Quellen eines
    # Vorschlags nebeneinander zeigen - das Datum, wo eines dasteht, sonst Ordnername
    # und laufende Nummer.
    $quelle = [System.IO.File]::OpenRead($vorratsdatei)
    try {
        $frame = ([System.Windows.Media.Imaging.BitmapDecoder]::Create(
            $quelle, 'None', 'OnLoad')).Frames[0]

        $box = New-Object System.Windows.Int32Rect `
            $Rahmen.Left, $Rahmen.Top, $Rahmen.Width, $Rahmen.Height
        $ohneRahmen = New-Object System.Windows.Media.Imaging.CroppedBitmap $frame, $box

        $faktor = $Zielbreite / $Rahmen.Width
        $skaliert = New-Object System.Windows.Media.Imaging.TransformedBitmap `
            $ohneRahmen, (New-Object System.Windows.Media.ScaleTransform $faktor, $faktor)

        $encoder = New-Object System.Windows.Media.Imaging.JpegBitmapEncoder
        $encoder.QualityLevel = 92
        $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($skaliert))

        $ziel = [System.IO.File]::Create($pfad)
        try { $encoder.Save($ziel) } finally { $ziel.Dispose() }
    }
    finally { $quelle.Dispose() }
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

if (-not (Test-Path $Vorrat)) { New-Item -ItemType Directory -Path $Vorrat | Out-Null }
if (Test-Path $Ordner) { Remove-Item $Ordner -Recurse -Force }
New-Item -ItemType Directory -Path $Ordner | Out-Null

$nummer = 2041
$dateien = @()
foreach ($m in $motive) {
    $vorratsdatei = Get-Vorratsdatei $m.Quelle
    $pfad = Join-Path $Ordner ("IMG_{0}.jpg" -f $nummer)
    Save-Foto $vorratsdatei $pfad $m.Datum
    $dateien += [pscustomobject]@{ Name = $m.Quelle; Pfad = $pfad }
    $nummer++
}

# Zwei Paare, die sich gleichen - damit die Duplikat-Suche etwas zu zeigen hat.
# Bewusst nicht die beiden Lernbeispiele: Ein doppeltes Beispiel verschöbe die Gruppe
# auf der Sortier-Seite, ohne dass es der Duplikat-Suche etwas brächte.
Copy-Item (Join-Path $Ordner "IMG_2042.jpg") (Join-Path $Ordner "IMG_2042 - Kopie.jpg")
Copy-Item (Join-Path $Ordner "IMG_2049.jpg") (Join-Path $Ordner "IMG_2049 - Kopie.jpg")

# Ein zweiter, kleiner Bestand mit unlesbaren Namen - so, wie ein aus iCloud geladener
# Ordner sie mitbringt. Bewusst getrennt vom Hauptbestand: Auf einem Werbebild der
# Sortierung oder der Duplikat-Suche hat solcher Zeichensalat nichts verloren.
#
# Zwei der vier Dateien tragen ein Aufnahmedatum, zwei nicht.
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
    $m = $motive[$k.Motiv]
    $vorratsdatei = Get-Vorratsdatei $m.Quelle
    $ziel = Join-Path $NamensOrdner $k.Name
    if ($k.MitDatum) { Save-Foto $vorratsdatei $ziel $m.Datum }
    else { Save-FotoOhneDatum $vorratsdatei $ziel }
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
