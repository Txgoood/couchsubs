#!/usr/bin/env python3
# SPDX-License-Identifier: AGPL-3.0-only
"""Measure Inworld on a paced local WAV replay, before application or PS5 I/O."""
import argparse
import math
from pathlib import Path
import time

from inworld_streaming_probe import RATE, read_pcm16_wav


def percentile95(values):
    return sorted(values)[math.ceil(len(values) * .95) - 1]


def measure(isolator, mono, chunk_ms, cache_frames, packet_ms):
    chunk = RATE * chunk_ms // 1000
    packet = RATE * packet_ms // 1000 if packet_ms else chunk
    frame = RATE // 50  # Report complete 20 ms output frames.
    warmup = isolator.stream(cache_frames=cache_frames)
    for start in range(0, min(len(mono), chunk * 16), chunk):
        warmup.push(mono[start:start + chunk])
    warmup.flush()
    del warmup

    stream = isolator.stream(cache_frames=cache_frames)
    origin = time.perf_counter()
    produced = 0
    next_frame_end = frame
    ages = []
    calls = []
    for start in range(0, len(mono), chunk):
        block = mono[start:start + chunk]
        block_end = start + len(block)
        packet_end = min(len(mono), ((block_end + packet - 1) // packet) * packet)
        scheduled = origin + packet_end / RATE
        time.sleep(max(0, scheduled - time.perf_counter()))
        began = time.perf_counter()
        output = stream.push(block)
        finished = time.perf_counter()
        calls.append((finished - began) * 1000)
        produced += output.size
        while next_frame_end <= produced:
            ages.append((finished - origin - next_frame_end / RATE) * 1000)
            next_frame_end += frame
    tail = stream.flush(as_numpy=True)
    if produced + tail.size != len(mono) or not ages:
        raise RuntimeError("Replay did not produce the expected complete sound frames")
    print(
        f"{chunk_ms} ms model input / {packet_ms or chunk_ms} ms packets: "
        f"push p95 {percentile95(calls):.2f} ms, "
        f"output age p95 {percentile95(ages):.2f} ms, "
        f"above 90 ms {sum(age > 90 for age in ages)}/{len(ages)}, "
        f"flush samples excluded {tail.size}",
        flush=True,
    )


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("input", type=Path, help="Local authorized 48 kHz PCM16 WAV")
    parser.add_argument("--chunk-ms", type=int, nargs="+", default=[20, 25, 30, 40])
    parser.add_argument("--packet-ms", type=int, default=0,
                        help="Deliver samples only on packet boundaries (0 follows chunk size)")
    parser.add_argument("--seconds", type=int, default=12, choices=range(1, 61),
                        metavar="1..60")
    parser.add_argument("--precision", choices=["fp32", "bf16"], default="fp32")
    parser.add_argument("--cache-frames", type=int)
    parser.add_argument("--tf32", action="store_true")
    args = parser.parse_args()
    if any(not 10 <= ms <= 100 for ms in args.chunk_ms):
        parser.error("Chunk size must be 10 to 100 ms")
    if not 0 <= args.packet_ms <= 100:
        parser.error("Packet size must be 0 to 100 ms")
    if args.cache_frames is not None and args.cache_frames < 1:
        parser.error("Cache frame count must be positive")

    import numpy as np
    import torch
    from inworld import voice_isolation

    if not torch.cuda.is_available():
        parser.error("This probe requires CUDA")
    mono = read_pcm16_wav(args.input).mean(axis=1, dtype=np.float32)
    mono = np.resize(mono, RATE * args.seconds)
    torch.set_num_threads(1)
    torch.set_num_interop_threads(1)
    if args.tf32:
        torch.backends.cuda.matmul.allow_tf32 = True
    isolator = voice_isolation.VoiceIsolator(
        "inworld/voice-isolator", device="cuda", mode="streaming",
        precision=args.precision, lookahead_frames=0)
    for chunk_ms in args.chunk_ms:
        measure(isolator, mono, chunk_ms, args.cache_frames, args.packet_ms)


if __name__ == "__main__":
    main()
