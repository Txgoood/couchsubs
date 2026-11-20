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
        var engine = new SherpaSpeakerService(modelPath);
        engine.Start();
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (!engine.ModelReady && DateTime.UtcNow < deadline)
                await Task.Delay(50);
            Assert.True(engine.ModelReady, "Sherpa ONNX speaker extractor did not initialize.");
            Assert.Null(engine.Latest.Name);
            var silence = new short[1920];
            for (int i = 0; i < 20; i++)
                engine.Push(silence, 2, 48000, speechConfirmed: false);
            Assert.Null(engine.Latest.Name);
        }
        finally { engine.Stop(); }
    }
}
