using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

internal sealed class ItemExportProfile
{
    /// <summary>导出到 <c>summary</c> 的列名（LDT 内原名，如 <c>_RowId</c>）。可在 JSON 中增删，无需改代码。</summary>
    [JsonPropertyName("summaryColumns")]
    public List<string>? SummaryColumns { get; init; }

    /// <summary>除 summary 外，按列名附加的原始字段（用于后续扩展词条、Option 等）。</summary>
    [JsonPropertyName("includeRawColumns")]
    public List<string>? IncludeRawColumns { get; init; }

    /// <summary>StatusType 整型 ID → 中文词条（与客户端 STATUS 表对齐后可在此维护）。</summary>
    [JsonPropertyName("statusTypeLabels")]
    public Dictionary<string, string>? StatusTypeLabels { get; init; }
}

internal static class ItemLdtJsonExporter
{
    private static readonly Regex RequireColumnRx = new(
        @"^_Require(\d+)_(Type|ID|Value1|Value2)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>默认与 ITEM 表常用展示列一致；可被 profile.summaryColumns 整表覆盖。</summary>
    private static readonly string[] DefaultSummaryColumns =
    [
        "_RowId", "_Category", "_Num", "_ColorID", "_Name", "_Description", "_Use_Type",
        "_Icon", "_IconIndex", "_GamePrice", "_CashPrice", "_CashCheck",
        "_RareLimit", "_Type", "_SubType", "_ItemLv", "_ItemLvValue", "_SetID"
    ];

    public static void ExportToJsonFile(
        ParsedLdtTable table,
        string sourcePath,
        string outputPath,
        ItemExportProfile? profile,
        string? resolvedProfilePath)
    {
        var labelOverrides = BuildLabelOverrides(profile?.StatusTypeLabels);
        var summaryCols = profile?.SummaryColumns is { Count: > 0 }
            ? profile.SummaryColumns
            : DefaultSummaryColumns.ToList();

        var summaryIndices = ResolveColumnIndices(table, summaryCols);
        var rawExtraIndices = profile?.IncludeRawColumns is { Count: > 0 }
            ? ResolveColumnIndices(table, profile.IncludeRawColumns)
            : [];

        var descOk = ItemPreviewColumnResolver.TryResolveDescriptionColumn(table, out var descCol, out _);
        var map = ItemPreviewColumnResolver.Build(table, descOk ? descCol : -1);
        var reqSlots = DiscoverRequirementSlots(table);

        var root = new ItemExportRootDto
        {
            SourceFile = sourcePath,
            ColumnCount = table.ColumnNames.Length,
            RowCount = table.Rows.Count,
            ProfileFile = resolvedProfilePath,
            ColumnNames = table.ColumnNames.ToArray(),
            Items = new List<ItemExportRowDto>(table.Rows.Count)
        };

        for (var r = 0; r < table.Rows.Count; r++)
        {
            var row = table.Rows[r];
            root.Items.Add(BuildRow(
                r,
                row,
                table,
                map,
                summaryIndices,
                rawExtraIndices,
                reqSlots,
                labelOverrides));
        }

        var opts = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        var json = JsonSerializer.Serialize(root, opts);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        File.WriteAllText(outputPath, json);
    }

    private static Dictionary<int, string>? BuildLabelOverrides(Dictionary<string, string>? raw)
    {
        if (raw is null || raw.Count == 0)
        {
            return null;
        }

        var d = new Dictionary<int, string>();
        foreach (var kv in raw)
        {
            if (!int.TryParse(kv.Key.Trim(), out var id))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(kv.Value))
            {
                continue;
            }

            d[id] = kv.Value.Trim();
        }

        return d.Count == 0 ? null : d;
    }

    private static List<(string Name, int Index)> ResolveColumnIndices(ParsedLdtTable table, List<string> names)
    {
        var list = new List<(string, int)>();
        var seen = new HashSet<int>();
        foreach (var want in names)
        {
            var ix = TryFindColumnIndex(table, want.Trim());
            if (ix < 0 || !seen.Add(ix))
            {
                continue;
            }

            list.Add((table.ColumnNames[ix] ?? want, ix));
        }

        return list;
    }

