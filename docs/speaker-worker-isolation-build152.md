# Speaker recognition isolation — Build #152

This is the first **fail-closed** step toward recovering automatic acoustic
speaker identification after the Windows native access violation in Build #146.

- `NativeSpeakerSafetyPolicy.InProcessInferenceAllowed` remains **false**.
- `SpeakerWorkerProtocol` validates a bounded, versioned health response from
  a **future separately launched child process**. It does not launch Sherpa.
- `SpeakerWorkerCircuitBreaker` disables requests after three errors until a
  new explicit session; no game audio, raw voice recording, or embedding is
  written by this safety component.
- A future worker must run Sherpa native inference only inside its own
  process with timeouts, restart limits, model checksum verification and
  supervised exit handling. The application must fall back to subtitles and
  manually selected character voices on any failure.
- This build **does not enable automatic speaker identification**, persistent
  voiceprints, multi-speaker overlap detection, or visual identity matching.
- PS5/PULSE 3D and webOS 2.0 field testing remains necessary; CI success is
  not a field-test substitute.
