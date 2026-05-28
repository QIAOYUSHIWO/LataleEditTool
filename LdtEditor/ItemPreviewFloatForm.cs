using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

internal sealed class ItemPreviewModel
{
    public string Title { get; set; } = "";
    public Color TitleColor { get; set; } = Color.DarkRed;
    public string Subtitle { get; set; } = "";
    public string LevelLine { get; set; } = "";
    public string DecomposeLine { get; set; } = "";
    public string UseTypeHeader { get; set; } = "";
    public string LifeTimeLine { get; set; } = "";
    public List<string> GreenStatLines { get; } = new();
    /// <summary>与 <see cref="GreenStatLines"/> 一一对应的可编辑元数据；占位行（如「（多选 · 首行）」）对应 <c>Editable=false</c> 项。</summary>
    public List<StatusLineMeta> StatusLineMetas { get; } = new();
    public string LoreTitle { get; set; } = "";
    public string LoreBody { get; set; } = "";
    public List<ItemPreviewFooterTag> FooterTags { get; } = new();
    public List<string> BlackLines { get; } = new();
}

/// <summary>
/// 物品预览 Status 行可编辑元数据：用于内联编辑时反查 ValueCol、数值文本在本行字符串中的子串范围、以及百分/千分换算。
/// 通过<see cref="Editable"/>区分占位行（如多选首行提示）。
/// </summary>
internal sealed class StatusLineMeta
{
    public bool Editable { get; init; }
    public int TypeId { get; init; }
    public int ValueCol { get; init; } = -1;
    public int StoredValue { get; init; }
    public int NumStartInLine { get; init; }
    public int NumLength { get; init; }
    public bool HasPercentSuffix { get; init; }
    public bool IsPermilleStored { get; init; }

    public static StatusLineMeta NotEditable { get; } = new() { Editable = false };
}

internal sealed class ItemPreviewFooterTag(string label, bool on)
{
    public string Label { get; } = label;
    public bool On { get; } = on;
}

internal sealed class ItemPreviewColumnMap
{
    public int DescColumn = -1;
    public int NameColumn = -1;
    public int TitleRedColumn = -1;
    public int TitleGreenColumn = -1;
    public int TitleBlueColumn = -1;
    public int UseTypeColumn = -1;
    public int BreakupColumn = -1;
    public int SubtitleTextColumn = -1;
    public int SubTypeNumberColumn = -1;
    public int InventoryTypeColumn = -1;
    public int InventorySubTypeColumn = -1;
    public int PosId1Column = -1;
    public int LevelOwnColumn = -1;
    public int ItemIdColumn = -1;
    public int LifetimeColumn = -1;
    public List<(int TypeCol, int ValueCol)> StatusPairs = new();
    public int[] FooterSlotColumns { get; } = [-1, -1, -1, -1];
}

internal sealed class ItemPreviewFloatForm : Form
{
    /// <summary>上部正文（等级/属性等）换行：单换行略紧凑于 CRLF，减轻段落间距。</summary>
    private const string UpperContentNewLine = "\n";
    /// <summary>RichEdit 软换行（与 Shift+Enter 相同）：同一段内折行，段落格式里的行距才会作用在词条行间。</summary>
    private const char StatLinesSoftBreak = '\v';

    private static readonly Color FooterValueOffGray = Color.FromArgb(184, 184, 184);
    private static readonly Color FooterValueOnGreen = Color.FromArgb(76, 168, 76);
    /// <summary>与客户端说明条接近的深绿色（Use_Type）。</summary>
    private static readonly Color UseTypeDeepGreen = Color.FromArgb(28, 112, 62);
    /// <summary>有效时间行的橙棕色。</summary>
    private static readonly Color LifeTimeAccentBrown = Color.FromArgb(168, 96, 36);

    private readonly MainForm _owner;
    private readonly TableLayoutPanel _bodyRegion;
    private readonly Panel _upperHost;
    private readonly RichTextBox _bodyUpper;
    private readonly Panel _descHost;
    private readonly RichTextBox _bodyDesc;
    private readonly PictureBox _icon;
    private readonly TableLayoutPanel _rootTable;
    private readonly Panel _headerTextPanel;
    private readonly Label _titleLabel;
    private readonly Label _subtitleLabel;
    private readonly TableLayoutPanel _footerStrip;
    private readonly Label[] _footerLabels;
    private readonly Panel _editBar;
    private readonly Button _btnConfirm;
    private readonly Button _btnCancel;
    private readonly Font _fontTitle;
    private readonly Font _fontCategory;
    private readonly Font _fontNormal;
    /// <summary>物品属性词条（Status 等）：略小于正文，黑色紧凑排版。</summary>
    private readonly Font _fontAttr;
    /// <summary>装备 Status 黑字词条：比 <see cref="_fontAttr"/> 大一号，并配合 RichEdit 行距收紧。</summary>
    private readonly Font _fontStat;

    private int _iconThumbnailRequestSeq;

    /// <summary>当前展示用的模型；按选行/筛选切换时由 <see cref="ApplyContent"/> 缓存，便于 Pending 状态下重渲染。</summary>
    private ItemPreviewModel? _currentModel;
    /// <summary>当前预览所对应的 <c>_parsedTable</c> 行号；多选模式或不可编辑时为 <c>-1</c>。</summary>
    private int _currentParsedRow = -1;
    /// <summary>是否允许双击数字内联编辑（多选模式或未绑定上下文时禁用）。</summary>
    public bool EditEnabled { get; private set; }

    /// <summary>渲染期间记录每条可编辑词条的数字部分在 <see cref="_bodyUpper"/> 中的绝对字符范围，供 hit-test 与定位 TextBox。</summary>
    private readonly List<LineNumericRange> _lineNumericRanges = new();
    private TextBox? _inlineEdit;
    private int _inlineEditLineIndex = -1;
    private StatusLineMeta? _inlineEditMeta;
    /// <summary>单值待提交状态（数值型 Status）；进入后底部出现「确认/取消」按钮。</summary>
    private (int LineIndex, StatusLineMeta Meta, int NewStored)? _pending;
    /// <summary>字符串字段待提交状态（Name / Desc）；与 <see cref="_pending"/> 互斥。</summary>
    private (int ColIndex, string NewValue)? _stringPending;
    /// <summary>字符串字段内联编辑框（覆盖式 TextBox）；与 <see cref="_inlineEdit"/> 互斥。</summary>
    private TextBox? _stringInlineEdit;
    private int _stringInlineEditColIndex = -1;

    /// <summary>Name 列索引；由 <see cref="BindEditContext"/> 写入，-1 表示不可编辑。</summary>
    private int _nameColumn = -1;
    /// <summary>Desc 列索引；由 <see cref="BindEditContext"/> 写入，-1 表示不可编辑。</summary>
    private int _descColumn = -1;

    /// <summary>待编辑/已提交的词条在预览中的高亮色（与有效时间的橙棕色同色系，便于一眼区分）。</summary>
    private static readonly Color PendingHighlightColor = Color.FromArgb(200, 96, 16);

    private struct LineNumericRange
    {
        public int LineIndex;
        /// <summary>整条词条文本在 <see cref="_bodyUpper"/> 中的起始字符下标（含标签部分），用于"整行可双击"的 hit-test。</summary>
        public int LineStartInBox;
        /// <summary>整条词条文本的字符长度（不含其前后的软换行 <c>\v</c>）。</summary>
        public int LineLength;
        /// <summary>数字子串在 <see cref="_bodyUpper"/> 中的起始下标（仅用于定位内联 <c>TextBox</c>）。</summary>
        public int NumStartInBox;
        /// <summary>数字子串字符长度。</summary>
        public int NumLength;
        public StatusLineMeta Meta;
    }

