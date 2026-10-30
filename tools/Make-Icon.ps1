# Génère assets\app.ico (icône F : fusée violette sur carré arrondi) en PNG 16/32/48/256 empaquetés dans un ICO.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$out = Join-Path (Split-Path $PSScriptRoot -Parent) 'assets\app.ico'
New-Item -ItemType Directory -Force (Split-Path $out) | Out-Null

function Hex($h) { [System.Drawing.ColorTranslator]::FromHtml($h) }

function Render([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.ScaleTransform($size / 100.0, $size / 100.0)

    $bg = New-Object System.Drawing.Drawing2D.GraphicsPath
    $r = 22
    $bg.AddArc(0, 0, $r * 2, $r * 2, 180, 90); $bg.AddArc(100 - $r * 2, 0, $r * 2, $r * 2, 270, 90)
    $bg.AddArc(100 - $r * 2, 100 - $r * 2, $r * 2, $r * 2, 0, 90); $bg.AddArc(0, 100 - $r * 2, $r * 2, $r * 2, 90, 90)
    $bg.CloseFigure()
    $g.FillPath((New-Object System.Drawing.SolidBrush (Hex '#534AB7')), $bg)

    $light = New-Object System.Drawing.SolidBrush (Hex '#EEEDFE')
    $mid = New-Object System.Drawing.SolidBrush (Hex '#CECBF6')
    $P = { param($x, $y) New-Object System.Drawing.PointF $x, $y }

    # ailerons, corps, hublot, flamme
    $g.FillPolygon($mid, [System.Drawing.PointF[]]@((& $P 37 54), (& $P 22 76), (& $P 40 70)))
    $g.FillPolygon($mid, [System.Drawing.PointF[]]@((& $P 63 54), (& $P 78 76), (& $P 60 70)))
    $body = New-Object System.Drawing.Drawing2D.GraphicsPath
    $body.AddBezier((& $P 50 8), (& $P 67 22), (& $P 71 46), (& $P 64 68))
    $body.AddLine((& $P 64 68), (& $P 36 68))
    $body.AddBezier((& $P 36 68), (& $P 29 46), (& $P 33 22), (& $P 50 8))
    $g.FillPath($light, $body)
    $g.FillEllipse((New-Object System.Drawing.SolidBrush (Hex '#534AB7')), 42, 32, 16, 16)
    $g.FillPolygon((New-Object System.Drawing.SolidBrush (Hex '#E24B4A')), [System.Drawing.PointF[]]@((& $P 43 72), (& $P 57 72), (& $P 50 92)))
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    if ($size -ge 256) {
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    } else {
        # petites tailles en DIB 32 bits (System.Drawing.Icon ne lit pas les entrées PNG) : en-tête, pixels du bas vers le haut, masque AND vide
        $bw = New-Object System.IO.BinaryWriter $ms
        $bw.Write([uint32]40); $bw.Write([int32]$size); $bw.Write([int32]($size * 2)); $bw.Write([uint16]1); $bw.Write([uint16]32)
        $maskRow = [int]([math]::Ceiling($size / 32.0) * 4)
        $bw.Write([uint32]0); $bw.Write([uint32]($size * $size * 4 + $maskRow * $size)); 0..3 | ForEach-Object { $bw.Write([uint32]0) }
        for ($y = $size - 1; $y -ge 0; $y--) { for ($x = 0; $x -lt $size; $x++) { $bw.Write([int32]$bmp.GetPixel($x, $y).ToArgb()) } }
        $bw.Write((New-Object byte[] ($maskRow * $size)))
        $bw.Flush()
    }
    $bmp.Dispose()
    , $ms.ToArray()
}

$sizes = 16, 32, 48, 256
$pngs = $sizes | ForEach-Object { , (Render $_) }
$fs = [System.IO.File]::Create($out)
$w = New-Object System.IO.BinaryWriter $fs
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $len = $pngs[$i].Length
    $w.Write([byte]($s % 256)); $w.Write([byte]($s % 256)); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$len); $w.Write([uint32]$offset)
    $offset += $len
}
foreach ($p in $pngs) { $w.Write($p) }
$w.Close()
"icone : $out ($((Get-Item $out).Length) octets)"
