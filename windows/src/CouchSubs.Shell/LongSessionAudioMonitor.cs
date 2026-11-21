// SPDX-License-Identifier: AGPL-3.0-only
using CouchSubs.Core.Dubbing;

namespace CouchSubs.Shell;

/// <summary>
/// Passive long-session audio telemetry. Compares consecutive samples of the
/// bounded PS5 PCM ring buffer. Never copies PCM, changes gains, or restarts
/// the game stream. No sample means "insufficient data", not a healthy stream.
/// </summary>
public sealed class LongSessionAudioMonitor
{
    private readonly object gate = new();
    private GameAudioBufferStats previous;
    private long previousMs;
    private bool initialized;
    private long samples;
    private long warnings;

    public LongSessionAudioSnapshot Observe(GameAudioBufferStats current, long nowMs)
    {
        lock (gate)
        {
            if (!initialized || nowMs < previousMs ||
                current.DroppedSamples < previous.DroppedSamples ||
                current.UnderrunSamples < previous.UnderrunSamples)
            {
                previous = current;
                previousMs = nowMs;
                initialized = true;
                return new LongSessionAudioSnapshot(false, 0, 0, 0, samples, warnings);
            }

            var interval = nowMs - previousMs;
            // At most one quality judgement per second. Avoid interpreting
            // rapid UI refreshes or a long sleep/resume gap as audio failures.
            if (interval < 1000)
                return new LongSessionAudioSnapshot(false, 0, 0, interval, samples, warnings);
            if (interval > 30000)
            {
                previous = current;
                previousMs = nowMs;
                return new LongSessionAudioSnapshot(false, 0, 0, interval, samples, warnings);
            }
            var dropped = current.DroppedSamples - previous.DroppedSamples;
            var underrun = current.UnderrunSamples - previous.UnderrunSamples;
            previous = current;
            previousMs = nowMs;
            samples++;
            // 48k stereo: >100ms of dropped input over one window indicates
            // significant backlog. Underruns may be expected during silence;
            // display them as metadata, not definitive game-audio failure.
            var warn = dropped > 9600;
            if (warn) warnings++;
            return new LongSessionAudioSnapshot(true, dropped, underrun, interval, samples, warnings, warn);
        }
    }

    public void Reset()
    {
        lock (gate)
        {
            initialized = false;
            previous = default;
            previousMs = 0;
            samples = 0;
            warnings = 0;
        }
    }
}

public readonly record struct LongSessionAudioSnapshot(bool ValidWindow,
    long DroppedSamples, long UnderrunSamples, long WindowMs,
    long ObservedWindows, long WarningWindows, bool BacklogWarning = false);
