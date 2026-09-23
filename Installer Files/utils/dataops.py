#!/usr/bin/env python3
# -*- coding: utf-8 -*-

import gc
import warnings
from pathlib import Path

import cv2
import numpy as np
import torch
import logging


def imread_unicode(path) -> np.ndarray:
    """cv2.imread that works with non-ASCII Windows paths. Returns None if unreadable."""
    data = np.fromfile(str(path), dtype=np.uint8)
    if data.size == 0:
        return None
    return cv2.imdecode(data, cv2.IMREAD_UNCHANGED)


def imwrite_unicode(path, img: np.ndarray, params=None) -> None:
    """cv2.imwrite that works with non-ASCII Windows paths."""
    ok, buf = cv2.imencode(Path(path).suffix or ".png", img, params or [])
    if not ok:
        raise IOError(f"Failed to encode {path}")
    buf.tofile(str(path))


def load_state_dict(path: str):
    """torch.load to CPU without executing pickled code; falls back for legacy checkpoints."""
    try:
        return torch.load(path, map_location="cpu", weights_only=True)
    except TypeError:  # torch < 1.13 has no weights_only
        return torch.load(path, map_location="cpu")
    except Exception as e:
        warnings.warn(f'"{path}" is not a plain state dict ({e}); loading it with full unpickling.')
        return torch.load(path, map_location="cpu", weights_only=False)


def bgr_to_rgb(image: torch.Tensor) -> torch.Tensor:
    # flip image channels
    # https://github.com/pytorch/pytorch/issues/229
    out: torch.Tensor = image.flip(-3)
    # out: torch.Tensor = image[[2, 1, 0], :, :] #RGB to BGR #may be faster
    return out


def rgb_to_bgr(image: torch.Tensor) -> torch.Tensor:
    # same operation as bgr_to_rgb(), flip image channels
    return bgr_to_rgb(image)


def bgra_to_rgba(image: torch.Tensor) -> torch.Tensor:
    out: torch.Tensor = image[[2, 1, 0, 3], :, :]
    return out


def rgba_to_bgra(image: torch.Tensor) -> torch.Tensor:
    # same operation as bgra_to_rgba(), flip image channels
    return bgra_to_rgba(image)


def auto_split_upscale(
    lr_img: np.ndarray,
    upscale_function,
    scale: int = 4,
    overlap: int = 32,
    max_depth: int = None,
    current_depth: int = 1,
    current_tile_num: int = 1
):

    # Attempt to upscale if unknown depth or if reached known max depth
    if max_depth is None or max_depth == current_depth:
        try:
            result = upscale_function(lr_img)
            print(f'Tile {current_tile_num}/{4 ** (max_depth-1) if max_depth is not None else 4 ** (current_depth-1)}')
            logging.info("Tile %d/%d" % (current_tile_num, (4 ** (max_depth-1) if max_depth is not None else 4 ** (current_depth-1))))
            return result, current_depth, current_tile_num+1
        except RuntimeError as e:
            # Check to see if its actually the CUDA out of memory error
            if "CUDA" in str(e):
                # Collect garbage (clear VRAM)
                torch.cuda.empty_cache()
                gc.collect()
            # Re-raise the exception if not an OOM error
            else:
                raise RuntimeError(e)

    h, w, c = lr_img.shape

    # Split image into 4ths
    top_left = lr_img[: h // 2 + overlap, : w // 2 + overlap, :]
    top_right = lr_img[: h // 2 + overlap, w // 2 - overlap :, :]
    bottom_left = lr_img[h // 2 - overlap :, : w // 2 + overlap, :]
    bottom_right = lr_img[h // 2 - overlap :, w // 2 - overlap :, :]

    # Recursively upscale the quadrants
    # After we go through the top left quadrant, we know the maximum depth and no longer need to test for out-of-memory
    top_left_rlt, depth, current_tile_num = auto_split_upscale(
        top_left,
        upscale_function,
        scale=scale,
        overlap=overlap,
        max_depth=max_depth,
        current_depth=current_depth + 1,
        current_tile_num=current_tile_num
    )
    top_right_rlt, _, current_tile_num = auto_split_upscale(
        top_right,
        upscale_function,
        scale=scale,
        overlap=overlap,
        max_depth=depth,
        current_depth=current_depth + 1,
        current_tile_num=current_tile_num
    )
    bottom_left_rlt, _, current_tile_num = auto_split_upscale(
        bottom_left,
        upscale_function,
        scale=scale,
        overlap=overlap,
        max_depth=depth,
        current_depth=current_depth + 1,
        current_tile_num=current_tile_num
    )
    bottom_right_rlt, _, current_tile_num = auto_split_upscale(
        bottom_right,
        upscale_function,
        scale=scale,
        overlap=overlap,
        max_depth=depth,
        current_depth=current_depth + 1,
        current_tile_num=current_tile_num
    )

    # Define output shape
    out_h = h * scale
    out_w = w * scale

    # Create blank output image
    output_img = np.zeros((out_h, out_w, c), np.uint8)

    # Fill output image with tiles, cropping out the overlaps
    output_img[: out_h // 2, : out_w // 2, :] = top_left_rlt[
        : out_h // 2, : out_w // 2, :
    ]
    output_img[: out_h // 2, -out_w // 2 :, :] = top_right_rlt[
        : out_h // 2, -out_w // 2 :, :
    ]
    output_img[-out_h // 2 :, : out_w // 2, :] = bottom_left_rlt[
        -out_h // 2 :, : out_w // 2, :
    ]
    output_img[-out_h // 2 :, -out_w // 2 :, :] = bottom_right_rlt[
        -out_h // 2 :, -out_w // 2 :, :
    ]

    return output_img, depth, current_tile_num
