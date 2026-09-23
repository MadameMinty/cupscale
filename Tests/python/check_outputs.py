"""Checks GUI test results in __outputs__ (see GUI_TEST.md). Needs magick and ffprobe in PATH. Usage: python check_outputs.py"""
import json
import subprocess
import sys
from pathlib import Path

HERE = Path(__file__).parent
FIX = HERE / "__fixtures__"
OUT = HERE / "__outputs__"
SCALE = 4
failures = []


def check(ok, msg):
    print(("  ok   " if ok else "  FAIL ") + msg)
    if not ok:
        failures.append(msg)


def identify(path):
    w, h = subprocess.check_output(["magick", "identify", "-format", "%w %h", f"{path}[0]"], text=True).split()
    return int(w), int(h)


def mean_rgb(path, region):
    x, y, w, h = region
    out = subprocess.check_output(
        ["magick", f"{path}[0]", "-crop", f"{w}x{h}+{x}+{y}", "-format", "%[fx:mean.r] %[fx:mean.g] %[fx:mean.b]", "info:"], text=True)
    return [float(v) for v in out.split()]


def batch_inputs():
    return [p for p in (FIX / "batch").rglob("*") if p.is_file()]


def find_output(folder, rel_parent, stem):
    return sorted((folder / rel_parent).glob(f"{stem}-*.*"))


def check_batch(name, same_as_source, keep_structure):
    folder = OUT / name
    print(f"[{name}]")
    if not folder.is_dir():
        print("  skip (not run)")
        return
    outputs = [p for p in folder.rglob("*") if p.is_file()]
    check(not any(p.suffix in (".tmp", ".part") for p in outputs), "no leftover .tmp/.part files")
    for src in batch_inputs():
        rel_parent = src.parent.relative_to(FIX / "batch") if keep_structure else Path()
        found = [p for p in find_output(folder, rel_parent, src.stem)
                 if not same_as_source or p.suffix.lower() == src.suffix.lower()]
        label = str(src.relative_to(FIX / "batch"))
        if not found:
            check(False, f"{label}: output missing")
            continue
        out = found[0]
        if same_as_source:
            check(out.suffix.lower() == src.suffix.lower(), f"{label}: kept extension ({out.name})")
        sw, sh = identify(src)
        ow, oh = identify(out)
        check((ow, oh) == (sw * SCALE, sh * SCALE), f"{label}: {sw}x{sh} -> {ow}x{oh}")
    if not keep_structure:
        check(not any(p.parent != folder for p in outputs), "copy-to-root: no subfolders")


def check_colors(name):
    folder = OUT / name
    hits = sorted(folder.rglob("same-*.*")) if folder.is_dir() else []
    if not hits:
        return
    out = hits[0]
    w, h = identify(out)
    left = mean_rgb(out, (0, 0, w // 8, h))
    check(left[0] > left[2] or left[1] > left[2], f"[{name}] {out.name}: no R/B channel swap (left edge rgb={[round(v, 2) for v in left]})")


def check_single():
    folder = OUT / "single"
    print("[single]")
    hits = sorted(folder.glob("landscape-*.*")) if folder.is_dir() else []
    if not hits:
        print("  skip (not run)")
        return
    ow, oh = identify(hits[0])
    check((ow, oh) == (256 * SCALE, 192 * SCALE), f"{hits[0].name}: {ow}x{oh}")


def probe(path):
    out = subprocess.check_output(["ffprobe", "-v", "error", "-show_streams", "-show_format", "-of", "json", str(path)], text=True)
    return json.loads(out)


def check_video(name, expect_ext):
    folder = OUT / name
    print(f"[{name}]")
    hits = sorted(folder.glob("clip*.*")) if folder.is_dir() else []
    if not hits:
        print("  skip (not run)")
        return
    out = hits[0]
    check(out.suffix.lower() == expect_ext, f"{out.name}: container {out.suffix}")
    info = probe(out)
    video = next(s for s in info["streams"] if s["codec_type"] == "video")
    check((video["width"], video["height"]) == (160 * SCALE, 90 * SCALE), f"size {video['width']}x{video['height']}")
    check(video["r_frame_rate"] == "30000/1001", f"frame rate {video['r_frame_rate']} (exact NTSC)")
    check(any(s["codec_type"] == "audio" for s in info["streams"]), "audio kept")
    duration = float(info["format"]["duration"])
    check(abs(duration - 3.0) < 0.15, f"duration {duration:.2f}s")


check_batch("batch-png", same_as_source=False, keep_structure=True)
check_batch("batch-same", same_as_source=True, keep_structure=True)
check_batch("batch-root-jpeg", same_as_source=False, keep_structure=False)
check_colors("batch-root-jpeg")
check_colors("batch-png")
check_single()
check_video("video-mp4", ".mp4")
check_video("video-mkv", ".mp4")

print(f"\n{len(failures)} failure(s)")
sys.exit(1 if failures else 0)
