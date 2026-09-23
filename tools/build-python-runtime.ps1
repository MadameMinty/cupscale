<#
Builds the embedded Python runtime package (py.7z) used by the ESRGAN (PyTorch) backend.

Output: <OutDir>\py.7z containing a top-level "py" folder with a relocatable CPython,
torch + torchvision (CUDA wheels), spandrel, OpenCV, NumPy and the ONNX packages needed by pth2ncnn.

Install it by hosting py.7z and setting "pythonRuntimeUrl" (Turing or newer) or
"pythonRuntimeUrlLegacy" (older GPUs) in config.json to its URL, or by extracting it into
CupscaleData\bin (so that bin\py\python.exe exists).

CUDA builds: cu132/cu128 cover sm_75 (RTX 20) .. sm_120 (RTX 50). For GTX 900/10 use -Cuda cu126.
-Cpu builds a CPU-only torch instead (py-cpu.7z; not part of regular releases).

Requires: uv, 7-Zip (7z.exe/7za.exe in PATH, or the bundled Code\Resources\7za.exe).
#>
param(
    [string]$OutDir = (Join-Path $PSScriptRoot "..\build\python-runtime"),
    [string]$PythonVersion = "3.14",
    [string]$Cuda = "cu132",       # Needs NVIDIA driver 580+; cu128 works from 570
    [switch]$Cpu,                  # CPU-only torch; overrides -Cuda
    [string]$TorchVersion = ""      # e.g. "2.8.0"; empty = latest for the CUDA index
)

$ErrorActionPreference = "Stop"
$OutDir = [IO.Path]::GetFullPath($OutDir)
$pyDir = Join-Path $OutDir "py"
$archive = Join-Path $OutDir ($(if ($Cpu) { "py-cpu.7z" } else { "py.7z" }))
$torchIndex = if ($Cpu) { "cpu" } else { $Cuda }

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

# torchvision (needed by spandrel) must come from the same index to match torch
uv pip install --python $python --break-system-packages --index-url "https://download.pytorch.org/whl/$torchIndex" $torch torchvision
if ($LASTEXITCODE -ne 0) { throw "torch install failed" }

# spandrel: non-ESRGAN architectures (RealPLKSR, OmniSR, SPAN, DAT, HAT...); extra arches: SRFormer etc.
uv pip install --python $python --break-system-packages numpy opencv-python-headless onnx onnxoptimizer spandrel spandrel_extra_arches safetensors
if ($LASTEXITCODE -ne 0) { throw "package install failed" }

# Slim down: bytecode caches, tests, headers/static libs, Tk/IDLE, packaging tools and entry-point exes
Get-ChildItem $pyDir -Recurse -Directory -Filter "__pycache__" | Remove-Item -Recurse -Force
$prune = @(
    "include", "libs", "share", "Scripts",
    "Lib\test", "Lib\idlelib", "Lib\tkinter", "Lib\turtledemo", "Lib\ensurepip", "Lib\turtle.py",
    "DLLs\tcl90.dll", "DLLs\tcl9tk90.dll", "DLLs\libtommath.dll", "DLLs\_tkinter.pyd", "DLLs\_ctypes_test.pyd",
    "Lib\site-packages\pip", "Lib\site-packages\setuptools", "Lib\site-packages\pkg_resources",
    "Lib\site-packages\_distutils_hack", "Lib\site-packages\distutils-precedence.pth",
    "Lib\site-packages\torch\include", "Lib\site-packages\torch\test"
)
foreach ($rel in $prune) {
    $p = Join-Path $pyDir $rel
    if (Test-Path $p) { Remove-Item -Recurse -Force $p }
}
Get-ChildItem (Join-Path $pyDir "Lib") -Directory -Filter "tcl*" | Remove-Item -Recurse -Force
Get-ChildItem (Join-Path $pyDir "Lib\site-packages") -Directory -Filter "*.dist-info" |
    Where-Object { $_.Name -match '^(pip|setuptools)-' } | Remove-Item -Recurse -Force
Get-ChildItem (Join-Path $pyDir "DLLs") -Filter "_test*.pyd" | Remove-Item -Force
Get-ChildItem (Join-Path $pyDir "Lib\site-packages\torch\lib") -Filter "*.lib" -ErrorAction SilentlyContinue | Remove-Item -Force

& $python -c "import torch, torch.nn.functional, torchvision, cv2, numpy, onnx, onnxoptimizer, spandrel, spandrel_extra_arches, safetensors; print('torch', torch.__version__, 'cuda', torch.version.cuda, 'archs', torch.cuda.get_arch_list())"
if ($LASTEXITCODE -ne 0) { throw "runtime import check failed" }
Get-ChildItem $pyDir -Recurse -Directory -Filter "__pycache__" | Remove-Item -Recurse -Force     # Created by the check

$sevenZip = (Get-Command 7z, 7za -ErrorAction SilentlyContinue | Select-Object -First 1).Source
if (-not $sevenZip) { $sevenZip = Join-Path $PSScriptRoot "..\Code\Resources\7za.exe" }
if (Test-Path $archive) { Remove-Item -Force $archive }

Push-Location $OutDir
try {
    & $sevenZip a -t7z -mx=8 -mmt=on $archive "py" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "7-Zip failed" }
}
finally {
    Pop-Location
}

"Built $archive ({0:N0} MB)" -f ((Get-Item $archive).Length / 1MB)
