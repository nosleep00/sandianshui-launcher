using System.IO.Compression;
using SandianshuiLauncher.Core.Models;
using SandianshuiLauncher.Core.Services;

namespace SandianshuiLauncher.App.Tests;

/// <summary>
/// 拖拽安装路由测试：jar 装到当前版本、整合包走导入并跳转模组页、未知类型提示。
/// </summary>
public class MainViewModelDroppedFileTests : IDisposable
{
    private readonly TestServices _services = new();

    public void Dispose() => _services.Dispose();

    private string ModsDir => Path.Combine(_services.Config.Config.GameDirectory, "mods");

    [Fact]
    public async Task DropModJar_InstallsToSelectedVersionMods()
    {
        _services.Mods.SelectedVersion = new LocalVersion { Id = "1.20.4" };
        string jarPath = Path.Combine(Path.GetTempPath(), "dropped-mod.jar");
        await File.WriteAllTextAsync(jarPath, "jar-content");

        await _services.Main.HandleDroppedFileAsync(jarPath);

        Assert.True(File.Exists(Path.Combine(ModsDir, "dropped-mod.jar")));
        Assert.Contains(_services.Dialogs.Infos, m => m.Contains("已安装模组：dropped-mod.jar"));
    }

    [Fact]
    public async Task DropUnknownFile_Warns()
    {
        string txtPath = Path.Combine(Path.GetTempPath(), "dropped-note.txt");
        await File.WriteAllTextAsync(txtPath, "text");

        await _services.Main.HandleDroppedFileAsync(txtPath);

        Assert.Contains(_services.Dialogs.Warnings, w => w.Contains("不支持的文件类型"));
    }

    [Fact]
    public async Task DropModpack_ImportsAndNavigatesToMods()
    {
        _services.Mods.SelectedVersion = new LocalVersion { Id = "1.20.4" };
        _services.Dialogs.ConfirmResult = true;

        // 构造 SDLauncherPack 整合包。
        string packPath = Path.Combine(Path.GetTempPath(), "dropped-pack.zip");
        using (var fileStream = new FileStream(packPath, FileMode.Create))
        using (var zip = new ZipArchive(fileStream, ZipArchiveMode.Create))
        {
            ZipArchiveEntry manifest = zip.CreateEntry("sdlauncher-pack.json");
            using (var writer = new StreamWriter(manifest.Open()))
            {
                writer.Write("""{ "name": "dropped", "mods": [ { "file_name": "x.jar", "enabled": true } ] }""");
            }
            ZipArchiveEntry mod = zip.CreateEntry("mods/x.jar");
            using var stream = mod.Open();
            stream.Write("mod"u8);
        }

        await _services.Main.HandleDroppedFileAsync(packPath);

        Assert.True(File.Exists(Path.Combine(ModsDir, "x.jar")));
        Assert.Same(_services.Mods, _services.Main.CurrentPage);
    }

    [Fact]
    public async Task DropMrpack_CreatesNewInstanceInsteadOfPollutingSelectedVersion()
    {
        // 用户当前选中的是 Fabric 1.20.4 实例。
        _services.Mods.SelectedVersion = new LocalVersion { Id = "fabric-loader-0.20.1-1.20.4" };
        _services.Dialogs.ConfirmResult = true;

        // 构造 Modrinth 整合包（要求 Forge 1.20.1 47.4.12），原版已本地就绪。
        string gameDir = _services.Config.Config.GameDirectory;
        string vanillaDir = Path.Combine(gameDir, "versions", "1.20.1");
        Directory.CreateDirectory(vanillaDir);
        await File.WriteAllTextAsync(Path.Combine(vanillaDir, "1.20.1.json"),
            """{ "id": "1.20.1", "mainClass": "net.minecraft.client.main.Main", "libraries": [] }""");

        string packPath = Path.Combine(Path.GetTempPath(), "dropped-" + Guid.NewGuid().ToString("N") + ".mrpack");
        try
        {
            using (var fileStream = new FileStream(packPath, FileMode.Create))
            using (var zip = new ZipArchive(fileStream, ZipArchiveMode.Create))
            {
                ZipArchiveEntry index = zip.CreateEntry("modrinth.index.json");
                using (var writer = new StreamWriter(index.Open()))
                {
                    writer.Write(
                        "{\"formatVersion\":1,\"game\":\"minecraft\",\"name\":\"DroppedPack\"," +
                        "\"dependencies\":{\"minecraft\":\"1.20.1\",\"forge\":\"47.4.12\"},\"files\":[]}");
                }
            }

            // Forge 安装会因缺少网络/安装器失败，但关键点是：不得向当前选中版本写入任何 mods。
            await _services.Main.HandleDroppedFileAsync(packPath);

            string selectedModsDir = Path.Combine(gameDir, "versions", "fabric-loader-0.20.1-1.20.4", "mods");
            Assert.False(Directory.Exists(selectedModsDir)
                         && Directory.EnumerateFiles(selectedModsDir).Any());
            // 也不会把 mods 灌进游戏根目录。
            Assert.False(Directory.Exists(Path.Combine(gameDir, "mods"))
                         && Directory.EnumerateFiles(Path.Combine(gameDir, "mods")).Any());
        }
        finally
        {
            try
            {
                File.Delete(packPath);
            }
            catch
            {
                // 清理失败不影响测试结果。
            }
        }
    }
}
