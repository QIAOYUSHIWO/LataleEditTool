using System.Buffers.Binary;
using System.Globalization;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
// 单文件等宿主下尽早触发代码页注册，避免首次解析在后台线程才触碰 GBK/CP949 时出现异常。
_ = Encoding.GetEncoding(936);
_ = Encoding.GetEncoding(949);

if (!ProcessBootstrap.IsGuiLaunch(args))
    ProcessBootstrap.TryAttachParentConsole();

if (args.Length >= 1 && string.Equals(args[0], "spf-probe", StringComparison.OrdinalIgnoreCase))
{
    var path = args.Length >= 2
        ? args[1]
        : @"F:\LataleS4\LataleS4(9999)\Latale\BANX.SPF";
    Console.WriteLine($"SPF: {path}");
    var chain = SpfPngArchive.ScanPngChain(path);
    var names = SpfPngArchive.BuildLogicalNameIndex(path, chain);
    Console.WriteLine($"PNG chain: {chain.Count}, name map: {names.Count}");
    foreach (var kv in names.Take(30))
    {
        Console.WriteLine($"  {kv.Key} @ {kv.Value.FileOffset}");
    }

    return;
}

if (args.Length >= 2 && string.Equals(args[0], "ldt-probe-columns", StringComparison.OrdinalIgnoreCase))
{
    var path = Path.GetFullPath(args[1]);
    if (!File.Exists(path))
    {
        Console.WriteLine($"File not found: {path}");
        return;
    }

    var data = File.ReadAllBytes(path);
    var table = MainForm.ParseTypedTableFromBytes(data, LdtTextDecodePreference.Auto);
    Console.WriteLine($"Columns: {table.ColumnNames.Length}, Rows: {table.Rows.Count}");
    for (var i = 0; i < table.ColumnNames.Length; i++)
    {
        Console.WriteLine($"{i}\t{table.ColumnTypes[i]}\t{table.ColumnNames[i]}");
    }

    const int wantId = 211000311;
    for (var r = 0; r < table.Rows.Count; r++)
    {
        var row = table.Rows[r];
        if (row.Length == 0)
        {
            continue;
        }

        if (MainForm.CoerceCellToInt32ForProbe(row[0]) == wantId)
        {
            Console.WriteLine($"--- row index {r}, _RowId={row[0]} ---");
            for (var c = 0; c < Math.Min(row.Length, table.ColumnNames.Length); c++)
            {
                var v = row[c];
                var s = v is string str ? str : v?.ToString() ?? "";
                if (s.Length > 120)
                {
                    s = s[..120] + "…";
                }

                Console.WriteLine($"  [{c}] {table.ColumnNames[c]} = {s}");
            }

            break;
        }
    }

    return;
}

if (args.Length >= 3 && string.Equals(args[0], "ldt-export-items", StringComparison.OrdinalIgnoreCase))
{
    var ldtPath = Path.GetFullPath(args[1]);
    var outPath = Path.GetFullPath(args[2]);
    if (!File.Exists(ldtPath))
    {
        Console.WriteLine($"File not found: {ldtPath}");
        return;
    }

    string? profilePath = null;
    if (args.Length >= 4)
    {
        profilePath = Path.GetFullPath(args[3]);
        if (!File.Exists(profilePath))
        {
            Console.WriteLine($"Profile not found: {profilePath}");
            return;
        }
    }
    else
    {
        var besideOut = Path.Combine(Path.GetDirectoryName(outPath) ?? ".", "item-export-profile.json");
        if (File.Exists(besideOut))
        {
            profilePath = besideOut;
        }
    }

    ItemExportProfile? profile = null;
    if (profilePath is not null)
    {
        try
        {
            var profileJson = File.ReadAllText(profilePath);
            profile = JsonSerializer.Deserialize<ItemExportProfile>(profileJson);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to read profile: {ex.Message}");
            return;
        }
    }

    Console.WriteLine($"Reading LDT: {ldtPath}");
    var data = File.ReadAllBytes(ldtPath);
    var table = MainForm.ParseTypedTableFromBytes(data, LdtTextDecodePreference.Auto);
    Console.WriteLine($"Rows: {table.Rows.Count}, Columns: {table.ColumnNames.Length}");
    Console.WriteLine($"Writing: {outPath}");
    ItemLdtJsonExporter.ExportToJsonFile(table, ldtPath, outPath, profile, profilePath);
    Console.WriteLine("Done.");
    return;
}

if (args.Length >= 2 && string.Equals(args[0], "ldt-roundtrip", StringComparison.OrdinalIgnoreCase))
{
    var ldtPath = Path.GetFullPath(args[1]);
    if (!File.Exists(ldtPath))
    {
        Console.Error.WriteLine($"File not found: {ldtPath}");
        Environment.Exit(2);
    }

    var saveEnc = LdtSaveStringEncoding.Gbk;
    if (args.Length >= 3 && string.Equals(args[2], "cp949", StringComparison.OrdinalIgnoreCase))
    {
        saveEnc = LdtSaveStringEncoding.Cp949;
    }

    var data = File.ReadAllBytes(ldtPath);
    var decode = LdtTextDecodePreference.Auto;
    var t1 = MainForm.ParseTypedTableFromBytes(data, decode);
    var rowEnc = saveEnc == LdtSaveStringEncoding.Cp949
        ? Encoding.GetEncoding(949)
        : Encoding.GetEncoding(936);
    var (patched, _, _) = MainForm.PatchTypedLdtBytes(data, t1, rowEnc);
    var t2 = MainForm.ParseTypedTableFromBytes(patched, decode);
    if (!MainForm.TypedTableDataEquals(t1, t2))
    {
        Console.Error.WriteLine("Roundtrip semantic mismatch (parse → patch unchanged → parse).");
        Environment.Exit(1);
    }

    Console.WriteLine($"OK ldt-roundtrip rows={t1.Rows.Count} cols={t1.ColumnNames.Length} saveEnc={saveEnc}");
    return;
}

if (args.Length == 0 || string.Equals(args[0], "gui", StringComparison.OrdinalIgnoreCase))
{
    var launch = args.Length >= 2 ? args[1] : null;
    RunGuiOnStaThread(launch);
    return;
}

if (args.Length >= 1
    && File.Exists(args[0])
    && args[0].EndsWith(".ldt", StringComparison.OrdinalIgnoreCase))
{
    RunGuiOnStaThread(args[0]);
    return;
}

Console.WriteLine(
    "Usage: LdtEditor [gui] [file.ldt] | file.ldt | spf-probe [path-to.spf] | ldt-probe-columns path.ldt | "
    + "ldt-export-items path.ldt out.json [profile.json] | ldt-roundtrip path.ldt [gbk|cp949]");

static void RunGuiOnStaThread(string? openLdtPath = null)
{
    Exception? guiError = null;
    var thread = new Thread(() =>
    {
        try
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm(openLdtPath));
        }
        catch (Exception ex)
        {
            guiError = ex;
        }
    });

    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();

    if (guiError is not null)
    {
        throw new InvalidOperationException("GUI failed to start.", guiError);
    }
}

internal static class ProcessBootstrap
{
    private const uint AttachParentProcess = 0xFFFF_FFFFu;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(uint dwProcessId);

