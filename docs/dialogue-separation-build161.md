# CouchSubs Build #161 — ONNX dialogue-separation adapter (experimental foundation)

Windows **v0.3.17 Build #161**, LG webOS **v0.9.39 Build #161**.

The first real ONNX Runtime model-execution adapter is in
`windows/src/CouchSubs.Translation/OnnxDialogueStemAdapter.cs`.

- An explicitly supplied **local** ONNX model may be loaded only if its
  tensor contract is compatible: input `audio` float32 [1,2,480],
  output `dialogue` float32 [1,2,480], 48 kHz stereo, 10ms PCM.
- The adapter invokes ONNX Runtime CPU inference, verifies tensor shapes,
  finiteness, and the time budget, and fails closed if an output is invalid.
- Future output must additionally pass the existing Build #157 stem
  confidence/alignment gates and must never be injected after a stale frame.
- Model selection uses `COUCHSUBS_DIALOGUE_STEM_MODEL` pointing to a fully
  qualified local path and `COUCHSUBS_ENABLE_DIALOGUE_SEPARATOR=1`.
  These variables **do not automatically start inference** in the app;
  they document explicit opt-in prerequisites for the separate integration.
- The default PS5 Remote Play -> PULSE3D path is unchanged and bypasses this
  adapter. Soundtrack, music, stereo effects, TTS, character memory,
  subtitles, webOS app ID and Android TV apps remain untouched.

**NOT COMPLETE:** No source-separation model has been selected, bundled,
licensed and benchmarked for game dialogue. Most public vocal/instrument
models do NOT use the above tiny-PCM ONNX tensor contract and would require
real resampling/STFT/windowing. The adapter is not yet connected to a
real-time supervised producer. This build does **not** actually isolate
English dialogue from GTA 6 music/effects. Do not use an arbitrary ONNX
file or claim that sample-level latency meets 90ms without real benchmarks.

Next mandatory tasks before marking #161 complete:
1. Choose a legally distributable, high-quality dialogue-vs-ambience model.
2. Implement its exact preprocessing, chunking, overlap-add and outputs
   in an isolated supervised worker, with crash/latency fallback.
3. Connect producer to the existing alignment gate without delaying,
   duplicating or dropping PS5 game audio.
4. Run actual PS5/PULSE3D and RTX5080 audio quality/latency tests.
