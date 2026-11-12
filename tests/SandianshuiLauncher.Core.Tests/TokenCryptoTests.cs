using System.Text.Json;
using SandianshuiLauncher.Core.Models;
using SandianshuiLauncher.Core.Services;
using Xunit;

namespace SandianshuiLauncher.Core.Tests;

/// <summary>
/// <see cref="TokenCrypto"/> 与账号令牌加密存储测试：
/// 验证加解密往返、非法密文容错、旧明文配置自动迁移为密文、落盘文件不含明文。
/// </summary>
public class TokenCryptoTests : IDisposable
{
    private readonly string _root;

    public TokenCryptoTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "sdsl_token_" + Guid.NewGuid().ToString("N"));
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

    [Fact]
    public void Protect_Unprotect_RoundTrip()
    {
        string token = "eyJhbGciOiJIUzI1NiJ9.测试令牌 payload";

        string encrypted = TokenCrypto.Protect(token);
        string decrypted = TokenCrypto.Unprotect(encrypted);

        Assert.NotEqual(token, encrypted);
        Assert.Equal(token, decrypted);
    }

    [Fact]
    public void Protect_EmptyString_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, TokenCrypto.Protect(string.Empty));
    }

    [Fact]
    public void Unprotect_InvalidBase64_ReturnsInput()
    {
        // 旧版明文（如离线占位 token "0"）不是 base64，解密失败时原样返回。
        Assert.Equal("0", TokenCrypto.Unprotect("0"));
        Assert.Equal("not-base64!!", TokenCrypto.Unprotect("not-base64!!"));
    }

    [Fact]
    public void Account_SettingAccessToken_EncryptsForStorage()
    {
        var account = new GameAccount();

        account.AccessToken = "secret-token";

        Assert.Equal("secret-token", account.AccessToken);
        Assert.False(string.IsNullOrEmpty(account.EncryptedToken));
        Assert.NotEqual("secret-token", account.EncryptedToken);
    }

    [Fact]
    public void ConfigService_LoadLegacyPlaintextToken_MigratesToEncrypted()
    {
        // 旧版配置：access_token 明文落盘。
        string legacyJson = """
        {
          "config_version": 1,
          "game_directory": "C:\\mc",
          "accounts": [
            {
              "id": "a1",
              "type": 0,
              "username": "player",
              "uuid": "0123456789abcdef0123456789abcdef",
              "access_token": "0"
            }
          ],
          "selected_account_id": "a1"
        }
        """;
        string configPath = Path.Combine(_root, "launcher_config.json");
        File.WriteAllText(configPath, legacyJson);

        var service = new ConfigService(_root);
        GameAccount account = service.Config.Accounts.Single();

        // 迁移后：明文可用、密文已生成、旧字段清空。
        Assert.Equal("0", account.AccessToken);
        Assert.False(string.IsNullOrEmpty(account.EncryptedToken));
        Assert.Null(account.LegacyAccessToken);
    }

    [Fact]
    public void ConfigService_Save_DoesNotPersistPlaintextToken()
    {
        var service = new ConfigService(_root);
        service.Config.Accounts.Add(new GameAccount
        {
            Id = "a1",
            Type = AccountType.Offline,
            Username = "player",
            Uuid = "0123456789abcdef0123456789abcdef",
            AccessToken = "top-secret-value"
        });
        service.Save();

        string json = File.ReadAllText(Path.Combine(_root, "launcher_config.json"));
        var raw = JsonSerializer.Deserialize<JsonElement>(json);
        JsonElement accountElement = raw.GetProperty("accounts")[0];

        Assert.DoesNotContain("top-secret-value", json);
        Assert.True(accountElement.TryGetProperty("encrypted_token", out _));
        Assert.False(accountElement.TryGetProperty("access_token", out _));
    }
}