    private const int EmSetParaFormat = 0x400 + 71;
    private const nint SfSelection = 1;
    private const uint PfmAlignment = 0x08;
    private const uint PfmSpaceBefore = 0x00000040;
    private const uint PfmSpaceAfter = 0x00000080;
    private const uint PfmLineSpacing = 0x00000100;
    private const ushort PfaLeft = 1;
    /// <summary>RichEdit：dyLineSpacing 为相邻两行基线间距（twips），可小于单倍行高。</summary>
    private const byte ParaLineSpacingRuleExactTwips = 4;
    /// <summary>Status 块与上方（等级/可分解/使用类型等）及下方（有效时间等）的段间距，twips。</summary>
    private const int StatBlockExternalMarginTwips = 52;
    /// <summary>物品说明预览里 Status 黑字词条的字号（磅）。</summary>
    private const float StatLinesFontSizePt = 9.2f;
    /// <summary>
    /// Status 词条行距比例分子：实际段内行距（twips）≈ <see cref="EstimateStatLineLineTwips"/> × 本值 ÷ 20（规则 4）。
    /// 20 接近单倍行高；更小更紧凑。须修改此类级常量；若在方法内写同名 <c>int</c> 局部变量则不会生效。
    /// </summary>
    private const int StatLineInternalSpacingDy = 18;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto, Pack = 4)]
    private struct Paraformat
    {
        public int cbSize;
        public uint dwMask;
        public ushort wNumbering;
        public ushort wReserved;
        public int dxStartIndent;
        public int dxRightIndent;
        public int dxOffset;
        public ushort wAlignment;
        public short cTabCount;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public int[] rgxTabs;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto, Pack = 4)]
    private struct Paraformat2
    {
        public int cbSize;
        public uint dwMask;
        public ushort wNumbering;
        public ushort wReserved;
        public int dxStartIndent;
        public int dxRightIndent;
        public int dxOffset;
        public ushort wAlignment;
        public short cTabCount;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public int[] rgxTabs;
        public int dySpaceBefore;
        public int dySpaceAfter;
        public int dyLineSpacing;
        public short sStyle;
        public byte bLineSpacingRule;
        public byte bOutlineLevel;
        public ushort wShadingWeight;
        public ushort wShadingStyle;
        public ushort wNumberingStart;
        public ushort wNumberingStyle;
        public ushort wNumberingTab;
        public ushort wBorderSpace;
        public ushort wBorderWidth;
        public ushort wBorders;
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto, EntryPoint = "SendMessageW")]
    private static extern nint SendMessagePara(nint hWnd, int msg, nint wParam, ref Paraformat lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto, EntryPoint = "SendMessageW")]
    private static extern nint SendMessagePara2(nint hWnd, int msg, nint wParam, ref Paraformat2 lParam);

    public ItemPreviewFloatForm(MainForm owner)
    {
        _owner = owner;
        _fontTitle = new Font("Microsoft YaHei UI", 10.5f, FontStyle.Bold, GraphicsUnit.Point);
        _fontCategory = new Font("Microsoft YaHei UI", 8.5f, FontStyle.Regular, GraphicsUnit.Point);
        _fontNormal = new Font("Microsoft YaHei UI", 9f, FontStyle.Regular, GraphicsUnit.Point);
        _fontAttr = new Font("Microsoft YaHei UI", 9f, FontStyle.Regular, GraphicsUnit.Point);
        _fontStat = new Font("Microsoft YaHei UI", StatLinesFontSizePt, FontStyle.Regular, GraphicsUnit.Point);

        FormBorderStyle = FormBorderStyle.SizableToolWindow;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Text = "物品模拟器";
        MinimumSize = new Size(252, 200);
        ClientSize = new Size(300, 480);
        BackColor = Color.White;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 4,
            Padding = new Padding(8)
        };
        _rootTable = root;
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 44));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48f));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 0f));

        _icon = new PictureBox
        {
            Dock = DockStyle.Top,
            Width = 40,
            Height = 40,
            SizeMode = PictureBoxSizeMode.CenterImage,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.WhiteSmoke,
            Margin = new Padding(0, 0, 4, 0)
        };
        root.Controls.Add(_icon, 0, 0);

        _headerTextPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            Padding = new Padding(0),
            Margin = new Padding(0)
        };
        _titleLabel = new Label
        {
            AutoSize = false,
            ForeColor = Color.DarkRed,
            BackColor = Color.White,
            Font = _fontTitle,
            TextAlign = ContentAlignment.TopLeft,
            Margin = new Padding(0),
            Padding = new Padding(0),
            UseMnemonic = false
        };
        _subtitleLabel = new Label
        {
            AutoSize = false,
            ForeColor = Color.Black,
            BackColor = Color.White,
            Font = _fontCategory,
            TextAlign = ContentAlignment.TopLeft,
            Margin = new Padding(0),
            Padding = new Padding(0),
            UseMnemonic = false
        };
        _headerTextPanel.Controls.Add(_titleLabel);
        _headerTextPanel.Controls.Add(_subtitleLabel);
        root.Controls.Add(_headerTextPanel, 1, 0);
        root.Resize += RootTableOnResize;

        _bodyRegion = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.White,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        _bodyRegion.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        _bodyRegion.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _upperHost = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        _bodyUpper = new RichTextBox
        {
            BorderStyle = BorderStyle.None,
            ReadOnly = true,
            BackColor = Color.White,
            DetectUrls = false,
            ScrollBars = RichTextBoxScrollBars.Vertical,
            WordWrap = true,
            Margin = new Padding(0),
            Padding = new Padding(0),
            Dock = DockStyle.Fill
        };
        _upperHost.Controls.Add(_bodyUpper);
        _bodyRegion.Controls.Add(_upperHost, 0, 0);

        _descHost = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            Margin = new Padding(0),
            Padding = new Padding(0),
            AutoScroll = false
        };
        _bodyDesc = new RichTextBox
        {
            BorderStyle = BorderStyle.None,
            ReadOnly = true,
            BackColor = Color.White,
            DetectUrls = false,
            ScrollBars = RichTextBoxScrollBars.Vertical,
            WordWrap = true,
            Margin = new Padding(0),
            Padding = new Padding(0),
            Dock = DockStyle.Fill,
            Visible = false
        };
        _descHost.Controls.Add(_bodyDesc);
        _bodyRegion.Controls.Add(_descHost, 0, 1);

        _bodyRegion.Resize += (_, _) => ReflowBodyLayout();
        _upperHost.Resize += (_, _) => ReflowBodyLayout();

        root.SetColumnSpan(_bodyRegion, 2);
        root.Controls.Add(_bodyRegion, 0, 1);

        _footerStrip = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 1,
            BackColor = Color.White,
            Padding = new Padding(0, 4, 0, 0)
        };
        for (var i = 0; i < 4; i++)
        {
            _footerStrip.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
        }

        _footerStrip.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        _footerLabels = new Label[4];
        for (var i = 0; i < 4; i++)
        {
            _footerLabels[i] = new Label
            {
                Text = ItemPreviewColumnResolver.FooterFourLabels[i],
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Fill,
                AutoSize = false,
                ForeColor = FooterValueOffGray,
                BackColor = Color.White,
                Font = _fontNormal
            };
            _footerStrip.Controls.Add(_footerLabels[i], i, 0);
        }

        root.Controls.Add(_footerStrip, 0, 2);
        root.SetColumnSpan(_footerStrip, 2);

        _editBar = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            Margin = new Padding(0),
            Padding = new Padding(0, 4, 0, 0),
            Visible = false
        };
        _btnCancel = new Button
        {
            Text = "取消",
            AutoSize = false,
            Width = 72,
            Height = 26,
            Anchor = AnchorStyles.Right | AnchorStyles.Top,
            Margin = new Padding(0),
            UseVisualStyleBackColor = true
        };
        _btnConfirm = new Button
        {
            Text = "确认",
            AutoSize = false,
            Width = 72,
            Height = 26,
            Anchor = AnchorStyles.Right | AnchorStyles.Top,
            Margin = new Padding(0, 0, 6, 0),
            UseVisualStyleBackColor = true
        };
        _btnConfirm.Click += (_, _) => CommitPendingOrString();
        _btnCancel.Click += (_, _) => ClearPendingOrStringAndRefresh();
        _editBar.SizeChanged += (_, _) => LayoutEditBarButtons();
        _editBar.Controls.Add(_btnCancel);
        _editBar.Controls.Add(_btnConfirm);
        root.Controls.Add(_editBar, 0, 3);
        root.SetColumnSpan(_editBar, 2);

        _bodyUpper.MouseDoubleClick += BodyUpperMouseDoubleClick;
        _bodyUpper.VScroll += BodyUpperScrollOrResize;
        _bodyUpper.HScroll += BodyUpperScrollOrResize;
        _bodyUpper.SizeChanged += BodyUpperScrollOrResize;

        _titleLabel.DoubleClick += TitleLabelOnDoubleClick;
        _bodyDesc.DoubleClick += BodyDescOnDoubleClick;

        Controls.Add(root);
    }

    private void LayoutEditBarButtons()
    {
        if (_editBar is null || _btnConfirm is null || _btnCancel is null)
        {
            return;
        }

        var w = Math.Max(1, _editBar.ClientSize.Width);
        var y = 4;
        _btnCancel.SetBounds(w - _btnCancel.Width, y, _btnCancel.Width, _btnCancel.Height);
        _btnConfirm.SetBounds(w - _btnCancel.Width - _btnConfirm.Width - 6, y, _btnConfirm.Width, _btnConfirm.Height);
    }

    private void RootTableOnResize(object? sender, EventArgs e) => RelayoutPreviewHeader();

    private void RelayoutPreviewHeader()
    {
        if (!IsHandleCreated || _rootTable.IsDisposed || !_headerTextPanel.Visible)
        {
            return;
        }

        var wrapW = Math.Max(1, _headerTextPanel.ClientSize.Width);
        const TextFormatFlags tf = TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPadding;
        var y = 0;
        if (!string.IsNullOrEmpty(_titleLabel.Text))
        {
            var titleH = TextRenderer.MeasureText(_titleLabel.Text, _titleLabel.Font, new Size(wrapW, int.MaxValue), tf).Height;
            _titleLabel.SetBounds(0, y, wrapW, Math.Max(titleH, 1));
            _titleLabel.Visible = true;
            y += _titleLabel.Height;
        }
        else
        {
            _titleLabel.Visible = false;
        }

        const int gap = 2;
        if (_subtitleLabel.Visible && !string.IsNullOrEmpty(_subtitleLabel.Text))
        {
            y += gap;
            var subH = TextRenderer.MeasureText(_subtitleLabel.Text, _subtitleLabel.Font, new Size(wrapW, int.MaxValue), tf).Height;
            _subtitleLabel.SetBounds(0, y, wrapW, Math.Max(subH, 1));
            y += _subtitleLabel.Height;
        }

        var rowH = Math.Max(40f, y);
        if (Math.Abs(_rootTable.RowStyles[0].Height - rowH) > 0.5f)
        {
            _rootTable.RowStyles[0] = new RowStyle(SizeType.Absolute, rowH);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _rootTable.Resize -= RootTableOnResize;
            _fontTitle.Dispose();
            _fontCategory.Dispose();
            _fontNormal.Dispose();
            _fontAttr.Dispose();
            _fontStat.Dispose();
        }

        base.Dispose(disposing);
    }

    public void SyncBoundsNearOwner()
    {
        if (_owner.IsDisposed || !IsHandleCreated)
        {
            return;
        }

        var wa = Screen.FromControl(_owner).WorkingArea;
        var w = Math.Min(Math.Max(Width, MinimumSize.Width), 400);
        var h = Math.Min(Math.Max(Height, MinimumSize.Height), wa.Height - 8);
        var x = _owner.Bounds.Right;
        var y = _owner.Bounds.Top;
        if (x + w > wa.Right)
        {
            x = _owner.Bounds.Left - w;
        }

        if (x < wa.Left)
        {
            x = wa.Left;
        }

        if (y + h > wa.Bottom)
        {
            y = wa.Bottom - h;
        }

        if (y < wa.Top)
        {
            y = wa.Top;
        }

        Bounds = new Rectangle(x, y, w, h);
    }

    public void ClearIcon()
    {
        var old = _icon.Image;
        _icon.Image = null;
        old?.Dispose();
    }

    public void RequestIconThumbnail(string spfPath, int iconId, int iconIndexOneBased)
    {
        var seq = Interlocked.Increment(ref _iconThumbnailRequestSeq);
        _ = Task.Run(() =>
        {
            Image? img = null;
            try
            {
                img = ItemIconPreviewLoader.TryLoadIconCell(spfPath, iconId, iconIndexOneBased);
            }
            catch
            {
                img?.Dispose();
                return;
            }

            try
            {
                if (IsDisposed)
                {
                    img?.Dispose();
                    return;
                }

                BeginInvoke(() =>
                {
                    try
                    {
                        if (IsDisposed || seq != _iconThumbnailRequestSeq)
                        {
                            img?.Dispose();
                            return;
                        }

                        if (img is null)
                        {
                            ClearIcon();
                            return;
                        }

                        ClearIcon();
                        _icon.Image = img;
                    }
                    catch
                    {
                        img?.Dispose();
                    }
                });
            }
            catch
            {
                img?.Dispose();
            }
        });
    }

    public void ApplyContent(ItemPreviewModel model)
    {
        DiscardPendingAndInlineSilently();
        _currentModel = model;
        ApplyContentCore(model);
    }

    private void ApplyContentCore(ItemPreviewModel model)
    {
        _bodyUpper.Clear();
        _bodyDesc.Clear();
        _lineNumericRanges.Clear();
        if (model.BlackLines.Count > 0)
        {
            _titleLabel.Text = "";
            _subtitleLabel.Text = "";
            _headerTextPanel.Visible = false;
            _icon.Visible = false;
            _rootTable.RowStyles[0] = new RowStyle(SizeType.Absolute, 0f);
            _bodyRegion.RowStyles[1] = new RowStyle(SizeType.Absolute, 0f);
            _descHost.Visible = false;
            foreach (var line in model.BlackLines)
            {
                AppendSegment(_bodyUpper, Color.DimGray, _fontNormal, line + UpperContentNewLine);
            }

            ApplyBodyParagraphLayout();
            ReflowBodyLayout();
            return;
        }

        _headerTextPanel.Visible = true;
        _icon.Visible = true;
        _titleLabel.ForeColor = model.TitleColor;
        _titleLabel.Text = model.Title;
        _subtitleLabel.Text = model.Subtitle;
        _subtitleLabel.Visible = model.Subtitle.Length > 0;
        RelayoutPreviewHeader();

        if (model.LevelLine.Length > 0)
        {
            AppendSegment(_bodyUpper, Color.DodgerBlue, _fontNormal, model.LevelLine + UpperContentNewLine);
        }

        if (model.DecomposeLine.Length > 0)
        {
            AppendSegment(_bodyUpper, Color.DodgerBlue, _fontAttr, model.DecomposeLine + UpperContentNewLine);
        }

        if (model.UseTypeHeader.Length > 0)
        {
            AppendSegment(_bodyUpper, UseTypeDeepGreen, _fontAttr, model.UseTypeHeader + UpperContentNewLine);
        }

        var statParagraphs = new List<(int Start, int Length)>();
        AppendStatLinesAsSingleParagraph(model.GreenStatLines, model.StatusLineMetas, statParagraphs);

        if (model.LifeTimeLine.Length > 0)
        {
            AppendSegment(_bodyUpper, LifeTimeAccentBrown, _fontAttr, model.LifeTimeLine + UpperContentNewLine);
        }

        var hasDesc = model.LoreTitle.Length > 0 || model.LoreBody.Length > 0;
        _bodyDesc.Visible = hasDesc;
        _descHost.Visible = hasDesc;
        _bodyRegion.RowStyles[1] = hasDesc
            ? new RowStyle(SizeType.Absolute, 1f)
            : new RowStyle(SizeType.Absolute, 0f);
        if (hasDesc)
        {
            if (model.LoreTitle.Length > 0)
            {
                AppendSegment(_bodyDesc, Color.Black, _fontNormal, model.LoreTitle + Environment.NewLine);
            }

            if (model.LoreBody.Length > 0)
            {
                AppendSegment(_bodyDesc, Color.Black, _fontNormal, model.LoreBody + Environment.NewLine);
            }
        }

        ApplyBodyParagraphLayout();
        if (statParagraphs.Count > 0)
        {
            ApplyStatLinesParagraphLayout(_bodyUpper, _fontStat, statParagraphs);
        }

        ReflowBodyLayout();
        ApplyFooterRow(model);
    }

    private void ApplyFooterRow(ItemPreviewModel model)
    {
        if (model.BlackLines.Count > 0)
        {
            _footerStrip.Visible = false;
            return;
        }

        _footerStrip.Visible = true;
        for (var i = 0; i < 4; i++)
        {
            var on = i < model.FooterTags.Count && model.FooterTags[i].On;
            var text = i < model.FooterTags.Count ? model.FooterTags[i].Label : ItemPreviewColumnResolver.FooterFourLabels[i];
            _footerLabels[i].Text = text;
            _footerLabels[i].ForeColor = on ? FooterValueOnGreen : FooterValueOffGray;
        }
    }

    private static void AppendSegment(RichTextBox box, Color color, Font font, string text)
    {
        box.SelectionStart = box.TextLength;
        box.SelectionLength = 0;
        box.SelectionColor = color;
        box.SelectionFont = font;
        box.AppendText(text);
    }

    /// <summary>
    /// 用 RichEdit 软换行（<see cref="StatLinesSoftBreak"/>）把多条 Status 放进**同一段**，末尾再 <see cref="UpperContentNewLine"/>。
    /// 使用 <see cref="RichTextBox.SelectedText"/> 插入，避免 <see cref="RichTextBox.AppendText"/> 把 <c>\v</c> 变成硬分段导致行距格式无效。
    /// 同时把每条可编辑词条的数字部分在 <see cref="_bodyUpper"/> 中的绝对字符范围登记到 <see cref="_lineNumericRanges"/>；
    /// 当存在 <see cref="_pending"/> 时，把对应行替换为待提交数值显示并整行染色 <see cref="PendingHighlightColor"/>，且该行不登记可点击范围。
    /// </summary>
    private void AppendStatLinesAsSingleParagraph(
        IReadOnlyList<string> lines,
        IReadOnlyList<StatusLineMeta> metas,
        List<(int Start, int Length)> outRanges)
    {
        var paraStart = -1;
        var any = false;
        for (var i = 0; i < lines.Count; i++)
        {
            var meta = i < metas.Count ? metas[i] : StatusLineMeta.NotEditable;

            string displayLine;
            var isPendingLine = _pending is { } p && p.LineIndex == i;
            if (isPendingLine)
            {
                var (newLine, _, _, _, _) = ItemStatusTypeFormat.FormatLineWithRange(
                    _pending!.Value.Meta.TypeId,
                    _pending!.Value.NewStored);
                displayLine = newLine;
            }
            else
            {
                displayLine = lines[i];
            }

            if (displayLine.Length == 0)
            {
                continue;
            }

            if (!any)
            {
                paraStart = _bodyUpper.TextLength;
            }

            var color = isPendingLine ? PendingHighlightColor : Color.Black;

            _bodyUpper.SelectionStart = _bodyUpper.TextLength;
            _bodyUpper.SelectionLength = 0;
            _bodyUpper.SelectionColor = color;
            _bodyUpper.SelectionFont = _fontStat;
            if (any)
            {
                _bodyUpper.SelectedText = new string(StatLinesSoftBreak, 1);
            }

            _bodyUpper.SelectionColor = color;
            _bodyUpper.SelectionFont = _fontStat;
            var lineStartInBox = _bodyUpper.TextLength;
            _bodyUpper.SelectedText = displayLine;
            any = true;

            if (meta.Editable && !isPendingLine && meta.NumLength > 0)
            {
                _lineNumericRanges.Add(new LineNumericRange
                {
                    LineIndex = i,
                    LineStartInBox = lineStartInBox,
                    LineLength = displayLine.Length,
                    NumStartInBox = lineStartInBox + meta.NumStartInLine,
                    NumLength = meta.NumLength,
                    Meta = meta
                });
            }
        }

        if (!any || paraStart < 0)
        {
            return;
        }

        _bodyUpper.SelectionStart = _bodyUpper.TextLength;
        _bodyUpper.SelectionLength = 0;
        _bodyUpper.SelectionColor = Color.Black;
        _bodyUpper.SelectionFont = _fontStat;
        _bodyUpper.SelectedText = UpperContentNewLine;
        outRanges.Add((paraStart, _bodyUpper.TextLength - paraStart));
    }

    /// <summary>按当前字体估计单行显示高度并换算为 twips（供规则 4 行距）。</summary>
    private static int EstimateStatLineLineTwips(RichTextBox box, Font font)
    {
        const TextFormatFlags tf = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.TextBoxControl;
        var text = "中Ag1pq";
        var hPx = TextRenderer.MeasureText(text, font, Size.Empty, tf).Height;
        using var g = box.CreateGraphics();
        var twips = (int)Math.Round(hPx * 1440.0 / g.DpiY);
        return Math.Clamp(twips, 80, 720);
    }

    private void ApplyBodyParagraphLayout()
    {
        if (!IsHandleCreated)
        {
            return;
        }

        ApplyBodyParagraphLayout(_bodyUpper);
        if (_bodyDesc.Visible)
        {
            ApplyBodyParagraphLayout(_bodyDesc);
        }
    }

    private static void ApplyBodyParagraphLayout(RichTextBox box)
    {
        if (box.TextLength == 0 || !box.IsHandleCreated)
        {
            return;
        }

        var selStart = box.SelectionStart;
        var selLen = box.SelectionLength;
        box.SelectAll();
        var pf = new Paraformat
        {
            cbSize = Marshal.SizeOf<Paraformat>(),
            dwMask = PfmAlignment,
            wAlignment = PfaLeft,
            cTabCount = 0,
            rgxTabs = new int[32]
        };
        _ = SendMessagePara(box.Handle, EmSetParaFormat, SfSelection, ref pf);
        box.SelectionStart = Math.Min(selStart, box.TextLength);
        box.SelectionLength = selLen;
    }

    /// <summary>
    /// Status 整块（通常单段 + 软换行）：段内行距（规则 4 twips）；段前/段后与上下正文拉开。
    /// </summary>
    private static void ApplyStatLinesParagraphLayout(RichTextBox box, Font statFont, List<(int Start, int Length)> paragraphs)
    {
        if (paragraphs.Count == 0 || !box.IsHandleCreated)
        {
            return;
        }

        var baseTwips = EstimateStatLineLineTwips(box, statFont);
        var lineTwips = Math.Clamp(
            (int)Math.Round(baseTwips * (double)StatLineInternalSpacingDy / 20.0),
            40,
            2400);

        var selStart = box.SelectionStart;
        var selLen = box.SelectionLength;
        var last = paragraphs.Count - 1;
        for (var i = 0; i < paragraphs.Count; i++)
        {
            var (start, length) = paragraphs[i];
            var safeStart = Math.Clamp(start, 0, box.TextLength);
            var safeLen = Math.Clamp(length, 0, box.TextLength - safeStart);
            if (safeLen <= 0)
            {
                continue;
            }

            box.Select(safeStart, safeLen);
            var before = i == 0 ? StatBlockExternalMarginTwips : 0;
            var after = i == last ? StatBlockExternalMarginTwips : 0;
            var pf2 = new Paraformat2
            {
                cbSize = Marshal.SizeOf<Paraformat2>(),
                dwMask = PfmAlignment | PfmSpaceBefore | PfmSpaceAfter | PfmLineSpacing,
                wNumbering = 0,
                wReserved = 0,
                dxStartIndent = 0,
                dxRightIndent = 0,
                dxOffset = 0,
                wAlignment = PfaLeft,
                cTabCount = 0,
                rgxTabs = new int[32],
                dySpaceBefore = before,
                dySpaceAfter = after,
                dyLineSpacing = lineTwips,
                sStyle = 0,
                bLineSpacingRule = ParaLineSpacingRuleExactTwips,
                bOutlineLevel = 0,
                wShadingWeight = 0,
                wShadingStyle = 0,
                wNumberingStart = 0,
                wNumberingStyle = 0,
                wNumberingTab = 0,
                wBorderSpace = 0,
                wBorderWidth = 0,
                wBorders = 0
            };
            _ = SendMessagePara2(box.Handle, EmSetParaFormat, SfSelection, ref pf2);
        }

        box.SelectionStart = Math.Min(selStart, box.TextLength);
        box.SelectionLength = selLen;
    }

    private void ReflowBodyLayout()
    {
        if (!IsHandleCreated)
        {
            return;
        }

        if (!_bodyDesc.Visible)
        {
            return;
        }

        var wD = Math.Max(1, _bodyRegion.ClientSize.Width);
        _bodyDesc.Width = wD;
        var prefH = Math.Max(_bodyDesc.GetPreferredSize(new Size(wD, int.MaxValue)).Height, 1);
        var regionH = Math.Max(1, _bodyRegion.ClientSize.Height);
        var maxDesc = Math.Max(96, (int)(regionH * 0.42f));
        var rowH = Math.Min(prefH, maxDesc);
        _bodyRegion.RowStyles[1] = new RowStyle(SizeType.Absolute, rowH);
        _bodyRegion.PerformLayout();
    }

    /// <summary>
    /// 由 <see cref="MainForm.UpdateItemDescriptionPreview"/> 在 <see cref="ApplyContent"/> 前调用：
    /// 绑定当前预览所对应的 <c>_parsedTable</c> 行号，并据多选状态启用/禁用内联编辑。
    /// <paramref name="nameCol"/> / <paramref name="descCol"/> 为 -1 时对应字段不可编辑。
    /// </summary>
    public void BindEditContext(int parsedRow, bool multiSelectFirstRow, int nameCol = -1, int descCol = -1)
    {
        _currentParsedRow = parsedRow;
        EditEnabled = !multiSelectFirstRow && parsedRow >= 0;
        _nameColumn = EditEnabled ? nameCol : -1;
        _descColumn = EditEnabled ? descCol : -1;
    }

    private void BodyUpperMouseDoubleClick(object? sender, MouseEventArgs e)
    {
        if (!EditEnabled || _pending is not null || _inlineEdit is not null)
        {
            return;
        }

        if (_currentParsedRow < 0)
        {
            return;
        }

        var ci = _bodyUpper.GetCharIndexFromPosition(e.Location);
        for (var i = 0; i < _lineNumericRanges.Count; i++)
        {
            var r = _lineNumericRanges[i];
            if (ci >= r.LineStartInBox && ci < r.LineStartInBox + r.LineLength)
            {
                TryStartInlineEdit(r);
                return;
            }
        }
    }

    private void BodyUpperScrollOrResize(object? sender, EventArgs e)
    {
        if (_inlineEdit is not null)
        {
            CancelInlineEdit();
        }
    }

    /// <summary>
    /// 在 <paramref name="range"/> 对应的数字子串上方放置一个 <see cref="TextBox"/>：用 <c>_fontStat</c> 测出数字宽度并外扩 8px；
    /// 失焦/Esc 取消；Enter 走 <see cref="TryEnterPendingFromInline"/>（解析失败留在原地、闪红背景）。
    /// </summary>
    private void TryStartInlineEdit(LineNumericRange range)
    {
        var p1 = _bodyUpper.GetPositionFromCharIndex(range.NumStartInBox);
        var meta = range.Meta;
        var currentNumberText = meta.IsPermilleStored
            ? ItemStatusTypeCatalog.FormatStoredPermilleAsPercentNumber(meta.StoredValue)
            : meta.StoredValue.ToString(CultureInfo.InvariantCulture);
        var numWidth = TextRenderer.MeasureText(currentNumberText, _fontStat, Size.Empty, TextFormatFlags.NoPadding).Width;
        var boxWidth = Math.Max(36, numWidth + 24);
        var boxHeight = _fontStat.Height + 4;

        var tb = new TextBox
        {
            Font = _fontStat,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.White,
            ForeColor = PendingHighlightColor,
            Text = currentNumberText
        };
        var offsetX = _bodyUpper.Location.X + p1.X - 2;
        var offsetY = _bodyUpper.Location.Y + p1.Y - 1;
        tb.SetBounds(offsetX, offsetY, boxWidth, boxHeight);
        tb.KeyDown += InlineEditOnKeyDown;
        tb.LostFocus += InlineEditOnLostFocus;
        _upperHost.Controls.Add(tb);
        tb.BringToFront();
        _inlineEdit = tb;
        _inlineEditLineIndex = range.LineIndex;
        _inlineEditMeta = meta;
        tb.SelectAll();
        tb.Focus();
    }

    private void InlineEditOnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            e.SuppressKeyPress = true;
            e.Handled = true;
            TryEnterPendingFromInline();
        }
        else if (e.KeyCode == Keys.Escape)
        {
            e.SuppressKeyPress = true;
            e.Handled = true;
            CancelInlineEdit();
        }
    }

    private void InlineEditOnLostFocus(object? sender, EventArgs e)
    {
        if (_inlineEdit is null || _pending is not null)
        {
            return;
        }

        TryEnterPendingFromInline();
    }

    private void CancelInlineEdit()
    {
        if (_inlineEdit is null)
        {
            return;
        }

        DisposeInlineEditControl();
    }

    private void DisposeInlineEditControl()
    {
        var tb = _inlineEdit;
        if (tb is null)
        {
            _inlineEditLineIndex = -1;
            _inlineEditMeta = null;
            return;
        }

        tb.KeyDown -= InlineEditOnKeyDown;
        tb.LostFocus -= InlineEditOnLostFocus;
        if (_upperHost.Controls.Contains(tb))
        {
            _upperHost.Controls.Remove(tb);
        }

        tb.Dispose();
        _inlineEdit = null;
        _inlineEditLineIndex = -1;
        _inlineEditMeta = null;
    }

    /// <summary>
    /// 把内联输入框的文本解析回 stored int（千分类×10、其它直接整数），失败则保留焦点并闪红；成功则进入 <see cref="BeginPending"/>。
    /// </summary>
    private void TryEnterPendingFromInline()
    {
        if (_inlineEdit is null || _inlineEditMeta is null)
        {
            return;
        }

        var raw = _inlineEdit.Text?.Trim() ?? "";
        var meta = _inlineEditMeta;
        int newStored;
        if (meta.IsPermilleStored)
        {
            if (!decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var dec))
            {
                FlashInlineInvalid();
                return;
            }

            var scaled = Math.Round(dec * 10m, MidpointRounding.AwayFromZero);
            if (scaled < int.MinValue || scaled > int.MaxValue)
            {
                FlashInlineInvalid();
                return;
            }

            newStored = (int)scaled;
        }
        else
        {
            if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var iv))
            {
                FlashInlineInvalid();
                return;
            }

            newStored = iv;
        }

        var line = _inlineEditLineIndex;
        if (newStored == meta.StoredValue)
        {
            DisposeInlineEditControl();
            return;
        }

        BeginPending(line, meta, newStored);
    }

    private void FlashInlineInvalid()
    {
        if (_inlineEdit is null)
        {
            return;
        }

        _inlineEdit.BackColor = Color.MistyRose;
        _inlineEdit.SelectAll();
        _inlineEdit.Focus();
    }

    private void BeginPending(int lineIndex, StatusLineMeta meta, int newStored)
    {
        DisposeInlineEditControl();
        _pending = (lineIndex, meta, newStored);
        ShowEditBar(true);
        AcceptButton = _btnConfirm;
        CancelButton = _btnCancel;
        if (_currentModel is not null)
        {
            ApplyContentCore(_currentModel);
        }

        if (_btnConfirm.CanFocus)
        {
            _btnConfirm.Focus();
        }
    }

    private void CommitPendingOrString()
    {
        if (_stringPending is not null)
        {
            CommitStringPending();
            return;
        }

        CommitPending();
    }

    private void ClearPendingOrStringAndRefresh()
    {
        if (_stringPending is not null)
        {
            ClearStringPending();
            return;
        }

        ClearPendingAndRefresh();
    }

    private void CommitPending()
    {
        if (_pending is not { } pen || _currentParsedRow < 0)
        {
            ClearPendingAndRefresh();
            return;
        }

        var ok = _owner.CommitCellFromPreview(_currentParsedRow, pen.Meta.ValueCol, pen.NewStored);
        _pending = null;
        ShowEditBar(false);
        AcceptButton = null;
        CancelButton = null;

        if (!ok)
        {
            if (_currentModel is not null)
            {
                ApplyContentCore(_currentModel);
            }
        }
    }

    private void ClearPendingAndRefresh()
    {
        _pending = null;
        ShowEditBar(false);
        AcceptButton = null;
        CancelButton = null;
        if (_currentModel is not null)
        {
            ApplyContentCore(_currentModel);
        }
    }

    private void TitleLabelOnDoubleClick(object? sender, EventArgs e)
    {
        if (!EditEnabled || _pending is not null || _stringPending is not null
            || _inlineEdit is not null || _stringInlineEdit is not null)
        {
            return;
        }

        if (_nameColumn < 0 || _currentParsedRow < 0)
        {
            return;
        }

        var currentText = _titleLabel.Text;
        TryStartStringInlineEdit(
            _nameColumn,
            _headerTextPanel,
            _titleLabel.Bounds,
            currentText,
            singleLine: true);
    }

    private void BodyDescOnDoubleClick(object? sender, EventArgs e)
    {
        if (!EditEnabled || _pending is not null || _stringPending is not null
            || _inlineEdit is not null || _stringInlineEdit is not null)
        {
            return;
        }

        if (_descColumn < 0 || _currentParsedRow < 0 || _currentModel is null)
        {
            return;
        }

        // 从模型重建显示文本（LoreTitle 是首行，LoreBody 是其余行）。
        // 存储格式为 "\\n" 作为换行符，这里已经是解码后的可见文本。
        var displayText = _currentModel.LoreTitle.Length > 0
            ? _currentModel.LoreTitle + "\n" + _currentModel.LoreBody
            : _currentModel.LoreBody;

        TryStartStringInlineEdit(
            _descColumn,
            _descHost,
            new System.Drawing.Rectangle(0, 0, _descHost.ClientSize.Width, _descHost.ClientSize.Height),
            displayText,
            singleLine: false);
    }

    private void TryStartStringInlineEdit(
        int colIndex,
        Panel host,
        System.Drawing.Rectangle bounds,
        string currentText,
        bool singleLine)
    {
        var font = singleLine ? _fontTitle : _fontNormal;
        var tb = new TextBox
        {
            Multiline = !singleLine,
            Font = font,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.White,
            ForeColor = PendingHighlightColor,
            Text = currentText,
            WordWrap = true,
            ScrollBars = singleLine ? ScrollBars.None : ScrollBars.Vertical
        };
        tb.SetBounds(bounds.X, bounds.Y, Math.Max(40, bounds.Width), Math.Max(20, bounds.Height));
        tb.KeyDown += StringInlineEditOnKeyDown;
        tb.LostFocus += StringInlineEditOnLostFocus;
        host.Controls.Add(tb);
        tb.BringToFront();
        _stringInlineEdit = tb;
        _stringInlineEditColIndex = colIndex;
        tb.SelectAll();
        tb.Focus();
    }

    private void StringInlineEditOnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_stringInlineEdit is null)
        {
            return;
        }

        var isMultiline = _stringInlineEdit.Multiline;
        if (!isMultiline && e.KeyCode == Keys.Enter)
        {
            e.SuppressKeyPress = true;
            e.Handled = true;
            TryEnterStringPendingFromInline();
        }
        else if (isMultiline && e.KeyCode == Keys.Enter && e.Control)
        {
            e.SuppressKeyPress = true;
            e.Handled = true;
            TryEnterStringPendingFromInline();
        }
        else if (e.KeyCode == Keys.Escape)
        {
            e.SuppressKeyPress = true;
            e.Handled = true;
            CancelStringInlineEdit();
        }
    }

    private void StringInlineEditOnLostFocus(object? sender, EventArgs e)
    {
        if (_stringInlineEdit is null || _stringPending is not null)
        {
            return;
        }

        TryEnterStringPendingFromInline();
    }

    private void TryEnterStringPendingFromInline()
    {
        if (_stringInlineEdit is null)
        {
            return;
        }

        var newValue = _stringInlineEdit.Text;
        var colIndex = _stringInlineEditColIndex;
        DisposeStringInlineEditControl();
        BeginStringPending(colIndex, newValue);
    }

    private void CancelStringInlineEdit()
    {
        DisposeStringInlineEditControl();
    }

    private void DisposeStringInlineEditControl()
    {
        var tb = _stringInlineEdit;
        if (tb is null)
        {
            _stringInlineEditColIndex = -1;
            return;
        }

        tb.KeyDown -= StringInlineEditOnKeyDown;
        tb.LostFocus -= StringInlineEditOnLostFocus;
        var parent = tb.Parent;
        if (parent is not null && parent.Controls.Contains(tb))
        {
            parent.Controls.Remove(tb);
        }

        tb.Dispose();
        _stringInlineEdit = null;
        _stringInlineEditColIndex = -1;
    }

    private void BeginStringPending(int colIndex, string newValue)
    {
        _stringPending = (colIndex, newValue);
        ShowEditBar(true);
        AcceptButton = _btnConfirm;
        CancelButton = _btnCancel;
        if (_btnConfirm.CanFocus)
        {
            _btnConfirm.Focus();
        }
    }

    private void CommitStringPending()
    {
        if (_stringPending is not { } sp || _currentParsedRow < 0)
        {
            ClearStringPending();
            return;
        }

        _stringPending = null;
        ShowEditBar(false);
        AcceptButton = null;
        CancelButton = null;

        // desc 需要把显示用的 \n 换行重新编码为存储格式 \\n
        var storedValue = sp.ColIndex == _descColumn
            ? sp.NewValue.Replace("\n", "\\n", StringComparison.Ordinal)
            : sp.NewValue;

        _owner.CommitStringCellFromPreview(_currentParsedRow, sp.ColIndex, storedValue);
    }

    private void ClearStringPending()
    {
        _stringPending = null;
        ShowEditBar(false);
        AcceptButton = null;
        CancelButton = null;
        if (_currentModel is not null)
        {
            ApplyContentCore(_currentModel);
        }
    }

    /// <summary>
    /// 选行/筛选切换前由 <see cref="ApplyContent"/> 调用：静默丢弃任何 InlineEdit/Pending 状态，避免状态串行。
    /// </summary>
    private void DiscardPendingAndInlineSilently()
    {
        DisposeInlineEditControl();
        DisposeStringInlineEditControl();
        if (_pending is not null)
        {
            _pending = null;
            ShowEditBar(false);
            AcceptButton = null;
            CancelButton = null;
        }

        if (_stringPending is not null)
        {
            _stringPending = null;
            ShowEditBar(false);
            AcceptButton = null;
            CancelButton = null;
        }
    }

    private void ShowEditBar(bool show)
    {
        _editBar.Visible = show;
        _rootTable.RowStyles[3] = new RowStyle(SizeType.Absolute, show ? 34f : 0f);
        if (show)
        {
            LayoutEditBarButtons();
        }
    }
}

