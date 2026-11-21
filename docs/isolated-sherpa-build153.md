# Build #153 — isolated Sherpa speaker worker

Windows v0.3.9 / Build #153. LG webOS v0.9.31 / Build #153.

The parent CouchSubs.exe **never** calls Sherpa native inference. It starts
a second instance of the same executable with `--speaker-worker` before WPF
initialization. Only that child loads the pinned Sherpa model. A native crash
can terminate the child, but the Windows parent remains alive.

Parent-child communication uses private redirected stdin/stdout, no open network
port. The parent enqueues at most four audio packets and never waits for worker
inference on the PS5 audio callback. Startup is bounded by 5 seconds and each
packet response by 750 milliseconds; failures terminate the child and preserve
ordinary subtitles, manually assigned voices, TTS, and unmodified game audio.
The worker buffers two seconds of Silero-confirmed speech before embedding
inference. Music and effects are **not** source-separated; speaker identities
are provisional. No audio or embedding is saved to disk.

**Safe rollout:** The feature is intentionally off by default. For a controlled
test, set `COUCHSUBS_ENABLE_ISOLATED_SPEAKER=1` before launching CouchSubs.
The existing AutoSpeakerTracking preference still governs use. Remove the
environment variable or unset the preference to disable the experiment.

Validation: GitHub CI verifies build/tests and the packaged Windows/webOS
artifacts. No claim of accurate identification, reliable three-person overlap
recognition, or PS5/PULSE 3D physical testing until recorded field tests pass.
