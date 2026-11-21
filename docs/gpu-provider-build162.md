# Build #162 — opt-in DirectML negotiation and CPU fallback

Windows **v0.3.18 Build #162**; LG webOS **v0.9.40 Build #162**.

Build #162 adds `OptionalGpuSessionFactory` to the Windows ONNX
dialogue-stem adapter:

- Environment `COUCHSUBS_ENABLE_DIRECTML=1` explicitly requests DirectML.
- `COUCHSUBS_DIRECTML_DEVICE=0..8` chooses a bounded device index (default 0).
- The factory detects a compatible managed DirectML provider method and
  requests a GPU inference session. If the method/native runtime is
  absent, unsupported or fails to initialize, it creates a fresh **CPU**
  session. The adapter reports `GpuActive` and `ExecutionStatus`.
- Missing model paths fail without claiming that any GPU is active.
- GPU session creation is strictly outside PS5/game audio callbacks.
- Existing default CPU inference, subtitles, PULSE3D mix, webOS app ID and
  character memory remain unchanged. The experimental #161 adapter is not
  automatically started with the live game stream.

**Major remaining limitations:** This PR does not package a verified
DirectML native execution provider nor a compatible dialogue-separation
ONNX model. It does NOT validate CUDA or RTX 5080 execution or show GPU
performance results, and does NOT yet provide real-time English-dialogue
isolation. DirectML is distinct from CUDA. Actual GPU acceleration must
be confirmed from `GpuActive == true` and real hardware benchmarks.
Do not set the opt-in as default until validated.

Next: package matching runtime binaries with hashes/licensing, add GPU
inference smoke tests on actual target hardware, benchmark CPU-vs-GPU,
and connect an isolated measured model worker to safe PCM routing.
