using System.Globalization;
using System.Text.Json;

internal static class ItemStatusTypeCatalog
{
    private static Dictionary<int, string>? _merged;
    private static readonly object Gate = new();

    public static bool UsesPercentSuffix(int typeId) => VerifiedPercentTypeIds.Contains(typeId);

    /// <summary>StatusValue 为千分整数，预览/导出时按百分显示并带「%」（数值 = 表格值 ÷ 10）。</summary>
    public static bool UsesPermilleStoredAsPercentDisplay(int typeId) =>
        PermilleStoredAsPercentDisplayTypeIds.Contains(typeId);

    private static readonly HashSet<int> PermilleStoredAsPercentDisplayTypeIds = [408, 440, 448, 573, 574];

    private static readonly HashSet<int> VerifiedPercentTypeIds =
    [
        15, 23, 27, 31, 39,35, 55, 59, 86, 90, 94, 98,102, 106,107,110, 114, 118,
        124, 128, 132, 136, 141, 158, 157, 162, 192, 196,164,241,
        148, 156, 176, 180, 292, 369, 408,440, 448,
        456, 461, 462, 463, 488, 490, 493, 494,497,
        464,465,466,584,585,587,588,590,591,592,573,574,595
    ];

    public static void Invalidate()
    {
        lock (Gate)
        {
            _merged = null;
        }
    }

    /// <summary>将表格中的千分整数值格式化为百分数的数字部分（不含 %）。</summary>
    public static string FormatStoredPermilleAsPercentNumber(int permilleValue)
    {
        var percent = permilleValue / 10.0;
        if (percent == Math.Truncate(percent))
        {
            return ((long)percent).ToString(CultureInfo.InvariantCulture);
        }

        return percent.ToString("0.##", CultureInfo.InvariantCulture);
    }


    public static bool TryGetLabel(int typeId, out string label)
    {
        EnsureMerged();
        return _merged!.TryGetValue(typeId, out label!);
    }

    private static void EnsureMerged()
    {
        if (_merged is not null)
        {
            return;
        }

        lock (Gate)
        {
            if (_merged is not null)
            {
                return;
            }

            var d = new Dictionary<int, string>(CreateBuiltInVerifiedOnly());
            TryMergeJsonFile(d, Path.Combine(AppContext.BaseDirectory, "item-status-type-labels.json"));
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            TryMergeJsonFile(d, Path.Combine(appData, "LdtEditor", "item-status-type-labels.json"));
            _merged = d;
        }
    }

    private static void TryMergeJsonFile(Dictionary<int, string> into, string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return;
            }

            var json = File.ReadAllText(path);
            var raw = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (raw is null)
            {
                return;
            }

            foreach (var kv in raw)
            {
                if (!int.TryParse(kv.Key.Trim(), out var id) || string.IsNullOrWhiteSpace(kv.Value))
                {
                    continue;
                }

                into[id] = kv.Value.Trim();
            }
        }
        catch
        {
        }
    }

    private static Dictionary<int, string> CreateBuiltInVerifiedOnly() =>
        new()
        {
            [14] = "力量",
            [15] = "力量",
            [18] = "幸运",
            [19] = "敏捷",
            [22] = "魔力",
            [23] = "魔力",
            [26] = "体力",
            [27] = "体力",
            [30] = "最大HP",
            [31] = "最大HP",
            [34] = "最大SP",
            [35] = "最大SP",
            [38] = "属性力",
            [39] = "属性力",
            [55] = "休息恢复HP",
            [59] = "休息恢复SP",
            [62] = "最大攻击力",
            [66] = "最小攻击力",
            [70] = "水抵抗力",
            [86] = "减少伤害",
            [90] = "物理最小伤害",
            [94] = "物理最大伤害",
            [98] = "魔法最小伤害",
            [102] = "魔法最大伤害",
            [106] = "移动速度",
            [107] = "移动速度",
            [110] = "弹跳力",
            [111] = "弹跳力",
            [114] = "绳索移动速度",
            [118] = "梯子移动速度",
            [124] = "经验值获得量",
            [128] = "彩虹币获得量",
            [132] = "道具掉落率",
            [136] = "属性发生率",
            [140] = "物理伤害",
            [141] = "物理伤害",
            [144] = "物理减少伤害",
            [148] = "目标物理防御力减少",
            [156] = "物理暴击伤害",
            [157] = "最终物理暴击伤害",
            [158] = "物理暴击率",
            [162] = "魔法暴击伤害",
            [164] = "魔法暴击率",
            [176] = "物理命中率",
            [180] = "魔法命中率",
            [192] = "物理反击伤害",
            [196] = "魔法反击伤害",
            [216] = "防御力",
            [240] = "魔法伤害",
            [241] = "魔法伤害",
            [292] = "目标魔法抵抗力减少",
            [296] = "HP治愈量",
            [369] = "镶嵌成功率",
            [408] = "眩晕抗力",
            [440] = "技能冷却时间减少",
            [448] = "混乱抗力",
            [455] = "所有能力值",
            [456] = "所有能力值",
            [461] = "物理/魔法最大伤害",
            [462] = "物理/魔法最小伤害",
            [463] = "物理/魔法贯穿力",
            [464] = "物理/魔法暴击伤害",
            [465] = "物理/魔法暴击率",
            [466] = "物理/魔法命中率",
            [488] = "任务奖励强化",
            [490] = "物理最小/最大伤害",
            [492] = "最小/最大攻击力",
            [493] = "最小/最大攻击力",
            [494] = "物理/魔法背后攻击伤害",
            [495] = "伤害减少",
            [496] = "物理/魔法固定伤害",
            [497] = "物理/魔法回避率",
            [499] = "特殊计量条",
            [531] = "最大 MP",
            [533] = "最大所有卡片",
            [537] = "最大 RP",
            [546] = "最大 VP",
            [566] = "一般怪物额外伤害",
            [570] = "BOSS怪物额外伤害",
            [573] = "一般怪物支配力",
            [574] = "BOSS怪物支配力",
            [584] = "超人任务奖励加成",
            [585] = "最小/最大伤害",
            [586] = "武器攻击力/属性力",
            [587] = "武器攻击力/属性力",
            [588] = "物理/魔法固定伤害",
            [590] = "最终最小伤害",
            [591] = "最终最大伤害",
            [592] = "最终暴击伤害",
            [594] = "力量/魔力",
            [595] = "力量/魔力",
            [614] = "所有技能目标数",
        };
}
