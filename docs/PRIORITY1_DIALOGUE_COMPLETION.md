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
