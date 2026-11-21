# CouchSubs Build #157 — stereo-safe dialogue-stem integration

Windows **v0.3.13 Build #157** / LG webOS **v0.9.35 Build #157**.

This increment improves the pre-existing optional `PushSeparatedGameAudio`
consumer. When an **external model has already produced** a reliable,
same-frame 48-kHz stereo dialogue stem, the pipeline now:

- rejects stale (>90ms), low-confidence (<0.90), mismatched, non-finite,
  implausible, or out-of-order stem results and copies original PCM exactly;
- reduces **only the shared mid channel** (maximum 25% of the model-estimated
  voice) while preserving the L-R stereo side channel;
- fades suppression in over 10ms instead of applying a hard step;
- leaves regular `PushGameAudio` from PS5/Remote Play untouched; when there
  is no trusted stem, the original game audio is always used;
- resets routing and gains when the audio output is disposed.

The model's isolation, latency, and synchronization must be validated
separately. **This build does not bundle or run a neural source-separation
model and therefore does not automatically remove English speech.** An
external, verified model producer is still needed. In particular stereo
center-channel attenuation on its own is not source separation.

The user-activated existing ambience profiles (Legacy, SafeOriginal,
SpatialBalanced, Cinematic, AdaptiveDialogue), PULSE3D stereo PCM, existing
character memory, dubbed voice choice, TTS, subtitles, and application IDs
remain unchanged.

GitHub CI passes code/test compatibility if green; PS5/PULSE3D physical
quality measurements and external source-separation model integration
remain pending.
