using System.Security.Cryptography;

namespace SandianshuiLauncher.Core.Utils;

/// <summary>哈希校验工具。</summary>
public static class HashUtils
{
    /// <summary>计算文件的 SHA1（小写十六进制）。</summary>
    public static string Sha1File(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA1.HashData(stream)).ToLowerInvariant();
    }

    /// <summary>
    /// 校验文件 SHA1；文件不存在或不匹配时返回 false。
    /// </summary>
    public static bool VerifySha1(string path, string? expectedSha1)
    {
        if (string.IsNullOrEmpty(expectedSha1))
        {
            return File.Exists(path);
        }
        if (!File.Exists(path))
        {
            return false;
        }
        return string.Equals(Sha1File(path), expectedSha1, StringComparison.OrdinalIgnoreCase);
    }
}
