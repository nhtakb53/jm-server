using JmServer.GameIntegration;

namespace JmServer.GameIntegration.Tests;

public sealed class D2RLoaderVerifierTests
{
    [Theory]
    [InlineData("D2RLoader.exe", true)]
    [InlineData("d2rloader/config/d2rloader.toml", true)]
    [InlineData("../D2R.exe", false)]
    [InlineData("d2rloader/../../D2R.exe", false)]
    [InlineData("C:/Windows/System32/file.dll", false)]
    [InlineData("", false)]
    public void IsSafeRelativePath_RejectsArchiveTraversal(string path, bool expected)
    {
        Assert.Equal(expected, D2RLoaderVerifier.IsSafeRelativePath(path));
    }

    [Fact]
    public async Task VerifyReleaseArchive_ReturnsFailureForMissingFile()
    {
        var result = await D2RLoaderVerifier.VerifyReleaseArchiveAsync(
            Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.zip"));

        Assert.False(result.IsValid);
    }
}
