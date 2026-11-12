using System.Text.Json;
using SandianshuiLauncher.Core.Models;

namespace SandianshuiLauncher.Core.Services;

/// <summary>一个模组分组：记录该组合方案下应启用的文件名清单。</summary>
public sealed class ModGroup
{
    public required string Name { get; init; }

    public required List<string> EnabledFiles { get; init; }
}

/// <summary>
/// 模组分组管理：把「当前启用状态」保存为命名分组，
/// 一键应用分组即可切换不同的模组组合方案。
/// 持久化于 <c>&lt;dataDir&gt;/mod-groups.json</c>。
/// </summary>
public sealed class ModGroupService
{
    private sealed class GroupFile
    {
        public List<ModGroupDto> Groups { get; set; } = [];
    }

    private sealed class ModGroupDto
    {
        public string Name { get; set; } = string.Empty;

        public List<string> EnabledFiles { get; set; } = [];
    }

    private static readonly JsonSerializerOptions GroupJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _groupPath;
    private readonly List<ModGroup> _groups = [];

    public ModGroupService(string dataDirectory)
    {
        _groupPath = Path.Combine(dataDirectory, "mod-groups.json");
        Load();
    }

    public IReadOnlyList<ModGroup> Groups => _groups;

    /// <summary>全部分组名（供 UI 下拉）。</summary>
    public List<string> Names => _groups.Select(g => g.Name).ToList();

    public ModGroup? Find(string name)
        => _groups.FirstOrDefault(g => string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>保存（或覆盖）一个分组：记录当前启用的文件名清单。</summary>
    public void SaveGroup(string name, IEnumerable<string> enabledFileNames)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("分组名称不能为空。", nameof(name));
        }
        string trimmed = name.Trim();
        ModGroup? existing = Find(trimmed);
        var group = new ModGroup
        {
            Name = trimmed,
            EnabledFiles = enabledFileNames
                .Where(f => !string.IsNullOrWhiteSpace(f))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()
        };
        if (existing is not null)
        {
            _groups.Remove(existing);
        }
        _groups.Add(group);
        Save();
    }

    public bool RemoveGroup(string name)
    {
        ModGroup? group = Find(name);
        if (group is null)
        {
            return false;
        }
        _groups.Remove(group);
        Save();
        return true;
    }

    /// <summary>
    /// 应用分组：组内文件启用、组外文件禁用。
    /// 返回（本次启用数, 本次禁用数）。
    /// </summary>
    public (int Enabled, int Disabled) ApplyGroup(string name, IReadOnlyList<InstalledMod> mods)
    {
        ModGroup group = Find(name)
            ?? throw new InvalidDataException($"分组 {name} 不存在。");

        int enabled = 0;
        int disabled = 0;
        foreach (InstalledMod mod in mods)
        {
            bool shouldEnable = group.EnabledFiles.Contains(mod.FileName, StringComparer.OrdinalIgnoreCase);
            if (shouldEnable == mod.IsEnabled)
            {
                continue;
            }
            ModService.SetEnabled(mod, shouldEnable);
            if (shouldEnable)
            {
                enabled++;
            }
            else
            {
                disabled++;
            }
        }
        return (enabled, disabled);
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_groupPath))
            {
                return;
            }
            string json = File.ReadAllText(_groupPath);
            var file = JsonSerializer.Deserialize<GroupFile>(json, GroupJsonOptions);
            if (file is null)
            {
                return;
            }
            foreach (ModGroupDto dto in file.Groups)
            {
                _groups.Add(new ModGroup
                {
                    Name = dto.Name,
                    EnabledFiles = dto.EnabledFiles
                });
            }
        }
        catch (JsonException)
        {
            TryBackupCorrupt();
        }
        catch (IOException)
        {
            // 读取失败按无分组处理。
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_groupPath)!);
            var file = new GroupFile
            {
                Groups = _groups
                    .Select(g => new ModGroupDto { Name = g.Name, EnabledFiles = g.EnabledFiles })
                    .ToList()
            };
            string json = JsonSerializer.Serialize(file, GroupJsonOptions);
            string tempPath = _groupPath + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, _groupPath, overwrite: true);
        }
        catch
        {
            // 分组落盘失败不影响主流程。
        }
    }

    private void TryBackupCorrupt()
    {
        try
        {
            if (File.Exists(_groupPath))
            {
                File.Copy(_groupPath, _groupPath + ".corrupt-" + DateTime.Now.ToString("yyyyMMddHHmmss"), overwrite: true);
            }
        }
        catch
        {
            // 备份失败不影响重建。
        }
    }
}
