using System.Buffers;
using System.Drawing.Drawing2D;
using System.Threading;

internal static class SpfIconIndexCache
{
    private static readonly object Gate = new();
    private static string? _path;
    private static long _cachedWriteTicksUtc;
    private static List<SpfPngEntry>? _chain;
    private static Dictionary<string, SpfPngEntry>? _byName;

    private static long s_pngChainStartOffset;
    private static int s_chainScanMaxEntries = 4000;
    private static int s_chainScanMaxBytesPerEntry = 256 * 1024;

    public static void SetLiteralChainScanLimits(int maxEntries, int maxBytesPerEntry)
    {
        maxEntries = Math.Clamp(maxEntries, 100, 100_000);
        maxBytesPerEntry = Math.Clamp(maxBytesPerEntry, 4096, 4 * 1024 * 1024);
        lock (Gate)
        {
            s_chainScanMaxEntries = maxEntries;
            s_chainScanMaxBytesPerEntry = maxBytesPerEntry;
        }
    }

    /// <summary>链扫描起始偏移（BANX 等布局）。变更后须由主程序调用 <see cref="Invalidate"/> 以重建索引。</summary>
    public static void SetPngChainStartOffset(long startOffset)
    {
        lock (Gate)
        {
            s_pngChainStartOffset = Math.Max(0, startOffset);
        }
    }

    public static void Invalidate()
    {
        IconAtlasBitmapCache.Clear();
        lock (Gate)
        {
            _path = null;
            _cachedWriteTicksUtc = 0;
            _chain = null;
            _byName = null;
        }
    }

    public static bool TryEnsureLoaded(string spfPath, IProgress<string>? progress, out List<SpfPngEntry> chain, out Dictionary<string, SpfPngEntry> byName, out string? error)
    {
        chain = [];
        byName = new Dictionary<string, SpfPngEntry>(StringComparer.OrdinalIgnoreCase);
        error = null;
        if (string.IsNullOrWhiteSpace(spfPath) || !File.Exists(spfPath))
        {
            error = "SPF 路径无效或文件不存在。";
            return false;
        }

        long writeTicksUtc;
        try
        {
            writeTicksUtc = File.GetLastWriteTimeUtc(spfPath).Ticks;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }

        lock (Gate)
        {
            if (string.Equals(_path, spfPath, StringComparison.OrdinalIgnoreCase)
                && _chain is not null
                && _byName is not null
                && _cachedWriteTicksUtc == writeTicksUtc)
            {
                chain = _chain;
                byName = _byName;
                return true;
            }
        }

        try
        {
            progress?.Report("正在扫描 SPF 中的 PNG…");
            var scanned = SpfPngArchive.ScanPngChain(spfPath, s_pngChainStartOffset, progress);
            progress?.Report("正在提取内嵌图标文件名…");
            var names = SpfPngArchive.BuildLogicalNameIndex(spfPath, scanned, progress);
            lock (Gate)
            {
                _path = spfPath;
                _cachedWriteTicksUtc = writeTicksUtc;
                _chain = scanned;
                _byName = names;
                chain = scanned;
                byName = names;
            }

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            lock (Gate)
            {
                _path = null;
                _cachedWriteTicksUtc = 0;
                _chain = null;
                _byName = null;
            }

            return false;
        }
    }

    public static bool TryIsSheetPresent(
        string spfPath,
        string logicalFileName,
        IReadOnlyDictionary<string, SpfPngEntry> nameMap)
    {
        var key = logicalFileName.Trim();
        if (key.Length == 0)
        {
            return false;
        }

        if (nameMap.ContainsKey(key.ToUpperInvariant()))
        {
            return true;
        }

        var dir = Path.GetDirectoryName(spfPath);
        if (string.IsNullOrEmpty(dir))
        {
            return false;
        }

        return File.Exists(Path.Combine(dir, "DATA", "GLOBALRES", key));
    }

    public static bool TryGetAvailableSheetSuffixes(
        int iconId,
        string spfPath,
        IReadOnlyDictionary<string, SpfPngEntry> nameMap,
        out int[] suffixesOrdered)
    {
        suffixesOrdered = [];
        if (!LaTaleIconSheet.IconIdUsesSheetSuffixInStoredIconId(iconId))
        {
            return false;
        }

        var list = new List<int>(16);
        for (var s = 0; s < 100; s++)
        {
            if (!LaTaleIconSheet.TryResolveSheet(iconId, s, out var logical, out _))
            {
                continue;
            }

            if (TryIsSheetPresent(spfPath, logical, nameMap))
            {
                list.Add(s);
            }
        }

        if (list.Count == 0)
        {
            return false;
        }

        suffixesOrdered = list.ToArray();
        return true;
    }

