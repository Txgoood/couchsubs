// SPDX-License-Identifier: AGPL-3.0-only
using CouchSubs.Core.Dubbing;

namespace CouchSubs.Core.Tests;

public sealed class DialogueInferenceAdmissionGateTests
{
    [Fact]
    public void FastTestFramesDoNotCountUntilSustainedWindow()
    {
        var governor = new DialogueInferenceAdmissionGate();
        for (var i = 0; i < 63; i++) governor.Observe(5, true);
        Assert.False(governor.Eligible);
        governor.Observe(5, true);
        Assert.True(governor.Eligible);
    }

    [Fact]
    public void FastCudaIsNotEnoughWhenAlgorithmicAgeBudgetExhausted()
    {
        var gate = new DialogueInferenceAdmissionGate(63, 15, 8);
        for (var i = 0; i < 100; i++) gate.Observe(10, true);
        Assert.False(gate.Eligible);
    }

    [Fact]
    public void ModelSlowerThanTwentyMillisecondFrameMustStayOff()
    {
        var gate = new DialogueInferenceAdmissionGate();
        for (var i = 0; i < 100; i++) gate.Observe(28, true);
        Assert.False(gate.Eligible);
        Assert.Contains("unsafe", gate.Status);
    }

    [Fact]
    public void FaultForcesImmediateOriginalAudioFallbackAndResetsWindow()
    {
        var gate = new DialogueInferenceAdmissionGate();
        for (var i = 0; i < 100; i++) gate.Observe(5, true);
        Assert.True(gate.Eligible);
        gate.Observe(double.NaN, false);
        Assert.False(gate.Eligible);
        Assert.Equal(1, gate.Faults);
        Assert.Equal(0, gate.MeasuredFrames);
    }

    [Fact]
    public void LongSessionsHaveBoundedObservationMemory()
    {
        var gate = new DialogueInferenceAdmissionGate();
        for (var i = 0; i < 100000; i++) gate.Observe(7, true);
        Assert.Equal(128, gate.MeasuredFrames);
        Assert.True(gate.Eligible);
    }
}
