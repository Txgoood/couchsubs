#!/usr/bin/env python3
# SPDX-License-Identifier: AGPL-3.0-only
"""Manual offline validation of a *real streaming* 48kHz speech separator.

Official Inworld Voice Isolator v1 realtime (Apache-2.0) is loaded only
when the user invokes this tool. It is never started by CouchSubs/PS5.
Inputs and outputs stay on this computer. Running may fetch the upstream
SHA256-pinned model checkpoint once via the official package.
"""
import argparse
import json
import math
import statistics
import time
import wave
from pathlib import Path


RATE = 48000
CHUNK_FRAMES = 960
MAX_SECONDS = 60
MODEL = "voice-isolator-v1-realtime"
# The published streaming model documents 63 ms ALGORITHMIC latency at
# lookahead_frames=2; inference, capture, IO and output are extra.
MODEL_ALGORITHMIC_MS = 63


def read_pcm16_wav(path, max_seconds=MAX_SECONDS):
    with wave.open(str(path), "rb") as src:
        if src.getframerate() != RATE or src.getnchannels() not in (1, 2):
            raise ValueError("Input must be 48000 Hz mono/stereo WAV")
        if src.getsampwidth() != 2 or src.getcomptype() != "NONE":
            raise ValueError("Input must be uncompressed signed 16-bit PCM")
        count = src.getnframes()
        channels = src.getnchannels()
        if not 4800 <= count <= max_seconds * RATE:
            raise ValueError("Input must be 0.1 to 60 seconds")
        data = src.readframes(count)
    import numpy as np
    stereo_or_mono = np.frombuffer(data, dtype="<i2").astype(np.float32).reshape(-1, channels) / 32768.0
    if not np.isfinite(stereo_or_mono).all():
        raise ValueError("Non-finite input")
    return stereo_or_mono


def subtract_center_stem(stereo, speech, attenuation=0.25):
    """Preserves original L-R stereo difference, clamps and never amplifies stems."""
    import numpy as np
    if stereo.ndim != 2 or stereo.shape[1] != 2 or speech.ndim != 1:
        raise ValueError("Expected 2-channel interleaved game audio and mono stem")
    if len(stereo) != len(speech) or not 0 <= attenuation <= .25:
        raise ValueError("Invalid size or attenuation")
    if not np.isfinite(stereo).all() or not np.isfinite(speech).all():
        raise ValueError("Non-finite PCM")
    if np.max(np.abs(speech)) > 1.0:
        raise ValueError("Model stem out of bounds")
    # Retain exact stereo side information by applying the same mid reduction
    # to L and R. If attenuation would clip either sample, keep BOTH originals.
    reduce = speech * attenuation
    processed = stereo - reduce[:, None]
    clip = (np.abs(processed) > 1.0).any(axis=1)
    processed[clip] = stereo[clip]
    return processed.astype(np.float32)


def save_pcm16_wav(path, pcm):
    import numpy as np
    if not np.isfinite(pcm).all():
        raise ValueError("Non-finite output")
    normalized = np.clip(pcm, -1, 1)
    integers = np.clip(np.rint(normalized * 32768.0), -32768, 32767).astype("<i2")
    channels = 1 if pcm.ndim == 1 else pcm.shape[1]
    with wave.open(str(path), "wb") as dst:
        dst.setnchannels(channels)
        dst.setsampwidth(2)
        dst.setframerate(RATE)
        dst.writeframes(integers.tobytes())


