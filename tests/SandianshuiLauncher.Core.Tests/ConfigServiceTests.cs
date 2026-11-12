using System.IO;
using SandianshuiLauncher.Core.Services;
using Xunit;

namespace SandianshuiLauncher.Core.Tests;

public class ConfigServiceTests
{
    [Fact]
    public void GetDefaultGameDirectory_IsRootedAbsolute()
    {
        string dir = ConfigService.GetDefaultGameDirectory();

        Assert.False(string.IsNullOrWhiteSpace(dir));
        Assert.True(Path.IsPathRooted(dir));
    }

    [Fact]
    public void GetDefaultGameDirectory_IsAppDataMinecraft()
    {
        string expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            ".minecraft");

        Assert.Equal(expected, ConfigService.GetDefaultGameDirectory());
    }

    [Fact]
    public void CreateDefault_UsesGetDefaultGameDirectory()
    {
        var config = ConfigService.CreateDefault();

        Assert.Equal(ConfigService.GetDefaultGameDirectory(), config.GameDirectory);
    }

    [Fact]
    public void CreateDefault_UsesBmclapiSource()
    {
        var config = ConfigService.CreateDefault();

        Assert.Equal("bmclapi", config.DownloadSourceId);
    }

    [Fact]
    public void Load_FillsEmptyGameDirectoryWithDefault()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "sds_cfg_test_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempDir);
            File.WriteAllText(
                Path.Combine(tempDir, "launcher_config.json"),
                "{\"game_directory\":\"\",\"download_source_id\":\"guangsu\"}");

            var service = new ConfigService(tempDir);

            Assert.Equal(ConfigService.GetDefaultGameDirectory(), service.Config.GameDirectory);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }
}
