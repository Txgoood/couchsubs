# Build #163 — expressive character-turn planning

Windows v0.3.19 **Build #163**; LG webOS v0.9.41 **Build #163**.

Adds a conservative per-character `ExpressiveTurnPlanner` to the existing
Windows dubbing text pipeline. It is used ONLY when the existing optional
`Dubbing.ExpressiveSafety` setting is enabled (default false), so the
legacy Pocket-TTS emotion tags remain unchanged for existing installations.

- English subtitle lexical cues such as clear apology or explicit commands
  can select the supported Pocket-TTS emotion label without guessing from
  mixed PS5 soundtrack volume.
- Emotion continuity is limited to the same named character, an emphatic
  continuation, and at most **2200ms**. A `NewTurn` boundary refuses carry,
  and missing/unreliable speaker labels lead to neutral tags.
- Acoustic/Neural carry requires actual recent speech activity; silence,
  explosions and music alone cannot classify sadness, anger or excitement.
- 128 named speakers maximum can be retained only in volatile session memory;
  counters reset on session Stop. Character voice assignment and game sounds
  are never changed.
- Unit tests cover different characters, new turn, old state, no vocal
  evidence, and explicit lexical evidence.

**Limits:** This does not install or train a new neural expressive TTS,
voice-emotion recognition model or real-time speech-to-speech tone transfer.
Existing Pocket-TTS support is limited to the previously available tag
vocabulary. Natural perceptual effect still needs real PS5/PULSE3D testing.
Open #161 neural dialogue separation and #162 GPU provider work remain open.
