# Priority 1 — MSI laptop actual model benchmark, 2026-11-22

Scope: **Build #157 and #161 unfinished together**. Do not mark green.

## Test hardware and software (local authorized device)

- NVIDIA GeForce RTX 5080 Laptop GPU, 16303 MiB; driver 621.24.
- Windows Python 3.12.10 isolated virtualenv; `inworld-voice-isolation==1.0.0`.
- PyTorch CPU 2.15.0+cpu for baseline, then official PyTorch
  `torch==2.11.0+cu128` / `torchaudio==2.11.0+cu128`.
- `torch.cuda.is_available()==True`, GPU `sm_120`.
- Official Inworld realtime checkpoint loaded successfully with the
  upstream registry's pinned SHA-256. 100 input frames -> 100 model
  output frames, in order, via `inworld_live_worker.py --stdio`.
- `python -m unittest discover -s tools/priority1 -p "test_*.py"`:
  5 Python smoke tests passed on the laptop.
- GitHub Windows Run #621: **SUCCESS** (native + dotnet). This proves
  build and unit tests, **NOT real PS5 acoustic safety**.

## Reproducible local-only synthetic measurements

Sample A: synthetic 2 second stereo tones, 100 x 20ms model pushes.

| Backend | 20ms push p50 | p95 | worst |
|---|---:|---:|---:|
| CPU torch 2.15 | 44.69 ms | 53.65 ms | 77.18 ms |

Sample B: Windows System.Speech 4.46s synthetic English sentence
plus generated multi-tone background, 224 x 20ms model pushes.

| Backend | 20ms push p50 | p95 | worst |
|---|---:|---:|---:|
| CPU torch 2.15 | 50.26 ms | 77.13 ms | 98.03 ms |
| CPU OMP_NUM_THREADS=4 | 75.17 ms | 138.17 ms | 165.78 ms |
| RTX5080 CUDA torch 2.11 cu128 | 27.86 ms | 44.50 ms | 8463.74 ms (first-call warmup) |

Metrics measured by `inworld_streaming_probe.py`, which synchronizes CUDA
before/after inference. They are local **offline replay** measurements,
not actual PS5 live-capture/PCM mixer end-to-end latency.

The official model's stated algorithmic latency at
`lookahead_frames=2` is **63ms**. CUDA p95 push 44.50ms is **already
107.50ms** if conservatively added, before any capture, IPC, buffering,
audio output or display latency. Also median 27.86ms exceeds the 20ms
incoming audio frame period, potentially causing sustained backlog.
The initial CUDA call must be warmed before live mode.

### Separation sanity check (synthetic sample only)

Using a two-source least-squares projection against known synthesized
speech + generated tone background:

- Model speech stem coefficient against original speech: **0.989**;
  synthetic background leak coefficient **-0.002**.
- Conservative 25% center suppression produced output speech
  coefficient **0.753** and background coefficient **1.000**.

This is encouraging for *this synthetic input only*; it is **NOT**
evidence about actual GTA6 soundtrack, overlapping voices, stereo sound
quality, or intelligibility. The 25% attenuation cap means the English
voice is deliberately only modestly reduced; stronger removal would
require separately calibrated confidence, leakage tests and guardrails.

## Remaining blocker for turning both builds green

1. Benchmark a causal configuration with lower lookahead and actual
   average *throughput* faster than 20ms/frame, including sustained
   10 minute processing and consistent p95.
2. Integrate the supervised worker with the original 48k stereo PS5 PCM
   path using a bounded 90ms deadline and fail-open fallback. Default
   stays **disabled** until real game quality and synchronization pass.
3. Calibrate a trustworthy speech-stem confidence/quality check.
   Do not invent a 0.95 probability; the model has no verified
   such calibration.
4. Validate 2+ mixed game speakers, effects, music, scene changes and
   subtitles/character speech synchronization on PS5+Sony PULSE3D.
5. Produce, CI-verify and test a paired Windows/webOS release before
   advertising #157/#161 as fully complete.

**Priority 2 remains paused.** CUDA used here is only for testing
whether Priority 1's actual separator can meet its time budget.

## Additional follow-up after GitHub #622 success

Further real RTX5080 and CPU tests of the same synthetic 4.46 second clip:

| Backend / chunk | p50 / model call | p95 | comment |
|---|---:|---:|---|
| CPU, 1 OMP thread, 20ms | 192.54ms | 415.94ms | Far slower; reject |
| RTX5080 CUDA, 20ms, zero model lookahead | 28.23ms | 45.34ms | Throughput still below 20ms/frame |
| RTX5080 CUDA, 40ms chunks | 28.72ms | 57.87ms | Median throughput better, high variance; chunking adds latency |

All measured offline on user MSI system, **not** captured PS5 input.
The announced 63ms algorithmic figure is for the original 20ms
lookahead configuration; it must NOT be quoted as exact for zero lookahead.
For the 40ms chunk result, chunk accumulation and callback wakeups further
increase overall latency and require measurement.

Implemented new `DialogueInferenceAdmissionGate` to keep the experimental
separator disabled until 64+ valid model frames sustain a mean inference
well below real-time (15ms per 20ms chunk) and total p95 predicted age
<90ms. This prevents 20ms overflow even when individual calls are
sometimes fast. A failed inference forces immediate ineligible status.
Also implemented `Ps5DialogueFrameSegmenter` to segment variable
48k stereo PCM16 packets into model-compatible 20ms float frames on a
background thread only. It rejects unsupported input formats rather than
inventing a resampler and corrupting synchronization. Tests added.

**Reminder:** These two classes are groundwork for opt-in live capture,
not currently enabled by application startup, and their presence is not
proof of a working game speech separator. #157 and #161 remain orange.

