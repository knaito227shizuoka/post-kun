# post-kun のアイコン(assets/post-kun.ico)を生成する。
# オレンジの角丸四角に白い紙飛行機(送信)。サイズごとに描き直し、PNG 形式で ICO に詰める。
# 実行: pwsh -ExecutionPolicy Bypass -File assets/make-icon.ps1
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$out = Join-Path $PSScriptRoot 'post-kun.ico'

function New-RoundRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function New-Points([float]$s, [float[]]$xy) {
    $pts = for ($i = 0; $i -lt $xy.Length; $i += 2) { New-Object System.Drawing.PointF ($xy[$i] * $s), ($xy[$i + 1] * $s) }
    return [System.Drawing.PointF[]]$pts
}

function Get-IconPng([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)

    # 背景: 上が明るいオレンジ、下が濃いオレンジのグラデーション
    $m = [Math]::Max(0.5, $s / 32.0)          # 外側の余白
    $r = $s * 0.22
    $bg = New-RoundRect $m $m ($s - 2 * $m) ($s - 2 * $m) $r
    $rect = New-Object System.Drawing.RectangleF 0, 0, $s, $s
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, `
        ([System.Drawing.Color]::FromArgb(255, 255, 167, 38)), `
        ([System.Drawing.Color]::FromArgb(255, 239, 108, 0)), 90.0
    $g.FillPath($brush, $bg)

    # 紙飛行機(右上へ飛ぶ)
    $white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
    $shade = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 255, 224, 189))
    $fold  = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 230, 120, 20))
    $g.FillPolygon($white, (New-Points $s @(0.80, 0.20, 0.16, 0.47, 0.40, 0.57)))       # 上の翼
    $g.FillPolygon($shade, (New-Points $s @(0.80, 0.20, 0.40, 0.57, 0.55, 0.82)))       # 下の翼
    if ($s -ge 24) {
        $g.FillPolygon($fold, (New-Points $s @(0.40, 0.57, 0.43, 0.76, 0.51, 0.66)))    # 折り目の影
    }

    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return , $ms.ToArray()
}

$images = foreach ($s in $sizes) { , (Get-IconPng $s) }

# ICO: ICONDIR + ICONDIRENTRY[n] + PNG データ
$fs = [System.IO.File]::Create($out)
$w = New-Object System.IO.BinaryWriter $fs
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $len = $images[$i].Length
    $b = if ($s -ge 256) { 0 } else { $s }
    $w.Write([byte]$b); $w.Write([byte]$b); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([UInt16]1); $w.Write([UInt16]32); $w.Write([UInt32]$len); $w.Write([UInt32]$offset)
    $offset += $len
}
foreach ($img in $images) { $w.Write($img) }
$w.Close()

# 確認用プレビュー(引数でパスを渡したときだけ)
if ($args.Count -gt 0) {
    [System.IO.File]::WriteAllBytes($args[0], (Get-IconPng 256))
}
Write-Host "Wrote $out"
