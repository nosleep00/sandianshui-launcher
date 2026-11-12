using SandianshuiLauncher.Core.Services;
using Xunit;

namespace SandianshuiLauncher.Core.Tests;

/// <summary>
/// <see cref="DragDropInstaller"/> 测试：文件分类、安装复制、重名冲突处理。
/// </summary>
public class DragDropInstallerTests : IDisposable
{
    private readonly string _root;

    public DragDropInstallerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "sdsl_drop_" + Guid.NewGuid().ToString("N"));
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

    [Theory]
    [InlineData("a.jar", DroppedFileKind.ModJar)]
    [InlineData("A.JAR", DroppedFileKind.ModJar)]
    [InlineData("pack.mrpack", DroppedFileKind.Modpack)]
    [InlineData("pack.zip", DroppedFileKind.Modpack)]
    [InlineData("note.txt", DroppedFileKind.Unknown)]
    [InlineData("photo.png", DroppedFileKind.Unknown)]
    public void Classify_ByExtension(string path, DroppedFileKind expected)
    {
        Assert.Equal(expected, DragDropInstaller.Classify(path));
    }

    [Fact]
    public void InstallMod_CopiesFileToTarget()
    {
        string jarPath = Path.Combine(_root, "sodium.jar");
        File.WriteAllText(jarPath, "content");
        string modsDir = Path.Combine(_root, "mods");

        string destination = DragDropInstaller.InstallMod(jarPath, modsDir, out string note);

        Assert.True(File.Exists(Path.Combine(modsDir, "sodium.jar")));
        Assert.Empty(note);
    }

    [Fact]
    public void InstallMod_SameContent_AlreadyExists_SkipsWithNote()
    {
        string jarPath = Path.Combine(_root, "sodium.jar");
        File.WriteAllText(jarPath, "content");
        string modsDir = Path.Combine(_root, "mods");
        Directory.CreateDirectory(modsDir);
        File.WriteAllText(Path.Combine(modsDir, "sodium.jar"), "content");

        DragDropInstaller.InstallMod(jarPath, modsDir, out string note);

        Assert.Contains("已跳过", note);
        Assert.Single(Directory.EnumerateFiles(modsDir));
    }

    [Fact]
    public void InstallMod_DifferentContent_Conflict_GetsSuffix()
    {
        string jarPath = Path.Combine(_root, "sodium.jar");
        File.WriteAllText(jarPath, "new content");
        string modsDir = Path.Combine(_root, "mods");
        Directory.CreateDirectory(modsDir);
        File.WriteAllText(Path.Combine(modsDir, "sodium.jar"), "old content");

        string destination = DragDropInstaller.InstallMod(jarPath, modsDir, out string note);

        Assert.True(File.Exists(Path.Combine(modsDir, "sodium(1).jar")));
        Assert.Equal("sodium(1).jar", Path.GetFileName(destination));
        Assert.Contains("另存为", note);
    }
}
