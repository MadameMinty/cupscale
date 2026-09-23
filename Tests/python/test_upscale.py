import numpy as np
import pytest
import torch

import utils.dataops as ops
from upscale import AlphaOptions, Upscale


class NearestX2(torch.nn.Module):
    def forward(self, x):
        return torch.nn.functional.interpolate(x, scale_factor=2, mode="nearest")


def make_upscaler(tmp_path, model="m", in_nc=3, out_nc=3, **kwargs):
    inp, out = tmp_path / "in", tmp_path / "out"
    inp.mkdir(exist_ok=True)
    up = Upscale(model=model, input=inp, output=out, cpu=True, alpha_mode=AlphaOptions.NO_ALPHA, **kwargs)
    up.model = NearestX2()
    up.last_in_nc, up.last_out_nc, up.last_scale = in_nc, out_nc, 2
    return up


def test_fast_path_returns_uint8_and_keeps_bgr_order(tmp_path):
    up = make_upscaler(tmp_path)
    img = np.zeros((4, 4, 3), np.uint8)
    img[..., 0] = 200  # Blue in BGR

    out = up.upscale(img)

    assert out.dtype == np.uint8
    assert out.shape == (8, 8, 3)
    assert (out[..., 0] == 200).all() and (out[..., 2] == 0).all()


def test_fast_path_normalizes_uint16(tmp_path):
    up = make_upscaler(tmp_path)
    img = np.full((2, 2, 3), 65535, np.uint16)

    assert (up.upscale(img) == 255).all()


def test_alpha_path_still_produces_rgba(tmp_path):
    up = make_upscaler(tmp_path)
    img = np.full((4, 4, 4), 255, np.uint8)

    out = up.upscale(img)

    assert out.shape == (8, 8, 4)
    assert np.allclose(out, 255)


def test_models_are_cached_across_images(tmp_path, monkeypatch):
    up = make_upscaler(tmp_path)
    builds = []

    def fake_build(self, path):
        builds.append(path)
        return (NearestX2(), 3, 3, 0, 0, 2)

    monkeypatch.setattr(Upscale, "_Upscale__build_model", fake_build)

    for path in ["a", "b", "a", "b", "a"]:
        up.load_model(path)

    assert builds == ["a", "b"]


def test_chain_with_cached_split_depth_runs(tmp_path, monkeypatch):
    """Regression: chaining two models with --cache_max_split_depth raised KeyError."""
    monkeypatch.setattr(Upscale, "_Upscale__build_model", lambda self, path: (NearestX2(), 3, 3, 0, 0, 2))
    monkeypatch.setattr(Upscale, "_Upscale__check_model_path", lambda self, path: path)
    up = make_upscaler(tmp_path, model="a>b", cache_max_split_depth=True)

    for name in ["one.png", "two ü.png"]:
        ops.imwrite_unicode(up.input / name, np.full((6, 5, 3), 90, np.uint8))

    up.run()

    for name in ["one.png", "two ü.png"]:
        result = ops.imread_unicode(up.output / name)
        assert result.shape == (24, 20, 3)
        assert (result == 90).all()
