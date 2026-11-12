// SPDX-License-Identifier: AGPL-3.0-only

using CouchSubs.Core.Dubbing;
using Xunit;

namespace CouchSubs.Core.Tests;

public class GameSpeechMixerTests
{
    [Fact]
    public void GameRemainsFullVolumeWhenNoTurkishSpeech()
    {
        float[] output = new float[3];
        GameSpeechMixer.Mix(output, [0.2f, -0.5f, 0.7f], []);
        Assert.Equal([0.2f, -0.5f, 0.7f], output);
    }

    [Fact]
    public void TurkishVoiceDucksGameAudioOnlyDuringSpeech()
    {
        float[] output = new float[3];
        GameSpeechMixer.Mix(output, [0.8f, 0.8f, 0.8f], [0f, 0.5f, 0f], ducking: 0.25f);
        Assert.Equal(0.8f, output[0], 4);
        Assert.Equal(0.7f, output[1], 4);
        Assert.Equal(0.8f, output[2], 4);
    }

    [Fact]
    public void MixClampsToAvoidOverflow()
    {
        float[] output = new float[2];
        GameSpeechMixer.Mix(output, [1f, -1f], [1f, -1f], ducking: 1f);
        Assert.Equal(1f, output[0]);
        Assert.Equal(-1f, output[1]);
    }

    [Fact]
    public void InvalidVolumeIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            GameSpeechMixer.Mix(new float[1], [0.5f], [0.5f], gameVolume: 2f));
    }
}
