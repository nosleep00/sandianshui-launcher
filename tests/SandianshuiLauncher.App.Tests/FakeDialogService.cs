using SandianshuiLauncher.Services;

namespace SandianshuiLauncher.App.Tests;

/// <summary>记录调用、可配置确认结果的假对话框服务。</summary>
internal sealed class FakeDialogService : IDialogService
{
    public bool ConfirmResult { get; set; } = true;

    /// <summary>按顺序消费的确认结果（优先于 <see cref="ConfirmResult"/>，用于精确控制多次弹窗）。</summary>
    public Queue<bool> ConfirmResults { get; } = new();

    /// <summary>PickFile 返回的路径（null 表示用户取消）。</summary>
    public string? PickFileResult { get; set; }

    public List<string> PickFileCalls { get; } = [];

    /// <summary>PickSaveFile 返回的路径（null 表示用户取消）。</summary>
    public string? PickSaveFileResult { get; set; }

    public List<string> PickSaveFileCalls { get; } = [];

    /// <summary>PickFolder 返回的路径（null 表示用户取消）。</summary>
    public string? PickFolderResult { get; set; }

    public List<string> PickFolderCalls { get; } = [];

    /// <summary>PromptText 返回的文本（null 表示用户取消）。</summary>
    public string? PromptTextResult { get; set; }

    public List<string> PromptTextCalls { get; } = [];

    public List<string> Infos { get; } = [];
    public List<string> Warnings { get; } = [];
    public List<string> Errors { get; } = [];
    public List<string> ConfirmMessages { get; } = [];
    public List<string> OpenedFolders { get; } = [];
    public int ConfirmCalls { get; private set; }

    public void Info(string message, string title = "提示") => Infos.Add(message);

    public void Warn(string message, string title = "提示") => Warnings.Add(message);

    public void Error(string message, string title = "错误") => Errors.Add(message);

    public bool Confirm(string message, string title, bool isWarning = false)
    {
        ConfirmCalls++;
        ConfirmMessages.Add(message);
        return ConfirmResults.Count > 0 ? ConfirmResults.Dequeue() : ConfirmResult;
    }

    public string? PickFile(string filter, string title)
    {
        PickFileCalls.Add(filter);
        return PickFileResult;
    }

    public string? PickSaveFile(string filter, string title, string defaultFileName)
    {
        PickSaveFileCalls.Add(defaultFileName);
        return PickSaveFileResult;
    }

    public string? PickFolder(string title)
    {
        PickFolderCalls.Add(title);
        return PickFolderResult;
    }

    public string? PromptText(string message, string title, string initial = "")
    {
        PromptTextCalls.Add(message);
        return PromptTextResult ?? initial;
    }

    public void OpenFolder(string path) => OpenedFolders.Add(path);
}
