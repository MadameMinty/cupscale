<#
Builds a release zip: build\Cupscale-<VERSION>.zip

  Cupscale\Cupscale.exe
  Cupscale\CupscaleData\bin\...   (Installer Files, incl. ffmpeg.exe)

The Python runtime (py.7z) is not included; Cupscale downloads it on demand.
Fetches ffmpeg.exe/7za.exe via fetch-binaries.ps1 if missing.
#>
$ErrorActionPreference = "Stop"
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$version = (Get-Content (Join-Path $repo "VERSION") -Raw).Trim()
$installerFiles = Join-Path $repo "Installer Files"
$7za = Join-Path $repo "Code\Resources\7za.exe"
$stage = Join-Path $repo "build\release"
$zip = Join-Path $repo "build\Cupscale-$version.zip"

$fetchArgs = @{}
if (Test-Path (Join-Path $installerFiles "ffmpeg.exe")) { $fetchArgs.SkipFfmpeg = $true }
if (Test-Path $7za) { $fetchArgs.Skip7za = $true }
if ($fetchArgs.Count -lt 2) { & (Join-Path $PSScriptRoot "fetch-binaries.ps1") @fetchArgs }

if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
$app = Join-Path $stage "Cupscale"
$bin = Join-Path $app "CupscaleData\bin"

Remove-Item Env:MSBuildExtensionsPath -ErrorAction SilentlyContinue     # Breaks the dotnet CLI when set
dotnet publish (Join-Path $repo "Code\Cupscale.csproj") -c Release -p:Platform=x64 -o (Join-Path $stage "publish")
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

New-Item -ItemType Directory -Force $bin | Out-Null
Get-ChildItem (Join-Path $stage "publish\*") -File -Exclude *.pdb | Copy-Item -Destination $app
if (-not (Test-Path (Join-Path $app "Cupscale.exe"))) { throw "Cupscale.exe missing from publish output" }
robocopy $installerFiles $bin /E /XD __pycache__ /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw "robocopy failed ($LASTEXITCODE)" }     # 1-7 = success variants

if (Test-Path $zip) { Remove-Item -Force $zip }
& $7za a -tzip -mx=9 $zip $app | Out-Null
if ($LASTEXITCODE -ne 0) { throw "7za failed" }

Remove-Item -Recurse -Force $stage
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLower()
"{0} ({1:N0} MB)" -f $zip, ((Get-Item $zip).Length / 1MB)
"SHA256: $hash"
