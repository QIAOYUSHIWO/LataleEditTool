/// <summary>
/// LaTale / 彩虹岛 LDT：由 _Icon 编码解析 GLOBALRES 下图集 PNG。
/// 除 <c>20001300–20001399</c> 固定为 <c>542042520.PNG</c> 外，其余由区间 + 低位 00–99 或 ITEMCHINA 锚点曲线推算。
/// </summary>
internal static class LaTaleIconSheet
{
    public const string IconColumnName = "_Icon";
    public const string IconIndexColumnName = "_IconIndex";

    public const int AtlasCellPixels = 32;
    public const int IconIndexFirstValue = 1;

    public const int ItemEtc100013Min = 10001300;
    public const int ItemEtc100013Max = 10001399;
    public const int ItemEtc100013Modulo = 100;

    private static readonly (int Rem, int PngSuffix)[] ItemChinaKnots =
    [
        (8, 1), (14, 5), (21, 12), (23, 15), (28, 16), (34, 17), (37, 19),
        (43, 23), (45, 25), (48, 27), (53, 30), (62, 38),
    ];

    public static bool IsItemEtc100013Range(int iconId) =>
        iconId >= ItemEtc100013Min && iconId <= ItemEtc100013Max;

    /// <summary>必须用显式比较，避免 <c>is</c> 模式在边界上的误解析。</summary>
    public static bool IsItemChinaIconRange(int iconId)
    {
        var hi = iconId - iconId % 100;
        if (hi >= 10001400 && hi <= 10001499)
        {
            return true;
        }

        if (hi >= 10002400 && hi <= 10002499)
        {
            return true;
        }

        return false;
    }

    public static bool IsSpecial542SheetRange(int iconId) =>
        iconId >= 20001300 && iconId <= 20001399;

    public static bool IconIdUsesSheetSuffixInStoredIconId(int iconId) =>
        !IsItemChinaIconRange(iconId);

    /// <summary>与选图窗图集序号下拉默认规则一致：用于物品预览等小图从 SPF 解析图集文件名。</summary>
    public static int DefaultSheetSuffixForIconId(int iconId)
    {
        var mod = ItemEtc100013Modulo;
        if (IsItemChinaIconRange(iconId))
        {
            return 0;
        }

        if (IconIdUsesSheetSuffixInStoredIconId(iconId))
        {
            return Math.Clamp((int)((uint)iconId % (uint)mod), 0, mod - 1);
        }

        return 0;
    }

    public static int IconIdHighBase100013(int iconId) =>
        iconId - (int)((uint)iconId % ItemEtc100013Modulo);

    public static string ItemEtcLogicalFileName(int sheetSuffix00to99) =>
        $"ITEMETC_ICON{sheetSuffix00to99 % ItemEtc100013Modulo:D2}.PNG";

    public static bool IsKnownIconSheet(int iconId) =>
        IsItemEtc100013Range(iconId)
        || IsItemChinaIconRange(iconId)
        || IsSpecial542SheetRange(iconId)
        || InRange(iconId, 20000, 20099)
        || InRange(iconId, 10001100, 10001199)
        || InRange(iconId, 10001200, 10001299)
        || InRange(iconId, 10001500, 10001599)
        || InRange(iconId, 10001600, 10001699)
        || InRange(iconId, 10001700, 10001799)
        || InRange(iconId, 10002200, 10002299)
        || InRange(iconId, 10004000, 10004099)
        || InRange(iconId, 10004400, 10004499)
        || InRange(iconId, 10003000, 10003099)
        || InRange(iconId, 10005000, 10005099)
        || InRange(iconId, 10002100, 10002199)
        || InRange(iconId, 20001200, 20001299)
        || iconId == 10004301;

