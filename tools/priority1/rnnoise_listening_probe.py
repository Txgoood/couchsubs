#!/usr/bin/env python3
# SPDX-License-Identifier: AGPL-3.0-only
"""Render a local original/attenuated listening pair from an authorized game clip."""
import argparse
import json
from pathlib import Path
import subprocess
import sys

from inworld_live_worker import FRAME_SAMPLES, HEADER
from inworld_streaming_probe import read_pcm16_wav, save_pcm16_wav, subtract_center_stem


def separate(stereo, np):
    packet_count = (len(stereo) + FRAME_SAMPLES - 1) // FRAME_SAMPLES
    padded = np.pad(stereo, ((0, packet_count * FRAME_SAMPLES - len(stereo)), (0, 0)))
    packets = bytearray()
    for frame_id, frame in enumerate(padded.reshape(packet_count, FRAME_SAMPLES, 2)):
        packets.extend(HEADER.pack(frame_id))
        packets.extend(frame.astype("<f4", copy=False).tobytes())

    worker = Path(__file__).with_name("rnnoise_live_worker.py")
    result = subprocess.run([sys.executable, str(worker), "--stdio"], input=packets,
                            capture_output=True, timeout=60)
    if result.returncode:
        raise RuntimeError(result.stderr.decode(errors="replace").strip())
    marker = b"READY\n"
    record_size = HEADER.size + FRAME_SAMPLES * 4
    if not result.stdout.startswith(marker) or len(result.stdout) != len(marker) + packet_count * record_size:
        raise RuntimeError("Incomplete RNNoise response")

    stem = np.empty(packet_count * FRAME_SAMPLES, dtype=np.float32)
    for frame_id in range(packet_count):
        offset = len(marker) + frame_id * record_size
        if HEADER.unpack_from(result.stdout, offset)[0] != frame_id:
            raise RuntimeError("RNNoise returned a mismatched frame ID")
        stem[frame_id * FRAME_SAMPLES:(frame_id + 1) * FRAME_SAMPLES] = np.frombuffer(
            result.stdout, dtype="<f4", count=FRAME_SAMPLES, offset=offset + HEADER.size)
    return stem[:len(stereo)]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("input", type=Path, help="Local 48 kHz stereo PCM16 game clip")
    parser.add_argument("output", type=Path, help="New directory for the listening pair")
    args = parser.parse_args()
    if args.output.exists():
        parser.error("Output directory already exists")

    import numpy as np

    stereo = read_pcm16_wav(args.input)
    if stereo.shape[1] != 2:
        parser.error("A stereo source is required to compare the game mix")
    stem = separate(stereo, np)
    attenuated = subtract_center_stem(stereo, stem)
    side_change = np.max(np.abs((stereo[:, 0] - stereo[:, 1]) -
                                (attenuated[:, 0] - attenuated[:, 1])))

    args.output.mkdir()
    save_pcm16_wav(args.output / "A-original.wav", stereo)
    save_pcm16_wav(args.output / "B-reduced.wav", attenuated)
    save_pcm16_wav(args.output / "speech-estimate.wav", stem)
    print(json.dumps({"frames": len(stereo), "maximum_stem_reduction": 0.25,
                      "maximum_stereo_difference_change": float(side_change)}))


if __name__ == "__main__":
    main()
