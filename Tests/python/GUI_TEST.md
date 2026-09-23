# GUI test pass

Inputs: `__fixtures__` (run `make_fixtures.ps1`). Outputs: `__outputs__\<scenario>`. Check with `python check_outputs.py`.

## Setup
- Settings → General → Models path: `__fixtures__\models`. Select model `realesr-general-x4v3`.
- AI: ESRGAN (Pytorch), embedded Python runtime.
- Overwrite: No. Post-resize: 100%.

## Scenarios
| Scenario | Steps | Expect |
|---|---|---|
| `batch-png` | Batch tab, drop `__fixtures__\batch` folder. Format PNG, Keep structure. Output dir `__outputs__\batch-png`. Upscale. | All inputs 4×, `a\` and `b\` subfolders kept, UI responsive, no hang at end |
| `batch-same` | Same, format Same as source, output `__outputs__\batch-same` | Extensions kept (.jpg .png .webp .bmp .tga .dds), `r2image.200` and `zażółć ñ` intact, no "format not supported" popups |
| `batch-root-jpeg` | Same, format JPEG, Copy to root, output `__outputs__\batch-root-jpeg` | Flat output, colours correct (no red/blue swap) |
| cancel | Start `batch-png` again into `__outputs__\cancel`, cancel mid-run, then rerun `batch-png` | Cancel stops promptly; rerun completes; no files from the cancelled run in `batch-png` |
| NCNN | Repeat `batch-png` with Real-ESRGAN (NCNN) into `__outputs__\batch-ncnn` | Progress shows (comma locale), completes, `a\` and `b\` subfolders upscaled too |
| `single` | Copy `__fixtures__\single\landscape.png` to `__outputs__\single\`, drop it on Preview, Upscale And Save | `landscape-<model>.png` 1024×768 next to it |
| preview | Same image: Refresh preview (cutout), Full image, clipboard comparison, Save merged preview | Previews render, no errors |
| `video-mp4` | Video tab, drop `__fixtures__\video\clip.mp4`, output `__outputs__\video-mp4`, format MP4 | 640×360, 29.97 fps exact, audio kept, 3 s |
| `video-mkv` | Drop `clip.mkv`, format Same as source, output `__outputs__\video-mkv` | Falls back to MP4 with a log line, no crash |
| JXL out | Batch, format JXL, quality 90, then again with Lossless ticked, output `__outputs__\batch-jxl` / `batch-jxl-lossless` | `.jxl` files; lossless ones larger. Also run `batch-png` with Pre-Processing disabled: `jpegxl.jxl` (and `.tga`) still upscale |
| DDS out | Batch, format DDS, output `__outputs__\batch-dds` | `.dds` files, no `.tga` |
| close | Start a batch, close Cupscale | No python/ffmpeg/ncnn processes left in Task Manager |

Report anything unexpected with `CupscaleData\sessionlog.txt`.
