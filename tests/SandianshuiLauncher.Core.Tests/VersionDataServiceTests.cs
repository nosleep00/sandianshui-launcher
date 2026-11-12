using SandianshuiLauncher.Core.Services;
using Xunit;

namespace SandianshuiLauncher.Core.Tests;

/// <summary>
/// <see cref="VersionDataService"/> 测试：
/// 验证删除版本前对 saves/screenshots 等用户数据的识别与迁移保护。
/// </summary>
public class VersionDataServiceTests : IDisposable
{
    private readonly string _root;

    public VersionDataServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "sdsl_versiondata_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // 清理失败不影响测试结果。
        }
    }

    private string MakeVersionDir(string versionId, params (string RelativePath, bool IsDirectory, string? Content)[] entries)
    {
        string dir = Path.Combine(_root, "versions", versionId);
        Directory.CreateDirectory(dir);
        foreach (var (relativePath, isDirectory, content) in entries)
        {
            string path = Path.Combine(dir, relativePath);
            if (isDirectory)
            {
                Directory.CreateDirectory(path);
                if (content is not null)
                {
                    File.WriteAllText(Path.Combine(path, content), "x");
                }
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, content ?? string.Empty);
            }
        }
        return dir;
    }

    [Fact]
    public void GetUserDataEntries_IsolatedSavesAndScreenshots_AreDetected()
    {
        string versionDir = MakeVersionDir("1.20.4",
            ("saves", true, "world1"),
            ("screenshots", true, "shot.png"));

        List<string> entries = VersionDataService.GetUserDataEntries(versionDir);

        Assert.Equal(["saves", "screenshots"], entries);
    }

    [Fact]
    public void GetUserDataEntries_EmptySavesDirectory_IsIgnored()
    {
        string versionDir = MakeVersionDir("1.20.4",
            ("saves", true, null));

        List<string> entries = VersionDataService.GetUserDataEntries(versionDir);

        Assert.Empty(entries);
    }

    [Fact]
    public void GetUserDataEntries_OptionsAndServersFiles_AreDetected()
    {
        string versionDir = MakeVersionDir("1.20.4",
            ("options.txt", false, "key:value"),
            ("servers.dat", false, "binary"));

        List<string> entries = VersionDataService.GetUserDataEntries(versionDir);

        Assert.Equal(["options.txt", "servers.dat"], entries);
    }

    [Fact]
    public void GetUserDataEntries_ConfigDirectory_IsDetected()
    {
        string versionDir = MakeVersionDir("1.20.4",
            ("config", true, "mod.conf"));

        List<string> entries = VersionDataService.GetUserDataEntries(versionDir);

        Assert.Equal(["config"], entries);
    }

    [Fact]
    public void MigrateToGameRoot_ConfigDirectory_IsMigrated()
    {
        string versionDir = MakeVersionDir("1.20.4",
            ("config", true, "mod.conf"));
        string gameDir = Path.Combine(_root, ".minecraft");

        List<string> migrated = VersionDataService.MigrateToGameRoot(
            versionDir, gameDir, ["config"]);

        Assert.Equal(Path.Combine(gameDir, "config"), migrated[0]);
        Assert.True(File.Exists(Path.Combine(gameDir, "config", "mod.conf")));
        Assert.False(Directory.Exists(Path.Combine(versionDir, "config")));
    }

    [Fact]
    public void GetUserDataEntries_NoUserData_ReturnsEmpty()
    {
        string versionDir = MakeVersionDir("1.20.4",
            ("natives", true, null),
            ("1.20.4.jar", false, "jar"));

        List<string> entries = VersionDataService.GetUserDataEntries(versionDir);

        Assert.Empty(entries);
    }

    [Fact]
    public void MigrateToGameRoot_MovesEntriesAndRemovesSource()
    {
        string versionDir = MakeVersionDir("1.20.4",
            ("saves", true, "world1"),
            ("options.txt", false, "key:value"));
        string gameDir = Path.Combine(_root, ".minecraft");

        List<string> migrated = VersionDataService.MigrateToGameRoot(
            versionDir, gameDir, ["saves", "options.txt"]);

        Assert.Equal(2, migrated.Count);
        Assert.True(Directory.Exists(Path.Combine(gameDir, "saves")));
        Assert.True(File.Exists(Path.Combine(gameDir, "saves", "world1")));
        Assert.True(File.Exists(Path.Combine(gameDir, "options.txt")));
        Assert.False(Directory.Exists(Path.Combine(versionDir, "saves")));
        Assert.False(File.Exists(Path.Combine(versionDir, "options.txt")));
    }

    [Fact]
    public void MigrateToGameRoot_NameConflict_GetsBackupSuffix()
    {
        string versionDir = MakeVersionDir("1.20.4",
            ("saves", true, "world1"));
        string gameDir = Path.Combine(_root, ".minecraft");
        Directory.CreateDirectory(Path.Combine(gameDir, "saves"));

        List<string> migrated = VersionDataService.MigrateToGameRoot(
            versionDir, gameDir, ["saves"]);

        Assert.Single(migrated);
        Assert.Equal(Path.Combine(gameDir, "saves(备份-1)"), migrated[0]);
        Assert.True(Directory.Exists(migrated[0]));
        // 原目录保持不变。
        Assert.True(Directory.Exists(Path.Combine(gameDir, "saves")));
    }

    [Fact]
    public void MigrateToGameRoot_MissingEntry_ThrowsAndLeavesVersionDir()
    {
        string versionDir = MakeVersionDir("1.20.4",
            ("options.txt", false, "key:value"));
        string gameDir = Path.Combine(_root, ".minecraft");

        Assert.ThrowsAny<Exception>(() =>
            VersionDataService.MigrateToGameRoot(versionDir, gameDir, ["options.txt", "saves"]));

        // 迁移失败后版本目录与已迁移条目仍在，调用方（UI）应中止删除。
        Assert.True(File.Exists(Path.Combine(versionDir, "options.txt"))
                    || File.Exists(Path.Combine(gameDir, "options.txt")));
    }
}