    private static int TryFindColumnIndex(ParsedLdtTable table, string wantName)
    {
        for (var c = 0; c < table.ColumnNames.Length; c++)
        {
            if (string.Equals(table.ColumnNames[c]?.Trim(), wantName, StringComparison.OrdinalIgnoreCase))
            {
                return c;
            }
        }

        for (var c = 0; c < table.ColumnNames.Length; c++)
        {
            if (ItemPreviewColumnResolver.HeaderMatch(table.ColumnNames[c], wantName))
            {
                return c;
            }
        }

        return -1;
    }

    private sealed class RequirementSlot
    {
        public int TypeCol = -1;
        public int IdCol = -1;
        public int V1Col = -1;
        public int V2Col = -1;
    }

    private static List<(int Slot, RequirementSlot Cols)> DiscoverRequirementSlots(ParsedLdtTable table)
    {
        var bySlot = new Dictionary<int, RequirementSlot>();
        for (var c = 0; c < table.ColumnNames.Length; c++)
        {
            var n = table.ColumnNames[c]?.Trim() ?? "";
            var m = RequireColumnRx.Match(n);
            if (!m.Success)
            {
                continue;
            }

            var slot = int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            var part = m.Groups[2].Value;
            if (!bySlot.TryGetValue(slot, out var rs))
            {
                rs = new RequirementSlot();
                bySlot[slot] = rs;
            }

            switch (part.ToUpperInvariant())
            {
                case "TYPE":
                    rs.TypeCol = c;
                    break;
                case "ID":
                    rs.IdCol = c;
                    break;
                case "VALUE1":
                    rs.V1Col = c;
                    break;
                case "VALUE2":
                    rs.V2Col = c;
                    break;
            }
        }

        return bySlot
            .OrderBy(kv => kv.Key)
            .Select(kv => (kv.Key, kv.Value))
            .ToList();
    }

    private static ItemExportRowDto BuildRow(
        int parsedRowIndex,
        object?[] row,
        ParsedLdtTable table,
        ItemPreviewColumnMap map,
        List<(string Name, int Index)> summaryIndices,
        List<(string Name, int Index)> rawExtraIndices,
        List<(int Slot, RequirementSlot Cols)> reqSlots,
        IReadOnlyDictionary<int, string>? labelOverrides)
    {
        var summary = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
        foreach (var (name, ix) in summaryIndices)
        {
            summary[name] = CellToJsonNode(row, ix, table.ColumnTypes[ix]);
        }

        var extras = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
        foreach (var (name, ix) in rawExtraIndices)
        {
            if (summary.ContainsKey(name))
            {
                continue;
            }

            extras[name] = CellToJsonNode(row, ix, table.ColumnTypes[ix]);
        }

        var stats = new List<ItemExportStatDto>();
        foreach (var (tc, vc) in map.StatusPairs)
        {
            var t = CoerceCellToInt32(row, tc);
            var v = CoerceCellToInt32(row, vc);
            if (t == 0 && v == 0)
            {
                continue;
            }

            stats.Add(new ItemExportStatDto
            {
                StatusType = t,
                StatusValue = v,
                Display = ItemStatusTypeFormat.FormatLine(t, v, labelOverrides)
            });
        }

        var requirements = new List<ItemExportRequirementDto>();
        foreach (var (slot, cols) in reqSlots)
        {
            var type = cols.TypeCol >= 0 ? CoerceCellToInt32(row, cols.TypeCol) : 0;
            var id = cols.IdCol >= 0 ? CoerceCellToInt32(row, cols.IdCol) : 0;
            JsonNode? v1 = cols.V1Col >= 0 ? CellToJsonNode(row, cols.V1Col, table.ColumnTypes[cols.V1Col]) : JsonValue.Create(0);
            JsonNode? v2 = cols.V2Col >= 0 ? CellToJsonNode(row, cols.V2Col, table.ColumnTypes[cols.V2Col]) : JsonValue.Create(0);
            if (type == 0 && id == 0 && IsEmptyReqPart(v1) && IsEmptyReqPart(v2))
            {
                continue;
            }

            requirements.Add(new ItemExportRequirementDto
            {
                Slot = slot,
                RequireType = type,
                RequireId = id,
                Value1 = v1,
                Value2 = v2
            });
        }

        var flags = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
        for (var i = 0; i < 4; i++)
        {
            var col = map.FooterSlotColumns[i];
            if (col < 0)
            {
                continue;
            }

            var cn = table.ColumnNames[col] ?? $"col{col}";
            flags[cn] = CellToJsonNode(row, col, table.ColumnTypes[col]);
        }

        var rowId = CoerceCellToInt32(row, 0);

        return new ItemExportRowDto
        {
            ParsedRowIndex = parsedRowIndex,
            RowId = rowId,
            Summary = summary,
            Stats = stats,
            Requirements = requirements,
            Flags = flags,
            RawExtras = extras.Count > 0 ? extras : null
        };
    }

