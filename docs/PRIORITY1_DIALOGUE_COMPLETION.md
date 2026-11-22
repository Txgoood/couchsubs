# Priority 1 — close Build #157 + #161 (dialogue source separation)

Status: **IN PROGRESS**. Branch `priority1-dialogue-model-integration-20261121`
forked from the last successful Build #164. No new release/build number is
claimed until the actual dialogue-separation feature passes verification.

## Research decision, 2026-11-21

**Primary offline evaluation candidate:** DBS-TIGER-DnR (model
`JusperLee/DBS-TIGER-DnR`, Apache-2.0 model card), official PyTorch engine.
Model card: https://huggingface.co/JusperLee/DBS-TIGER-DnR

Official documented input is **44.1kHz mono** with **6 second windows /
3 second hop**. Outputs: music, sound effects, speech. This is a real
cinematic-source separation model, but not currently a 90ms realtime PS5
stereo processor. Do NOT feed it to `OnnxDialogueStemAdapter` from #161:
that adapter expects float32 `audio` [1,2,480] 48k stereo 10ms; the DBS
weights are PyTorch Safetensors, not that ONNX contract.

## Implemented in the first Priority-1 branch

1. `DialogueSeparationReadiness` checks licensing, speech output, sample
   rate, channel count, model window duration, lookahead, measured inference
   and verified target-game hardware measurements. Unit tests prove the
   DBS-TIGER offline profile cannot be mistaken for live-safe.
2. `tools/priority1/dbs_tiger_offline_probe.py`: optional manual, real
   model inference using the official project engine. Reads only local,
   authorized **44.1kHz mono WAV**; writes separate `speech.wav`,
   `music.wav`, `effects.wav` and JSON timing report into an empty
   output directory. Reports CPU vs CUDA *only if actually run* on hardware.
   Limits input to 15 seconds by default; never auto-records, uploads or
   downloads user game audio; no automatic inclusion in the installer.
3. Existing PS5 Remote Play audio callback, PULSE3D stereo mixer,
   fallback route, subtitles and webOS app ID are untouched. No
   model license or model weights are silently copied into this repo.

## Manual test (requires explicit local setup)

Install the **official** DBS Dynamic-Band-Splitting Python project with
its documented instructions and dependencies. Obtain a copyright-cleared
mono 44.1kHz WAV sample, then run from repo root:

```powershell
python tools/priority1/dbs_tiger_offline_probe.py --input C:\\audio\\sample-mono44100.wav --output C:\\audio\\dnr-result --device cpu
```

The output directory must be empty. A manual GPU test may use `--device
cuda` only after compatible PyTorch/CUDA is installed; the script refuses
to claim GPU when unavailable. No model packaging is implied.

## Required before #157 + #161 turn green

- Verify the model license/checkpoint/hash and benchmark speech leakage,
  music/effects corruption, interference and actual inference time with
  representative game audio.
- Select a compatible **causal/low-lookahead** neural dialogue model if
  the candidate's 6-second windows cannot meet realtime latency.
- Build isolated supervised worker, bounded channel buffers, stereo
  reconstruction and artifact-free alignment; test with PULSE3D.
- Add producer integration to existing `SeparatedAudioFrameRouter` and
  `DialogueStemSafetyGate` with conservative confidence thresholds
  and immediate unmodified PCM fallback.
- Pass hardware target <90ms added age budget and confirm real PS5
  speech/subtitle/TTS synchronization; otherwise do not enable live mode.

## Priority checklist

P1 #157/#161: ACTIVE; candidate + offline probe and readiness gate present,
                       real model stream integration and hardware tests open.
P2 #158/#162: WAITING for a selected compatible model and GPU package.
P3 #159/#163: WAITING for an expressive voice model.
P4 #160/#164: software checks passed; actual console/headset long tests open.

Tracking PR: https://github.com/Txgoood/couchsubs/pull/49
CI must pass on this priority branch before any attempt to merge or release.

## 2026-11-21: better real-time candidate — Inworld Voice Isolator v1 realtime

Official source: https://github.com/inworld-ai/voice-isolator
Official model card: https://github.com/inworld-ai/voice-isolator/blob/main/MODEL_CARD.md

- The released model is `voice-isolator-v1-realtime`, Apache-2.0 (code and weights).
- Official model registry pins Hugging Face revision
  `9b0285b3fe8403aa8e2f1dbb2165a80a4294c970` and SHA256
  `76598f05523c0669e76562a8b2f31c9d395fca316e80c907ee11775e7497ae0f`.
- Real streaming inference uses 48kHz mono, 10ms hop and 20ms lookahead,
  with **63ms published algorithmic latency** at two lookahead frames.
  This is *not* measured end-to-end PS5 latency, and the model does not
  output a calibrated dialogue confidence score.
