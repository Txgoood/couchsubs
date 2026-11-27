# PS Remote Play Legacy

Target: LG 49UF8507-ZB, webOS 2.0. Planned version 0.1.0 / build 1 (not yet a functioning Remote Play release).

Upstream: https://github.com/TacticalBill/chiaki-lg (Chiaki-ng native port). Preserve upstream licenses and notices. No proprietary PXPlay code.

## Validation gates
1. Package and launch the independent ES5 capability probe via Developer Mode; no root required for the probe.
2. Verify ARM ABI, glibc and availability of native decoder and SDL input on the actual TV; upstream targets newer firmware. A successful HTML app does not prove native compatibility.
3. Build libchiaki with a compatible toolchain/sysroot and audit dynamic dependencies. Validate PS5 pairing on LAN, then H.264 720p30, audio, controller, 720p60 and 1080p60 in order. Do not claim 60 fps without measurement.
4. Integrate CouchSubs only after standalone streaming works. Keep main branch and IPTV repositories unchanged.

## Probe
Run `ares-package remote-play-legacy/probe` from repository root with LG CLI installed, then install the generated IPK using your configured TV device. The probe reports browser capabilities only; it cannot pair or stream PS5. Version 0.1.0 is reserved for the actual client; probe is 0.0.1.
