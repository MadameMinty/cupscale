from __future__ import annotations

from typing import Any, Tuple, Union
from io import BytesIO

import onnx
import onnxoptimizer
import torch
import time
import sys

import os
import pathlib

from utils.torch_types import PyTorchModel
from utils.ncnn_model import NcnnModelWrapper
from utils.onnx_model import OnnxModel
from utils.onnx_to_ncnn import Onnx2NcnnConverter
from utils.onnx_model import OnnxModel
from utils.torch_types import PyTorchModel
from utils.pytorch_model_loading import load_state_dict
from utils.dataops import load_state_dict as safe_load_state_dict

major_version = 1
minor_version = 0
revision_version = 0
version = F"{major_version}.{minor_version}.{revision_version}"

class Timer:
    def __init__(self) -> None:
        self._start_time = time.time()
        self._end_time = None

    def end(self):
        self._end_time = time.time()

    def get_elapsed(self, rounding=2) -> Union[int, float]:
        if self._end_time is None:
            self._end_time = time.time()
        return round(self._end_time - self._start_time, rounding)

    @property
    def elapsed(self):
        return self.get_elapsed(2)

def LoadTorchModel(path, fp16=False) -> PyTorchModel:
        """Read a pth file from the specified path and return it as a state dict
        and loaded model after finding arch config"""

        assert os.path.exists(path), f"Model file at location {path} does not exist"

        assert os.path.isfile(path), f"Path {path} is not a file"

        device = "cpu"

        try:
            timer = Timer()
            print(f"Reading state dict from path: {path}")
            state_dict = safe_load_state_dict(path)
            model = load_state_dict(state_dict)

            for _, v in model.named_parameters():
                v.requires_grad = False
            model.eval()
            model = model.to(torch.device(device))
            should_use_fp16 = fp16 and model.supports_fp16
            if should_use_fp16:
                model = model.half()
            else:
                model = model.float()
            print(F"Loading Model Complete! {timer.elapsed}s")
        except ValueError as e:
            raise e
        except Exception:
            # pylint: disable=raise-missing-from
            raise ValueError(
                f"Model {os.path.basename(path)} is unsupported by chaiNNer. Please try another."
            )

        dirname, basename = os.path.split(os.path.splitext(path)[0])
        return model

def TorchToONNX(model: PyTorchModel, fp16=False) -> OnnxModel:
    print("Converting TORCH to ONNX")
    timer = Timer()
    device = "cpu"
    model = model.eval()
    model = model.to(torch.device(device))
    # https://github.com/onnx/onnx/issues/654
    dynamic_axes = {
        "data": {0: "batch_size", 2: "width", 3: "height"},
        "output": {0: "batch_size", 2: "width", 3: "height"},
    }
    dummy_input = torch.rand(1, model.in_nc, 64, 64)  # type: ignore
    dummy_input = dummy_input.to(torch.device(device))

    should_use_fp16 = fp16 and model.supports_fp16
    if should_use_fp16:
        model = model.half()
        dummy_input = dummy_input.half()
    else:
        model = model.float()
        dummy_input = dummy_input.float()

    with BytesIO() as f:
        print("Exporting ONNX")
        export_args = dict(
            opset_version=11,
            verbose=False,
            input_names=["data"],
            output_names=["output"],
            dynamic_axes=dynamic_axes,
            do_constant_folding=True,
        )
        try:  # torch >= 2.9 defaults to the dynamo exporter (needs onnxscript, different graph)
            torch.onnx.export(model, dummy_input, f, dynamo=False, **export_args)
        except TypeError:  # older torch has no dynamo argument
            torch.onnx.export(model, dummy_input, f, **export_args)
        print("Exporting Finished")
        f.seek(0)
        onnx_model_bytes = f.read()
    print(f'Conversion Complete! {timer.elapsed}s')
    return OnnxModel(onnx_model_bytes)

def ConvertOnnxToNCNN(model: OnnxModel, is_fp16=False) -> Tuple[NcnnModelWrapper, str]:
        print("Converting ONNX to NCNN")
        timer = Timer()
        fp16 = is_fp16

        model_proto = onnx.load_model_from_string(model.bytes)
        passes = onnxoptimizer.get_fuse_and_elimination_passes()
        opt_model = onnxoptimizer.optimize(model_proto, passes)  # type: ignore
        converter = Onnx2NcnnConverter(opt_model)
        ncnn_model = NcnnModelWrapper(converter.convert(fp16, False))

        fp_mode = "fp16" if fp16 else "fp32"
        print(F'Conversion Complete! {timer.elapsed}s')
        return ncnn_model, fp_mode

def TorchToNCNN(model: PyTorchModel, fp16=False) -> Tuple[NcnnModelWrapper, str]:
    onnx_model = TorchToONNX(model, fp16)
    ncnn_model, fp_mode = ConvertOnnxToNCNN(onnx_model, fp16)
    return ncnn_model, fp_mode

def SaveTorchToNCNN(model: PyTorchModel, directory: str, name: str) -> int:
    model, _ = TorchToNCNN(model)
    full_bin = f"{name}.bin"
    full_param = f"{name}.param"
    full_bin_path = os.path.join(directory, full_bin)
    full_param_path = os.path.join(directory, full_param)

    # Create Path
    os.makedirs(directory, exist_ok=True)

    print(f"Writing NCNN model to paths: {full_bin_path} {full_param_path}")
    model.model.write_bin(full_bin_path)
    model.model.write_param(full_param_path)

def Convert(model: str, directory: str, name: str, fp16=False):
    timer = Timer()
    print("PTH2NCNN ChaiNNer Edition")
    model = LoadTorchModel(model, fp16)
    SaveTorchToNCNN(model, directory, name)
    print(F"PTH2NCNN Conversion Completed! in {timer.elapsed}")


def pth2ncnn_compatibility(model: str, fp16=False, directory: str = None):
    timer = Timer()
    print("Running PTH2NCNN Compatibility Mode - PTH2NCNN ChaiNNer Edition")
    model_name = pathlib.Path(model).resolve().stem
    model: PyTorchModel = LoadTorchModel(model, False)  # Exported as fp32; loading as fp16 first only lost precision
    model_scale = model.scale
    path = (pathlib.Path() / model_name) if directory is None else pathlib.Path(directory)
    SaveTorchToNCNN(model, str(path), f"x{str(model_scale)}")
    print(F"PTH2NCNN Conversion Completed! in {timer.elapsed}")

if "__main__" == __name__:
    import argparse
    parser = argparse.ArgumentParser(
        prog=F"PTH2NCNN - ChaiNNer Edition",
        description="Code from ChaiNNer. Modified by DrPleaseRespect to Replace PTH2NCNN",
        epilog="ChaiNNer: https://github.com/chaiNNer-org/chaiNNer DrPleaseRespect: https://github.com/DrPleaseRespect "
    )
    parser.add_argument("model")
    parser.add_argument("-o", "--outpath", required=False, default=None)
    parser.add_argument('--full', action='store_true', help='Use 32bit precision instead of 16bit')
    parser.add_argument("--version", action='version', version=f'%(prog)s {version}')
    args = parser.parse_args()

    if not os.path.exists(args.model):
        print('Error: Model [{:s}] does not exist.'.format(args.model))
        sys.exit(1)

    pth2ncnn_compatibility(args.model, not args.full, args.outpath)