    /// <summary>选图器顶部说明：与 <see cref="TryResolveSheet"/> 当前解析出的逻辑文件名一致。</summary>
    public static string FormatPickerBannerFromLogical(string logicalFileName)
    {
        var n = logicalFileName.Trim();
        if (n.Length == 0)
        {
            return "图集";
        }

        if (n.StartsWith("ITEMCHINA_ICON", StringComparison.OrdinalIgnoreCase))
        {
            return "图集：ITEMCHINA（随 _Icon 低位锚点；仅选格改指数）";
        }

        if (n.StartsWith("ITEMETC_ICON CHINA", StringComparison.OrdinalIgnoreCase))
        {
            return "图集序号 00–99（ITEMETC CHINA_**）";
        }

        if (n.StartsWith("ITEMETC_ICON", StringComparison.OrdinalIgnoreCase))
        {
            return "图集序号 00–99 (ITEMETC_ICON**)";
        }

        if (n.StartsWith("MOB_ICON", StringComparison.OrdinalIgnoreCase))
        {
            return "图集序号 00–99 (MOB_ICON**)";
        }

        if (n.StartsWith("NPC_ICON", StringComparison.OrdinalIgnoreCase))
        {
            return "图集序号 00–99 (NPC_ICON**)";
        }

        if (n.StartsWith("SKILL_ICON", StringComparison.OrdinalIgnoreCase))
        {
            return "图集序号 00–99 (SKILL_ICON**)";
        }

        if (n.StartsWith("ITEMBATTLE_ICON", StringComparison.OrdinalIgnoreCase))
        {
            return "图集序号 00–99 (ITEMBATTLE_ICON**)";
        }

        if (n.StartsWith("ITEMCASH_ICON", StringComparison.OrdinalIgnoreCase))
        {
            return "图集序号 00–99 (ITEMCASH_ICON**)";
        }

        if (n.StartsWith("ITEMKOREA_ICON", StringComparison.OrdinalIgnoreCase))
        {
            return "图集序号 00–99 (ITEMKOREA_ICON**)";
        }

        if (n.StartsWith("ITEMCOLLABO_ICON", StringComparison.OrdinalIgnoreCase))
        {
            return "图集序号 00–99 (ITEMCOLLABO_ICON**)";
        }

        if (n.StartsWith("ITEMBEAUTY_ICON", StringComparison.OrdinalIgnoreCase))
        {
            return "图集序号 00–99 (ITEMBEAUTY_ICON**)";
        }

        if (n.StartsWith("ITEMTAIWAN_ICON", StringComparison.OrdinalIgnoreCase))
        {
            return "图集序号 00–99 (ITEMTAIWAN_ICON**)";
        }

        if (n.StartsWith("ITEMJAPAN_ICON", StringComparison.OrdinalIgnoreCase))
        {
            return "图集：ITEMJAPAN（固定 ICON00）";
        }

        if (n.StartsWith("ITEMBATTLE_CHINA", StringComparison.OrdinalIgnoreCase))
        {
            return "图集：ITEMBATTLE_CHINA（固定 ICON00）";
        }

        if (string.Equals(n, "542042520.PNG", StringComparison.OrdinalIgnoreCase))
        {
            return "图集：542042520.PNG（200013xx）";
        }

        return $"图集：{n}";
    }

    /// <summary>选图顶栏：在 <see cref="FormatPickerBannerFromLogical"/> 基础上列出 SPF 中实际存在的序号。</summary>
    public static string FormatPickerSheetLabel(string logicalFileName, IReadOnlyList<int>? availableSuffixes)
    {
        var baseText = FormatPickerBannerFromLogical(logicalFileName);
        if (availableSuffixes is null || availableSuffixes.Count == 0)
        {
            return baseText;
        }

        var nums = string.Join(
            ", ",
            availableSuffixes.Select(static s => s.ToString("D2", System.Globalization.CultureInfo.InvariantCulture)));
        return $"{baseText}（SPF：{nums}）";
    }

