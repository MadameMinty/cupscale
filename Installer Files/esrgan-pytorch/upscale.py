#!/usr/bin/env python3
# -*- coding: utf-8 -*-

import argparse
import logging
import sys
from collections import OrderedDict
from concurrent.futures import ThreadPoolExecutor
from enum import Enum
from pathlib import Path
from typing import List, Optional, Union

import cv2
import numpy as np
import torch

import utils.dataops as ops
from utils.architecture.RRDB import RRDBNet as ESRGAN
from utils.architecture.SPSR import SPSRNet as SPSR
from utils.architecture.SRVGG import SRVGGNetCompact as RealESRGANv2

inference_mode = getattr(torch, "inference_mode", torch.no_grad)  # torch < 1.9 fallback

ESRGAN_KEYS = ("model.1.sub.0.RDB1.conv1.0.weight", "RRDB_trunk.0.RDB1.conv1.weight", "body.0.rdb1.conv1.weight")


def is_esrgan(state_dict: dict) -> bool:
    for key in ("params_ema", "params"):
        if key in state_dict and isinstance(state_dict[key], dict):
            state_dict = state_dict[key]
            break
    return any(k in state_dict for k in ESRGAN_KEYS)


class SpandrelModel(torch.nn.Module):
    """spandrel model (RealPLKSR, OmniSR, SRFormer, SPAN, DAT, HAT...) with the plain nn.Module call used here.
    Pads to the model's size requirements and stays in fp32 if it doesn't support fp16."""

    def __init__(self, descriptor):
        super().__init__()
        self.descriptor = descriptor
        self.net = descriptor.model  # Registered, so .to()/.eval() reach it

    def half(self):
        return super().half() if self.descriptor.supports_half else self

    def forward(self, x):
        return self.descriptor(x.to(next(self.net.parameters()).dtype))


def load_spandrel(state_dict: dict):
    import warnings

    warnings.filterwarnings("ignore", message=r".*torch\.jit\.script.*", category=FutureWarning)  # Harmless on Python 3.14
    import spandrel

    try:
        import spandrel_extra_arches  # Restrictive-license archs, e.g. SRFormer

        spandrel_extra_arches.install(ignore_duplicates=True)
    except ImportError:
        pass

    descriptor = spandrel.ModelLoader().load_from_state_dict(state_dict)
    if not isinstance(descriptor, spandrel.ImageModelDescriptor):
        raise ValueError(f"{descriptor.architecture.name} models are not supported (not image-to-image)")
    return SpandrelModel(descriptor)


class SeamlessOptions(str, Enum):
    TILE = "tile"
    MIRROR = "mirror"
    REPLICATE = "replicate"
    ALPHA_PAD = "alpha_pad"


class AlphaOptions(str, Enum):
    NO_ALPHA = "none"
    BG_DIFFERENCE = "bg_difference"
    ALPHA_SEPARATELY = "separate"
    SWAPPING = "swapping"


