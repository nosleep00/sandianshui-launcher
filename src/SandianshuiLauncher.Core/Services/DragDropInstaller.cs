using SandianshuiLauncher.Core.Utils;

namespace SandianshuiLauncher.Core.Services;

/// <summary>拖入文件的类型。</summary>
public enum DroppedFileKind
{
    Unknown,
    ModJar,
    Modpack
}

/// <summary>
/// 拖拽安装：把本地拖入的模组 jar 复制到目标 mods 目录
/// （同名同内容跳过；同名不同内容自动加 (1) 后缀），整合包交给导入流程。
/// </summary>
public static class DragDropInstaller
{
    /// <summary>按扩展名分类拖入的文件。</summary>
    public static DroppedFileKind Classify(string path)
    {
        string extension = Path.GetExtension(path);
        if (extension.Equals(".jar", StringComparison.OrdinalIgnoreCase))
        {
            return DroppedFileKind.ModJar;
        }
        if (extension.Equals(".mrpack", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            return DroppedFileKind.Modpack;
        }
        return DroppedFileKind.Unknown;
    }

    /// <summary>
    /// 安装本地模组 jar 到目标目录，返回最终文件路径；
    /// <paramref name="note"/> 输出提示（已存在跳过等）。
    /// </summary>
    public static string InstallMod(string jarPath, string targetDirectory, out string note)
    {
        Directory.CreateDirectory(targetDirectory);
        string fileName = Path.GetFileName(jarPath);
        string destination = Path.Combine(targetDirectory, fileName);
        string sha1 = HashUtils.Sha1File(jarPath);

        if (File.Exists(destination))
        {
            if (HashUtils.VerifySha1(destination, sha1))
            {
                note = "目标目录已存在相同文件，已跳过。";
                return destination;
            }
            string baseName = Path.GetFileNameWithoutExtension(fileName);
            string extension = Path.GetExtension(fileName);
            int index = 1;
            while (File.Exists(destination))
            {
                destination = Path.Combine(targetDirectory, $"{baseName}({index}){extension}");
                index++;
            }
            note = $"已存在同名但内容不同的文件，已另存为 {Path.GetFileName(destination)}。";
        }
        else
        {
            note = string.Empty;
        }

        File.Copy(jarPath, destination, overwrite: false);
        return destination;
    }
}
