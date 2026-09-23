<#
Builds the embedded Python runtime package (py.7z) used by the ESRGAN (PyTorch) backend.

Output: <OutDir>\py.7z containing a top-level "py" folder with a relocatable CPython,
torch (CUDA wheel), OpenCV, NumPy and the ONNX packages needed by pth2ncnn.

Install it by hosting py.7z and setting "pythonRuntimeUrl" (Turing or newer) or
"pythonRuntimeUrlLegacy" (older GPUs) in config.json to its URL, or by extracting it into
CupscaleData\bin (so that bin\py\python.exe exists).

CUDA builds: cu128 covers sm_75 (RTX 20) .. sm_120 (RTX 50). For GTX 900/10 use -Cuda cu126.

Requires: uv, 7-Zip (7z.exe/7za.exe in PATH, or the bundled Code\Resources\7za.exe).
#>
param(
    [string]$OutDir = (Join-Path $PSScriptRoot "..\build\python-runtime"),
    [string]$PythonVersion = "3.12",
    [string]$Cuda = "cu128",
    [string]$TorchVersion = ""      # e.g. "2.8.0"; empty = latest for the CUDA index
)

$ErrorActionPreference = "Stop"
$OutDir = [IO.Path]::GetFullPath($OutDir)
$pyDir = Join-Path $OutDir "py"
$archive = Join-Path $OutDir "py.7z"

if (Test-Path $pyDir) { Remove-Item -Recurse -Force $pyDir }
New-Item -ItemType Directory -Force $OutDir | Out-Null

# Relocatable CPython from python-build-standalone via uv
$installDir = Join-Path $OutDir "uv-python"
uv python install $PythonVersion --install-dir $installDir
if ($LASTEXITCODE -ne 0) { throw "uv python install failed" }
$srcPython = Get-ChildItem $installDir -Directory | Where-Object { Test-Path (Join-Path $_.FullName "python.exe") } | Select-Object -First 1
if (-not $srcPython) { throw "No python.exe found under $installDir" }
Copy-Item -Recurse $srcPython.FullName $pyDir
$python = Join-Path $pyDir "python.exe"

$torch = if ($TorchVersion) { "torch==$TorchVersion" } else { "torch" }

uv pip install --python $python --break-system-packages --index-url "https://download.pytorch.org/whl/$Cuda" $torch
if ($LASTEXITCODE -ne 0) { throw "torch install failed" }

uv pip install --python $python --break-system-packages numpy opencv-python-headless onnx onnxoptimizer
if ($LASTEXITCODE -ne 0) { throw "package install failed" }

& $python -c "import torch, cv2, numpy, onnx, onnxoptimizer; print('torch', torch.__version__, 'cuda', torch.version.cuda, 'archs', torch.cuda.get_arch_list())"
if ($LASTEXITCODE -ne 0) { throw "runtime import check failed" }

# Slim down: bytecode caches, test suites, headers/static libs not needed at runtime
Get-ChildItem $pyDir -Recurse -Directory -Filter "__pycache__" | Remove-Item -Recurse -Force
foreach ($rel in "Lib\test", "Lib\site-packages\torch\include", "Lib\site-packages\torch\test") {
    $p = Join-Path $pyDir $rel
    if (Test-Path $p) { Remove-Item -Recurse -Force $p }
}
Get-ChildItem (Join-Path $pyDir "Lib\site-packages\torch\lib") -Filter "*.lib" -ErrorAction SilentlyContinue | Remove-Item -Force

$sevenZip = (Get-Command 7z, 7za -ErrorAction SilentlyContinue | Select-Object -First 1).Source
if (-not $sevenZip) { $sevenZip = Join-Path $PSScriptRoot "..\Code\Resources\7za.exe" }
if (Test-Path $archive) { Remove-Item -Force $archive }

Push-Location $OutDir
try {
    & $sevenZip a -t7z -mx=7 -mmt=on $archive "py" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "7-Zip failed" }
}
finally {
    Pop-Location
}

"Built $archive ({0:N0} MB)" -f ((Get-Item $archive).Length / 1MB)