    public static int SnapSuffixToAvailable(int desired, IReadOnlyList<int> available)
    {
        if (available.Count == 0)
        {
            return Math.Clamp(desired, 0, 99);
        }

        if (available.Contains(desired))
        {
            return desired;
        }

        var best = available[0];
        var bestDist = Math.Abs(desired - best);
        for (var i = 1; i < available.Count; i++)
        {
            var s = available[i];
            var dist = Math.Abs(desired - s);
            if (dist < bestDist || (dist == bestDist && s < best))
            {
                best = s;
                bestDist = dist;
            }
        }

        return best;
    }

    /// <summary>标准 <c>*_ICONdd.PNG</c> 后缀图集：禁止用链序下标冒充缺失文件。</summary>
    private static bool IsBlindChainIndexIconSheet(string logicalUpper) =>
        logicalUpper.EndsWith(".PNG", StringComparison.Ordinal)
        && !logicalUpper.StartsWith("ITEMCHINA_ICON", StringComparison.Ordinal)
        && !logicalUpper.StartsWith("ITEMBATTLE_CHINA", StringComparison.Ordinal)
        && !string.Equals(logicalUpper, "542042520.PNG", StringComparison.Ordinal)
        && !string.Equals(logicalUpper, "UI_ICON_TYPE.PNG", StringComparison.Ordinal)
        && !string.Equals(logicalUpper, "ITEMJAPAN_ICON00.PNG", StringComparison.Ordinal)
        && !logicalUpper.StartsWith("ITEMETC_ICON CHINA_", StringComparison.Ordinal)
        && logicalUpper.Contains("_ICON", StringComparison.Ordinal);

    public static bool TryLoadSheetPngBytes(
        string spfPath,
        string logicalFileName,
        IReadOnlyList<SpfPngEntry> chain,
        IReadOnlyDictionary<string, SpfPngEntry> nameMap,
        out byte[] pngBytes,
        out string? error)
    {
        pngBytes = [];
        error = null;
        var key = logicalFileName.Trim();
        var keyUpper = key.ToUpperInvariant();
        if (nameMap.TryGetValue(keyUpper, out var ent))
        {
            if (SpfPngArchive.TryGetPngBytes(spfPath, ent, out pngBytes))
            {
                return true;
            }

            error = "无法从 SPF 读取 PNG 数据。";
            return false;
        }

        var dir = Path.GetDirectoryName(spfPath);
        if (!string.IsNullOrEmpty(dir))
        {
            var loose = Path.Combine(dir, "DATA", "GLOBALRES", key);
            if (File.Exists(loose))
            {
                try
                {
                    pngBytes = File.ReadAllBytes(loose);
                    return true;
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                    return false;
                }
            }
        }

        if (IsBlindChainIndexIconSheet(keyUpper))
        {
            error = $"在 SPF 与 DATA\\GLOBALRES 中均未找到：{key}";
            return false;
        }

        if (TryLoadByChainHeuristic(spfPath, keyUpper, chain, out pngBytes))
        {
            return true;
        }

        error = $"在 SPF 与 DATA\\GLOBALRES 中均未找到：{key}";
        return false;
    }