class Upscale:
    model_str: str = None
    input: Path = None
    output: Path = None
    reverse: bool = None
    skip_existing: bool = None
    delete_input: bool = None
    seamless: SeamlessOptions = None
    cpu: bool = None
    fp16: bool = None
    # device_id: int = None
    cache_max_split_depth: bool = None
    binary_alpha: bool = None
    ternary_alpha: bool = None
    alpha_threshold: float = None
    alpha_boundary_offset: float = None
    alpha_mode: AlphaOptions = None
    log: logging.Logger = None

    device: torch.device = None
    in_nc: int = None
    out_nc: int = None
    last_model: str = None
    last_in_nc: int = None
    last_out_nc: int = None
    last_nf: int = None
    last_nb: int = None
    last_scale: int = None
    last_kind: str = None
    model: Union[torch.nn.Module, ESRGAN, RealESRGANv2, SPSR] = None

    def __init__(
        self,
        model: str,
        input: Path,
        output: Path,
        reverse: bool = False,
        skip_existing: bool = False,
        delete_input: bool = False,
        seamless: Optional[SeamlessOptions] = None,
        cpu: bool = False,
        fp16: bool = False,
        device_id: int = 0,
        cache_max_split_depth: bool = False,
        binary_alpha: bool = False,
        ternary_alpha: bool = False,
        alpha_threshold: float = 0.5,
        alpha_boundary_offset: float = 0.2,
        alpha_mode: Optional[AlphaOptions] = None,
        log: logging.Logger = logging.getLogger(),
    ) -> None:
        self.model_str = model
        self.input = input.resolve()
        self.output = output.resolve()
        self.reverse = reverse
        self.skip_existing = skip_existing
        self.delete_input = delete_input
        self.seamless = seamless
        self.cpu = cpu
        self.fp16 = fp16
        self.device = torch.device("cpu" if self.cpu else f"cuda:{device_id}")
        self.cache_max_split_depth = cache_max_split_depth
        self.binary_alpha = binary_alpha
        self.ternary_alpha = ternary_alpha
        self.alpha_threshold = alpha_threshold
        self.alpha_boundary_offset = alpha_boundary_offset
        self.alpha_mode = alpha_mode
        self.log = log
        self.model_cache = {}

    def run(self) -> None:
        model_chain = (
            self.model_str.split("+")
            if "+" in self.model_str
            else self.model_str.split(">")
        )

        for idx, model in enumerate(model_chain):

            interpolations = (
                model.split("|") if "|" in self.model_str else model.split("&")
            )

            if len(interpolations) > 1:
                for i, interpolation in enumerate(interpolations):
                    interp_model, interp_amount = (
                        interpolation.split("@")
                        if "@" in interpolation
                        else interpolation.split(":")
                    )
                    interp_model = self.__check_model_path(interp_model)
                    interpolations[i] = f"{interp_model}@{interp_amount}"
                model_chain[idx] = "&".join(interpolations)
            else:
                model_chain[idx] = self.__check_model_path(model)

        if not self.input.exists():
            self.log.error(f'Folder "{self.input}" does not exist.')
            sys.exit(1)
        elif self.input.is_file():
            self.log.error(f'Folder "{self.input}" is a file.')
            sys.exit(1)
        elif self.output.is_file():
            self.log.error(f'Folder "{self.output}" is a file.')
            sys.exit(1)
        elif not self.output.exists():
            self.output.mkdir(parents=True)

        print(
            'Model{:s}: "{:s}"'.format(
                "s" if len(model_chain) > 1 else "",
                # ", ".join([Path(x).stem for x in model_chain]),
                ", ".join([x for x in model_chain]),
            )
        )


        images: List[Path] = []
        for ext in ["png", "jpg", "jpeg", "gif", "bmp", "tiff", "tga"]:
            images.extend(self.input.glob(f"**/*.{ext}"))

        # Store the maximum split depths for each model in the chain
        # TODO: there might be a better way of doing this but it's good enough for now
        split_depths = {}

        writer = ThreadPoolExecutor(max_workers=2)
        pending_writes = []

        for idx, img_path in enumerate(images, 1):
            img_input_path_rel = img_path.relative_to(self.input)
            output_dir = self.output.joinpath(img_input_path_rel).parent
            img_output_path_rel = output_dir.joinpath(f"{img_path.stem}.png")
            output_dir.mkdir(parents=True, exist_ok=True)
            if len(model_chain) == 1:
                self.log.info(
                    f'Processing... {str(idx).zfill(len(str(len(images))))}: "{img_input_path_rel}"'
                )
            if self.skip_existing and img_output_path_rel.is_file():
                self.log.warning("Already exists, skipping")
                if self.delete_input:
                    img_path.unlink(missing_ok=True)
                continue
            # read image
            img = ops.imread_unicode(img_path.absolute())
            if img is None:
                self.log.error(f'Could not read "{img_input_path_rel}", skipping.')
                continue
            if len(img.shape) < 3:
                img = cv2.cvtColor(img, cv2.COLOR_GRAY2BGR)

            # Seamless modes
            if self.seamless == SeamlessOptions.TILE:
                img = cv2.copyMakeBorder(img, 16, 16, 16, 16, cv2.BORDER_WRAP)
            elif self.seamless == SeamlessOptions.MIRROR:
                img = cv2.copyMakeBorder(
                    img, 16, 16, 16, 16, cv2.BORDER_REFLECT_101
                )
            elif self.seamless == SeamlessOptions.REPLICATE:
                img = cv2.copyMakeBorder(img, 16, 16, 16, 16, cv2.BORDER_REPLICATE)
            elif self.seamless == SeamlessOptions.ALPHA_PAD:
                img = cv2.copyMakeBorder(
                    img, 16, 16, 16, 16, cv2.BORDER_CONSTANT, value=[0, 0, 0, 0]
                )
            final_scale: int = 1

            for i, model_path in enumerate(model_chain):

                img_height, img_width = img.shape[:2]

                # Load the model so we can access the scale
                self.load_model(model_path)

                if self.cache_max_split_depth and i in split_depths:
                    rlt, depth, _ = ops.auto_split_upscale(
                        img,
                        self.upscale,
                        self.last_scale,
                        max_depth=split_depths[i],
                    )
                else:
                    rlt, depth, _ = ops.auto_split_upscale(
                        img, self.upscale, self.last_scale
                    )
                    split_depths[i] = depth

                final_scale *= self.last_scale

                # This is for model chaining
                img = rlt.astype("uint8")

            if self.seamless:
                rlt = self.crop_seamless(rlt, final_scale)

            if rlt.dtype != np.uint8:
                rlt = np.clip(rlt, 0, 255).astype(np.uint8)

            # Encode on a worker thread so the GPU can start the next image
            while len(pending_writes) >= 4:  # Bound memory held by queued results
                pending_writes.pop(0).result()
            pending_writes.append(writer.submit(self.__write_output, img_output_path_rel.absolute(), rlt, img_path))

        for future in pending_writes:
            future.result()
        writer.shutdown()

    def __write_output(self, path: Path, img: np.ndarray, source: Path) -> None:
        # Intermediate file (Cupscale re-encodes it), so favor speed over size
        ops.imwrite_unicode(path, img, [cv2.IMWRITE_PNG_COMPRESSION, 1])
        if self.delete_input:
            source.unlink(missing_ok=True)


    def __check_model_path(self, model_path: str) -> str:
        if Path(model_path).is_file():
            return model_path
        elif Path("./models/").joinpath(model_path).is_file():
            return str(Path("./models/").joinpath(model_path))
        else:
            self.log.error(f'Model "{model_path}" does not exist.')
            sys.exit(1)

    # This code is a somewhat modified version of BlueAmulet's fork of ESRGAN by Xinntao
    def process(self, img: np.ndarray, to_uint8: bool = False) -> np.ndarray:
        """
        Runs the model on an HWC BGR(A) image. Integer images are normalized on the device.

                Parameters:
                        img (array): float image in [0, 1], or uint8/uint16 image
                        to_uint8 (bool): return uint8 in [0, 255] (converted on the device) instead of float in [0, 1]

                Returns:
                        rlt (array): The processed image
        """
        if img.shape[2] == 3:
            img = img[:, :, [2, 1, 0]]
        elif img.shape[2] == 4:
            img = img[:, :, [2, 1, 0, 3]]

        max_val = 1.0
        if np.issubdtype(img.dtype, np.integer):
            max_val = float(np.iinfo(img.dtype).max)
            if img.dtype != np.uint8:  # torch lacks general uint16 support
                img = img.astype(np.float32)

        tensor = torch.from_numpy(np.ascontiguousarray(np.transpose(img, (2, 0, 1))))
        tensor = tensor.to(self.device, non_blocking=True).float()
        if max_val != 1.0:
            tensor = tensor.div_(max_val)
        if self.fp16:
            tensor = tensor.half()

        with inference_mode():  # In-place ops on its outputs are only allowed inside it
            output = self.model(tensor.unsqueeze(0)).squeeze(0).float().clamp_(0, 1)

            if output.shape[0] == 3:
                output = output[[2, 1, 0], :, :]
            elif output.shape[0] == 4:
                output = output[[2, 1, 0, 3], :, :]
            if to_uint8:
                output = output.mul_(255.0).round_().to(torch.uint8)
            return output.permute(1, 2, 0).cpu().numpy()

    def load_model(self, model_path: str):
        if model_path == self.last_model:
            return

        if model_path not in self.model_cache:
            self.model_cache[model_path] = self.__build_model(model_path)

        (
            self.model,
            self.last_in_nc,
            self.last_out_nc,
            self.last_nf,
            self.last_nb,
            self.last_scale,
        ) = self.model_cache[model_path]
        self.last_model = model_path

    def __build_model(self, model_path: str):
        # interpolating OTF, example: 4xBox@25&4xPSNR@75
        if (":" in model_path or "@" in model_path) and (
            "&" in model_path or "|" in model_path
        ):
            interps = model_path.split("&")[:2]
            model_1 = ops.load_state_dict(interps[0].split("@")[0])
            model_2 = ops.load_state_dict(interps[1].split("@")[0])
            state_dict = ops.interpolate_state_dicts(
                model_1, model_2, int(interps[0].split("@")[1]) / 100, int(interps[1].split("@")[1]) / 100
            )
        else:
            state_dict = ops.load_state_dict(model_path)

        # SRVGGNet Real-ESRGAN (v2)
        if any(
            key in state_dict and "body.0.weight" in state_dict[key]
            for key in ("params_ema", "params")
        ):
            model = RealESRGANv2(state_dict)
            info = (model.in_nc, model.out_nc, model.num_feat, model.num_conv, model.scale)
        # SPSR (ESRGAN with lots of extra layers)
        elif "f_HR_conv1.0.weight" in state_dict:
            model = SPSR(state_dict)
            info = (model.in_nc, model.out_nc, model.num_filters, model.num_blocks, model.scale)
        # Regular ESRGAN, "new-arch" ESRGAN, Real-ESRGAN v1
        elif is_esrgan(state_dict):
            model = ESRGAN(state_dict)
            info = (model.in_nc, model.out_nc, model.num_filters, model.num_blocks, model.scale)
        # Anything else spandrel knows
        else:
            try:
                model = load_spandrel(state_dict)
            except ImportError:
                self.log.error("This model needs spandrel, which is missing from the Python environment.")
                sys.exit(1)
            d = model.descriptor
            info = (d.input_channels, d.output_channels, 0, 0, d.scale)

        del state_dict
        model.eval()
        for _, v in model.named_parameters():
            v.requires_grad = False
        model = model.to(self.device)
        if self.fp16:
            model = model.half()
        return (model,) + info

    # This code is a somewhat modified version of BlueAmulet's fork of ESRGAN by Xinntao
    def upscale(self, img: np.ndarray) -> np.ndarray:
        """
        Upscales the image passed in with the specified model

                Parameters:
                        img: The image to upscale
                        model_path (string): The model to use

                Returns:
                        output: The processed image
        """

        needs_alpha_handling = (
            img.ndim == 3
            and img.shape[2] == 4
            and self.last_in_nc == 3
            and self.last_out_nc == 3
        )

        if not needs_alpha_handling:  # Fast path: normalize and quantize on the device
            if img.ndim == 2:
                img = np.tile(
                    np.expand_dims(img, axis=2), (1, 1, min(self.last_in_nc, 3))
                )
            if img.shape[2] > self.last_in_nc:  # remove extra channels
                self.log.warning("Truncating image channels")
                img = img[:, :, : self.last_in_nc]
            # pad with solid alpha channel
            elif img.shape[2] == 3 and self.last_in_nc == 4:
                img = np.dstack((img, np.full(img.shape[:-1], np.iinfo(img.dtype).max, img.dtype)))
            return self.process(img, to_uint8=True)

        img = img * 1.0 / np.iinfo(img.dtype).max

        if needs_alpha_handling:

            # Fill alpha with white and with black, remove the difference
            if self.alpha_mode == AlphaOptions.BG_DIFFERENCE:
                img1 = np.copy(img[:, :, :3])
                img2 = np.copy(img[:, :, :3])
                for c in range(3):
                    img1[:, :, c] *= img[:, :, 3]
                    img2[:, :, c] = (img2[:, :, c] - 1) * img[:, :, 3] + 1

                output1 = self.process(img1)
                output2 = self.process(img2)
                alpha = 1 - np.mean(output2 - output1, axis=2)
                output = np.dstack((output1, alpha))
                output = np.clip(output, 0, 1)
            # Upscale the alpha channel itself as its own image
            elif self.alpha_mode == AlphaOptions.ALPHA_SEPARATELY:
                img1 = np.copy(img[:, :, :3])
                img2 = cv2.merge((img[:, :, 3], img[:, :, 3], img[:, :, 3]))
                output1 = self.process(img1)
                output2 = self.process(img2)
                output = cv2.merge(
                    (
                        output1[:, :, 0],
                        output1[:, :, 1],
                        output1[:, :, 2],
                        output2[:, :, 0],
                    )
                )
            # Use the alpha channel like a regular channel
            elif self.alpha_mode == AlphaOptions.SWAPPING:
                img1 = cv2.merge((img[:, :, 0], img[:, :, 1], img[:, :, 2]))
                img2 = cv2.merge((img[:, :, 1], img[:, :, 2], img[:, :, 3]))
                output1 = self.process(img1)
                output2 = self.process(img2)
                output = cv2.merge(
                    (
                        output1[:, :, 0],
                        output1[:, :, 1],
                        output1[:, :, 2],
                        output2[:, :, 2],
                    )
                )
            # Remove alpha
            else:
                img1 = np.copy(img[:, :, :3])
                output = self.process(img1)
                output = cv2.cvtColor(output, cv2.COLOR_BGR2BGRA)

            if self.binary_alpha:
                alpha = output[:, :, 3]
                threshold = self.alpha_threshold
                _, alpha = cv2.threshold(alpha, threshold, 1, cv2.THRESH_BINARY)
                output[:, :, 3] = alpha
            elif self.ternary_alpha:
                alpha = output[:, :, 3]
                half_transparent_lower_bound = (
                    self.alpha_threshold - self.alpha_boundary_offset
                )
                half_transparent_upper_bound = (
                    self.alpha_threshold + self.alpha_boundary_offset
                )
                alpha = np.where(
                    alpha < half_transparent_lower_bound,
                    0,
                    np.where(alpha <= half_transparent_upper_bound, 0.5, 1),
                )
                output[:, :, 3] = alpha

        output = (output * 255.0).round()

        return output

    def crop_seamless(self, img: np.ndarray, scale: int) -> np.ndarray:
        img_height, img_width = img.shape[:2]
        y, x = 16 * scale, 16 * scale
        h, w = img_height - (32 * scale), img_width - (32 * scale)
        img = img[y : y + h, x : x + w]
        return img




