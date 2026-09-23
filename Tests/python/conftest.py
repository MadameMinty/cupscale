import sys
from pathlib import Path

root = Path(__file__).resolve().parents[2] / "Installer Files"
sys.path[:0] = [str(root), str(root / "esrgan-pytorch")]
