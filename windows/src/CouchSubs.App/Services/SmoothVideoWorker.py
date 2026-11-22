# SPDX-License-Identifier: AGPL-3.0-only
"""Local NVIDIA interpolation. Share frames through memory and use pipes for control only."""

import mmap
import sys

import numpy as np
import torch
from nvvfx import VideoFrameGeneration


def run():
    tag, width, height = sys.argv[1:]
    width, height = int(width), int(height)
    size = width * height * 4
    control = sys.stdin.buffer
    reply = sys.stdout.buffer

    def send(code):
        reply.write(code)
        reply.flush()

    def upload(pixels):
        bgra = np.frombuffer(pixels, dtype=np.uint8, count=size).reshape(height, width, 4)
        rgb = torch.from_numpy(bgra).to("cuda")[:, :, [2, 1, 0]]
        return rgb.permute(2, 0, 1).contiguous().float().mul_(1.0 / 255)

    def interpolate(effect, previous, current):
        image = torch.from_dlpack(effect.run_at_timestep(previous, current, 0.5).image)
        bgra = torch.empty((height, width, 4), dtype=torch.uint8, device="cuda")
        bgra[:, :, :3] = image[[2, 1, 0]].permute(1, 2, 0).mul(255).round_().clamp_(0, 255)
        bgra[:, :, 3] = 255
        return bgra.cpu().numpy().tobytes()

    with mmap.mmap(-1, size * 2, tagname=tag) as pixels, VideoFrameGeneration(
        mode=VideoFrameGeneration.Mode.LOW
    ) as effect:
        effect.input_width = width
        effect.input_height = height
        effect.load()
        warmup = upload(pixels)
        interpolate(effect, warmup, torch.rand_like(warmup))
        del warmup
        send(b"R")
        previous = None
        while command := control.read(1):
            if command not in (b"N", b"C"):
                break
            current = upload(pixels)
            if previous is None or command == b"C":
                previous = current
                send(b"C")
                continue
            pixels[size:] = interpolate(effect, previous, current)
            previous = current
            send(b"N")


if __name__ == "__main__":
    run()
