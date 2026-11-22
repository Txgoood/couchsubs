// SPDX-License-Identifier: AGPL-3.0-only
using CouchSubs.Core.Dubbing;
using CouchSubs.Shell;

namespace CouchSubs.Shell.Tests;

public sealed class InworldRealtimeWorkerClientTests
{
    [Fact]
    public async Task DefaultStateCannotSilentlyEnableModelOrLaunchPython()
    {
        await using var client = new InworldRealtimeWorkerClient((_, _) => { });
        Assert.False(client.Ready);
        Assert.False(client.TryQueue(0,
            new float[InworldStemWireProtocol.FrameSamplesStereo]));
        Assert.Null(client.Failure);
    }

    [Fact]
    public async Task MissingWorkerScriptNeverStartsBackgroundProcess()
    {
        await using var client = new InworldRealtimeWorkerClient((_, _) => { });
        var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".py");
        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            client.StartAsync("python", missing));
        Assert.False(client.Ready);
    }

    [Fact]
    public async Task WorkerNeverAcceptsInvalidDevice()
    {
        await using var client = new InworldRealtimeWorkerClient((_, _) => { });
        var file = Path.GetTempFileName();
        try
        {
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
                client.StartAsync("python", file, "auto"));
        }
        finally { File.Delete(file); }
    }
}
