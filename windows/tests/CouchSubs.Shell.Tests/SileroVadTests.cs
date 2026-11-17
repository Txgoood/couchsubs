// SPDX-License-Identifier: AGPL-3.0-only
using CouchSubs.App.Services;
using CouchSubs.Core.Dubbing;
using CouchSubs.Shell;
using Xunit;

namespace CouchSubs.Shell.Tests;

public sealed class SileroVadTests
{
    [Fact]
    public void NeuralModeNeverInventsAngerFromSoundEffects()
    {
        var memory = new EmotionContinuity();
        Assert.Null(memory.Resolve(
            new SpokenLine("An explosion hit the car.", "Bir patlama arabaya çarptı."),
            DubbingEmotionMode.Neural, allowAcousticCarry: true));
    }

    [Fact]
    public void NeuralModeKeepsPriorEmotionOnlyWhenSpeechIsVerified()
    {
        var memory = new EmotionContinuity();
        Assert.Equal(SpeechEmotion.Sad, memory.Resolve(
            new SpokenLine("I'm sorry.", "Üzgünüm.", "Lucia"), DubbingEmotionMode.Neural));
        Assert.Null(memory.Resolve(
            new SpokenLine("Please!", "Lütfen!", "Lucia"),
            DubbingEmotionMode.Neural, allowAcousticCarry: false));
        Assert.Equal(SpeechEmotion.Sad, memory.Resolve(
            new SpokenLine("Please!", "Lütfen!", "Lucia"),
            DubbingEmotionMode.Neural, allowAcousticCarry: true));
    }

    [Fact]
    public async Task BundledOnnxModelLoadsAndRejectsSilentPcm()
    {
        var modelPath = Environment.GetEnvironmentVariable("COUCHSUBS_SILERO_SMOKE_MODEL");
        // Regular developer test runs may lack the optional model. CI smoke step sets it.
        if (string.IsNullOrWhiteSpace(modelPath)) return;
        Assert.True(File.Exists(modelPath), "Pinned Silero model must exist for the CI smoke test.");
        var service = new SileroVadService(modelPath);
        service.Start();
        try
        {
            var pcm = new short[1920]; // 20 ms 48 kHz stereo silence.
            // Offer some frames over time to let ONNX inference consume the bounded queue.
            for (int i = 0; i < 90; i++)
            {
                service.Push(pcm, 2, 48000);
                await Task.Delay(12);
            }
            var until = DateTime.UtcNow.AddSeconds(8);
            while (!service.Ready && DateTime.UtcNow < until)
                await Task.Delay(30);
            Assert.True(service.Ready, "ONNX inference failed or returned no speech probabilities.");
            Assert.False(service.RecentSpeech, "Silence must not be classified as speech.");
        }
        finally { service.Stop(); }
    }
}
