using SandianshuiLauncher.Core.Services;
using Xunit;

namespace SandianshuiLauncher.Core.Tests;

/// <summary>
/// <see cref="GamePaths"/> 测试：验证 mods / resourcepacks / shaderpacks 目录的隔离感知计算。
/// </summary>
public class GamePathsTests
{
    private const string GameDir = @"E:\MC";

    [Fact]
    public void ResourcePacks_NoIsolation_PointsToGameRoot()
    {
        Assert.Equal(
            @"E:\MC\resourcepacks",
            GamePaths.GetResourcePacksDirectory(GameDir, "1.20.4", versionIsolation: false));
    }

    [Fact]
    public void ShaderPacks_NoIsolation_PointsToGameRoot()
    {
        Assert.Equal(
            @"E:\MC\shaderpacks",
            GamePaths.GetShaderPacksDirectory(GameDir, "1.20.4", versionIsolation: false));
    }

    [Fact]
    public void ResourcePacks_WithIsolation_PointsToVersionDir()
    {
        Assert.Equal(
            @"E:\MC\versions\1.20.4\resourcepacks",
            GamePaths.GetResourcePacksDirectory(GameDir, "1.20.4", versionIsolation: true));
    }

    [Fact]
    public void Mods_WithIsolation_PointsToVersionDir()
    {
        Assert.Equal(
            @"E:\MC\versions\1.20.4\mods",
            GamePaths.GetModsDirectory(GameDir, "1.20.4", versionIsolation: true));
    }
}
