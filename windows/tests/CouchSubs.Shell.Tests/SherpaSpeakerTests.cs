// SPDX-License-Identifier: AGPL-3.0-only
using CouchSubs.App.Services;
using CouchSubs.Core.Dubbing;
using Xunit;

namespace CouchSubs.Shell.Tests;

public sealed class SherpaSpeakerTests
{
    [Fact]
    public async Task BundledSpeakerModelInitializesWithoutGameAudio()
    {
        var modelPath = Environment.GetEnvironmentVariable("COUCHSUBS_SPEAKER_SMOKE_MODEL");
        if (string.IsNullOrWhiteSpace(modelPath)) return;
        Assert.True(File.Exists(modelPath), "Pinned speaker model is missing.");

        // Build #147: real Windows Event 1026 recorded an unrecoverable
        // native access violation in SpeakerEmbeddingExtractor.get_Dim().
        // Never invoke that native API during CI or inside CouchSubs.exe.
        // Package integrity was already verified via pinned SHA256 by CI.
        Assert.False(NativeSpeakerSafetyPolicy.InProcessInferenceAllowed);
        var engine = new SherpaSpeakerService(modelPath);
        engine.Start();
        await Task.Yield();
        Assert.False(engine.ModelReady);
        Assert.Null(engine.Latest.Name);
        engine.Push(new short[1920], 2, 48000, speechConfirmed: true);
        Assert.Null(engine.Latest.Name);
        engine.Stop();
    }
}
