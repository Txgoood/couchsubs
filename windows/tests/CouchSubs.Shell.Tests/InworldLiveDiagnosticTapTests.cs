// SPDX-License-Identifier: AGPL-3.0-only
using CouchSubs.Shell;

namespace CouchSubs.Shell.Tests;

public sealed class InworldLiveDiagnosticTapTests
{
    [Fact]
    public async Task DiagnosticTapIsOffByDefaultAndCannotInterfereWithSound()
    {
        await using var tap = new InworldLiveDiagnosticTap();
        Assert.False(tap.Ready);
        Assert.False(tap.TryCapture(new short[1920], 2, 48000));
        Assert.Equal(0, tap.ReceivedStems);
        Assert.Equal(0, tap.DroppedCaptureChunks);
        Assert.Null(tap.Failure);
    }

    [Fact]
    public async Task DiagnosticTapIsSafeToDisposeWithoutInstalledModel()
    {
        var tap = new InworldLiveDiagnosticTap();
        await tap.DisposeAsync();
        Assert.False(tap.TryCapture(new short[1920], 2, 48000));
        Assert.False(tap.Ready);
    }
}
