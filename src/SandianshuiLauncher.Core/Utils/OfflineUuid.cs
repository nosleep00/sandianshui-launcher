using System.Security.Cryptography;
using System.Text;

namespace SandianshuiLauncher.Core.Utils;

/// <summary>
/// 生成 Minecraft 离线模式 UUID。
/// 等价于 Java 的 UUID.nameUUIDFromBytes(("OfflinePlayer:" + name).getBytes(UTF_8))，
/// 即对字符串做 MD5 后设置 version=3 与 IETF variant 位。
/// </summary>
public static class OfflineUuid
{
    /// <summary>
    /// 返回 Java UUID 字符串格式（8-4-4-4-12，小写）。
    /// </summary>
    public static string Generate(string username)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);

        byte[] data = MD5.HashData(Encoding.UTF8.GetBytes("OfflinePlayer:" + username));

        // 设置 version 与 variant 位（与 Java nameUUIDFromBytes 相同）。
        data[6] = (byte)((data[6] & 0x0F) | 0x30);
        data[8] = (byte)((data[8] & 0x3F) | 0x80);

        // Java UUID 字符串 = 16 字节按原始顺序的十六进制，不能直接用 new Guid(bytes)，
        // 因为 C# Guid 构造函数把前 8 字节按小端解释。
        return string.Create(36, data, (chars, bytes) =>
        {
            int[] dashPositions = [8, 13, 18, 23];
            int byteIndex = 0;
            for (int i = 0; i < chars.Length; i++)
            {
                if (Array.IndexOf(dashPositions, i) >= 0)
                {
                    chars[i] = '-';
                }
                else
                {
                    byte b = bytes[byteIndex++];
                    chars[i] = HexChar(b >> 4);
                    chars[++i] = HexChar(b & 0xF);
                }
            }
        });
    }

    /// <summary>返回无连字符的 32 位 UUID（部分启动参数使用）。</summary>
    public static string GenerateDigits(string username) =>
        Generate(username).Replace("-", string.Empty);

    private static char HexChar(int value) =>
        value < 10 ? (char)('0' + value) : (char)('a' + value - 10);
}
