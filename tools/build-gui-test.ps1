<#
Updates the local test build in build\gui-test in place:

  Cupscale.exe            freshly published
  CupscaleData\bin\...    synced from Installer Files (incl. ffmpeg.exe if fetched)
  CupscaleData\bin\py     Python runtime, only if missing or with -Runtime (from build\python-runtime)

Config, models and other CupscaleData contents are left alone.
#>
param(
    [switch]$Runtime     # Replace bin\py with build\python-runtime (py folder, else py.7z)
)

$ErrorActionPreference = "Stop"
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$dest = Join-Path $repo "build\gui-test"
$bin = Join-Path $dest "CupscaleData\bin"
$publish = Join-Path $repo "build\gui-test-publish"
$runtimeDir = Join-Path $repo "build\python-runtime"
$7za = Join-Path $repo "Code\Resources\7za.exe"

if (Get-Process Cupscale -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($dest, [StringComparison]::OrdinalIgnoreCase) }) {
    throw "Cupscale is running from $dest - close it first"
}

Remove-Item Env:MSBuildExtensionsPath -ErrorAction SilentlyContinue     # Breaks the dotnet CLI when set
dotnet publish (Join-Path $repo "Code\Cupscale.csproj") -c Release -p:Platform=x64 -o $publish -v q
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

New-Item -ItemType Directory -Force $bin | Out-Null
Copy-Item (Join-Path $publish "Cupscale.exe") $dest -Force
Remove-Item -Recurse -Force $publish

robocopy (Join-Path $repo "Installer Files") $bin /E /XD __pycache__ /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw "robocopy failed ($LASTEXITCODE)" }     # 1-7 = success variants

$py = Join-Path $bin "py"
if ($Runtime -or -not (Test-Path (Join-Path $py "python.exe"))) {
    if (Test-Path $py) { Remove-Item -Recurse -Force $py }

    if (Test-Path (Join-Path $runtimeDir "py\python.exe")) {
        robocopy (Join-Path $runtimeDir "py") $py /E /NFL /NDL /NJH /NJS /NP /MT:16 | Out-Null
        if ($LASTEXITCODE -ge 8) { throw "robocopy failed ($LASTEXITCODE)" }
    }
    elseif (Test-Path (Join-Path $runtimeDir "py.7z")) {
        & $7za x (Join-Path $runtimeDir "py.7z") "-o$bin" -y -bso0 -bsp0
        if ($LASTEXITCODE -ne 0) { throw "7za failed" }
    }
    else {
        Write-Warning "No Python runtime in $runtimeDir (run build-python-runtime.ps1); CUDA upscaling won't work"
    }
}

"{0}: Cupscale.exe {1:N0} MB, runtime: {2}" -f $dest, ((Get-Item (Join-Path $dest "Cupscale.exe")).Length / 1MB),
    $(if (Test-Path (Join-Path $py "python.exe")) { "present" } else { "missing" })
