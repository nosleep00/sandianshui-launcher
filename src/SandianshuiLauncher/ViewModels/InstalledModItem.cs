using CommunityToolkit.Mvvm.ComponentModel;
using SandianshuiLauncher.Core.Models;

namespace SandianshuiLauncher.ViewModels;

/// <summary>
/// 已安装模组的界面包装：在 <see cref="InstalledMod"/> 之外附加选择状态，
/// 用于批量启用/禁用/删除。
/// </summary>
public partial class InstalledModItem : ObservableObject
{
    public required InstalledMod Mod { get; init; }

    [ObservableProperty]
    private bool _isSelected;

    public string FileName => Mod.FileName;

    public string SizeDisplay => Mod.SizeDisplay;

    public bool IsEnabled => Mod.IsEnabled;
}
