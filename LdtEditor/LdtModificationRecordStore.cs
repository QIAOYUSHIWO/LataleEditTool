using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

internal static class LdtCellValueComparer
{
    public static bool ValuesEqual(object? x, object? y)
    {
        if (x is null && y is null)
        {
            return true;
        }

        if (x is null || y is null)
        {
            return false;
        }

        if (x is float fx && y is float fy)
        {
            return fx.Equals(fy) || (float.IsNaN(fx) && float.IsNaN(fy));
        }

        return Equals(x, y);
    }
}

internal sealed class LdtFileIdentity
{
    public string FullPath { get; init; } = "";
    public long Length { get; init; }
    public string LastWriteUtc { get; init; } = "";
}

internal sealed class LdtModificationChangeDto
{
    public int R { get; init; }
    public int C { get; init; }
    public int T { get; init; }
    public string V { get; init; } = "";
}

internal sealed class LdtModificationRecordFile
{
    public int V { get; init; } = 1;
    public LdtFileIdentity Identity { get; init; } = new();
    public List<LdtModificationChangeDto> Changes { get; init; } = new();
}

internal static class LdtModificationRecordStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string GetChangeRecordsDirectory()
    {
        var dir = Path.Combine(
          Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
          "LdtEditor",
          "change-records");
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static string ComputeSidecarPath(string ldtFullPath)
    {
        var key = ComputeFileKey(ldtFullPath);
        return Path.Combine(GetChangeRecordsDirectory(), key + ".json");
    }

    public static string ComputeFileKey(string ldtFullPath)
    {
        var norm = Path.GetFullPath(ldtFullPath).ToUpperInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(norm));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static LdtFileIdentity BuildIdentity(string ldtFullPath)
    {
        var full = Path.GetFullPath(ldtFullPath);
        var fi = new FileInfo(full);
        return new LdtFileIdentity
        {
            FullPath = full,
            Length = fi.Exists ? fi.Length : 0,
            LastWriteUtc = fi.Exists ? fi.LastWriteTimeUtc.ToString("o", CultureInfo.InvariantCulture) : ""
        };
    }

    public static bool IdentityMatchesDisk(LdtFileIdentity identity, string ldtFullPath)
    {
        if (string.IsNullOrWhiteSpace(identity.FullPath))
        {
            return false;
        }

        var full = Path.GetFullPath(ldtFullPath);
        if (!string.Equals(Path.GetFullPath(identity.FullPath), full, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!File.Exists(full))
        {
            return false;
        }

        var fi = new FileInfo(full);
        if (fi.Length != identity.Length)
        {
            return false;
        }

        if (string.IsNullOrEmpty(identity.LastWriteUtc))
        {
            return true;
        }

        if (!DateTime.TryParse(identity.LastWriteUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var storedUtc))
        {
            return false;
        }

        return fi.LastWriteTimeUtc == storedUtc.ToUniversalTime();
    }

    public static bool TryLoad(
      string ldtFullPath,
      IReadOnlyList<int> columnTypes,
      out Dictionary<(int Row, int Col), object?> changes,
      out string? mismatchReason)
    {
        changes = new Dictionary<(int Row, int Col), object?>();
        mismatchReason = null;
        var path = ComputeSidecarPath(ldtFullPath);
        if (!File.Exists(path))
        {
            return true;
        }

        try
        {
            var json = File.ReadAllText(path);
            var doc = JsonSerializer.Deserialize<LdtModificationRecordFile>(json, JsonOptions);
            if (doc?.Identity is null || doc.Changes is null)
            {
                mismatchReason = "sidecar 格式无效";
                return false;
            }

            if (!IdentityMatchesDisk(doc.Identity, ldtFullPath))
            {
                mismatchReason = "修改记录与当前文件不一致";
                return false;
            }

            foreach (var ch in doc.Changes)
            {
                if (ch.C < 0 || ch.C >= columnTypes.Count)
                {
                    continue;
                }

                if (!TryParseStoredValue(columnTypes[ch.C], ch.T, ch.V, out var parsed))
                {
                    continue;
                }

                changes[(ch.R, ch.C)] = parsed;
            }

            return true;
        }
        catch (Exception ex)
        {
            mismatchReason = ex.Message;
            return false;
        }
    }

    public static bool TrySave(
      string ldtFullPath,
      IReadOnlyList<int> columnTypes,
      IReadOnlyDictionary<(int Row, int Col), object?> changes,
      out Exception? error)
    {
        error = null;
        var sidecarPath = ComputeSidecarPath(ldtFullPath);
        if (changes.Count == 0)
        {
            return TryDelete(ldtFullPath, out error);
        }

        var list = new List<LdtModificationChangeDto>(changes.Count);
        foreach (var kv in changes)
        {
            var (row, col) = kv.Key;
            if (col < 0 || col >= columnTypes.Count)
            {
                continue;
            }

            var cellType = columnTypes[col];
            if (!TryFormatStoredValue(cellType, kv.Value, out var typeTag, out var text))
            {
                continue;
            }

            list.Add(new LdtModificationChangeDto
            {
                R = row,
                C = col,
                T = typeTag,
                V = text
            });
        }

        var payload = new LdtModificationRecordFile
        {
            V = 1,
            Identity = BuildIdentity(ldtFullPath),
            Changes = list
        };

        try
        {
            var json = JsonSerializer.Serialize(payload, JsonOptions);
            return TryAtomicWriteUtf8NoBom(sidecarPath, json, out error);
        }
        catch (Exception ex)
        {
            error = ex;
            return false;
        }
    }

    public static bool TryDelete(string ldtFullPath, out Exception? error)
    {
        error = null;
        var sidecarPath = ComputeSidecarPath(ldtFullPath);
        try
        {
            if (File.Exists(sidecarPath))
            {
                File.Delete(sidecarPath);
            }

            return true;
        }
        catch (Exception ex)
        {
            error = ex;
            return false;
        }
    }

    private static bool TryFormatStoredValue(int cellType, object? value, out int typeTag, out string text)
    {
        typeTag = cellType;
        text = cellType switch
        {
            0 => Convert.ToString(value ?? 0u, CultureInfo.InvariantCulture) ?? "0",
            1 => value as string ?? value?.ToString() ?? "",
            2 => (value is int bi && bi != 0) || value is bool bb && bb ? "1" : "0",
            3 => Convert.ToString(value ?? 0, CultureInfo.InvariantCulture) ?? "0",
            4 => Convert.ToString(value ?? 0f, CultureInfo.InvariantCulture) ?? "0",
            _ => value?.ToString() ?? ""
        };
        return cellType is >= 0 and <= 4;
    }

    private static bool TryParseStoredValue(int cellType, int typeTag, string raw, out object? parsed)
    {
        parsed = null;
        if (typeTag != cellType)
        {
            return false;
        }

        switch (cellType)
        {
            case 0:
                if (uint.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var u))
                {
                    parsed = u;
                    return true;
                }

                return false;
            case 1:
                parsed = raw;
                return true;
            case 2:
                if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bi) && bi is 0 or 1)
                {
                    parsed = bi;
                    return true;
                }

                return false;
            case 3:
                if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var si))
                {
                    parsed = si;
                    return true;
                }

                return false;
            case 4:
                if (float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var f))
                {
                    parsed = f;
                    return true;
                }

                return false;
            default:
                return false;
        }
    }

    private static bool TryAtomicWriteUtf8NoBom(string path, string contents, out Exception? error)
    {
        error = null;
        var tmp = path + ".new.tmp";
        try
        {
            File.WriteAllText(tmp, contents, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            if (File.Exists(path))
            {
                File.Replace(tmp, path, null);
            }
            else
            {
                File.Move(tmp, path);
            }

            return true;
        }
        catch (Exception ex)
        {
            error = ex;
            try
            {
                if (File.Exists(tmp))
                {
                    File.Delete(tmp);
                }
            }
            catch
            {
                // Ignore cleanup failure.
            }

            return false;
        }
    }
}

