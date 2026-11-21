// SPDX-License-Identifier: AGPL-3.0-only
using CouchSubs.Translation;

namespace CouchSubs.Translation.Tests;

public sealed class OnnxDialogueStemAdapterTests
{
    [Fact]
    public void MissingModelMustNotEnableUnsafeSeparation()
    {
        var path = Path.Combine(Path.GetTempPath(), "not-a-model-" + Guid.NewGuid() + ".onnx");
        Assert.Throws<FileNotFoundException>(() => new OnnxDialogueStemAdapter(path));
    }

    [Fact]
    public void ModelContractUsesBoundedStereoFrames()
    {
        Assert.Equal(48000, OnnxDialogueStemAdapter.SampleRate);
        Assert.Equal(2, OnnxDialogueStemAdapter.Channels);
        Assert.Equal(480, OnnxDialogueStemAdapter.SamplesPerChannel);
        Assert.Equal("audio", OnnxDialogueStemAdapter.InputName);
        Assert.Equal("dialogue", OnnxDialogueStemAdapter.OutputName);
    }
}
