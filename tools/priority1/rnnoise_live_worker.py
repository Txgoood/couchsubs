#!/usr/bin/env python3
# SPDX-License-Identifier: AGPL-3.0-only
"""Opt-in RNNoise PS5 diagnostic worker. Emits mono stems over the existing PCM protocol."""
import argparse
from importlib.metadata import version
import sys

from inworld_live_worker import FRAME_SAMPLES, serve, self_test


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--stdio", action="store_true")
    parser.add_argument("--self-test", action="store_true")
    parser.add_argument("--device", choices=["cpu"], default="cpu")
    args = parser.parse_args()
    if args.self_test:
        self_test()
        return
    if not args.stdio or sys.byteorder != "little":
        parser.error("Explicit --stdio and a little-endian host are required")
    if version("pyrnnoise") != "0.4.6":
        raise RuntimeError("Diagnostic alignment was measured with pyrnnoise 0.4.6")

    import numpy as np
    from pyrnnoise.rnnoise import create, destroy, process_mono_frame

    state = create()
    if not state:
        raise RuntimeError("Could not initialize RNNoise")
    first = True

    def process(center):
        frames = [process_mono_frame(state, block)[0]
                  for block in center.reshape(2, FRAME_SAMPLES // 2)]
        return np.concatenate(frames).astype(np.float32) / 32768

    def render(center):
        nonlocal first
        output = process(center)
        if first:
            first = False
            # Discard one packet of model delay measured in the local probe.
            return output[:0]
        return output

    def finish():
        if first:
            return np.empty(0, dtype=np.float32)
        return process(np.zeros(FRAME_SAMPLES, dtype=np.float32))

    try:
        serve(np, render, finish, require_contiguous=True)
    finally:
        destroy(state)


if __name__ == "__main__":
    try:
        main()
    except BrokenPipeError:
        sys.exit(0)
    except Exception as exc:
        print("RNNoise worker stopped: " + str(exc), file=sys.stderr)
        sys.exit(1)
