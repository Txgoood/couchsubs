# Build #159 — expressive Turkish dubbing safety

Windows **v0.3.15 Build #159**, LG webOS **v0.9.37 Build #159**.

This increment preserves existing Pocket-TTS Turkish emotion tags, and adds
the opt-in `Dubbing.ExpressiveSafety` preference (default: **false**).
An `ExpressiveDeliveryGuard` now rejects ambiguous inherited emotion
assignments when the model has no reliable lexical indication. Character-
specific emotion may carry for only 3.5 seconds and only to the SAME named
character. Speech activity is required in Acoustic/Neural carry modes.
A lone exclamation point in English subtitles is not enough evidence of anger.

The guard does not change the assigned character voice, existing VoiceCast,
original PS5 game sound/music/effects, PULSE3D routing, subtitles, or LG webOS
application identity. It is only a text-grounded TTS tag safety layer.
The prior text-based emotional routing continues unmodified unless the
user explicitly enables the new safety preference.

**Important limitations:** Mixed PS5 audio and Silero VAD cannot classify
anger, excitement, or sadness reliably. No neural vocal emotion classifier,
independent expressive neural voice model, voice pitch transformation, or
simultaneous speech separation was added. Pocket-TTS emotion tags are hints,
not proof of audible emotional delivery. Real PS5/PULSE3D tests remain pending.
