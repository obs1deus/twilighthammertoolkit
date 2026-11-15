param([string]$Path = (Join-Path $PSScriptRoot 'TwilightHammer.ico'))
Add-Type -AssemblyName System.Drawing
$sizes = @(16,24,32,48,64,128,256)
$images = foreach ($size in $sizes) {
    $bitmap = [Drawing.Bitmap]::new($size,$size)
    $graphic = [Drawing.Graphics]::FromImage($bitmap)
    $font = [Drawing.Font]::new('Segoe UI Symbol',($size * 0.71),[Drawing.FontStyle]::Regular,[Drawing.GraphicsUnit]::Pixel)
    $format = [Drawing.StringFormat]::new()
    $ink = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(239,197,130))
    $background = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(44,33,57))
    $memory = [IO.MemoryStream]::new()
    try {
        $graphic.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphic.TextRenderingHint = [Drawing.Text.TextRenderingHint]::AntiAliasGridFit
        $graphic.FillEllipse($background,0,0,($size-1),($size-1))
        $format.Alignment = $format.LineAlignment = [Drawing.StringAlignment]::Center
        $graphic.DrawString([string][char]0x2692,$font,$ink,[Drawing.RectangleF]::new(0,0,$size,$size),$format)
        $bitmap.Save($memory,[Drawing.Imaging.ImageFormat]::Png)
        ,$memory.ToArray()
    } finally {
        $memory.Dispose(); $background.Dispose(); $ink.Dispose(); $format.Dispose()
        $font.Dispose(); $graphic.Dispose(); $bitmap.Dispose()
    }
}
$stream = [IO.File]::Create($Path)
$writer = [IO.BinaryWriter]::new($stream)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($index=0; $index -lt $sizes.Count; $index++) {
        $dimension = [byte]($sizes[$index] % 256)
        $writer.Write($dimension); $writer.Write($dimension)
        $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$images[$index].Length); $writer.Write([uint32]$offset)
        $offset += $images[$index].Length
    }
    foreach ($image in $images) { $writer.Write([byte[]]$image) }
} finally { $writer.Dispose() }
