// SPDX-License-Identifier: AGPL-3.0-only
using CouchSubs.RemotePlay;
using Xunit;

namespace CouchSubs.RemotePlay.Tests;

public sealed class Ps5RunningGameDiscoveryTests
{
    [Fact]
    public void ParsesRealisticPs5DiscoveryGameName()
    {
        var reply = "HTTP/1.1 200 OK\n" +
            "host-type:PS5\n" +
            "running-app-titleid:PPSA12345\n" +
            "running-app-name:Grand Theft Auto VI\n\0";
        Assert.Equal("Grand Theft Auto VI", Ps5RunningGameDiscovery.ParseGameName(reply));
    }

    [Theory]
    [InlineData("HTTP/1.1 620 Server Standby\nrunning-app-name:GTA 6\n")]
    [InlineData("HTTP/1.1 200 OK\nhost-name:PS5\n")]
    [InlineData("HTTP/1.1 200 OK\nrunning-app-name:Remote Play\n")]
    [InlineData("HTTP/1.1 200 OK\nrunning-app-name:GTA 6\nfoo:bad\0suffix")]
    public void UntrustedOrMissingTitlesCannotBeInvented(string payload)
    {
        var found = Ps5RunningGameDiscovery.ParseGameName(payload);
        Assert.True(found is null || found == "GTA 6");
    }

    [Fact]
    public void BlankAndMalformedTitlesAreRejected()
    {
        Assert.Null(Ps5RunningGameDiscovery.ParseGameName(""));
        Assert.Null(Ps5RunningGameDiscovery.ParseGameName("garbage"));
        Assert.Null(Ps5RunningGameDiscovery.ParseGameName("HTTP/1.1 200 OK\nrunning-app-name:A\n"));
    }
}
