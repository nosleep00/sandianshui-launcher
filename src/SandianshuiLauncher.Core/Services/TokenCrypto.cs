using System.Security.Cryptography;
using System.Text;

namespace SandianshuiLauncher.Core.Services;

/// <summary>
/// 访问令牌加密：使用 Windows DPAPI（当前用户作用域 + 固定熵）加密，
/// 密文以 base64 落盘，防止配置文件泄露账号访问令牌。
/// 同一 Windows 用户下可解密；跨用户/跨机器迁移后的密文无法解密，属预期行为。
/// </summary>
public static class TokenCrypto
{
    private static readonly byte[] Entropy = "SandianshuiLauncher.Token.v1"u8.ToArray();

    /// <summary>加密明文令牌（空串原样返回；非 Windows 平台不支持 DPAPI，明文返回）。</summary>
    public static string Protect(string plainText)
    {
        if (string.IsNullOrEmpty(plainText) || !OperatingSystem.IsWindows())
        {
            return plainText;
        }
        byte[] data = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(plainText), Entropy, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(data);
    }

    /// <summary>
    /// 解密密文；输入为空、无法解密（损坏 / 旧版明文 / 跨机器迁移）或非 Windows 平台时
    /// 原样返回，由上层按明文继续处理（例如旧版占位 token "0"）。
    /// </summary>
    public static string Unprotect(string protectedText)
    {
        if (string.IsNullOrEmpty(protectedText) || !OperatingSystem.IsWindows())
        {
            return protectedText;
        }
        try
        {
            byte[] data = Convert.FromBase64String(protectedText);
            return Encoding.UTF8.GetString(
                ProtectedData.Unprotect(data, Entropy, DataProtectionScope.CurrentUser));
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            return protectedText;
        }
    }
}