    /// <summary>
    /// 当内嵌名索引未命中且无 GLOBALRES 时：按链序 + 文件名后缀推断（ITEMCHINA 等映射非链序，显式排除）。
    /// </summary>
    private static bool TryLoadByChainHeuristic(string spfPath, string logicalUpper, IReadOnlyList<SpfPngEntry> chain, out byte[] pngBytes)
    {
        pngBytes = [];
        if (chain.Count == 0)
        {
            return false;
        }

        // ITEMCHINA 与链序不一致，不能按 ICON 下标映射；在名索引为空时扫描每帧 PNG 是否内嵌该逻辑名（与 542042520 链扫类似）。
        if (logicalUpper.StartsWith("ITEMCHINA_ICON", StringComparison.Ordinal)
            && logicalUpper.EndsWith(".PNG", StringComparison.Ordinal))
        {
            const int prefixLen = 14; // "ITEMCHINA_ICON".Length
            if (logicalUpper.Length == prefixLen + 2 + 4)
            {
                var d = logicalUpper.AsSpan(prefixLen, 2);
                if (char.IsAsciiDigit(d[0]) && char.IsAsciiDigit(d[1]))
                {
                    Span<byte> literal = stackalloc byte[logicalUpper.Length];
                    for (var i = 0; i < logicalUpper.Length; i++)
                    {
                        literal[i] = (byte)logicalUpper[i];
                    }

                    return TryScanChainForAsciiLiteral(spfPath, chain, literal, out pngBytes);
                }
            }

            return false;
        }

        if (string.Equals(logicalUpper, "542042520.PNG", StringComparison.Ordinal))
        {
            return TryScanChainForAsciiLiteral(spfPath, chain, "542042520.PNG"u8, out pngBytes);
        }

        if (string.Equals(logicalUpper, "UI_ICON_TYPE.PNG", StringComparison.Ordinal))
        {
            return TryScanChainForAsciiLiteral(spfPath, chain, "UI_ICON_TYPE.PNG"u8, out pngBytes);
        }

        if (string.Equals(logicalUpper, "ITEMJAPAN_ICON00.PNG", StringComparison.Ordinal))
        {
            return SpfPngArchive.TryGetPngBytes(spfPath, chain[0], out pngBytes);
        }

        // ITEMBATTLE_CHINA##：链序下标与文件名末两位一致（00→chain[0]，02→chain[2]）。
        const string battleChinaPrefix = "ITEMBATTLE_CHINA";
        if (logicalUpper.StartsWith(battleChinaPrefix, StringComparison.Ordinal)
            && logicalUpper.EndsWith(".PNG", StringComparison.Ordinal)
            && logicalUpper.Length == battleChinaPrefix.Length + 2 + 4)
        {
            var bc0 = logicalUpper[battleChinaPrefix.Length];
            var bc1 = logicalUpper[battleChinaPrefix.Length + 1];
            if (char.IsAsciiDigit(bc0) && char.IsAsciiDigit(bc1))
            {
                var battleChinaChainIdx = (bc0 - '0') * 10 + (bc1 - '0');
                if (battleChinaChainIdx < chain.Count)
                {
                    return SpfPngArchive.TryGetPngBytes(spfPath, chain[battleChinaChainIdx], out pngBytes);
                }
            }

            return false;
        }

        const string chinaPrefix = "ITEMETC_ICON CHINA_";
        if (logicalUpper.StartsWith(chinaPrefix, StringComparison.Ordinal)
            && logicalUpper.EndsWith(".PNG", StringComparison.Ordinal)
            && logicalUpper.Length == chinaPrefix.Length + 2 + 4)
        {
            var digits = logicalUpper.AsSpan(chinaPrefix.Length, 2);
            if (char.IsAsciiDigit(digits[0]) && char.IsAsciiDigit(digits[1])
                && int.TryParse(digits, out var cn)
                && cn is >= 0 and <= 99)
            {
                var idx = cn == 0 ? 0 : cn - 1;
                if (idx < chain.Count)
                {
                    return SpfPngArchive.TryGetPngBytes(spfPath, chain[idx], out pngBytes);
                }
            }

            return false;
        }

        return false;
    }

    private static bool TryScanChainForAsciiLiteral(string spfPath, IReadOnlyList<SpfPngEntry> chain, ReadOnlySpan<byte> literal, out byte[] pngBytes)
    {
        pngBytes = [];
        var limit = Math.Min(chain.Count, s_chainScanMaxEntries);
        for (var i = 0; i < limit; i++)
        {
            var e = chain[i];
            if (e.ByteLength <= 0)
            {
                continue;
            }

            var n = Math.Min(e.ByteLength, s_chainScanMaxBytesPerEntry);
            var buf = ArrayPool<byte>.Shared.Rent(n);
            try
            {
                using var fs = new FileStream(spfPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.SequentialScan);
                if (e.FileOffset < 0 || e.FileOffset + n > fs.Length)
                {
                    continue;
                }

                fs.Position = e.FileOffset;
                if (fs.Read(buf, 0, n) != n)
                {
                    continue;
                }

                if (!ContainsAsciiIgnoreCase(buf.AsSpan(0, n), literal))
                {
                    continue;
                }

                return SpfPngArchive.TryGetPngBytes(spfPath, e, out pngBytes);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buf, clearArray: false);
            }
        }