if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument('model')
    parser.add_argument('--input', default='input', help='Input folder', type=Path)
    parser.add_argument('--output', default='output', help='Output folder', type=Path)
    parser.add_argument('--reverse', help='Reverse Order', action="store_true")
    parser.add_argument('--skip_existing', action="store_true",
                        help='Skip existing output files')
    parser.add_argument('--seamless', nargs='?', choices=['tile', 'mirror', 'replicate', 'alpha_pad'], default=None,
                        help='Helps seamlessly upscale an image. Tile = repeating along edges. Mirror = reflected along edges. Replicate = extended pixels along edges. Alpha pad = extended alpha border.')
    parser.add_argument('--cpu', action='store_true',
                        help='Use CPU instead of CUDA')
    parser.add_argument('--device_id', help='The numerical ID of the GPU you want to use. Defaults to 0.',
                        type=int, nargs='?', default=0)
    parser.add_argument('--fp16', action='store_true',
                        help='Use FloatingPoint16/Halftensor type for images')
    parser.add_argument('--cache_max_split_depth', action='store_true',
                        help='Caches the maximum recursion depth used by the split/merge function. Useful only when upscaling images of the same size.')
    parser.add_argument('--binary_alpha', action='store_true',
                        help='Whether to use a 1 bit alpha transparency channel, Useful for PSX upscaling')
    parser.add_argument('--ternary_alpha', action='store_true',
                        help='Whether to use a 2 bit alpha transparency channel, Useful for PSX upscaling')
    parser.add_argument('--alpha_threshold', default=.5,
                        help='Only used when binary_alpha is supplied. Defines the alpha threshold for binary transparency', type=float)
    parser.add_argument('--alpha_boundary_offset', default=.2,
                        help='Only used when binary_alpha is supplied. Determines the offset boundary from the alpha threshold for half transparency.', type=float)
    parser.add_argument('--alpha_mode', help='Type of alpha processing to use. 0 is no alpha processing. 1 is BA\'s difference method. 2 is upscaling the alpha channel separately (like IEU). 3 is swapping an existing channel with the alpha channel.',
                        type=int, nargs='?', choices=[0, 1, 2, 3], default=0)
    args = parser.parse_args()


    logging.basicConfig(
        level=logging.INFO,
        format="%(message)s",
        filename="prog",
        filemode="w",
        #datefmt="[%X]",
        #handlers=[RichHandler(markup=True)],
        # handlers=[RichHandler(markup=True, rich_tracebacks=True)],
    )

    upscale = Upscale(
        model=args.model,
        input=args.input,
        output=args.output,
        reverse=args.reverse,
        skip_existing=args.skip_existing,
        delete_input=False,
        seamless=SeamlessOptions(args.seamless) if args.seamless != None else None,
        cpu=args.cpu,
        fp16=args.fp16,
        device_id=args.device_id,
        cache_max_split_depth=args.cache_max_split_depth,
        binary_alpha=args.binary_alpha,
        ternary_alpha=args.ternary_alpha,
        alpha_threshold=args.alpha_threshold,
        alpha_boundary_offset=args.alpha_boundary_offset,
        alpha_mode=AlphaOptions[AlphaOptions._member_names_[args.alpha_mode]],
    )
    upscale.run()