    internal static bool IsGuiLaunch(string[] args)
    {
        if (args.Length == 0)
        {
            return true;
        }

        if (string.Equals(args[0], "gui", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (File.Exists(args[0]) && args[0].EndsWith(".ldt", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    /// <summary>WinExe 无自有控制台时，挂到从 cmd/PowerShell 启动时的父控制台，便于子命令输出与退出码。</summary>
    internal static void TryAttachParentConsole() => _ = AttachConsole(AttachParentProcess);
}

internal sealed record ParsedLdtTable(
    string[] ColumnNames,
    int[] ColumnTypes,
    List<object?[]> Rows,
    int DataStartOffset,
    int DataEndOffset);

internal sealed record EditorSnapshot(List<object?[]> Rows, bool IsDirty);
internal sealed record FilterToolState(
    int X,
    int Y,
    bool AllColumns,
    bool MatchCase,
    bool UseRegex = false,
    string Keyword = "",
    int ActiveColumnIndex = -1,
    string ReplaceText = "",
    bool ReplaceAllColumns = false,
    bool ReplaceMatchCase = false,
    bool ReplaceUseRegex = false,
    string ReplaceKeyword = "",
    string BatchInsertStart = "0",
    string BatchInsertStep = "1",
    bool BatchInsertUseMultiply = false,
    string BatchInsertPrefix = "",
    string BatchInsertSuffix = "",
    int BatchInsertOriginalPlacement = 0,
    bool BatchInsertVisibleRowsOnly = false,
    bool? BatchInsertPreviewBeforeApply = null,
    int BatchInsertBoolRule = 0);

internal enum LdtTextDecodePreference
{
    Auto,
    Gbk,
    Cp949,
    Utf8
}

internal enum LdtAutoDecodeWinner
{
    Empty,
    Gbk,
    Cp949,
    Utf8
}

internal static class LdtStringDecodeHelper
{
    public static int CountReplacementChars(string s)
    {
        var count = 0;
        foreach (var ch in s)
        {
            if (ch == '\uFFFD' || ch == '?')
            {
                count++;
            }
        }

        return count;
    }

    public static string TruncatePreview(string s, int maxChars)
    {
        if (string.IsNullOrEmpty(s))
        {
            return string.Empty;
        }

        var oneLine = s.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
        return oneLine.Length <= maxChars ? oneLine : oneLine[..maxChars] + "…";
    }
}

internal static class LdtChineseScriptHelper
{
    private const uint LocaleSystemDefault = 0x0800;
    private const uint LcmapSimplifiedChinese = 0x02000000;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int LCMapStringW(uint Locale, uint dwMapFlags, string lpSrcStr, int cchSrc, StringBuilder? lpDestStr, int cchDest);

    public static string TraditionalToSimplified(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        var required = LCMapStringW(LocaleSystemDefault, LcmapSimplifiedChinese, text, text.Length, null, 0);
        if (required <= 0)
        {
            return text;
        }

        var dest = new StringBuilder(required);
        var written = LCMapStringW(LocaleSystemDefault, LcmapSimplifiedChinese, text, text.Length, dest, required);
        return written > 0 ? dest.ToString(0, written) : text;
    }
}

internal enum LdtSaveStringEncoding
{
    Gbk,
    Cp949
}

internal sealed record EditorAppSettings(
    bool RequireReplacePreview,
    LdtTextDecodePreference TextDecode,
    LdtSaveStringEncoding SaveEncoding,
    string GridFontFamily,
    float GridFontSizePoints,
    string SpfArchivePath = "",
    bool ShowItemDescriptionPanel = false,
    bool ShowQuickFindBar = false,
    bool BackupBeforeOverwrite = true,
    bool AutoPersistModificationRecords = false,
    bool SuppressModificationRecordsClosePrompt = false,
    bool ModificationTrackerVisible = false,
    string ReleaseAnnouncementsUrl = "",
    int IconAtlasCacheMaxEntries = 12,
    int SpfChainLiteralScanMaxEntries = 4000,
    int SpfChainLiteralScanMaxKiBPerEntry = 256,
    int SpfPngNameScanMaxKiB = 512,
    int IconAtlasDecodeMaxMegapixels = 0,
    long SpfPngChainStartOffset = 0)
{
    public static EditorAppSettings Default { get; } = new(
        RequireReplacePreview: false,
        TextDecode: LdtTextDecodePreference.Auto,
        SaveEncoding: LdtSaveStringEncoding.Gbk,
        GridFontFamily: "",
        GridFontSizePoints: 0f,
        SpfArchivePath: "",
        ShowItemDescriptionPanel: false,
        ShowQuickFindBar: false,
        BackupBeforeOverwrite: true,
        AutoPersistModificationRecords: false,
        SuppressModificationRecordsClosePrompt: false,
        ModificationTrackerVisible: false,
        ReleaseAnnouncementsUrl: "",
        IconAtlasCacheMaxEntries: 12,
        SpfChainLiteralScanMaxEntries: 4000,
        SpfChainLiteralScanMaxKiBPerEntry: 256,
        SpfPngNameScanMaxKiB: 512,
        IconAtlasDecodeMaxMegapixels: 0,
        SpfPngChainStartOffset: 0);
}

internal sealed class EditorToolOptionsBinding
{
    public required EditorAppSettings Initial { get; init; }
    public required Action<EditorAppSettings> Apply { get; init; }
}

internal sealed record ReplacePreviewEntry(int Row, int Column, string ColumnLabel, string OldText, string NewText, object Parsed);

/// <summary>
/// Intercepts Ctrl+C / Ctrl+V while a cell is being edited. Otherwise the hosted editor has focus and
/// <see cref="DataGridView"/> handles the shortcut first (row copy in full-row select mode).
/// </summary>
internal sealed class LdtDataGridView : DataGridView
{
    public Action? OnEditingClipboardCopy;
    public Action? OnEditingClipboardPaste;

    public LdtDataGridView()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (IsCurrentCellInEditMode && CurrentCell is { ColumnIndex: > 0 })
        {
            if ((keyData & Keys.Control) == Keys.Control && (keyData & Keys.KeyCode) == Keys.C)
            {
                OnEditingClipboardCopy?.Invoke();
                return true;
            }

            if ((keyData & Keys.Control) == Keys.Control && (keyData & Keys.KeyCode) == Keys.V)
            {
                OnEditingClipboardPaste?.Invoke();
                return true;
            }
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }
}

internal sealed class MainForm : Form, IMessageFilter
{
    private readonly LdtDataGridView _grid = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = false,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        MultiSelect = true,
        RowHeadersVisible = false
    };

    private readonly ToolStripMenuItem _contextCopyItem = new("复制\tCtrl+C");
    private readonly ToolStripMenuItem _contextPasteItem = new("粘贴\tCtrl+V");
    private readonly ToolStripMenuItem _pickIconFromSpfItem = new("从 SPF 选择图标…");

    private readonly StatusStrip _statusStrip = new() { Dock = DockStyle.Bottom, SizingGrip = false };
    private readonly ToolStripStatusLabel _statusLabel = new() { Text = "Rows: -" };
    private readonly ToolStripStatusLabel _globalHotKeyStatusLabel = new()
    {
        AutoSize = true,
        Text = "",
        TextAlign = ContentAlignment.MiddleLeft
    };
    private readonly ToolStripStatusLabel _selectionCountLabel = new() { Text = "已选中: 0 行" };
    private readonly ToolStripStatusLabel _historyLabel = new() { Text = "Undo:0 | Redo:0" };
    private readonly ToolStripStatusLabel _diskPersistHintLabel = new() { Text = "", AutoSize = true };
    private readonly ToolStripStatusLabel _iconPickerBindHintLabel = new() { Text = "", Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly ToolStripStatusLabel _selectionModeLabel = new() { IsLink = true };
    private readonly ContextMenuStrip _cellContextMenu = new();
    private bool _rowSelectionMode = true;
    private byte[]? _data;
    private string? _filePath;
    private ParsedLdtTable? _parsedTable;
    private bool _isDirty;
    private readonly Dictionary<(int ParsedRow, int Col), object?> _modChanges = new();
    private bool _modTrackerVisible;
    private (int ParsedRow, int Col)? _hoverTrackedCell;
    private ToolStripMenuItem? _menuModificationTrackerItem;
    private readonly System.Windows.Forms.Timer _modPersistDebounceTimer = new() { Interval = 650 };
    private static readonly Color ModHighlightBackColor = Color.FromArgb(255, 236, 179);
    private static readonly Color ModHighlightSelectionBackColor = Color.FromArgb(255, 214, 130);
    private readonly Stack<EditorSnapshot> _undoStack = new();
    private readonly Stack<EditorSnapshot> _redoStack = new();
    private bool _suppressHistory;
    private FilterToolForm? _filterToolForm;
    private string _filterKeyword = string.Empty;
    private int _activeFilterColumnIndex = -1;
    private bool _filterAllColumns;
    private bool _filterMatchCase;
    private bool _filterUseRegex;
    private string _batchInsertStartText = "0";
    private string _batchInsertStepText = "1";
    private bool _batchInsertUseMultiply;
    private string _batchInsertPrefix = "";
    private string _batchInsertSuffix = "";
    private int _batchInsertOriginalPlacement;
    private bool _batchInsertVisibleRowsOnly;
    private bool _batchInsertPreviewBeforeApply = true;
    private int _batchInsertBoolRule;
    private string _replaceKeyword = string.Empty;
    private string _filterReplaceText = string.Empty;
    private bool _replaceMatchCase;
    private int _replaceSearchRow = -1;
    private int _replaceSearchColumn = -1;
    private Point? _filterToolLocation;
    private EditorAppSettings _editorSettings = EditorAppSettings.Default;
    private (long ChainStart, int NameScanKiB)? _appliedIconStructuralTuning;
    private int _appliedIconDecodeMegapixels = int.MinValue;
    private Font? _gridDisplayFont;
    private IconAtlasPickerForm? _iconPicker;
    private string? _openPathOnLaunch;

    private readonly MenuStrip _mainMenu = new();
    private readonly SplitContainer _splitMain = new()
    {
        Dock = DockStyle.Fill,
        Orientation = Orientation.Vertical,
        FixedPanel = FixedPanel.Panel2,
        SplitterWidth = 5
    };
    private readonly Panel _findBarPanel = new() { Height = 30, Dock = DockStyle.Top };
    private readonly Label _quickFindLabel = new() { Text = "查找:", AutoSize = true, Margin = new Padding(8, 6, 4, 0) };
    private readonly TextBox _quickFindBox = new() { Width = 220, Margin = new Padding(0, 4, 4, 0) };
    private readonly CheckBox _quickFindAllCols = new() { Text = "全列", AutoSize = true, Margin = new Padding(0, 2, 8, 0) };
    private readonly Button _quickFindNextBtn = new() { Text = "下一个 (F3)", AutoSize = true, Margin = new Padding(0, 2, 0, 0) };
    private readonly ToolStripMenuItem _menuRecentFiles = new("最近文件(&R)");
    private readonly ToolStripMenuItem _menuPinCurrentFile = new("钉选当前文件到最近(&P)") { Enabled = false };
    private ToolStripMenuItem? _menuViewPreviewItem;
    private ToolStripMenuItem? _menuViewFindBarItem;
    private int _descPreviewColumnIndex = -1;
    private ItemPreviewFloatForm? _itemPreviewForm;
    private ItemPreviewColumnMap _itemPreviewColumnMap = new();
    private bool _mainFormClosing;

    /// <summary>虚拟模式下：显示行下标 → <see cref="ParsedLdtTable.Rows"/> 下标（筛选后子集）。</summary>
    private readonly List<int> _visibleParsedRows = new();

    /// <summary>列头排序：按 <see cref="_sortDataColumnIndex"/>（-1 表示「#」源行号列）排序当前可见行。</summary>
    private bool _sortActive;
    private int _sortDataColumnIndex;
    private bool _sortAscending = true;

    private static readonly JsonSerializerOptions EditorSettingsJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly System.Windows.Forms.Timer _openTitleTimer = new();
    private readonly System.Windows.Forms.Timer _iconPickerSyncDebounceTimer = new() { Interval = 110 };
    private readonly System.Windows.Forms.Timer _diskPersistHintTimer = new() { Interval = 14000 };
    private string? _diskPersistHintThrottleKey;
    private long _diskPersistHintThrottleTick;
    private readonly object _openTitleLock = new();
    private int _openTitleCur;
    private int _openTitleTotal;
    private bool _openTitleBytesMode;

    private static readonly Encoding sLdtGbk = Encoding.GetEncoding(936);
    private static readonly Encoding sLdtCp949 = Encoding.GetEncoding(949);

    private const int WmSetRedraw = 11;
    private const int WmActivateApp = 0x001C;
    private const int WmHotKey = 0x0312;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModNorepeat = 0x4000;
    private const int HotKeyIdInsert = 1;
    private const int HotKeyIdShiftP = 2;
    private const int HotKeyIdShiftA = 3;

    [DllImport("user32.dll", EntryPoint = "SendMessageW", ExactSpelling = true)]
    private static extern nint SendMessageW(nint hWnd, int msg, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(nint hWnd, int id);

    /// <summary>系统级热键注册成功时，由 <see cref="WmHotKey"/> 处理，避免与 <see cref="MainForm_KeyDown"/> 重复触发。</summary>
    private bool _hotKeyInsertRegistered;
    private bool _hotKeyShiftPRegistered;
    private bool _hotKeyShiftARegistered;

    /// <summary>
    /// 本 UI 线程所属进程是否处于前台（WM_ACTIVATEAPP）。用于在「焦点在功能菜单等附属顶层面」时仍能释放 RegisterHotKey，
    /// 使多开的另一进程可独占 Ctrl+F / Shift+P / Shift+A。
    /// </summary>
    private bool _appThreadInForegroundForHotKeys;

    private readonly System.Windows.Forms.Timer _globalHotKeyRetryTimer = new() { Interval = 90 };
    private int _globalHotKeyRetryRemaining;
    private bool _hotKeyActivateAppMessageFilterAdded;

    public MainForm(string? openPathOnLaunch = null)
    {
        _openPathOnLaunch = openPathOnLaunch;
        try
        {
            if (Icon.ExtractAssociatedIcon(Application.ExecutablePath) is { } appIcon)
                Icon = (Icon)appIcon.Clone();
        }
        catch
        {
            /* 设计时或异常路径下保留系统默认 */
        }

        Width = 1080;
        Height = 760;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        KeyPreview = true;
        AllowDrop = true;
        Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

        _grid.AllowUserToOrderColumns = true;
        _grid.AllowUserToResizeRows = false;
        _grid.AllowUserToResizeColumns = true;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        _grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        _grid.ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableWithoutHeaderText;
        EnableDoubleBuffering(_grid);
        _grid.OnEditingClipboardCopy = () => TryCopyCurrentEditingCell();
        _grid.OnEditingClipboardPaste = () => TryPasteToCurrentEditingCell();
        _grid.VirtualMode = true;
        _grid.ShowCellToolTips = true;
        _grid.AllowDrop = true;
        _grid.CellValueNeeded += MainDataGridView_CellValueNeeded;
        _grid.CellValuePushed += MainDataGridView_CellValuePushed;
        _grid.CellFormatting += MainDataGridView_CellFormatting;
        _grid.CellToolTipTextNeeded += MainDataGridView_CellToolTipTextNeeded;
        _grid.CellMouseMove += GridCellMouseMove;
        _grid.CellMouseLeave += (_, _) => _hoverTrackedCell = null;
        InitializeContextMenu();

        _statusStrip.Items.Add(_statusLabel);
        _statusStrip.Items.Add(new ToolStripStatusLabel { Text = " | " });
        _statusStrip.Items.Add(_selectionCountLabel);
        _statusStrip.Items.Add(new ToolStripStatusLabel { Text = " | " });
        _statusStrip.Items.Add(_historyLabel);
        _statusStrip.Items.Add(new ToolStripStatusLabel { Text = " | " });
        _statusStrip.Items.Add(_globalHotKeyStatusLabel);
        _statusStrip.Items.Add(new ToolStripStatusLabel { Text = " | " });
        _statusStrip.Items.Add(_diskPersistHintLabel);
        _statusStrip.Items.Add(new ToolStripStatusLabel { Text = " | " });
        _statusStrip.Items.Add(_iconPickerBindHintLabel);
        _statusStrip.Items.Add(_selectionModeLabel);
        _statusStrip.ShowItemToolTips = true;
        _diskPersistHintTimer.Tick += (_, _) =>
        {
            _diskPersistHintTimer.Stop();
            _diskPersistHintLabel.Text = "";
            _diskPersistHintLabel.ToolTipText = "";
            _diskPersistHintLabel.ForeColor = _statusStrip.ForeColor;
        };

        _globalHotKeyRetryTimer.Tick += GlobalHotKeyRetryTimerOnTick;
        _modPersistDebounceTimer.Tick += (_, _) => TryPersistModificationRecordsIfEligible();

        _splitMain.Panel1.Controls.Add(_grid);
        _splitMain.Panel2Collapsed = true;

        var findFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(4, 2, 4, 0)
        };
        findFlow.Controls.Add(_quickFindLabel);
        findFlow.Controls.Add(_quickFindBox);
        findFlow.Controls.Add(_quickFindAllCols);
        findFlow.Controls.Add(_quickFindNextBtn);
        _findBarPanel.Controls.Add(findFlow);

        var menuFile = new ToolStripMenuItem("文件(&F)");
        menuFile.DropDownItems.Add(new ToolStripMenuItem("打开(&O)…", null, async (_, _) => await OpenFileAsync())
        {
            ShortcutKeys = Keys.Control | Keys.O,
            ShowShortcutKeys = true
        });
        menuFile.DropDownItems.Add(_menuRecentFiles);
        menuFile.DropDownItems.Add(_menuPinCurrentFile);
        _menuPinCurrentFile.Click += (_, _) => TogglePinCurrentFile();
        menuFile.DropDownItems.Add(new ToolStripSeparator());
        menuFile.DropDownItems.Add(new ToolStripMenuItem("保存(&S)", null, (_, _) => SaveToCurrentFile())
        {
            ShortcutKeys = Keys.Control | Keys.S,
            ShowShortcutKeys = true
        });
        menuFile.DropDownItems.Add(new ToolStripMenuItem("另存为(&A)…", null, (_, _) => SaveAsNewFile()));
        menuFile.DropDownItems.Add(new ToolStripSeparator());
        menuFile.DropDownItems.Add(new ToolStripMenuItem("退出(&X)", null, (_, _) => Close()));

        var menuEdit = new ToolStripMenuItem("编辑(&E)");
        menuEdit.DropDownItems.Add(new ToolStripMenuItem("撤销(&U)", null, (_, _) => Undo())
        {
            ShortcutKeys = Keys.Control | Keys.Z,
            ShowShortcutKeys = true
        });
        menuEdit.DropDownItems.Add(new ToolStripMenuItem("重做(&R)", null, (_, _) => Redo())
        {
            ShortcutKeys = Keys.Control | Keys.Y,
            ShowShortcutKeys = true
        });
        menuEdit.DropDownItems.Add(new ToolStripSeparator());
        menuEdit.DropDownItems.Add(new ToolStripMenuItem("复制(&C)", null, (_, _) => CopySelectionByMode())
        {
            ShortcutKeys = Keys.Control | Keys.C,
            ShowShortcutKeys = true
        });
        menuEdit.DropDownItems.Add(new ToolStripMenuItem("粘贴(&P)", null, (_, _) => PasteSelectionByMode())
        {
            ShortcutKeys = Keys.Control | Keys.V,
            ShowShortcutKeys = true
        });
        menuEdit.DropDownItems.Add(new ToolStripSeparator());
        menuEdit.DropDownItems.Add(new ToolStripMenuItem("繁体转简体(&T)…", null, (_, _) => OpenTraditionalToSimplifiedPreview()));
        menuEdit.DropDownItems.Add(new ToolStripSeparator());
        menuEdit.DropDownItems.Add(new ToolStripMenuItem("功能菜单(&M)…", null, (_, _) => ToggleFilterToolWindow())
        {
            ShortcutKeys = Keys.Control | Keys.F,
            ShowShortcutKeys = true
        });
        // 勿设置 ShortcutKeys = Shift|A：WinForms 菜单不接受该枚举组合，会在构造时抛 InvalidEnumArgumentException。
        // Shift+A 仍由 MainForm_KeyDown 处理。
        menuEdit.DropDownItems.Add(new ToolStripMenuItem("选图窗 (&T)…\tShift+A", null, (_, _) => ToggleIconPickerWindow()));

        var menuView = new ToolStripMenuItem("视图(&V)");
        _menuViewPreviewItem = new ToolStripMenuItem("物品说明预览\tShift+P", null, MenuToggleItemDescriptionPreview)
        {
            CheckOnClick = true
        };
        _menuViewFindBarItem = new ToolStripMenuItem("内嵌查找条(&F)", null, MenuToggleQuickFindBar)
        {
            CheckOnClick = true
        };
        menuView.DropDownItems.Add(_menuViewPreviewItem);
        menuView.DropDownItems.Add(_menuViewFindBarItem);
        _menuModificationTrackerItem = new ToolStripMenuItem("修改记录\tAlt+S", null, MenuToggleModificationTracker)
        {
            CheckOnClick = true
        };
        menuView.DropDownItems.Add(new ToolStripSeparator());
        menuView.DropDownItems.Add(_menuModificationTrackerItem);

        var menuTools = new ToolStripMenuItem("工具(&T)");
        menuTools.DropDownItems.Add(new ToolStripMenuItem("将 .LDT 关联到本程序（当前用户）(&R)…", null, (_, _) => TryRegisterLdtFileAssociation()));
        menuTools.DropDownItems.Add(new ToolStripMenuItem("将 .LDT 关联到本程序（本机所有用户，需管理员）(&M)…", null, (_, _) => TryRegisterLdtFileAssociationAllUsers()));

        var menuHelp = new ToolStripMenuItem("帮助(&H)");
        menuHelp.DropDownItems.Add(new ToolStripMenuItem("使用说明(&R)…", null, (_, _) => ShowUserReadmeDialog()));
        menuHelp.DropDownItems.Add(new ToolStripMenuItem("版本与公告(&U)…", null, async (_, _) => await ShowReleaseAnnouncementsDialogAsync().ConfigureAwait(true)));
        menuHelp.DropDownItems.Add(new ToolStripMenuItem("关于(&A)…", null, (_, _) => ShowAboutDialog()));

        _mainMenu.Items.Add(menuFile);
        _mainMenu.Items.Add(menuEdit);
        _mainMenu.Items.Add(menuView);
        _mainMenu.Items.Add(menuTools);
        _mainMenu.Items.Add(menuHelp);
        MainMenuStrip = _mainMenu;

        Controls.Add(_splitMain);
        Controls.Add(_statusStrip);
        Controls.Add(_findBarPanel);
        Controls.Add(_mainMenu);

        _grid.DragEnter += MainFormOnDragEnter;
        _grid.DragOver += MainFormOnDragOver;
        _grid.DragDrop += MainFormOnDragDrop;
        _grid.CellEndEdit += GridCellEndEdit;
        _grid.CellDoubleClick += GridCellDoubleClick;
        _grid.CellMouseDown += GridCellMouseDown;
        _grid.ColumnHeaderMouseClick += GridColumnHeaderMouseClick;
        _grid.EditingControlShowing += GridEditingControlShowing;
        _grid.SelectionChanged += (_, _) =>
        {
            UpdateSelectionCount();
            UpdateItemDescriptionPreview();
            _iconPickerSyncDebounceTimer.Stop();
            _iconPickerSyncDebounceTimer.Start();
        };
        _selectionModeLabel.Click += (_, _) => ToggleSelectionMode();
        KeyDown += MainForm_KeyDown;
        FormClosing += MainForm_FormClosing;
        DragEnter += MainFormOnDragEnter;
        DragOver += MainFormOnDragOver;
        DragDrop += MainFormOnDragDrop;
        Resize += (_, _) =>
        {
            ClampPreviewSplitter();
            SyncItemPreviewFloatBounds();
            ClampFilterToolFormToMainForm();
        };
        Move += (_, _) =>
        {
            SyncItemPreviewFloatBounds();
            ClampFilterToolFormToMainForm();
        };
        Shown += (_, _) =>
        {
            ApplyViewLayoutFromSettings();
            if (!string.IsNullOrWhiteSpace(_openPathOnLaunch))
            {
                var p = _openPathOnLaunch;
                _openPathOnLaunch = null;
                _ = LoadLdtFromPathAsync(p);
            }
        };

        _quickFindNextBtn.Click += (_, _) => RunQuickFindNext();
        _quickFindBox.KeyDown += QuickFindBoxOnKeyDown;
        var quickFindTip = new ToolTip();
        quickFindTip.SetToolTip(
            _quickFindBox,
            "Enter / F3：在可见行中查找下一处。\nCtrl+Enter：将框内文字应用为行筛选（与 Ctrl+F 查找页的大小写、正则选项一致）。");
        quickFindTip.SetToolTip(_quickFindNextBtn, "查找下一处（与主窗 F3 相同）。");

        ApplyClassicEditorTheme();
        _openTitleTimer.Interval = 40;
        _openTitleTimer.Tick += OpenTitleTimerOnTick;
        _iconPickerSyncDebounceTimer.Tick += (_, _) =>
        {
            _iconPickerSyncDebounceTimer.Stop();
            SyncIconPickerToCurrentGridRowIfOpen();
        };
        LoadFilterToolState();
        SyncQuickFindBarFromFilterState();
        LoadEditorAppSettings();
        ApplyGridFontFromSettings();
        RebuildRecentFilesMenu();
        if (_menuViewPreviewItem is not null)
        {
            _menuViewPreviewItem.Checked = _editorSettings.ShowItemDescriptionPanel;
        }

        if (_menuViewFindBarItem is not null)
        {
            _menuViewFindBarItem.Checked = _editorSettings.ShowQuickFindBar;
        }

        SetModificationTrackerVisible(_editorSettings.ModificationTrackerVisible, persistSettings: false);

        ApplyViewLayoutFromSettings();
        UpdateStatusBar();
        UpdateWindowTitle();
    }

    private async Task OpenFileAsync()
    {
        if (!ConfirmDiscardChanges())
        {
            return;
        }

        using var dialog = new OpenFileDialog
        {
            Filter = "LDT files (*.LDT)|*.LDT|All files (*.*)|*.*",
            Title = "Open LDT file"
        };
        if (dialog.ShowDialog() != DialogResult.OK)
        {
            return;
        }

        await LoadLdtFromPathAsync(dialog.FileName).ConfigureAwait(true);
    }

    private async Task OpenRecentFileAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            MessageBox.Show(this, "文件已不存在或无法访问。", "最近文件", MessageBoxButtons.OK, MessageBoxIcon.Information);
            RemoveRecentFile(path);
            RebuildRecentFilesMenu();
            return;
        }

        if (!ConfirmDiscardChanges())
        {
            return;
        }

        await LoadLdtFromPathAsync(path).ConfigureAwait(true);
    }

    private async Task LoadLdtFromPathAsync(string path)
    {
        var full = Path.GetFullPath(path);
        _filePath = full;
        CloseIconPickerIfOpen();
        _parsedTable = null;
        _isDirty = false;
        ClearModificationTracker();

        StopOpenTitleProgress();
        _openTitleTimer.Start();
        UpdateWindowTitle();

        void EndOpenProgressAndChrome()
        {
            StopOpenTitleProgress();
            UpdateStatusBar();
            UpdateWindowTitle();
        }

        try
        {
            byte[] data;
            try
            {
                data = await ReadAllBytesStreamingAsync(_filePath!, PushOpenProgressBytes).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                CloseIconPickerIfOpen();
                _grid.RowCount = 0;
                _grid.Columns.Clear();
                _visibleParsedRows.Clear();
                _data = null;
                _filePath = null;
                _descPreviewColumnIndex = -1;
                UpdateItemDescriptionPreview();
                MessageBox.Show(ex.Message, "Open Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                EndOpenProgressAndChrome();
                return;
            }

            _data = data;

            int rowCount;
            try
            {
                rowCount = PeekTypedTableRowCount(_data);
            }
            catch (Exception ex)
            {
                CloseIconPickerIfOpen();
                _grid.RowCount = 0;
                _grid.Columns.Clear();
                _visibleParsedRows.Clear();
                _data = null;
                _filePath = null;
                _descPreviewColumnIndex = -1;
                UpdateItemDescriptionPreview();
                MessageBox.Show(ex.Message, "Open Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                EndOpenProgressAndChrome();
                return;
            }

            PushOpenProgressRows(0, rowCount);

            ParsedLdtTable? parsed = null;
            Exception? parseError = null;
            // 单文件宿主等场景下，ConfigureAwait(true) 在 Task.Run 之后未必回到 UI 线程；解析后必须封送到窗体线程再碰 DataGridView。
            await Task.Run(() =>
            {
                try
                {
                    var decodePref = _editorSettings.TextDecode;
                    parsed = ParseTypedTable(_data, decodePref, PushOpenProgressRows);
                }
                catch (Exception ex)
                {
                    parseError = ex;
                }
            }).ConfigureAwait(false);

            var tcs = new TaskCompletionSource<object?>();
            void ApplyParseResultOnUiThread()
            {
                try
                {
                    if (IsDisposed)
                    {
                        return;
                    }

                    if (parseError is not null)
                    {
                        CloseIconPickerIfOpen();
                        _grid.RowCount = 0;
                        _grid.Columns.Clear();
                        _visibleParsedRows.Clear();
                        _parsedTable = null;
                        _descPreviewColumnIndex = -1;
                        UpdateItemDescriptionPreview();
                        MessageBox.Show(parseError.Message, "Open Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }

                    _parsedTable = parsed;
                    // 新开文件：沿用上次保存的筛选词易导致「无可见行」；清空查找关键词（保留全列/大小写/正则等选项）。
                    _filterKeyword = string.Empty;
                    PushOpenProgressRows(0, rowCount);
                    LoadTable(PushOpenProgressRows);
                    ResetHistory();
                    TryLoadModificationRecordsFromSidecar();
                    SyncFilterColumns();
                    SyncQuickFindBarFromFilterState();
                    SaveFilterToolState();
                    AddRecentFile(full);
                    RebuildRecentFilesMenu();
                }
                catch (Exception ex)
                {
                    CloseIconPickerIfOpen();
                    _grid.RowCount = 0;
                    _grid.Columns.Clear();
                    _visibleParsedRows.Clear();
                    _parsedTable = null;
                    _descPreviewColumnIndex = -1;
                    UpdateItemDescriptionPreview();
                    MessageBox.Show(ex.Message, "Open Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally
                {
                    EndOpenProgressAndChrome();
                    tcs.TrySetResult(null);
                }
            }

            if (InvokeRequired)
            {
                BeginInvoke(ApplyParseResultOnUiThread);
            }
            else
            {
                ApplyParseResultOnUiThread();
            }

            await tcs.Task.ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            CloseIconPickerIfOpen();
            _grid.RowCount = 0;
            _grid.Columns.Clear();
            _visibleParsedRows.Clear();
            _parsedTable = null;
            _descPreviewColumnIndex = -1;
            UpdateItemDescriptionPreview();
            MessageBox.Show(ex.Message, "Open Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            EndOpenProgressAndChrome();
        }
    }

    private void QuickFindBoxOnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter && e.Control)
        {
            ApplyQuickFindTextAsRowFilter();
            e.SuppressKeyPress = true;
            return;
        }

        if (e.KeyCode == Keys.Enter)
        {
            RunQuickFindNext();
            e.SuppressKeyPress = true;
        }
    }

    private void ApplyQuickFindTextAsRowFilter()
    {
        if (_parsedTable is null)
        {
            return;
        }

        ApplyFilterFromTool(_quickFindBox.Text.Trim(), _quickFindAllCols.Checked, _filterMatchCase, _filterUseRegex);
    }

    private void SyncQuickFindBarFromFilterState()
    {
        if (_quickFindBox.Text != _filterKeyword)
        {
            _quickFindBox.Text = _filterKeyword;
        }

        if (_quickFindAllCols.Checked != _filterAllColumns)
        {
            _quickFindAllCols.Checked = _filterAllColumns;
        }
    }

    private void RunQuickFindNext()
    {
        if (_parsedTable is null)
        {
            return;
        }

        var needle = _quickFindBox.Text.Trim();
        if (needle.Length == 0)
        {
            return;
        }

        var allCols = _quickFindAllCols.Checked;
        var colCount = _parsedTable.ColumnNames.Length;
        var vCount = _visibleParsedRows.Count;
        if (vCount == 0 || colCount == 0)
        {
            return;
        }

        var cmp = StringComparison.OrdinalIgnoreCase;
        var focusCol = 0;
        if (_grid.CurrentCell is { ColumnIndex: > 0 } cc)
        {
            focusCol = Math.Clamp(cc.ColumnIndex - 1, 0, colCount - 1);
        }

        var startParsed = 0;
        if (_grid.CurrentCell is { RowIndex: >= 0 } cur && TryGetParsedRowIndexFromGridRow(cur.RowIndex, out var pr))
        {
            startParsed = pr;
        }

        var curVi = _visibleParsedRows.IndexOf(startParsed);
        if (curVi < 0)
        {
            curVi = 0;
        }

        if (allCols)
        {
            var flatMax = vCount * colCount;
            var curFlat = curVi * colCount + focusCol;
            for (var k = 1; k <= flatMax; k++)
            {
                var f = (curFlat + k) % flatMax;
                var vi = f / colCount;
                var c = f % colCount;
                var parsedRow = _visibleParsedRows[vi];
                var txt = _parsedTable.Rows[parsedRow][c]?.ToString() ?? "";
                if (txt.Contains(needle, cmp))
                {
                    RevealAndFocusCell(parsedRow, c);
                    return;
                }
            }
        }
        else
        {
            for (var step = 1; step <= vCount; step++)
            {
                var vi = (curVi + step) % vCount;
                var parsedRow = _visibleParsedRows[vi];
                var txt = _parsedTable.Rows[parsedRow][focusCol]?.ToString() ?? "";
                if (txt.Contains(needle, cmp))
                {
                    RevealAndFocusCell(parsedRow, focusCol);
                    return;
                }
            }
        }

        MessageBox.Show(this, "未找到匹配项。", "查找", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void MainFormOnDragEnter(object? sender, DragEventArgs e)
    {
        e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private void MainFormOnDragOver(object? sender, DragEventArgs e) => MainFormOnDragEnter(sender, e);

    private async void MainFormOnDragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0)
        {
            return;
        }

        var ldt = paths.FirstOrDefault(p => p.EndsWith(".ldt", StringComparison.OrdinalIgnoreCase));
        if (ldt is null)
        {
            MessageBox.Show(this, "请拖入 .LDT 文件。", "拖放打开", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (!ConfirmDiscardChanges())
        {
            return;
        }

        await LoadLdtFromPathAsync(ldt).ConfigureAwait(true);
    }

    private static string GetRecentFilesPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LdtEditor", "recent-files.json");

    private sealed class RecentFilesDocument
    {
        public List<string> Paths { get; set; } = [];
        public List<string> Pinned { get; set; } = [];
    }

    private static RecentFilesDocument LoadRecentDocument()
    {
        try
        {
            var path = GetRecentFilesPath();
            if (!File.Exists(path))
            {
                return new RecentFilesDocument();
            }

            var json = File.ReadAllText(path);
            var doc = JsonSerializer.Deserialize<RecentFilesDocument>(json);
            return doc ?? new RecentFilesDocument();
        }
        catch
        {
            return new RecentFilesDocument();
        }
    }

    private static void NormalizeRecentDocument(RecentFilesDocument doc)
    {
        doc.Paths ??= [];
        doc.Pinned ??= [];
        doc.Pinned = doc.Pinned
            .Where(static p => !string.IsNullOrWhiteSpace(p) && File.Exists(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        doc.Paths = doc.Paths
            .Where(static p => !string.IsNullOrWhiteSpace(p) && File.Exists(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var pinSet = new HashSet<string>(doc.Pinned, StringComparer.OrdinalIgnoreCase);
        doc.Paths.RemoveAll(p => pinSet.Contains(p));
    }

    private static void SaveRecentDocument(RecentFilesDocument doc)
    {
        try
        {
            NormalizeRecentDocument(doc);
            const int pinCap = 16;
            const int pathCap = 16;
            if (doc.Pinned.Count > pinCap)
            {
                doc.Pinned = doc.Pinned.Take(pinCap).ToList();
            }

            if (doc.Paths.Count > pathCap)
            {
                doc.Paths = doc.Paths.Take(pathCap).ToList();
            }

            var path = GetRecentFilesPath();
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json, new UTF8Encoding(false));
        }
        catch
        {
            // ignore
        }
    }

    private void RebuildRecentFilesMenu()
    {
        _menuRecentFiles.DropDownItems.Clear();
        var doc = LoadRecentDocument();
        NormalizeRecentDocument(doc);
        var pinned = doc.Pinned;
        var paths = doc.Paths;
        if (pinned.Count == 0 && paths.Count == 0)
        {
            _menuRecentFiles.DropDownItems.Add(new ToolStripMenuItem("（无）") { Enabled = false });
            UpdateRecentFilePinMenu();
            return;
        }

        foreach (var p in pinned)
        {
            var pathCopy = p;
            var mi = new ToolStripMenuItem($"📌 {Path.GetFileName(p)}") { ToolTipText = p };
            mi.Click += async (_, _) => await OpenRecentFileAsync(pathCopy).ConfigureAwait(true);
            _menuRecentFiles.DropDownItems.Add(mi);
        }

        if (pinned.Count > 0 && paths.Count > 0)
        {
            _menuRecentFiles.DropDownItems.Add(new ToolStripSeparator());
        }

        const int maxShow = 12;
        for (var i = 0; i < paths.Count && i < maxShow; i++)
        {
            var p = paths[i];
            var label = $"{i + 1}  {Path.GetFileName(p)}";
            var mi = new ToolStripMenuItem(label) { ToolTipText = p };
            var pathCopy = p;
            mi.Click += async (_, _) => await OpenRecentFileAsync(pathCopy).ConfigureAwait(true);
            _menuRecentFiles.DropDownItems.Add(mi);
        }

        _menuRecentFiles.DropDownItems.Add(new ToolStripSeparator());
        _menuRecentFiles.DropDownItems.Add(new ToolStripMenuItem("管理钉选…", null, (_, _) => ShowManagePinnedRecentDialog()));
        _menuRecentFiles.DropDownItems.Add(new ToolStripMenuItem("清除未钉选的最近记录", null, (_, _) =>
        {
            doc.Paths.Clear();
            SaveRecentDocument(doc);
            RebuildRecentFilesMenu();
        }));
        _menuRecentFiles.DropDownItems.Add(new ToolStripMenuItem("清除全部（含钉选）", null, (_, _) =>
        {
            ClearRecentFiles();
            RebuildRecentFilesMenu();
        }));
        UpdateRecentFilePinMenu();
    }

    private void ShowManagePinnedRecentDialog()
    {
        var doc = LoadRecentDocument();
        NormalizeRecentDocument(doc);
        using var form = new Form
        {
            Text = "已钉选的文件",
            Width = 520,
            Height = 360,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            Owner = this
        };
        var list = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
        foreach (var p in doc.Pinned)
        {
            list.Items.Add(p);
        }

        var btnRemove = new Button { Text = "移除所选", Dock = DockStyle.Fill, Height = 36 };
        btnRemove.Click += (_, _) =>
        {
            if (list.SelectedItem is not string sel)
            {
                return;
            }

            doc.Pinned.RemoveAll(p => string.Equals(p, sel, StringComparison.OrdinalIgnoreCase));
            SaveRecentDocument(doc);
            list.Items.Remove(sel);
            RebuildRecentFilesMenu();
        };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(8)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44f));
        layout.Controls.Add(list, 0, 0);
        layout.Controls.Add(btnRemove, 0, 1);
        form.Controls.Add(layout);
        form.ShowDialog(this);
    }

    private void TogglePinCurrentFile()
    {
        if (string.IsNullOrWhiteSpace(_filePath) || !File.Exists(_filePath))
        {
            return;
        }

        var doc = LoadRecentDocument();
        NormalizeRecentDocument(doc);
        var norm = Path.GetFullPath(_filePath);
        var ix = doc.Pinned.FindIndex(p => string.Equals(Path.GetFullPath(p), norm, StringComparison.OrdinalIgnoreCase));
        if (ix >= 0)
        {
            doc.Pinned.RemoveAt(ix);
        }
        else
        {
            doc.Pinned.Insert(0, norm);
            doc.Paths.RemoveAll(p => string.Equals(Path.GetFullPath(p), norm, StringComparison.OrdinalIgnoreCase));
        }

        SaveRecentDocument(doc);
        RebuildRecentFilesMenu();
    }

    private void AddRecentFile(string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath) || !File.Exists(fullPath))
        {
            return;
        }

        var norm = Path.GetFullPath(fullPath);
        var doc = LoadRecentDocument();
        NormalizeRecentDocument(doc);
        var pinIx = doc.Pinned.FindIndex(p => string.Equals(Path.GetFullPath(p), norm, StringComparison.OrdinalIgnoreCase));
        if (pinIx >= 0)
        {
            doc.Pinned.RemoveAt(pinIx);
            doc.Pinned.Insert(0, norm);
            SaveRecentDocument(doc);
            return;
        }

        doc.Paths.RemoveAll(p => string.Equals(Path.GetFullPath(p), norm, StringComparison.OrdinalIgnoreCase));
        doc.Paths.Insert(0, norm);
        SaveRecentDocument(doc);
    }

    private void RemoveRecentFile(string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
        {
            return;
        }

        var doc = LoadRecentDocument();
        NormalizeRecentDocument(doc);
        doc.Paths.RemoveAll(p => string.Equals(p, fullPath, StringComparison.OrdinalIgnoreCase));
        doc.Pinned.RemoveAll(p => string.Equals(p, fullPath, StringComparison.OrdinalIgnoreCase));
        SaveRecentDocument(doc);
    }

    private static void ClearRecentFiles()
    {
        try
        {
            var path = GetRecentFilesPath();
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // ignore
        }
    }

    private void TryRegisterLdtFileAssociation()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
        {
            MessageBox.Show(this, "无法确定当前程序路径。", "文件关联", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            const string progId = "LdtEditor.LdtFile";
            using (var ext = Registry.CurrentUser.CreateSubKey(@"Software\Classes\.ldt", writable: true))
            {
                ext?.SetValue("", progId);
            }

            using (var id = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{progId}", writable: true))
            {
                id?.SetValue("", "LaTale LDT (LdtEditor)");
            }

            using (var cmd = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{progId}\shell\open\command", writable: true))
            {
                cmd?.SetValue("", $"\"{exe}\" \"%1\"");
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "文件关联", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        MessageBox.Show(
            this,
            "已为当前用户注册 .ldt 双击打开（写入 HKCU\\Software\\Classes）。\n若系统仍用其它程序打开，请在资源管理器「打开方式」中选择本程序。",
            "文件关联",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private void TryRegisterLdtFileAssociationAllUsers()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
        {
            MessageBox.Show(this, "无法确定当前程序路径。", "文件关联", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            const string progId = "LdtEditor.LdtFile";
            using (var ext = Registry.LocalMachine.CreateSubKey(@"Software\Classes\.ldt", writable: true))
            {
                ext?.SetValue("", progId);
            }

            using (var id = Registry.LocalMachine.CreateSubKey($@"Software\Classes\{progId}", writable: true))
            {
                id?.SetValue("", "LaTale LDT (LdtEditor)");
            }

            using (var cmd = Registry.LocalMachine.CreateSubKey($@"Software\Classes\{progId}\shell\open\command", writable: true))
            {
                cmd?.SetValue("", $"\"{exe}\" \"%1\"");
            }
        }
        catch (UnauthorizedAccessException)
        {
            MessageBox.Show(
                this,
                "写入 HKLM\\Software\\Classes 需要管理员权限。\n\n请右键本程序选择「以管理员身份运行」后，再从菜单执行本项；或由管理员通过组策略/安装脚本写入相同注册表项。",
                "文件关联（本机所有用户）",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "文件关联（本机所有用户）", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        MessageBox.Show(
            this,
            "已为「本机所有用户」注册 .ldt 双击打开（写入 HKLM\\Software\\Classes）。\n若当前用户下仍有 HKCU 关联，Windows 可能优先 HKCU；可保留其一或删除 HKCU 下 .ldt 项以统一行为。",
            "文件关联（本机所有用户）",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private async Task ShowReleaseAnnouncementsDialogAsync()
    {
        var url = _editorSettings.ReleaseAnnouncementsUrl?.Trim() ?? "";
        using var form = new Form
        {
            Text = "版本与公告",
            Width = 640,
            Height = 480,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            ShowInTaskbar = false,
            Owner = this
        };
        var tb = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            Dock = DockStyle.Fill,
            Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Regular, GraphicsUnit.Point),
            WordWrap = true
        };
        form.Controls.Add(tb);
        if (string.IsNullOrEmpty(url))
        {
            tb.Text =
                "未配置在线公告地址。\n\n"
                + "请在 Ctrl+F →「选项」页的「更新公告 URL」中填写发布方提供的 https 地址（建议返回纯文本或简短 Markdown）。\n"
                + "留空时仅显示本说明；配置后本窗口会尝试 GET 拉取（约 10 秒超时）。\n\n"
                + "程序同目录 README.md 中的「当前版本下载地址」亦可能由发布方维护。";
            form.ShowDialog(this);
            return;
        }

        tb.Text = "正在拉取…";
        form.Shown += async (_, _) =>
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                var body = await http.GetStringAsync(url).ConfigureAwait(true);
                tb.Text = string.IsNullOrWhiteSpace(body) ? "（服务器返回空内容）" : body;
            }
            catch (Exception ex)
            {
                tb.Text = $"拉取失败：{ex.Message}\n\nURL：{url}";
            }
        };
        form.ShowDialog(this);
    }

    private static void ShowAboutDialog()
    {
        var asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var ver = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? asm.GetName().Version?.ToString()
            ?? "?";
        var fx = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;
        MessageBox.Show(
            $"LDT Editor\n版本 {ver}\n运行时 {fx}\n\n彩虹岛 / LaTale 改端工具\n作者：79（qq：402411873）\n完整说明见菜单「帮助 → 使用说明」或程序目录下的 README.md。\n许可：MIT（见程序目录 LICENSE）；游戏数据与资源权属见 README 免责声明。",
            "关于 LDT Editor",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private void ShowUserReadmeDialog()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "README.md");
        string markdown;
        try
        {
            markdown = File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : UserReadmeFallbackText;
        }
        catch
        {
            markdown = UserReadmeFallbackText;
        }

        using var form = new UserReadmeHtmlForm(markdown);
        form.ShowDialog(this);
    }

    private const string UserReadmeFallbackText =
        "LDT Editor — 使用说明（摘要）\r\n\r\n"
        + "未找到程序目录下的 README.md，以下为简要说明；完整内容请以发行包内的 README.md 为准。\r\n\r\n"
        + "打开表：文件 → 打开 (Ctrl+O)、拖入 .ldt、或双击已关联的 .ldt 文件。\r\n"
        + "功能菜单 (Ctrl+F)：查找 / 替换 / 插入 / 选项。替换仅限当前列；选项可要求须先预览后才可「直接」。\r\n"
        + "插入：按规则向当前列批量填数，可仅作用于筛选可见行，建议写入前预览。\r\n"
        + "视图：物品说明预览 (Shift+P)、内嵌查找条 (Enter/F3 下一处，Ctrl+Enter 应用筛选)、修改记录 (Alt+S，Alt+Z 还原悬停格)。\r\n"
        + "编辑：选图窗 (Shift+A)，须在选项中配置 SPF 路径。\r\n"
        + "工具：将 .ldt 关联到本程序（当前用户，或管理员下的本机所有用户）。\r\n"
        + "个人设置保存在 %LocalAppData%\\LdtEditor\\（选项、最近文件与钉选等会自动写入）。\r\n"
        + "帮助 → 版本与公告：可在选项中填写公告 URL 后查看在线说明。\r\n\r\n"
        + "修改表数据前请自行备份；本工具非游戏官方产品。";

    private void ApplyViewLayoutFromSettings()
    {
        if (_menuViewPreviewItem is not null)
        {
            _menuViewPreviewItem.Checked = _editorSettings.ShowItemDescriptionPanel;
        }

        if (_menuViewFindBarItem is not null)
        {
            _menuViewFindBarItem.Checked = _editorSettings.ShowQuickFindBar;
        }

        _findBarPanel.Visible = _editorSettings.ShowQuickFindBar;
        _splitMain.Panel2Collapsed = true;
        var showPrev = _editorSettings.ShowItemDescriptionPanel;
        if (showPrev)
        {
            EnsureItemPreviewForm();
            if (_itemPreviewForm is { IsDisposed: false })
            {
                _itemPreviewForm.Show(this);
                SyncItemPreviewFloatBounds();
                UpdateItemDescriptionPreview();
            }
        }
        else if (_itemPreviewForm is { IsDisposed: false })
        {
            _itemPreviewForm.Hide();
        }
    }

    /// <summary>
    /// 避免在窗体/容器 ClientSize 未就绪时设置 <see cref="SplitContainer.Panel2MinSize"/> 或
    /// <see cref="SplitContainer.SplitterDistance"/>（否则会抛出「SplitterDistance 必须在 … 之间」）。
    /// </summary>
    private void TryApplyPreviewSplitterLayout()
    {
        if (_splitMain.Panel2Collapsed)
        {
            return;
        }

        var w = ClientSize.Width;
        if (w <= _splitMain.SplitterWidth + _splitMain.Panel1MinSize + 40)
        {
            return;
        }

        var p2Min = Math.Clamp(160, 40, Math.Max(40, w / 4));
        try
        {
            _splitMain.Panel2MinSize = p2Min;
        }
        catch
        {
            _splitMain.Panel2MinSize = 25;
        }

        var maxDist = w - _splitMain.Panel2MinSize - _splitMain.SplitterWidth;
        if (maxDist <= _splitMain.Panel1MinSize)
        {
            return;
        }

        var want = w - 280 - _splitMain.SplitterWidth;
        try
        {
            _splitMain.SplitterDistance = Math.Clamp(want, _splitMain.Panel1MinSize, maxDist);
        }
        catch
        {
            try
            {
                _splitMain.SplitterDistance = maxDist;
            }
            catch
            {
                _splitMain.Panel2Collapsed = true;
            }
        }
    }

    private void ClampPreviewSplitter()
    {
        if (_splitMain.Panel2Collapsed || ClientSize.Width <= 0)
        {
            return;
        }

        var w = ClientSize.Width;
        var maxDist = w - _splitMain.Panel2MinSize - _splitMain.SplitterWidth;
        if (maxDist <= _splitMain.Panel1MinSize)
        {
            return;
        }

        try
        {
            if (_splitMain.SplitterDistance > maxDist)
            {
                _splitMain.SplitterDistance = maxDist;
            }
        }
        catch
        {
            TryApplyPreviewSplitterLayout();
        }
    }

    private void MenuToggleItemDescriptionPreview(object? sender, EventArgs e) =>
        CommitItemDescriptionPanelSettingFromMenu();

    private void CommitItemDescriptionPanelSettingFromMenu()
    {
        if (_menuViewPreviewItem is null)
        {
            return;
        }

        _editorSettings = _editorSettings with { ShowItemDescriptionPanel = _menuViewPreviewItem.Checked };
        ApplyViewLayoutFromSettings();
        SaveEditorAppSettings(false);
    }

    private void MenuToggleQuickFindBar(object? sender, EventArgs e)
    {
        if (_menuViewFindBarItem is null)
        {
            return;
        }

        _editorSettings = _editorSettings with { ShowQuickFindBar = _menuViewFindBarItem.Checked };
        ApplyViewLayoutFromSettings();
        SaveEditorAppSettings(false);
    }

    private void RefreshDescriptionPreviewColumn()
    {
        _descPreviewColumnIndex = -1;
        _itemPreviewColumnMap = new ItemPreviewColumnMap();
        if (_parsedTable is null)
        {
            return;
        }

        if (ItemPreviewColumnResolver.TryResolveDescriptionColumn(_parsedTable, out var col, out _))
        {
            _descPreviewColumnIndex = col;
        }

        _itemPreviewColumnMap = ItemPreviewColumnResolver.Build(_parsedTable, _descPreviewColumnIndex);
    }

    private void EnsureItemPreviewForm()
    {
        if (_itemPreviewForm is { IsDisposed: false })
        {
            return;
        }

        var f = new ItemPreviewFloatForm(this);
        f.FormClosed += ItemPreviewFormOnFormClosed;
        _itemPreviewForm = f;
    }

    private void ItemPreviewFormOnFormClosed(object? sender, FormClosedEventArgs e)
    {
        if (_mainFormClosing || !ReferenceEquals(sender, _itemPreviewForm))
        {
            return;
        }

        _itemPreviewForm = null;
        if (_menuViewPreviewItem is not null)
        {
            _menuViewPreviewItem.Checked = false;
        }

        _editorSettings = _editorSettings with { ShowItemDescriptionPanel = false };
        SaveEditorAppSettings(false);
    }

    private void SyncItemPreviewFloatBounds()
    {
        if (_itemPreviewForm is { IsDisposed: false, Visible: true })
        {
            _itemPreviewForm.SyncBoundsNearOwner();
        }
    }

    private void UpdateItemDescriptionPreview()
    {
        if (!_editorSettings.ShowItemDescriptionPanel)
        {
            _itemPreviewForm?.Hide();
            return;
        }

        EnsureItemPreviewForm();
        if (_itemPreviewForm is null || _itemPreviewForm.IsDisposed)
        {
            return;
        }

        if (!_itemPreviewForm.Visible)
        {
            _itemPreviewForm.Show(this);
        }

        SyncItemPreviewFloatBounds();

        if (_parsedTable is null)
        {
            _itemPreviewForm.ClearIcon();
            _itemPreviewForm.BindEditContext(-1, multiSelectFirstRow: true);
            _itemPreviewForm.ApplyContent(new ItemPreviewModel
            {
                Title = "物品预览",
                BlackLines = { "未打开表格。" }
            });
            return;
        }

        if (_descPreviewColumnIndex < 0 || _descPreviewColumnIndex >= _parsedTable.ColumnNames.Length)
        {
            _itemPreviewForm.ClearIcon();
            _itemPreviewForm.BindEditContext(-1, multiSelectFirstRow: true);
            _itemPreviewForm.ApplyContent(new ItemPreviewModel
            {
                Title = "物品预览",
                BlackLines =
                {
                    "当前表未匹配到说明类文本列（列名可含「说明」「DESC」等）。",
                    "可在「视图」菜单关闭本悬浮窗。"
                }
            });
            return;
        }

        var multi = _grid.SelectedRows.Count > 1;
        int parsed;
        int gridRowForIcon = -1;
        if (_grid.CurrentCell is { RowIndex: >= 0 } cc && TryGetParsedRowIndexFromGridRow(cc.RowIndex, out parsed))
        {
            gridRowForIcon = cc.RowIndex;
        }
        else if (_grid.SelectedRows.Count > 0)
        {
            var g = _grid.SelectedRows.Cast<DataGridViewRow>().Min(r => r.Index);
            if (!TryGetParsedRowIndexFromGridRow(g, out parsed))
            {
                _itemPreviewForm.ClearIcon();
                _itemPreviewForm.BindEditContext(-1, multiSelectFirstRow: true);
                _itemPreviewForm.ApplyContent(new ItemPreviewModel
                {
                    BlackLines = { "请选中表格中的一行以预览物品。" }
                });
                return;
            }

            gridRowForIcon = g;
        }
        else
        {
            _itemPreviewForm.ClearIcon();
            _itemPreviewForm.BindEditContext(-1, multiSelectFirstRow: true);
            _itemPreviewForm.ApplyContent(new ItemPreviewModel
            {
                BlackLines = { "请选中表格中的一行以预览物品。" }
            });
            return;
        }

        var model = ItemPreviewColumnResolver.BuildModel(_parsedTable, parsed, _itemPreviewColumnMap, multi);
        _itemPreviewForm.BindEditContext(
            parsed,
            multi,
            nameCol: multi ? -1 : _itemPreviewColumnMap.NameColumn,
            descCol: multi ? -1 : _itemPreviewColumnMap.DescColumn);
        _itemPreviewForm.ApplyContent(model);

        var spfPath = _editorSettings.SpfArchivePath?.Trim() ?? "";
        if (spfPath.Length == 0 || !File.Exists(spfPath))
        {
            _itemPreviewForm.ClearIcon();
            return;
        }

        var iconId = 0;
        var iconIx = LaTaleIconSheet.IconIndexFirstValue;
        if (gridRowForIcon >= 0
            && TryGetIconPickerLaunchContext(gridRowForIcon, out _, out var idOk, out var ixOk, out _, out _, notifyUser: false))
        {
            iconId = idOk;
            iconIx = ixOk;
        }

        _itemPreviewForm.RequestIconThumbnail(spfPath, iconId, iconIx);
    }

    private void CloseIconPickerIfOpen()
    {
        var p = _iconPicker;
        if (p is null || p.IsDisposed)
        {
            return;
        }

        try
        {
            p.Close();
        }
        catch
        {
            // ignore
        }
    }

    private void PushOpenProgressBytes(int current, int total) => PushOpenProgress(current, total, bytesMode: true);

    private void PushOpenProgressRows(int current, int total) => PushOpenProgress(current, total, bytesMode: false);

    private void PushOpenProgress(int current, int total, bool bytesMode)
    {
        lock (_openTitleLock)
        {
            _openTitleCur = current;
            _openTitleTotal = total;
            _openTitleBytesMode = bytesMode;
        }
    }

    private void StopOpenTitleProgress()
    {
        _openTitleTimer.Stop();
        lock (_openTitleLock)
        {
            _openTitleCur = 0;
            _openTitleTotal = 0;
        }
    }

    private void OpenTitleTimerOnTick(object? sender, EventArgs e)
    {
        int c;
        int t;
        bool bytesMode;
        lock (_openTitleLock)
        {
            c = _openTitleCur;
            t = _openTitleTotal;
            bytesMode = _openTitleBytesMode;
        }

        if (t <= 0)
        {
            return;
        }

        var numbers = bytesMode ? $"{c:N0}/{t:N0}" : $"{c}/{t}";
        Text = $"{numbers} | {BuildWindowTitleBase()}";
    }

    private static async Task<byte[]> ReadAllBytesStreamingAsync(string path, Action<int, int> reportProgress)
    {
        var length = new FileInfo(path).Length;
        if (length > int.MaxValue)
        {
            throw new InvalidDataException($"File is too large ({length} bytes).");
        }

        var total = (int)length;
        var data = GC.AllocateUninitializedArray<byte>(total);
        reportProgress(0, total);

        await using var fs = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var offset = 0;
        while (offset < total)
        {
            var read = await fs.ReadAsync(data.AsMemory(offset, total - offset)).ConfigureAwait(true);
            if (read == 0)
            {
                throw new EndOfStreamException("Unexpected end of file while reading.");
            }

            offset += read;
            reportProgress(offset, total);
        }

        return data;
    }

    private void SaveToCurrentFile()
    {
        if (_data is null || _parsedTable is null || _filePath is null)
        {
            MessageBox.Show("Open a supported LDT file first.", "LDT Editor", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            var oldData = _data!;
            var (patched, dataStart, dataEndExclusive) = BuildPatchedTypedFile(oldData, _parsedTable);
            if (!TryWriteLdtWithOptionalBackup(_filePath, patched, out var writeErr))
            {
                MessageBox.Show(writeErr?.Message ?? "保存失败。", "Save Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            _data = patched;
            _parsedTable = _parsedTable with { DataStartOffset = dataStart, DataEndOffset = dataEndExclusive };
            _isDirty = false;
            TryPersistModificationRecordsIfEligible(force: true);
            UpdateStatusBar();
            UpdateWindowTitle();
            MessageBox.Show("Saved.", "LDT Editor", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Save Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void SaveAsNewFile()
    {
        if (_data is null || _parsedTable is null || _filePath is null)
        {
            MessageBox.Show("Open a supported LDT file first.", "LDT Editor", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Filter = "LDT files (*.LDT)|*.LDT|All files (*.*)|*.*",
            FileName = Path.GetFileNameWithoutExtension(_filePath) + ".table.edited" + Path.GetExtension(_filePath),
            Title = "Save patched LDT"
        };
        if (dialog.ShowDialog() != DialogResult.OK)
        {
            return;
        }

        try
        {
            var oldData = _data!;
            var (patched, dataStart, dataEndExclusive) = BuildPatchedTypedFile(oldData, _parsedTable);
            var destPath = Path.GetFullPath(dialog.FileName);
            if (!TryWriteLdtWithOptionalBackup(destPath, patched, out var writeErr))
            {
                MessageBox.Show(writeErr?.Message ?? "保存失败。", "Save As Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            _filePath = destPath;
            _data = patched;
            _parsedTable = _parsedTable with { DataStartOffset = dataStart, DataEndOffset = dataEndExclusive };
            _isDirty = false;
            TryPersistModificationRecordsIfEligible(force: true);
            AddRecentFile(_filePath);
            RebuildRecentFilesMenu();
            UpdateWindowTitle();
            UpdateStatusBar();
            MessageBox.Show("Saved.", "LDT Editor", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Save As Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>覆盖已有文件时可选复制为「路径 + .bak」，再原子写入新内容。</summary>
    private bool TryWriteLdtWithOptionalBackup(string targetPath, byte[] patched, out Exception? error)
    {
        error = null;
        try
        {
            if (_editorSettings.BackupBeforeOverwrite && File.Exists(targetPath))
            {
                var bakPath = targetPath + ".bak";
                File.Copy(targetPath, bakPath, overwrite: true);
            }

            if (!TryAtomicWriteAllBytes(targetPath, patched, out var wEx))
            {
                error = wEx;
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            error = ex;
            return false;
        }
    }

    /// <summary>先写临时文件再替换目标，降低保存过程中异常导致半写入的概率。</summary>
    private static bool TryAtomicWriteAllBytes(string path, byte[] contents, out Exception? error)
    {
        error = null;
        var tmp = path + ".new.tmp";
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllBytes(tmp, contents);
            if (File.Exists(path))
            {
                File.Replace(tmp, path, destinationBackupFileName: null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(tmp, path, overwrite: true);
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
                // ignore cleanup failure
            }

            return false;
        }
    }

    private void LoadTable(Action<int, int>? rowProgress)
    {
        CloseIconPickerIfOpen();

        if (_parsedTable is null)
        {
            return;
        }

        var totalRows = _parsedTable.Rows.Count;
        var suspendRedraw = _grid.IsHandleCreated;

        _grid.SuspendLayout();
        if (suspendRedraw)
        {
            _ = SendMessageW(_grid.Handle, WmSetRedraw, 0, 0);
        }

        try
        {
            _grid.RowCount = 0;
            _grid.Columns.Clear();

            _grid.Columns.Add("rowIndex", "#");
            for (var c = 0; c < _parsedTable.ColumnNames.Length; c++)
            {
                var name = _parsedTable.ColumnNames[c];
                var header = string.IsNullOrWhiteSpace(name) ? $"COL_{c}" : name;
                _grid.Columns.Add($"c{c}", header);
            }

            _grid.Columns[0].ReadOnly = true;
            _grid.Columns[0].Frozen = true;
            _grid.Columns[0].Width = 48;
            _sortActive = false;
            for (var c = 0; c < _grid.Columns.Count; c++)
            {
                _grid.Columns[c].SortMode = DataGridViewColumnSortMode.Programmatic;
                _grid.Columns[c].HeaderCell.SortGlyphDirection = SortOrder.None;
            }

            for (var c = 1; c < _grid.Columns.Count; c++)
            {
                var columnType = _parsedTable.ColumnTypes[c - 1];
                var columnName = _parsedTable.ColumnNames[c - 1];
                _grid.Columns[c].Width = GetDefaultColumnWidth(columnName, columnType);
            }

            for (var r = 0; r < totalRows; r++)
            {
                rowProgress?.Invoke(r + 1, totalRows);
            }

            ApplyFilterToGrid();
            RefreshDescriptionPreviewColumn();
            UpdateItemDescriptionPreview();
        }
        finally
        {
            if (suspendRedraw)
            {
                _ = SendMessageW(_grid.Handle, WmSetRedraw, 1, 0);
            }

            _grid.ResumeLayout();
            if (suspendRedraw)
            {
                _grid.Refresh();
            }
        }
    }

    private void MainDataGridView_CellValueNeeded(object? sender, DataGridViewCellValueEventArgs e)
    {
        if (_parsedTable is null || e.RowIndex < 0 || e.RowIndex >= _visibleParsedRows.Count)
        {
            return;
        }

        var parsedRow = _visibleParsedRows[e.RowIndex];
        if (parsedRow < 0 || parsedRow >= _parsedTable.Rows.Count)
        {
            return;
        }

        if (e.ColumnIndex == 0)
        {
            e.Value = parsedRow;
            return;
        }

        if (e.ColumnIndex <= _parsedTable.ColumnNames.Length)
        {
            e.Value = _parsedTable.Rows[parsedRow][e.ColumnIndex - 1];
        }
    }

    private void MainDataGridView_CellValuePushed(object? sender, DataGridViewCellValueEventArgs e)
    {
        CommitGridDataCellIfChanged(e.RowIndex, e.ColumnIndex, e.Value);
    }

    private void GridCellEndEdit(object? sender, DataGridViewCellEventArgs e)
    {
        if (_parsedTable is null || e.RowIndex < 0 || e.ColumnIndex <= 0)
        {
            return;
        }

        var cell = _grid.Rows[e.RowIndex].Cells[e.ColumnIndex];
        var raw = cell.EditedFormattedValue?.ToString() ?? cell.Value?.ToString() ?? string.Empty;
        CommitGridDataCellIfChanged(e.RowIndex, e.ColumnIndex, raw);
    }

    /// <summary>虚拟模式下必须在 <see cref="MainDataGridView_CellValuePushed"/> 写回；本方法也供 <see cref="GridCellEndEdit"/> 兜底。</summary>
    private void CommitGridDataCellIfChanged(int gridRowIndex, int columnIndex, object? editedValue)
    {
        if (_parsedTable is null || gridRowIndex < 0 || columnIndex <= 0)
        {
            return;
        }

        if (!TryGetParsedRowIndexFromGridRow(gridRowIndex, out var parsedRow))
        {
            return;
        }

        var colIndex = columnIndex - 1;
        if (parsedRow >= _parsedTable.Rows.Count || colIndex >= _parsedTable.ColumnNames.Length)
        {
            return;
        }

        var raw = editedValue switch
        {
            null => "",
            string s => s,
            IFormattable f => f.ToString(null, CultureInfo.CurrentCulture),
            _ => editedValue.ToString() ?? ""
        };

        var oldValue = _parsedTable.Rows[parsedRow][colIndex];

        if (!TryParseCellValue(_parsedTable.ColumnTypes[colIndex], raw, out var parsed, out var error))
        {
            _grid.CancelEdit();
            _grid.InvalidateCell(columnIndex, gridRowIndex);
            MessageBox.Show(error ?? "Invalid value.", "Edit Cell Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (Equals(oldValue, parsed))
        {
            return;
        }

        CaptureSnapshotForUndo();
        _parsedTable.Rows[parsedRow][colIndex] = parsed;
        TrackCellModification(parsedRow, colIndex, oldValue);
        _grid.InvalidateCell(columnIndex, gridRowIndex);
        _isDirty = true;
        UpdateStatusBar();
        UpdateWindowTitle();
    }

    private void GridCellDoubleClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex <= 0)
        {
            return;
        }

        var header = _grid.Columns[e.ColumnIndex].HeaderText ?? "";
        if (!string.Equals(header.Trim(), LaTaleIconSheet.IconColumnName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        TryOpenIconPickerForRow(e.RowIndex);
    }

    private bool TryFindTableColumnIndex(string header, out int tableColumnIndex)
    {
        tableColumnIndex = -1;
        if (_parsedTable is null)
        {
            return false;
        }

        var h = header.Trim();
        for (var i = 0; i < _parsedTable.ColumnNames.Length; i++)
        {
            if (string.Equals(_parsedTable.ColumnNames[i].Trim(), h, StringComparison.OrdinalIgnoreCase))
            {
                tableColumnIndex = i;
                return true;
            }
        }

        return false;
    }

    private static int CoerceCellToInt32(object? cell)
    {
        return cell switch
        {
            int i => i,
            uint u => (int)u,
            long l => (int)Math.Clamp(l, int.MinValue, int.MaxValue),
            float f => (int)f,
            double d => (int)d,
            _ => int.TryParse(cell?.ToString(), out var p) ? p : 0
        };
    }

    internal static int CoerceCellToInt32ForProbe(object? cell) => CoerceCellToInt32(cell);

    internal static ParsedLdtTable ParseTypedTableFromBytes(byte[] data, LdtTextDecodePreference textDecode) =>
        ParseTypedTable(data, textDecode, null);

    /// <summary>
    /// 无改保存布局的补丁（与 GUI 保存同路径），供 <c>ldt-roundtrip</c> 等回归使用。
    /// </summary>
    internal static (byte[] PatchedBytes, int DataStartOffset, int DataEndOffsetExclusive) PatchTypedLdtBytes(
        byte[] original,
        ParsedLdtTable table,
        Encoding rowStringEncoding)
    {
        var (dataStartOffset, dataEndOffset) = MeasureOnDiskTypedRowBlob(original, table);

        var body = SerializeTypedRows(table, rowStringEncoding);
        var prefix = original.AsSpan(0, dataStartOffset).ToArray();
        if (prefix.Length >= 12)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(prefix.AsSpan(8, 4), checked((uint)table.Rows.Count));
        }

        var suffix = original.AsSpan(dataEndOffset).ToArray();

        var output = new byte[prefix.Length + body.Length + suffix.Length];
        Buffer.BlockCopy(prefix, 0, output, 0, prefix.Length);
        Buffer.BlockCopy(body, 0, output, prefix.Length, body.Length);
        Buffer.BlockCopy(suffix, 0, output, prefix.Length + body.Length, suffix.Length);
        var dataEndExclusive = dataStartOffset + body.Length;
        return (output, dataStartOffset, dataEndExclusive);
    }

    internal static bool TypedTableDataEquals(ParsedLdtTable a, ParsedLdtTable b)
    {
        if (a.Rows.Count != b.Rows.Count || a.ColumnTypes.Length != b.ColumnTypes.Length)
        {
            return false;
        }

        for (var i = 0; i < a.ColumnTypes.Length; i++)
        {
            if (a.ColumnTypes[i] != b.ColumnTypes[i])
            {
                return false;
            }
        }

        for (var r = 0; r < a.Rows.Count; r++)
        {
            var ra = a.Rows[r];
            var rb = b.Rows[r];
            for (var c = 0; c < a.ColumnTypes.Length; c++)
            {
                if (!LdtCellValuesEqual(ra[c], rb[c]))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool LdtCellValuesEqual(object? x, object? y)
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

    /// <summary>
    /// 将当前显示行映射到 <see cref="ParsedLdtTable.Rows"/>；虚拟模式下由 <see cref="_visibleParsedRows"/> 决定。
    /// </summary>
    private bool TryGetParsedRowIndexFromGridRow(int gridRowIndex, out int parsedRowIndex)
    {
        parsedRowIndex = -1;
        if (_parsedTable is null || gridRowIndex < 0 || gridRowIndex >= _grid.RowCount)
        {
            return false;
        }

        if (gridRowIndex >= _visibleParsedRows.Count)
        {
            return false;
        }

        parsedRowIndex = _visibleParsedRows[gridRowIndex];
        if (parsedRowIndex < 0 || parsedRowIndex >= _parsedTable.Rows.Count)
        {
            parsedRowIndex = -1;
            return false;
        }

        return true;
    }

    private int FindFirstGridRowForParsedIndex(int parsedRowIndex)
    {
        if (_parsedTable is null || parsedRowIndex < 0 || parsedRowIndex >= _parsedTable.Rows.Count)
        {
            return -1;
        }

        for (var g = 0; g < _visibleParsedRows.Count; g++)
        {
            if (_visibleParsedRows[g] == parsedRowIndex)
            {
                return g;
            }
        }

        return -1;
    }

    private static object MapIntForColumnType(int columnType, int value)
    {
        return columnType switch
        {
            0 => (object)(uint)Math.Clamp(value, 0, int.MaxValue),
            3 => value,
            _ => value
        };
    }

    private void ToggleIconPickerWindow()
    {
        if (_iconPicker is { IsDisposed: false, Visible: true })
        {
            _iconPicker.Close();
            return;
        }

        if (_grid.CurrentCell is { RowIndex: >= 0 } cc)
        {
            TryOpenIconPickerForRow(cc.RowIndex);
        }
        else
        {
            MessageBox.Show(
                "请先选中表格中的一行。",
                "图标",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
    }

    private void SyncIconPickerToCurrentGridRowIfOpen()
    {
        if (_iconPicker is not { IsDisposed: false, Visible: true })
        {
            return;
        }

        if (_grid.CurrentCell is not { RowIndex: >= 0 } cc)
        {
            return;
        }

        TrySyncIconPickerToRow(cc.RowIndex);
        UpdateIconPickerBindHint();
    }

    private void UpdateIconPickerBindHint()
    {
        if (_iconPicker is not { IsDisposed: false, Visible: true })
        {
            _iconPickerBindHintLabel.Text = "";
            return;
        }

        if (_grid.CurrentCell is not { RowIndex: >= 0 } cc || cc.RowIndex >= _grid.RowCount)
        {
            _iconPickerBindHintLabel.Text = "选图窗已打开";
            return;
        }

        var hashCol = TryGetParsedRowIndexFromGridRow(cc.RowIndex, out var pr) ? pr.ToString() : "?";
        var hint = $"选图窗 · 主窗 #{hashCol}";
        if (_rowSelectionMode && TryGetIconApplyTargetGridRows(_iconPicker.BoundGridRow, out var targets) && targets.Count > 1)
        {
            hint += $" · 批量 {targets.Count} 行";
        }

        _iconPickerBindHintLabel.Text = hint;
    }

    /// <summary>
    /// 行选择且多选时返回全部选中网格行；块选择或单行时返回 <paramref name="fallbackBoundGridRow"/>（或当前格行）。
    /// </summary>
    private bool TryGetIconApplyTargetGridRows(int fallbackBoundGridRow, out List<int> sortedGridRows)
    {
        sortedGridRows = new List<int>();
        if (_parsedTable is null)
        {
            return false;
        }

        if (_rowSelectionMode)
        {
            foreach (DataGridViewRow r in _grid.SelectedRows)
            {
                if (r.Index >= 0 && r.Index < _grid.RowCount)
                {
                    sortedGridRows.Add(r.Index);
                }
            }

            if (sortedGridRows.Count > 1)
            {
                sortedGridRows.Sort();
                return true;
            }

            sortedGridRows.Clear();
        }

        var single = fallbackBoundGridRow;
        if (single < 0 && _grid.CurrentCell is { RowIndex: >= 0 } cc)
        {
            single = cc.RowIndex;
        }

        if (single >= 0 && single < _grid.RowCount)
        {
            sortedGridRows.Add(single);
            return true;
        }

        return false;
    }

    private bool TryGetIconPickerLaunchContext(
        int gridRowIndex,
        out string spf,
        out int iconId,
        out int iconIxOneBased,
        out int cIcon,
        out int cIdx,
        bool notifyUser)
    {
        spf = "";
        iconId = 0;
        iconIxOneBased = 0;
        cIcon = 0;
        cIdx = 0;

        if (_parsedTable is null || gridRowIndex < 0 || gridRowIndex >= _grid.RowCount)
        {
            return false;
        }

        if (!TryGetParsedRowIndexFromGridRow(gridRowIndex, out var parsedRow) || parsedRow < 0 || parsedRow >= _parsedTable.Rows.Count)
        {
            return false;
        }

        spf = _editorSettings.SpfArchivePath?.Trim() ?? "";
        if (spf.Length == 0 || !File.Exists(spf))
        {
            if (notifyUser)
            {
                MessageBox.Show(
                    "请先在「功能菜单 (Ctrl+F)」→「选项」页配置 BANX.SPF 等文件的完整路径。",
                    "SPF 未配置",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }

            return false;
        }

        if (!TryFindTableColumnIndex(LaTaleIconSheet.IconColumnName, out cIcon))
        {
            if (notifyUser)
            {
                MessageBox.Show(
                    $"当前表未找到列「{LaTaleIconSheet.IconColumnName}」。",
                    "图标列",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }

            return false;
        }

        if (!TryFindTableColumnIndex(LaTaleIconSheet.IconIndexColumnName, out cIdx))
        {
            if (notifyUser)
            {
                MessageBox.Show(
                    $"当前表未找到列「{LaTaleIconSheet.IconIndexColumnName}」。",
                    "图标指数列",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }

            return false;
        }

        iconId = CoerceCellToInt32(_parsedTable.Rows[parsedRow][cIcon]);
        if (!LaTaleIconSheet.IsKnownIconSheet(iconId))
        {
            if (notifyUser)
            {
                MessageBox.Show(
                    "当前行的 _Icon 无已知图集映射。\n" +
                    "已支持：按编码区间解析图集（含 ITEMETC、ITEMCHINA、ITEMTAIWAN 等）；仅 20001300–20001399 固定对应 542042520.PNG。",
                    "图标编码",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }

            return false;
        }

        iconIxOneBased = Math.Max(LaTaleIconSheet.IconIndexFirstValue, CoerceCellToInt32(_parsedTable.Rows[parsedRow][cIdx]));
        return true;
    }

    private void TrySyncIconPickerToRow(int gridRowIndex)
    {
        if (_iconPicker is not { IsDisposed: false, Visible: true })
        {
            return;
        }

        if (!TryGetIconPickerLaunchContext(gridRowIndex, out _, out var iconId, out var iconIx, out var cIcon, out var cIdx, notifyUser: false))
        {
            return;
        }

        _iconPicker.ApplyRowFromMain(gridRowIndex, cIcon, cIdx, iconId, iconIx);
        try
        {
            _iconPicker.BringToFront();
            _iconPicker.Activate();
        }
        catch
        {
            // Ignore focus races (e.g. parent minimized).
        }
    }

    private void TryOpenIconPickerForRow(int gridRowIndex)
    {
        if (_iconPicker is { IsDisposed: false, Visible: true })
        {
            TrySyncIconPickerToRow(gridRowIndex);
            UpdateIconPickerBindHint();
            return;
        }

        if (!TryGetIconPickerLaunchContext(gridRowIndex, out var spf, out var iconId, out var iconIxOneBased, out var cIcon, out var cIdx, notifyUser: true))
        {
            return;
        }

        IconAtlasPickerForm? holder = null;
        holder = new IconAtlasPickerForm(
            spf,
            gridRowIndex,
            cIcon,
            cIdx,
            iconId,
            iconIxOneBased,
            (newId, newIx) => ApplyIconPickFromPicker(holder!, newId, newIx));
        holder.FormClosed += (_, _) =>
        {
            if (ReferenceEquals(_iconPicker, holder))
            {
                _iconPicker = null;
                _iconPickerBindHintLabel.Text = "";
            }
        };

        _iconPicker = holder;
        holder.Show(this);
        try
        {
            holder.BringToFront();
            holder.Activate();
        }
        catch
        {
            // Ignore focus races.
        }

        UpdateIconPickerBindHint();
    }

    /// <summary>
    /// 由 <see cref="ItemPreviewFloatForm"/> 内联编辑词条数值回写：复用
    /// <see cref="CaptureSnapshotForUndo"/> + <see cref="MapIntForColumnType"/> +
    /// <see cref="DataGridView.InvalidateCell(int,int)"/> + 标脏 + 刷新预览 的既有路径。
    /// 仅接受数值列（类型 0/3/4）；string/bool 列上的双击编辑不会触发该路径。
    /// </summary>
    internal bool CommitStringCellFromPreview(int parsedRow, int colIndex, string newValue)
    {
        if (_parsedTable is null
            || parsedRow < 0 || parsedRow >= _parsedTable.Rows.Count
            || colIndex < 0 || colIndex >= _parsedTable.ColumnNames.Length)
        {
            return false;
        }

        if (_parsedTable.ColumnTypes[colIndex] != 1)
        {
            return false;
        }

        var oldValue = _parsedTable.Rows[parsedRow][colIndex] as string ?? "";
        if (string.Equals(oldValue, newValue, StringComparison.Ordinal))
        {
            return false;
        }

        CaptureSnapshotForUndo();
        _parsedTable.Rows[parsedRow][colIndex] = newValue;
        TrackCellModification(parsedRow, colIndex, oldValue);
        var gridRow = FindFirstGridRowForParsedIndex(parsedRow);
        if (gridRow >= 0)
        {
            _grid.InvalidateCell(colIndex + 1, gridRow);
        }

        _isDirty = true;
        UpdateStatusBar();
        UpdateWindowTitle();
        UpdateItemDescriptionPreview();
        return true;
    }

    internal bool CommitCellFromPreview(int parsedRow, int colIndex, int newStoredValue)
    {
        if (_parsedTable is null
            || parsedRow < 0 || parsedRow >= _parsedTable.Rows.Count
            || colIndex < 0 || colIndex >= _parsedTable.ColumnNames.Length)
        {
            return false;
        }

        var ct = _parsedTable.ColumnTypes[colIndex];
        if (ct is not (0 or 3 or 4))
        {
            return false;
        }

        if (ct == 0 && newStoredValue < 0)
        {
            return false;
        }

        var newObj = MapIntForColumnType(ct, newStoredValue);
        var oldValue = _parsedTable.Rows[parsedRow][colIndex];
        if (Equals(oldValue, newObj))
        {
            return false;
        }

        CaptureSnapshotForUndo();
        _parsedTable.Rows[parsedRow][colIndex] = newObj;
        TrackCellModification(parsedRow, colIndex, oldValue);
        var gridRow = FindFirstGridRowForParsedIndex(parsedRow);
        if (gridRow >= 0)
        {
            _grid.InvalidateCell(colIndex + 1, gridRow);
        }

        _isDirty = true;
        UpdateStatusBar();
        UpdateWindowTitle();
        UpdateItemDescriptionPreview();
        return true;
    }

    private void ApplyIconPickFromPicker(IconAtlasPickerForm picker, int newIconId, int newIndex)
    {
        if (!TryGetIconApplyTargetGridRows(picker.BoundGridRow, out var gridRows))
        {
            return;
        }

        ApplyIconPickToGridRows(gridRows, picker.BoundTableColIcon, picker.BoundTableColIndex, newIconId, newIndex);
    }

    private void ApplyIconPickToGridRows(
        IReadOnlyList<int> gridRows,
        int tableColIcon,
        int tableColIndex,
        int newIconId,
        int newIndex)
    {
        if (_parsedTable is null || gridRows.Count == 0)
        {
            return;
        }

        var idObj = MapIntForColumnType(_parsedTable.ColumnTypes[tableColIcon], newIconId);
        var ixObj = MapIntForColumnType(_parsedTable.ColumnTypes[tableColIndex], newIndex);
        var gridColIcon = tableColIcon + 1;
        var gridColIndex = tableColIndex + 1;
        var changed = false;

        CaptureSnapshotForUndo();
        foreach (var gridRowIndex in gridRows)
        {
            if (gridRowIndex < 0 || gridRowIndex >= _grid.RowCount)
            {
                continue;
            }

            if (!TryGetParsedRowIndexFromGridRow(gridRowIndex, out var parsedRow)
                || parsedRow < 0
                || parsedRow >= _parsedTable.Rows.Count)
            {
                continue;
            }

            var row = _parsedTable.Rows[parsedRow];
            if (Equals(row[tableColIcon], idObj) && Equals(row[tableColIndex], ixObj))
            {
                continue;
            }

            var oldIcon = row[tableColIcon];
            var oldIndex = row[tableColIndex];
            row[tableColIcon] = idObj;
            row[tableColIndex] = ixObj;
            TrackCellModification(parsedRow, tableColIcon, oldIcon);
            TrackCellModification(parsedRow, tableColIndex, oldIndex);
            _grid.InvalidateCell(gridColIcon, gridRowIndex);
            _grid.InvalidateCell(gridColIndex, gridRowIndex);
            changed = true;
        }

        if (!changed)
        {
            if (_undoStack.Count > 0)
            {
                _undoStack.Pop();
            }

            return;
        }

        _isDirty = true;
        UpdateStatusBar();
        UpdateWindowTitle();
        UpdateItemDescriptionPreview();
    }

    private void GridEditingControlShowing(object? sender, DataGridViewEditingControlShowingEventArgs e)
    {
        if (e.Control is not TextBox tb)
        {
            return;
        }

        tb.AcceptsReturn = false;
        tb.Multiline = false;
        tb.AcceptsTab = false;
        tb.KeyDown -= EditingTextBox_KeyDown;
        tb.KeyDown += EditingTextBox_KeyDown;
    }

    private void EditingTextBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (_grid.CurrentCell is not { ColumnIndex: > 0 })
        {
            return;
        }

        if (e.Control && e.KeyCode == Keys.C)
        {
            TryCopyCurrentEditingCell();
            e.Handled = true;
            e.SuppressKeyPress = true;
            return;
        }

        if (e.Control && e.KeyCode == Keys.V)
        {
            TryPasteToCurrentEditingCell();
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
    }

    private void GridCellMouseDown(object? sender, DataGridViewCellMouseEventArgs e)
    {
        if (e.RowIndex >= 0 && e.ColumnIndex > 0)
        {
            _activeFilterColumnIndex = e.ColumnIndex - 1;
            SyncFilterColumns();
            SaveFilterToolState();
        }

        if (e.Button != MouseButtons.Right || e.RowIndex < 0 || e.ColumnIndex <= 0)
        {
            return;
        }

        if (_grid.IsCurrentCellInEditMode)
        {
            return;
        }

        _grid.ClearSelection();
        _grid.CurrentCell = _grid.Rows[e.RowIndex].Cells[e.ColumnIndex];
        if (_rowSelectionMode)
        {
            _grid.Rows[e.RowIndex].Selected = true;
        }
        else
        {
            _grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Selected = true;
        }
    }

    private void GridColumnHeaderMouseClick(object? sender, DataGridViewCellMouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || _parsedTable is null || e.ColumnIndex < 0)
        {
            return;
        }

        var col = _grid.Columns[e.ColumnIndex];
        if (!TryGetSortDataColumnIndexFromGridColumn(col, out var dataIdx))
        {
            return;
        }

        if (_sortActive && _sortDataColumnIndex == dataIdx)
        {
            _sortAscending = !_sortAscending;
        }
        else
        {
            _sortDataColumnIndex = dataIdx;
            _sortAscending = true;
        }

        _sortActive = true;
        RunWithVisibleRowReorderPreservingSelection(() =>
        {
            SortVisibleParsedRowsByDataColumn(_sortDataColumnIndex, _sortAscending);
            UpdateSortGlyphDisplay();
            _grid.Refresh();
        });
    }

    private void RunWithVisibleRowReorderPreservingSelection(Action action)
    {
        var selectedParsed = new HashSet<int>();
        foreach (DataGridViewRow r in _grid.SelectedRows)
        {
            if (r.Index >= 0 && TryGetParsedRowIndexFromGridRow(r.Index, out var p))
            {
                selectedParsed.Add(p);
            }
        }

        int? anchorParsed = null;
        var anchorCol = 0;
        if (_grid.CurrentCell is { RowIndex: >= 0, ColumnIndex: >= 0 } cc)
        {
            anchorCol = cc.ColumnIndex;
            if (TryGetParsedRowIndexFromGridRow(cc.RowIndex, out var p))
            {
                anchorParsed = p;
            }
        }

        action();

        if (_parsedTable is null)
        {
            return;
        }

        if (_rowSelectionMode && selectedParsed.Count > 0)
        {
            _grid.ClearSelection();
            foreach (var p in selectedParsed)
            {
                var g = FindFirstGridRowForParsedIndex(p);
                if (g >= 0)
                {
                    _grid.Rows[g].Selected = true;
                }
            }
        }

        if (anchorParsed is int ap && anchorCol >= 0 && anchorCol < _grid.Columns.Count)
        {
            var g = FindFirstGridRowForParsedIndex(ap);
            if (g >= 0)
            {
                _grid.CurrentCell = _grid.Rows[g].Cells[anchorCol];
                if (!_rowSelectionMode)
                {
                    _grid.ClearSelection();
                    _grid.Rows[g].Cells[anchorCol].Selected = true;
                }
            }
        }
    }

    private void UpdateSortGlyphDisplay()
    {
        foreach (DataGridViewColumn c in _grid.Columns)
        {
            c.HeaderCell.SortGlyphDirection = SortOrder.None;
        }

        if (!_sortActive || _parsedTable is null)
        {
            return;
        }

        var key = _sortDataColumnIndex < 0 ? "rowIndex" : $"c{_sortDataColumnIndex}";
        DataGridViewColumn? target = null;
        foreach (DataGridViewColumn c in _grid.Columns)
        {
            if (c.Name == key)
            {
                target = c;
                break;
            }
        }

        if (target is null)
        {
            return;
        }

        target.HeaderCell.SortGlyphDirection = _sortAscending ? SortOrder.Ascending : SortOrder.Descending;
    }

    private void SortVisibleParsedRowsByDataColumn(int dataColumnIndex, bool ascending)
    {
        if (_parsedTable is null || _visibleParsedRows.Count <= 1)
        {
            return;
        }

        var table = _parsedTable;
        var colType = 1;
        if (dataColumnIndex >= 0 && dataColumnIndex < table.ColumnTypes.Length)
        {
            colType = table.ColumnTypes[dataColumnIndex];
        }

        _visibleParsedRows.Sort((ra, rb) =>
        {
            int c;
            if (dataColumnIndex < 0)
            {
                c = ra.CompareTo(rb);
            }
            else
            {
                var va = ra >= 0 && ra < table.Rows.Count ? table.Rows[ra][dataColumnIndex] : null;
                var vb = rb >= 0 && rb < table.Rows.Count ? table.Rows[rb][dataColumnIndex] : null;
                c = CompareTypedLdtCells(va, vb, colType);
            }

            if (c != 0)
            {
                return ascending ? c : -c;
            }

            return ra.CompareTo(rb);
        });
    }

    private static bool TryGetSortDataColumnIndexFromGridColumn(DataGridViewColumn col, out int dataColumnIndex)
    {
        if (string.Equals(col.Name, "rowIndex", StringComparison.Ordinal))
        {
            dataColumnIndex = -1;
            return true;
        }

        if (col.Name.StartsWith("c", StringComparison.Ordinal)
            && col.Name.Length > 1
            && int.TryParse(col.Name.AsSpan(1), out var idx)
            && idx >= 0)
        {
            dataColumnIndex = idx;
            return true;
        }

        dataColumnIndex = 0;
        return false;
    }

    private static int CompareTypedLdtCells(object? a, object? b, int cellType)
    {
        switch (cellType)
        {
            case 0:
                return Convert.ToUInt32(a ?? 0).CompareTo(Convert.ToUInt32(b ?? 0));
            case 1:
                return string.CompareOrdinal(a?.ToString() ?? "", b?.ToString() ?? "");
            case 2:
                {
                    var ba = Convert.ToInt32(a ?? 0) != 0;
                    var bb = Convert.ToInt32(b ?? 0) != 0;
                    return ba.CompareTo(bb);
                }
            case 3:
                return Convert.ToInt32(a ?? 0).CompareTo(Convert.ToInt32(b ?? 0));
            case 4:
                return Convert.ToSingle(a ?? 0f).CompareTo(Convert.ToSingle(b ?? 0f));
            default:
                return string.CompareOrdinal(a?.ToString() ?? "", b?.ToString() ?? "");
        }
    }

    private void MainForm_KeyDown(object? sender, KeyEventArgs e)
    {
        if (ActiveForm is { } active && !ReferenceEquals(active, this))
        {
            return;
        }

        if (e.Shift && e.KeyCode == Keys.A)
        {
            e.SuppressKeyPress = true;
            if (_hotKeyShiftARegistered)
            {
                return;
            }

            ToggleIconPickerWindow();
            return;
        }

        if (e.Shift && e.KeyCode == Keys.P)
        {
            e.SuppressKeyPress = true;
            if (_hotKeyShiftPRegistered)
            {
                return;
            }

            if (_menuViewPreviewItem is not null)
            {
                _menuViewPreviewItem.Checked = !_menuViewPreviewItem.Checked;
                CommitItemDescriptionPanelSettingFromMenu();
            }

            return;
        }

        if (e.Control && e.KeyCode == Keys.F)
        {
            e.SuppressKeyPress = true;
            if (_hotKeyInsertRegistered)
            {
                return;
            }

            ToggleFilterToolWindow();
            return;
        }

        if (e.KeyCode == Keys.F3)
        {
            RunQuickFindNext();
            e.SuppressKeyPress = true;
            return;
        }

        if (e.KeyCode == Keys.Escape && _quickFindBox.Focused)
        {
            _grid.Focus();
            e.SuppressKeyPress = true;
            return;
        }

        if (e.Control && e.KeyCode == Keys.S)
        {
            SaveToCurrentFile();
            e.SuppressKeyPress = true;
            return;
        }

        if (e.Control && e.KeyCode == Keys.G)
        {
            ToggleSelectionMode();
            e.SuppressKeyPress = true;
            return;
        }

        if (e.Alt && e.KeyCode == Keys.S)
        {
            ToggleModificationTracker();
            e.SuppressKeyPress = true;
            return;
        }

        if (e.Alt && e.KeyCode == Keys.Z)
        {
            if (TryRevertHoveredModification())
            {
                e.SuppressKeyPress = true;
            }

            return;
        }

        if (e.Control && e.KeyCode == Keys.Z)
        {
            Undo();
            e.SuppressKeyPress = true;
            return;
        }

        if (e.Control && e.KeyCode == Keys.Y)
        {
            Redo();
            e.SuppressKeyPress = true;
            return;
        }

        if (e.Control && e.KeyCode == Keys.C)
        {
            if (HandleEditingCellClipboardShortcut(e))
            {
                return;
            }

            CopySelectionByMode();
            e.SuppressKeyPress = true;
            return;
        }

        if (e.Control && e.KeyCode == Keys.V)
        {
            if (HandleEditingCellClipboardShortcut(e))
            {
                return;
            }

            PasteSelectionByMode();
            e.SuppressKeyPress = true;
            return;
        }

        if (e.Shift && e.KeyCode == Keys.V)
        {
            DuplicateCurrentRowBelow();
            e.SuppressKeyPress = true;
            return;
        }

        if (e.KeyCode == Keys.Delete)
        {
            DeleteSelectedRows();
            e.SuppressKeyPress = true;
            return;
        }

    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // 全局热键：OnActivated + IMessageFilter(WM_ACTIVATEAPP)。WM_ACTIVATEAPP 会发到当前前台的顶层面（含功能菜单），不能仅在 MainForm.WndProc 处理。
        if (!_hotKeyActivateAppMessageFilterAdded)
        {
            Application.AddMessageFilter(this);
            _hotKeyActivateAppMessageFilterAdded = true;
        }
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        _appThreadInForegroundForHotKeys = true;
        RequestGlobalHotKeyRegistrationWithRetry();
    }

    protected override void OnDeactivate(EventArgs e)
    {
        // 焦点移到本窗体拥有的辅助窗（功能菜单、选图窗、物品预览等）时勿卸载：否则在这些窗里按全局热键无法送达本句柄。
        if (!IsFormInThisOwnershipChain(Form.ActiveForm))
        {
            UnregisterMainFormGlobalHotKeys();
        }

        base.OnDeactivate(e);
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        TryRemoveHotKeyActivateAppMessageFilter();
        UnregisterMainFormGlobalHotKeys();
        base.OnHandleDestroyed(e);
    }

    /// <summary>是否为 <see cref="Owner"/> 链上最终属主为本主窗的窗体（含本窗）。</summary>
    private bool IsFormInThisOwnershipChain(Form? start)
    {
        for (var f = start; f is not null; f = f.Owner)
        {
            if (ReferenceEquals(f, this))
            {
                return true;
            }
        }

        return false;
    }

    bool IMessageFilter.PreFilterMessage(ref Message m)
    {
        if (m.Msg != WmActivateApp || _mainFormClosing)
        {
            return false;
        }

        if (m.WParam == IntPtr.Zero)
        {
            _appThreadInForegroundForHotKeys = false;
            _globalHotKeyRetryTimer.Stop();
            UnregisterMainFormGlobalHotKeys();
        }
        else
        {
            _appThreadInForegroundForHotKeys = true;
            RequestGlobalHotKeyRegistrationWithRetry();
        }

        return false;
    }

    private void TryRemoveHotKeyActivateAppMessageFilter()
    {
        if (!_hotKeyActivateAppMessageFilterAdded)
        {
            return;
        }

        try
        {
            Application.RemoveMessageFilter(this);
        }
        catch
        {
            // ignore double-remove / teardown races
        }

        _hotKeyActivateAppMessageFilterAdded = false;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmHotKey)
        {
            var id = m.WParam.ToInt32();
            BeginInvoke(() => HandleGlobalHotKey(id));
            m.Result = IntPtr.Zero;
            return;
        }

        base.WndProc(ref m);
    }

    private bool AnyMainFormGlobalHotKeyMissing() =>
        !_hotKeyInsertRegistered || !_hotKeyShiftPRegistered || !_hotKeyShiftARegistered;

    private void RequestGlobalHotKeyRegistrationWithRetry()
    {
        RegisterMainFormGlobalHotKeys();
        if (!_appThreadInForegroundForHotKeys || _mainFormClosing)
        {
            _globalHotKeyRetryTimer.Stop();
            return;
        }

        if (!AnyMainFormGlobalHotKeyMissing())
        {
            _globalHotKeyRetryTimer.Stop();
            return;
        }

        _globalHotKeyRetryRemaining = 14;
        _globalHotKeyRetryTimer.Stop();
        _globalHotKeyRetryTimer.Start();
    }

    private void GlobalHotKeyRetryTimerOnTick(object? sender, EventArgs e)
    {
        if (!_appThreadInForegroundForHotKeys || _mainFormClosing)
        {
            _globalHotKeyRetryTimer.Stop();
            return;
        }

        RegisterMainFormGlobalHotKeys();
        if (!AnyMainFormGlobalHotKeyMissing())
        {
            _globalHotKeyRetryTimer.Stop();
            return;
        }

        if (--_globalHotKeyRetryRemaining < 0)
        {
            _globalHotKeyRetryTimer.Stop();
        }
    }

    private void RegisterMainFormGlobalHotKeys()
    {
        if (!IsHandleCreated)
        {
            return;
        }

        var h = Handle;
        if (h == IntPtr.Zero)
        {
            return;
        }

        UnregisterMainFormGlobalHotKeys();
        var modNr = ModNorepeat;
        _hotKeyInsertRegistered = RegisterHotKey(h, HotKeyIdInsert, modNr | ModControl, (uint)Keys.F);
        _hotKeyShiftPRegistered = RegisterHotKey(h, HotKeyIdShiftP, modNr | ModShift, (uint)Keys.P);
        _hotKeyShiftARegistered = RegisterHotKey(h, HotKeyIdShiftA, modNr | ModShift, (uint)Keys.A);
        RefreshGlobalHotKeyStatusLabel();
    }

    private void RefreshGlobalHotKeyStatusLabel()
    {
        if (!IsHandleCreated)
        {
            _globalHotKeyStatusLabel.Text = "全局热键: …";
            _globalHotKeyStatusLabel.ToolTipText = "句柄就绪后将显示 Ctrl+F / Shift+P / Shift+A 的注册状态（✓/✗）。";
            return;
        }

        static string Mark(bool ok) => ok ? "✓" : "✗";
        _globalHotKeyStatusLabel.Text =
            $"全局热键 Ctrl+F{Mark(_hotKeyInsertRegistered)} · Shift+P{Mark(_hotKeyShiftPRegistered)} · Shift+A{Mark(_hotKeyShiftARegistered)}";
        _globalHotKeyStatusLabel.ToolTipText =
            "✓ 已 RegisterHotKey（焦点在其它窗口也可触发）；✗ 与第三方冲突或未注册时仅主窗有焦点时可用菜单/键盘。详见 README。";
    }

    private void UnregisterMainFormGlobalHotKeys()
    {
        if (!IsHandleCreated)
        {
            _hotKeyInsertRegistered = false;
            _hotKeyShiftPRegistered = false;
            _hotKeyShiftARegistered = false;
            return;
        }

        var h = Handle;
        if (h == IntPtr.Zero)
        {
            _hotKeyInsertRegistered = false;
            _hotKeyShiftPRegistered = false;
            _hotKeyShiftARegistered = false;
            return;
        }

        if (_hotKeyInsertRegistered)
        {
            UnregisterHotKey(h, HotKeyIdInsert);
            _hotKeyInsertRegistered = false;
        }

        if (_hotKeyShiftPRegistered)
        {
            UnregisterHotKey(h, HotKeyIdShiftP);
            _hotKeyShiftPRegistered = false;
        }

        if (_hotKeyShiftARegistered)
        {
            UnregisterHotKey(h, HotKeyIdShiftA);
            _hotKeyShiftARegistered = false;
        }

        RefreshGlobalHotKeyStatusLabel();
    }

    private void ActivateForGlobalShortcut()
    {
        if (WindowState == FormWindowState.Minimized)
        {
            WindowState = FormWindowState.Normal;
        }

        Activate();
    }

    private void HandleGlobalHotKey(int id)
    {
        ActivateForGlobalShortcut();
        switch (id)
        {
            case HotKeyIdInsert:
                ToggleFilterToolWindow();
                break;
            case HotKeyIdShiftP:
                if (_menuViewPreviewItem is not null)
                {
                    _menuViewPreviewItem.Checked = !_menuViewPreviewItem.Checked;
                    CommitItemDescriptionPanelSettingFromMenu();
                }

                break;
            case HotKeyIdShiftA:
                ToggleIconPickerWindow();
                break;
        }
    }

    private void MainForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        if (!ConfirmDiscardChanges())
        {
            e.Cancel = true;
            return;
        }

        if (!ConfirmModificationRecordsBeforeClose())
        {
            e.Cancel = true;
            return;
        }

        _mainFormClosing = true;
        _modPersistDebounceTimer.Stop();
        TryRemoveHotKeyActivateAppMessageFilter();
        _globalHotKeyRetryTimer.Stop();
        if (_itemPreviewForm is { IsDisposed: false })
        {
            _itemPreviewForm.Dispose();
            _itemPreviewForm = null;
        }

        StopOpenTitleProgress();
        _diskPersistHintTimer.Stop();
        PushFindStateFromFilterToolUi();
        PushReplaceStateFromFilterToolUi();
        PushBatchInsertFromFilterToolUi();
        var feOk = TrySaveFilterToolStateCore(out var feErr);
        var seOk = TrySaveEditorAppSettingsCore(out var seErr);
        if (!feOk || !seOk)
        {
            var lines = new List<string>();
            if (!feOk && feErr is not null)
            {
                lines.Add($"功能菜单状态 (filter-tool-state.json)：{feErr.Message}");
            }

            if (!seOk && seErr is not null)
            {
                lines.Add($"选项 (editor-settings.json)：{seErr.Message}");
            }

            if (lines.Count > 0)
            {
                MessageBox.Show(
                    this,
                    "以下配置未能写入磁盘。本次会话内的修改仍然有效，但下次启动可能无法恢复：\n\n" + string.Join("\n", lines),
                    "本地配置未保存",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        _diskPersistHintTimer.Dispose();
        _globalHotKeyRetryTimer.Dispose();
        if (_iconPicker is { IsDisposed: false })
        {
            _iconPicker.Close();
        }

        _iconPicker = null;
        _iconPickerSyncDebounceTimer.Stop();
        _iconPickerSyncDebounceTimer.Dispose();
        _gridDisplayFont?.Dispose();
        _gridDisplayFont = null;
        _grid.DefaultCellStyle.Font = Font;
        _grid.ColumnHeadersDefaultCellStyle.Font = Font;
    }

    private bool ConfirmDiscardChanges()
    {
        if (!_isDirty)
        {
            return true;
        }

        var result = MessageBox.Show(
            "You have unsaved changes. Discard them?",
            "LDT Editor",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);

        return result == DialogResult.Yes;
    }

    private void UpdateStatusBar()
    {
        if (_parsedTable is null)
        {
            _statusLabel.Text = "Rows: -";
            _selectionCountLabel.Text = "已选中: 0 行";
            _historyLabel.Text = "Undo:0 | Redo:0";
            _selectionModeLabel.Text = "选择模式: 行选择";
            _iconPickerBindHintLabel.Text = "";
            RefreshGlobalHotKeyStatusLabel();
            UpdateItemDescriptionPreview();
            return;
        }

        var dirtyText = _isDirty ? " | Modified" : string.Empty;
        _statusLabel.Text = $"Rows: {_parsedTable.Rows.Count}{dirtyText}";
        _historyLabel.Text = $"Undo:{_undoStack.Count} | Redo:{_redoStack.Count}";
        _selectionModeLabel.Text = _rowSelectionMode ? "选择模式: 行选择 (Ctrl+G)" : "选择模式: 块选择 (Ctrl+G)";

        UpdateSelectionCount();
        RefreshGlobalHotKeyStatusLabel();
    }

    private string BuildWindowTitleBase()
    {
        var filePart = string.IsNullOrWhiteSpace(_filePath) ? "Untitled" : Path.GetFileName(_filePath);
        var dirtyText = _isDirty ? " *" : string.Empty;
        return $"LDT Editor - {filePart}{dirtyText}";
    }

    private void UpdateWindowTitle()
    {
        Text = BuildWindowTitleBase();
        UpdateRecentFilePinMenu();
    }

    private void UpdateRecentFilePinMenu()
    {
        if (string.IsNullOrWhiteSpace(_filePath) || !File.Exists(_filePath))
        {
            _menuPinCurrentFile.Enabled = false;
            _menuPinCurrentFile.Text = "钉选当前文件到最近(&P)";
            return;
        }

        _menuPinCurrentFile.Enabled = true;
        var doc = LoadRecentDocument();
        NormalizeRecentDocument(doc);
        var norm = Path.GetFullPath(_filePath);
        var pinned = doc.Pinned.Exists(p => string.Equals(Path.GetFullPath(p), norm, StringComparison.OrdinalIgnoreCase));
        _menuPinCurrentFile.Text = pinned ? "取消钉选当前文件(&P)" : "钉选当前文件到最近(&P)";
    }

    private static void EnableDoubleBuffering(LdtDataGridView grid)
    {
        var prop = typeof(DataGridView).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        prop?.SetValue(grid, true, null);
    }

    private void ApplyClassicEditorTheme()
    {
        BackColor = Color.FromArgb(236, 236, 236);
        _grid.BackgroundColor = Color.FromArgb(242, 242, 242);
        _grid.GridColor = Color.FromArgb(208, 208, 208);
        _grid.BorderStyle = BorderStyle.FixedSingle;
        _grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        _grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
        _grid.EnableHeadersVisualStyles = false;
        _grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(230, 230, 230);
        _grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(50, 50, 50);
        _grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        _grid.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.False;
        _grid.ColumnHeadersDefaultCellStyle.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Regular, GraphicsUnit.Point);
        _grid.ColumnHeadersHeight = 28;
        _grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        _grid.DefaultCellStyle.BackColor = Color.FromArgb(250, 250, 250);
        _grid.DefaultCellStyle.ForeColor = Color.FromArgb(40, 40, 40);
        _grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(216, 228, 248);
        _grid.DefaultCellStyle.SelectionForeColor = Color.FromArgb(28, 28, 28);
        _grid.DefaultCellStyle.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Regular, GraphicsUnit.Point);
        _grid.RowTemplate.Height = 21;
        _grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(245, 245, 245);

        _statusStrip.BackColor = Color.FromArgb(230, 230, 230);
        _statusStrip.ForeColor = Color.FromArgb(45, 45, 45);
        _statusStrip.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Regular, GraphicsUnit.Point);
    }

    private void InitializeContextMenu()
    {
        var clearItem = new ToolStripMenuItem("清空单元格");
        var insertRowBelowItem = new ToolStripMenuItem("向下插入空行...");
        var fillDownItem = new ToolStripMenuItem("复制整行到下一行\tShift+V");
        var deleteRowsItem = new ToolStripMenuItem("删除选中行\tDel");

        _contextCopyItem.Click += (_, _) => CopySelectionByMode();
        _contextPasteItem.Click += (_, _) => PasteSelectionByMode();
        clearItem.Click += (_, _) => ClearCurrentCell();
        insertRowBelowItem.Click += (_, _) => InsertEmptyRowBelowCurrent();
        fillDownItem.Click += (_, _) => DuplicateCurrentRowBelow();
        deleteRowsItem.Click += (_, _) => DeleteSelectedRows();
        _pickIconFromSpfItem.Click += (_, _) =>
        {
            if (_grid.CurrentCell is { RowIndex: >= 0 } cc)
            {
                TryOpenIconPickerForRow(cc.RowIndex);
            }
        };

        _cellContextMenu.Items.Add(_contextCopyItem);
        _cellContextMenu.Items.Add(_contextPasteItem);
        _cellContextMenu.Items.Add(new ToolStripSeparator());
        _cellContextMenu.Items.Add(_pickIconFromSpfItem);
        _cellContextMenu.Items.Add(new ToolStripSeparator());
        _cellContextMenu.Items.Add(clearItem);
        _cellContextMenu.Items.Add(insertRowBelowItem);
        _cellContextMenu.Items.Add(fillDownItem);
        _cellContextMenu.Items.Add(deleteRowsItem);
        _cellContextMenu.Opening += (_, e) =>
        {
            var valid = _grid.CurrentCell is { RowIndex: >= 0, ColumnIndex: > 0 } && _parsedTable is not null;
            var editing = valid && IsEditingCellActive();
            _contextCopyItem.Text = editing ? "复制当前格\tCtrl+C" : "复制\tCtrl+C";
            _contextPasteItem.Text = editing ? "粘贴到当前格\tCtrl+V" : "粘贴\tCtrl+V";
            foreach (ToolStripItem item in _cellContextMenu.Items)
            {
                item.Enabled = valid || item is ToolStripSeparator;
            }

            var spfOk = !string.IsNullOrWhiteSpace(_editorSettings.SpfArchivePath) &&
                        File.Exists(_editorSettings.SpfArchivePath.Trim());
            _pickIconFromSpfItem.Enabled = valid && spfOk;

            if (!valid)
            {
                e.Cancel = true;
            }
        };

        _grid.ContextMenuStrip = _cellContextMenu;
    }

    private void CopySelectionByMode()
    {
        if (_parsedTable is null || _grid.CurrentCell is not { RowIndex: >= 0, ColumnIndex: > 0 } cell)
        {
            return;
        }

        if (TryCopyCurrentEditingCell())
        {
            return;
        }

        if (_rowSelectionMode)
        {
            var rowIndexes = _grid.SelectedRows
                .Cast<DataGridViewRow>()
                .Select(r => r.Index)
                .Where(i => i >= 0 && i < _grid.RowCount)
                .Distinct()
                .OrderBy(i => i)
                .ToList();

            if (rowIndexes.Count == 0)
            {
                rowIndexes.Add(cell.RowIndex);
            }

            var lines = new List<string>(rowIndexes.Count);
            foreach (var gridRow in rowIndexes)
            {
                if (!TryGetParsedRowIndexFromGridRow(gridRow, out var parsedRow))
                {
                    continue;
                }

                var row = _parsedTable.Rows[parsedRow];
                lines.Add(string.Join('\t', row.Select(v => v?.ToString() ?? string.Empty)));
            }

            Clipboard.SetText(string.Join(Environment.NewLine, lines));
            return;
        }

        var clip = _grid.GetClipboardContent();
        if (clip is not null)
        {
            Clipboard.SetDataObject(clip);
        }
    }

    private void PasteSelectionByMode()
    {
        if (_parsedTable is null || _grid.CurrentCell is not { RowIndex: >= 0, ColumnIndex: > 0 } cell)
        {
            return;
        }

        if (TryPasteToCurrentEditingCell())
        {
            return;
        }

        var text = Clipboard.ContainsText() ? Clipboard.GetText() : string.Empty;
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var rows = ParseClipboardRows(text);
        if (rows.Count == 0)
        {
            return;
        }

        if (!TryGetParsedRowIndexFromGridRow(cell.RowIndex, out var startParsedRow))
        {
            return;
        }

        var startCol = _rowSelectionMode ? 1 : cell.ColumnIndex;
        var changed = 0;
        CaptureSnapshotForUndo();
        _suppressHistory = true;
        try
        {
            for (var r = 0; r < rows.Count; r++)
            {
                var targetParsed = startParsedRow + r;
                if (targetParsed >= _parsedTable.Rows.Count)
                {
                    break;
                }

                var gridRow = FindFirstGridRowForParsedIndex(targetParsed);
                if (gridRow < 0)
                {
                    break;
                }

                var cols = rows[r];
                for (var c = 0; c < cols.Count; c++)
                {
                    var targetCol = startCol + c;
                    if (targetCol <= 0 || targetCol >= _grid.Columns.Count)
                    {
                        break;
                    }

                    if (ApplyCellValue(gridRow, targetCol, cols[c], showError: false))
                    {
                        changed++;
                    }
                }
            }
        }
        finally
        {
            _suppressHistory = false;
        }

        if (changed == 0)
        {
            if (_undoStack.Count > 0)
            {
                _undoStack.Pop();
            }
            MessageBox.Show("粘贴内容未生效，请检查数据类型或粘贴范围。", "粘贴", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private void ClearCurrentCell()
    {
        if (_grid.CurrentCell is not { RowIndex: >= 0, ColumnIndex: > 0 } cell || _parsedTable is null)
        {
            return;
        }

        var value = GetDefaultCellValueText(_parsedTable.ColumnTypes[cell.ColumnIndex - 1]);
        ApplyCellValue(cell.RowIndex, cell.ColumnIndex, value);
    }

    private bool ApplyCellValue(int gridRowIndex, int gridColumnIndex, string raw, bool showError = true)
    {
        if (_parsedTable is null || gridRowIndex < 0 || gridColumnIndex <= 0)
        {
            return false;
        }

        if (gridRowIndex >= _grid.RowCount)
        {
            return false;
        }

        if (!TryGetParsedRowIndexFromGridRow(gridRowIndex, out var parsedRow))
        {
            return false;
        }

        var colIndex = gridColumnIndex - 1;
        if (parsedRow >= _parsedTable.Rows.Count || colIndex >= _parsedTable.ColumnNames.Length)
        {
            return false;
        }

        if (!TryParseCellValue(_parsedTable.ColumnTypes[colIndex], raw, out var parsed, out var error))
        {
            if (showError)
            {
                MessageBox.Show(error ?? "Invalid value.", "Edit Cell Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            return false;
        }

        var oldValue = _parsedTable.Rows[parsedRow][colIndex];
        if (Equals(oldValue, parsed))
        {
            return false;
        }

        if (!_suppressHistory)
        {
            CaptureSnapshotForUndo();
        }

        _parsedTable.Rows[parsedRow][colIndex] = parsed;
        TrackCellModification(parsedRow, colIndex, oldValue);
        _grid.InvalidateCell(gridColumnIndex, gridRowIndex);
        _isDirty = true;
        UpdateStatusBar();
        UpdateWindowTitle();
        return true;
    }

    private static string GetDefaultCellValueText(int cellType)
    {
        return cellType switch
        {
            0 => "0",
            1 => string.Empty,
            2 => "0",
            3 => "0",
            4 => "0",
            _ => string.Empty
        };
    }

    private void InsertEmptyRowBelowCurrent()
    {
        if (_grid.CurrentCell is not { RowIndex: >= 0, ColumnIndex: > 0 } cell || _parsedTable is null)
        {
            return;
        }

        using var dlg = new InsertEmptyRowsDialog();
        if (dlg.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var count = dlg.Quantity;

        if (!TryGetParsedRowIndexFromGridRow(cell.RowIndex, out var parsedRow))
        {
            return;
        }

        CaptureSnapshotForUndo();
        var insertParsedAt = parsedRow + 1;
        for (var i = 0; i < count; i++)
        {
            var newRow = BuildDefaultRowValues(_parsedTable.ColumnTypes);
            _parsedTable.Rows.Insert(insertParsedAt + i, newRow);
        }

        LoadTable(null);
        var focusGrid = FindFirstGridRowForParsedIndex(insertParsedAt);
        if (focusGrid >= 0 && focusGrid < _grid.RowCount)
        {
            _grid.CurrentCell = _grid.Rows[focusGrid].Cells[1];
            _grid.Rows[focusGrid].Cells[1].Selected = true;
        }

        _isDirty = true;
        UpdateStatusBar();
        UpdateWindowTitle();
    }

    private void DuplicateCurrentRowBelow()
    {
        if (_grid.CurrentCell is not { RowIndex: >= 0, ColumnIndex: > 0 } cell || _parsedTable is null)
        {
            return;
        }

        if (!TryGetParsedRowIndexFromGridRow(cell.RowIndex, out var parsedRow))
        {
            return;
        }

        CaptureSnapshotForUndo();
        var insertParsedAt = parsedRow + 1;
        var source = _parsedTable.Rows[parsedRow];
        var copy = new object?[source.Length];
        for (var c = 0; c < source.Length; c++)
        {
            copy[c] = source[c];
        }

        _parsedTable.Rows.Insert(insertParsedAt, copy);
        LoadTable(null);
        var focusGrid = FindFirstGridRowForParsedIndex(insertParsedAt);
        if (focusGrid >= 0 && focusGrid < _grid.RowCount)
        {
            _grid.CurrentCell = _grid.Rows[focusGrid].Cells[1];
            _grid.Rows[focusGrid].Cells[1].Selected = true;
        }

        _isDirty = true;
        UpdateStatusBar();
        UpdateWindowTitle();
    }

    private void DeleteSelectedRows()
    {
        if (_parsedTable is null || _grid.RowCount == 0)
        {
            return;
        }

        var selectedGridRows = _grid.SelectedRows
            .Cast<DataGridViewRow>()
            .Select(r => r.Index)
            .Where(i => i >= 0 && i < _grid.RowCount)
            .Distinct()
            .ToList();

        if (selectedGridRows.Count == 0 && _grid.CurrentCell is { RowIndex: >= 0 } currentCell)
        {
            selectedGridRows.Add(currentCell.RowIndex);
        }

        if (selectedGridRows.Count == 0)
        {
            return;
        }

        var parsedToRemove = new List<int>();
        foreach (var g in selectedGridRows)
        {
            if (TryGetParsedRowIndexFromGridRow(g, out var p))
            {
                parsedToRemove.Add(p);
            }
        }

        parsedToRemove = parsedToRemove.Distinct().OrderByDescending(p => p).ToList();
        if (parsedToRemove.Count == 0)
        {
            return;
        }

        CaptureSnapshotForUndo();
        foreach (var p in parsedToRemove)
        {
            if (p < 0 || p >= _parsedTable.Rows.Count)
            {
                continue;
            }

            _parsedTable.Rows.RemoveAt(p);
        }

        LoadTable(null);
        if (_grid.RowCount > 0)
        {
            var focusParsed = Math.Clamp(parsedToRemove.Min(), 0, _parsedTable.Rows.Count - 1);
            var nextGrid = FindFirstGridRowForParsedIndex(focusParsed);
            if (nextGrid < 0)
            {
                nextGrid = 0;
            }

            var nextCol = _rowSelectionMode ? 0 : 1;
            nextCol = Math.Min(nextCol, _grid.Columns.Count - 1);
            _grid.CurrentCell = _grid.Rows[nextGrid].Cells[nextCol];
        }

        _isDirty = true;
        UpdateStatusBar();
        UpdateWindowTitle();
    }

    private void ToggleSelectionMode()
    {
        _rowSelectionMode = !_rowSelectionMode;
        _grid.ClearSelection();
        _grid.SelectionMode = _rowSelectionMode
            ? DataGridViewSelectionMode.FullRowSelect
            : DataGridViewSelectionMode.CellSelect;
        UpdateStatusBar();
    }

    private void ResetHistory()
    {
        _undoStack.Clear();
        _redoStack.Clear();
    }

    private void CaptureSnapshotForUndo()
    {
        if (_parsedTable is null || _suppressHistory)
        {
            return;
        }

        _undoStack.Push(new EditorSnapshot(CloneRows(_parsedTable.Rows), _isDirty));
        _redoStack.Clear();
    }

    private void Undo()
    {
        if (_parsedTable is null || _undoStack.Count == 0)
        {
            return;
        }

        _redoStack.Push(new EditorSnapshot(CloneRows(_parsedTable.Rows), _isDirty));
        var snapshot = _undoStack.Pop();
        ApplySnapshot(snapshot);
    }

    private void Redo()
    {
        if (_parsedTable is null || _redoStack.Count == 0)
        {
            return;
        }

        _undoStack.Push(new EditorSnapshot(CloneRows(_parsedTable.Rows), _isDirty));
        var snapshot = _redoStack.Pop();
        ApplySnapshot(snapshot);
    }

    private void ApplySnapshot(EditorSnapshot snapshot)
    {
        if (_parsedTable is null)
        {
            return;
        }

        _parsedTable = _parsedTable with { Rows = CloneRows(snapshot.Rows) };
        _isDirty = snapshot.IsDirty;
        LoadTable(null);
        ReconcileModificationMap();
        UpdateStatusBar();
        UpdateWindowTitle();
    }

    private static List<object?[]> CloneRows(List<object?[]> sourceRows)
    {
        return sourceRows.Select(row => row.ToArray()).ToList();
    }

    private void ClearModificationTracker()
    {
        _modChanges.Clear();
        _hoverTrackedCell = null;
        _modPersistDebounceTimer.Stop();
    }

    private void TrackCellModification(int parsedRow, int colIndex, object? valueBeforeEdit)
    {
        if (_parsedTable is null || parsedRow < 0 || parsedRow >= _parsedTable.Rows.Count
            || colIndex < 0 || colIndex >= _parsedTable.ColumnNames.Length)
        {
            return;
        }

        var key = (parsedRow, colIndex);
        if (!_modChanges.ContainsKey(key))
        {
            _modChanges[key] = valueBeforeEdit;
        }

        var current = _parsedTable.Rows[parsedRow][colIndex];
        if (LdtCellValueComparer.ValuesEqual(_modChanges[key], current))
        {
            _modChanges.Remove(key);
        }

        ScheduleModificationRecordsPersist();
    }

    private void ReconcileModificationMap()
    {
        if (_parsedTable is null || _modChanges.Count == 0)
        {
            return;
        }

        var keys = _modChanges.Keys.ToList();
        foreach (var (row, col) in keys)
        {
            if (row < 0 || row >= _parsedTable.Rows.Count || col < 0 || col >= _parsedTable.ColumnNames.Length)
            {
                _modChanges.Remove((row, col));
                continue;
            }

            var current = _parsedTable.Rows[row][col];
            if (LdtCellValueComparer.ValuesEqual(_modChanges[(row, col)], current))
            {
                _modChanges.Remove((row, col));
            }
        }

        if (_modTrackerVisible)
        {
            _grid.Invalidate();
        }
    }

    private void ScheduleModificationRecordsPersist()
    {
        if (!_editorSettings.AutoPersistModificationRecords || _isDirty || _filePath is null)
        {
            return;
        }

        _modPersistDebounceTimer.Stop();
        _modPersistDebounceTimer.Start();
    }

    private void TryPersistModificationRecordsIfEligible(bool force = false)
    {
        if (_filePath is null || _parsedTable is null)
        {
            return;
        }

        if (_isDirty)
        {
            return;
        }

        if (!force && !_editorSettings.AutoPersistModificationRecords)
        {
            return;
        }

        if (!LdtModificationRecordStore.TrySave(_filePath, _parsedTable.ColumnTypes, _modChanges, out var ex))
        {
            ThrottledPersistDiskHint("change-records", "修改记录未能写入磁盘", ex);
        }
    }

    private void TryDeleteModificationRecordsSidecar()
    {
        if (_filePath is null)
        {
            return;
        }

        LdtModificationRecordStore.TryDelete(_filePath, out _);
    }

    private void TryLoadModificationRecordsFromSidecar()
    {
        if (_parsedTable is null || _filePath is null)
        {
            return;
        }

        if (!LdtModificationRecordStore.TryLoad(_filePath, _parsedTable.ColumnTypes, out var loaded, out var reason))
        {
            if (!string.IsNullOrEmpty(reason))
            {
                ThrottledPersistDiskHint("change-records-load", $"修改记录未加载：{reason}", null);
            }

            return;
        }

        _modChanges.Clear();
        foreach (var kv in loaded)
        {
            var (row, col) = kv.Key;
            if (row < 0 || row >= _parsedTable.Rows.Count || col < 0 || col >= _parsedTable.ColumnNames.Length)
            {
                continue;
            }

            if (!LdtCellValueComparer.ValuesEqual(_parsedTable.Rows[row][col], kv.Value))
            {
                _modChanges[kv.Key] = kv.Value;
            }
        }

        if (_modTrackerVisible && _modChanges.Count > 0)
        {
            _grid.Invalidate();
        }
    }

    private void MenuToggleModificationTracker(object? sender, EventArgs e)
    {
        if (_menuModificationTrackerItem is null)
        {
            return;
        }

        SetModificationTrackerVisible(_menuModificationTrackerItem.Checked);
    }

    private void SetModificationTrackerVisible(bool visible, bool persistSettings = true)
    {
        _modTrackerVisible = visible;
        if (_menuModificationTrackerItem is not null)
        {
            _menuModificationTrackerItem.Checked = visible;
        }

        if (persistSettings && _editorSettings.ModificationTrackerVisible != visible)
        {
            _editorSettings = _editorSettings with { ModificationTrackerVisible = visible };
            SaveEditorAppSettings(false);
        }

        _grid.Invalidate();
    }

    private void ToggleModificationTracker()
    {
        SetModificationTrackerVisible(!_modTrackerVisible);
    }

    private static string FormatModificationCellDisplay(object? value)
    {
        var s = value?.ToString() ?? string.Empty;
        return LdtStringDecodeHelper.TruncatePreview(s, 96);
    }

    private void MainDataGridView_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (!_modTrackerVisible || e.RowIndex < 0 || e.ColumnIndex <= 0)
        {
            return;
        }

        if (!TryGetParsedRowIndexFromGridRow(e.RowIndex, out var parsedRow))
        {
            return;
        }

        var colIndex = e.ColumnIndex - 1;
        if (!_modChanges.ContainsKey((parsedRow, colIndex)))
        {
            return;
        }

        e.CellStyle ??= new DataGridViewCellStyle();
        e.CellStyle.BackColor = ModHighlightBackColor;
        if (_grid.Rows[e.RowIndex].Selected)
        {
            e.CellStyle.SelectionBackColor = ModHighlightSelectionBackColor;
            e.CellStyle.SelectionForeColor = Color.FromArgb(28, 28, 28);
        }
    }

    private void MainDataGridView_CellToolTipTextNeeded(object? sender, DataGridViewCellToolTipTextNeededEventArgs e)
    {
        if (!_modTrackerVisible || e.RowIndex < 0 || e.ColumnIndex <= 0)
        {
            e.ToolTipText = null;
            return;
        }

        if (!TryGetParsedRowIndexFromGridRow(e.RowIndex, out var parsedRow))
        {
            e.ToolTipText = null;
            return;
        }

        var colIndex = e.ColumnIndex - 1;
        if (!_modChanges.TryGetValue((parsedRow, colIndex), out var original))
        {
            e.ToolTipText = null;
            return;
        }

        e.ToolTipText = $"原值: {FormatModificationCellDisplay(original)}{Environment.NewLine}────────{Environment.NewLine}Alt+Z 还原";
    }

    private void GridCellMouseMove(object? sender, DataGridViewCellMouseEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex <= 0)
        {
            _hoverTrackedCell = null;
            return;
        }

        if (!TryGetParsedRowIndexFromGridRow(e.RowIndex, out var parsedRow))
        {
            _hoverTrackedCell = null;
            return;
        }

        var colIndex = e.ColumnIndex - 1;
        _hoverTrackedCell = _modChanges.ContainsKey((parsedRow, colIndex)) ? (parsedRow, colIndex) : null;
    }

    private bool TryRevertHoveredModification()
    {
        if (_parsedTable is null || _hoverTrackedCell is not { } hover)
        {
            return false;
        }

        var (parsedRow, colIndex) = hover;
        if (!_modChanges.TryGetValue(hover, out var original))
        {
            return false;
        }

        if (parsedRow < 0 || parsedRow >= _parsedTable.Rows.Count
            || colIndex < 0 || colIndex >= _parsedTable.ColumnNames.Length)
        {
            return false;
        }

        var current = _parsedTable.Rows[parsedRow][colIndex];
        if (LdtCellValueComparer.ValuesEqual(current, original))
        {
            _modChanges.Remove(hover);
            var gridRow = FindFirstGridRowForParsedIndex(parsedRow);
            if (gridRow >= 0)
            {
                _grid.InvalidateCell(colIndex + 1, gridRow);
            }

            return true;
        }

        CaptureSnapshotForUndo();
        _parsedTable.Rows[parsedRow][colIndex] = original;
        _modChanges.Remove(hover);
        var gridRowIx = FindFirstGridRowForParsedIndex(parsedRow);
        if (gridRowIx >= 0)
        {
            _grid.InvalidateCell(colIndex + 1, gridRowIx);
        }

        _isDirty = true;
        ReconcileModificationMap();
        UpdateStatusBar();
        UpdateWindowTitle();
        UpdateItemDescriptionPreview();
        ScheduleModificationRecordsPersist();
        return true;
    }

    private bool ConfirmModificationRecordsBeforeClose()
    {
        if (!_modTrackerVisible || _modChanges.Count == 0 || _editorSettings.SuppressModificationRecordsClosePrompt)
        {
            return true;
        }

        using var dlg = new ModificationRecordsCloseForm();
        var result = dlg.ShowDialog(this);
        if (result == DialogResult.Cancel)
        {
            return false;
        }

        if (dlg.SuppressInFuture)
        {
            _editorSettings = _editorSettings with { SuppressModificationRecordsClosePrompt = true };
            SaveEditorAppSettings(false);
        }

        if (result == DialogResult.Yes && !_isDirty)
        {
            TryPersistModificationRecordsIfEligible(force: true);
        }
        else if (result == DialogResult.No)
        {
            TryDeleteModificationRecordsSidecar();
        }

        return true;
    }

    private void UpdateSelectionCount()
    {
        if (_parsedTable is null)
        {
            _selectionCountLabel.Text = "已选中: 0 行";
            _iconPickerBindHintLabel.Text = "";
            return;
        }

        if (_rowSelectionMode)
        {
            var selectedIndexes = _grid.SelectedRows
                .Cast<DataGridViewRow>()
                .Select(r => r.Index)
                .Where(i => i >= 0)
                .Distinct()
                .OrderBy(i => i)
                .ToList();
            if (selectedIndexes.Count == 0 && _grid.CurrentCell is { RowIndex: >= 0 } currentCell)
            {
                selectedIndexes.Add(currentCell.RowIndex);
            }

            var rowCount = selectedIndexes.Count;
            var startRow = rowCount > 0 ? selectedIndexes.First() : 0;
            var endRow = rowCount > 0 ? selectedIndexes.Last() : 0;
            _selectionCountLabel.Text = $"已选中: {rowCount} 行 (从 {startRow} 行到 {endRow} 行)";
        }
        else
        {
            var selectedCells = _grid.SelectedCells
                .Cast<DataGridViewCell>()
                .Where(c => c.RowIndex >= 0 && c.ColumnIndex > 0)
                .ToList();
            if (selectedCells.Count == 0 && _grid.CurrentCell is { RowIndex: >= 0, ColumnIndex: > 0 } currentCell)
            {
                selectedCells.Add(currentCell);
            }

            var cellCount = selectedCells.Count;
            var startRow = cellCount > 0 ? selectedCells.Min(c => c.RowIndex) : 0;
            var endRow = cellCount > 0 ? selectedCells.Max(c => c.RowIndex) : 0;
            _selectionCountLabel.Text = $"已选中: {cellCount} 单元格 (从 {startRow} 行到 {endRow} 行)";
        }

        UpdateIconPickerBindHint();
    }

    private void PushReplaceStateFromFilterToolUi()
    {
        if (_filterToolForm is null || _filterToolForm.IsDisposed)
        {
            return;
        }

        _replaceKeyword = _filterToolForm.LiveReplaceKeyword.Trim();
        _filterReplaceText = _filterToolForm.LiveReplaceText;
        _replaceMatchCase = _filterToolForm.LiveReplaceMatchCase;
    }

    private void PushBatchInsertFromFilterToolUi()
    {
        if (_filterToolForm is null || _filterToolForm.IsDisposed)
        {
            return;
        }

        _batchInsertStartText = _filterToolForm.LiveBatchInsertStart;
        _batchInsertStepText = _filterToolForm.LiveBatchInsertStep;
        _batchInsertUseMultiply = _filterToolForm.LiveBatchInsertUseMultiply;
        _batchInsertPrefix = _filterToolForm.LiveBatchInsertPrefix;
        _batchInsertSuffix = _filterToolForm.LiveBatchInsertSuffix;
        _batchInsertOriginalPlacement = _filterToolForm.LiveBatchInsertOriginalPlacement;
        _batchInsertVisibleRowsOnly = _filterToolForm.LiveBatchInsertVisibleRowsOnly;
        _batchInsertPreviewBeforeApply = _filterToolForm.LiveBatchInsertPreviewBeforeApply;
        _batchInsertBoolRule = _filterToolForm.LiveBatchInsertBoolRule;
    }

    private void PushFindStateFromFilterToolUi()
    {
        if (_filterToolForm is null || _filterToolForm.IsDisposed)
        {
            return;
        }

        _filterKeyword = _filterToolForm.LiveKeyword.Trim();
        _filterAllColumns = _filterToolForm.LiveAllColumns;
        _filterMatchCase = _filterToolForm.LiveMatchCase;
        _filterUseRegex = _filterToolForm.LiveUseRegex;
    }

    private void PersistFilterToolUiToDisk()
    {
        PushFindStateFromFilterToolUi();
        PushReplaceStateFromFilterToolUi();
        PushBatchInsertFromFilterToolUi();
        SaveFilterToolState();
    }

    private void ToggleFilterToolWindow()
    {
        if (_filterToolForm is null || _filterToolForm.IsDisposed)
        {
            _filterToolForm = new FilterToolForm(
                ApplyFilterFromTool,
                QueryReplaceFromTool,
                OpenReplacePreviewFromTool,
                ApplyReplaceFromTool,
                ApplyBatchNumberSeriesFromTool,
                ClearFilterFromTool,
                ToggleFilterToolWindow,
                PushReplaceStateFromFilterToolUi,
                PersistFilterToolUiToDisk,
                new EditorToolOptionsBinding
                {
                    Initial = _editorSettings,
                    Apply = ApplyEditorSettingsFromTool
                },
                OpenTraditionalToSimplifiedPreview);
            if (_filterToolLocation is { } location)
            {
                _filterToolForm.StartPosition = FormStartPosition.Manual;
                _filterToolForm.Location = location;
            }

            _filterToolForm.LocationChanged += FilterToolFormOnLocationChanged;
            _filterToolForm.FormClosed += (_, _) => _filterToolForm = null;
        }

        if (_grid.CurrentCell is { ColumnIndex: > 0 } cell)
        {
            _activeFilterColumnIndex = cell.ColumnIndex - 1;
        }

        SyncFilterColumns();
        UpdateFilterToolStatus();

        if (_filterToolForm.Visible)
        {
            _filterToolForm.Hide();
        }
        else
        {
            _filterToolForm.Show(this);
            _filterToolForm.BringToFront();
            ClampFilterToolFormToMainForm();
        }
    }

    private void FilterToolFormOnLocationChanged(object? sender, EventArgs e)
    {
        if (_filterToolForm is null || _filterToolForm.IsDisposed)
        {
            return;
        }

        ClampFilterToolFormToMainForm();
        _filterToolLocation = _filterToolForm.Location;
        SaveFilterToolState();
    }

    /// <summary>将功能菜单（查找/替换/插入/选项）限制在本主窗屏幕矩形内，避免拖出可视工作区。</summary>
    private void ClampFilterToolFormToMainForm()
    {
        if (_filterToolForm is null || _filterToolForm.IsDisposed || !IsHandleCreated || !_filterToolForm.IsHandleCreated)
        {
            return;
        }

        if (WindowState == FormWindowState.Minimized)
        {
            return;
        }

        var host = RectangleToScreen(new Rectangle(0, 0, Width, Height));
        var next = ClampToolWindowLocation(host, _filterToolForm.Width, _filterToolForm.Height, _filterToolForm.Location);
        if (next != _filterToolForm.Location)
        {
            _filterToolForm.Location = next;
        }
    }

    private static Point ClampToolWindowLocation(Rectangle hostScreen, int toolWidth, int toolHeight, Point location)
    {
        var minX = hostScreen.Left;
        var maxX = hostScreen.Right - toolWidth;
        var minY = hostScreen.Top;
        var maxY = hostScreen.Bottom - toolHeight;
        if (maxX < minX)
        {
            maxX = minX;
        }

        if (maxY < minY)
        {
            maxY = minY;
        }

        return new Point(
            Math.Clamp(location.X, minX, maxX),
            Math.Clamp(location.Y, minY, maxY));
    }

    private void SyncFilterColumns()
    {
        if (_filterToolForm is null || _filterToolForm.IsDisposed || _parsedTable is null)
        {
            return;
        }

        _replaceKeyword = _filterToolForm.LiveReplaceKeyword.Trim();
        _filterReplaceText = _filterToolForm.LiveReplaceText;
        _replaceMatchCase = _filterToolForm.LiveReplaceMatchCase;

        _filterToolForm.SetContext(
            _parsedTable.ColumnNames,
            _activeFilterColumnIndex,
            _filterKeyword,
            _filterAllColumns,
            _filterMatchCase,
            _filterUseRegex,
            _replaceKeyword,
            _filterReplaceText,
            _replaceMatchCase);
        _filterToolForm.SyncBatchInsertFromMain(
            _batchInsertStartText,
            _batchInsertStepText,
            _batchInsertUseMultiply,
            _batchInsertPrefix,
            _batchInsertSuffix,
            _batchInsertOriginalPlacement,
            _batchInsertVisibleRowsOnly,
            _batchInsertPreviewBeforeApply,
            _batchInsertBoolRule);
    }

    private void ApplyFilterFromTool(string keyword, bool allColumns, bool matchCase, bool useRegex)
    {
        _filterKeyword = keyword.Trim();
        _filterAllColumns = allColumns;
        _filterMatchCase = matchCase;
        _filterUseRegex = useRegex;
        ApplyFilterToGrid();
        SyncQuickFindBarFromFilterState();
        if (_filterToolForm is { IsDisposed: false })
        {
            SyncFilterColumns();
        }

        SaveFilterToolState();
    }

    private void QueryReplaceFromTool(string keyword, string replaceText, bool matchCase, bool forward)
    {
        if (_parsedTable is null)
        {
            return;
        }

        _replaceKeyword = keyword.Trim();
        _filterReplaceText = replaceText;
        _replaceMatchCase = matchCase;
        if (string.IsNullOrWhiteSpace(_replaceKeyword))
        {
            MessageBox.Show("请先输入替换区的查找关键词。", "替换查询", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var startRow = _replaceSearchRow;
        var startCol = _replaceSearchColumn;
        if (_grid.CurrentCell is { RowIndex: >= 0, ColumnIndex: > 0 } current)
        {
            if (TryGetParsedRowIndexFromGridRow(current.RowIndex, out var pr))
            {
                startRow = pr;
            }

            startCol = current.ColumnIndex - 1;
        }

        var match = FindReplaceMatchFrom(
            _replaceKeyword,
            false,
            _replaceMatchCase,
            false,
            startRow,
            startCol,
            forward);
        if (match is null)
        {
            MessageBox.Show("没有找到匹配项。", "替换查询", MessageBoxButtons.OK, MessageBoxIcon.Information);
            SaveFilterToolState();
            return;
        }

        var (row, col) = match.Value;
        _activeFilterColumnIndex = col;
        _replaceSearchRow = row;
        _replaceSearchColumn = col;
        RevealAndFocusCell(row, col);
        SaveFilterToolState();
    }

    private void OpenReplacePreviewFromTool(string keyword, string replaceText, bool matchCase)
    {
        if (!TryPrepareReplaceCollection(keyword, replaceText, matchCase, out var entries, out var skippedTypeMismatch, out var validationMessage, out var validationTitle))
        {
            if (validationMessage is not null)
            {
                MessageBox.Show(validationMessage, validationTitle ?? "替换预览", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }

            SaveFilterToolState();
            return;
        }

        if (entries.Count == 0)
        {
            var suffix = skippedTypeMismatch > 0 ? $"（另有 {skippedTypeMismatch} 项类型不匹配已跳过）" : string.Empty;
            MessageBox.Show($"没有可预览的替换。{suffix}", "替换预览", MessageBoxButtons.OK, MessageBoxIcon.Information);
            SaveFilterToolState();
            return;
        }

        using var dlg = new ReplacePreviewForm(entries, skippedTypeMismatch);
        if (dlg.ShowDialog(this) != DialogResult.OK)
        {
            SaveFilterToolState();
            return;
        }

        var selected = dlg.GetSelectedEntries();
        if (selected.Count == 0)
        {
            MessageBox.Show("请至少勾选一项后再执行替换。", "替换预览", MessageBoxButtons.OK, MessageBoxIcon.Information);
            SaveFilterToolState();
            return;
        }

        ApplyReplacePreviewEntries(selected);
        var skippedText = skippedTypeMismatch > 0 ? $"（预览列表外另有 {skippedTypeMismatch} 项因类型不匹配已排除）" : string.Empty;
        MessageBox.Show($"替换完成：{selected.Count} 项{skippedText}。", "替换预览", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void OpenTraditionalToSimplifiedPreview()
    {
        if (!TryCollectTraditionalToSimplifiedEntries(out var entries, out var validationMessage, out var validationTitle))
        {
            if (validationMessage is not null)
            {
                MessageBox.Show(validationMessage, validationTitle ?? "繁体转简体", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }

            SaveFilterToolState();
            return;
        }

        if (entries.Count == 0)
        {
            MessageBox.Show("没有需要转换的繁体文本（可见行中的文本列均已为简体或无变化）。", "繁体转简体", MessageBoxButtons.OK, MessageBoxIcon.Information);
            SaveFilterToolState();
            return;
        }

        var hint =
            $"以下列出 {entries.Count} 处可转换的文本（全部文本列 · 当前筛选可见行）。请勾选要写入的项后点击「写入已勾选」。";
        using var dlg = new ReplacePreviewForm(entries, 0, "繁体转简体预览", "写入已勾选", hint, textOnlyColumns: true);
        if (dlg.ShowDialog(this) != DialogResult.OK)
        {
            SaveFilterToolState();
            return;
        }

        var selected = dlg.GetSelectedEntries();
        if (selected.Count == 0)
        {
            MessageBox.Show("请至少勾选一项后再执行转换。", "繁体转简体", MessageBoxButtons.OK, MessageBoxIcon.Information);
            SaveFilterToolState();
            return;
        }

        ApplyReplacePreviewEntries(selected);
        MessageBox.Show($"转换完成：{selected.Count} 项。", "繁体转简体", MessageBoxButtons.OK, MessageBoxIcon.Information);
        SaveFilterToolState();
    }

    private bool TryCollectTraditionalToSimplifiedEntries(
        out List<ReplacePreviewEntry> entries,
        out string? validationMessage,
        out string? validationTitle)
    {
        entries = new List<ReplacePreviewEntry>();
        validationMessage = null;
        validationTitle = null;

        if (_parsedTable is null)
        {
            validationMessage = "请先打开 LDT 文件。";
            validationTitle = "繁体转简体";
            return false;
        }

        var stringColumns = new List<int>();
        for (var c = 0; c < _parsedTable.ColumnNames.Length; c++)
        {
            if (_parsedTable.ColumnTypes[c] == 1)
            {
                stringColumns.Add(c);
            }
        }

        if (stringColumns.Count == 0)
        {
            validationMessage = "当前表没有文本列。";
            validationTitle = "繁体转简体";
            return false;
        }

        for (var r = 0; r < _parsedTable.Rows.Count; r++)
        {
            var gridR0 = FindFirstGridRowForParsedIndex(r);
            if (gridR0 < 0 || !IsDataGridRowVisible(gridR0))
            {
                continue;
            }

            foreach (var c in stringColumns)
            {
                var sourceText = _parsedTable.Rows[r][c] as string ?? string.Empty;
                var replacedText = LdtChineseScriptHelper.TraditionalToSimplified(sourceText);
                if (string.Equals(sourceText, replacedText, StringComparison.Ordinal))
                {
                    continue;
                }

                var colName = c < _parsedTable.ColumnNames.Length && !string.IsNullOrWhiteSpace(_parsedTable.ColumnNames[c])
                    ? _parsedTable.ColumnNames[c]
                    : $"COL_{c}";
                entries.Add(new ReplacePreviewEntry(r, c, colName, sourceText, replacedText, replacedText));
            }
        }

        return true;
    }

    private void ApplyReplaceFromTool(string keyword, string replaceText, bool matchCase)
    {
        if (_editorSettings.RequireReplacePreview)
        {
            MessageBox.Show(
                "已在选项中启用「直接替换须先经过预览」。请使用「预览…」勾选要替换的项后再写入。",
                "直接替换",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            SaveFilterToolState();
            return;
        }

        if (!TryPrepareReplaceCollection(keyword, replaceText, matchCase, out var entries, out var skippedTypeMismatch, out var validationMessage, out var validationTitle))
        {
            if (validationMessage is not null)
            {
                var validationIcon = validationMessage.Contains("正则", StringComparison.Ordinal)
                    ? MessageBoxIcon.Warning
                    : MessageBoxIcon.Information;
                MessageBox.Show(validationMessage, validationTitle ?? "替换", MessageBoxButtons.OK, validationIcon);
            }

            SaveFilterToolState();
            return;
        }

        if (entries.Count == 0)
        {
            var suffix = skippedTypeMismatch > 0 ? $"（另有 {skippedTypeMismatch} 项类型不匹配已跳过）" : string.Empty;
            MessageBox.Show($"没有可应用的替换。{suffix}", "替换", MessageBoxButtons.OK, MessageBoxIcon.Information);
            SaveFilterToolState();
            return;
        }

        var confirm = MessageBox.Show(
            $"将不经预览立即替换 {entries.Count} 处。确定继续吗？",
            "直接替换",
            MessageBoxButtons.OKCancel,
            MessageBoxIcon.Warning);
        if (confirm != DialogResult.OK)
        {
            SaveFilterToolState();
            return;
        }

        ApplyReplacePreviewEntries(entries);
        var skippedText = skippedTypeMismatch > 0 ? $"，跳过 {skippedTypeMismatch} 项（类型不匹配）" : string.Empty;
        MessageBox.Show($"替换完成：{entries.Count} 项{skippedText}。", "替换", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private bool TryBuildBatchInsertPreviewEntries(
        int dataColumnIndex,
        IReadOnlyList<int> orderedGridRows,
        double start,
        bool useMultiply,
        double step,
        string prefix,
        string suffix,
        int originalPlacement,
        int batchBoolRule,
        out List<ReplacePreviewEntry> entries,
        out int skipped)
    {
        entries = new List<ReplacePreviewEntry>();
        skipped = 0;
        if (_parsedTable is null)
        {
            return false;
        }

        var c = dataColumnIndex;
        if (c < 0 || c >= _parsedTable.ColumnNames.Length)
        {
            return false;
        }

        prefix ??= string.Empty;
        suffix ??= string.Empty;
        if (originalPlacement is < 0 or > 2)
        {
            originalPlacement = 0;
        }

        var colType = _parsedTable.ColumnTypes[c];
        var colName = c < _parsedTable.ColumnNames.Length && !string.IsNullOrWhiteSpace(_parsedTable.ColumnNames[c])
            ? _parsedTable.ColumnNames[c]
            : $"COL_{c}";
        var runningMul = start;
        for (var i = 0; i < orderedGridRows.Count; i++)
        {
            if (!TryGetParsedRowIndexFromGridRow(orderedGridRows[i], out var pr) || pr < 0 || pr >= _parsedTable.Rows.Count)
            {
                skipped++;
                continue;
            }

            var v = useMultiply ? runningMul : start + i * step;
            if (useMultiply)
            {
                runningMul *= step;
            }

            object next;
            string newTextDisplay;
            if (colType == 1)
            {
                var orig = _parsedTable.Rows[pr][c]?.ToString() ?? string.Empty;
                var core = prefix + FormatNumericSeriesToken(v) + suffix;
                var combined = originalPlacement switch
                {
                    1 => orig + core,
                    2 => core + orig,
                    _ => core
                };
                if (!TryParseCellValue(1, combined, out next, out _))
                {
                    skipped++;
                    continue;
                }

                newTextDisplay = combined;
            }
            else
            {
                if (!TryCoerceSeriesDoubleToCellValue(v, colType, batchBoolRule, out next))
                {
                    skipped++;
                    continue;
                }

                newTextDisplay = Convert.ToString(next, CultureInfo.InvariantCulture) ?? "";
            }

            if (Equals(_parsedTable.Rows[pr][c], next))
            {
                continue;
            }

            var oldText = _parsedTable.Rows[pr][c]?.ToString() ?? string.Empty;
            entries.Add(new ReplacePreviewEntry(pr, c, colName, oldText, newTextDisplay, next));
        }

        return true;
    }

    private void ApplyBatchNumberSeriesFromTool(
        double start,
        bool useMultiply,
        double step,
        string prefix,
        string suffix,
        int originalPlacement,
        bool visibleRowsOnly,
        bool previewBeforeApply,
        int batchBoolRule)
    {
        PushBatchInsertFromFilterToolUi();

        if (_parsedTable is null)
        {
            MessageBox.Show("当前没有已加载的表格。", "批量生成数值", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var c = _activeFilterColumnIndex;
        if (c < 0 || c >= _parsedTable.ColumnNames.Length)
        {
            MessageBox.Show("请先在表格中点击一列数据列（非「#」列），再使用批量生成。", "批量生成数值", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        List<int> gridRows;
        if (visibleRowsOnly)
        {
            if (_visibleParsedRows.Count == 0)
            {
                MessageBox.Show("当前没有可见行（请先加载表格或放宽筛选条件）。", "批量生成数值", MessageBoxButtons.OK, MessageBoxIcon.Information);
                SaveFilterToolState();
                return;
            }

            gridRows = Enumerable.Range(0, _visibleParsedRows.Count).ToList();
        }
        else
        {
            if (!TryGetOrderedSelectedGridRowsForBatchColumn(c, out var sel) || sel.Count == 0)
            {
                MessageBox.Show(
                    "请先在主表中选中要写入的行：行选择模式下选中整行；块选择模式下请选中当前列上的单元格。或勾选「当前筛选可见行」以按可见顺序整列写入。",
                    "批量生成数值",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                SaveFilterToolState();
                return;
            }

            gridRows = sel;
        }

        if (useMultiply && Math.Abs(step) < double.Epsilon)
        {
            MessageBox.Show("乘法模式下步长不能为 0。", "批量生成数值", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            SaveFilterToolState();
            return;
        }

        if (!TryBuildBatchInsertPreviewEntries(
                c,
                gridRows,
                start,
                useMultiply,
                step,
                prefix,
                suffix,
                originalPlacement,
                batchBoolRule,
                out var entries,
                out var skipped))
        {
            SaveFilterToolState();
            return;
        }

        if (entries.Count == 0)
        {
            var skipNote = skipped > 0 ? $"（有 {skipped} 行因越界或无法换算为列类型而跳过）" : string.Empty;
            MessageBox.Show($"没有可写入的单元格。{skipNote}", "批量生成数值", MessageBoxButtons.OK, MessageBoxIcon.Information);
            SaveFilterToolState();
            return;
        }

        if (previewBeforeApply)
        {
            var tail = skipped > 0 ? $"另有 {skipped} 行因越界或无法换算为列类型未列入预览。" : "";
            var hint = $"以下列出 {entries.Count} 个可写入的单元格（当前列）。{tail}请勾选要写入的行后点击「写入已勾选」。";
            using var dlg = new ReplacePreviewForm(entries, skipped, "批量插入预览", "写入已勾选", hint);
            if (dlg.ShowDialog(this) != DialogResult.OK)
            {
                SaveFilterToolState();
                return;
            }

            var selected = dlg.GetSelectedEntries();
            if (selected.Count == 0)
            {
                MessageBox.Show("请至少勾选一项后再写入。", "批量插入预览", MessageBoxButtons.OK, MessageBoxIcon.Information);
                SaveFilterToolState();
                return;
            }

            ApplyReplacePreviewEntries(selected);
            var skipSuffix = skipped > 0 ? $"（预览外另有 {skipped} 行已跳过）" : string.Empty;
            MessageBox.Show($"已写入 {selected.Count} 个单元格{skipSuffix}。", "批量生成数值", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var confirm = MessageBox.Show(
            $"将不经预览写入 {entries.Count} 个单元格。确定继续吗？",
            "批量生成数值",
            MessageBoxButtons.OKCancel,
            MessageBoxIcon.Warning);
        if (confirm != DialogResult.OK)
        {
            SaveFilterToolState();
            return;
        }

        ApplyReplacePreviewEntries(entries);
        var skipDone = skipped > 0 ? $"，另有 {skipped} 行因换算失败未生成" : string.Empty;
        MessageBox.Show($"已写入 {entries.Count} 个单元格{skipDone}。", "批量生成数值", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private bool TryGetOrderedSelectedGridRowsForBatchColumn(int dataColumnIndex, out List<int> sortedGridRows)
    {
        sortedGridRows = new List<int>();
        if (dataColumnIndex < 0)
        {
            return false;
        }

        var gridCol = dataColumnIndex + 1;
        if (gridCol <= 0 || gridCol >= _grid.Columns.Count)
        {
            return false;
        }

        if (_rowSelectionMode)
        {
            foreach (DataGridViewRow r in _grid.SelectedRows)
            {
                if (r.Index >= 0 && r.Index < _grid.RowCount)
                {
                    sortedGridRows.Add(r.Index);
                }
            }

            if (sortedGridRows.Count == 0 && _grid.CurrentCell is { RowIndex: >= 0 } cc)
            {
                sortedGridRows.Add(cc.RowIndex);
            }
        }
        else
        {
            foreach (DataGridViewCell cell in _grid.SelectedCells)
            {
                if (cell.RowIndex >= 0 && cell.ColumnIndex == gridCol)
                {
                    sortedGridRows.Add(cell.RowIndex);
                }
            }

            sortedGridRows = sortedGridRows.Distinct().ToList();
            if (sortedGridRows.Count == 0
                && _grid.CurrentCell is { RowIndex: >= 0, ColumnIndex: var cci }
                && cci == gridCol)
            {
                sortedGridRows.Add(_grid.CurrentCell.RowIndex);
            }
        }

        sortedGridRows.Sort();
        return sortedGridRows.Count > 0;
    }

    private static string FormatNumericSeriesToken(double v)
    {
        if (double.IsNaN(v) || double.IsInfinity(v))
        {
            return "0";
        }

        if (Math.Abs(v - Math.Round(v)) < 1e-9)
        {
            return ((long)Math.Round(v)).ToString(CultureInfo.InvariantCulture);
        }

        return v.ToString("G9", CultureInfo.InvariantCulture);
    }

    private static bool TryCoerceSeriesDoubleToCellValue(double v, int cellType, int batchBoolRule, out object parsed)
    {
        parsed = 0;
        if (double.IsNaN(v) || double.IsInfinity(v))
        {
            return false;
        }

        var br = Math.Clamp(batchBoolRule, 0, 4);

        switch (cellType)
        {
            case 0:
                {
                    var r = Math.Round(v);
                    if (r < 0 || r > uint.MaxValue)
                    {
                        return false;
                    }

                    parsed = (uint)r;
                    return true;
                }
            case 2:
                {
                    long n;
                    switch (br)
                    {
                        case 1:
                            n = (long)Math.Round(v);
                            parsed = n != 0 ? 1 : 0;
                            return true;
                        case 2:
                            parsed = v >= 0.5 ? 1 : 0;
                            return true;
                        case 3:
                            n = (long)Math.Floor(v);
                            parsed = (n & 1L) == 0 ? 0 : 1;
                            return true;
                        case 4:
                            n = (long)Math.Ceiling(v);
                            parsed = (n & 1L) == 0 ? 0 : 1;
                            return true;
                        default:
                            n = (long)Math.Round(v);
                            parsed = (n & 1L) == 0 ? 0 : 1;
                            return true;
                    }
                }
            case 3:
                {
                    var r = Math.Round(v);
                    if (r < int.MinValue || r > int.MaxValue)
                    {
                        return false;
                    }

                    parsed = (int)r;
                    return true;
                }
            case 4:
                parsed = (float)v;
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Returns false if collection cannot proceed; <paramref name="validationMessage"/> is set when the user should be notified.
    /// </summary>
    private bool TryPrepareReplaceCollection(
        string keyword,
        string replaceText,
        bool matchCase,
        out List<ReplacePreviewEntry> entries,
        out int skippedTypeMismatch,
        out string? validationMessage,
        out string? validationTitle)
    {
        entries = new List<ReplacePreviewEntry>();
        skippedTypeMismatch = 0;
        validationMessage = null;
        validationTitle = null;

        if (_parsedTable is null)
        {
            return false;
        }

        _replaceKeyword = keyword.Trim();
        _filterReplaceText = replaceText;
        _replaceMatchCase = matchCase;

        if (string.IsNullOrWhiteSpace(_replaceKeyword))
        {
            validationMessage = "请先输入查找关键词。";
            validationTitle = "替换";
            return false;
        }

        var targetColumns = new List<int> { _activeFilterColumnIndex };
        targetColumns = targetColumns.Where(c => c >= 0 && c < _parsedTable.ColumnNames.Length).ToList();
        if (targetColumns.Count == 0)
        {
            validationMessage = "请先选中一列数据列，再使用替换。";
            validationTitle = "替换";
            return false;
        }

        for (var r = 0; r < _parsedTable.Rows.Count; r++)
        {
            var gridR0 = FindFirstGridRowForParsedIndex(r);
            if (gridR0 < 0 || !IsDataGridRowVisible(gridR0))
            {
                continue;
            }

            foreach (var c in targetColumns)
            {
                var sourceText = _parsedTable.Rows[r][c]?.ToString() ?? string.Empty;
                var replacedText = ReplaceText(sourceText, _replaceKeyword, _filterReplaceText, _replaceMatchCase, null);
                if (string.Equals(sourceText, replacedText, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!TryParseCellValue(_parsedTable.ColumnTypes[c], replacedText, out var parsed, out _))
                {
                    skippedTypeMismatch++;
                    continue;
                }

                if (Equals(_parsedTable.Rows[r][c], parsed))
                {
                    continue;
                }

                var colName = c < _parsedTable.ColumnNames.Length && !string.IsNullOrWhiteSpace(_parsedTable.ColumnNames[c])
                    ? _parsedTable.ColumnNames[c]
                    : $"COL_{c}";
                entries.Add(new ReplacePreviewEntry(r, c, colName, sourceText, replacedText, parsed!));
            }
        }

        return true;
    }

    private void ApplyReplacePreviewEntries(IReadOnlyList<ReplacePreviewEntry> changes)
    {
        if (_parsedTable is null || changes.Count == 0)
        {
            return;
        }

        CaptureSnapshotForUndo();
        foreach (var change in changes)
        {
            var before = _parsedTable.Rows[change.Row][change.Column];
            _parsedTable.Rows[change.Row][change.Column] = change.Parsed;
            TrackCellModification(change.Row, change.Column, before);
        }

        _isDirty = true;
        _grid.Invalidate();
        ApplyFilterToGrid();
        UpdateStatusBar();
        UpdateWindowTitle();
        SaveFilterToolState();
    }

    private (int Row, int Column)? FindReplaceMatchFrom(
        string keyword,
        bool allColumns,
        bool matchCase,
        bool useRegex,
        int startRow,
        int startColumn,
        bool forward)
    {
        if (_parsedTable is null)
        {
            return null;
        }

        var targetColumns = allColumns
            ? Enumerable.Range(0, _parsedTable.ColumnNames.Length).ToList()
            : new List<int> { _activeFilterColumnIndex };
        targetColumns = targetColumns.Where(c => c >= 0 && c < _parsedTable.ColumnNames.Length).ToList();
        if (targetColumns.Count == 0)
        {
            return null;
        }

        Regex? regex = null;
        if (useRegex)
        {
            var options = matchCase ? RegexOptions.None : RegexOptions.IgnoreCase;
            try
            {
                regex = new Regex(keyword, options);
            }
            catch (ArgumentException ex)
            {
                MessageBox.Show($"正则表达式无效：{ex.Message}", "替换查询", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return null;
            }
        }

        if (startRow < 0 || startRow >= _parsedTable.Rows.Count)
        {
            startRow = 0;
            startColumn = -1;
        }

        var orderedColumns = targetColumns.OrderBy(c => c).ToList();
        var searchSpace = new List<(int Row, int Col)>(_parsedTable.Rows.Count * orderedColumns.Count);
        for (var r = 0; r < _parsedTable.Rows.Count; r++)
        {
            var gridR1 = FindFirstGridRowForParsedIndex(r);
            if (gridR1 < 0 || !IsDataGridRowVisible(gridR1))
            {
                continue;
            }

            foreach (var c in orderedColumns)
            {
                searchSpace.Add((r, c));
            }
        }

        if (searchSpace.Count == 0)
        {
            return null;
        }

        var currentIndex = searchSpace.FindIndex(x => x.Row == startRow && x.Col == startColumn);
        if (currentIndex < 0)
        {
            currentIndex = forward ? -1 : 0;
        }

        for (var i = 1; i <= searchSpace.Count; i++)
        {
            var idx = forward
                ? (currentIndex + i) % searchSpace.Count
                : (currentIndex - i + searchSpace.Count) % searchSpace.Count;
            var (row, col) = searchSpace[idx];
            var text = _parsedTable.Rows[row][col]?.ToString() ?? string.Empty;
            if (IsTextMatch(text, keyword, matchCase, regex))
            {
                return (row, col);
            }
        }

        return null;
    }

    private void RevealAndFocusCell(int parsedRowIndex, int columnIndex)
    {
        if (_parsedTable is null || columnIndex < 0 || parsedRowIndex < 0 || parsedRowIndex >= _parsedTable.Rows.Count || (columnIndex + 1) >= _grid.Columns.Count)
        {
            return;
        }

        var gridRow = FindFirstGridRowForParsedIndex(parsedRowIndex);
        if (gridRow < 0 || gridRow >= _grid.RowCount)
        {
            return;
        }

        if (!IsDataGridRowVisible(gridRow))
        {
            return;
        }

        _grid.ClearSelection();
        var gridColumnIndex = columnIndex + 1;
        _grid.CurrentCell = _grid.Rows[gridRow].Cells[gridColumnIndex];
        _grid.Rows[gridRow].Cells[gridColumnIndex].Selected = true;
        var anchor = Math.Max(gridRow - 2, 0);
        var scrollRow = FindFirstVisibleScrollingRowIndex(_grid, anchor);
        try
        {
            _grid.FirstDisplayedScrollingRowIndex = scrollRow;
        }
        catch (Exception)
        {
            // Ignore: FirstDisplayedScrollingRowIndex rejects non-displayable rows in edge cases.
        }
    }

    private bool IsDataGridRowVisible(int rowIndex)
    {
        return rowIndex >= 0 && rowIndex < _grid.RowCount;
    }

    private static int FindFirstVisibleScrollingRowIndex(DataGridView grid, int preferredTopRow)
    {
        if (grid.VirtualMode)
        {
            if (grid.RowCount == 0)
            {
                return 0;
            }

            return Math.Clamp(preferredTopRow, 0, grid.RowCount - 1);
        }

        if (grid.Rows.Count == 0)
        {
            return 0;
        }

        var lo = Math.Clamp(preferredTopRow, 0, grid.Rows.Count - 1);
        for (var i = lo; i < grid.Rows.Count; i++)
        {
            if (grid.Rows[i].Visible)
            {
                return i;
            }
        }

        for (var i = lo; i >= 0; i--)
        {
            if (grid.Rows[i].Visible)
            {
                return i;
            }
        }

        return lo;
    }

    private void ClearFilterFromTool()
    {
        _filterKeyword = string.Empty;
        ApplyFilterToGrid();
        SyncQuickFindBarFromFilterState();
        if (_filterToolForm is { IsDisposed: false })
        {
            SyncFilterColumns();
        }

        SaveFilterToolState();
    }

    private void ApplyFilterToGrid()
    {
        if (_parsedTable is null)
        {
            return;
        }

        var hasFilter = !string.IsNullOrWhiteSpace(_filterKeyword);
        var keyword = _filterKeyword;
        var filterColumnIndex = _filterAllColumns ? -1 : _activeFilterColumnIndex;
        Regex? regex = null;
        if (hasFilter && _filterUseRegex)
        {
            var options = _filterMatchCase ? RegexOptions.None : RegexOptions.IgnoreCase;
            try
            {
                regex = new Regex(keyword, options);
            }
            catch (ArgumentException ex)
            {
                MessageBox.Show($"正则表达式无效：{ex.Message}", "筛选", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
        }
        _visibleParsedRows.Clear();
        for (var p = 0; p < _parsedTable.Rows.Count; p++)
        {
            var visible = !hasFilter || RowMatchesFilter(_parsedTable.Rows[p], keyword, filterColumnIndex, _filterMatchCase, regex);
            if (visible)
            {
                _visibleParsedRows.Add(p);
            }
        }

        if (_sortActive)
        {
            SortVisibleParsedRowsByDataColumn(_sortDataColumnIndex, _sortAscending);
        }

        _grid.SuspendLayout();
        try
        {
            _grid.RowCount = _visibleParsedRows.Count;
        }
        finally
        {
            _grid.ResumeLayout();
        }

        UpdateFilterToolStatus();
    }

    private void UpdateFilterToolStatus()
    {
        if (_filterToolForm is null || _filterToolForm.IsDisposed || _parsedTable is null)
        {
            return;
        }

        _filterToolForm.SetResultInfo(_visibleParsedRows.Count, _parsedTable.Rows.Count);
    }

    private static bool RowMatchesFilter(object?[] row, string keyword, int columnIndex, bool matchCase, Regex? regex)
    {
        if (regex is not null)
        {
            if (columnIndex >= 0 && columnIndex < row.Length)
            {
                return regex.IsMatch(row[columnIndex]?.ToString() ?? string.Empty);
            }

            for (var c = 0; c < row.Length; c++)
            {
                if (regex.IsMatch(row[c]?.ToString() ?? string.Empty))
                {
                    return true;
                }
            }

            return false;
        }

        var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        if (columnIndex >= 0 && columnIndex < row.Length)
        {
            return (row[columnIndex]?.ToString() ?? string.Empty).Contains(keyword, comparison);
        }

        for (var c = 0; c < row.Length; c++)
        {
            if ((row[c]?.ToString() ?? string.Empty).Contains(keyword, comparison))
            {
                return true;
            }
        }

        return false;
    }

    private bool TryCopyCurrentEditingCell()
    {
        if (!IsEditingCellActive() || _grid.CurrentCell is not { ColumnIndex: > 0 })
        {
            return false;
        }

        if (_grid.EditingControl is TextBox editingTextBox)
        {
            var fragment = editingTextBox.SelectionLength > 0
                ? editingTextBox.SelectedText
                : editingTextBox.Text;
            Clipboard.SetText(fragment);
            return true;
        }

        var text = _grid.CurrentCell.EditedFormattedValue?.ToString()
            ?? _grid.CurrentCell.Value?.ToString()
            ?? string.Empty;
        Clipboard.SetText(text);
        return true;
    }

    private bool TryPasteToCurrentEditingCell()
    {
        if (!IsEditingCellActive() || _grid.CurrentCell is not { ColumnIndex: > 0 })
        {
            return false;
        }

        var text = Clipboard.ContainsText() ? Clipboard.GetText() : string.Empty;
        if (string.IsNullOrEmpty(text))
        {
            return true;
        }

        if (_grid.EditingControl is TextBox editingTextBox)
        {
            editingTextBox.Text = text;
            editingTextBox.SelectionStart = editingTextBox.TextLength;
            editingTextBox.SelectionLength = 0;
            return true;
        }

        _grid.CurrentCell.Value = text;
        return true;
    }

    private bool HandleEditingCellClipboardShortcut(KeyEventArgs e)
    {
        if (!e.Control || _grid.CurrentCell is not { ColumnIndex: > 0 } || !IsEditingCellActive())
        {
            return false;
        }

        if (e.KeyCode == Keys.C)
        {
            TryCopyCurrentEditingCell();
            e.Handled = true;
            e.SuppressKeyPress = true;
            return true;
        }

        if (e.KeyCode == Keys.V)
        {
            TryPasteToCurrentEditingCell();
            e.Handled = true;
            e.SuppressKeyPress = true;
            return true;
        }

        return false;
    }

    private bool IsEditingCellActive()
    {
        return _grid.IsCurrentCellInEditMode || _grid.EditingControl is not null;
    }

    private string GetFilterToolStatePath()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LdtEditor");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, $"filter-tool-state-{Environment.ProcessId}.json");
    }

    private static string GetLegacySharedFilterToolStatePath()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LdtEditor");
        return Path.Combine(dir, "filter-tool-state.json");
    }

    private void LoadFilterToolState()
    {
        try
        {
            var path = GetFilterToolStatePath();
            if (!File.Exists(path))
            {
                var legacy = GetLegacySharedFilterToolStatePath();
                if (File.Exists(legacy))
                {
                    path = legacy;
                }
                else
                {
                    return;
                }
            }

            var json = File.ReadAllText(path);
            var state = JsonSerializer.Deserialize<FilterToolState>(json);
            if (state is null)
            {
                return;
            }

            _filterToolLocation = new Point(state.X, state.Y);
            _filterAllColumns = state.AllColumns;
            _filterMatchCase = state.MatchCase;
            _filterUseRegex = state.UseRegex;
            _filterKeyword = state.Keyword ?? string.Empty;
            _activeFilterColumnIndex = state.ActiveColumnIndex;
            _replaceKeyword = state.ReplaceKeyword ?? string.Empty;
            _filterReplaceText = state.ReplaceText ?? string.Empty;
            _replaceMatchCase = state.ReplaceMatchCase;
            _batchInsertStartText = string.IsNullOrWhiteSpace(state.BatchInsertStart) ? "0" : state.BatchInsertStart;
            _batchInsertStepText = string.IsNullOrWhiteSpace(state.BatchInsertStep) ? "1" : state.BatchInsertStep;
            _batchInsertUseMultiply = state.BatchInsertUseMultiply;
            _batchInsertPrefix = state.BatchInsertPrefix ?? "";
            _batchInsertSuffix = state.BatchInsertSuffix ?? "";
            _batchInsertOriginalPlacement = state.BatchInsertOriginalPlacement is >= 0 and <= 2 ? state.BatchInsertOriginalPlacement : 0;
            _batchInsertVisibleRowsOnly = state.BatchInsertVisibleRowsOnly;
            _batchInsertPreviewBeforeApply = state.BatchInsertPreviewBeforeApply ?? true;
            _batchInsertBoolRule = Math.Clamp(state.BatchInsertBoolRule, 0, 4);
            SyncQuickFindBarFromFilterState();
        }
        catch
        {
            // Ignore state load failures and continue with defaults.
        }
    }

    private void SaveFilterToolState()
    {
        if (!TrySaveFilterToolStateCore(out var ex))
        {
            ThrottledPersistDiskHint("filter-tool", "功能菜单状态未能写入磁盘（本会话仍有效）", ex);
        }
    }

    /// <summary>先写临时文件再替换目标，降低异常关机时半写入 JSON 的概率。</summary>
    private static bool TryAtomicWriteUtf8NoBom(string path, string contents, out Exception? error)
    {
        error = null;
        var tmp = path + ".new.tmp";
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(tmp, contents, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            if (File.Exists(path))
            {
                File.Replace(tmp, path, destinationBackupFileName: null, ignoreMetadataErrors: true);
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
                // ignore cleanup failure
            }

            return false;
        }
    }

    private bool TrySaveFilterToolStateCore(out Exception? error)
    {
        error = null;
        try
        {
            var location = _filterToolForm is { IsDisposed: false }
                ? _filterToolForm.Location
                : _filterToolLocation;
            if (location is null)
            {
                return true;
            }

            var value = location.Value;
            var state = new FilterToolState(
                value.X,
                value.Y,
                _filterAllColumns,
                _filterMatchCase,
                _filterUseRegex,
                _filterKeyword,
                _activeFilterColumnIndex,
                _filterReplaceText,
                false,
                _replaceMatchCase,
                false,
                _replaceKeyword,
                _batchInsertStartText,
                _batchInsertStepText,
                _batchInsertUseMultiply,
                _batchInsertPrefix,
                _batchInsertSuffix,
                _batchInsertOriginalPlacement,
                _batchInsertVisibleRowsOnly,
                _batchInsertPreviewBeforeApply,
                _batchInsertBoolRule);
            var json = JsonSerializer.Serialize(state);
            return TryAtomicWriteUtf8NoBom(GetFilterToolStatePath(), json, out error);
        }
        catch (Exception ex)
        {
            error = ex;
            return false;
        }
    }

    private bool TrySaveEditorAppSettingsCore(out Exception? error)
    {
        error = null;
        try
        {
            var json = JsonSerializer.Serialize(EditorSettingsForJsonPersistence(), EditorSettingsJsonOptions);
            return TryAtomicWriteUtf8NoBom(GetEditorSettingsPath(), json, out error);
        }
        catch (Exception ex)
        {
            error = ex;
            return false;
        }
    }

    private void ThrottledPersistDiskHint(string throttleKey, string shortMessage, Exception? ex)
    {
        var now = Environment.TickCount64;
        if (_diskPersistHintThrottleKey == throttleKey && now - _diskPersistHintThrottleTick < 45_000)
        {
            return;
        }

        _diskPersistHintThrottleKey = throttleKey;
        _diskPersistHintThrottleTick = now;
        var detail = ex?.Message ?? "写入失败";
        if (detail.Length > 900)
        {
            detail = detail[..900] + "…";
        }

        void Apply()
        {
            _diskPersistHintLabel.Text = shortMessage;
            _diskPersistHintLabel.ForeColor = Color.FromArgb(180, 70, 30);
            _diskPersistHintLabel.ToolTipText = detail;
            _diskPersistHintTimer.Stop();
            _diskPersistHintTimer.Start();
        }

        if (InvokeRequired)
        {
            BeginInvoke(Apply);
        }
        else
        {
            Apply();
        }
    }

    private bool SaveEditorAppSettings(bool promptOnFailure = false)
    {
        if (TrySaveEditorAppSettingsCore(out var ex))
        {
            return true;
        }

        var reason = ex?.Message ?? "写入失败";
        if (promptOnFailure)
        {
            MessageBox.Show(
                this,
                $"未能写入 editor-settings.json。\n\n{reason}\n\n本次选项在当前会话仍有效。",
                "选项未保存",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        else
        {
            ThrottledPersistDiskHint("editor-settings", "选项未能写入磁盘（本会话仍有效）", ex);
        }

        return false;
    }

    private string GetEditorSettingsPath()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LdtEditor");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "editor-settings.json");
    }

    private void LoadEditorAppSettings()
    {
        try
        {
            var path = GetEditorSettingsPath();
            if (!File.Exists(path))
            {
                _editorSettings = EditorAppSettings.Default;
                ApplyIconResourceRuntimeTuning(_editorSettings, fromInitialLoad: true);
                return;
            }

            var json = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(json);
            var loaded = JsonSerializer.Deserialize<EditorAppSettings>(json, EditorSettingsJsonOptions);
            var baseSettings = (loaded ?? EditorAppSettings.Default) with { ShowItemDescriptionPanel = false };
            var explicitBackup = JsonObjectHasPropertyIgnoreCase(doc.RootElement, nameof(EditorAppSettings.BackupBeforeOverwrite));
            var mergedBackup = explicitBackup
                ? baseSettings
                : baseSettings with { BackupBeforeOverwrite = true };
            _editorSettings = NormalizeEditorIconResourceTuning(mergedBackup);
            ApplyIconResourceRuntimeTuning(_editorSettings, fromInitialLoad: true);
        }
        catch
        {
            _editorSettings = EditorAppSettings.Default;
            ApplyIconResourceRuntimeTuning(_editorSettings, fromInitialLoad: true);
        }
    }

    private static EditorAppSettings NormalizeEditorIconResourceTuning(EditorAppSettings s) =>
        s with
        {
            IconAtlasCacheMaxEntries = s.IconAtlasCacheMaxEntries <= 0 ? 12 : Math.Clamp(s.IconAtlasCacheMaxEntries, 1, 96),
            SpfChainLiteralScanMaxEntries = s.SpfChainLiteralScanMaxEntries <= 0 ? 4000 : Math.Clamp(s.SpfChainLiteralScanMaxEntries, 100, 100_000),
            SpfChainLiteralScanMaxKiBPerEntry = s.SpfChainLiteralScanMaxKiBPerEntry <= 0 ? 256 : Math.Clamp(s.SpfChainLiteralScanMaxKiBPerEntry, 8, 4096),
            SpfPngNameScanMaxKiB = s.SpfPngNameScanMaxKiB <= 0 ? 512 : Math.Clamp(s.SpfPngNameScanMaxKiB, 16, 8192),
            IconAtlasDecodeMaxMegapixels = Math.Clamp(s.IconAtlasDecodeMaxMegapixels, 0, 512),
            SpfPngChainStartOffset = Math.Max(0L, s.SpfPngChainStartOffset)
        };

    private void ApplyIconResourceRuntimeTuning(EditorAppSettings s, bool fromInitialLoad = false)
    {
        var nameKiB = Math.Clamp(s.SpfPngNameScanMaxKiB, 16, 8192);
        SpfPngArchive.ConfigureNameScanMaxBytes(nameKiB * 1024);
        IconAtlasBitmapCache.ConfigureMaxEntries(Math.Clamp(s.IconAtlasCacheMaxEntries, 1, 96));
        var decodeMp = Math.Clamp(s.IconAtlasDecodeMaxMegapixels, 0, 512);
        IconAtlasDecodeHelper.SetMaxDecodedMegapixels(decodeMp);
        SpfIconIndexCache.SetLiteralChainScanLimits(
            Math.Clamp(s.SpfChainLiteralScanMaxEntries, 100, 100_000),
            Math.Clamp(s.SpfChainLiteralScanMaxKiBPerEntry, 8, 4096) * 1024);
        SpfIconIndexCache.SetPngChainStartOffset(Math.Max(0L, s.SpfPngChainStartOffset));

        if (!fromInitialLoad && _appliedIconDecodeMegapixels != decodeMp)
        {
            IconAtlasBitmapCache.Clear();
        }

        _appliedIconDecodeMegapixels = decodeMp;

        var structural = (ChainStart: Math.Max(0L, s.SpfPngChainStartOffset), NameScanKiB: nameKiB);
        if (fromInitialLoad)
        {
            _appliedIconStructuralTuning = structural;
            return;
        }

        if (_appliedIconStructuralTuning != structural)
        {
            _appliedIconStructuralTuning = structural;
            SpfIconIndexCache.Invalidate();
        }
    }

    private static bool JsonObjectHasPropertyIgnoreCase(JsonElement root, string name)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var p in root.EnumerateObject())
        {
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private EditorAppSettings EditorSettingsForJsonPersistence()
    {
        var f = _editorSettings.GridFontFamily ?? "";
        var s = _editorSettings.GridFontSizePoints;
        if (string.IsNullOrWhiteSpace(f) && s <= 0)
        {
            return _editorSettings with { GridFontFamily = "Microsoft YaHei UI", GridFontSizePoints = 9f };
        }

        if (string.IsNullOrWhiteSpace(f))
        {
            return _editorSettings with { GridFontFamily = "Microsoft YaHei UI" };
        }

        if (s <= 0)
        {
            return _editorSettings with { GridFontSizePoints = 9f };
        }

        return _editorSettings;
    }

    private void ApplyGridFontFromSettings()
    {
        var family = string.IsNullOrWhiteSpace(_editorSettings.GridFontFamily)
            ? "Microsoft YaHei UI"
            : _editorSettings.GridFontFamily.Trim();
        var size = _editorSettings.GridFontSizePoints <= 0 ? 9f : _editorSettings.GridFontSizePoints;
        Font? next;
        try
        {
            next = new Font(family, size, FontStyle.Regular, GraphicsUnit.Point);
        }
        catch
        {
            next = new Font("Microsoft YaHei UI", 9f, FontStyle.Regular, GraphicsUnit.Point);
        }

        _gridDisplayFont?.Dispose();
        _gridDisplayFont = next;
        _grid.DefaultCellStyle.Font = _gridDisplayFont;
        _grid.ColumnHeadersDefaultCellStyle.Font = _gridDisplayFont;
    }

    private void TryReparseLoadedTableWithCurrentDecodePreference()
    {
        if (_data is null)
        {
            return;
        }

        try
        {
            _parsedTable = ParseTypedTable(_data, _editorSettings.TextDecode, null);
            ClearModificationTracker();
            LoadTable(null);
            ResetHistory();
            SyncFilterColumns();
            UpdateFilterToolStatus();
            UpdateStatusBar();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "重新解析", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ApplyEditorSettingsFromTool(EditorAppSettings s)
    {
        var prevDecode = _editorSettings.TextDecode;
        var prevSpf = _editorSettings.SpfArchivePath;
        var keepPreview = _editorSettings.ShowItemDescriptionPanel;
        var keepFindBar = _editorSettings.ShowQuickFindBar;
        var keepModVisible = _editorSettings.ModificationTrackerVisible;
        var keepSuppressClose = _editorSettings.SuppressModificationRecordsClosePrompt;
        _editorSettings = NormalizeEditorIconResourceTuning(s with
        {
            ShowItemDescriptionPanel = keepPreview,
            ShowQuickFindBar = keepFindBar,
            ModificationTrackerVisible = keepModVisible,
            SuppressModificationRecordsClosePrompt = keepSuppressClose,
            IconAtlasCacheMaxEntries = _editorSettings.IconAtlasCacheMaxEntries,
            SpfChainLiteralScanMaxEntries = _editorSettings.SpfChainLiteralScanMaxEntries,
            SpfChainLiteralScanMaxKiBPerEntry = _editorSettings.SpfChainLiteralScanMaxKiBPerEntry,
            SpfPngNameScanMaxKiB = _editorSettings.SpfPngNameScanMaxKiB,
            IconAtlasDecodeMaxMegapixels = _editorSettings.IconAtlasDecodeMaxMegapixels,
            SpfPngChainStartOffset = _editorSettings.SpfPngChainStartOffset
        });
        if (!string.Equals(prevSpf, s.SpfArchivePath, StringComparison.OrdinalIgnoreCase))
        {
            SpfIconIndexCache.Invalidate();
        }

        ApplyIconResourceRuntimeTuning(_editorSettings, fromInitialLoad: false);

        SaveEditorAppSettings(promptOnFailure: true);
        if (_editorSettings.AutoPersistModificationRecords)
        {
            TryPersistModificationRecordsIfEligible(force: true);
        }

        ApplyGridFontFromSettings();
        if (_data is not null && prevDecode != s.TextDecode)
        {
            TryReparseLoadedTableWithCurrentDecodePreference();
        }
    }

    private static List<List<string>> ParseClipboardRows(string text)
    {
        var rows = new List<List<string>>();
        var lineParts = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        foreach (var line in lineParts)
        {
            if (line.Length == 0)
            {
                continue;
            }

            rows.Add(line.Split('\t').ToList());
        }

        return rows;
    }

    private static string ReplaceText(string source, string find, string replacement, bool matchCase, Regex? regex)
    {
        if (regex is not null)
        {
            return regex.Replace(source, replacement);
        }

        var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        return source.Replace(find, replacement, comparison);
    }

    private static bool IsTextMatch(string source, string find, bool matchCase, Regex? regex)
    {
        if (regex is not null)
        {
            return regex.IsMatch(source);
        }

        var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        return source.Contains(find, comparison);
    }

    private static object?[] BuildDefaultRowValues(int[] columnTypes)
    {
        var newRow = new object?[columnTypes.Length];
        for (var c = 0; c < columnTypes.Length; c++)
        {
            var defaultRaw = GetDefaultCellValueText(columnTypes[c]);
            if (!TryParseCellValue(columnTypes[c], defaultRaw, out var parsed, out _))
            {
                parsed = defaultRaw;
            }
            newRow[c] = parsed;
        }

        return newRow;
    }

    private static object[] BuildGridRowValues(int rowIndex, object?[] rowData)
    {
        var gridValues = new object[rowData.Length + 1];
        gridValues[0] = rowIndex;
        for (var c = 0; c < rowData.Length; c++)
        {
            gridValues[c + 1] = rowData[c] ?? string.Empty;
        }

        return gridValues;
    }

    private static int GetDefaultColumnWidth(string? columnName, int columnType)
    {
        var name = (columnName ?? string.Empty).Trim();
        var lowered = name.ToLowerInvariant();

        // Name/description-like text columns get more room by default.
        if (columnType == 1 && (
            lowered.Contains("name") ||
            lowered.Contains("desc") ||
            lowered.Contains("title") ||
            lowered.Contains("text") ||
            lowered.Contains("memo") ||
            name.Contains("名称", StringComparison.Ordinal) ||
            name.Contains("描述", StringComparison.Ordinal) ||
            name.Contains("说明", StringComparison.Ordinal)))
        {
            return 240;
        }

        // ID-like columns are generally compact numeric references.
        if (lowered.Contains("id") ||
            lowered.Contains("idx") ||
            lowered.Contains("index") ||
            lowered.EndsWith("no", StringComparison.Ordinal) ||
            name.Contains("编号", StringComparison.Ordinal) ||
            name.Contains("序号", StringComparison.Ordinal))
        {
            return 104;
        }

        // Numeric/boolean columns can be narrower than text.
        if (columnType == 0 || columnType == 2 || columnType == 3 || columnType == 4)
        {
            return 108;
        }

        // Default width for general text columns.
        return 156;
    }

    private static int PeekTypedTableRowCount(byte[] data)
    {
        EnsureLength(data, 8716);
        return checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(8, 4)));
    }

    private static ParsedLdtTable ParseTypedTable(
        byte[] data,
        LdtTextDecodePreference textDecode,
        Action<int, int>? onRowProgress = null)
    {
        EnsureLength(data, 8716);

        var rowCount = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(8, 4)));
        var rawColumnCount = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4, 4)));
        var columnCount = rawColumnCount + 1;
        if (columnCount <= 0 || columnCount > 129)
        {
            throw new InvalidDataException($"Unexpected column count: {columnCount}");
        }

        var names = new string[columnCount];
        var types = new int[columnCount];
        names[0] = "_RowId";
        types[0] = 3;
        for (var i = 1; i < columnCount; i++)
        {
            var nameOffset = 12 + ((i - 1) * 64);
            names[i] = ReadPaddedUtf8(data.AsSpan(nameOffset, 64));
            var typeOffset = 8204 + ((i - 1) * 4);
            types[i] = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(typeOffset, 4));
        }

        var rows = new List<object?[]>(rowCount);
        var pos = 8716;
        var dataStartOffset = pos;

        for (var r = 0; r < rowCount; r++)
        {
            var row = new object?[columnCount];
            for (var c = 0; c < columnCount; c++)
            {
                var t = types[c];
                switch (t)
                {
                    case 0:
                        EnsureLength(data, pos + 4);
                        row[c] = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(pos, 4));
                        pos += 4;
                        break;
                    case 1:
                        EnsureLength(data, pos + 2);
                        var len = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(pos, 2));
                        pos += 2;
                        EnsureLength(data, pos + len);
                        var bytes = data.AsSpan(pos, len);
                        var zeroIndex = bytes.IndexOf((byte)0);
                        var effective = zeroIndex >= 0 ? bytes[..zeroIndex] : bytes;
                        row[c] = DecodeLdtStringBytes(effective, textDecode);
                        pos += len;
                        break;
                    case 2:
                        EnsureLength(data, pos + 4);
                        row[c] = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(pos, 4)) != 0 ? 1 : 0;
                        pos += 4;
                        break;
                    case 3:
                        EnsureLength(data, pos + 4);
                        row[c] = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(pos, 4));
                        pos += 4;
                        break;
                    case 4:
                        EnsureLength(data, pos + 4);
                        row[c] = BitConverter.ToSingle(data, pos);
                        pos += 4;
                        break;
                    default:
                        throw new InvalidDataException($"Unknown cell type {t} at column {c} ({names[c]}).");
                }
            }
            rows.Add(row);
            onRowProgress?.Invoke(r + 1, rowCount);
        }

        return new ParsedLdtTable(names, types, rows, dataStartOffset, pos);
    }

    /// <summary>
    /// Locates the on-disk typed row blob using the file header (row count + column types) without decoding strings.
    /// Ensures <paramref name="table"/>'s schema matches the file so save cannot splice the wrong region.
    /// </summary>
    private static (int DataStartOffset, int DataEndOffset) MeasureOnDiskTypedRowBlob(byte[] data, ParsedLdtTable table)
    {
        EnsureLength(data, 8716);

        var rowCountOnDisk = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(8, 4)));
        var rawColumnCount = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4, 4)));
        var columnCount = rawColumnCount + 1;
        if (columnCount <= 0 || columnCount > 129)
        {
            throw new InvalidDataException($"Unexpected column count: {columnCount}");
        }

        if (table.ColumnTypes.Length != columnCount || table.ColumnNames.Length != columnCount)
        {
            throw new InvalidDataException(
                "The in-memory column layout does not match this file's header. Re-open the file or check for a corrupted LDT.");
        }

        var types = new int[columnCount];
        types[0] = 3;
        for (var i = 1; i < columnCount; i++)
        {
            var typeOffset = 8204 + ((i - 1) * 4);
            types[i] = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(typeOffset, 4));
        }

        for (var i = 0; i < columnCount; i++)
        {
            if (table.ColumnTypes[i] != types[i])
            {
                throw new InvalidDataException(
                    $"Column type mismatch at index {i} (memory={table.ColumnTypes[i]}, file={types[i]}). Re-open the file.");
            }
        }

        const int dataStartOffset = 8716;
        var pos = dataStartOffset;
        for (var r = 0; r < rowCountOnDisk; r++)
        {
            for (var c = 0; c < columnCount; c++)
            {
                var t = types[c];
                switch (t)
                {
                    case 0:
                        EnsureLength(data, pos + 4);
                        pos += 4;
                        break;
                    case 1:
                        EnsureLength(data, pos + 2);
                        var len = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(pos, 2));
                        pos += 2;
                        EnsureLength(data, pos + len);
                        pos += len;
                        break;
                    case 2:
                    case 3:
                    case 4:
                        EnsureLength(data, pos + 4);
                        pos += 4;
                        break;
                    default:
                        throw new InvalidDataException($"Unknown cell type {t} at row {r}, column {c}.");
                }
            }
        }

        if (pos < dataStartOffset || pos > data.Length)
        {
            throw new InvalidDataException(
                $"Typed row region is inconsistent (computed end offset {pos}, file length {data.Length}).");
        }

        return (dataStartOffset, pos);
    }

    private (byte[] PatchedBytes, int DataStartOffset, int DataEndOffsetExclusive) BuildPatchedTypedFile(byte[] original, ParsedLdtTable table) =>
        PatchTypedLdtBytes(original, table, GetSaveRowStringEncoding());

    private Encoding GetSaveRowStringEncoding() =>
        _editorSettings.SaveEncoding == LdtSaveStringEncoding.Cp949 ? sLdtCp949 : sLdtGbk;

    private static byte[] SerializeTypedRows(ParsedLdtTable table, Encoding rowStringEncoding)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        for (var r = 0; r < table.Rows.Count; r++)
        {
            var row = table.Rows[r];
            for (var c = 0; c < table.ColumnTypes.Length; c++)
            {
                var t = table.ColumnTypes[c];
                var value = row[c];
                switch (t)
                {
                    case 0:
                        bw.Write(Convert.ToUInt32(value));
                        break;
                    case 1:
                        var s = value?.ToString() ?? string.Empty;
                        var strBytes = rowStringEncoding.GetBytes(s);
                        var payload = new byte[strBytes.Length + 1];
                        Buffer.BlockCopy(strBytes, 0, payload, 0, strBytes.Length);
                        if (payload.Length > ushort.MaxValue)
                        {
                            throw new InvalidDataException($"String too long at row {r}, col {c}.");
                        }
                        bw.Write((ushort)payload.Length);
                        bw.Write(payload);
                        break;
                    case 2:
                        bw.Write(Convert.ToInt32(value) != 0 ? 1 : 0);
                        break;
                    case 3:
                        bw.Write(Convert.ToInt32(value));
                        break;
                    case 4:
                        bw.Write(Convert.ToSingle(value));
                        break;
                    default:
                        throw new InvalidDataException($"Unsupported type {t} at row {r}, col {c}.");
                }
            }
        }

        bw.Flush();
        return ms.ToArray();
    }

    private static bool TryParseCellValue(int cellType, string raw, out object parsed, out string? error)
    {
        parsed = 0;
        error = null;
        switch (cellType)
        {
            case 0:
                if (!uint.TryParse(raw, out var u))
                {
                    error = "Expected uint.";
                    return false;
                }
                parsed = u;
                return true;
            case 1:
                parsed = raw;
                return true;
            case 2:
                if (!int.TryParse(raw, out var bi) || (bi != 0 && bi != 1))
                {
                    error = "Bool field expects 0 or 1.";
                    return false;
                }
                parsed = bi;
                return true;
            case 3:
                if (!int.TryParse(raw, out var si))
                {
                    error = "Expected int32.";
                    return false;
                }
                parsed = si;
                return true;
            case 4:
                if (!float.TryParse(raw, out var f))
                {
                    error = "Expected float.";
                    return false;
                }
                parsed = f;
                return true;
            default:
                error = $"Unsupported type: {cellType}";
                return false;
        }
    }

    private static string ReadPaddedUtf8(ReadOnlySpan<byte> span)
    {
        var zeroIndex = span.IndexOf((byte)0);
        var effective = zeroIndex >= 0 ? span[..zeroIndex] : span;
        return Encoding.UTF8.GetString(effective).Trim();
    }

    private static string DecodeLdtStringBytes(ReadOnlySpan<byte> effective, LdtTextDecodePreference pref) =>
        DecodeLdtStringBytesWithMeta(effective, pref).Text;

    private static (string Text, LdtAutoDecodeWinner? AutoWinner) DecodeLdtStringBytesWithMeta(
        ReadOnlySpan<byte> effective,
        LdtTextDecodePreference pref)
    {
        return pref switch
        {
            LdtTextDecodePreference.Auto => DecodeBestLdtStringWithWinner(effective) switch
            {
                var r => (r.Text, (LdtAutoDecodeWinner?)r.Winner)
            },
            LdtTextDecodePreference.Gbk => (sLdtGbk.GetString(effective), null),
            LdtTextDecodePreference.Cp949 => (sLdtCp949.GetString(effective), null),
            LdtTextDecodePreference.Utf8 => (DecodeUtf8Chain(effective), null),
            _ => DecodeBestLdtStringWithWinner(effective) switch
            {
                var r => (r.Text, (LdtAutoDecodeWinner?)r.Winner)
            }
        };
    }

    /// <summary>
    /// 从 GBK、CP949、UTF-8（含错位尝试链）三候选中选取替换字符数最少者。
    /// UTF-8 仅在严格优于双字节编码时才获选，保持对历史遗留表的 GBK/CP949 优先。
    /// </summary>
    private static (string Text, LdtAutoDecodeWinner Winner) DecodeBestLdtStringWithWinner(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length == 0)
        {
            return (string.Empty, LdtAutoDecodeWinner.Empty);
        }

        var a = sLdtGbk.GetString(bytes);
        var b = sLdtCp949.GetString(bytes);
        var aBad = LdtStringDecodeHelper.CountReplacementChars(a);
        var bBad = LdtStringDecodeHelper.CountReplacementChars(b);

        // UTF-8 参与竞争：仅在严格更优时才优先（避免纯 ASCII 时不必要地切换到 UTF-8）
        var c = DecodeUtf8Chain(bytes);
        var cBad = LdtStringDecodeHelper.CountReplacementChars(c);
        if (cBad < aBad && cBad < bBad)
        {
            return (c, LdtAutoDecodeWinner.Utf8);
        }

        return aBad <= bBad ? (a, LdtAutoDecodeWinner.Gbk) : (b, LdtAutoDecodeWinner.Cp949);
    }

    /// <summary>
    /// UTF-8 错位尝试链：先剥 BOM，再尝试跳过最多 3 个前导字节，取替换字符数最少的结果。
    /// 适用于部分工具写入带 BOM 的 UTF-8 内容或字节边界错位的场景。
    /// </summary>
    private static string DecodeUtf8Chain(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length == 0)
        {
            return string.Empty;
        }

        // 剥 UTF-8 BOM（EF BB BF）
        var b = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF
            ? bytes[3..]
            : bytes;

        var best = Encoding.UTF8.GetString(b);
        var bestBad = LdtStringDecodeHelper.CountReplacementChars(best);
        if (bestBad == 0)
        {
            return best;
        }

        // 错位修正：依次跳过 1–3 个前导字节，取替换字符数更少的结果
        for (var skip = 1; skip <= Math.Min(3, b.Length - 1); skip++)
        {
            var candidate = Encoding.UTF8.GetString(b[skip..]);
            var candidateBad = LdtStringDecodeHelper.CountReplacementChars(candidate);
            if (candidateBad < bestBad)
            {
                best = candidate;
                bestBad = candidateBad;
                if (bestBad == 0)
                {
                    break;
                }
            }
        }

        return best;
    }

    private static void EnsureLength(byte[] data, int minLength)
    {
        if (data.Length < minLength)
        {
            throw new InvalidDataException($"File is too small ({data.Length} bytes), need at least {minLength} bytes.");
        }
    }
}

internal sealed class InsertEmptyRowsDialog : Form
{
    private readonly TextBox _countBox = new()
    {
        Width = 52,
        Height = 22,
        Text = "1",
        TextAlign = HorizontalAlignment.Center,
        BorderStyle = BorderStyle.FixedSingle,
        Margin = new Padding(0),
        MaxLength = 8,
        TabIndex = 0
    };

    public int Quantity { get; private set; }

    public InsertEmptyRowsDialog()
    {
        Text = "向下插入空行";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ShowIcon = false;
        KeyPreview = true;
        Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        BackColor = Color.FromArgb(236, 236, 236);
        ClientSize = new Size(268, 108);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(14, 12, 14, 12),
            BackColor = Color.FromArgb(236, 236, 236)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

        var topRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Color.FromArgb(236, 236, 236)
        };
        topRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        topRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 56));
        var hint = new Label
        {
            Text = "插入空行数量：",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 4, 8, 0),
            ForeColor = Color.FromArgb(40, 40, 40)
        };
        _countBox.Anchor = AnchorStyles.Left;
        topRow.Controls.Add(hint, 0, 0);
        topRow.Controls.Add(_countBox, 1, 0);

        var btnPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(236, 236, 236),
            Padding = new Padding(0, 12, 0, 0)
        };

        var okBtn = new Button
        {
            Text = "确定",
            Size = new Size(72, 26),
            DialogResult = DialogResult.None,
            AutoSize = false,
            TabIndex = 1,
            FlatStyle = FlatStyle.Flat,
            UseCompatibleTextRendering = false
        };
        okBtn.FlatAppearance.BorderColor = Color.FromArgb(150, 150, 150);
        okBtn.FlatAppearance.MouseOverBackColor = Color.FromArgb(232, 232, 232);
        okBtn.BackColor = Color.FromArgb(244, 244, 244);
        btnPanel.Controls.Add(okBtn);

        void CenterOkButton(object? s, EventArgs e)
        {
            okBtn.Left = Math.Max(0, (btnPanel.ClientSize.Width - okBtn.Width) / 2);
            okBtn.Top = Math.Max(0, (btnPanel.ClientSize.Height - okBtn.Height) / 2);
        }

        btnPanel.Resize += CenterOkButton;

        root.Controls.Add(topRow, 0, 0);
        root.Controls.Add(btnPanel, 0, 1);
        Controls.Add(root);

        AcceptButton = okBtn;

        okBtn.Click += (_, _) =>
        {
            if (!int.TryParse(_countBox.Text.Trim(), out var n) || n <= 0)
            {
                MessageBox.Show(this, "请输入大于 0 的整数。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            Quantity = n;
            DialogResult = DialogResult.OK;
        };

        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
            }
        };

        _countBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                okBtn.PerformClick();
                e.SuppressKeyPress = true;
            }
        };

        Shown += (_, _) =>
        {
            CenterOkButton(this, EventArgs.Empty);
            _countBox.SelectAll();
        };
    }
}

internal sealed class ReplacePreviewForm : Form
{
    private readonly DataGridView _grid = new()
    {
        Dock = DockStyle.Fill,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        ReadOnly = false,
        SelectionMode = DataGridViewSelectionMode.CellSelect,
        MultiSelect = true,
        RowHeadersVisible = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        BackgroundColor = SystemColors.Window,
        BorderStyle = BorderStyle.FixedSingle,
        EnableHeadersVisualStyles = false,
        ColumnHeadersDefaultCellStyle = { BackColor = Color.FromArgb(230, 230, 230) },
        EditMode = DataGridViewEditMode.EditOnKeystroke
    };

    public ReplacePreviewForm(
        List<ReplacePreviewEntry> entries,
        int skippedTypeMismatch,
        string windowTitle = "替换预览",
        string primaryActionText = "执行已勾选",
        string? hintOverride = null,
        bool textOnlyColumns = false)
    {
        Text = windowTitle;
        Width = 760;
        Height = 520;
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Regular, GraphicsUnit.Point);
        MinimumSize = new Size(560, 360);
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(8),
            ColumnCount = 1,
            RowCount = 3
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

        var defaultHint = skippedTypeMismatch > 0
            ? $"以下列出 {entries.Count} 处可安全写入的替换；另有 {skippedTypeMismatch} 处因类型校验未通过未显示。请勾选要应用的行后点击「{primaryActionText}」。"
            : $"以下列出 {entries.Count} 处替换。请勾选要应用的行后点击「{primaryActionText}」。";
        var hint = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.TopLeft,
            Text = hintOverride ?? defaultHint
        };

        var applyCol = new DataGridViewCheckBoxColumn
        {
            Name = "apply",
            HeaderText = "√",
            Width = 32,
            MinimumWidth = 32,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
            ThreeState = false,
            ReadOnly = false
        };
        _grid.Columns.Add(applyCol);
        if (!textOnlyColumns)
        {
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "row", HeaderText = "行", FillWeight = 12, ReadOnly = true });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "col", HeaderText = "列", FillWeight = 12, ReadOnly = true });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "name", HeaderText = "列名", FillWeight = 22, ReadOnly = true });
        }

        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "old", HeaderText = "原值", FillWeight = 28, ReadOnly = true });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "neu", HeaderText = "新值", FillWeight = 28, ReadOnly = true });
        applyCol.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        applyCol.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
        foreach (DataGridViewColumn col in _grid.Columns)
        {
            col.SortMode = DataGridViewColumnSortMode.NotSortable;
        }

        _grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_grid.IsCurrentCellDirty && _grid.CurrentCell is { ColumnIndex: 0 })
            {
                _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        };

        foreach (var e in entries)
        {
            var i = textOnlyColumns
                ? _grid.Rows.Add(true, e.OldText, e.NewText)
                : _grid.Rows.Add(true, e.Row, e.Column, e.ColumnLabel, e.OldText, e.NewText);
            _grid.Rows[i].Tag = e;
        }

        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 6, 0, 0)
        };
        var selectAllBtn = new Button { Text = "全选", AutoSize = true, Margin = new Padding(0, 0, 8, 0) };
        var selectNoneBtn = new Button { Text = "全不选", AutoSize = true, Margin = new Padding(0, 0, 8, 0) };
        var okBtn = new Button { Text = primaryActionText, AutoSize = true, Margin = new Padding(0, 0, 8, 0), DialogResult = DialogResult.OK };
        var cancelBtn = new Button { Text = "取消", AutoSize = true, DialogResult = DialogResult.Cancel };
        foreach (var b in new[] { selectAllBtn, selectNoneBtn, okBtn, cancelBtn })
        {
            b.Height = 26;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderColor = Color.FromArgb(150, 150, 150);
            b.BackColor = Color.FromArgb(238, 238, 238);
        }

        footer.Controls.Add(selectAllBtn);
        footer.Controls.Add(selectNoneBtn);
        footer.Controls.Add(okBtn);
        footer.Controls.Add(cancelBtn);
        AcceptButton = okBtn;
        CancelButton = cancelBtn;

        selectAllBtn.Click += (_, _) => SetAllChecks(true);
        selectNoneBtn.Click += (_, _) => SetAllChecks(false);

        root.Controls.Add(hint, 0, 0);
        root.Controls.Add(_grid, 0, 1);
        root.Controls.Add(footer, 0, 2);
        Controls.Add(root);
    }

    public List<ReplacePreviewEntry> GetSelectedEntries()
    {
        var list = new List<ReplacePreviewEntry>();
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.IsNewRow)
            {
                continue;
            }

            if (row.Tag is ReplacePreviewEntry e && row.Cells[0].Value is not null && Convert.ToBoolean(row.Cells[0].Value))
            {
                list.Add(e);
            }
        }

        return list;
    }

    private void SetAllChecks(bool value)
    {
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (!row.IsNewRow)
            {
                row.Cells[0].Value = value;
            }
        }
    }
}

internal sealed class FilterToolForm : Form
{
    private readonly Label _columnValueLabel = new()
    {
        AutoSize = false,
        Dock = DockStyle.Fill,
        BorderStyle = BorderStyle.FixedSingle,
        TextAlign = ContentAlignment.MiddleLeft
    };
    private readonly Label _resultLabel = new() { AutoSize = true, Text = "可见行: 0 / 总行: 0" };
    private readonly Label _filterRegexStatusLabel = new() { AutoSize = true, Text = "正则: 未启用" };
    private readonly FlowLayoutPanel _filterScopeHint = new()
    {
        Dock = DockStyle.Fill,
        AutoSize = false,
        FlowDirection = FlowDirection.LeftToRight,
        WrapContents = true,
        Visible = false,
        Padding = new Padding(0, 2, 0, 0),
        Margin = new Padding(0),
        BackColor = Color.Transparent
    };
    private readonly TextBox _keywordBox = new() { Dock = DockStyle.Fill };
    private readonly TextBox _replaceKeywordBox = new() { Dock = DockStyle.Fill };
    private readonly TextBox _replaceBox = new() { Dock = DockStyle.Fill };
    private readonly CheckBox _allColumnsCheckBox = new() { Text = "在全部列中搜索", AutoSize = true };
    private readonly CheckBox _matchCaseCheckBox = new() { Text = "区分大小写", AutoSize = true };
    private readonly CheckBox _regexCheckBox = new() { Text = "使用正则表达式", AutoSize = true };
    private readonly CheckBox _replaceMatchCaseCheckBox = new() { Text = "区分大小写", AutoSize = true };
    private readonly Action<string, bool, bool, bool> _applyAction;
    private readonly Action<string, string, bool, bool> _queryReplaceAction;
    private readonly Action<string, string, bool> _previewReplaceAction;
    private readonly Action<string, string, bool> _replaceAction;
    private readonly Action<double, bool, double, string, string, int, bool, bool, int> _batchNumberSeriesAction;
    private readonly Action? _traditionalToSimplifiedAction;
    private readonly Action _clearAction;
    private readonly Action _toggleAction;
    private readonly Action? _pushReplaceStateToMain;
    private readonly Action? _persistFilterToolUiToDisk;
    private readonly EditorToolOptionsBinding? _editorOptionsBinding;
    private bool _suppressPushReplaceToMain;
    private CheckBox? _requirePreviewOpt;
    private CheckBox? _backupBeforeOverwriteCheck;
    private CheckBox? _autoPersistModificationRecordsCheck;
    private ComboBox? _decodeCombo;
    private ComboBox? _saveEncCombo;
    private Label? _fontSummaryLabel;
    private string _optionsFontFamily = "";
    private float _optionsFontSizePts;
    private TextBox? _spfPathBox;
    private TextBox? _releaseAnnounceUrlBox;

    internal string LiveKeyword => _keywordBox.Text;
    internal bool LiveAllColumns => _allColumnsCheckBox.Checked;
    internal bool LiveMatchCase => _matchCaseCheckBox.Checked;
    internal bool LiveUseRegex => _regexCheckBox.Checked;

    internal string LiveReplaceKeyword => _replaceKeywordBox.Text;
    internal string LiveReplaceText => _replaceBox.Text;
    internal bool LiveReplaceMatchCase => _replaceMatchCaseCheckBox.Checked;

    internal string LiveBatchInsertStart => _batchStartBox.Text;
    internal string LiveBatchInsertStep => _batchStepBox.Text;
    internal bool LiveBatchInsertUseMultiply => _batchOpCombo.SelectedIndex == 1;
    internal string LiveBatchInsertPrefix => _batchPrefixBox.Text;
    internal string LiveBatchInsertSuffix => _batchSuffixBox.Text;
    internal int LiveBatchInsertOriginalPlacement => _batchRbOrigLeft.Checked ? 1 : _batchRbOrigRight.Checked ? 2 : 0;
    internal bool LiveBatchInsertVisibleRowsOnly => _batchVisibleOnlyCheck.Checked;
    internal bool LiveBatchInsertPreviewBeforeApply => _batchPreviewCheck.Checked;
    internal int LiveBatchInsertBoolRule => Math.Clamp(_batchBoolRuleCombo.SelectedIndex, 0, _batchBoolRuleCombo.Items.Count - 1);

    private Label _tabFindHeader = null!;
    private Label _tabReplaceHeader = null!;
    private Label _tabInsertHeader = null!;
    private Label _tabOptionsHeader = null!;
    private Panel _findPage = null!;
    private Panel _replacePage = null!;
    private Panel _insertPage = null!;
    private Panel _optionsPage = null!;
    private Button? _directReplaceButton;
    private TextBox _batchStartBox = null!;
    private TextBox _batchStepBox = null!;
    private ComboBox _batchOpCombo = null!;
    private TextBox _batchPrefixBox = null!;
    private TextBox _batchSuffixBox = null!;
    private RadioButton _batchRbOrigNone = null!;
    private RadioButton _batchRbOrigLeft = null!;
    private RadioButton _batchRbOrigRight = null!;
    private CheckBox _batchVisibleOnlyCheck = null!;
    private CheckBox _batchPreviewCheck = null!;
    private ComboBox _batchBoolRuleCombo = null!;
    private readonly ToolTip _formToolTip = new();

    public FilterToolForm(
        Action<string, bool, bool, bool> applyAction,
        Action<string, string, bool, bool> queryReplaceAction,
        Action<string, string, bool> previewReplaceAction,
        Action<string, string, bool> replaceAction,
        Action<double, bool, double, string, string, int, bool, bool, int> batchNumberSeriesAction,
        Action clearAction,
        Action toggleAction,
        Action? pushReplaceStateToMain = null,
        Action? persistFilterToolUiToDisk = null,
        EditorToolOptionsBinding? editorOptions = null,
        Action? traditionalToSimplifiedAction = null)
    {
        _applyAction = applyAction;
        _queryReplaceAction = queryReplaceAction;
        _previewReplaceAction = previewReplaceAction;
        _replaceAction = replaceAction;
        _batchNumberSeriesAction = batchNumberSeriesAction;
        _traditionalToSimplifiedAction = traditionalToSimplifiedAction;
        _clearAction = clearAction;
        _toggleAction = toggleAction;
        _pushReplaceStateToMain = pushReplaceStateToMain;
        _persistFilterToolUiToDisk = persistFilterToolUiToDisk;
        _editorOptionsBinding = editorOptions;

        Text = "功能菜单 (右键隐藏)";
        Width = 536;
        Height = 468;
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        ControlBox = false;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Regular, GraphicsUnit.Point);
        BackColor = Color.FromArgb(232, 232, 232);
        const int filterRootPadRight = 18;
        const int filterRootPadBottom = 12;
        // 仅中间内容区右侧留白（不动窗体最外缘）。
        const int filterContentPadRight = 4;
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(8, 10, filterRootPadRight, filterRootPadBottom),
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Color.FromArgb(232, 232, 232)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));

        var tabBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0),
            Padding = new Padding(0, 2, 0, 0),
            BackColor = Color.Transparent
        };
        _tabFindHeader = new Label
        {
            Text = "查找",
            Width = 92,
            Height = 22,
            TextAlign = ContentAlignment.MiddleCenter,
            BorderStyle = BorderStyle.FixedSingle,
            Margin = new Padding(0, 0, 6, 0),
            Cursor = Cursors.Hand,
            BackColor = Color.FromArgb(218, 232, 252)
        };
        _tabReplaceHeader = new Label
        {
            Text = "替换",
            Width = 92,
            Height = 22,
            TextAlign = ContentAlignment.MiddleCenter,
            BorderStyle = BorderStyle.FixedSingle,
            Margin = new Padding(0, 0, 6, 0),
            Cursor = Cursors.Hand,
            BackColor = Color.FromArgb(248, 248, 248)
        };
        _tabInsertHeader = new Label
        {
            Text = "插入",
            Width = 92,
            Height = 22,
            TextAlign = ContentAlignment.MiddleCenter,
            BorderStyle = BorderStyle.FixedSingle,
            Margin = new Padding(0, 0, 6, 0),
            Cursor = Cursors.Hand,
            BackColor = Color.FromArgb(248, 248, 248)
        };
        _tabOptionsHeader = new Label
        {
            Text = "选项",
            Width = 92,
            Height = 22,
            TextAlign = ContentAlignment.MiddleCenter,
            BorderStyle = BorderStyle.FixedSingle,
            Margin = new Padding(0, 0, 0, 0),
            Cursor = Cursors.Hand,
            BackColor = Color.FromArgb(248, 248, 248)
        };
        tabBar.Controls.Add(_tabFindHeader);
        tabBar.Controls.Add(_tabReplaceHeader);
        tabBar.Controls.Add(_tabInsertHeader);
        tabBar.Controls.Add(_tabOptionsHeader);
        root.Controls.Add(tabBar, 0, 0);

        var contentHost = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 4, 0, 0),
            BackColor = Color.FromArgb(236, 236, 236),
            BorderStyle = BorderStyle.FixedSingle,
            Padding = new Padding(2, 8, filterContentPadRight, 2)
        };
        _findPage = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(236, 236, 236), Visible = true };
        _replacePage = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(236, 236, 236), Visible = false };
        _insertPage = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(236, 236, 236), Visible = false };
        _optionsPage = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(240, 242, 246), Visible = false };

        var leftPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 5,
            Margin = new Padding(0),
            BackColor = Color.FromArgb(236, 236, 236),
            CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        leftPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 62));
        leftPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        leftPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        leftPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        leftPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        leftPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        leftPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        leftPanel.Controls.Add(new Label { Text = "当前列", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        leftPanel.Controls.Add(_columnValueLabel, 1, 0);
        leftPanel.Controls.Add(new Label { Text = "查找", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
        leftPanel.Controls.Add(_keywordBox, 1, 1);
        leftPanel.Controls.Add(new Label { Text = "模式", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 2);
        leftPanel.Controls.Add(_regexCheckBox, 1, 2);

        leftPanel.Controls.Add(new Label { Text = "选项", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 3);
        var findOptionsFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            AutoSize = false,
            Margin = new Padding(0),
            Padding = new Padding(0, 1, 0, 0),
            BackColor = Color.Transparent
        };
        findOptionsFlow.Controls.Add(_allColumnsCheckBox);
        findOptionsFlow.Controls.Add(_matchCaseCheckBox);
        leftPanel.Controls.Add(findOptionsFlow, 1, 3);

        leftPanel.Controls.Add(new Label { Text = "状态", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 4);
        var statusHost = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
            Padding = new Padding(0, 1, 0, 0)
        };
        statusHost.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        statusHost.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        statusHost.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var statusLabels = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.Transparent
        };
        _resultLabel.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold, GraphicsUnit.Point);
        _resultLabel.ForeColor = Color.FromArgb(30, 30, 30);
        _resultLabel.Margin = new Padding(0, 0, 0, 2);
        _filterRegexStatusLabel.Font = new Font("Microsoft YaHei UI", 8.0F, FontStyle.Regular, GraphicsUnit.Point);
        _filterRegexStatusLabel.ForeColor = Color.FromArgb(92, 92, 92);
        _filterRegexStatusLabel.Margin = new Padding(0, 0, 0, 0);
        statusLabels.Controls.Add(_resultLabel);
        statusLabels.Controls.Add(_filterRegexStatusLabel);
        statusHost.Controls.Add(statusLabels, 0, 0);

        var scopeIcon = new Label
        {
            Text = "✓",
            AutoSize = true,
            ForeColor = Color.FromArgb(0, 176, 80),
            Font = new Font("Segoe UI Symbol", 11F, FontStyle.Bold, GraphicsUnit.Point),
            Margin = new Padding(0, 0, 4, 0),
            TextAlign = ContentAlignment.MiddleLeft
        };
        var scopeText = new Label
        {
            Text = "筛选未显示全部行时，替换仍在当前列的可见行中进行",
            AutoSize = true,
            ForeColor = Color.FromArgb(0, 120, 55),
            Font = new Font("Microsoft YaHei UI", 8F, FontStyle.Regular, GraphicsUnit.Point),
            Margin = new Padding(0, 1, 0, 0),
            TextAlign = ContentAlignment.MiddleLeft
        };
        _filterScopeHint.Controls.Add(scopeIcon);
        _filterScopeHint.Controls.Add(scopeText);
        statusHost.Controls.Add(_filterScopeHint, 0, 1);

        leftPanel.Controls.Add(statusHost, 1, 4);

        var rightPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            ColumnCount = 2,
            RowCount = 6,
            BackColor = Color.FromArgb(236, 236, 236),
            CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        rightPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 62));
        rightPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        rightPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        rightPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        rightPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        rightPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        rightPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        rightPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        rightPanel.Controls.Add(new Label { Text = "替换", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        rightPanel.Controls.Add(new Label { Text = "当前列 · 字面量匹配", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.FromArgb(72, 72, 72) }, 1, 0);

        const int replacePanelBtnColWidth = 62;
        static Button MakeReplacePanelButton(string text) => new()
        {
            Text = text,
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            Padding = new Padding(1, 0, 1, 0),
            TextAlign = ContentAlignment.MiddleCenter,
            UseCompatibleTextRendering = false,
            MinimumSize = new Size(52, 22)
        };

        var queryRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = new Padding(0), Padding = new Padding(0) };
        queryRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        queryRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, replacePanelBtnColWidth));
        queryRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, replacePanelBtnColWidth));
        var queryPrevBtn = MakeReplacePanelButton("上个");
        var queryNextBtn = MakeReplacePanelButton("下个");
        queryRow.Controls.Add(_replaceKeywordBox, 0, 0);
        queryRow.Controls.Add(queryPrevBtn, 1, 0);
        queryRow.Controls.Add(queryNextBtn, 2, 0);
        rightPanel.Controls.Add(new Label { Text = "查找", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
        rightPanel.Controls.Add(queryRow, 1, 1);

        var replaceRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = new Padding(0), Padding = new Padding(0) };
        replaceRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        replaceRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, replacePanelBtnColWidth));
        replaceRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, replacePanelBtnColWidth));
        var previewReplaceBtn = MakeReplacePanelButton("预览…");
        var directReplaceBtn = MakeReplacePanelButton("直接");
        _directReplaceButton = directReplaceBtn;
        replaceRow.Controls.Add(_replaceBox, 0, 0);
        replaceRow.Controls.Add(previewReplaceBtn, 1, 0);
        replaceRow.Controls.Add(directReplaceBtn, 2, 0);
        rightPanel.Controls.Add(new Label { Text = "替换", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 2);
        rightPanel.Controls.Add(replaceRow, 1, 2);

        rightPanel.Controls.Add(new Label { Text = "选项", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 3);
        var replaceOptionsFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = false,
            Margin = new Padding(0),
            Padding = new Padding(0, 1, 0, 0),
            BackColor = Color.Transparent
        };
        replaceOptionsFlow.Controls.Add(_replaceMatchCaseCheckBox);
        rightPanel.Controls.Add(replaceOptionsFlow, 1, 3);

        var tradToSimpBtn = MakeReplacePanelButton("繁体→简体…");
        rightPanel.Controls.Add(new Label { Text = "繁简", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 4);
        rightPanel.Controls.Add(tradToSimpBtn, 1, 4);

        var optionsScroll = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = Color.FromArgb(240, 242, 246),
            Padding = new Padding(0, 0, 0, 0)
        };
        if (_editorOptionsBinding is not null)
        {
            var init = _editorOptionsBinding.Initial;
            _optionsFontFamily = init.GridFontFamily ?? "";
            _optionsFontSizePts = init.GridFontSizePoints;

            var optionsRoot = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 2,
                Padding = new Padding(6, 6, filterContentPadRight, 4),
                BackColor = Color.FromArgb(240, 242, 246)
            };
            optionsRoot.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 76));
            optionsRoot.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            static int AddOptionRow(TableLayoutPanel p, int row, string caption, Control valueControl)
            {
                p.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                p.Controls.Add(new Label
                {
                    Text = caption,
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleLeft,
                    AutoSize = true,
                    Margin = new Padding(0, 0, 2, 2),
                    ForeColor = Color.FromArgb(55, 55, 55),
                    Font = new Font("Microsoft YaHei UI", 8.25F, FontStyle.Regular, GraphicsUnit.Point)
                }, 0, row);
                valueControl.Margin = new Padding(0, 0, 0, 2);
                p.Controls.Add(valueControl, 1, row);
                return row + 1;
            }

            var rowIx = 0;
            _requirePreviewOpt = new CheckBox
            {
                Text = "须先预览后才可「直接」",
                AutoSize = true,
                Checked = init.RequireReplacePreview
            };
            rowIx = AddOptionRow(optionsRoot, rowIx, "替换", _requirePreviewOpt);

            _backupBeforeOverwriteCheck = new CheckBox
            {
                Text = "覆盖保存前写入「同路径 .bak」备份",
                AutoSize = true,
                Checked = init.BackupBeforeOverwrite
            };
            _formToolTip.SetToolTip(
                _backupBeforeOverwriteCheck,
                "在覆盖已有 LDT 前，将磁盘上当前文件复制为「原文件名 + .bak」。另存为覆盖已存在文件时同样生效。");

            _autoPersistModificationRecordsCheck = new CheckBox
            {
                Text = "自动保存修改记录",
                AutoSize = true,
                Checked = init.AutoPersistModificationRecords
            };
            _formToolTip.SetToolTip(
                _autoPersistModificationRecordsCheck,
                "在 LDT 已保存到磁盘（无未保存改动）时，将修改记录写入 %LocalAppData%\\LdtEditor\\change-records\\。");
            var savePanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                Margin = new Padding(0),
                Padding = new Padding(0),
                BackColor = Color.Transparent
            };
            savePanel.Controls.Add(_backupBeforeOverwriteCheck);
            savePanel.Controls.Add(_autoPersistModificationRecordsCheck);
            rowIx = AddOptionRow(optionsRoot, rowIx, "保存", savePanel);

