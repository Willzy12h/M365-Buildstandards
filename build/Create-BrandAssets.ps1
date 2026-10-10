#Requires -Version 5.1
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, WindowsBase
$root = Split-Path -Parent $PSScriptRoot
$target = Join-Path $root 'src\BDIT.TenantToolkit.Graph\Assets'
New-Item -ItemType Directory -Path $target -Force | Out-Null
$visual = [System.Windows.Media.DrawingVisual]::new()
$draw = $visual.RenderOpen()
$blue = [System.Windows.Media.BrushConverter]::new().ConvertFromString('#1665AD')
$green = [System.Windows.Media.BrushConverter]::new().ConvertFromString('#4DE0AE')
$draw.DrawRoundedRectangle($blue, $null, [System.Windows.Rect]::new(0,0,256,256),48,48)
foreach ($y in 58,105,152) { $draw.DrawRoundedRectangle([System.Windows.Media.Brushes]::White,$null,[System.Windows.Rect]::new(49,$y,99,16),8,8) }
$pen = [System.Windows.Media.Pen]::new($green,20)
$pen.StartLineCap = 'Round'; $pen.EndLineCap = 'Round'; $pen.LineJoin = 'Round'
$geometry = [System.Windows.Media.Geometry]::Parse('M 145,173 L 169,197 L 216,139')
$draw.DrawGeometry($null,$pen,$geometry)
$draw.Close()
$bitmap = [System.Windows.Media.Imaging.RenderTargetBitmap]::new(256,256,96,96,[System.Windows.Media.PixelFormats]::Pbgra32)
$bitmap.Render($visual)
$encoder = [System.Windows.Media.Imaging.PngBitmapEncoder]::new()
$encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
$png = Join-Path $target 'buildstandard.png'
$stream = [IO.File]::Create($png)
try { $encoder.Save($stream) } finally { $stream.Dispose() }
$bytes = [IO.File]::ReadAllBytes($png)
$ico = [IO.BinaryWriter]::new([IO.File]::Create((Join-Path $target 'buildstandard.ico')))
try {
    $ico.Write([uint16]0); $ico.Write([uint16]1); $ico.Write([uint16]1)
    $ico.Write([byte]0); $ico.Write([byte]0); $ico.Write([byte]0); $ico.Write([byte]0)
    $ico.Write([uint16]1); $ico.Write([uint16]32); $ico.Write([uint32]$bytes.Length); $ico.Write([uint32]22); $ico.Write($bytes)
} finally { $ico.Dispose() }
Write-Output 'Created the original BuildStandard checklist icon (PNG and ICO).'
