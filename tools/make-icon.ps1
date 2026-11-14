<#
.SYNOPSIS
  Regenerates WindowsApp/WatchPointer.App/Assets/WatchPointer.ico (16–256 px, PNG-compressed frames).
  The design matches the tray icon: dark watch face, blue ring, red laser dot.
#>
param([string]$Out = (Join-Path $PSScriptRoot "..\WindowsApp\WatchPointer.App\Assets\WatchPointer.ico"))
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$sizes = 16, 20, 24, 32, 40, 48, 64, 96, 128, 256
$frames = foreach ($size in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)
    $s = $size / 32.0
    $body = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 28, 28, 32))
    $ring = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 0, 120, 212)), ([Math]::Max(1.5, 3 * $s))
    $dot = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 255, 70, 60))
    $g.FillEllipse($body, 1 * $s, 1 * $s, 30 * $s, 30 * $s)
    $g.DrawEllipse($ring, 3 * $s, 3 * $s, 26 * $s, 26 * $s)
    $g.FillEllipse($dot, 11.5 * $s, 11.5 * $s, 9 * $s, 9 * $s)
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    , @($size, $ms.ToArray())
}

New-Item -ItemType Directory -Force (Split-Path $Out) | Out-Null
$fs = [System.IO.File]::Create($Out)
$w = New-Object System.IO.BinaryWriter $fs
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$frames.Count)   # ICONDIR
$offset = 6 + 16 * $frames.Count
foreach ($f in $frames) {
    $size = $f[0]; $data = $f[1]
    $dim = if ($size -ge 256) { 0 } else { $size }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([UInt16]1); $w.Write([UInt16]32); $w.Write([UInt32]$data.Length); $w.Write([UInt32]$offset)
    $offset += $data.Length
}
foreach ($f in $frames) { $w.Write([byte[]]$f[1]) }
$w.Dispose()
Write-Host "Wrote $Out ($((Get-Item $Out).Length) bytes, $($frames.Count) sizes)"
