<#
Generates GUI test inputs in __fixtures__ (and creates __outputs__). Requires ffmpeg and magick in PATH.
See GUI_TEST.md for the checklist; run check_outputs.py afterwards.
#>
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"
$fx = Join-Path $PSScriptRoot "__fixtures__"
$outs = Join-Path $PSScriptRoot "__outputs__"
if (Test-Path $fx) { Remove-Item -Recurse -Force $fx }
foreach ($d in "batch\a", "batch\b", "single", "video", "models") { New-Item -ItemType Directory -Force (Join-Path $fx $d) | Out-Null }
New-Item -ItemType Directory -Force $outs | Out-Null

function Img($spec, $path) { & magick @spec $path; if ($LASTEXITCODE -ne 0) { throw "magick failed: $path" } }

# Batch: subfolders, same names in different folders, same stem with different extensions,
# dotted and non-ASCII names, alpha, grayscale, palette and every input format
Img @("-size", "96x64", "gradient:red-blue") (Join-Path $fx "batch\a\same.png")
Img @("-size", "96x64", "gradient:green-yellow") (Join-Path $fx "batch\b\same.png")
Img @("-size", "96x64", "plasma:", "-seed", "1") (Join-Path $fx "batch\photo.jpg")
Img @("-size", "80x80", "pattern:checkerboard") (Join-Path $fx "batch\r2image.200.png")
Img @("-size", "80x60", "gradient:orange-purple") (Join-Path $fx "batch\zażółć ñ.png")
Img @("-size", "64x64", "xc:none", "-fill", "#ff000080", "-draw", "circle 32,32 32,4") (Join-Path $fx "batch\alpha.png")
Img @("-size", "64x64", "gradient:", "-colorspace", "Gray") (Join-Path $fx "batch\gray.png")
Img @("-size", "64x64", "plasma:", "-colors", "16", "-type", "Palette") (Join-Path $fx "batch\palette.png")
Img @("-size", "64x48", "gradient:cyan-magenta") (Join-Path $fx "batch\webp.webp")
Img @("-size", "64x48", "gradient:navy-gold") (Join-Path $fx "batch\bitmap.bmp")
Img @("-size", "64x48", "gradient:teal-pink") (Join-Path $fx "batch\targa.tga")
Img @("-size", "64x48", "gradient:lime-navy") (Join-Path $fx "batch\jpegxl.jxl")
Img @("-size", "64x64", "gradient:red-lime", "-define", "dds:compression=dxt1") (Join-Path $fx "batch\texture.dds")

# Single image
Img @("-size", "256x192", "plasma:", "-seed", "7") (Join-Path $fx "single\landscape.png")

# Video: 12000+ frames would exceed the old 4-digit naming; keep it short but with audio and odd NTSC rate
& ffmpeg -hide_banner -loglevel error -y -f lavfi -i "testsrc2=size=160x90:rate=30000/1001" -f lavfi -i "sine=frequency=440:sample_rate=48000" -t 3 -c:v libx264 -pix_fmt yuv420p -c:a aac -shortest (Join-Path $fx "video\clip.mp4")
& ffmpeg -hide_banner -loglevel error -y -i (Join-Path $fx "video\clip.mp4") -c copy (Join-Path $fx "video\clip.mkv")

# Small Real-ESRGAN model (SRVGG, 4.7 MB)
Invoke-WebRequest "https://github.com/xinntao/Real-ESRGAN/releases/download/v0.2.5.0/realesr-general-x4v3.pth" -OutFile (Join-Path $fx "models\realesr-general-x4v3.pth")

"Fixtures written to $fx"