internal static class ItemPreviewColumnResolver
{
    public static readonly string[] FooterFourLabels = ["交易", "破坏", "仓库", "贩卖"];

    private static readonly string[] NameHeaders =
    [
        "ITEM_NAME", "NAME", "物品名", "物品名称", "道具名", "名称"
    ];

    private static readonly string[] UseTypeHeaders =
    [
        "USE_TYPE", "ITEM_USE", "使用类型", "物品大类", "装备类型"
    ];

    private static readonly string[] SubtitleHeaders =
    [
        "SUB_NAME", "SECOND_NAME", "子标题", "物品子类名", "分类说明"
    ];

    private static readonly string[] ItemWearLevelHeaders =
    [
        "_ItemLvValue", "ITEM_LV_VALUE", "ItemLvValue", "OWN_LEVEL", "HAVE_LEVEL",
        "可佩戴等级", "佩戴可能等级", "可拥有等级", "需求等级值"
    ];

    private static readonly string[] IdHeaders =
    [
        "ROW_ID", "ROWID", "ITEM_ID", "ITEMID", "道具ID", "物品ID"
    ];

    public static bool TryResolveDescriptionColumn(ParsedLdtTable table, out int columnIndex, out string pickedName)
    {
        columnIndex = -1;
        pickedName = "";
        ReadOnlySpan<string> exact =
        [
            "物品说明", "ITEM_DESC", "ITEMDESC", "ITEM_DESCRIPTION", "DESCRIPTION", "DESC"
        ];

        foreach (var want in exact)
        {
            for (var c = 1; c < table.ColumnNames.Length; c++)
            {
                if (!string.Equals(table.ColumnNames[c]?.Trim(), want, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (table.ColumnTypes[c] == 1)
                {
                    columnIndex = c;
                    pickedName = table.ColumnNames[c] ?? want;
                    return true;
                }
            }
        }

        for (var c = 1; c < table.ColumnNames.Length; c++)
        {
            if (table.ColumnTypes[c] != 1)
            {
                continue;
            }

            var n = table.ColumnNames[c] ?? "";
            var l = n.ToLowerInvariant();
            if (n.Contains("说明", StringComparison.Ordinal)
                || n.Contains("描述", StringComparison.Ordinal)
                || l.Contains("desc")
                || l.Contains("memo")
                || l.Contains("comment"))
            {
                columnIndex = c;
                pickedName = n;
                return true;
            }
        }

        return false;
    }

    public static ItemPreviewColumnMap Build(ParsedLdtTable table, int descColumnIndex)
    {
        var m = new ItemPreviewColumnMap { DescColumn = descColumnIndex };
        m.NameColumn = TryFirstStringColumn(table, NameHeaders);
        m.TitleRedColumn = TryFirstIntColumn(table, ["RED", "NAME_RED", "ITEM_NAME_RED", "TITLE_RED"]);
        m.TitleGreenColumn = TryFirstIntColumn(table, ["GREEN", "NAME_GREEN", "ITEM_NAME_GREEN", "TITLE_GREEN"]);
        m.TitleBlueColumn = TryFirstIntColumn(table, ["BLUE", "NAME_BLUE", "ITEM_NAME_BLUE", "TITLE_BLUE"]);
        m.UseTypeColumn = TryFirstStringColumn(table, UseTypeHeaders);
        m.BreakupColumn = TryFirstColumnByHeaderKeys(table, ["BREAKUP", "Breakup", "_Breakup"]);
        m.SubtitleTextColumn = TryFirstStringColumn(table, SubtitleHeaders);
        m.LevelOwnColumn = TryFirstIntColumn(table, ItemWearLevelHeaders);
        m.ItemIdColumn = TryFirstIntColumn(table, IdHeaders);
        m.InventoryTypeColumn = TryExactIntColumn(table, "_Type");
        m.InventorySubTypeColumn = TryExactIntColumn(table, "_SubType");
        m.PosId1Column = TryFirstColumnByHeaderKeysAny(table, ["_PosID1", "PosID1"]);
        m.LifetimeColumn = TryExactIntColumn(table, "_Lifetime");
        if (m.LifetimeColumn < 0)
        {
            m.LifetimeColumn = TryFirstIntColumn(table, ["Lifetime", "ITEM_LIFETIME", "LIFETIME"]);
        }

        m.SubTypeNumberColumn = TryFirstIntColumn(table, ["SUB_TYPE", "SUBTYPE", "ITEM_SUB", "子类", "子类型号", "SPECIAL_NO", "CARD_NO"]);

        DiscoverStatusPairs(table, m.StatusPairs);
        DiscoverFooterFour(table, m.FooterSlotColumns);
        return m;
    }

    private static string HeaderNorm(string? name) =>
        (name ?? "").Trim().TrimStart('_').ToUpperInvariant().Replace("_", "", StringComparison.Ordinal);

    internal static bool HeaderMatch(string? columnName, string headerWant) =>
        string.Equals(HeaderNorm(columnName), HeaderNorm(headerWant), StringComparison.Ordinal);

    private static int TryFirstColumnByHeaderKeysAny(ParsedLdtTable table, string[] keys)
    {
        foreach (var key in keys)
        {
            for (var c = 0; c < table.ColumnNames.Length; c++)
            {
                if (HeaderMatch(table.ColumnNames[c], key))
                {
                    return c;
                }
            }
        }

        return -1;
    }

    private static int TryFirstColumnByHeaderKeys(ParsedLdtTable table, string[] keys)
    {
        foreach (var key in keys)
        {
            for (var c = 0; c < table.ColumnNames.Length; c++)
            {
                if (HeaderMatch(table.ColumnNames[c], key))
                {
                    return c;
                }
            }
        }

        return -1;
    }

    private static int TryExactIntColumn(ParsedLdtTable table, string exactName)
    {
        for (var c = 0; c < table.ColumnNames.Length; c++)
        {
            if (!IsNumericColumn(table, c))
            {
                continue;
            }

            if (string.Equals(table.ColumnNames[c]?.Trim(), exactName, StringComparison.OrdinalIgnoreCase))
            {
                return c;
            }
        }

        return -1;
    }

    private static int TryFirstStringColumn(ParsedLdtTable table, string[] headers)
    {
        foreach (var h in headers)
        {
            for (var c = 0; c < table.ColumnNames.Length; c++)
            {
                if (table.ColumnTypes[c] != 1)
                {
                    continue;
                }

                if (HeaderMatch(table.ColumnNames[c], h))
                {
                    return c;
                }
            }
        }

        return -1;
    }

    private static int TryFirstIntColumn(ParsedLdtTable table, string[] headers)
    {
        foreach (var h in headers)
        {
            for (var c = 0; c < table.ColumnNames.Length; c++)
            {
                if (!IsNumericColumn(table, c))
                {
                    continue;
                }

                if (HeaderMatch(table.ColumnNames[c], h))
                {
                    return c;
                }
            }
        }

        return -1;
    }

    private static bool IsNumericColumn(ParsedLdtTable table, int c) =>
        c >= 0 && c < table.ColumnTypes.Length && table.ColumnTypes[c] is 0 or 3;

    private static void DiscoverStatusPairs(ParsedLdtTable table, List<(int, int)> pairs)
    {
        var seen = new HashSet<(int, int)>();
        for (var i = 0; i + 1 < table.ColumnNames.Length; i++)
        {
            if (!IsNumericColumn(table, i) || !IsNumericColumn(table, i + 1))
            {
                continue;
            }

            var a = table.ColumnNames[i]?.Trim() ?? "";
            var b = table.ColumnNames[i + 1]?.Trim() ?? "";
            if (!TryGetTrailingNumber(a, out var na, out var sa) || !TryGetTrailingNumber(b, out var nb, out var sb))
            {
                continue;
            }

            if (sa != sb)
            {
                continue;
            }

            var aType = NameLooksTypeColumn(na);
            var bVal = NameLooksValueColumn(nb);
            if (aType && bVal)
            {
                var key = (i, i + 1);
                if (seen.Add(key))
                {
                    pairs.Add(key);
                }

                i++;
            }
        }
    }

    private static bool TryGetTrailingNumber(string name, out string prefix, out string digits)
    {
        prefix = "";
        digits = "";
        var m = Regex.Match(name, @"^(.*)(\d+)\s*$");
        if (!m.Success)
        {
            return false;
        }

        prefix = m.Groups[1].Value;
        digits = m.Groups[2].Value;
        return digits.Length > 0;
    }

    private static bool NameLooksTypeColumn(string prefixTrimmed)
    {
        var u = prefixTrimmed.ToUpperInvariant();
        return u.Contains("TYPE", StringComparison.Ordinal)
               && !u.Contains("VALUE", StringComparison.Ordinal)
               && !u.Contains("VAL_", StringComparison.Ordinal);
    }

    private static bool NameLooksValueColumn(string prefixTrimmed)
    {
        var u = prefixTrimmed.ToUpperInvariant();
        return u.Contains("VALUE", StringComparison.Ordinal)
               || u.Contains("VAL_", StringComparison.Ordinal)
               || u.EndsWith("VAL", StringComparison.OrdinalIgnoreCase)
               || u.Contains("POWER", StringComparison.Ordinal)
               || u.Contains("RATE", StringComparison.Ordinal);
    }

    private static bool IsBoolOrIntFlagColumn(ParsedLdtTable table, int c) =>
        c >= 0 && c < table.ColumnTypes.Length && table.ColumnTypes[c] is 0 or 2 or 3;

    private static readonly string[][] FooterSlotMatchKeys =
    [
        ["TRADE", "交易"],
        ["DESTROY", "BREAK", "破坏"],
        ["WAREHOUSE", "STORAGE", "仓库"],
        ["SELL", "出售", "贩卖"]
    ];

    private static void DiscoverFooterFour(ParsedLdtTable table, int[] slots)
    {
        Array.Fill(slots, -1);
        var used = new HashSet<int>();
        for (var s = 0; s < 4; s++)
        {
            var keys = FooterSlotMatchKeys[s];
            for (var c = 0; c < table.ColumnNames.Length; c++)
            {
                if (used.Contains(c) || !IsBoolOrIntFlagColumn(table, c))
                {
                    continue;
                }

                var n = table.ColumnNames[c]?.Trim() ?? "";
                if (n.Length == 0)
                {
                    continue;
                }

                foreach (var key in keys)
                {
                    if (!n.Contains(key, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    slots[s] = c;
                    used.Add(c);
                    goto NextSlot;
                }
            }

        NextSlot:;
        }
    }

    public static ItemPreviewModel BuildModel(
        ParsedLdtTable table,
        int parsedRow,
        ItemPreviewColumnMap map,
        bool multiSelectFirstRowPrefix)
    {
        var row = table.Rows[parsedRow];
        var model = new ItemPreviewModel();

        string CellStr(int col) =>
            col < 0 || col >= row.Length ? "" : row[col]?.ToString()?.Trim() ?? "";

        int CellInt(int col)
        {
            if (col < 0 || col >= row.Length)
            {
                return 0;
            }

            return CoerceCellToInt32(row[col]);
        }

        model.Title = CellStr(map.NameColumn);
        if (model.Title.Length == 0 && map.ItemIdColumn >= 0)
        {
            model.Title = $"（ID {CellInt(map.ItemIdColumn)}）";
        }

        if (map.TitleRedColumn >= 0 && map.TitleGreenColumn >= 0 && map.TitleBlueColumn >= 0)
        {
            var r = Math.Clamp(CellInt(map.TitleRedColumn), 0, 255);
            var g = Math.Clamp(CellInt(map.TitleGreenColumn), 0, 255);
            var b = Math.Clamp(CellInt(map.TitleBlueColumn), 0, 255);
            model.TitleColor = Color.FromArgb(r, g, b);
        }
        else
        {
            model.TitleColor = Color.DarkRed;
        }

        if (map.InventoryTypeColumn >= 0)
        {
            var invType = CellInt(map.InventoryTypeColumn);
            var invSub = map.InventorySubTypeColumn >= 0 ? CellInt(map.InventorySubTypeColumn) : 0;
            var posRaw = map.PosId1Column >= 0 ? CellStr(map.PosId1Column) : "";
            model.Subtitle = ItemInventoryCategoryFormat.BuildPreviewCategoryLine(invType, invSub, posRaw);
        }

        if (model.Subtitle.Length == 0)
        {
            model.Subtitle = CellStr(map.SubtitleTextColumn);
        }

        if (model.Subtitle.Length == 0 && map.SubTypeNumberColumn >= 0)
        {
            var sn = CellInt(map.SubTypeNumberColumn);
            if (sn != 0)
            {
                model.Subtitle = $"特殊-{sn}号";
            }
        }

        if (map.LevelOwnColumn >= 0)
        {
            var lv = CellInt(map.LevelOwnColumn);
            if (lv != 0 || NameColumnSuggestsWearLevel(map, table))
            {
                model.LevelLine = $"可佩戴等级: {lv}";
            }
        }

        model.DecomposeLine = map.BreakupColumn >= 0 && CellInt(map.BreakupColumn) != 0 ? "可分解" : "";
        var useRaw = CellStr(map.UseTypeColumn);
        if (string.Equals(useRaw, "可分解", StringComparison.OrdinalIgnoreCase))
        {
            useRaw = "";
        }

        model.UseTypeHeader = useRaw.Trim();

        foreach (var (tc, vc) in map.StatusPairs)
        {
            var t = CellInt(tc);
            var v = CellInt(vc);
            if (t == 0 && v == 0)
            {
                continue;
            }

            var (line, ns, nl, hp, ip) = ItemStatusTypeFormat.FormatLineWithRange(t, v, labelOverrides: null);
            model.GreenStatLines.Add(line);
            model.StatusLineMetas.Add(new StatusLineMeta
            {
                Editable = true,
                TypeId = t,
                ValueCol = vc,
                StoredValue = v,
                NumStartInLine = ns,
                NumLength = nl,
                HasPercentSuffix = hp,
                IsPermilleStored = ip
            });
        }

        if (map.LifetimeColumn >= 0)
        {
            model.LifeTimeLine = ItemLifetimePreviewFormat.BuildLine(CellInt(map.LifetimeColumn));
        }

        var desc = map.DescColumn >= 0 ? CellStr(map.DescColumn) : "";
        desc = desc.Replace("\\n", "\n", StringComparison.Ordinal);
        var loreParts = SplitLore(desc);
        model.LoreTitle = loreParts.Title;
        model.LoreBody = loreParts.Body;

        for (var i = 0; i < 4; i++)
        {
            var col = map.FooterSlotColumns[i];
            var on = col >= 0 && CellInt(col) != 0;
            model.FooterTags.Add(new ItemPreviewFooterTag(FooterFourLabels[i], on));
        }

        if (multiSelectFirstRowPrefix)
        {
            model.GreenStatLines.Insert(0, "（多选 · 首行）");
            model.StatusLineMetas.Insert(0, StatusLineMeta.NotEditable);
        }

        return model;
    }

    private static bool NameColumnSuggestsWearLevel(ItemPreviewColumnMap map, ParsedLdtTable table)
    {
        if (map.LevelOwnColumn < 0)
        {
            return false;
        }

        var n = table.ColumnNames[map.LevelOwnColumn]?.ToUpperInvariant() ?? "";
        return n.Contains("LEVEL", StringComparison.Ordinal) || n.Contains("等级", StringComparison.Ordinal);
    }

    private static (string Title, string Body) SplitLore(string desc)
    {
        if (desc.Length == 0)
        {
            return ("", "");
        }

        var lines = desc.Replace("\r\n", "\n").Split('\n', StringSplitOptions.None);
        if (lines.Length == 0)
        {
            return ("", "");
        }

        if (lines.Length == 1)
        {
            return ("", lines[0].Trim());
        }

        var title = lines[0].Trim();
        var body = string.Join("\n", lines.Skip(1)).Trim();
        return (title, body);
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

/// <summary>ITEM 表 <c>_Lifetime</c>：按<strong>总小时</strong>换算为「有效时间：X天Y小时」。</summary>
internal static class ItemLifetimePreviewFormat
{
    public static string BuildLine(int totalHours)
    {
        if (totalHours <= 0)
        {
            return "";
        }

        var days = totalHours / 24;
        var hours = totalHours % 24;
        return $"有效时间：{days}天{hours}小时";
    }
}

internal static class ItemInventoryCategoryFormat
{
    /// <summary>
    /// 装备栏（<c>_Type==1</c>）下 <c>_SubType</c> 为 1–4 时，将 <c>_PosID1</c> 数值解析为中文槽位/宝石/特殊分类名。
    /// </summary>
    public static bool TryGetEquipPosId1Label(int subType, int posId1, out string label)
    {
        label = "";
        switch (subType)
        {
            case 1:
            case 2:
                label = posId1 switch
                {
                    1 => "帽子",
                    2 => "眼镜",
                    3 => "耳环",
                    4 => "上衣",
                    5 => "下衣",
                    6 => "披风",
                    7 => "手套",
                    8 => "鞋子",
                    9 => "丝袜",
                    10 => "星朵",
                    11 => "精灵石",
                    12 => "戒指",
                    13 => "主武器",
                    14 => "副武器",
                    17 => "缀饰",
                    18 => "图腾",
                    19 => "遗物",
                    20 => "手表",
                    _ => ""
                };
                break;
            case 3:
                label = posId1 switch
                {
                    1 => "红色1号",
                    2 => "红色2号",
                    3 => "红色3号",
                    4 => "红色4号",
                    5 => "黄色1号",
                    6 => "黄色2号",
                    7 => "黄色3号",
                    8 => "黄色4号",
                    9 => "蓝色1号",
                    10 => "蓝色2号",
                    11 => "蓝色3号",
                    12 => "蓝色4号",
                    13 => "彩色宝石",
                    _ => ""
                };
                break;
            case 4:
                label = posId1 switch
                {
                    1 => "镜子",
                    2 => "项链",
                    3 => "指南针",
                    4 => "教本",
                    5 => "卡片",
                    6 => "钥匙",
                    7 => "1号徽章",
                    8 => "2号徽章",
                    9 => "3号徽章",
                    10 => "4号徽章",
                    11 => "5号徽章",
                    12 => "6号徽章",
                    13 => "贴纸",
                    14 => "腰带",
                    15 => "胸针",
                    16 => "特殊1号",
                    17 => "特殊2号",
                    18 => "特殊3号",
                    19 => "特殊4号",
                    _ => ""
                };
                break;
            default:
                return false;
        }

        return label.Length > 0;
    }

    /// <summary>
    /// 若 <paramref name="posSegment"/> 为整数字符串且在 <paramref name="subType"/> 1–4 的映射表内，则返回中文；否则返回去空白后的原文。
    /// </summary>
    public static string ResolveEquipPosId1DisplaySegment(int subType, string? posSegment)
    {
        var raw = posSegment?.Trim() ?? "";
        if (raw.Length == 0 || subType is < 1 or > 4)
        {
            return raw;
        }

        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
        {
            return raw;
        }

        return TryGetEquipPosId1Label(subType, id, out var mapped) ? mapped : raw;
    }

    public static string FormatSubTypeWithPosId(string? subTypeLabel, string? posId1)
    {
        var label = subTypeLabel?.Trim() ?? "";
        if (label.Length == 0)
        {
            return "";
        }

        var pos = posId1?.Trim() ?? "";
        return pos.Length == 0 ? label : $"{label}-{pos}";
    }

    public static bool TryGetEquipSubTypeLabel(int subType, out string label)
    {
        label = subType switch
        {
            1 => "战斗装备",
            2 => "时尚装备",
            3 => "宝石",
            4 => "特殊",
            5 => "觉醒石",
            6 => "圣物",
            _ => ""
        };
        return label.Length > 0;
    }

    public static bool TryGetBagTypeLabel(int type, out string label)
    {
        label = type switch
        {
            1 => "装备栏",
            2 => "消耗栏",
            3 => "其他栏",
            4 => "活动栏",
            5 => "宠物栏",
            _ => ""
        };
        return label.Length > 0;
    }

    public static string BuildPreviewCategoryLine(int type, int subType, string? posId1FromCell) =>
        BuildPreviewCategoryLine(type, subType, posId1FromCell, posIdResolverBySubType: null);

    /// <summary>
    /// <paramref name="posIdResolverBySubType"/> 在 <paramref name="type"/>==1（装备栏）时优先：
    /// 按子类返回 PosID 片段，便于各 <c>_SubType</c> 使用不同填法；返回 null/空白则回退到表格 <c>_PosID1</c>。
    /// </summary>
    public static string BuildPreviewCategoryLine(int type, int subType, string? posId1FromCell, Func<int, string?>? posIdResolverBySubType)
    {
        if (type == 1)
        {
            if (!TryGetEquipSubTypeLabel(subType, out var subLabel))
            {
                return "";
            }

            var pos = posIdResolverBySubType?.Invoke(subType)?.Trim();
            if (string.IsNullOrEmpty(pos))
            {
                pos = posId1FromCell?.Trim() ?? "";
            }

            pos = ResolveEquipPosId1DisplaySegment(subType, pos);
            return FormatSubTypeWithPosId(subLabel, pos);
        }

        if (!TryGetBagTypeLabel(type, out var bag))
        {
            return "";
        }

        return FormatSubTypeWithPosId(bag, posId1FromCell);
    }

    public static string FormatEquipSubTypeLine(int subType, IReadOnlyDictionary<int, string> posId1BySubType)
    {
        if (!TryGetEquipSubTypeLabel(subType, out var subLabel))
        {
            return "";
        }

        return posId1BySubType.TryGetValue(subType, out var pos)
            ? FormatSubTypeWithPosId(subLabel, pos)
            : subLabel;
    }
}

internal static class ItemStatusTypeFormat
{
    public static string FormatLine(int typeId, int value, IReadOnlyDictionary<int, string>? labelOverrides = null)
    {
        var (line, _, _, _, _) = FormatLineWithRange(typeId, value, labelOverrides);
        return line;
    }

    /// <summary>
    /// 与 <see cref="FormatLine"/> 同义但同时返回数值数字部分在结果字符串中的子串区间，以及类型属性：
    /// 是否带百分号后缀（<see cref="ItemStatusTypeCatalog.UsesPercentSuffix"/>）与是否为千分储存（<see cref="ItemStatusTypeCatalog.UsesPermilleStoredAsPercentDisplay"/>）。
    /// 数字区间不含「%」符号——内联编辑只覆盖数字，% 保持可见。
    /// </summary>
    public static (string Line, int NumStartInLine, int NumLength, bool HasPercentSuffix, bool IsPermilleStored)
        FormatLineWithRange(int typeId, int value, IReadOnlyDictionary<int, string>? labelOverrides = null)
    {
        if (typeId == 0)
        {
            return ("", 0, 0, false, false);
        }

        string label;
        if (labelOverrides is not null
            && labelOverrides.TryGetValue(typeId, out var ovr)
            && !string.IsNullOrWhiteSpace(ovr))
        {
            label = ovr;
        }
        else if (ItemStatusTypeCatalog.TryGetLabel(typeId, out var cat))
        {
            label = cat;
        }
        else
        {
            label = $"状态类型 {typeId}";
        }

        var hasPercent = ItemStatusTypeCatalog.UsesPercentSuffix(typeId);
        var isPermille = hasPercent && ItemStatusTypeCatalog.UsesPermilleStoredAsPercentDisplay(typeId);
        var numberText = isPermille
            ? ItemStatusTypeCatalog.FormatStoredPermilleAsPercentNumber(value)
            : value.ToString(CultureInfo.InvariantCulture);
        var suffix = hasPercent ? numberText + "%" : numberText;
        var prefix = label + " + ";
        var line = prefix + suffix;
        return (line, prefix.Length, numberText.Length, hasPercent, isPermille);
    }
}

internal static class ItemIconPreviewLoader
{
    public static Image? TryLoadIconCell(string spfPath, int iconId, int iconIndexOneBased)
    {
        if (!LaTaleIconSheet.IsKnownIconSheet(iconId))
        {
            return null;
        }

        if (!SpfIconIndexCache.TryEnsureLoaded(spfPath, null, out var chain, out var map, out _))
        {
            return null;
        }

        var suf = LaTaleIconSheet.DefaultSheetSuffixForIconId(iconId);
        if (LaTaleIconSheet.IconIdUsesSheetSuffixInStoredIconId(iconId)
            && SpfIconIndexCache.TryGetAvailableSheetSuffixes(iconId, spfPath, map, out var available))
        {
            suf = SpfIconIndexCache.SnapSuffixToAvailable(suf, available);
        }

        if (!LaTaleIconSheet.TryResolveSheet(iconId, suf, out var logical, out _))
        {
            return null;
        }

        if (!SpfIconIndexCache.TryLoadSheetPngBytes(spfPath, logical, chain, map, out var png, out _))
        {
            return null;
        }

        var atlas = Decode(png);
        try
        {
            atlas = IconAtlasDecodeHelper.MaybeDownscaleDecodedAtlas(atlas, out var decodeScale);
            var cell = Math.Max(8, (int)Math.Round(LaTaleIconSheet.AtlasCellPixels * decodeScale));
            var cols = Math.Max(1, atlas.Width / cell);
            var rows = Math.Max(1, atlas.Height / cell);
            var maxIdx = cols * rows - 1;
            var zeroBased = Math.Max(0, iconIndexOneBased - LaTaleIconSheet.IconIndexFirstValue);
            var idx = Math.Clamp(zeroBased, 0, Math.Max(0, maxIdx));
            var rr = idx / cols;
            var cc = idx % cols;
            var src = new Rectangle(cc * cell, rr * cell, cell, cell);
            var thumb = new Bitmap(cell, cell);
            using (var g = Graphics.FromImage(thumb))
            {
                g.DrawImage(atlas, new Rectangle(0, 0, cell, cell), src, GraphicsUnit.Pixel);
            }

            return thumb;
        }
        finally
        {
            atlas.Dispose();
        }
    }

    private static Bitmap Decode(byte[] payload)
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
}
