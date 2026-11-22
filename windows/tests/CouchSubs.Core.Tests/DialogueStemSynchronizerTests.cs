// SPDX-License-Identifier: AGPL-3.0-only
using CouchSubs.Core.Dubbing;

namespace CouchSubs.Core.Tests;

public sealed class DialogueStemSynchronizerTests
{
    private static float[] Original(int sign = 1)
    {
        var data = new float[DialogueStemSynchronizer.SamplesPerFrame];
        for (var i = 0; i < data.Length; i += 2)
        {
            data[i] = .3f * sign;
            data[i + 1] = .1f * sign;
        }
        return data;
    }

    [Fact]
    public void MissingStemReturnsExactOriginalAfterDeadline()
    {
        var join = new DialogueStemSynchronizer();
        var original = Original();
        Assert.Empty(join.OfferOriginal(1, original, 1000));
        Assert.Empty(join.Drain(1089));
        var outFrames = join.Drain(1091);
        var result = Assert.Single(outFrames);
        Assert.False(result.HasTrustedStem);
        Assert.Equal(original, result.Original);
        Assert.Equal(1, join.FallbackFrames);
    }

    [Fact]
    public void MatchedVerifiedMonoStemRetainsStereoFrameIdentity()
    {
        var join = new DialogueStemSynchronizer();
        var original = Original();
        join.OfferOriginal(10, original, 1000);
        Assert.True(join.OfferStem(10, Enumerable.Repeat(.1f, 960).ToArray(),
            .95f, 1040, true));
        var result = Assert.Single(join.Drain(1040));
        Assert.True(result.HasTrustedStem);
        Assert.True(result.TurkishDubActive);
        Assert.Equal(40, result.AgeMilliseconds);
        Assert.Equal(original, result.Original);
        Assert.Equal(.1f, result.DialogueStem![0]);
        Assert.Equal(.1f, result.DialogueStem[1]);
    }

    [Fact]
    public void OutOfOrderResultCannotReorderPlayedGameAudio()
    {
        var join = new DialogueStemSynchronizer();
        join.OfferOriginal(1, Original(), 1000);
        join.OfferOriginal(2, Original(-1), 1020);
        Assert.True(join.OfferStem(2, Enumerable.Repeat(.1f, 960).ToArray(),
            .95f, 1040, true));
        Assert.Empty(join.Drain(1050)); // id 1 missing; no id2 before id1
        var ready = join.Drain(1091);
        Assert.Equal(2, ready.Count);
        Assert.Equal([1L, 2L], ready.Select(f => f.FrameId).ToArray());
        Assert.False(ready[0].HasTrustedStem);
        Assert.True(ready[1].HasTrustedStem);
    }

    [Fact]
    public void InvalidOrUntrustedStemNeverChangesOriginal()
    {
        var join = new DialogueStemSynchronizer();
        join.OfferOriginal(1, Original(), 1000);
        Assert.False(join.OfferStem(1, Enumerable.Repeat(.1f, 960).ToArray(),
            .5f, 1010, true));
        Assert.False(join.OfferStem(1, Enumerable.Repeat(.1f, 960).ToArray(),
            1.01f, 1010, true));
        Assert.False(join.OfferStem(1, Enumerable.Repeat(float.NaN, 960).ToArray(),
            .99f, 1020, true));
        Assert.False(join.OfferStem(1, Enumerable.Repeat(.1f, 960).ToArray(),
            .99f, 1100, true));
        Assert.False(Assert.Single(join.Drain(1100)).HasTrustedStem);
    }

    [Fact]
    public void QueueNeverGrowsUnboundedAndNoAudioIsSilentlyDiscarded()
    {
        var join = new DialogueStemSynchronizer();
        var outputs = new List<JoinedDialogueFrame>();
        for (var i = 1; i <= 100; i++)
        {
            outputs.AddRange(join.OfferOriginal(i, Original(), i * 20L));
            Assert.InRange(join.BufferedFrames, 0, DialogueStemSynchronizer.MaxPendingFrames);
        }
        outputs.AddRange(join.FlushOriginals());
        Assert.Equal(100, outputs.Count);
        Assert.Equal(Enumerable.Range(1, 100).Select(i => (long)i),
            outputs.Select(f => f.FrameId));
        Assert.All(outputs, f => Assert.False(f.HasTrustedStem));
        Assert.Equal(100, join.FallbackFrames);
    }

    [Fact]
    public void ResetDiscardedOldResultsCannotAffectNewSession()
    {
        var join = new DialogueStemSynchronizer();
        join.OfferOriginal(1, Original(), 0);
        join.Reset();
        Assert.False(join.OfferStem(1, Enumerable.Repeat(.1f, 960).ToArray(),
            .99f, 10, true));
        Assert.Empty(join.Drain(100));
        join.OfferOriginal(1, Original(), 1000);
        Assert.Equal(0, join.FallbackFrames);
    }
}
