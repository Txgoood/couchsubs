# CouchSubs Build #160 — dubbing health panel and long-session safety

Windows **v0.3.16 Build #160**, LG webOS **v0.9.38 Build #160**.

Windows dubbing settings now include a **passive health panel refreshed
every five seconds while the card is visible**, exposing:

- TTS synthesis average/peak latency, slow-call count (>1500ms).
- Current pending dialogue backlog, late-line skips, duplicate OCR
  suppression, and neural speech inference calls/errors.
- Clear states: Idle, InsufficientData, Healthy, Warning, Error.
- Rule-based hints for missing models, reported engine faults, full
  queues, high TTS latency, and high speech inference error ratio.
- No recorded PCM, subtitle lines, speaker voiceprints, personal data,
  or network telemetry in this panel. The counters are in-memory only.

**Safety:** This component never restarts PS5 Remote Play, resets a TTS
model, interrupts audio callbacks, changes subtitles or pauses game sound.
Automatic quality checks are informational and do not claim to measure
objective audio fidelity, voice accuracy, or MOS scores. Thresholds are
diagnostic rules, not automatic fixes. CI tests cover silence, normal
operation, model errors, backlog, delays and speech-model errors.

This build does **not** complete the separate outstanding:
- Build #157 neural speech source-separation model integration,
- Build #158 RTX 5080 CUDA/DirectML model execution,
- Build #159 independent neural emotional voice classification/synthesis.

These require separately validated models and physical device testing.
