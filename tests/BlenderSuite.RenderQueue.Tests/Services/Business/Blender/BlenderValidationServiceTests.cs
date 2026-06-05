using System;
using System.Threading;
using System.Threading.Tasks;
using BlenderSuite.RenderQueue.Services.Business.Blender;
using Xunit;

namespace BlenderSuite.RenderQueue.Tests.Services.Business.Blender;

public sealed class BlenderValidationServiceTests
{
    [Fact]
    public async Task ValidatePathAsync_EmptyPath_ReturnsEmptyPath()
    {
        var sut = new BlenderValidationService(new FakeBlenderCliInfoService());

        var result = await sut.ValidatePathAsync("", TestContext.Current.CancellationToken);

        Assert.Equal(BlenderValidationStatus.EmptyPath, result.Status);
        Assert.Equal("Blender路径为空", result.Message);
        Assert.False(result.IsCanceled);
    }

    [Fact]
    public async Task ValidatePathAsync_MissingFile_ReturnsFileNotFound()
    {
        var sut = new BlenderValidationService(new FakeBlenderCliInfoService());
        var missingPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"{Guid.NewGuid():N}.exe");

        var result = await sut.ValidatePathAsync(missingPath, TestContext.Current.CancellationToken);

        Assert.Equal(BlenderValidationStatus.FileNotFound, result.Status);
        Assert.Equal("指定的文件不存在", result.Message);
        Assert.False(result.IsCanceled);
    }

    [Fact]
    public async Task ValidateAsync_PreviousRequestCanceledByNewRequest_ReturnsCanceled()
    {
        var sut = new BlenderValidationService(new FakeBlenderCliInfoService());
        var existingPath = typeof(BlenderValidationServiceTests).Assembly.Location;
        var first = sut.BeginValidation(existingPath);

        _ = sut.BeginValidation(existingPath);
        var result = await sut.ValidateAsync(first, TestContext.Current.CancellationToken);

        Assert.Equal(BlenderValidationStatus.Canceled, result.Status);
        Assert.True(result.IsCanceled);
        Assert.False(result.IsCurrent);
    }

    [Fact]
    public async Task ValidateAsync_DifferentChannels_DoNotCancelEachOther()
    {
        var sut = new BlenderValidationService(new FakeBlenderCliInfoService());
        var existingPath = typeof(BlenderValidationServiceTests).Assembly.Location;
        var settingsRequest = sut.BeginValidation(existingPath, "settings");

        _ = sut.BeginValidation(existingPath, "main");
        var result = await sut.ValidateAsync(settingsRequest, TestContext.Current.CancellationToken);

        Assert.Equal(BlenderValidationStatus.Success, result.Status);
        Assert.True(result.IsCurrent);
        Assert.NotNull(result.VersionInfo);
    }

    private sealed class FakeBlenderCliInfoService : IBlenderCliInfoService
    {
        public async Task<BlenderVersionInfo> GetVersionInfoAsync(
            string blenderExePath,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(1, cancellationToken);
            return new BlenderVersionInfo
            {
                Product = "Blender",
                Version = "Test"
            };
        }
    }
}
