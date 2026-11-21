// SPDX-License-Identifier: AGPL-3.0-only
using CouchSubs.Translation;

namespace CouchSubs.Translation.Tests;

public sealed class OptionalGpuSessionFactoryTests
{
    [Theory]
    [InlineData("1", true)]
    [InlineData("true", false)]
    [InlineData(null, false)]
    [InlineData("0", false)]
    public void DirectMlOnlyEnablesWithExplicitOptIn(string? flag, bool expected) =>
        Assert.Equal(expected, OptionalGpuSessionFactory.IsOptedIn(flag));

    [Theory]
    [InlineData("0", 0)]
    [InlineData("3", 3)]
    [InlineData("-1", 0)]
    [InlineData("200", 0)]
    [InlineData("invalid", 0)]
    public void GpuDeviceIndexIsBounded(string? text, int expected) =>
        Assert.Equal(expected, OptionalGpuSessionFactory.ParseDeviceId(text));

    [Fact]
    public void MissingModelNeverClaimsGpuActive()
    {
        var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".onnx");
        Assert.Throws<FileNotFoundException>(() =>
            OptionalGpuSessionFactory.Open(missing, true));
    }
}
