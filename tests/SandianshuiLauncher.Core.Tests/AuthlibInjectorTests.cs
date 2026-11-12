using System.Net;
using System.Text;
using SandianshuiLauncher.Core.Models;
using SandianshuiLauncher.Core.Services;
using Xunit;

namespace SandianshuiLauncher.Core.Tests;

/// <summary>
/// <see cref="AuthlibInjectorService"/> 测试：
/// 验证 Yggdrasil 认证成功/失败路径、authlib-injector 多源下载与本地缓存跳过。
/// </summary>
public class AuthlibInjectorTests : IDisposable
{
    private readonly string _root;

    public AuthlibInjectorTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "sdsl_authlib_" + Guid.NewGuid().ToString("N"));
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

    /// <summary>按 URL 路由返回响应的假 handler。</summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage> Responder { get; set; }
            = _ => new HttpResponseMessage(HttpStatusCode.NotFound);

        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(Responder(request));
        }
    }

    private static HttpResponseMessage Json(string content, HttpStatusCode status = HttpStatusCode.OK)
        => new(status) { Content = new StringContent(content, Encoding.UTF8, "application/json") };

    private AuthlibInjectorService CreateService(StubHandler handler)
        => new(new HttpClient(handler), new DownloadService(new HttpClient(handler)));

    [Fact]
    public async Task Authenticate_Success_ReturnsAccountWithNormalizedFields()
    {
        const string authResponse = """
        {
          "accessToken": "ygg-access-token",
          "clientToken": "client-token",
          "availableProfiles": [ { "id": "ABCDEF1234567890abcdef1234567890", "name": "SkinPlayer" } ],
          "selectedProfile": { "id": "ABCDEF1234567890abcdef1234567890", "name": "SkinPlayer" }
        }
        """;
        var handler = new StubHandler
        {
            Responder = request =>
            {
                Assert.Contains("/authserver/authenticate", request.RequestUri!.AbsolutePath);
                return Json(authResponse);
            }
        };
        var service = CreateService(handler);

        GameAccount account = await service.AuthenticateAsync(
            "https://littleskin.cn/api/yggdrasil/", "user@example.com", "password");

        Assert.Equal("SkinPlayer", account.Username);
        Assert.Equal("abcdef1234567890abcdef1234567890", account.Uuid);
        Assert.Equal("ygg-access-token", account.AccessToken);
        Assert.Equal("https://littleskin.cn/api/yggdrasil", account.AuthServerUrl);
        Assert.Equal(AccountType.AuthlibInjector, account.Type);
    }

    [Fact]
    public async Task Authenticate_InvalidCredentials_ThrowsFriendlyError()
    {
        const string errorBody = """
        { "error": "ForbiddenOperationException", "errorMessage": "Invalid credentials. Invalid username or password." }
        """;
        var handler = new StubHandler
        {
            Responder = _ => Json(errorBody, HttpStatusCode.Forbidden)
        };
        var service = CreateService(handler);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.AuthenticateAsync("https://example.com/api/yggdrasil", "u@e.com", "bad"));

        Assert.Contains("Invalid credentials", ex.Message);
    }

    [Fact]
    public async Task Authenticate_NonJsonError_ThrowsFallbackMessage()
    {
        var handler = new StubHandler
        {
            Responder = _ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("<html>gateway error</html>", Encoding.UTF8, "text/html")
            }
        };
        var service = CreateService(handler);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.AuthenticateAsync("https://example.com/api/yggdrasil", "u@e.com", "pw"));

        Assert.Contains("认证失败", ex.Message);
    }

    [Fact]
    public async Task Authenticate_MissingProfile_Throws()
    {
        const string authResponse = """{ "accessToken": "token-only", "availableProfiles": [] }""";
        var handler = new StubHandler { Responder = _ => Json(authResponse) };
        var service = CreateService(handler);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => service.AuthenticateAsync("https://example.com/api/yggdrasil", "u@e.com", "pw"));
    }

    [Fact]
    public async Task EnsureInjector_ResolvesVersionFromMetadata_AndDownloads()
    {
        const string metadata = """{ "build_number": 42, "version": "1.2.5" }""";
        byte[] artifact = "fake-jar-content"u8.ToArray();
        var handler = new StubHandler
        {
            Responder = request =>
            {
                if (request.RequestUri!.AbsolutePath.EndsWith("latest.json", StringComparison.OrdinalIgnoreCase))
                {
                    return Json(metadata);
                }
                if (request.RequestUri.AbsolutePath.Contains("authlib-injector-1.2.5.jar", StringComparison.OrdinalIgnoreCase))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(artifact)
                    };
                }
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
        };
        var service = CreateService(handler);

        string path = await service.EnsureInjectorAsync(_root);

        Assert.Equal(Path.Combine(_root, "authlib-injector.jar"), path);
        Assert.Equal(artifact, await File.ReadAllBytesAsync(path));
        Assert.True(handler.RequestCount >= 2, "至少需要一次元数据请求与一次下载请求");
    }

    [Fact]
    public async Task EnsureInjector_UsesDownloadUrlFromMetadata_WhenPresent()
    {
        const string metadata = """{ "version": "1.2.5", "download_url": "http://localhost/custom/authlib.jar" }""";
        byte[] artifact = "custom-mirror-content"u8.ToArray();
        var handler = new StubHandler
        {
            Responder = request =>
            {
                if (request.RequestUri!.AbsolutePath.EndsWith("latest.json", StringComparison.OrdinalIgnoreCase))
                {
                    return Json(metadata);
                }
                if (request.RequestUri.AbsolutePath.EndsWith("authlib.jar", StringComparison.OrdinalIgnoreCase))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(artifact)
                    };
                }
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
        };
        var service = CreateService(handler);

        string path = await service.EnsureInjectorAsync(_root);

        Assert.Equal(artifact, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task EnsureInjector_ExistingFile_SkipsDownload()
    {
        string jarPath = Path.Combine(_root, "authlib-injector.jar");
        await File.WriteAllTextAsync(jarPath, "already-present");
        var handler = new StubHandler();
        var service = CreateService(handler);

        string path = await service.EnsureInjectorAsync(_root);

        Assert.Equal(jarPath, path);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task EnsureInjector_AllMetadataSourcesFail_ThrowsChineseError()
    {
        var handler = new StubHandler
        {
            Responder = _ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        };
        var service = CreateService(handler);

        var ex = await Assert.ThrowsAsync<InvalidDataException>(
            () => service.EnsureInjectorAsync(_root));

        Assert.Contains("authlib-injector", ex.Message);
    }

    [Fact]
    public void AccountService_AddAuthlibAccount_RejectsDuplicateOnSameServer()
    {
        var config = new ConfigService(_root);
        var accounts = new AccountService(config);
        accounts.AddAuthlibAccount("SkinPlayer", "abcdef1234567890abcdef1234567890", "t1", "https://littleskin.cn/api/yggdrasil");

        Assert.Throws<ArgumentException>(() =>
            accounts.AddAuthlibAccount("SkinPlayer", "abcdef1234567890abcdef1234567890", "t2", "https://littleskin.cn/api/yggdrasil"));

        // 不同服务器允许同名账号。
        accounts.AddAuthlibAccount("SkinPlayer", "abcdef1234567890abcdef1234567890", "t3", "https://other.example.com/api/yggdrasil");
        Assert.Equal(2, config.Config.Accounts.Count);
    }
}
