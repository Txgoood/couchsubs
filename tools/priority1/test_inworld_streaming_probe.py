# SPDX-License-Identifier: AGPL-3.0-only
"""No-model smoke tests. Model inference and device metrics remain manual."""
import importlib.util
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
import wave

HERE = Path(__file__).resolve().parent
SPEC = importlib.util.spec_from_file_location(
    "inworld_streaming_probe", HERE / "inworld_streaming_probe.py")
PROBE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(PROBE)


class InworldStreamingProbeTests(unittest.TestCase):
    def test_stereo_frame_contract_and_reported_latency_are_not_benchmarks(self):
        self.assertEqual(48000, PROBE.RATE)
        self.assertEqual(960, PROBE.CHUNK_FRAMES)
        self.assertEqual(63, PROBE.MODEL_ALGORITHMIC_MS)
        self.assertEqual("voice-isolator-v1-realtime", PROBE.MODEL)

    def test_invalid_rate_or_channels_fail_before_model_import(self):
        with tempfile.TemporaryDirectory() as tmp:
            wav = Path(tmp) / "wrong-rate.wav"
            with wave.open(str(wav), "wb") as f:
                f.setnchannels(2)
                f.setsampwidth(2)
                f.setframerate(44100)
                f.writeframes(b"\x00\x00" * 2 * 4410)
            with self.assertRaisesRegex(ValueError, "48000"):
                PROBE.read_pcm16_wav(wav)

    def test_short_clip_cannot_claim_measured_latency(self):
        with tempfile.TemporaryDirectory() as tmp:
            wav = Path(tmp) / "too-short.wav"
            with wave.open(str(wav), "wb") as f:
                f.setnchannels(1)
                f.setsampwidth(2)
                f.setframerate(PROBE.RATE)
                f.writeframes(b"\x00\x00" * 100)
            with self.assertRaisesRegex(ValueError, "0.1"):
                PROBE.read_pcm16_wav(wav)

    def test_help_does_not_require_torch_or_download_model(self):
        result = subprocess.run(
            [sys.executable, str(HERE / "inworld_streaming_probe.py"), "--help"],
            check=True, capture_output=True, text=True)
        self.assertIn("--device", result.stdout)
        self.assertIn("--attenuation", result.stdout)


if __name__ == "__main__":
    unittest.main()
