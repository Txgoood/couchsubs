#!/usr/bin/env python3
# SPDX-License-Identifier: AGPL-3.0-only
"""Compare RNNoise latency and leakage on a local synthetic speech reference."""
import argparse
from pathlib import Path
import time
import wave

import numpy as np
from pyrnnoise.rnnoise import create, destroy, process_mono_frame

def read(path):
    with wave.open(str(path), "rb") as f:
        if f.getframerate() != 48000 or f.getsampwidth() != 2:
            raise ValueError("Expected a 48 kHz PCM16 WAV")
        return np.frombuffer(f.readframes(f.getnframes()), dtype="<i2").reshape(
            -1, f.getnchannels()).mean(1, dtype=np.float32) / 32768


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("speech", type=Path, help="Synthetic clean speech WAV")
    parser.add_argument("mixed", type=Path, help="The same speech with added background")
    args = parser.parse_args()

    speech = read(args.speech)
    mixed = read(args.mixed)
    if len(speech) != len(mixed) or len(speech) < 4800:
        parser.error("Matched synthetic WAV files must have equal lengths and at least 100 ms of audio")
    count = len(speech) // 480 * 480
    speech = speech[:count]
    background = mixed[:count] - speech
    rng = np.random.default_rng(157)
    t = np.arange(count) / 48000
    beat = np.exp(-12 * (t % 0.45)) * np.sin(2 * np.pi * 85 * t)
    chord = sum(np.sin(2 * np.pi * f * t) for f in (196, 247, 294, 392))
    effects = rng.normal(0, 1, count) * np.exp(-30 * ((t + 0.2) % 0.71))
    score = 0.065 * chord + 0.14 * beat + 0.06 * effects
    cases = [("existing tones", speech, background),
             ("synthetic music/effects", speech, score),
             ("overlapping speech", speech + .45 * np.roll(speech, 18321), score),
             ("background only", np.zeros_like(speech), score)]

    for name, voice, bg in cases:
        state = create()
        mix = np.clip(voice + bg, -1, 1)
        out = []
        timings = []
        try:
            for frame in mix.reshape(-1, 480):
                start = time.perf_counter()
                stem, _ = process_mono_frame(state, frame)
                timings.append((time.perf_counter() - start) * 1000)
                out.append(stem.astype(np.float32) / 32768)
        finally:
            destroy(state)
        denoised = np.concatenate(out)
        lag = max(range(0, 1441), key=lambda d: abs(np.dot(voice[:-d or None], denoised[d:]))) if np.any(voice) else 480
        measured = denoised[lag:]
        reference = np.column_stack([voice[:-lag or None], bg[:-lag or None]])
        coeff = np.linalg.lstsq(reference, measured, rcond=None)[0]
        residual = np.linalg.norm(measured) / max(np.linalg.norm(mix), 1e-7)
        print(name, "p95 ms", round(float(np.percentile(timings[1:], 95)), 3),
              "lag", lag, "speech/background", np.round(coeff, 4),
              "stem/input RMS", round(float(residual), 4), flush=True)

    state = create()
    origin = time.perf_counter()
    ages = []
    try:
        for index in range(1500):
            due = origin + (index + 1) * .020
            time.sleep(max(0, due - time.perf_counter()))
            start = (index * 960) % (count - 960)
            packet = mixed[start:start + 960]
            for frame in packet.reshape(2, 480):
                process_mono_frame(state, frame)
            if index:
                ages.append((time.perf_counter() - origin - index * .020) * 1000)
    finally:
        destroy(state)
    print("20 ms packet replay, 960-sample alignment hypothesis:",
          "p95", round(float(np.percentile(ages, 95)), 3),
          "max", round(max(ages), 3), "late", sum(age > 90 for age in ages),
          "of", len(ages))


if __name__ == "__main__":
    main()
