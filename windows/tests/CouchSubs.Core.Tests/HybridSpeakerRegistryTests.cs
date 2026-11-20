// SPDX-License-Identifier: AGPL-3.0-only
using CouchSubs.Core.Dubbing;
using Xunit;

namespace CouchSubs.Core.Tests;

public sealed class HybridSpeakerRegistryTests
{
    private static float[] Vector(int index)
    {
        var values = new float[32];
        values[index] = 1;
        return values;
    }

    private static SpeakerEvidence Sound(float[]? embedding, float quality = 0.95f) =>
        new(null, embedding, quality, true, true);

    [Fact]
    public void SubtitleNameOverridesAudioAndLearnsItsVoice()
    {
        var registry = new HybridSpeakerRegistry();
        var named = registry.Resolve(new("Lucia", Vector(3), 0.95f, true, true));
        Assert.Equal("Lucia", named.Name);
        Assert.Equal(SpeakerIdentitySource.SubtitleName, named.Source);
        var next = registry.Resolve(Sound(Vector(3)));
        Assert.Equal("Lucia", next.Name);
        Assert.Equal(SpeakerIdentitySource.VoiceEmbedding, next.Source);
    }

    [Fact]
    public void AudioOnlyCreatesAnonymousIdAfterRepeatedModelEvidence()
    {
        var registry = new HybridSpeakerRegistry();
        Assert.Null(registry.Resolve(Sound(Vector(1))).Name);
        Assert.Equal("Ses 1", registry.Resolve(Sound(Vector(1))).Name);
        Assert.Equal("Ses 1", registry.Resolve(Sound(Vector(1))).Name);
        Assert.Null(registry.Resolve(Sound(Vector(6))).Name);
        Assert.Equal("Ses 2", registry.Resolve(Sound(Vector(6))).Name);
    }

    [Fact]
    public void UncertainSilenceAndSoundEffectsCannotAssignIdentity()
    {
        var registry = new HybridSpeakerRegistry();
        Assert.Null(registry.Resolve(new(null, Vector(2), 0.99f, false, true)).Name);
        Assert.Null(registry.Resolve(new(null, Vector(2), 0.99f, true, false)).Name);
        Assert.Null(registry.Resolve(Sound(Vector(2), 0.4f)).Name);
        Assert.Null(registry.Resolve(Sound(new float[32])).Name);
        Assert.Null(registry.Resolve(Sound(null)).Name);
    }

    [Fact]
    public void SimilarAmbiguousEmbeddingDoesNotInventNewCharacter()
    {
        var registry = new HybridSpeakerRegistry();
        registry.Resolve(new("Jason", Vector(4), 0.9f, true, true));
        var similar = Vector(4);
        similar[5] = 0.95f;
        Assert.Null(registry.Resolve(Sound(similar)).Name);
    }

    [Fact]
    public void GameChangeResetsRegistryWithoutCrossingSpeakerIds()
    {
        var registry = new HybridSpeakerRegistry();
        registry.Resolve(Sound(Vector(4)));
        Assert.Equal("Ses 1", registry.Resolve(Sound(Vector(4))).Name);
        registry.Reset();
        Assert.Null(registry.Resolve(Sound(Vector(4))).Name);
    }
}
