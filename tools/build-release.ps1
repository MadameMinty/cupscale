<#
Builds a release zip: build\Cupscale-<VERSION>.zip

  Cupscale.exe
  CupscaleData\bin\...   (Installer Files)

Not included, downloaded by Cupscale on demand: FFmpeg and the Python runtime (py.7z).
#>
$ErrorActionPreference = "Stop"
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$version = (Get-Content (Join-Path $repo "VERSION") -Raw).Trim()
$installerFiles = Join-Path $repo "Installer Files"
$7za = Join-Path $repo "Code\Resources\7za.exe"
$stage = Join-Path $repo "build\release"
$zip = Join-Path $repo "build\Cupscale-$version.zip"

if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
$app = Join-Path $stage "Cupscale"
$bin = Join-Path $app "CupscaleData\bin"

Remove-Item Env:MSBuildExtensionsPath -ErrorAction SilentlyContinue     # Breaks the dotnet CLI when set
dotnet publish (Join-Path $repo "Code\Cupscale.csproj") -c Release -p:Platform=x64 -o (Join-Path $stage "publish")
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

New-Item -ItemType Directory -Force $bin | Out-Null
Get-ChildItem (Join-Path $stage "publish\*") -File -Exclude *.pdb | Copy-Item -Destination $app
if (-not (Test-Path (Join-Path $app "Cupscale.exe"))) { throw "Cupscale.exe missing from publish output" }
robocopy $installerFiles $bin /E /XD __pycache__ /XF ffmpeg.exe /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw "robocopy failed ($LASTEXITCODE)" }     # 1-7 = success variants

if (Test-Path $zip) { Remove-Item -Force $zip }
& $7za a -tzip -mx=9 $zip (Join-Path $app "*") | Out-Null     # Contents at the zip root
if ($LASTEXITCODE -ne 0) { throw "7za failed" }

Remove-Item -Recurse -Force $stage
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLower()
"{0} ({1:N0} MB)" -f $zip, ((Get-Item $zip).Length / 1MB)
"SHA256: $hash"
