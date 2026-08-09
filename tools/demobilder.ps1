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
param(
    [string]$Ordner = "F:\Fotos"
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

if (Test-Path $Ordner) { Remove-Item $Ordner -Recurse -Force }
New-Item -ItemType Directory -Path $Ordner | Out-Null

# Jedes Motiv: Name, Aufnahmedatum, Himmel oben, Grund unten, Farbe der Formen.
$motifs = @(
    @{ Name = "strand-sonnenuntergang"; Datum = "2021:07:14 19:42:00"; Oben = "#FF7043"; Unten = "#FFCA28"; Form = "sonne" },
    @{ Name = "strand-abends";          Datum = "2021:07:14 20:10:00"; Oben = "#EF6C00"; Unten = "#FFB74D"; Form = "sonne" },
    @{ Name = "berge-morgens";          Datum = "2021:07:18 07:15:00"; Oben = "#64B5F6"; Unten = "#37474F"; Form = "berge" },
    @{ Name = "berge-mittags";          Datum = "2021:07:18 12:30:00"; Oben = "#42A5F5"; Unten = "#455A64"; Form = "berge" },
    @{ Name = "wald-im-nebel";          Datum = "2021:09:03 08:05:00"; Oben = "#B0BEC5"; Unten = "#2E7D32"; Form = "trees" },
    @{ Name = "see-spiegelung";         Datum = "2021:09:03 17:20:00"; Oben = "#4FC3F7"; Unten = "#0277BD"; Form = "berge" },
    @{ Name = "stadt-bei-nacht";        Datum = "2021:12:24 21:00:00"; Oben = "#1A237E"; Unten = "#212121"; Form = "stadt" },
    @{ Name = "weihnachtsmarkt";        Datum = "2021:12:24 18:30:00"; Oben = "#311B92"; Unten = "#4E342E"; Form = "stadt" },
    @{ Name = "fruehling-im-garten";    Datum = "2023:05:01 11:00:00"; Oben = "#81D4FA"; Unten = "#66BB6A"; Form = "trees" },
    @{ Name = "picknick";               Datum = "2023:05:01 13:45:00"; Oben = "#90CAF9"; Unten = "#7CB342"; Form = "trees" },
    @{ Name = "segeln";                 Datum = "2023:08:12 15:20:00"; Oben = "#29B6F6"; Unten = "#01579B"; Form = "sonne" },
    @{ Name = "hafen";                  Datum = "2023:08:12 16:05:00"; Oben = "#039BE5"; Unten = "#01579B"; Form = "stadt" }
)

function Zeichne($motif, $width, $height) {
    $image = New-Object System.Drawing.Bitmap $width, $height
    $canvas = [System.Drawing.Graphics]::FromImage($image)
    $canvas.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias

    $oben = [System.Drawing.ColorTranslator]::FromHtml($motif.Oben)
    $unten = [System.Drawing.ColorTranslator]::FromHtml($motif.Unten)
    $verlauf = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.Rectangle -ArgumentList 0, 0, $width, $height), $oben, $unten, 90.0)
    $canvas.FillRectangle($verlauf, 0, 0, $width, $height)

    switch ($motif.Form) {
        "sonne" {
            $sonne = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(230, 255, 241, 118))
            $d = [int]($height * 0.28)
            $canvas.FillEllipse($sonne, [int]($width * 0.62), [int]($height * 0.18), $d, $d)
            $spiegel = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(70, 255, 255, 255))
            $canvas.FillRectangle($spiegel, [int]($width * 0.62), [int]($height * 0.62), $d, [int]($height * 0.3))
            $sonne.Dispose(); $spiegel.Dispose()
        }
        "berge" {
            $fels = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(210, 38, 50, 56))
            foreach ($v in @(0.05, 0.35, 0.62)) {
                $links = [int]($width * $v)
                $spitze = [int]($width * ($v + 0.18))
                $rechts = [int]($width * ($v + 0.36))
                $gipfel = [int]($height * 0.35)
                $punkte = [System.Drawing.Point[]]@(
                    (New-Object System.Drawing.Point -ArgumentList $links, $height),
                    (New-Object System.Drawing.Point -ArgumentList $spitze, $gipfel),
                    (New-Object System.Drawing.Point -ArgumentList $rechts, $height)
                )
                $canvas.FillPolygon($fels, $punkte)
            }
            $fels.Dispose()
        }
        "trees" {
            $stamm = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(220, 78, 52, 46))
            $krone = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(220, 27, 94, 32))
            foreach ($v in @(0.12, 0.42, 0.72)) {
                $x = [int]($width * $v)
                $canvas.FillRectangle($stamm, $x, [int]($height * 0.55), [int]($width * 0.03), [int]($height * 0.45))
                $canvas.FillEllipse($krone, $x - [int]($width * 0.07), [int]($height * 0.25), [int]($width * 0.17), [int]($height * 0.4))
            }
            $stamm.Dispose(); $krone.Dispose()
        }
        "stadt" {
            $haus = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(235, 33, 33, 33))
            $fenster = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(230, 255, 213, 79))
            $x = 0
            while ($x -lt $width) {
                $h = Get-Random -Minimum ([int]($height * 0.25)) -Maximum ([int]($height * 0.6))
                $b = Get-Random -Minimum ([int]($width * 0.05)) -Maximum ([int]($width * 0.11))
                $canvas.FillRectangle($haus, $x, $height - $h, $b, $h)
                for ($fy = $height - $h + 12; $fy -lt $height - 20; $fy += 26) {
                    for ($fx = $x + 8; $fx -lt $x + $b - 10; $fx += 20) {
                        if ((Get-Random -Minimum 0 -Maximum 3) -gt 0) { $canvas.FillRectangle($fenster, $fx, $fy, 8, 12) }
                    }
                }
                $x += $b + 6
            }
            $haus.Dispose(); $fenster.Dispose()
        }
    }

    $verlauf.Dispose()
    $canvas.Dispose()
    return $image
}

function Speichere($image, $pfad, $datum) {
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

$nummer = 2041
foreach ($m in $motifs) {
    $image = Zeichne $m 1600 1067
    $pfad = Join-Path $Ordner ("IMG_{0}.jpg" -f $nummer)
    Speichere $image $pfad $m.Datum
    $image.Dispose()
    $nummer++
}

# Zwei Paare, die sich gleichen - damit die Duplikat-Suche etwas zu zeigen hat.
Copy-Item (Join-Path $Ordner "IMG_2041.jpg") (Join-Path $Ordner "IMG_2041 - Kopie.jpg")
Copy-Item (Join-Path $Ordner "IMG_2047.jpg") (Join-Path $Ordner "IMG_2047 - Kopie.jpg")

Write-Output ("{0} Bilder in {1}" -f (Get-ChildItem $Ordner).Count, $Ordner)