    /// <summary>
    /// 解析顺序：先 100013xx（ITEMETC），再 100040xx（MOB，避免与任何其它规则歧义），再 ITEMCHINA，再其余区间。
    /// </summary>
    public static bool TryResolveSheet(int iconId, int sheetSuffix00to99, out string logicalFileName, out int highBaseForApply)
    {
        sheetSuffix00to99 = Math.Clamp(sheetSuffix00to99, 0, 99);

        if (IsItemEtc100013Range(iconId))
        {
            logicalFileName = ItemEtcLogicalFileName(sheetSuffix00to99);
            highBaseForApply = IconIdHighBase100013(iconId);
            return true;
        }

        if (InRange(iconId, 10004000, 10004099))
        {
            logicalFileName = $"MOB_ICON{sheetSuffix00to99:D2}.PNG";
            highBaseForApply = iconId - iconId % 100;
            return true;
        }

        if (IsItemChinaIconRange(iconId))
        {
            logicalFileName = ItemChinaLogicalFileName(iconId);
            highBaseForApply = iconId;
            return true;
        }

        if (IsSpecial542SheetRange(iconId))
        {
            logicalFileName = "542042520.PNG";
            highBaseForApply = iconId - iconId % 100;
            return true;
        }

        if (InRange(iconId, 20000, 20099))
        {
            logicalFileName = $"ITEMTAIWAN_ICON{sheetSuffix00to99:D2}.PNG";
            highBaseForApply = iconId - iconId % 100;
            return true;
        }

        if (iconId == 10004301)
        {
            logicalFileName = "ITEMETC_ICON CHINA_01.PNG";
            highBaseForApply = iconId - iconId % 100;
            return true;
        }

        if (InRange(iconId, 10004400, 10004499))
        {
            logicalFileName = $"ITEMETC_ICON CHINA_{sheetSuffix00to99 + 1:D2}.PNG";
            highBaseForApply = iconId - iconId % 100;
            return true;
        }

        if (InRange(iconId, 10001100, 10001199))
        {
            logicalFileName = $"ITEMBATTLE_ICON{sheetSuffix00to99:D2}.PNG";
            highBaseForApply = iconId - iconId % 100;
            return true;
        }

        if (InRange(iconId, 10001200, 10001299))
        {
            logicalFileName = $"ITEMCASH_ICON{sheetSuffix00to99:D2}.PNG";
            highBaseForApply = iconId - iconId % 100;
            return true;
        }

        if (InRange(iconId, 20001200, 20001299))
        {
            logicalFileName = $"ITEMCASH_ICON{sheetSuffix00to99:D2}.PNG";
            highBaseForApply = iconId - iconId % 100;
            return true;
        }

        if (iconId == 10001502)
        {
            logicalFileName = "ITEMJAPAN_ICON01.PNG";
            highBaseForApply = iconId - iconId % 100;
            return true;
        }

        if (InRange(iconId, 10001500, 10001599))
        {
            logicalFileName = "ITEMJAPAN_ICON00.PNG";
            highBaseForApply = iconId - iconId % 100;
            return true;
        }

        if (iconId == 10001600)
        {
            logicalFileName = "ITEMRENEWAL_ICON00.PNG";
            highBaseForApply = iconId - iconId % 100;
            return true;
        }

        if (InRange(iconId, 10001600, 10001699))
        {
            logicalFileName = "ITEMBATTLE_CHINA00.PNG";
            highBaseForApply = iconId - iconId % 100;
            return true;
        }

        if (InRange(iconId, 10001700, 10001799))
        {
            logicalFileName = $"ITEMKOREA_ICON{sheetSuffix00to99:D2}.PNG";
            highBaseForApply = iconId - iconId % 100;
            return true;
        }

        if (InRange(iconId, 10002200, 10002299))
        {
            logicalFileName = $"ITEMCOLLABO_ICON{sheetSuffix00to99:D2}.PNG";
            highBaseForApply = iconId - iconId % 100;
            return true;
        }

        if (InRange(iconId, 10003000, 10003099))
        {
            logicalFileName = $"SKILL_ICON{sheetSuffix00to99:D2}.PNG";
            highBaseForApply = iconId - iconId % 100;
            return true;
        }

        if (InRange(iconId, 10005000, 10005099))
        {
            logicalFileName = $"NPC_ICON{sheetSuffix00to99:D2}.PNG";
            highBaseForApply = iconId - iconId % 100;
            return true;
        }

        if (InRange(iconId, 10002100, 10002199))
        {
            logicalFileName = $"ITEMBEAUTY_ICON{sheetSuffix00to99:D2}.PNG";
            highBaseForApply = iconId - iconId % 100;
            return true;
        }

        logicalFileName = "";
        highBaseForApply = 0;
        return false;
    }

    private static bool InRange(int v, int min, int max) =>
        v >= min && v <= max;

    private static string ItemChinaLogicalFileName(int iconId)
    {
        // 个别 _Icon 与锚点插值不一致：显式指定 GLOBALRES 内 PNG 逻辑名（与 SPF 索引一致）。
        if (iconId == 10001401)
        {
            return "ITEMCHINA_ICON00.PNG";
        }

        if (iconId == 10001410)
        {
            return "ITEMETC_ICON CHINA_00.PNG";
        }

        if (iconId == 10001422)
        {
            return "ITEMCHINA_ICON13.PNG";
        }

        if (iconId == 10001423)
        {
            return "ITEMCHINA_ICON14.PNG";
        }

        if (iconId == 10001440)
        {
            return "ITEMCHINA_ICON20.PNG";
        }

        if (iconId == 10001441)
        {
            return "ITEMCHINA_ICON21.PNG";
        }

        if (iconId == 10001458)
        {
            return "ITEMCHINA_ICON35.PNG";
        }

        if (iconId == 10001463)
        {
            return "ITEMCHINA_ICON39.PNG";
        }

        if (iconId == 10001464)
        {
            return "ITEMCHINA_ICON40.PNG";
        }

        var rem = iconId % 100;
        var n = InterpolateItemChinaPngSuffix(rem);
        n = Math.Clamp(n, 1, 99);
        return $"ITEMCHINA_ICON{n:D2}.PNG";
    }

    private static int InterpolateItemChinaPngSuffix(int rem)
    {
        var k = ItemChinaKnots;
        if (rem <= k[0].Rem)
        {
            return k[0].PngSuffix;
        }

        if (rem >= k[^1].Rem)
        {
            return k[^1].PngSuffix;
        }

        for (var i = 0; i < k.Length - 1; i++)
        {
            var (r0, f0) = k[i];
            var (r1, f1) = k[i + 1];
            if (rem < r0 || rem > r1)
            {
                continue;
            }

            if (r1 == r0)
            {
                return f0;
            }

            return (int)Math.Round(f0 + (rem - r0) * (f1 - f0) / (double)(r1 - r0));
        }

        return k[^1].PngSuffix;
    }
}
