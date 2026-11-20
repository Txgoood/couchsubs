// SPDX-License-Identifier: AGPL-3.0-only
using System.Diagnostics;
using CouchSubs.Shell;
using Xunit;

namespace CouchSubs.Shell.Tests;

public sealed class AiPerformanceMetricsTests
{
    [Fact]
    public void MetricsNeverCaptureAudioOrSubtitleContents()
    {
        var metrics = new AiPerformanceMetrics();
        metrics.RecordPacket();
        metrics.RecordPacket();
        metrics.RecordInference(Stopwatch.GetTimestamp());
        metrics.RecordError();
        var stats = metrics.Snapshot();
        Assert.Equal(2, stats.AudioPackets);
        Assert.Equal(1, stats.InferenceCalls);
        Assert.Equal(1, stats.InferenceErrors);
        Assert.InRange(stats.AverageInferenceMs, 0, 10000);
        Assert.True(stats.PeakInferenceMs >= stats.AverageInferenceMs);
        metrics.Reset();
        Assert.Equal(default(AiPerformanceSnapshot), metrics.Snapshot());
    }

    [Fact]
    public async Task MetricsAreSafeAcrossBackgroundAudioThreads()
    {
        var metrics = new AiPerformanceMetrics();
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            for (var n = 0; n < 250; n++)
            {
                metrics.RecordPacket();
                metrics.RecordInference(Stopwatch.GetTimestamp());
            }
        })));
        Assert.Equal(1000, metrics.Snapshot().AudioPackets);
        Assert.Equal(1000, metrics.Snapshot().InferenceCalls);
    }

    [Fact]
    public void ExistingUserSettingsDefaultToTheOriginalSileroMode()
    {
        var settings = new DubbingSettings();
        Assert.Equal(SpeechDetectionEngine.Automatic, settings.SpeechDetection);
        Assert.True(settings.AutoSpeakerTracking);
        Assert.Equal(DubbingEmotionMode.Balanced, settings.EmotionMode);
        Assert.Equal(82, settings.GameAtmospherePercent);
        Assert.Equal(85, settings.TurkishVoicePercent);
    }
}