## Paced CUDA worker replay after adding startup warm-up

On the MSI RTX 5080, a subprocess replay fed 1,000 numbered stereo
frames from the existing synthetic speech-plus-background clip at
20 ms intervals. The CUDA worker warmed up on a separate stream before
printing READY. The replay read and checked each output frame ID.

| Metric | Observed |
|---|---:|
| Worker process start to READY | 3.64 s |
| First input to first matching stem | 102.6 ms |
| Scheduled input to matching stem, p95 | 5,563.6 ms |
| Last scheduled input to matching stem | 5,722.1 ms |
| Frames over the 90 ms budget | 1,000 / 1,000 |

The replay allowed producer and pipe backlog to expose cumulative drift.
This is a synthetic worker/IPC measurement. The application's bounded
queues would reject excess input instead of accumulating this delay.
Keep the diagnostic path read-only and the audio mix unchanged.

## Experimental 40 ms batches

An isolated local experiment combined pairs of 20 ms frames before model
inference, while preserving frame IDs. On the same MSI, synthetic replay
with zero lookahead and FP32 produced 3,000 frames over one minute. The
first matching stem arrived at 108.3 ms, p95 was 123.2 ms, and 2,239 of
3,000 frames exceeded 90 ms. The last two frames are emitted by flush
and their ages do not represent normal steady-state behavior. The run
did not capture PS5 audio or evaluate separation quality. The paired-frame
experiment is not enabled in the application. It improves throughput
compared with individual-frame inference, but still misses the deadline.

An instrumented 1,000-frame replay of the same configuration measured the
40 ms model push at median 26.51 ms and p95 46.76 ms. The subsequent
conversion of the returned tensor to CPU audio took median 0.05 ms and
p95 0.08 ms. Push timing includes work executed before the conversion;
these measurements do not separately identify GPU execution and CPU
scheduling. The full matching-stem p95 was 125.2 ms in that run.

## Owner test on PS5 and Sony PULSE 3D

The owner tested the CI #634 package. PS5 connected, the headset played game
audio without audible dropouts or delay, and Turkish dubbing was audible.
The owner reported better dubbing timing. This validates the existing audio
route. The experimental AI stem still needs its own live trial.

## Paced replay with intermediate chunk sizes

On the MSI, a 12-second synthetic replay with CUDA, FP32 and zero lookahead
reported p95 of 95.10 ms for 20 ms input chunks, 85.41 ms for 25 ms chunks,
and 91.80 ms for 30 ms chunks. The 25 ms run had 3 of 597 completed frames
above 90 ms. Output age starts at the scheduled end of each 20 ms output
frame and excludes the final flush, PS5 capture, IPC and headset output.
The 25 ms input is a benchmark configuration. The existing PS5 worker
receives 20 ms input frames.

The 60-second repeat at 25 ms measured p95 86.44 ms. It returned 2,997
complete frames before flush, of which 38 exceeded 90 ms. This leaves
3.56 ms at p95 before the other stages of the live pipeline. The matching
60-second run with 20 ms input reported p95 142.65 ms and 1,282 late
frames out of 2,997.

Restricting attention history to 64 or 16 frames in separate 12-second
runs did not get 20 ms input below the target. Output p95 was 120.73 ms
and 124.69 ms respectively. Leave the runtime model settings unchanged.

Reproduce with the installed Inworld CUDA environment:

```powershell
python -B tools/priority1/inworld_paced_probe.py path/to/local-48khz.wav --chunk-ms 20 25 30
```

To simulate fixed 20 ms delivery, add `--packet-ms 20`. The model then
waits for the packet containing the last sample of each input chunk.
On the MSI, a 6-second FP32 replay with zero lookahead tested 20, 21,
22, 24, 25, 26, 28, 30 and 40 ms chunks. The best p95 was 101.29 ms
with 24 ms chunks. 52 of 297 complete frames exceeded 90 ms in that
configuration. These measurements omit PS5 transport and playback.

## RNNoise candidate

A separate CPU experiment used the Windows PyPI wheel for pyrnnoise
0.4.6. It ran the model in 480-sample blocks at 48 kHz. Model-call p95
was 0.35–0.40 ms across the synthetic cases. A 30-second replay delivered
960 samples every 20 ms and measured from the scheduled end of the
previous packet, assuming 960 samples of model delay. It returned p95
22.83 ms, maximum 29.57 ms, with 0 of 1,499 frames above 90 ms.
This timing omits PS5 transport, application IPC, and playback.

A least-squares projection of the output on the aligned clean speech
and background yielded the following amplitude coefficients:

| Synthetic input | Speech | Background |
|---|---:|---:|
| Speech with test tones | 0.779 | 0.158 |
| Speech with music and effects | 0.674 | 0.155 |
| Overlapping speech with music and effects | 0.602 | 0.229 |

The correlation peak aligned within one sample of 960 samples for
these speech clips. This is not an alignment guarantee for live use.
Background in the estimated speech stem would also be attenuated
when the stem is subtracted. RNNoise needs representative game audio
quality, a reliable safety criterion, and live headset validation
before it can drive the mix.

To reproduce in an isolated Python environment with pyrnnoise 0.4.6:

```powershell
python -B tools/priority1/rnnoise_model_probe.py clean-synthetic-speech.wav matched-synthetic-mix.wav
```

The opt-in RNNoise worker also passed a binary-stdio replay on the same
MSI. All 1,500 input frame IDs matched the returned IDs in order.
Output age p95 was 23.3 ms, with no frames above 90 ms. The final frame
was returned by flush. A separate replay with a missing input ID
terminated before producing a mismatched stem. These were synthetic
tests of the worker process and its pipes.