- `tools/priority1/inworld_streaming_probe.py` actually invokes the official
  streaming `VoiceIsolator.stream().push()/flush()` API on local authorized
  PCM16 WAV. It runs 20ms chunks, checks every produced sample, measures
  CPU/CUDA inference push time (if installed on a real device), and writes
  `speech.wav`, `original_stereo.wav`, `conservative_stereo.wav`,
  and `metrics.json`. It preserves the source's L-R stereo difference
  when auditioning conservative subtraction. No model or audio is
  uploaded by this project; the upstream model weights may be downloaded
  once by the installed official package.
- This probe is **offline replay**, not live PS5 processing. The actual
  Windows mixer remains on unmodified PCM until a validated worker with
  bounded alignment/age, speech confidence gating and automatic fail-open
  can be connected safely.
- CI runs the dependency-free Python parser/syntax smoke tests. Real
  58M-parameter model inference is intentionally not claimed or exercised
  by the GitHub runner.

### Manual actual-model validation on MSI/RTX5080 (user PC required)

1. Install Python >=3.10 and official package
   `python -m pip install inworld-voice-isolation==1.0.0`.
2. For a 10s authorized 48kHz PCM16 stereo sample, run from repo root:

   `python tools/priority1/inworld_streaming_probe.py --input C:\\audio\\test48k.wav --output C:\\audio\\inworld-test --device cpu`

3. A *separate* CUDA trial can use `--device cuda` only when PyTorch
   CUDA availability has been confirmed and the GPU is ready. Record p95,
   model loading and audible speech bleed; listen to the original and
   conservative mix. Never mark RTX5080 confirmed from documentation alone.
4. Prefer a 10-30s clip with mixed speech + music/effects and several
   speaker turns. Compare playback and check no severe music loss.

### Updated green criteria for #157 + #161

- [x] Identify legally redistributable real-time 48kHz model candidate.
- [x] Add actual model streaming API in local authorized offline probe.
- [x] Prevent solo model-window / inferred speed claims from enabling live PS5.
- [ ] Confirm actual model inference on owner's computer, with signed timing metrics.
- [ ] Measure total STFT+lookahead+inference+transport <90ms with safety margin.
- [ ] Build supervised fault-isolated realtime worker, bounded queue and
      original-stereo-preserving synchronization, all tested under failure.
- [ ] Validate source separation on representative game music/effects and
      overlapping voices, including false-positive speech and clipping.
- [ ] Connect the model's verified stem to PULSE3D safe fallback path.
- [ ] Real PS5/PULSE 3D long test passes and owner confirms audible quality.

Do **not** advance to Priority 2 until every box above is verified.

## PS5 live diagnostic tap (added after #622)

`InworldLiveDiagnosticTap` is connected to the PS5 audio capture
handler in `AppController` as a strictly **read-only** side-channel.
The original PCM still goes through `PushGameAudio` without touching
the model. It is disabled by default and requires all these environment
variables before launching the Windows app:

- `COUCHSUBS_ENABLE_INWORLD_DIAGNOSTIC_TAP=1`
- `COUCHSUBS_INWORLD_PYTHON=<absolute path to Python 3.12 venv python.exe>`
- `COUCHSUBS_INWORLD_WORKER_SCRIPT=<absolute path to inworld_live_worker.py>`
- `COUCHSUBS_INWORLD_DEVICE=cuda` (optional; default CPU)

For an RNNoise diagnostic session, point
`COUCHSUBS_INWORLD_WORKER_SCRIPT` at the absolute path to
`tools/priority1/rnnoise_live_worker.py`, use an isolated Python environment
with `pyrnnoise==0.4.6`, and set `COUCHSUBS_INWORLD_DEVICE=cpu`.
The existing tap receives the stems over its PCM protocol. The worker
discards the measured startup delay and stops on a discontinuity in
frame IDs. RNNoise voice activity scores are unsuitable as stem confidence
without game-audio calibration. The tap keeps its read-only behavior.

Set `COUCHSUBS_INWORLD_METRICS_PATH` to an absolute path in an existing
writable directory to append one JSON summary when the session stops.
It contains counts of dropped or rejected chunks and stems, p95 age,
and the count over 90 ms. Age runs from the application audio callback
that completes the frame to the worker reply. It excludes console
transport before that callback and headset playback. The report contains
timings and counts only.

The tap can observe genuine stereo Remote Play PCM when it is captured
and can call the genuine Inworld subprocess. **It does not yet replace
or attenuate the English audio.** The bounded diagnostic queue can drop
observations under overload without affecting game playback. The model's
stem has no calibrated voice-confidence probability; never invent one.

New pipeline guard `DialogueInferenceAdmissionGate` requires a
minimum sample window, sustained faster-than-realtime inference and
the total 90ms model-to-mixer budget before any eventual audio-path
enablement. New `Ps5DialogueFrameSegmenter` prevents partial Opus
packets or wrong sample rates from masquerading as valid 20ms model
frames. Unit tests cover the default-off behavior and frame alignment.

**Green still requires:** true PS5/PULSE3D game playback measurements,
model inference stable and faster than real-time, proper audio output
reconciliation and user audible validation. A side-channel test or a
green CI run alone is not feature completion.
