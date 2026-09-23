<#
Updates third-party binaries:
  - FFmpeg (gyan.dev "essentials" release build) -> Installer Files\ffmpeg.exe (untracked, for local builds;
    releases leave it out and Cupscale downloads it on demand)
  - 7-Zip x64 standalone (7za.exe)               -> Code\Resources\7za.exe (tracked, embedded into Cupscale.exe)

Requires internet access; uses 7zr.exe from 7-zip.org to unpack.
#>
param(
    [string]$SevenZipVersion = "2603",    # 26.03; see https://www.7-zip.org/download.html
    [switch]$SkipFfmpeg,
    [switch]$Skip7za
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$tmp = Join-Path ([IO.Path]::GetTempPath()) ("cupscale-bin-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory $tmp | Out-Null

try {
    $7zr = Join-Path $tmp "7zr.exe"
    Invoke-WebRequest "https://www.7-zip.org/a/7zr.exe" -OutFile $7zr

    if (-not $Skip7za) {
        $extra = Join-Path $tmp "7z-extra.7z"
        Invoke-WebRequest "https://www.7-zip.org/a/7z$SevenZipVersion-extra.7z" -OutFile $extra
        & $7zr e $extra "x64\7za.exe" "-o$tmp" -y | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Failed to extract 7za.exe" }
        Copy-Item (Join-Path $tmp "7za.exe") (Join-Path $repo "Code\Resources\7za.exe") -Force
        "7za.exe (x64, 7-Zip $SevenZipVersion) -> Code\Resources\7za.exe"
    }

    if (-not $SkipFfmpeg) {
        $version = ([string](Invoke-RestMethod "https://www.gyan.dev/ffmpeg/builds/release-version")).Trim()
        $archive = Join-Path $tmp "ffmpeg.7z"
        Invoke-WebRequest "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.7z" -OutFile $archive
        & $7zr e $archive "*\bin\ffmpeg.exe" "-o$tmp" -r -y | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Failed to extract ffmpeg.exe" }
        Copy-Item (Join-Path $tmp "ffmpeg.exe") (Join-Path $repo "Installer Files\ffmpeg.exe") -Force
        "ffmpeg.exe (gyan.dev essentials $version) -> Installer Files\ffmpeg.exe"
    }
}
finally {
    Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue
}
