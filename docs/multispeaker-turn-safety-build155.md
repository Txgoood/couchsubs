# Build #155 — safe multi-speaker turn tracking

Windows **v0.3.11 Build #155**, LG webOS **v0.9.33 Build #155**.

- New `MultiSpeakerTurnTracker` tracks at least three sequential anonymous
  speaker identities without merging them.
- Rapid speaker changes, repeated stale detections, low confidence and
  uncertain mixtures **abstain** from character assignment. Explicit subtitle
  speaker names always take priority. No game PCM, soundtrack, effects or TTS
  playback are modified by this tracker.
- Enable experimentally by setting `Dubbing.MultiSpeakerSafety = true` in
  existing user settings, alongside opting into Build #153's isolated Sherpa
  worker. This remains **off by default** for compatibility.
- The algorithm observes **one** model identity at a time. It cannot infer
  the count or correct identities of two simultaneous voices in mixed audio.
  Actual source separation remains pending and requires a verified model.
- Added regression cases for three distinct sequential identities, rapid
  interruptions, uncertain model output, stale observations and reset.

CI is a compilation/unit test gate. PS5/PULSE3D physical field validation
and accuracy measurements remain outstanding.
