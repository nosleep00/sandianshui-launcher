using System.Text.Json;
using SandianshuiLauncher.Core.Models;

namespace SandianshuiLauncher.Core.Services;

/// <summary>
/// 下载队列持久化：把未完成的下载任务序列化到 <c>&lt;dataDir&gt;/downloads.queue.json</c>，
/// 启动时恢复、完成后移除，损坏时备份重建。配合安装流程实现“关闭启动器后未完成任务下次可继续”。
/// </summary>
public sealed class DownloadQueue
{
    private static readonly JsonSerializerOptions QueueJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _queuePath;
    private List<DownloadTask> _tasks = [];

    public DownloadQueue(string dataDirectory)
    {
        _queuePath = Path.Combine(dataDirectory, "downloads.queue.json");
        Load();
    }

    /// <summary>当前未完成的任务（按目标路径去重）。</summary>
    public IReadOnlyList<DownloadTask> Tasks => _tasks;

    /// <summary>任务数（供 UI 展示）。</summary>
    public int Count => _tasks.Count;

    /// <summary>入队：目标路径相同视为同一任务，不重复添加。</summary>
    public void Enqueue(IReadOnlyList<DownloadTask> tasks)
    {
        bool changed = false;
        foreach (DownloadTask task in tasks)
        {
            if (_tasks.All(t => !PathsEqual(t.DestinationPath, task.DestinationPath)))
            {
                _tasks.Add(task);
                changed = true;
            }
        }
        if (changed)
        {
            Save();
        }
    }

    /// <summary>任务完成（无论成功失败均可从待续清单移除；失败会由下次安装重新收集）。</summary>
    public void RemoveCompleted(IReadOnlyList<DownloadTask> tasks)
    {
        int before = _tasks.Count;
        _tasks.RemoveAll(t => tasks.Any(x => PathsEqual(x.DestinationPath, t.DestinationPath)));
        if (_tasks.Count != before)
        {
            Save();
        }
    }

    public void Clear()
    {
        _tasks.Clear();
        Save();
    }

    private static bool PathsEqual(string a, string b)
        => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private void Load()
    {
        try
        {
            if (!File.Exists(_queuePath))
            {
                return;
            }
            string json = File.ReadAllText(_queuePath);
            var loaded = JsonSerializer.Deserialize<List<DownloadTask>>(json, QueueJsonOptions);
            if (loaded is not null)
            {
                _tasks = loaded;
            }
        }
        catch (JsonException)
        {
            TryBackupCorruptQueue();
        }
        catch (IOException)
        {
            // 读取失败视为空队列，不阻断启动。
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_queuePath)!);
            string json = JsonSerializer.Serialize(_tasks, QueueJsonOptions);
            string tempPath = _queuePath + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, _queuePath, overwrite: true);
        }
        catch
        {
            // 队列落盘失败不影响下载本身。
        }
    }

    private void TryBackupCorruptQueue()
    {
        try
        {
            if (File.Exists(_queuePath))
            {
                File.Copy(_queuePath, _queuePath + ".corrupt-" + DateTime.Now.ToString("yyyyMMddHHmmss"), overwrite: true);
            }
        }
        catch
        {
            // 备份失败不影响重建。
        }
    }
}
