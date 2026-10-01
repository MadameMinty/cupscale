import cv2
import numpy as np
import pytest
import torch

import utils.dataops as ops


def nearest_x2(img):
    return np.repeat(np.repeat(img, 2, axis=0), 2, axis=1)


def test_unicode_path_roundtrip(tmp_path):
    path = tmp_path / "zażółć ñ 画像.png"
    img = np.random.default_rng(0).integers(0, 255, (8, 8, 3), dtype=np.uint8)

    ops.imwrite_unicode(path, img)

    np.testing.assert_array_equal(ops.imread_unicode(path), img)


def test_imread_unicode_returns_none_for_empty_file(tmp_path):
    path = tmp_path / "empty.png"
    path.write_bytes(b"")
    assert ops.imread_unicode(path) is None


def test_load_state_dict_reads_plain_state_dict(tmp_path):
    path = tmp_path / "m.pth"
    torch.save({"w": torch.ones(2)}, path)

    assert torch.equal(ops.load_state_dict(str(path))["w"], torch.ones(2))


class Legacy:
    pass


def test_load_state_dict_falls_back_for_pickled_objects(tmp_path):
    path = tmp_path / "legacy.pth"
    torch.save({"w": torch.ones(1), "meta": Legacy()}, path)

    with pytest.warns(UserWarning):
        assert "meta" in ops.load_state_dict(str(path))


def test_interpolate_keeps_params_ema_wrapper():
    a = {"params_ema": {"w": torch.zeros(2)}}
    b = {"params_ema": {"w": torch.ones(2)}}

    result = ops.interpolate_state_dicts(a, b, 0.25, 0.75)

    assert list(result) == ["params_ema"]
    assert torch.allclose(result["params_ema"]["w"], torch.full((2,), 0.75))


def test_interpolate_plain_state_dicts():
    result = ops.interpolate_state_dicts({"w": torch.ones(1)}, {"w": torch.full((1,), 3.0)}, 0.5, 0.5)
    assert torch.allclose(result["w"], torch.full((1,), 2.0))


def test_auto_split_matches_direct_upscale():
    img = np.random.default_rng(1).integers(0, 255, (64, 48, 3), dtype=np.uint8)

    rlt, depth, _ = ops.auto_split_upscale(img, nearest_x2, scale=2)

    assert depth == 1
    np.testing.assert_array_equal(rlt, nearest_x2(img))


def test_auto_split_recovers_from_oom():
    img = np.random.default_rng(2).integers(0, 255, (128, 96, 3), dtype=np.uint8)

    def limited(tile):
        if tile.shape[0] * tile.shape[1] > 64 * 64:
            raise RuntimeError("CUDA out of memory")
        return nearest_x2(tile)

    rlt, depth, _ = ops.auto_split_upscale(img, limited, scale=2, overlap=8)

    assert depth > 1
    np.testing.assert_array_equal(rlt, nearest_x2(img))


def test_auto_split_reraises_non_oom_errors():
    def broken(_):
        raise RuntimeError("shape mismatch")

    with pytest.raises(RuntimeError, match="shape mismatch"):
        ops.auto_split_upscale(np.zeros((8, 8, 3), np.uint8), broken, scale=2)


def blur_then_x2(img):
    """5x5 box blur twice: receptive radius 4, so tiles with overlap >= 4 must stitch to the exact direct result."""
    out = cv2.blur(cv2.blur(img, (5, 5)), (5, 5))
    return nearest_x2(out)


def blur_x1(img):
    return cv2.blur(cv2.blur(img, (5, 5)), (5, 5))


@pytest.mark.parametrize("shape", [(64, 48), (101, 77), (33, 130)])
@pytest.mark.parametrize("depth", [2, 3])
@pytest.mark.parametrize("fn, scale", [(blur_then_x2, 2), (blur_x1, 1)])
def test_forced_split_stitches_exactly(shape, depth, fn, scale):
    img = np.random.default_rng(3).integers(0, 255, shape + (3,), dtype=np.uint8)

    rlt, used, tiles = ops.auto_split_upscale(img, fn, scale=scale, overlap=8, max_depth=depth)

    assert (used, tiles) == (depth, 4 ** (depth - 1) + 1)
    np.testing.assert_array_equal(rlt, fn(img))


def test_forced_depth_still_splits_further_on_oom():
    img = np.random.default_rng(4).integers(0, 255, (128, 96, 3), dtype=np.uint8)

    def limited(tile):
        if tile.shape[0] * tile.shape[1] > 48 * 48:
            raise RuntimeError("CUDA out of memory")
        return blur_then_x2(tile)

    rlt, depth, _ = ops.auto_split_upscale(img, limited, scale=2, overlap=8, max_depth=2)

    assert depth == 3
    np.testing.assert_array_equal(rlt, blur_then_x2(img))


def test_auto_split_gives_up_on_tiny_tiles():
    def always_oom(_):
        raise RuntimeError("CUDA out of memory")

    with pytest.raises(RuntimeError, match="Out of memory even on"):
        ops.auto_split_upscale(np.zeros((64, 64, 3), np.uint8), always_oom, scale=2, overlap=8)
