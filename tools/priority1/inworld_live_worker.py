#!/usr/bin/env python3
# SPDX-License-Identifier: AGPL-3.0-only
"""EXPERIMENTAL, local-only 48k stereo -> Inworld mono stem process.

Not enabled by the shipped CouchSubs app. A future supervised .NET host must
enforce the 90ms deadline and send fallback-original audio on ANY worker
fault. This worker never emits modified game audio; only model speech stems.

Wire protocol (little endian):
  input  : repeated {frame_id: int64, game: 1920 float32 interleaved L/R}
  startup: ASCII b"READY\n" after weights have been loaded
  output : repeated {frame_id: int64, dialogue: 960 float32 MONO}
The model has delayed output, so output IDs refer to EARLIER input frames.
"""
import argparse
from array import array
from collections import deque
import math
import struct
import sys

RATE = 48000
FRAME_SAMPLES = 960               # 20ms mono
GAME_SAMPLES = FRAME_SAMPLES * 2
HEADER = struct.Struct("<q")
IN_BYTES = HEADER.size + GAME_SAMPLES * 4
MAX_PENDING = 8
MODEL = "voice-isolator-v1-realtime"


def exact_read(stream, size):
    parts = []
    left = size
    while left:
        chunk = stream.read(left)
        if not chunk:
            if left == size:
                return None
            raise IOError("Truncated PCM frame: abort worker, fail open on .NET side")
        parts.append(chunk)
        left -= len(chunk)
    return b"".join(parts)


class FrameEmitter:
    """Bounded mapping from delayed model samples to original frame IDs."""

    def __init__(self):
        self.ids = deque()
        self.audio = array("f")
        self.last_id = -1

    def accept(self, frame_id, produced):
        if frame_id <= self.last_id or frame_id < 0:
            raise ValueError("Out-of-order input frame ID")
        self.last_id = frame_id
        self.ids.append(frame_id)
        self.audio.extend(produced)
        completed = self._drain()
        if len(self.ids) > MAX_PENDING:
            raise RuntimeError("Model is too far behind PS5 stream; host must fail open")
        return completed

    def finalize(self, produced):
        self.audio.extend(produced)
        emitted = self._drain()
        if self.ids or self.audio:
            raise RuntimeError("Model didn't produce precisely aligned audio")
        return emitted

    def _drain(self):
        completed = []
        while self.ids and len(self.audio) >= FRAME_SAMPLES:
            audio = self.audio[:FRAME_SAMPLES]
            del self.audio[:FRAME_SAMPLES]
            if not all(math.isfinite(s) and abs(s) <= 1 for s in audio):
                raise ValueError("Non-finite or clipped output rejected")
            completed.append((self.ids.popleft(), audio.tobytes()))
        return completed


def self_test():
    emitter = FrameEmitter()
    assert emitter.accept(10, []) == []
    assert emitter.accept(11, [0.125] * 960) == [(10, array("f", [0.125] * 960).tobytes())]
    assert emitter.finalize([0.375] * 960)[0][0] == 11
    assert emitter.ids == deque()
    print("PASS: delayed output retains the exact original frame identity")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--stdio", action="store_true")
    parser.add_argument("--self-test", action="store_true")
    parser.add_argument("--device", choices=["cpu", "cuda"], default="cpu")
    args = parser.parse_args()
    if args.self_test:
        self_test()
        return
    if not args.stdio:
        parser.error("Only --self-test and explicit --stdio are supported")
    if sys.byteorder != "little":
        raise RuntimeError("Binary protocol requires little-endian host")
    import numpy as np
    import torch
    from inworld import voice_isolation

    if args.device == "cuda" and not torch.cuda.is_available():
        raise RuntimeError("CUDA requested but not available; never pretend GPU enabled")
    isolator = voice_isolation.VoiceIsolator(
        "inworld/voice-isolator", device=args.device,
        mode="streaming", lookahead_frames=2)
    stream = isolator.stream()
    out = sys.stdout.buffer
    inp = sys.stdin.buffer
    out.write(b"READY\n")
    out.flush()
    ids = FrameEmitter()
    while True:
        message = exact_read(inp, IN_BYTES)
        if message is None:
            break
        frame_id = HEADER.unpack_from(message)[0]
        pcm = np.frombuffer(message, dtype="<f4", count=GAME_SAMPLES,
                            offset=HEADER.size).reshape(FRAME_SAMPLES, 2)
        if not np.isfinite(pcm).all() or float(np.max(np.abs(pcm))) > 1:
            raise ValueError("Invalid PCM; host must return unmodified originals")
        center = pcm.mean(axis=1, dtype=np.float32)
        with torch.inference_mode():
            rendered = stream.push(torch.from_numpy(center.copy()))
        voice = rendered.detach().cpu().numpy().reshape(-1).tolist()
        for frame_id, audio_bytes in ids.accept(frame_id, voice):
            out.write(HEADER.pack(frame_id) + audio_bytes)
        out.flush()
    with torch.inference_mode():
        tail = stream.flush()
    for frame_id, audio_bytes in ids.finalize(
            tail.detach().cpu().numpy().reshape(-1).tolist()):
        out.write(HEADER.pack(frame_id) + audio_bytes)
    out.flush()


if __name__ == "__main__":
    try:
        main()
    except BrokenPipeError:
        sys.exit(0)
    except Exception as exc:
        # stdout is a binary protocol. Diagnostics MUST only go to stderr.
        print("Inworld worker stopped: " + str(exc), file=sys.stderr)
        sys.exit(1)
