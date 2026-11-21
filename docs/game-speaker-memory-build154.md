# CouchSubs Build #154 — game-scoped speaker memory

Windows **v0.3.10 Build #154**, LG webOS **v0.9.32 Build #154**.

The experimental out-of-process Sherpa speaker worker (Build #153) now
keeps **normalized embedding vectors only**, using a per-game hashed filename in
the current Windows user's LocalApplicationData/CouchSubs/speaker-memory folder.
No raw PCM/audio recordings or subtitles are written by this component.

- An anonymous identity such as "Ses 1" can be recalled after restarting
  CouchSubs **only when the speaker embeddings match**.
- Each game's registry is loaded independently; switching games clears active
  embeddings before loading the selected game.
- Character rename and removal operations are passed to the isolated worker,
  and deleting a game's library removes its acoustic profile file.
- Snapshots are written atomically. Malformed vectors and oversized files
  fail closed. The manual character-to-Turkish-voice mapping remains stored
  in the pre-existing VoiceCast/GameCasts settings.
- Feature remains **off by default** unless
  COUCHSUBS_ENABLE_ISOLATED_SPEAKER=1. The in-process native crash guard
  remains false, and LG application ID is unchanged.

**Limitations:** Dialogue text alone does not validate who spoke. Mixed
music/effects and overlapping speech can cause false matches. This does not
implement source separation, visual recognition or calibrated biometric
accuracy. GitHub Actions validates build/tests, not PS5/PULSE 3D field
accuracy. Any misidentification should be corrected using the character
library. This is an opt-in local gameplay feature, not a real-world identity
verification system.
