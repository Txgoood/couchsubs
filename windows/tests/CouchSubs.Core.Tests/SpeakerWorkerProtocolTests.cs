// SPDX-License-Identifier: AGPL-3.0-only
using System.Text;
using CouchSubs.Core.Dubbing;

namespace CouchSubs.Core.Tests;

public sealed class SpeakerWorkerProtocolTests
{
    [Fact]
    public void AcceptsOnlyCompleteVersionedHealthStatus()
    {
        Assert.True(SpeakerWorkerProtocol.TryReadStatus(
            Encoding.UTF8.GetBytes("""{"protocol":1,"ready":true,"modelLoaded":true}"""), out var healthy));
        Assert.True(healthy.Ready);
        Assert.True(SpeakerWorkerProtocol.TryReadStatus(
            Encoding.UTF8.GetBytes("""{"protocol":1,"ready":true,"modelLoaded":false}"""), out var unavailable));
        Assert.False(unavailable.Ready);
        Assert.False(SpeakerWorkerProtocol.TryReadStatus(
            Encoding.UTF8.GetBytes("""{"protocol":2,"ready":true,"modelLoaded":true}"""), out _));
        Assert.False(SpeakerWorkerProtocol.TryReadStatus(
            Encoding.UTF8.GetBytes("""{"protocol":1,"ready":true}"""), out _));
        Assert.False(SpeakerWorkerProtocol.TryReadStatus(
            Encoding.UTF8.GetBytes("""{"protocol":1,"ready":1,"modelLoaded":true}"""), out _));
    }

    [Fact]
    public void RejectsMalformedAndOversizedUntrustedMessages()
    {
        Assert.False(SpeakerWorkerProtocol.TryReadStatus([], out _));
        Assert.False(SpeakerWorkerProtocol.TryReadStatus(new byte[SpeakerWorkerProtocol.MaxMessageBytes + 1], out _));
        Assert.False(SpeakerWorkerProtocol.TryReadStatus(Encoding.UTF8.GetBytes("{"), out _));
    }

    [Fact]
    public void FailureCircuitOpensAndNeedsExplicitSessionReset()
    {
        var breaker = new SpeakerWorkerCircuitBreaker();
        breaker.ReportFailure();
        breaker.ReportFailure();
        Assert.True(breaker.CanSend);
        breaker.ReportFailure();
        Assert.True(breaker.IsOpen);
        breaker.ReportSuccess();
        Assert.False(breaker.CanSend);
        breaker.ResetForNewSession();
        Assert.True(breaker.CanSend);
    }

    [Fact]
    public void NativeInferenceRemainsDisabledInParentProcess()
        => Assert.False(NativeSpeakerSafetyPolicy.InProcessInferenceAllowed);
}