        return false;
    }

    private static bool ContainsAsciiIgnoreCase(ReadOnlySpan<byte> haystack, ReadOnlySpan<byte> needle)
    {
        if (needle.Length == 0)
        {
            return true;
        }

        if (haystack.Length < needle.Length)
        {
            return false;
        }

        for (var i = 0; i <= haystack.Length - needle.Length; i++)
        {
            var ok = true;
            for (var j = 0; j < needle.Length; j++)
            {
                var a = haystack[i + j];
                var b = needle[j];
                var ua = a is >= (byte)'a' and <= (byte)'z' ? (byte)(a - 32) : a;
                var ub = b is >= (byte)'a' and <= (byte)'z' ? (byte)(b - 32) : b;
                if (ua != ub)
                {
                    ok = false;
                    break;
                }
            }

            if (ok)
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// Picks a cell inside an ITEMETC-style atlas; writes <see cref="LaTaleIconSheet.IconColumnName"/> and
/// <see cref="LaTaleIconSheet.IconIndexColumnName"/> (1-based index).
/// </summary>
internal sealed class IconAtlasPickerForm : Form
{
    private readonly string _spfPath;
    private readonly Action<int, int> _onApply;

    private int _contextIconId;
    private int _contextIndexOneBased;

    private int _reloadEpoch;

    /// <summary>大于 0 表示正在异步加载图集，画布显示占位文案。</summary>
    private int _atlasReloadDepth;

    /// <summary>最近一次成功解码的图集指纹；<see cref="int.MinValue"/> 表示无效，用于跳过重复解码。</summary>
    private int _lastAtlasIconId = int.MinValue;
    private int _lastAtlasIconIx;
    private int _lastAtlasSuffix = int.MinValue;

    private bool _suppressSuffixSelectorReload;

    private int[] _availableSuffixes = [];

    public int BoundGridRow { get; private set; }
    public int BoundTableColIcon { get; private set; }
    public int BoundTableColIndex { get; private set; }

    private readonly ComboBox _suffixCombo = new() { Width = 56, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Label _sheetLabel = new() { AutoSize = true, Text = "图集序号：" };
    private readonly AtlasPanel _canvas = new() { Dock = DockStyle.Fill, AutoScroll = true };

    private Bitmap? _atlas;
    private string? _atlasBitmapLeaseKey;

    private int _cell;
    private int _cols;
    private int _rows;
    private int _pickedIndex = -1;
    private Pen? _pickOutlinePen;

    /// <summary>屏上每格边长（像素）；加载图集后按窗口与行列重算，与 <see cref="TryHit"/> 一致。</summary>
    private int _displayCellPixels = 36;

    /// <summary>格与格之间的间隙像素。</summary>
    private const int CellSpacingPixels = 2;

    private const int TopBarHeight = 32;
    private const int ChromeVertical = TopBarHeight;
    private const int MinDisplayCellPixels = 16;
    private const int MaxDisplayCellPixels = 38;
    private const int MinClientWidthFallback = 360;
    private const int ScreenFitMargin = 40;

    /// <summary>标题栏与上下边框的保守预留，使 <see cref="ClientSize"/> 不会顶出 <see cref="Screen.WorkingArea"/>。</summary>
    private const int NonClientHeightSlack = 56;

    public IconAtlasPickerForm(
        string spfArchivePath,
        int boundGridRow,
        int boundTableColIcon,
        int boundTableColIndex,
        int contextIconId,
        int contextIndexOneBased,
        Action<int, int> onApply)
    {
        _spfPath = spfArchivePath.Trim();
        BoundGridRow = boundGridRow;
        BoundTableColIcon = boundTableColIcon;
        BoundTableColIndex = boundTableColIndex;
        _contextIconId = contextIconId;
        _contextIndexOneBased = contextIndexOneBased;
        _onApply = onApply;

        Text = $"图标 · {_contextIconId}";
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ControlBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        KeyPreview = true;
        KeyDown += FormOnKeyDown;

        var top = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = TopBarHeight,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(6, 4, 6, 4)
        };
        top.Controls.Add(_sheetLabel);
        top.Controls.Add(_suffixCombo);
        _suffixCombo.SelectedIndexChanged += SuffixComboOnSelectedIndexChanged;
        _suffixCombo.MouseWheel += SuffixComboOnMouseWheel;

        Controls.Add(_canvas);
        Controls.Add(top);

        ConfigureSuffixSelectorForIcon(_contextIconId);

        Shown += async (_, _) => await ReloadAtlasAsync().ConfigureAwait(true);
        _canvas.MouseClick += CanvasOnMouseClick;
        _canvas.MouseDoubleClick += CanvasOnMouseDoubleClick;
        _canvas.Paint += CanvasOnPaint;
        _canvas.Scroll += (_, _) => _canvas.Invalidate();
        RefreshPickPen();
    }

    private void DetachAtlasBitmap()
    {
        if (_atlasBitmapLeaseKey is not null)
        {
            IconAtlasBitmapCache.Release(_atlasBitmapLeaseKey);
            _atlasBitmapLeaseKey = null;
            _atlas = null;
        }
        else
        {
            _atlas?.Dispose();
            _atlas = null;
        }
    }

    private static Bitmap DecodeAtlasFromPngBytes(byte[] payload)
    {
        using var ms = new MemoryStream(payload, writable: false);
        var img = Image.FromStream(ms, useEmbeddedColorManagement: false, validateImageData: false);
        if (img is Bitmap bm)
        {
            return bm;
        }

        try
        {
            return new Bitmap(img);
        }
        finally
        {
            img.Dispose();
        }
    }

    private long GetSpfWriteTicksUtc()
    {
        try
        {
            return File.GetLastWriteTimeUtc(_spfPath).Ticks;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>主窗换行后刷新绑定与图集；仅内存中替换一张位图，配合世代丢弃过期异步结果，无额外常驻大分配。</summary>
    public void ApplyRowFromMain(int gridRowIndex, int tableColIcon, int tableColIndex, int iconId, int iconIndexOneBased)
    {
        if (gridRowIndex == BoundGridRow
            && tableColIcon == BoundTableColIcon
            && tableColIndex == BoundTableColIndex
            && iconId == _contextIconId
            && iconIndexOneBased == _contextIndexOneBased)
        {
            return;
        }

        if (iconId == _contextIconId
            && iconIndexOneBased == _contextIndexOneBased
            && tableColIcon == BoundTableColIcon
            && tableColIndex == BoundTableColIndex)
        {
            BoundGridRow = gridRowIndex;
            return;
        }

        BoundGridRow = gridRowIndex;
        BoundTableColIcon = tableColIcon;
        BoundTableColIndex = tableColIndex;
        _contextIconId = iconId;
        _contextIndexOneBased = iconIndexOneBased;
        ResetSuffixComboForNewIconContext();
        _suppressSuffixSelectorReload = true;
        try
        {
            ConfigureSuffixSelectorForIcon(iconId);
        }
        finally
        {
            _suppressSuffixSelectorReload = false;
        }

        var suf = GetSelectedSheetSuffix();
        if (_atlas is not null
            && iconId == _lastAtlasIconId
            && suf == _lastAtlasSuffix)
        {
            if (iconIndexOneBased != _lastAtlasIconIx)
            {
                _lastAtlasIconIx = iconIndexOneBased;
                SyncPickedIndexFromContext();
                if (LaTaleIconSheet.TryResolveSheet(_contextIconId, suf, out var logicalT, out _))
                {
                    Text = $"图标 · {_contextIconId} · {_cols}×{_rows} · {logicalT}";
                }

                _canvas.Invalidate();
            }

            return;
        }

        _ = ReloadAtlasAsync();
    }

    private void SuffixComboOnSelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_suppressSuffixSelectorReload)
        {
            return;
        }

        InvalidateAtlasFingerprint();
        _ = ReloadAtlasAsync();
    }

    private void SuffixComboOnMouseWheel(object? sender, MouseEventArgs e)
    {
        if (!LaTaleIconSheet.IconIdUsesSheetSuffixInStoredIconId(_contextIconId)
            || _availableSuffixes.Length <= 1
            || _suffixCombo.Items.Count == 0)
        {
            return;
        }

        var idx = _suffixCombo.SelectedIndex;
        if (idx < 0)
        {
            idx = 0;
        }

        var step = e.Delta > 0 ? -1 : 1;
        var newIdx = Math.Clamp(idx + step, 0, _availableSuffixes.Length - 1);
        if (newIdx == idx)
        {
            return;
        }

        _suffixCombo.SelectedIndex = newIdx;
    }

    private void ResetSuffixComboForNewIconContext()
    {
        _availableSuffixes = [];
        _suffixCombo.Items.Clear();
        _suffixCombo.SelectedIndex = -1;
    }

    private int GetSelectedSheetSuffix()
    {
        if (_availableSuffixes.Length > 0
            && _suffixCombo.SelectedIndex >= 0
            && _suffixCombo.SelectedIndex < _availableSuffixes.Length)
        {
            return _availableSuffixes[_suffixCombo.SelectedIndex];
        }

        return LaTaleIconSheet.DefaultSheetSuffixForIconId(_contextIconId);
    }

    private void RefreshSuffixComboFromSpf(IReadOnlyDictionary<string, SpfPngEntry> nameMap, int preferredSuffix)
    {
        _suppressSuffixSelectorReload = true;
        try
        {
            if (!LaTaleIconSheet.IconIdUsesSheetSuffixInStoredIconId(_contextIconId))
            {
                _availableSuffixes = [];
                _suffixCombo.Items.Clear();
                _suffixCombo.Enabled = false;
                _suffixCombo.Visible = false;
                _sheetLabel.Visible = false;
                return;
            }

            _suffixCombo.Visible = true;
            _sheetLabel.Visible = true;
            if (!SpfIconIndexCache.TryGetAvailableSheetSuffixes(_contextIconId, _spfPath, nameMap, out var available))
            {
                _availableSuffixes = [];
                _suffixCombo.Items.Clear();
                _suffixCombo.Enabled = false;
                return;
            }

            _availableSuffixes = available;
            var snap = SpfIconIndexCache.SnapSuffixToAvailable(preferredSuffix, available);

            var needRebuild = _suffixCombo.Items.Count != available.Length;
            if (!needRebuild)
            {
                for (var i = 0; i < available.Length; i++)
                {
                    var text = available[i].ToString("D2", System.Globalization.CultureInfo.InvariantCulture);
                    if (!string.Equals(_suffixCombo.Items[i]?.ToString(), text, StringComparison.Ordinal))
                    {
                        needRebuild = true;
                        break;
                    }
                }
            }

            if (needRebuild)
            {
                _suffixCombo.Items.Clear();
                foreach (var s in available)
                {
                    _suffixCombo.Items.Add(s.ToString("D2", System.Globalization.CultureInfo.InvariantCulture));
                }
            }

            var pickIdx = Array.IndexOf(available, snap);
            _suffixCombo.SelectedIndex = pickIdx >= 0 ? pickIdx : 0;
            _suffixCombo.Enabled = available.Length > 1;
        }
        finally
        {
            _suppressSuffixSelectorReload = false;
        }
    }

    private void ConfigureSuffixSelectorForIcon(int iconId)
    {
        if (LaTaleIconSheet.IsItemChinaIconRange(iconId)
            || !LaTaleIconSheet.IconIdUsesSheetSuffixInStoredIconId(iconId))
        {
            _suffixCombo.Enabled = false;
            _suffixCombo.Visible = false;
            _sheetLabel.Visible = false;
            _availableSuffixes = [];
            _suffixCombo.Items.Clear();
        }
        else
        {
            _suffixCombo.Visible = true;
            _sheetLabel.Visible = true;
            _suffixCombo.Enabled = false;
        }
    }

    private void InvalidateAtlasFingerprint()
    {
        _lastAtlasIconId = int.MinValue;
        _lastAtlasSuffix = int.MinValue;
    }

    private void RecordAtlasFingerprint(int suffix)
    {
        _lastAtlasIconId = _contextIconId;
        _lastAtlasIconIx = _contextIndexOneBased;
        _lastAtlasSuffix = suffix;
    }

    private void SyncPickedIndexFromContext()
    {
        if (_atlas is null)
        {
            return;
        }

        var maxIdx = _cols * _rows - 1;
        var zeroBased = Math.Max(0, _contextIndexOneBased - LaTaleIconSheet.IconIndexFirstValue);
        _pickedIndex = Math.Clamp(zeroBased, 0, Math.Max(0, maxIdx));
    }

    private void FormOnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            e.SuppressKeyPress = true;
            Close();
            return;
        }

        if (e.Shift && e.KeyCode == Keys.A)
        {
            e.SuppressKeyPress = true;
            Close();
        }
    }

    private void RefreshPickPen()
    {
        _pickOutlinePen?.Dispose();
        var penW = Math.Clamp(_displayCellPixels / 6f, 2f, 5f);
        _pickOutlinePen = new Pen(Color.FromArgb(255, 60, 255, 80), penW);
    }

    /// <summary>按屏幕工作区缩放到可一屏容纳；过大图集用最小格 + 滚动条，窗口仍固定不可拉伸。</summary>
    private void ApplyFitWindowToAtlas()
    {
        if (!IsHandleCreated)
        {
            return;
        }

        var area = Screen.FromControl(this).WorkingArea;
        var maxClientW = Math.Max(MinClientWidthFallback, area.Width - ScreenFitMargin - 12);
        var maxClientH = Math.Max(
            ChromeVertical + MinDisplayCellPixels * 2 + 80,
            area.Height - ScreenFitMargin - NonClientHeightSlack);
        var availCanvasH = maxClientH - ChromeVertical;

        var maxCellByW = maxClientW / Math.Max(1, _cols);
        var maxCellByH = availCanvasH / Math.Max(1, _rows);
        var rawFit = Math.Min(maxCellByW, maxCellByH);
        var needScroll = rawFit < MinDisplayCellPixels;

        if (!needScroll)
        {
            _displayCellPixels = Math.Clamp(rawFit, MinDisplayCellPixels, MaxDisplayCellPixels);
        }
        else
        {
            _displayCellPixels = MinDisplayCellPixels;
        }

        var gridW = _cols * _displayCellPixels;
        var gridH = _rows * _displayCellPixels;

        if (!needScroll)
        {
            _canvas.AutoScroll = false;
            _canvas.AutoScrollMinSize = Size.Empty;
            ClientSize = new Size(Math.Max(MinClientWidthFallback, gridW), ChromeVertical + gridH);
        }
        else
        {
            _canvas.AutoScroll = true;
            _canvas.AutoScrollMinSize = new Size(gridW, gridH);
            ClientSize = new Size(maxClientW, maxClientH);
        }

        MinimumSize = Size.Empty;
        MaximumSize = Size.Empty;
        var locked = Size;
        MinimumSize = locked;
        MaximumSize = locked;
        RefreshPickPen();
    }

    private async Task ReloadAtlasAsync()
    {
        if (IsDisposed)
        {
            return;
        }

        var epoch = Interlocked.Increment(ref _reloadEpoch);

        if (_spfPath.Length == 0 || !File.Exists(_spfPath))
        {
            MessageBox.Show(
                this,
                "请先在「功能菜单 (Ctrl+F)」→「选项」中配置有效的 SPF 路径。",
                Text,
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        Text = $"图标 · {_contextIconId} · 加载…";

        List<SpfPngEntry>? spfChain = null;
        Dictionary<string, SpfPngEntry>? spfMap = null;
        string? spfLoadErr = null;
        try
        {
            await Task.Run(() =>
            {
                if (!SpfIconIndexCache.TryEnsureLoaded(
                        _spfPath,
                        new Progress<string>(s =>
                        {
                            try
                            {
                                if (!IsDisposed && IsHandleCreated && Volatile.Read(ref _reloadEpoch) == epoch)
                                {
                                    BeginInvoke(() =>
                                    {
                                        if (Volatile.Read(ref _reloadEpoch) == epoch)
                                        {
                                            Text = $"图标 · {_contextIconId} · {s}";
                                        }
                                    });
                                }
                            }
                            catch
                            {
                                // ignore if form is closing
                            }
                        }),
                        out spfChain,
                        out spfMap,
                        out spfLoadErr))
                {
                    spfChain = null;
                    spfMap = null;
                }
            }).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            spfLoadErr = ex.Message;
        }

        if (IsDisposed || Volatile.Read(ref _reloadEpoch) != epoch)
        {
            return;
        }

        if (spfChain is null || spfMap is null)
        {
            MessageBox.Show(this, spfLoadErr ?? "无法加载 SPF 索引。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Text = $"图标 · {_contextIconId}";
            return;
        }

        var preferredSuffix = GetSelectedSheetSuffix();
        RefreshSuffixComboFromSpf(spfMap, preferredSuffix);

        if (_availableSuffixes.Length == 0
            && LaTaleIconSheet.IconIdUsesSheetSuffixInStoredIconId(_contextIconId))
        {
            Text = $"图标 · {_contextIconId}";
            return;
        }

        var suffix = GetSelectedSheetSuffix();
        if (!LaTaleIconSheet.TryResolveSheet(_contextIconId, suffix, out var logical, out _))
        {
            MessageBox.Show(this, "无法解析图集文件名（当前 _Icon 编码区间未配置）。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            Text = $"图标 · {_contextIconId}";
            return;
        }

        var ticksUtc = GetSpfWriteTicksUtc();
        var logicalU = logical.ToUpperInvariant();

        if (Volatile.Read(ref _reloadEpoch) == epoch
            && IconAtlasBitmapCache.TryAddRef(_spfPath, logicalU, ticksUtc, out var cached, out var leaseKey, out var cachedCellStride))
        {
            if (Volatile.Read(ref _reloadEpoch) != epoch)
            {
                IconAtlasBitmapCache.Release(leaseKey);
            }
            else
            {
                DetachAtlasBitmap();
                InvalidateAtlasFingerprint();
                _canvas.AutoScrollMinSize = Size.Empty;
                _atlas = cached;
                _atlasBitmapLeaseKey = leaseKey;
                var nominal = Math.Max(8, LaTaleIconSheet.AtlasCellPixels);
                _cell = cachedCellStride > 0 ? cachedCellStride : nominal;
                _cols = Math.Max(1, _atlas.Width / _cell);
                _rows = Math.Max(1, _atlas.Height / _cell);
                ApplyFitWindowToAtlas();
                SyncPickedIndexFromContext();
                Text = $"图标 · {_contextIconId} · {_cols}×{_rows} · {logical}";
                RecordAtlasFingerprint(suffix);
                _canvas.Invalidate();
                return;
            }
        }

        DetachAtlasBitmap();
        InvalidateAtlasFingerprint();
        _canvas.AutoScrollMinSize = Size.Empty;

        Interlocked.Increment(ref _atlasReloadDepth);
        try
        {
            _canvas.Invalidate();

            UseWaitCursor = true;
            byte[]? payload = null;
            string? err = null;
            var chain = spfChain;
            var map = spfMap;
            try
            {
                await Task.Run(() =>
                {
                    if (Volatile.Read(ref _reloadEpoch) != epoch)
                    {
                        return;
                    }

                    if (!SpfIconIndexCache.TryLoadSheetPngBytes(_spfPath, logical, chain, map, out var bytes, out err))
                    {
                        return;
                    }

                    if (Volatile.Read(ref _reloadEpoch) != epoch)
                    {
                        return;
                    }

                    payload = bytes;
                }).ConfigureAwait(true);
            }
            finally
            {
                UseWaitCursor = false;
            }

            if (IsDisposed || Volatile.Read(ref _reloadEpoch) != epoch)
            {
                return;
            }

            if (payload is null)
            {
                MessageBox.Show(this, err ?? "加载失败。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                Text = $"图标 · {_contextIconId}";
                return;
            }

            try
            {
                var bmp = DecodeAtlasFromPngBytes(payload);
                if (Volatile.Read(ref _reloadEpoch) != epoch)
                {
                    bmp.Dispose();
                    return;
                }

                bmp = IconAtlasDecodeHelper.MaybeDownscaleDecodedAtlas(bmp, out var decodeScale);
                var nominalCell = Math.Max(8, LaTaleIconSheet.AtlasCellPixels);
                var cellStride = Math.Max(8, (int)Math.Round(nominalCell * decodeScale));

                _ = IconAtlasBitmapCache.TryInsert(_spfPath, logicalU, ticksUtc, bmp, cellStride, out var decodedAtlas, out var insertLeaseKey);
                ArgumentNullException.ThrowIfNull(decodedAtlas);
                _atlas = decodedAtlas;
                _atlasBitmapLeaseKey = insertLeaseKey;

                _cell = cellStride;
                _cols = Math.Max(1, _atlas.Width / _cell);
                _rows = Math.Max(1, _atlas.Height / _cell);
                ApplyFitWindowToAtlas();
                SyncPickedIndexFromContext();
                Text = $"图标 · {_contextIconId} · {_cols}×{_rows} · {logical}";
                RecordAtlasFingerprint(suffix);
                _canvas.Invalidate();
            }
            catch (Exception ex)
            {
                if (Volatile.Read(ref _reloadEpoch) == epoch)
                {
                    MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    Text = $"图标 · {_contextIconId}";
                    InvalidateAtlasFingerprint();
                    DetachAtlasBitmap();
                }
            }
        }
        finally
        {
            Interlocked.Decrement(ref _atlasReloadDepth);
            try
            {
                if (IsHandleCreated && !IsDisposed)
                {
                    BeginInvoke(() =>
                    {
                        if (!IsDisposed)
                        {
                            _canvas.Invalidate();
                        }
                    });
                }
            }
            catch
            {
                // ignore if form is tearing down
            }
        }
    }

    private void CanvasOnPaint(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        if (_atlas is null)
        {
            g.Clear(Color.FromArgb(48, 48, 52));
            if (Volatile.Read(ref _atlasReloadDepth) > 0)
            {
                var r = _canvas.ClientRectangle;
                TextRenderer.DrawText(
                    g,
                    "加载图集中…",
                    Font,
                    r,
                    Color.FromArgb(200, 200, 205),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            }

            return;
        }

        g.Clear(Color.FromArgb(48, 48, 52));
        g.SmoothingMode = SmoothingMode.None;
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;

        var pt = _canvas.AutoScrollPosition;
        var clip = e.ClipRectangle;
        var cellPx = _displayCellPixels;
        var firstC = Math.Max(0, (clip.Left - pt.X) / cellPx);
        var firstR = Math.Max(0, (clip.Top - pt.Y) / cellPx);
        var lastC = Math.Min(_cols - 1, (clip.Right - 1 - pt.X) / cellPx);
        var lastR = Math.Min(_rows - 1, (clip.Bottom - 1 - pt.Y) / cellPx);
        if (firstC > lastC || firstR > lastR)
        {
            return;
        }

        var inner = cellPx - CellSpacingPixels;
        for (var r = firstR; r <= lastR; r++)
        {
            for (var c = firstC; c <= lastC; c++)
            {
                var i = r * _cols + c;
                var dx = c * cellPx + pt.X;
                var dy = r * cellPx + pt.Y;
                var dest = new Rectangle(dx, dy, inner, inner);
                var sx = c * _cell;
                var sy = r * _cell;
                var src = new Rectangle(sx, sy, _cell, _cell);
                g.DrawImage(_atlas, dest, src, GraphicsUnit.Pixel);
                if (i == _pickedIndex && _pickOutlinePen is not null)
                {
                    var outline = dest;
                    outline.Inflate(2, 2);
                    g.DrawRectangle(_pickOutlinePen, outline);
                }
            }
        }
    }

    private void CanvasOnMouseClick(object? sender, MouseEventArgs e)
    {
        if (_atlas is null || e.Button != MouseButtons.Left)
        {
            return;
        }

        if (TryHit(e.Location, out var idx))
        {
            _pickedIndex = idx;
            _canvas.Invalidate();
        }
    }

    private void CanvasOnMouseDoubleClick(object? sender, MouseEventArgs e)
    {
        if (_atlas is null || e.Button != MouseButtons.Left)
        {
            return;
        }

        if (!TryHit(e.Location, out var idx))
        {
            return;
        }

        var suffix = GetSelectedSheetSuffix();
        if (!LaTaleIconSheet.TryResolveSheet(_contextIconId, suffix, out _, out var highBase))
        {
            return;
        }

        var newId = LaTaleIconSheet.IconIdUsesSheetSuffixInStoredIconId(_contextIconId)
            ? highBase + suffix
            : _contextIconId;
        var indexOneBased = idx + LaTaleIconSheet.IconIndexFirstValue;
        _pickedIndex = idx;
        _canvas.Invalidate();
        _onApply(newId, indexOneBased);
    }

    private bool TryHit(Point client, out int index)
    {
        index = -1;
        if (_atlas is null)
        {
            return false;
        }

        var pt = _canvas.AutoScrollPosition;
        var x = client.X - pt.X;
        var y = client.Y - pt.Y;
        if (x < 0 || y < 0)
        {
            return false;
        }

        var c = x / _displayCellPixels;
        var r = y / _displayCellPixels;
        if (c < 0 || r < 0 || c >= _cols || r >= _rows)
        {
            return false;
        }

        index = r * _cols + c;
        return true;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Interlocked.Increment(ref _reloadEpoch);
            InvalidateAtlasFingerprint();
            DetachAtlasBitmap();
            _pickOutlinePen?.Dispose();
            _pickOutlinePen = null;
        }

        base.Dispose(disposing);
    }

    private sealed class AtlasPanel : Panel
    {
        public AtlasPanel()
        {
            DoubleBuffered = true;
            AutoScroll = true;
            BackColor = Color.FromArgb(48, 48, 52);
        }
    }
}