    private static bool IsEmptyReqPart(JsonNode? v)
    {
        if (v is null)
        {
            return true;
        }

        if (v is JsonValue jv)
        {
            if (jv.TryGetValue(out string? s))
            {
                return string.IsNullOrWhiteSpace(s) || s == "0";
            }

            if (jv.TryGetValue(out int i))
            {
                return i == 0;
            }

            if (jv.TryGetValue(out long l))
            {
                return l == 0;
            }
        }

        return false;
    }

    private static JsonNode? CellToJsonNode(object?[] row, int col, int colType)
    {
        if (col < 0 || col >= row.Length)
        {
            return null;
        }

        var cell = row[col];
        if (cell is null)
        {
            return null;
        }

        return cell switch
        {
            int i => JsonValue.Create(i),
            uint u => JsonValue.Create((long)u),
            long l => JsonValue.Create(l),
            float f => JsonValue.Create(f),
            double d => JsonValue.Create(d),
            bool b => JsonValue.Create(b),
            string s => JsonValue.Create(s),
            _ => JsonValue.Create(cell.ToString())
        };
    }

    private static int CoerceCellToInt32(object?[] row, int col)
    {
        if (col < 0 || col >= row.Length)
        {
            return 0;
        }

        return CoerceCellToInt32(row[col]);
    }

    private static int CoerceCellToInt32(object? cell) =>
        cell switch
        {
            int i => i,
            uint u => (int)u,
            long l => (int)Math.Clamp(l, int.MinValue, int.MaxValue),
            float f => (int)f,
            double d => (int)d,
            bool b => b ? 1 : 0,
            _ => int.TryParse(cell?.ToString(), out var p) ? p : 0
        };
}

internal sealed class ItemExportRootDto
{
    public string SourceFile { get; init; } = "";
    public int ColumnCount { get; init; }
    public int RowCount { get; init; }
    public string? ProfileFile { get; init; }
    public string[] ColumnNames { get; init; } = [];
    public List<ItemExportRowDto> Items { get; init; } = new();
}

internal sealed class ItemExportRowDto
{
    public int ParsedRowIndex { get; init; }
    public int RowId { get; init; }
    public Dictionary<string, JsonNode?> Summary { get; init; } = new();
    public List<ItemExportStatDto> Stats { get; init; } = new();
    public List<ItemExportRequirementDto> Requirements { get; init; } = new();
    public Dictionary<string, JsonNode?> Flags { get; init; } = new();
    public Dictionary<string, JsonNode?>? RawExtras { get; init; }
}

internal sealed class ItemExportStatDto
{
    public int StatusType { get; init; }
    public int StatusValue { get; init; }
    public string Display { get; init; } = "";
}

internal sealed class ItemExportRequirementDto
{
    public int Slot { get; init; }
    public int RequireType { get; init; }
    public int RequireId { get; init; }
    public JsonNode? Value1 { get; init; }
    public JsonNode? Value2 { get; init; }
}