            _decodeCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Dock = DockStyle.Top,
                Width = 340
            };
            _decodeCombo.Items.AddRange(new object[] { "自动择优", "GBK 936", "CP949", "UTF-8" });
            _decodeCombo.SelectedIndex = init.TextDecode switch
            {
                LdtTextDecodePreference.Gbk => 1,
                LdtTextDecodePreference.Cp949 => 2,
                LdtTextDecodePreference.Utf8 => 3,
                _ => 0
            };
            rowIx = AddOptionRow(optionsRoot, rowIx, "解码", _decodeCombo);

            _saveEncCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Dock = DockStyle.Top,
                Width = 340
            };
            _saveEncCombo.Items.AddRange(new object[] { "存盘 GBK", "存盘 CP949" });
            _saveEncCombo.SelectedIndex = init.SaveEncoding == LdtSaveStringEncoding.Cp949 ? 1 : 0;
            rowIx = AddOptionRow(optionsRoot, rowIx, "存盘", _saveEncCombo);

            var fontRow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoSize = true,
                Margin = new Padding(0),
                Padding = new Padding(0),
                BackColor = Color.Transparent
            };
            _fontSummaryLabel = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(300, 0),
                Text = FormatOptionsFontSummary(_optionsFontFamily, _optionsFontSizePts),
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.FromArgb(40, 40, 40),
                Margin = new Padding(0, 2, 6, 0)
            };
            var fontPickBtn = new Button { Text = "字体…", AutoSize = true, Margin = new Padding(0, 0, 0, 0) };
            fontRow.Controls.Add(_fontSummaryLabel);
            fontRow.Controls.Add(fontPickBtn);
            rowIx = AddOptionRow(optionsRoot, rowIx, "字体", fontRow);

            var spfBrowseColW = LogicalToDeviceUnits(82);
            var spfRow = new TableLayoutPanel
            {
                // 勿 Fill：在选项表 AutoSize 行内会纵向吃满剩余高度，连带把「浏览…」拉成巨高。
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0),
                Padding = new Padding(0),
                BackColor = Color.Transparent
            };
            spfRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            spfRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, spfBrowseColW));
            spfRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _spfPathBox = new TextBox
            {
                Multiline = false,
                Dock = DockStyle.Fill,
                Text = init.SpfArchivePath ?? ""
            };
            _spfPathBox.Font = Font;
            var spfLineH = Math.Max(LogicalToDeviceUnits(21), _spfPathBox.PreferredSize.Height);
            var spfBrowse = new Button
            {
                Text = "浏览…",
                Dock = DockStyle.None,
                AutoSize = false,
                Size = new Size(spfBrowseColW, spfLineH),
                Margin = new Padding(4, 0, 0, 0),
                Anchor = AnchorStyles.Top | AnchorStyles.Left
            };
            spfBrowse.Click += (_, _) =>
            {
                using var ofd = new OpenFileDialog
                {
                    Filter = "SPF (*.spf)|*.spf|All files|*.*",
                    Title = "选择 SPF 资源包"
                };
                if (ofd.ShowDialog(this) == DialogResult.OK)
                {
                    _spfPathBox!.Text = ofd.FileName;
                    FireOptionsApplyIfAllowed();
                }
            };
            spfRow.Controls.Add(_spfPathBox, 0, 0);
            spfRow.Controls.Add(spfBrowse, 1, 0);
            rowIx = AddOptionRow(optionsRoot, rowIx, "BANX", spfRow);

            _releaseAnnounceUrlBox = new TextBox
            {
                Multiline = false,
                Dock = DockStyle.Top,
                Text = init.ReleaseAnnouncementsUrl ?? ""
            };
            _releaseAnnounceUrlBox.Font = Font;
            _formToolTip.SetToolTip(
                _releaseAnnounceUrlBox,
                "可选。填写 https 地址后，主菜单「帮助 → 版本与公告」会尝试 GET 拉取文本（约 10 秒超时）。发布方可用静态页或 raw 文本托管更新说明。");
            rowIx = AddOptionRow(optionsRoot, rowIx, "公告URL", _releaseAnnounceUrlBox);

            void FireOptionsApplyIfAllowed()
            {
                if (_editorOptionsBinding is null
                    || _requirePreviewOpt is null
                    || _backupBeforeOverwriteCheck is null
                    || _autoPersistModificationRecordsCheck is null
                    || _decodeCombo is null
                    || _saveEncCombo is null
                    || _releaseAnnounceUrlBox is null)
                {
                    return;
                }

                var decode = _decodeCombo.SelectedIndex switch
                {
                    1 => LdtTextDecodePreference.Gbk,
                    2 => LdtTextDecodePreference.Cp949,
                    3 => LdtTextDecodePreference.Utf8,
                    _ => LdtTextDecodePreference.Auto
                };
                var save = _saveEncCombo.SelectedIndex == 1 ? LdtSaveStringEncoding.Cp949 : LdtSaveStringEncoding.Gbk;
                var spfPath = _spfPathBox?.Text?.Trim() ?? "";
                var announceUrl = _releaseAnnounceUrlBox.Text?.Trim() ?? "";
                var next = new EditorAppSettings(
                    _requirePreviewOpt.Checked,
                    decode,
                    save,
                    _optionsFontFamily,
                    _optionsFontSizePts,
                    spfPath,
                    ShowItemDescriptionPanel: false,
                    ShowQuickFindBar: false,
                    BackupBeforeOverwrite: _backupBeforeOverwriteCheck.Checked,
                    AutoPersistModificationRecords: _autoPersistModificationRecordsCheck.Checked,
                    SuppressModificationRecordsClosePrompt: init.SuppressModificationRecordsClosePrompt,
                    ModificationTrackerVisible: init.ModificationTrackerVisible,
                    ReleaseAnnouncementsUrl: announceUrl,
                    IconAtlasCacheMaxEntries: init.IconAtlasCacheMaxEntries,
                    SpfChainLiteralScanMaxEntries: init.SpfChainLiteralScanMaxEntries,
                    SpfChainLiteralScanMaxKiBPerEntry: init.SpfChainLiteralScanMaxKiBPerEntry,
                    SpfPngNameScanMaxKiB: init.SpfPngNameScanMaxKiB,
                    IconAtlasDecodeMaxMegapixels: init.IconAtlasDecodeMaxMegapixels,
                    SpfPngChainStartOffset: init.SpfPngChainStartOffset);
                _editorOptionsBinding.Apply(next);
            }

            _requirePreviewOpt.CheckedChanged += (_, _) =>
            {
                FireOptionsApplyIfAllowed();
                UpdateDirectReplaceForPreviewPolicy();
            };
            _backupBeforeOverwriteCheck.CheckedChanged += (_, _) => FireOptionsApplyIfAllowed();
            _autoPersistModificationRecordsCheck.CheckedChanged += (_, _) => FireOptionsApplyIfAllowed();
            _decodeCombo.SelectedIndexChanged += (_, _) => FireOptionsApplyIfAllowed();
            _saveEncCombo.SelectedIndexChanged += (_, _) => FireOptionsApplyIfAllowed();
            _spfPathBox!.Leave += (_, _) => FireOptionsApplyIfAllowed();
            _releaseAnnounceUrlBox.Leave += (_, _) => FireOptionsApplyIfAllowed();
            fontPickBtn.Click += (_, _) =>
            {
                using var fd = new FontDialog();
                var fam = string.IsNullOrWhiteSpace(_optionsFontFamily) ? "Microsoft YaHei UI" : _optionsFontFamily;
                var sz = _optionsFontSizePts <= 0 ? 9f : _optionsFontSizePts;
                try
                {
                    fd.Font = new Font(fam, sz, FontStyle.Regular, GraphicsUnit.Point);
                }
                catch
                {
                    fd.Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Regular, GraphicsUnit.Point);
                }

                if (fd.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                _optionsFontFamily = fd.Font.Name;
                _optionsFontSizePts = fd.Font.SizeInPoints;
                if (_fontSummaryLabel is not null)
                {
                    _fontSummaryLabel.Text = FormatOptionsFontSummary(_optionsFontFamily, _optionsFontSizePts);
                }

                FireOptionsApplyIfAllowed();
            };

            optionsScroll.Controls.Add(optionsRoot);
        }
        else
        {
            var optionsTips = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(520, 0),
                Location = new Point(10, 10),
                Text = "选项页未绑定设置数据。",
                ForeColor = Color.FromArgb(55, 55, 55),
                Font = new Font("Microsoft YaHei UI", 8.25F, FontStyle.Regular, GraphicsUnit.Point),
                BackColor = Color.Transparent
            };
            optionsScroll.Controls.Add(optionsTips);
        }

        _optionsPage.Controls.Add(optionsScroll);

        leftPanel.Dock = DockStyle.Fill;
        _findPage.Controls.Add(leftPanel);
        rightPanel.Dock = DockStyle.Fill;
        _replacePage.Controls.Add(rightPanel);

        var insertScrollHost = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = Color.FromArgb(236, 236, 236),
            Padding = new Padding(2, 0, 0, 2)
        };
        var insertRoot = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 10,
            Padding = new Padding(2, 2, filterContentPadRight, 4),
            BackColor = Color.FromArgb(236, 236, 236),
            Margin = new Padding(0)
        };
        insertRoot.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 76));
        insertRoot.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        for (var ir = 0; ir < 10; ir++)
        {
            var sizeType = ir is 0 or 5 or 6 or 8 or 9 ? SizeType.AutoSize : SizeType.Absolute;
            var height = sizeType == SizeType.AutoSize ? 0 : 30;
            insertRoot.RowStyles.Add(new RowStyle(sizeType, height));
        }

        void LayoutInsertRootWidth(object? sender, EventArgs e)
        {
            var w = insertScrollHost.ClientSize.Width - insertScrollHost.Padding.Horizontal;
            if (insertScrollHost.VerticalScroll.Visible)
            {
                w -= SystemInformation.VerticalScrollBarWidth;
            }

            insertRoot.Width = Math.Max(120, w);
        }

        insertScrollHost.Resize += LayoutInsertRootWidth;
        insertScrollHost.Layout += LayoutInsertRootWidth;

        var batchHint = new Label
        {
            Text = "按当前列生成数值。可选「仅筛选可见行」整列写入；建议「写入前预览」。参数会自动保存。重要表请先备份。",
            AutoSize = true,
            MaximumSize = new Size(430, 0),
            Margin = new Padding(0, 0, 0, 6),
            ForeColor = Color.FromArgb(55, 55, 55),
            Font = new Font("Microsoft YaHei UI", 8.25F, FontStyle.Regular, GraphicsUnit.Point)
        };
        insertRoot.SetColumnSpan(batchHint, 2);
        insertRoot.Controls.Add(batchHint, 0, 0);

        _batchStartBox = new TextBox { Text = "0", Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle };
        insertRoot.Controls.Add(new Label { Text = "起始值", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
        insertRoot.Controls.Add(_batchStartBox, 1, 1);

        _batchOpCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 96,
            Margin = new Padding(0, 1, 6, 0)
        };
        _batchOpCombo.Items.AddRange(new object[] { "加法 (+)", "乘法 (×)" });
        _batchOpCombo.SelectedIndex = 0;
        _batchStepBox = new TextBox { Text = "1", Width = 110, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(0, 1, 0, 0) };
        var batchStepFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = false,
            Margin = new Padding(0),
            Padding = new Padding(0, 0, 0, 0),
            BackColor = Color.Transparent
        };
        batchStepFlow.Controls.Add(_batchOpCombo);
        batchStepFlow.Controls.Add(new Label { Text = "步长", AutoSize = true, Margin = new Padding(0, 6, 4, 0) });
        batchStepFlow.Controls.Add(_batchStepBox);
        insertRoot.Controls.Add(new Label { Text = "运算", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 2);
        insertRoot.Controls.Add(batchStepFlow, 1, 2);

        _batchPrefixBox = new TextBox { Text = "", Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle };
        _batchSuffixBox = new TextBox { Text = "", Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle };
        insertRoot.Controls.Add(new Label { Text = "前缀", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 3);
        insertRoot.Controls.Add(_batchPrefixBox, 1, 3);
        insertRoot.Controls.Add(new Label { Text = "后缀", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 4);
        insertRoot.Controls.Add(_batchSuffixBox, 1, 4);

        _batchRbOrigNone = new RadioButton { Text = "不使用原单元格文本", AutoSize = true, Checked = true, Margin = new Padding(0, 2, 12, 0) };
        _batchRbOrigLeft = new RadioButton { Text = "原值在左", AutoSize = true, Margin = new Padding(0, 2, 12, 0) };
        _batchRbOrigRight = new RadioButton { Text = "原值在右", AutoSize = true, Margin = new Padding(0, 2, 0, 0) };
        var origFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            AutoSize = true,
            Margin = new Padding(0),
            Padding = new Padding(0, 0, 0, 0),
            BackColor = Color.Transparent
        };
        origFlow.Controls.Add(_batchRbOrigNone);
        origFlow.Controls.Add(_batchRbOrigLeft);
        origFlow.Controls.Add(_batchRbOrigRight);
        insertRoot.Controls.Add(new Label { Text = "原值", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 5);
        insertRoot.Controls.Add(origFlow, 1, 5);

        _batchVisibleOnlyCheck = new CheckBox
        {
            Text = "仅当前筛选可见行（表内顺序整列，无需框选）",
            AutoSize = true,
            MaximumSize = new Size(420, 0),
            Margin = new Padding(0, 2, 0, 0)
        };
        _batchPreviewCheck = new CheckBox
        {
            Text = "写入前打开预览（可取消部分行）",
            AutoSize = true,
            MaximumSize = new Size(420, 0),
            Checked = true,
            Margin = new Padding(0, 2, 0, 0)
        };
        var batchOptionsFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            Margin = new Padding(0),
            Padding = new Padding(0, 2, 0, 0),
            BackColor = Color.Transparent
        };
        batchOptionsFlow.Controls.Add(_batchVisibleOnlyCheck);
        batchOptionsFlow.Controls.Add(_batchPreviewCheck);
        insertRoot.Controls.Add(new Label { Text = "范围", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 6);
        insertRoot.Controls.Add(batchOptionsFlow, 1, 6);

        _batchBoolRuleCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 1, 0, 0)
        };
        _batchBoolRuleCombo.Items.AddRange(new object[]
        {
            "四舍五入后奇偶（偶0奇1）",
            "四舍五入后非零为 1",
            "数值 ≥0.5 为 1",
            "向下取整后奇偶（偶0奇1）",
            "向上取整后奇偶（偶0奇1）"
        });
        _batchBoolRuleCombo.SelectedIndex = 0;
        insertRoot.Controls.Add(new Label { Text = "布尔列", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 7);
        insertRoot.Controls.Add(_batchBoolRuleCombo, 1, 7);

        var batchNote = new Label
        {
            Text = "未勾选「可见行」时须在主表选中行。乘法：首行起始值，之后每行乘步长。布尔列规则见上栏（仅列类型为布尔/位时生效）。",
            AutoSize = true,
            MaximumSize = new Size(430, 0),
            Margin = new Padding(0, 4, 0, 4),
            ForeColor = Color.FromArgb(92, 92, 92),
            Font = new Font("Microsoft YaHei UI", 8F, FontStyle.Regular, GraphicsUnit.Point)
        };
        insertRoot.SetColumnSpan(batchNote, 2);
        insertRoot.Controls.Add(batchNote, 0, 8);

        var batchGenBtn = new Button
        {
            Text = "生成数值",
            AutoSize = true,
            Margin = new Padding(0, 2, 0, 0),
            Padding = new Padding(12, 4, 12, 4)
        };
        insertRoot.SetColumnSpan(batchGenBtn, 2);
        insertRoot.Controls.Add(batchGenBtn, 0, 9);

        insertScrollHost.Controls.Add(insertRoot);
        _insertPage.Controls.Add(insertScrollHost);

        _formToolTip.SetToolTip(
            _batchVisibleOnlyCheck,
            "勾选后按当前筛选结果的可见行顺序写入整列，与主表中是否框选无关。取消勾选则仅写入主表当前列上已选中的行。");
        _formToolTip.SetToolTip(
            _batchPreviewCheck,
            "勾选后先打开列表核对「原值 / 新值」，可取消部分行再写入。取消勾选则一次性写入全部匹配单元格（仍会二次确认）。");

        batchGenBtn.Click += (_, _) =>
        {
            var startText = _batchStartBox.Text.Trim();
            if (!double.TryParse(startText, NumberStyles.Float, CultureInfo.InvariantCulture, out var st)
                && !double.TryParse(startText, NumberStyles.Float, CultureInfo.CurrentCulture, out st))
            {
                MessageBox.Show("起始值不是有效数字。", "批量生成数值", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var stepText = _batchStepBox.Text.Trim();
            if (!double.TryParse(stepText, NumberStyles.Float, CultureInfo.InvariantCulture, out var sp)
                && !double.TryParse(stepText, NumberStyles.Float, CultureInfo.CurrentCulture, out sp))
            {
                MessageBox.Show("步长不是有效数字。", "批量生成数值", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var useMul = _batchOpCombo.SelectedIndex == 1;
            var placement = _batchRbOrigLeft.Checked ? 1 : _batchRbOrigRight.Checked ? 2 : 0;
            _batchNumberSeriesAction(
                st,
                useMul,
                sp,
                _batchPrefixBox.Text,
                _batchSuffixBox.Text,
                placement,
                _batchVisibleOnlyCheck.Checked,
                _batchPreviewCheck.Checked,
                _batchBoolRuleCombo.SelectedIndex);
        };

        contentHost.Controls.Add(_replacePage);
        contentHost.Controls.Add(_optionsPage);
        contentHost.Controls.Add(_insertPage);
        contentHost.Controls.Add(_findPage);
        root.Controls.Add(contentHost, 0, 1);

        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0),
            Padding = new Padding(4, 8, filterContentPadRight, 10),
            BackColor = Color.FromArgb(232, 232, 232)
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        var applyBtn = new Button { Text = "执行查找", Dock = DockStyle.Fill, Margin = new Padding(0, 0, 6, 0) };
        var clearBtn = new Button { Text = "清除条件", Dock = DockStyle.Fill, Margin = new Padding(6, 0, 0, 0) };
        footer.Controls.Add(applyBtn, 0, 0);
        footer.Controls.Add(clearBtn, 1, 0);
        root.Controls.Add(footer, 0, 2);
        Controls.Add(root);

        Shown += (_, _) => LayoutInsertRootWidth(insertScrollHost, EventArgs.Empty);

        void SetActiveFilterTab(int tab)
        {
            _findPage.Visible = tab == 0;
            _replacePage.Visible = tab == 1;
            _insertPage.Visible = tab == 2;
            _optionsPage.Visible = tab == 3;
            var on = Color.FromArgb(210, 228, 252);
            var off = Color.FromArgb(248, 248, 248);
            _tabFindHeader.BackColor = tab == 0 ? on : off;
            _tabReplaceHeader.BackColor = tab == 1 ? on : off;
            _tabInsertHeader.BackColor = tab == 2 ? on : off;
            _tabOptionsHeader.BackColor = tab == 3 ? on : off;
            var showFindFooter = tab == 0;
            footer.Visible = showFindFooter;
            root.RowStyles[2] = new RowStyle(SizeType.Absolute, showFindFooter ? 50 : 0);
            if (tab == 2)
            {
                BeginInvoke(new Action(() => LayoutInsertRootWidth(null, EventArgs.Empty)));
            }
        }

        _tabFindHeader.MouseClick += (_, _) => SetActiveFilterTab(0);
        _tabReplaceHeader.MouseClick += (_, _) => SetActiveFilterTab(1);
        _tabInsertHeader.MouseClick += (_, _) => SetActiveFilterTab(2);
        _tabOptionsHeader.MouseClick += (_, _) => SetActiveFilterTab(3);
        SetActiveFilterTab(0);

        _columnValueLabel.BackColor = Color.FromArgb(246, 246, 246);
        _keywordBox.BorderStyle = BorderStyle.FixedSingle;
        _replaceKeywordBox.BorderStyle = BorderStyle.FixedSingle;
        _replaceBox.BorderStyle = BorderStyle.FixedSingle;
        _resultLabel.ForeColor = Color.FromArgb(40, 40, 40);
        void StyleToolBarButton(Button btn, int height)
        {
            btn.Height = height;
            btn.FlatStyle = FlatStyle.Flat;
            btn.FlatAppearance.BorderColor = Color.FromArgb(150, 150, 150);
            btn.FlatAppearance.MouseOverBackColor = Color.FromArgb(232, 232, 232);
            btn.BackColor = Color.FromArgb(238, 238, 238);
        }

        foreach (var btn in new[] { queryPrevBtn, queryNextBtn, previewReplaceBtn, directReplaceBtn, tradToSimpBtn })
        {
            StyleToolBarButton(btn, 22);
        }

        foreach (var btn in new[] { applyBtn, clearBtn })
        {
            StyleToolBarButton(btn, 28);
            btn.AutoSize = false;
            btn.MinimumSize = new Size(0, 28);
        }

        StyleToolBarButton(batchGenBtn, 26);

        _formToolTip.SetToolTip(_regexCheckBox,
            "正则表达式小教程（简单版）：\n" +
            "1) 直接写文字：abc  -> 包含 abc\n" +
            "2) . 代表任意一个字符：a.c  -> 可匹配 abc、a1c\n" +
            "3) * 代表前一个字符可重复：ab*c  -> 匹配 ac、abc、abbbc\n" +
            "4) ^ 开头、$ 结尾：^item_\\d+$  -> 只匹配 item_1、item_20 这类整段文本\n" +
            "5) \\d 代表数字，\\w 代表字母数字下划线\n" +
            "建议从简单模式开始，不会写正则时先不要勾选此开关。");
        _formToolTip.SetToolTip(previewReplaceBtn, "打开预览列表：勾选后再写入，降低误替换风险。");
        _formToolTip.SetToolTip(tradToSimpBtn, "将全部文本列中、当前筛选可见行的繁体中文转为简体，打开预览后写入。");
        _formToolTip.SetToolTip(_replaceMatchCaseCheckBox, "替换区按字面量查找：区分大小写时须完全匹配大小写。");
        UpdateDirectReplaceForPreviewPolicy();

        RegisterRightClickHide(this);

        applyBtn.Click += (_, _) => Apply();
        queryPrevBtn.Click += (_, _) => QueryReplace(false);
        queryNextBtn.Click += (_, _) => QueryReplace(true);
        previewReplaceBtn.Click += (_, _) => PreviewReplace();
        directReplaceBtn.Click += (_, _) => Replace();
        tradToSimpBtn.Click += (_, _) => _traditionalToSimplifiedAction?.Invoke();
        clearBtn.Click += (_, _) => Clear();
        _keywordBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                Apply();
                e.SuppressKeyPress = true;
            }
        };
        _keywordBox.TextChanged += (_, _) => UpdateRegexStatus();
        _allColumnsCheckBox.CheckedChanged += (_, _) => Apply();
        _matchCaseCheckBox.CheckedChanged += (_, _) =>
        {
            UpdateRegexStatus();
            Apply();
        };
        _regexCheckBox.CheckedChanged += (_, _) =>
        {
            UpdateRegexStatus();
            Apply();
        };
        if (_pushReplaceStateToMain is not null)
        {
            void PushReplaceIfNeeded()
            {
                if (_suppressPushReplaceToMain)
                {
                    return;
                }

                _pushReplaceStateToMain();
            }

            _replaceKeywordBox.TextChanged += (_, _) => PushReplaceIfNeeded();
            _replaceBox.TextChanged += (_, _) => PushReplaceIfNeeded();
            _replaceMatchCaseCheckBox.CheckedChanged += (_, _) => PushReplaceIfNeeded();
        }

        if (_persistFilterToolUiToDisk is not null)
        {
            void PersistIfNeeded() => _persistFilterToolUiToDisk();
            _keywordBox.Leave += (_, _) => PersistIfNeeded();
            _replaceKeywordBox.Leave += (_, _) => PersistIfNeeded();
            _replaceBox.Leave += (_, _) => PersistIfNeeded();
            _batchStartBox.Leave += (_, _) => PersistIfNeeded();
            _batchStepBox.Leave += (_, _) => PersistIfNeeded();
            _batchPrefixBox.Leave += (_, _) => PersistIfNeeded();
            _batchSuffixBox.Leave += (_, _) => PersistIfNeeded();
            _batchOpCombo.SelectedIndexChanged += (_, _) => PersistIfNeeded();
            _batchRbOrigNone.CheckedChanged += (_, _) => PersistIfNeeded();
            _batchRbOrigLeft.CheckedChanged += (_, _) => PersistIfNeeded();
            _batchRbOrigRight.CheckedChanged += (_, _) => PersistIfNeeded();
            _batchVisibleOnlyCheck.CheckedChanged += (_, _) => PersistIfNeeded();
            _batchPreviewCheck.CheckedChanged += (_, _) => PersistIfNeeded();
            _batchBoolRuleCombo.SelectedIndexChanged += (_, _) => PersistIfNeeded();
        }

        UpdateRegexStatus();
    }

    private static string FormatOptionsFontSummary(string family, float sizePts)
    {
        if (string.IsNullOrWhiteSpace(family) || sizePts <= 0)
        {
            return "默认 (Microsoft YaHei UI 9pt)";
        }

        return $"{family.Trim()}, {sizePts:g}pt";
    }

    private void UpdateDirectReplaceForPreviewPolicy()
    {
        if (_directReplaceButton is null)
        {
            return;
        }

        if (_requirePreviewOpt is null)
        {
            _directReplaceButton.Enabled = true;
            _formToolTip.SetToolTip(_directReplaceButton, "不经预览，立即对全部匹配项执行替换（需二次确认）。");
            return;
        }

        var locked = _requirePreviewOpt.Checked;
        _directReplaceButton.Enabled = !locked;
        _formToolTip.SetToolTip(
            _directReplaceButton,
            locked
                ? "选项要求须先预览，请使用「预览…」。"
                : "不经预览，立即对全部匹配项执行替换（需二次确认）。");
    }

    public void SetContext(
        string[] columnNames,
        int selectedColumnIndex,
        string keyword,
        bool allColumns,
        bool matchCase,
        bool useRegex,
        string replaceKeyword,
        string replaceText,
        bool replaceMatchCase)
    {
        _suppressPushReplaceToMain = true;
        try
        {
            if (selectedColumnIndex >= 0)
            {
                var label = selectedColumnIndex < columnNames.Length && !string.IsNullOrWhiteSpace(columnNames[selectedColumnIndex])
                    ? columnNames[selectedColumnIndex]
                    : $"COL_{selectedColumnIndex}";
                _columnValueLabel.Text = $"{label} (#{selectedColumnIndex})";
            }
            else
            {
                _columnValueLabel.Text = "未选中列";
            }

            if (!string.Equals(_keywordBox.Text, keyword, StringComparison.Ordinal))
            {
                _keywordBox.Text = keyword;
            }

            if (!string.Equals(_replaceKeywordBox.Text, replaceKeyword, StringComparison.Ordinal))
            {
                _replaceKeywordBox.Text = replaceKeyword;
            }

            if (!string.Equals(_replaceBox.Text, replaceText, StringComparison.Ordinal))
            {
                _replaceBox.Text = replaceText;
            }

            if (_allColumnsCheckBox.Checked != allColumns)
            {
                _allColumnsCheckBox.Checked = allColumns;
            }

            if (_matchCaseCheckBox.Checked != matchCase)
            {
                _matchCaseCheckBox.Checked = matchCase;
            }

            if (_regexCheckBox.Checked != useRegex)
            {
                _regexCheckBox.Checked = useRegex;
            }

            if (_replaceMatchCaseCheckBox.Checked != replaceMatchCase)
            {
                _replaceMatchCaseCheckBox.Checked = replaceMatchCase;
            }
        }
        finally
        {
            _suppressPushReplaceToMain = false;
        }

        UpdateRegexStatus();
    }

    public void SetResultInfo(int visibleRows, int totalRows)
    {
        _resultLabel.Text = $"可见行: {visibleRows} / 总行: {totalRows}";
        _filterScopeHint.Visible = totalRows > 0 && visibleRows < totalRows;
    }

    public void SyncBatchInsertFromMain(
        string start,
        string step,
        bool useMultiply,
        string prefix,
        string suffix,
        int originalPlacement,
        bool visibleRowsOnly,
        bool previewBeforeApply,
        int boolRule)
    {
        if (!string.Equals(_batchStartBox.Text, start ?? "", StringComparison.Ordinal))
        {
            _batchStartBox.Text = string.IsNullOrEmpty(start) ? "0" : start;
        }

        if (!string.Equals(_batchStepBox.Text, step ?? "", StringComparison.Ordinal))
        {
            _batchStepBox.Text = string.IsNullOrEmpty(step) ? "1" : step;
        }

        var mulIx = useMultiply ? 1 : 0;
        if (_batchOpCombo.SelectedIndex != mulIx)
        {
            _batchOpCombo.SelectedIndex = mulIx;
        }

        if (!string.Equals(_batchPrefixBox.Text, prefix ?? "", StringComparison.Ordinal))
        {
            _batchPrefixBox.Text = prefix ?? "";
        }

        if (!string.Equals(_batchSuffixBox.Text, suffix ?? "", StringComparison.Ordinal))
        {
            _batchSuffixBox.Text = suffix ?? "";
        }

        _batchRbOrigLeft.Checked = originalPlacement == 1;
        _batchRbOrigRight.Checked = originalPlacement == 2;
        _batchRbOrigNone.Checked = originalPlacement != 1 && originalPlacement != 2;
        if (_batchVisibleOnlyCheck.Checked != visibleRowsOnly)
        {
            _batchVisibleOnlyCheck.Checked = visibleRowsOnly;
        }

        if (_batchPreviewCheck.Checked != previewBeforeApply)
        {
            _batchPreviewCheck.Checked = previewBeforeApply;
        }

        var br = Math.Clamp(boolRule, 0, _batchBoolRuleCombo.Items.Count - 1);
        if (_batchBoolRuleCombo.SelectedIndex != br)
        {
            _batchBoolRuleCombo.SelectedIndex = br;
        }
    }

    private void Apply()
    {
        _applyAction(_keywordBox.Text, _allColumnsCheckBox.Checked, _matchCaseCheckBox.Checked, _regexCheckBox.Checked);
    }

    private void Clear()
    {
        _keywordBox.Text = string.Empty;
        _clearAction();
    }

    private void QueryReplace(bool forward)
    {
        _queryReplaceAction(
            _replaceKeywordBox.Text,
            _replaceBox.Text,
            _replaceMatchCaseCheckBox.Checked,
            forward);
    }

    private void PreviewReplace()
    {
        _previewReplaceAction(
            _replaceKeywordBox.Text,
            _replaceBox.Text,
            _replaceMatchCaseCheckBox.Checked);
    }

    private void Replace()
    {
        _replaceAction(
            _replaceKeywordBox.Text,
            _replaceBox.Text,
            _replaceMatchCaseCheckBox.Checked);
    }

    private void UpdateRegexStatus()
    {
        if (!_regexCheckBox.Checked)
        {
            _filterRegexStatusLabel.Text = "正则: 未启用";
            _filterRegexStatusLabel.ForeColor = SystemColors.ControlText;
            return;
        }

        var keyword = _keywordBox.Text;
        if (string.IsNullOrWhiteSpace(keyword))
        {
            _filterRegexStatusLabel.Text = "正则: 请输入表达式";
            _filterRegexStatusLabel.ForeColor = SystemColors.ControlText;
            return;
        }

        var options = _matchCaseCheckBox.Checked ? RegexOptions.None : RegexOptions.IgnoreCase;
        try
        {
            _ = new Regex(keyword, options);
            _filterRegexStatusLabel.Text = "正则: 语法有效";
            _filterRegexStatusLabel.ForeColor = Color.ForestGreen;
        }
        catch (ArgumentException ex)
        {
            _filterRegexStatusLabel.Text = $"正则: 语法错误 ({ex.Message})";
            _filterRegexStatusLabel.ForeColor = Color.Firebrick;
        }
    }


    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.F))
        {
            _toggleAction();
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void WndProc(ref Message m)
    {
        const int WM_NCRBUTTONUP = 0x00A5;
        const int HTCAPTION = 2;
        if (m.Msg == WM_NCRBUTTONUP && m.WParam == (IntPtr)HTCAPTION)
        {
            Hide();
            return;
        }

        base.WndProc(ref m);
    }

    private void RegisterRightClickHide(Control root)
    {
        root.MouseUp += (_, e) =>
        {
            if (e.Button == MouseButtons.Right)
            {
                Hide();
            }
        };

        foreach (Control child in root.Controls)
        {
            RegisterRightClickHide(child);
        }
    }
}
