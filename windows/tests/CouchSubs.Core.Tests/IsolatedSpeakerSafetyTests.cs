// SPDX-License-Identifier: AGPL-3.0-only
using System.Text;
using CouchSubs.Core.Dubbing;

namespace CouchSubs.Core.Tests;

public sealed class IsolatedSpeakerSafetyTests
{
    [Fact]
    public void WorkerStatusCannotClaimReadyWithoutModel()
    {
        Assert.True(SpeakerWorkerProtocol.TryReadStatus(
            Encoding.UTF8.GetBytes("""{"protocol":1,"ready":true,"modelLoaded":false}"""), out var result));
        Assert.False(result.Ready);
        Assert.False(NativeSpeakerSafetyPolicy.InProcessInferenceAllowed);
    }

    [Fact]
    public void StatusRejectsExtraLongPayloadAndUnsupportedProtocol()
    {
        Assert.False(SpeakerWorkerProtocol.TryReadStatus(
            new byte[SpeakerWorkerProtocol.MaxMessageBytes + 1], out _));
        Assert.False(SpeakerWorkerProtocol.TryReadStatus(
            Encoding.UTF8.GetBytes("""{"protocol":0,"ready":true,"modelLoaded":true}"""), out _));
    }
}
