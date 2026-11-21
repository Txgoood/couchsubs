# CouchSubs Build #164 — long-session PULSE 3D audio diagnostics

Windows **v0.3.20 Build #164**, LG webOS **v0.9.42 Build #164**.

The existing Windows dubbing health card now shows passive PS5/PULSE3D PCM
ring-buffer statistics: dropped samples (when input overfills a bounded
250ms buffer) and underrun samples (when the sink requests more audio than
is available). The audio interface exposes **counts only**, never raw PCM.

A `LongSessionAudioMonitor` compares 1–30 second observation windows;
it does not treat initial startup, rapid UI refresh, or 30+ second
sleep/resume gaps as valid measurements. A >100ms stereo input overflow
during a window produces an advisory backlog warning. Silence underruns
are shown only as counts, NOT automatic evidence of a sound fault.

Synthetic regression tests exercise 10,000 repeated PCM writes with
intermittent reads to verify the ring buffer remains bounded. Other
tests cover silence, backlog, first observation, reset and sleep/resume.

**Important:** The tests run on GitHub's Windows runner, NOT the owner's
physical PS5, AGON OLED, RTX 5080 or Sony PULSE3D. This release does not
prove 3-hour physical stability or audible glitch-free mixing. No
automatic TTS/stream restart, game-audio alteration, external telemetry,
or new privileged service has been introduced. The webOS version number
is paired with Windows, without changing the webOS playback engine.

Open tasks from earlier builds remain unfinished: verified neural dialogue
separation (#157/#161), real GPU provider packaging/hardware benchmark
(#158/#162), and neural emotional voice model (#159/#163).