def isolate_stream(stereo, device, lookahead):
    """Actual model execution using the upstream public streaming API."""
    import numpy as np
    import torch
    from inworld import voice_isolation

    if device == "cuda" and not torch.cuda.is_available():
        raise RuntimeError("CUDA unavailable; benchmark cannot claim GPU usage.")
    live = voice_isolation.VoiceIsolator(
        "inworld/voice-isolator", device=device,
        mode="streaming", lookahead_frames=lookahead)
    stream = live.stream()
    center = np.mean(stereo, axis=1, dtype=np.float32)
    pieces = []
    frame_times_ms = []
    for start in range(0, len(center), CHUNK_FRAMES):
        block = torch.from_numpy(center[start:start + CHUNK_FRAMES].copy())
        if device == "cuda":
            torch.cuda.synchronize()
        t = time.perf_counter()
        with torch.inference_mode():
            out = stream.push(block)
        if device == "cuda":
            torch.cuda.synchronize()
        frame_times_ms.append((time.perf_counter() - t) * 1000)
        pieces.append(out.detach().cpu().numpy().reshape(-1))
    with torch.inference_mode():
        final = stream.flush()
    pieces.append(final.detach().cpu().numpy().reshape(-1))
    speech = np.concatenate(pieces).astype(np.float32)
    if len(speech) != len(center) or not np.isfinite(speech).all():
        raise ValueError("Streaming model lost or generated extra audio samples")
    if np.max(np.abs(speech)) > 1:
        # Do NOT silently normalize: amplitude must be comparable to source.
        raise ValueError("Streaming model generated out-of-range PCM")
    return speech, frame_times_ms


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input", type=Path, required=True, help="authorized local WAV")
    parser.add_argument("--output", type=Path, required=True, help="new/empty local folder")
    parser.add_argument("--device", choices=["cpu", "cuda"], default="cpu")
    parser.add_argument("--lookahead", type=int, choices=[0, 2, 4], default=2)
    parser.add_argument("--attenuation", type=float, default=0.25)
    args = parser.parse_args()
    if not args.input.is_file():
        parser.error("Input WAV file does not exist")
    if args.output.exists() and any(args.output.iterdir()):
        parser.error("Output folder is not empty; refusing to overwrite")
    if not 0 <= args.attenuation <= .25:
        parser.error("Attenuation must be in [0, 0.25]")
    audio = read_pcm16_wav(args.input)
    # The native streaming checkpoint is mono. Two-channel PS5 sound is
    # downmixed for ANALYSIS ONLY. We keep original stereo for final mix.
    if audio.shape[1] == 1:
        stereo = audio.repeat(2, axis=1)
    else:
        stereo = audio
    started = time.perf_counter()
    speech, times = isolate_stream(stereo, args.device, args.lookahead)
    elapsed = time.perf_counter() - started
    preserved = subtract_center_stem(stereo, speech, args.attenuation)
    args.output.mkdir(parents=True, exist_ok=True)
    save_pcm16_wav(args.output / "speech.wav", speech)
    save_pcm16_wav(args.output / "original_stereo.wav", stereo)
    save_pcm16_wav(args.output / "conservative_stereo.wav", preserved)
    # 20ms chunks, model 63ms algorithmic with 20ms lookahead; p95 here
    # includes Python inference but excludes Windows/PS5 roundtrip.
    elapsed_frames = len(times)
    p95 = sorted(times)[math.ceil(len(times) * .95) - 1] if times else None
    diag = {
        "model": MODEL, "mode": "streaming",
        "device_requested": args.device, "lookahead_frames": args.lookahead,
        "published_algorithmic_delay_ms_at_2_frames": MODEL_ALGORITHMIC_MS,
        "sample_rate": RATE, "frames": len(stereo),
        "input_channels": audio.shape[1], "output_channels": 2,
        "frames_processed": elapsed_frames,
        "p50_push_ms": statistics.median(times) if times else None,
        "p95_push_ms": p95, "max_push_ms": max(times) if times else None,
        "overall_runtime_seconds_including_model_load": elapsed,
        "live_ps5_validated": False,
        "notes": "Offline replay of causal 20ms model. Not proof of live PS5 timing or dialogue isolation quality.",
    }
    (args.output / "metrics.json").write_text(
        json.dumps(diag, ensure_ascii=False, indent=2), encoding="utf8")
    print(json.dumps(diag, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
