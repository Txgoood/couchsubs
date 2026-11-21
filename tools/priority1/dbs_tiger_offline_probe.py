#!/usr/bin/env python3
# SPDX-License-Identifier: AGPL-3.0-only
"""Manual, OFFLINE-only DBS-TIGER-DnR benchmark, not a PS5 live processor.

Dependencies must be installed explicitly by the operator using the official
DBS model repository. This script is never invoked from audio callbacks or CI.
Audio and generated stems stay in a local output folder.
"""
import argparse
import json
import time
from pathlib import Path


def parse_args():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--input", required=True, type=Path,
                   help="local 44.1 kHz mono WAV with authorized sample audio")
    p.add_argument("--output", required=True, type=Path,
                   help="a new, empty directory for local benchmark output")
    p.add_argument("--device", choices=["cpu", "cuda"], default="cpu")
    p.add_argument("--model", default="JusperLee/DBS-TIGER-DnR")
    p.add_argument("--max-seconds", type=float, default=15,
                   help="reject input clips longer than this (1..60 s)")
    return p.parse_args()


def main():
    args = parse_args()
    if not 1 <= args.max_seconds <= 60:
        raise SystemExit("--max-seconds must be between 1 and 60")
    if not args.input.is_file() or args.input.resolve() == args.output.resolve():
        raise SystemExit("Provide an existing local input WAV and separate output folder.")
    if args.output.exists() and any(args.output.iterdir()):
        raise SystemExit("Output directory must be empty; never overwrite existing audio.")
    try:
        import numpy as np
        import soundfile as sf
        import torch
        from dbs.engine import separate
        from dbs.models import DBSTIGER
    except ImportError as exc:
        raise SystemExit("Install the official DBS project dependencies first: " + str(exc))

    info = sf.info(args.input)
    if info.samplerate != 44100 or info.channels != 1 or info.frames < 4410:
        raise SystemExit("DBS-TIGER evaluation requires >=0.1 s, 44.1 kHz mono audio.")
    seconds = info.frames / info.samplerate
    if seconds > args.max_seconds:
        raise SystemExit("Input too long for bounded offline test.")
    audio, rate = sf.read(args.input, dtype="float32", always_2d=True)
    if not np.isfinite(audio).all() or float(np.max(np.abs(audio))) > 1:
        raise SystemExit("Invalid input audio.")
    if args.device == "cuda" and not torch.cuda.is_available():
        raise SystemExit("CUDA requested, but CUDA is not available; no GPU claim made.")

    # This is an explicit manual model download, not an application asset.
    model = DBSTIGER.from_pretrained(args.model).eval().to(args.device)
    mixture = torch.from_numpy(audio.T.copy()).view(1, 1, -1).to(args.device)
    if args.device == "cuda":
        torch.cuda.synchronize()
    start = time.perf_counter()
    with torch.inference_mode():
        music, effects, speech = separate(
            model, mixture,
            {"mode": "chunked", "chunk_seconds": 6.0, "hop_seconds": 3.0},
            sample_rate=rate
        )[0]
    if args.device == "cuda":
        torch.cuda.synchronize()
    elapsed = time.perf_counter() - start
    args.output.mkdir(parents=True, exist_ok=True)
    results = {"model": args.model, "device": args.device,
               "input_sample_rate": rate, "input_channels": 1,
               "clip_seconds": seconds, "inference_seconds": elapsed,
               "real_time_factor": elapsed / seconds,
               "live_ready": False,
               "reason": "6s model window and 44.1kHz mono are incompatible with the 90ms stereo PS5 live contract"}
    for name, stem in (("music", music), ("effects", effects), ("speech", speech)):
        samples = stem.detach().cpu().numpy().reshape(-1)
        if len(samples) < len(audio) or not np.isfinite(samples).all():
            raise SystemExit(f"{name}: invalid model output; no result written")
        samples = samples[:len(audio)]
        results[name + "_peak"] = float(np.max(np.abs(samples)))
        sf.write(args.output / (name + ".wav"), np.clip(samples, -1, 1), rate,
                 subtype="PCM_24")
    (args.output / "diagnostics.json").write_text(
        json.dumps(results, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps(results, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
