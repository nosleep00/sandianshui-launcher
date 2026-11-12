using System.Net;
using System.Text.Json;
using SandianshuiLauncher.Core.Models;
using SandianshuiLauncher.Core.Services;
using SandianshuiLauncher.Services;
using SandianshuiLauncher.ViewModels;

namespace SandianshuiLauncher.App.Tests;

/// <summary>
/// 模组平台切换与中文关键词搜索测试：
/// Modrinth 路径按中文映射候选查询；CurseForge 路径要求 API Key 且结果带平台标记。
/// </summary>
public class ModsViewModelPlatformTests : IDisposable
{
    private readonly string _root;

    public ModsViewModelPlatformTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "sdsl_platform_" + Guid.NewGuid().ToString("N"));
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

    private sealed class RoutingHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage> Responder { get; set; }
            = _ => new HttpResponseMessage(HttpStatusCode.NotFound);

        public List<string> RequestedUrls { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestedUrls.Add(request.RequestUri!.ToString());
            return Task.FromResult(Responder(request));
        }
    }

    private static HttpResponseMessage JsonResponse(string content)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(content, System.Text.Encoding.UTF8, "application/json")
        };

    private (ModsViewModel ViewModel, RoutingHandler Handler, ConfigService Config) Create(string? apiKey)
    {
        var handler = new RoutingHandler();
        var config = new ConfigService(Path.Combine(_root, "data-" + Guid.NewGuid().ToString("N")));
        string gameDir = Path.Combine(_root, "mc-" + Guid.NewGuid().ToString("N"));
        config.Config.GameDirectory = gameDir;
        config.Config.ModUseMirror = true;
        config.Config.CurseForgeApiKey = apiKey;
        var http = new HttpClient(handler);
        var downloads = new DownloadService(http);
        var modService = new ModService(http, downloads, config);
        var modpackService = new ModpackService(http, downloads);
        var curseForgeService = new CurseForgeService(http, downloads, config);
        var groupService = new ModGroupService(Path.Combine(_root, "data-" + Guid.NewGuid().ToString("N")));
        var dialogs = new FakeDialogService();
        var viewModel = new ModsViewModel(config, modService, curseForgeService, groupService,
            new DirectoryPackService(new VersionService(http, new DownloadSourceProvider(config.Config))),
            TestServices.CreatePackImportService(http, config, downloads, dialogs),
            dialogs)
        {
            SelectedVersion = new LocalVersion { Id = "1.20.4" },
            SelectedLoader = "fabric",
            SelectedResourceType = "mod"
        };
        return (viewModel, handler, config);
    }

    [Fact]
    public async Task ModrinthSearch_ChineseKeyword_UsesMappedCandidate()
    {
        (ModsViewModel viewModel, RoutingHandler handler, _) = Create(apiKey: null);
        viewModel.SelectedPlatform = "modrinth";
        viewModel.SearchText = "小地图";

        handler.Responder = request =>
            JsonResponse("""{ "hits": [ { "project_id": "X", "slug": "x", "title": "Xaero Minimap", "description": "d", "display_categories": ["fabric"] } ], "total_hits": 1 }""");

        await viewModel.SearchCommand.ExecuteAsync(null);

        Assert.Contains(handler.RequestedUrls, u => u.Contains("query=minimap"));
        Assert.Single(viewModel.Results);
    }

    [Fact]
    public async Task CurseForgeSearch_WithKey_ReturnsPlatformTaggedResults()
    {
        (ModsViewModel viewModel, RoutingHandler handler, _) = Create(apiKey: "k-123");
        viewModel.SelectedPlatform = "curseforge";
        viewModel.SearchText = "minimap";

        string cfJson = JsonSerializer.Serialize(new
        {
            data = new object[]
            {
                new
                {
                    id = 12345,
                    name = "Xaero's Minimap",
                    slug = "xaeros-minimap",
                    summary = "Minimap mod",
                    downloadCount = 99999,
                    logo = new { url = "https://media.forgecdn.net/avatars/x.png" },
                    links = new { websiteUrl = "https://example.com" }
                }
            }
        });
        handler.Responder = request =>
            request.RequestUri!.Host.Contains("curseforge", StringComparison.OrdinalIgnoreCase)
                ? JsonResponse(cfJson)
                : new HttpResponseMessage(HttpStatusCode.NotFound);

        await viewModel.SearchCommand.ExecuteAsync(null);

        Assert.Single(viewModel.Results);
        Assert.Equal("curseforge", viewModel.Results[0].SourcePlatform);
        Assert.Equal("12345", viewModel.Results[0].ProjectId);
        Assert.Contains(handler.RequestedUrls, u => u.Contains("api.curseforge.com/v1/mods/search"));
    }

    [Fact]
    public async Task CurseForgeSearch_NoKey_ShowsWarningAndSendsNoRequest()
    {
        var dialogs = new FakeDialogService();
        var handler = new RoutingHandler();
        var config = new ConfigService(Path.Combine(_root, "data-" + Guid.NewGuid().ToString("N")));
        config.Config.GameDirectory = Path.Combine(_root, "mc-" + Guid.NewGuid().ToString("N"));
        config.Config.CurseForgeApiKey = null;
        var http = new HttpClient(handler);
        var downloads = new DownloadService(http);
        var groupService = new ModGroupService(Path.Combine(_root, "data-" + Guid.NewGuid().ToString("N")));
        var viewModel = new ModsViewModel(
            config,
            new ModService(http, downloads, config),
            new CurseForgeService(http, downloads, config),
            groupService,
            new DirectoryPackService(new VersionService(http, new DownloadSourceProvider(config.Config))),
            TestServices.CreatePackImportService(http, config, downloads, dialogs),
            dialogs)
        {
            SelectedVersion = new LocalVersion { Id = "1.20.4" },
            SelectedResourceType = "mod"
        };
        viewModel.SelectedPlatform = "curseforge";
        viewModel.SearchText = "minimap";

        await viewModel.SearchCommand.ExecuteAsync(null);

        Assert.Contains(dialogs.Warnings, w => w.Contains("API Key"));
        Assert.Empty(handler.RequestedUrls);
    }

    [Fact]
    public async Task ModrinthSearch_ResultGetsChineseTitle()
    {
        (ModsViewModel viewModel, RoutingHandler handler, _) = Create(apiKey: null);
        viewModel.SelectedPlatform = "modrinth";
        viewModel.SearchText = "jei";

        handler.Responder = request =>
            JsonResponse("""{ "hits": [ { "project_id": "u6dRKJwZ", "slug": "jei", "title": "Just Enough Items", "description": "View Items and Recipes", "display_categories": ["fabric"], "icon_url": "https://cdn.modrinth.com/data/u6dRKJwZ/icon.png" } ], "total_hits": 1 }""");

        await viewModel.SearchCommand.ExecuteAsync(null);

        ModSearchResult result = Assert.Single(viewModel.Results);
        Assert.Equal("JEI 物品管理器", result.DisplayTitle);
        Assert.Equal("Just Enough Items", result.DisplaySubtitle);
    }

    [Fact]
    public async Task CurseForgeSearch_ResultGetsChineseTitle()
    {
        (ModsViewModel viewModel, RoutingHandler handler, _) = Create(apiKey: "k-1");
        viewModel.SelectedPlatform = "curseforge";
        viewModel.SearchText = "create";

        string cfJson = JsonSerializer.Serialize(new
        {
            data = new object[]
            {
                new
                {
                    id = 328085,
                    name = "Create",
                    slug = "create",
                    summary = "Aesthetic technology mod",
                    downloadCount = 99999,
                    logo = new { url = "https://media.forgecdn.net/avatars/328085.png" },
                    links = new { websiteUrl = "https://example.com" }
                }
            }
        });
        handler.Responder = request =>
            request.RequestUri!.Host.Contains("curseforge", StringComparison.OrdinalIgnoreCase)
                ? JsonResponse(cfJson)
                : new HttpResponseMessage(HttpStatusCode.NotFound);

        await viewModel.SearchCommand.ExecuteAsync(null);

        Assert.Equal("机械动力", viewModel.Results.Single().DisplayTitle);
    }

    [Fact]
    public async Task EmptySearch_BrowsesBySort_NoQueryParam()
    {
        (ModsViewModel viewModel, RoutingHandler handler, _) = Create(apiKey: null);
        viewModel.SelectedPlatform = "modrinth";
        viewModel.SelectedSort = "downloads";
        viewModel.SearchText = "";

        handler.Responder = request =>
            JsonResponse("""{ "hits": [ { "project_id": "X", "slug": "x", "title": "Popular Mod", "description": "d", "display_categories": ["fabric"] } ], "total_hits": 1 }""");

        await viewModel.SearchCommand.ExecuteAsync(null);

        Assert.Single(viewModel.Results);
        string url = handler.RequestedUrls.Last();
        Assert.Contains("index=downloads", url);
        Assert.DoesNotContain("query=", url);
        Assert.Contains("limit=100", url); // 浏览模式推荐上限 100
        Assert.Contains("浏览热门", viewModel.StatusText);
    }

    [Fact]
    public async Task EmptySearch_RelevanceSort_FallsBackToDownloads()
    {
        (ModsViewModel viewModel, RoutingHandler handler, _) = Create(apiKey: null);
        viewModel.SelectedPlatform = "modrinth";
        viewModel.SelectedSort = "relevance";
        viewModel.SearchText = "";

        handler.Responder = request =>
            JsonResponse("""{ "hits": [ { "project_id": "X", "slug": "x", "title": "Popular Mod", "description": "d", "display_categories": ["fabric"] } ], "total_hits": 1 }""");

        await viewModel.SearchCommand.ExecuteAsync(null);

        Assert.Contains("index=downloads", handler.RequestedUrls.Last());
    }

    [Fact]
    public async Task EnterPage_WithEmptySearch_AutoLoadsPopular()
    {
        // 本地存在版本 → RefreshVersions（导航进入页面）后自动浏览。
        (ModsViewModel viewModel, RoutingHandler handler, ConfigService config) = Create(apiKey: null);
        string versionDir = Path.Combine(config.Config.GameDirectory, "versions", "1.20.4");
        Directory.CreateDirectory(versionDir);
        await File.WriteAllTextAsync(Path.Combine(versionDir, "1.20.4.json"), """{ "id": "1.20.4" }""");
        viewModel.SelectedPlatform = "modrinth";
        viewModel.SelectedSort = "downloads";
        viewModel.SearchText = "";

        handler.Responder = request =>
            JsonResponse("""{ "hits": [ { "project_id": "X", "slug": "x", "title": "Popular Mod", "description": "d", "display_categories": ["fabric"] } ], "total_hits": 1 }""");

        viewModel.RefreshVersions();
        await Task.Delay(300);

        Assert.Single(viewModel.Results);
        Assert.Contains(handler.RequestedUrls, u => u.Contains("index=downloads"));
    }

    [Fact]
    public async Task EmptySearch_ResourcePackType_BrowsesResourcePacks()
    {
        (ModsViewModel viewModel, RoutingHandler handler, _) = Create(apiKey: null);
        viewModel.SelectedPlatform = "modrinth";
        viewModel.SelectedSort = "downloads";
        viewModel.SelectedResourceType = "resourcepack";
        viewModel.SearchText = "";

        handler.Responder = request =>
            JsonResponse("""{ "hits": [ { "project_id": "RP1", "slug": "rp", "title": "Nice Pack", "description": "d", "display_categories": ["decoration"] } ], "total_hits": 1 }""");

        await viewModel.SearchCommand.ExecuteAsync(null);

        Assert.Single(viewModel.Results);
        string url = handler.RequestedUrls.Last();
        Assert.Contains("resourcepack", url);
        Assert.Contains("limit=100", url);
        Assert.Contains("浏览热门", viewModel.StatusText);
    }

    [Fact]
    public async Task BrowseMode_SwitchingResourceType_ReloadsRecommendations()
    {
        (ModsViewModel viewModel, RoutingHandler handler, _) = Create(apiKey: null);
        viewModel.SelectedPlatform = "modrinth";
        viewModel.SelectedSort = "downloads";
        viewModel.SelectedResourceType = "mod";
        viewModel.SearchText = "";

        handler.Responder = request =>
            JsonResponse("""{ "hits": [ { "project_id": "X", "slug": "x", "title": "Item", "description": "d", "display_categories": ["fabric"] } ], "total_hits": 1 }""");

        // 首次浏览（模组）。
        await viewModel.SearchCommand.ExecuteAsync(null);
        Assert.Contains(handler.RequestedUrls, u => u.Contains("project_type%3Amod"));

        // 切换到光影包 → 自动重新浏览。
        viewModel.SelectedResourceType = "shader";
        await Task.Delay(300);

        Assert.Contains(handler.RequestedUrls, u => u.Contains("shader"));
    }
}