internal sealed class ModificationRecordsCloseForm : Form
{
    private readonly CheckBox _suppressCheck = new() { Text = "不再显示", AutoSize = true };

    public bool SuppressInFuture => _suppressCheck.Checked;

    public ModificationRecordsCloseForm()
    {
        Text = "LDT Editor";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        Width = 420;
        Height = 200;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            ColumnCount = 1,
            RowCount = 3
        };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

        var message = new Label
        {
            Dock = DockStyle.Fill,
            Text = "关闭编辑器会清理修改记录，是否保存",
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft
        };

        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 6, 0, 0)
        };
        var saveBtn = new Button { Text = "保存", AutoSize = true, DialogResult = DialogResult.Yes, Margin = new Padding(8, 0, 0, 0) };
        var skipBtn = new Button { Text = "不保存", AutoSize = true, DialogResult = DialogResult.No, Margin = new Padding(8, 0, 0, 0) };
        footer.Controls.Add(saveBtn);
        footer.Controls.Add(skipBtn);

        root.Controls.Add(message, 0, 0);
        root.Controls.Add(_suppressCheck, 0, 1);
        root.Controls.Add(footer, 0, 2);
        Controls.Add(root);
        AcceptButton = saveBtn;
        CancelButton = skipBtn;
    }
}
