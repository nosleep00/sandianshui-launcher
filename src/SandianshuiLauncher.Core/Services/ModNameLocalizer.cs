namespace SandianshuiLauncher.Core.Services;

/// <summary>
/// 模组中文名本地化：内置「英文名 → 中文名」映射表，
/// 让搜索结果显示像 PCL2 一样的中文名称（无需联网）。
/// 先精确匹配，再按最长键做包含匹配，未命中返回原名。
/// </summary>
public static class ModNameLocalizer
{
    /// <summary>常见热门模组英文名 → 中文名（可随时扩充）。</summary>
    private static readonly Dictionary<string, string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Just Enough Items"] = "JEI 物品管理器",
        ["Just Enough Items (JEI)"] = "JEI 物品管理器",
        ["Roughly Enough Items"] = "REI 物品管理器",
        ["EMI"] = "EMI 物品管理器",
        ["JourneyMap"] = "旅行地图",
        ["Mouse Tweaks"] = "鼠标手势",
        ["Sodium"] = "钠（Sodium）",
        ["Sodium Extra"] = "钠扩展（Sodium Extra）",
        ["Reese's Sodium Options"] = "Reese 的钠设置",
        ["Indium"] = "铟（Indium）",
        ["Iris Shaders"] = "Iris 光影",
        ["OptiFine"] = "OptiFine 高清修复",
        ["Fabric API"] = "Fabric API 前置",
        ["Cloth Config API"] = "Cloth 配置",
        ["Mod Menu"] = "模组菜单",
        ["Xaero's Minimap"] = "Xaero 小地图",
        ["Xaero's World Map"] = "Xaero 世界地图",
        ["BetterF3"] = "更好的 F3",
        ["Biomes O' Plenty"] = "超多生物群系",
        ["FerriteCore"] = "FerriteCore 内存优化",
        ["Entity Culling"] = "实体剔除",
        ["Cull Leaves"] = "树叶剔除",
        ["Lithium"] = "锂（Lithium）",
        ["Continuity"] = "Continuity 连接纹理",
        ["LambDynamicLights"] = "动态光源",
        ["AppleSkin"] = "AppleSkin 苹果皮",
        ["Jade"] = "Jade 提示框",
        ["WTHIT"] = "WTHIT 这是什么",
        ["ImmediatelyFast"] = "ImmediatelyFast 极速渲染",
        ["Falling Leaves"] = "落叶",
        ["Not Enough Animations"] = "更多动画（NEA）",
        ["3D Skin Layers"] = "3D 皮肤层",
        ["First-person Model"] = "第一人称模型",
        ["Create"] = "机械动力",
        ["Immersive Engineering"] = "沉浸工程",
        ["Mekanism"] = "通用机械",
        ["Applied Energistics 2"] = "应用能源 2",
        ["Tinkers' Construct"] = "匠魂",
        ["Botania"] = "植物魔法",
        ["Quark"] = "夸克",
        ["Chisel"] = "凿子",
        ["Waystones"] = "传送石碑",
        ["Serene Seasons"] = "静谧四季",
        ["Farmer's Delight"] = "农夫乐事",
        ["Supplementaries"] = "锦致装饰",
        ["Distant Horizons"] = "远景地平线",
        ["Better Advancements"] = "更好的进度",
        ["Better Mount HUD"] = "更好的坐骑 HUD",
        ["YUNG's Better Dungeons"] = "YUNG 的更好地牢",
        ["YUNG's Better Mineshafts"] = "YUNG 的更好矿井",
        ["YUNG's Better Nether Fortresses"] = "YUNG 的更好下界要塞",
        ["Terralith"] = "Terralith 泰拉利特",
        ["Oh The Biomes You'll Go"] = "你将去的生物群系",
        ["Alex's Mobs"] = "Alex 的生物",
        ["Twilight Forest"] = "暮色森林",
        ["The Aether"] = "以太",
        ["Aether"] = "以太",
        ["Better Nether"] = "更好的下界",
        ["Better End"] = "更好的末地",
        ["MineColonies"] = "模拟殖民地",
        ["Storage Drawers"] = "储物抽屉",
        ["Iron Chests"] = "铁箱子",
        ["Sophisticated Backpacks"] = "精致背包",
        ["Traveler's Backpack"] = "旅行者背包",
        ["Curios"] = "Curios 饰品",
        ["Trinkets"] = "Trinkets 饰品",
        ["Patchouli"] = "Patchouli 指南书",
        ["Puzzles Lib"] = "Puzzles 前置库",
        ["Architectury"] = "Architectury 跨加载器 API",
        ["Balm"] = "Balm 前置",
        ["Cupboard"] = "Cupboard 前置",
        ["Epic Fight"] = "史诗战斗",
        ["Ice and Fire"] = "冰与火",
        ["Refined Storage"] = "精致存储",
        ["Draconic Evolution"] = "龙之进化",
        ["ProjectE"] = "等价交换",
        ["Blood Magic"] = "血魔法",
        ["Astral Sorcery"] = "星辉魔法",
        ["Ender IO"] = "末影接口",
        ["Thermal Expansion"] = "热力膨胀",
        ["Industrial Foregoing"] = "工业先锋",
        ["Flux Networks"] = "通量网络",
        ["WorldEdit"] = "创世神",
        ["Litematica"] = "投影",
        ["MiniHUD"] = "迷你 HUD",
        ["Tweakeroo"] = "Tweakeroo 工具箱",
        ["MaLiLib"] = "MaLiLib 前置",
        ["GeckoLib"] = "GeckoLib 动画引擎",
        ["ViaFabric"] = "ViaFabric 跨版本",
        ["ViaVersion"] = "ViaVersion 跨版本",
        ["CraftTweaker"] = "CraftTweaker 合成修改",
        ["KubeJS"] = "KubeJS 脚本",
        ["Pam's HarvestCraft"] = "潘马斯的丰收工艺",
        ["Cooking for Blockheads"] = "傻瓜烹饪",
        ["SecurityCraft"] = "安全工艺",
        ["Gravestone"] = "墓碑",
        ["Corpse"] = "尸体（Corpse）",
        ["Bookshelf"] = "Bookshelf 前置",
        ["Flywheel"] = "Flywheel 渲染引擎",
        ["Iron's Spells 'n Spellbooks"] = "铁的法术与法术书",
        ["Mowzie's Mobs"] = "Mowzie 的生物",
        ["Occultism"] = "神秘学",
        ["Productive Bees"] = "勤劳的蜜蜂",
        ["Nature's Aura"] = "自然灵气",
        ["Powah"] = "Powah 能量"
    };

    /// <summary>取中文名：精确匹配 → 最长键包含匹配 → 原名。</summary>
    public static string Localize(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return string.Empty;
        }
        string trimmed = title.Trim();
        if (Names.TryGetValue(trimmed, out string? exact))
        {
            return exact;
        }

        string? bestKey = null;
        foreach (string key in Names.Keys)
        {
            if (trimmed.Contains(key, StringComparison.OrdinalIgnoreCase)
                && (bestKey is null || key.Length > bestKey.Length))
            {
                bestKey = key;
            }
        }
        return bestKey is null ? trimmed : Names[bestKey];
    }
}
