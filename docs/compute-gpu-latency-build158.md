# Build #158 — compute headroom and GPU readiness

Windows **v0.3.14 Build #158**, LG webOS **v0.9.36 Build #158**.

This build improves the resource and latency safety layer of the existing
Windows dubbing pipeline without altering the PS5 audio callback:

- Pocket TTS worker `RAYON_NUM_THREADS` budget now defaults to at most
  four threads and approximately one quarter of logical processors, leaving
  CPU headroom for Remote Play, OCR, sound and translation. Override using
  `COUCHSUBS_TTS_THREADS=1..8` (clamped to local CPU count).
- ONNX Marian encoder/decoder use unchanged defaults unless
  `COUCHSUBS_ONNX_THREADS=1..8` is explicitly specified. This is a CPU
  tuning knob, not a GPU option.
- The Dubber now exposes metadata-only average/peak synthetic latency
  measurements and counts slow (>1500ms) model responses. It does not
  retain dialogue contents or raw PCM.
- A strict compute backend policy refuses to claim usable GPU acceleration
  unless a provider is **packaged, compatible with the model, runtime
  verified, and explicitly enabled**.

**Status of RTX 5080 acceleration:** This release does NOT bundle the ONNX
Runtime CUDA or DirectML execution provider and therefore performs no
actual GPU inference. The existing `Microsoft.ML.OnnxRuntime` package is
CPU-only. GPU execution requires a separately tested matching native
provider + CUDA/cuDNN or DirectML dependencies, model compatibility and
physical testing. The safe CPU fallback is intentional.

Existing audio mixer, TV/webOS app identity, PS5 capture, subtitle pipeline,
SmartTiming, speaker identities and all opt-in modes remain unchanged.
GitHub CI checks compilation and regression tests only; actual RTX 5080
performance and PULSE 3D latency still require device testing.
