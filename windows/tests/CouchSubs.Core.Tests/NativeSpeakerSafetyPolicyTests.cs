// SPDX-License-Identifier: AGPL-3.0-only
using CouchSubs.Core.Dubbing;
using Xunit;

namespace CouchSubs.Core.Tests;

public sealed class NativeSpeakerSafetyPolicyTests
{
    [Fact]
    public void NativeSherpaMustNotRunInsideMainWindowsProcess()
    {
        // Regression for Windows Event 1026 / CouchSubs v0.3.2 Build #146:
        // SherpaOnnx.SpeakerEmbeddingExtractor.get_Dim() raises an unrecoverable
        // native access violation. Do not re-enable in-process inference.
        // Require a separately supervised worker before this policy changes.
        Assert.False(NativeSpeakerSafetyPolicy.InProcessInferenceAllowed);
    }
}
