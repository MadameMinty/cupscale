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
