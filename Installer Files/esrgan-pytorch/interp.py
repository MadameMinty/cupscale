import sys
import torch

from utils.dataops import interpolate_state_dicts, load_state_dict as load


alpha = float(sys.argv[3])
net_PSNR_path = sys.argv[1]
net_ESRGAN_path = sys.argv[2]
net_interp_path = sys.argv[4]

net_PSNR = load(net_PSNR_path)
net_ESRGAN = load(net_ESRGAN_path)

print('Interpolating with alpha = ', alpha)

net_interp = interpolate_state_dicts(net_PSNR, net_ESRGAN, 1 - alpha, alpha)

torch.save(net_interp, net_interp_path)
