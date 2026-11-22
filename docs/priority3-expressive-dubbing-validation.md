# Priority 3 expressive dubbing validation

The Windows dubbing settings expose the existing opt-in expressive safety
preference as “Duyguyu aynı konuşma içinde sürdür”. A named new turn clears
that character's previous emotion. An anonymous new turn clears the
previous character context. A neutral boundary reaches the planner even
when the legacy tagger returns no emotion.

## Local Pocket TTS check

On the MSI, the packaged Pocket TTS executable and the pinned Turkish model
generated “sonunda buradasın.” twice without a tag and once with each
supported tag. All samples used the same built-in male voice. Both neutral
runs were 1.64 seconds and had estimated median pitch 110.6 Hz.

| Tag | Duration | Estimated median pitch |
|---|---:|---:|
| mutlu | 1.56 s | 110.6 Hz |
| üzgün | 1.64 s | 133.3 Hz |
| kızgın | 1.64 s | 112.7 Hz |
| şaşkın | 1.64 s | 114.3 Hz |
| sakin | 1.64 s | 116.0 Hz |

Pitch was estimated by autocorrelation on short voiced windows. Pitch
and duration differences do not establish that listeners recognize the
intended emotion. The samples were made with the current model and a
single sentence. Listening across voices and testing a real PS5 dubbing
session remain open.
