using SandianshuiLauncher.Core.Models;
using SandianshuiLauncher.Core.Services;
using Xunit;

namespace SandianshuiLauncher.Core.Tests;

/// <summary>
/// <see cref="DownloadQueue"/> 测试：入队去重、落盘恢复、完成移除与损坏容错。
/// </summary>
public class DownloadQueueTests : IDisposable
{
    private readonly string _root;

    public DownloadQueueTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "sdsl_queue_" + Guid.NewGuid().ToString("N"));
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

    private static DownloadTask Task(string name)
        => new($"http://localhost/{name}", Path.Combine("E:", "MC", name), "abc", 10, name);

    [Fact]
    public void Enqueue_DuplicateDestination_Deduplicates()
    {
        var queue = new DownloadQueue(_root);

        queue.Enqueue([Task("a.jar"), Task("a.jar"), Task("b.jar")]);

        Assert.Equal(2, queue.Count);
    }

    [Fact]
    public void Enqueue_PersistsAndRestoresAcrossInstances()
    {
        var first = new DownloadQueue(_root);
        first.Enqueue([Task("a.jar"), Task("b.jar")]);

        var second = new DownloadQueue(_root);

        Assert.Equal(2, second.Count);
        Assert.Equal(Task("a.jar").DestinationPath, second.Tasks.First().DestinationPath);
        Assert.Equal("abc", second.Tasks.First().Sha1);
        Assert.Equal(10, second.Tasks.First().Size);
    }

    [Fact]
    public void RemoveCompleted_RemovesMatchingTasks()
    {
        var queue = new DownloadQueue(_root);
        queue.Enqueue([Task("a.jar"), Task("b.jar")]);

        queue.RemoveCompleted([Task("a.jar")]);

        Assert.Single(queue.Tasks);
        Assert.Equal(Task("b.jar").DestinationPath, queue.Tasks.Single().DestinationPath);
    }

    [Fact]
    public void Clear_EmptiesQueueAndPersists()
    {
        var queue = new DownloadQueue(_root);
        queue.Enqueue([Task("a.jar")]);
        queue.Clear();

        var restored = new DownloadQueue(_root);
        Assert.Equal(0, restored.Count);
    }

    [Fact]
    public void CorruptQueueFile_IsTolerated()
    {
        File.WriteAllText(Path.Combine(_root, "downloads.queue.json"), "{ not valid json");

        var queue = new DownloadQueue(_root);

        Assert.Equal(0, queue.Count);
        Assert.True(Directory.EnumerateFiles(_root, "downloads.queue.json.corrupt-*").Any());
    }
}
