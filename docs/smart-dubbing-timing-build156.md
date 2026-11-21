# Build #156 — opt-in smart dubbing timing

Windows **v0.3.12 Build #156**, LG webOS **v0.9.34 Build #156**.

The Windows `Dubber` now supports an **opt-in** `Dubbing.SmartTiming`
preference (default: false, preserving legacy user audio/translation behavior).

- Suppress an identical OCR speech batch repeated within 900ms. This avoids
  cutting the current Turkish speech and synthesizing it twice as the same
  English subtitle persists over adjacent video frames.
- If a queued TTS result is older than 3500ms when it becomes ready, discard
  the outdated synthesized line rather than playing an unrelated old line.
- Existing generation/cancellation logic already cancels superseded dialogue,
  and older queued subtitle lines remain capped at three.
- Do not change voice character selection, TV playback transport, headset
  mixer, PS5 sound, audio output level or subtitle style.
- Timing stats include dropped late lines and suppressed duplicate batch count;
  no dialogue contents or raw audio are persisted by this guard.

This is a latency safety baseline, not phoneme-level lip sync or a new fast
TTS model. Excessive synthesis delay may result in a skipped line by design.
Tests cover repeats, different dialogue, expiry, clock reversal and reset.
Physical PS5/PULSE3D validation remains outstanding.
