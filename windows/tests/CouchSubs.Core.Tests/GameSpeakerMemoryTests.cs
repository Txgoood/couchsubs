// SPDX-License-Identifier: AGPL-3.0-only
using CouchSubs.Core.Dubbing;

namespace CouchSubs.Core.Tests;

public sealed class GameSpeakerMemoryTests
{
    private static float[] Voice(int index)
    {
        var result = new float[32];
        result[index] = 1;
        return result;
    }
    private static SpeakerEvidence Sample(int i) => new(null, Voice(i), 0.95f, true, true);

    [Fact]
    public void PersistsAnonymousIdentityAcrossProcessRestartsAndSeparatesGames()
    {
        var folder = Path.Combine(Path.GetTempPath(), "couchsubs-memory-" + Guid.NewGuid().ToString("N"));
        try
        {
            var memory = new GameSpeakerMemory(folder);
            var first = new HybridSpeakerRegistry();
            first.Resolve(Sample(1));
            Assert.Equal("Ses 1", first.Resolve(Sample(1)).Name);
            Assert.True(memory.Save("GTA 6", first));
            var second = new HybridSpeakerRegistry();
            Assert.True(memory.Load("GTA 6", second));
            Assert.Equal("Ses 1", second.Resolve(Sample(1)).Name);
            var anotherGame = new HybridSpeakerRegistry();
            Assert.False(memory.Load("Other Game", anotherGame));
            Assert.Null(anotherGame.Resolve(Sample(1)).Name);
            memory.Delete("GTA 6");
            Assert.False(memory.Load("GTA 6", new HybridSpeakerRegistry()));
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [Fact]
    public void RenamedSpeakerKeepsSameEmbeddingAndCanBeForgotten()
    {
        var registry = new HybridSpeakerRegistry();
        registry.Resolve(Sample(2));
        registry.Resolve(Sample(2));
        Assert.True(registry.Rename("Ses 1", "Lucia"));
        Assert.Equal("Lucia", registry.Resolve(Sample(2)).Name);
        Assert.False(registry.Rename("Lucia", ""));
        Assert.True(registry.Forget("Lucia"));
        Assert.Null(registry.Resolve(Sample(2)).Name);
    }

    [Fact]
    public void RejectsUnsafeAndInvalidSavedVectorsWithoutChangingRegistry()
    {
        var registry = new HybridSpeakerRegistry();
        registry.Resolve(new("Jason", Voice(4), 0.95f, true, true));
        var invalid = new SpeakerMemorySnapshot(new() { ["bad"] = new float[] { float.NaN, 1 } }, 1);
        Assert.False(registry.Restore(invalid));
        Assert.Equal("Jason", registry.Resolve(Sample(4)).Name);
        Assert.False(registry.Restore(new SpeakerMemorySnapshot(new(), 0)));
    }
}
