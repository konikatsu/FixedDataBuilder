using System.Globalization;
using System.Text;
using System.Diagnostics;

namespace FixedDataBuilder;

public sealed partial class MainForm : Form
{
    private static readonly string WindowTitle = $"FixedDataBuilder v{typeof(MainForm).Assembly.GetName().Version?.ToString(3)}";
    private static readonly Color DefinitionBackColor = Color.FromArgb(226, 242, 226);
    private static readonly Color DefinitionHeaderBackColor = Color.FromArgb(204, 232, 204);
    private static readonly Color OccursHeaderBackColor = Color.FromArgb(223, 235, 248);
    private static readonly Color OccursHeaderForeColor = Color.FromArgb(35, 62, 102);
    private static readonly Color OccursGroupBackColor = Color.FromArgb(243, 248, 255);
    private static readonly Color OccursGroupAlternateBackColor = Color.FromArgb(247, 244, 255);
    private static readonly Color ErrorBackColor = Color.FromArgb(255, 225, 225);
    private static readonly Color HalfWidthSpaceBackColor = Color.FromArgb(255, 250, 205);
    private static readonly string RecentFilesPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FixedDataBuilder",
        "recent-files.txt");
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FixedDataBuilder",
        "settings.txt");
    private static readonly SamplePattern[] CopybookSamplePatterns =
    [
        new("copybook-basic-data-crlf-utf8-n-utf16le.dat", "copybook-basic-definition.cbl", RecordSeparatorMode.CrLfOrLf, NationalTextEncoding.Utf16),
        new("copybook-basic-data-none-sjis-n-sjis.dat", "copybook-basic-definition.cbl", RecordSeparatorMode.None, NationalTextEncoding.ShiftJis),
        new("copybook-basic-data-none-utf8-n-utf16le.dat", "copybook-basic-definition.cbl", RecordSeparatorMode.None, NationalTextEncoding.Utf16),
        new("copybook-basic-data-none-utf8-n-utf32le.dat", "copybook-basic-definition.cbl", RecordSeparatorMode.None, NationalTextEncoding.Utf32),
        new("copybook-occurs-data-crlf-utf8-n-utf16le.dat", "copybook-occurs-definition.cbl", RecordSeparatorMode.CrLfOrLf, NationalTextEncoding.Utf16),
        new("copybook-group-occurs-data-crlf-utf8-n-utf16le.dat", "copybook-group-occurs-definition.cbl", RecordSeparatorMode.CrLfOrLf, NationalTextEncoding.Utf16),
        new("sample-copybook-utf16.dat", "definition-english.cbl", RecordSeparatorMode.CrLfOrLf, NationalTextEncoding.Utf16),
        new("sample-copybook-crlf-utf8-nutf16.dat", "definition-english.cbl", RecordSeparatorMode.CrLfOrLf, NationalTextEncoding.Utf16),
        new("sample-copybook-none-sjis-nsjis.dat", "definition-english.cbl", RecordSeparatorMode.None, NationalTextEncoding.ShiftJis),
        new("sample-copybook-none-utf8-nutf16.dat", "definition-english.cbl", RecordSeparatorMode.None, NationalTextEncoding.Utf16),
        new("sample-copybook-none-utf8-nutf32.dat", "definition-english.cbl", RecordSeparatorMode.None, NationalTextEncoding.Utf32)
    ];

    private readonly TextBox definitionPathTextBox = new();
    private readonly TextBox xmlLayoutTextBox = new();
    private readonly TextBox dataPathTextBox = new();
    private readonly TextBox separatorModeTextBox = new();
    private readonly WheelZoomTextBox hexTextBox = new();
    private readonly Label selectionSpaceLabel = new() { Name = "SelectedSpaceCount", Text = "半角スペース: — / 全角スペース: —", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
    private readonly Label zoomLabel = new() { Name = "DisplayZoom", AutoSize = true, TextAlign = ContentAlignment.MiddleRight, Dock = DockStyle.Fill };
    private int zoomWheelRemainder;
    private readonly WheelZoomDataGridView grid = new();
    private readonly Panel hexArea = new() { Name = "ResizableHexArea", Dock = DockStyle.Bottom, Height = 88 };
    private readonly Panel hexResizeGrip = new()
    {
        Name = "HexResizeGrip", Dock = DockStyle.Top, Height = 6,
        Cursor = Cursors.HSplit, BackColor = SystemColors.ControlDark,
        AccessibleName = "選択項目エリアの高さ調整"
    };
    private int? hexResizeStartScreenY;
    private int hexResizeStartHeight;
    private bool isResizingHexArea;
    private readonly MenuStrip menuStrip = new();
    private readonly ToolStripMenuItem recentDefinitionFilesMenu = new("最近使った定義ファイル");
    private readonly ToolStripMenuItem recentDataFilesMenu = new("最近使ったデータファイル");
    private readonly StatusStrip statusStrip = new();
    private readonly ToolStripStatusLabel statusLabel = new();
    private readonly ToolStripMenuItem fieldRowsButton;
    private readonly ToolStripMenuItem recordRowsButton;
    private readonly ToolStripMenuItem wrapDisplayButton = new("折り返し表示") { Name = "WrapDisplay", CheckOnClick = true };
    private DisplayWrapOptions displayWrapOptions = new();
    private readonly List<string> recentDefinitionFiles = [];
    private readonly List<string> recentDataFiles = [];
    private readonly List<FieldDefinition> fields = [];
    private readonly List<List<string>> records = [];
    private readonly HashSet<int> hiddenFieldIndexes = [];
    private readonly List<FieldGridRow> fieldGridRows = [];
    private readonly List<int> recordGridColumnFieldIndexes = [];
    private readonly Dictionary<int, OccursDisplayInfo> occursDisplayInfos = [];
    private string? saveDataPath;
    private DataFileFormat currentDataFileFormat = DataFileFormat.FixedLength;
    private CsvTextEncoding currentCsvEncoding = CsvTextEncoding.Utf8;
    private bool currentCsvHasHeader;
    private bool IsCsv => currentDataFileFormat == DataFileFormat.Csv;
    private RecordSeparatorMode currentSeparatorMode = RecordSeparatorMode.CrLfOrLf;
    private DataEncodingProfile currentDataEncodingProfile = DataEncodingProfile.ShiftJis;
    private NationalTextEncoding currentNationalTextEncoding = NationalTextEncoding.ShiftJis;
    private XmlLayoutDefinition? currentXmlLayout;
    private Encoding TextDataEncoding => JapaneseEncodings.ForData(currentDataEncodingProfile);
    private GridLayout layout = GridLayout.FieldRows;
    private bool isRefreshingGrid;
    private Font? displayFont;
    private bool hasUnsavedChanges;
    private readonly DataLoadSettingsStore loadSettingsStore;

    public MainForm(string[]? args = null, DataLoadSettingsStore? loadSettingsStore = null)
    {
        this.loadSettingsStore = loadSettingsStore ?? new DataLoadSettingsStore(DataLoadSettingsStore.DefaultPath);
        Text = WindowTitle;
        StartPosition = FormStartPosition.CenterScreen;
        Width = 1100;
        Height = 740;
        MinimumSize = new Size(720, 420);

        fieldRowsButton = CreateMenuItem("項目縦", (_, _) => ChangeLayout(GridLayout.FieldRows));
        recordRowsButton = CreateMenuItem("レコード縦", (_, _) => ChangeLayout(GridLayout.RecordRows));

        LoadRecentFiles();
        LoadSettings();
        displayFont ??= CreateDefaultDisplayFont();

        Controls.Add(grid);
        Controls.Add(BuildHexPanel());
        Controls.Add(statusStrip);
        Controls.Add(BuildViewPanel());
        Controls.Add(BuildFilePanel());
        Controls.Add(menuStrip);

        BuildMenuStrip();
        MainMenuStrip = menuStrip;
        BuildGrid();
        ApplyDisplayFont();

        statusLabel.Spring = true;
        statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        statusStrip.Items.Add(statusLabel);
        UpdateSeparatorModeText();
        EnableFileDrop(this);
        Layout += (_, _) => SetHexAreaHeight(hexArea.Height);
        DpiChanged += (_, _) =>
        {
            hexResizeGrip.Height = Math.Max(6, (int)Math.Round(6 * DeviceDpi / 96f));
            SetHexAreaHeight(hexArea.Height);
        };
        SetStatus("定義ファイルを選択するか、ファイルをドラッグ＆ドロップしてください。");
        LoadStartupFiles(args ?? []);
    }

    private void SetUnsavedChanges(bool value)
    {
        hasUnsavedChanges = value;
        Text = WindowTitle + (value ? " 【未保存】" : string.Empty);
    }

    private bool ConfirmDiscardChanges(string action)
    {
        if (!grid.EndEdit()) return false;
        if (!hasUnsavedChanges) return true;
        using var dialog = new UnsavedChangesForm(action);
        return dialog.ShowDialog(this) switch
        {
            DialogResult.Yes => SaveDataOverwrite(),
            DialogResult.No => true,
            _ => false
        };
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!e.Cancel && !ConfirmDiscardChanges("終了")) e.Cancel = true;
        base.OnFormClosing(e);
    }

    private bool RememberCurrentLoadSettings(string path)
    {
        var definition = GetCurrentDefinitionPath();
        return string.IsNullOrWhiteSpace(definition) || loadSettingsStore.Remember(definition, path,
            new DataLoadSettings(currentDataFileFormat, currentCsvEncoding, currentSeparatorMode, currentNationalTextEncoding, currentDataEncodingProfile, currentCsvHasHeader),
            currentXmlLayout?.Id);
    }

    private Control BuildFilePanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 3,
            RowCount = 6,
            Padding = new Padding(8, 6, 8, 4)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));

        ConfigureReadOnlyTextBox(definitionPathTextBox);
        ConfigureReadOnlyTextBox(xmlLayoutTextBox);
        ConfigureReadOnlyTextBox(dataPathTextBox);
        ConfigureReadOnlyTextBox(separatorModeTextBox);
        xmlLayoutTextBox.Visible = false;

        panel.Controls.Add(CreatePathLabel("定義ファイル"), 0, 0);
        panel.Controls.Add(definitionPathTextBox, 1, 0);
        var reloadDefinitionButton = new Button
        {
            Name = "ReloadDefinition",
            Text = "定義を再読み込み",
            Dock = DockStyle.Fill,
            Margin = new Padding(6, 0, 0, 0),
            UseVisualStyleBackColor = true
        };
        reloadDefinitionButton.Click += (_, _) => ReloadDefinition();
        panel.Controls.Add(reloadDefinitionButton, 2, 0);
        panel.Controls.Add(CreatePathLabel("データファイル"), 0, 1);
        panel.Controls.Add(dataPathTextBox, 1, 1);
        var reloadButton = new Button
        {
            Name = "ReloadFiles",
            Text = "全体再読込 (F5)",
            Dock = DockStyle.Fill,
            Margin = new Padding(6, 0, 0, 0),
            UseVisualStyleBackColor = true
        };
        reloadButton.Click += (_, _) => ReloadCurrentFiles();
        panel.Controls.Add(reloadButton, 2, 1);
        panel.Controls.Add(CreatePathLabel("レコード区切り"), 0, 2);
        panel.Controls.Add(separatorModeTextBox, 1, 2);
        panel.SetColumnSpan(separatorModeTextBox, 2);
        var xmlLayoutLabel = CreatePathLabel("XMLレイアウト");
        xmlLayoutLabel.Visible = false;
        panel.Controls.Add(xmlLayoutLabel, 0, 3);
        panel.Controls.Add(xmlLayoutTextBox, 1, 3);
        panel.SetColumnSpan(xmlLayoutTextBox, 2);
        var encodingHelp = new Label
        {
            Name = "EncodingHelp",
            Text = "半角の英字・数字はUTF-8・SJIS・MS932で同じです。日本語はファイルの文字コードに合わせてください。",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        };
        panel.Controls.Add(encodingHelp, 0, 4);
        panel.SetColumnSpan(encodingHelp, 3);
        var dropHint = new Label
        {
            Text = "ファイルを画面へドロップして開く（定義＋データの同時ドロップ可／データCSVはデータファイル欄へ）",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = SystemColors.GrayText,
            AutoEllipsis = true
        };
        panel.Controls.Add(dropHint, 0, 5);
        panel.SetColumnSpan(dropHint, 3);
        return panel;
    }

    private Control BuildHexPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Height = 82,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(8, 2, 8, 4)
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.Controls.Add(new Label { Text = "選択項目 HEX", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        header.Controls.Add(selectionSpaceLabel, 1, 0);
        header.Controls.Add(zoomLabel, 2, 0);
        panel.Controls.Add(header, 0, 0);

        hexTextBox.Dock = DockStyle.Fill;
        hexTextBox.ReadOnly = true;
        hexTextBox.Multiline = true;
        hexTextBox.ScrollBars = ScrollBars.Vertical;
        hexTextBox.Font = new Font(FontFamily.GenericMonospace, 9);
        panel.Controls.Add(hexTextBox, 0, 1);
        hexResizeGrip.Height = Math.Max(6, (int)Math.Round(6 * DeviceDpi / 96f));
        hexArea.Height = panel.Height + hexResizeGrip.Height;
        hexArea.Controls.Add(panel);
        hexArea.Controls.Add(hexResizeGrip);
        hexResizeGrip.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            hexResizeStartScreenY = hexResizeGrip.PointToScreen(e.Location).Y;
            hexResizeStartHeight = hexArea.Height;
            hexResizeGrip.Capture = true;
        };
        hexResizeGrip.MouseMove += (_, e) =>
        {
            if (hexResizeStartScreenY is not int startY || !hexResizeGrip.Capture) return;
            SetHexAreaHeight(hexResizeStartHeight - (hexResizeGrip.PointToScreen(e.Location).Y - startY));
        };
        hexResizeGrip.MouseUp += (_, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            hexResizeStartScreenY = null;
            hexResizeGrip.Capture = false;
        };
        hexResizeGrip.MouseCaptureChanged += (_, _) =>
        {
            if (!hexResizeGrip.Capture) hexResizeStartScreenY = null;
        };
        return hexArea;
    }

    private void SetHexAreaHeight(int requestedHeight)
    {
        if (isResizingHexArea || isSingleRecordView || hexArea.Parent != this || WindowState == FormWindowState.Minimized) return;
        var minimumHexHeight = Math.Max(64, (int)Math.Round(64 * DeviceDpi / 96f));
        var minimumGridHeight = Math.Max(100, (int)Math.Round(100 * DeviceDpi / 96f));
        // Use the client area and docked bars rather than grid.Height, which can
        // still contain its previous size while a form layout is in progress.
        var reservedHeight = Controls.Cast<Control>()
            .Where(control => control != hexArea && control.Dock is (DockStyle.Top or DockStyle.Bottom))
            .Sum(control => control.Height);
        isResizingHexArea = true;
        try
        {
            // Keep both areas usable even when DPI or the docked bars grow.
            var minimumWindow = new Size(Math.Max(720, (int)Math.Round(720 * DeviceDpi / 96f)),
                Math.Max((int)Math.Round(420 * DeviceDpi / 96f),
                    Height - ClientSize.Height + Padding.Vertical + reservedHeight + minimumHexHeight + minimumGridHeight));
            if (MinimumSize != minimumWindow) MinimumSize = minimumWindow;
            var maximum = Math.Max(minimumHexHeight, ClientSize.Height - Padding.Vertical - reservedHeight - minimumGridHeight);
            var height = Math.Clamp(requestedHeight, minimumHexHeight, maximum);
            if (hexArea.Height != height) hexArea.Height = height;
        }
        finally { isResizingHexArea = false; }
    }

    private static Label CreatePathLabel(string text)
    {
        return new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        };
    }

    private static void ConfigureReadOnlyTextBox(TextBox textBox)
    {
        textBox.Dock = DockStyle.Fill;
        textBox.ReadOnly = true;
        textBox.Margin = new Padding(0, 1, 0, 3);
    }

    private void EnableFileDrop(Control control)
    {
        control.AllowDrop = true;
        control.DragEnter -= FileDragEnter;
        control.DragEnter += FileDragEnter;
        control.DragOver -= FileDragEnter;
        control.DragOver += FileDragEnter;
        control.DragDrop -= FileDragDrop;
        control.DragDrop += FileDragDrop;
        control.ControlAdded -= FileDropControlAdded;
        control.ControlAdded += FileDropControlAdded;
        foreach (Control child in control.Controls)
        {
            EnableFileDrop(child);
        }
    }

    private void FileDropControlAdded(object? sender, ControlEventArgs e)
    {
        if (e.Control is not null) EnableFileDrop(e.Control);
    }

    private void FileDragEnter(object? sender, DragEventArgs e)
    {
        e.Effect = (e.AllowedEffect & DragDropEffects.Copy) != 0
            && TryGetFileDropRequest(sender, e.Data, out _)
                ? DragDropEffects.Copy
                : DragDropEffects.None;
    }

    private bool TryGetFileDropRequest(object? sender, IDataObject? data, out FileDropRequest? request)
    {
        request = null;
        if (data?.GetData(DataFormats.FileDrop) is not string[] { Length: 1 or 2 } paths
            || paths.Any(path => !File.Exists(path)))
        {
            return false;
        }

        if (paths.Length == 1)
        {
            var path = paths[0];
            if (IsExcelPath(path))
                request = new(null, null, path);
            else if (ReferenceEquals(sender, dataPathTextBox))
                request = new(null, path, null);
            else if (ReferenceEquals(sender, definitionPathTextBox) || IsDefinitionPath(path))
                request = new(path, null, null);
            else
                request = new(null, path, null);
            return true;
        }

        // 複数ファイルは定義1件とデータ1件だけ受け付け、並び順によらず定義を先に読む。
        var definitionPaths = paths.Where(IsDefinitionPath).ToArray();
        if (definitionPaths.Length != 1 || paths.Any(IsExcelPath)) return false;
        request = new(definitionPaths[0], paths.First(path => path != definitionPaths[0]), null);
        return true;
    }

    private static bool IsDefinitionPath(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".csv", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".xml", StringComparison.OrdinalIgnoreCase)
            || IsCopybookPath(path);
    }

    private static bool IsExcelPath(string path)
    {
        return Path.GetExtension(path).Equals(".xlsx", StringComparison.OrdinalIgnoreCase);
    }

    private void FileDragDrop(object? sender, DragEventArgs e)
    {
        e.Effect = DragDropEffects.None;
        if ((e.AllowedEffect & DragDropEffects.Copy) == 0
            || !TryGetFileDropRequest(sender, e.Data, out var request)
            || request is null) return;

        e.Effect = DragDropEffects.Copy;
        grid.EndEdit();
        if (request.DefinitionPath is not null
            && !LoadDefinitionFromPath(request.DefinitionPath, "ドロップした定義ファイルを読み込みました。")) return;
        if (request.DataPath is not null)
        {
            LoadDataFromPath(request.DataPath, "ドロップしたデータファイルを読み込みました。");
        }
        else if (request.ExcelPath is not null)
        {
            ImportExcelFromPath(request.ExcelPath);
        }
    }

    private void BuildMenuStrip()
    {
        menuStrip.Dock = DockStyle.Top;
        menuStrip.Items.Add(CreateMenu("ファイル",
            CreateMenuItem("定義を開く...", (_, _) => OpenDefinition()),
            CreateMenuItem("データを開く...", (_, _) => OpenData()),
            new ToolStripMenuItem("再読み込み", null, (_, _) => ReloadCurrentFiles()) { ShortcutKeys = Keys.F5 },
            new ToolStripSeparator(),
            recentDefinitionFilesMenu,
            recentDataFilesMenu,
            new ToolStripSeparator(),
            CreateMenuItem("上書き保存", (_, _) => SaveDataOverwrite()),
            CreateMenuItem("名前を付けて保存...", (_, _) => SaveDataAs()),
            new ToolStripSeparator(),
            CreateMenuItem("Excel出力...", (_, _) => ExportExcel()),
            CreateMenuItem("Excel取込...", (_, _) => ImportExcel()),
            new ToolStripSeparator(),
            CreateMenuItem("終了", (_, _) => Close())));
        menuStrip.Items.Add(CreateMenu("編集",
            CreateMenuItem("レコード追加", (_, _) => AddRecord()),
            CreateMenuItem("レコード複製", (_, _) => DuplicateRecord()),
            CreateMenuItem("レコード削除", (_, _) => DeleteRecord())));
        menuStrip.Items.Add(CreateMenu("定義",
            CreateMenuItem("定義作成...", (_, _) => CreateDefinition(editCurrent: false)),
            CreateMenuItem("定義修正...", (_, _) => CreateDefinition(editCurrent: true)),
            BuildDefinitionEncodingMenu()));
        wrapDisplayButton.CheckedChanged += (_, _) =>
        {
            if (displayWrapOptions.Enabled != wrapDisplayButton.Checked)
                ApplyDisplayWrapOptions(displayWrapOptions with { Enabled = wrapDisplayButton.Checked });
        };
        menuStrip.Items.Add(CreateMenu("表示",
            fieldRowsButton,
            recordRowsButton,
            singleRecordViewButton,
            new ToolStripSeparator(),
            CreateMenuItem("表示項目...", (_, _) => ChooseVisibleFields()),
            CreateMenuItem("フォント設定...", (_, _) => ChooseDisplayFont()),
            CreateMenuItem("拡大", (_, _) => ChangeZoomFromWheel(120)),
            CreateMenuItem("縮小", (_, _) => ChangeZoomFromWheel(-120)),
            new ToolStripSeparator(),
            wrapDisplayButton,
            CreateMenuItem("折り返し表示設定...", (_, _) => ChooseDisplayWrapOptions())));
        menuStrip.Items.Add(CreateMenu("ツール",
            CreateMenuItem("検証", (_, _) => ValidateRecords(showSuccess: true))));
        RefreshRecentFileItems();
    }

    private static ToolStripMenuItem CreateMenu(string text, params ToolStripItem[] items)
    {
        var menu = new ToolStripMenuItem(text);
        menu.DropDownItems.AddRange(items);
        return menu;
    }

    private static ToolStripMenuItem CreateMenuItem(string text, EventHandler onClick)
    {
        var item = new ToolStripMenuItem(text);
        item.Click += onClick;
        return item;
    }

    private void BuildGrid()
    {
        grid.Dock = DockStyle.Fill;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.RowHeadersVisible = false;
        grid.RowHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
        grid.RowHeadersDefaultCellStyle.BackColor = DefinitionBackColor;
        grid.RowHeadersDefaultCellStyle.SelectionBackColor = DefinitionBackColor;
        grid.RowHeadersDefaultCellStyle.SelectionForeColor = grid.DefaultCellStyle.ForeColor;
        grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
        grid.MultiSelect = false;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        grid.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
        grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
        grid.EnableHeadersVisualStyles = false;
        grid.CellEndEdit += (_, e) =>
        {
            if (isRefreshingGrid)
            {
                return;
            }

            try
            {
                UpdateRecordValue(e.RowIndex, e.ColumnIndex);
                AdjustWrappedRowHeights();
                grid.InvalidateRow(e.RowIndex);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "セル編集エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                RefreshGrid();
            }
        };
        grid.SelectionChanged += (_, _) =>
        {
            if (!isRefreshingGrid)
            {
                UpdateHexView();
            }
        };
        grid.CellPainting += (_, e) => PaintTextPaddingArea(e);
        grid.ZoomWheel += (_, e) => ChangeZoomFromWheel(e.Delta);
        hexTextBox.ZoomWheel += (_, e) => ChangeZoomFromWheel(e.Delta);
        grid.ColumnWidthChanged += (_, _) =>
        {
            if (isSingleRecordView && !isRefreshingGrid) AdjustSingleRecordRowHeights();
        };
    }

    private void ChooseDisplayWrapOptions()
    {
        using var form = new DisplayWrapOptionsForm(displayWrapOptions);
        if (form.ShowDialog(this) == DialogResult.OK) ApplyDisplayWrapOptions(form.Options);
    }

    private void ApplyDisplayWrapOptions(DisplayWrapOptions options)
    {
        grid.EndEdit();
        displayWrapOptions = options;
        wrapDisplayButton.Checked = options.Enabled;
        RefreshGridSizing();
        SizeColumnsToFitData();
        grid.Invalidate();
    }

    private IReadOnlyList<string>? WrappedDisplayLines(int row, int column)
    {
        if (!displayWrapOptions.Enabled || row < 0 || column < 0
            || (layout == GridLayout.RecordRows && RecordIndexFromGridRow(row) < 0)
            || !TryGetCellField(row, column, out var field)) return null;
        var value = grid[column, row].Value?.ToString() ?? string.Empty;
        var encoding = IsCsv ? JapaneseEncodings.ForCsv(currentCsvEncoding)
            : field.Type == FieldDataType.FullWidthText ? NationalTextEncodingHelper.GetEncoding(currentNationalTextEncoding)
            : TextDataEncoding;
        try
        {
            var shouldWrap = encoding.GetByteCount(value) >= displayWrapOptions.ThresholdBytes
                || value.Contains('\r') || value.Contains('\n');
            var truncated = false;
            var displayed = displayWrapOptions.LimitDisplay
                ? DisplayTextLimiter.Limit(value, displayWrapOptions.MaxCharacters, out truncated) : value;
            if (!shouldWrap && !truncated) return null;
            // Padding belongs to the full fixed-length display. A truncated value
            // ends at its omission marker rather than adding hidden padding.
            var padding = !truncated && !IsCsv && field.Type is FieldDataType.Text or FieldDataType.HalfWidthText or FieldDataType.FullWidthText
                ? Math.Max(0, GetVisibleTextValue(field, value).Length - value.Length) : 0;
            var padded = displayed + new string(field.Type == FieldDataType.FullWidthText ? '\u3000' : ' ', padding);
            var lines = (shouldWrap ? ByteTextWrapper.Lines(padded, encoding, displayWrapOptions.LineBytes) : [padded])
                .Select(part => BuildVisibleTextValue(field, part, 0)).ToArray();
            if (truncated) lines[^1] += "…";
            return lines;
        }
        catch (EncoderFallbackException)
        {
            // Invalid values remain editable and retain their validation error.
            return null;
        }
    }

    private void AdjustWrappedRowHeights()
    {
        if (!displayWrapOptions.Enabled) { AdjustSingleRecordRowHeights(); return; }
        foreach (DataGridViewRow row in grid.Rows)
        {
            var height = grid.RowTemplate.Height;
            foreach (DataGridViewCell cell in row.Cells)
            {
                var lines = WrappedDisplayLines(row.Index, cell.ColumnIndex);
                if (lines is not null)
                    height = Math.Max(height, lines.Count * ((cell.InheritedStyle.Font ?? grid.Font).Height + 2) + 6);
            }
            row.Height = Math.Clamp(height, row.MinimumHeight, 65536);
        }
        AdjustSingleRecordRowHeights();
    }

    private void SizeWrappedColumns()
    {
        foreach (DataGridViewColumn column in grid.Columns)
        {
            var width = TextRenderer.MeasureText(column.HeaderText, column.InheritedStyle.Font ?? grid.Font).Width + 20;
            foreach (DataGridViewRow row in grid.Rows)
            {
                var cell = row.Cells[column.Index];
                var lines = WrappedDisplayLines(row.Index, column.Index);
                if (lines is null)
                {
                    var value = cell.Value?.ToString() ?? string.Empty;
                    if (TryGetCellField(row.Index, column.Index, out var field)
                        && !(layout == GridLayout.RecordRows && RecordIndexFromGridRow(row.Index) < 0)
                        && (IsCsv || field.Type is FieldDataType.Text or FieldDataType.HalfWidthText or FieldDataType.FullWidthText))
                        value = GetVisibleTextValue(field, value);
                    lines = [value];
                }
                foreach (var line in lines)
                    width = Math.Max(width, TextRenderer.MeasureText(line, cell.InheritedStyle.Font ?? grid.Font,
                        Size.Empty, TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine).Width + cell.InheritedStyle.Padding.Horizontal + 2);
            }
            column.Width = Math.Clamp(width, column.MinimumWidth, 65536);
        }
    }

    private bool TryPaintWrappedCell(DataGridViewCellPaintingEventArgs e)
    {
        if (e.Graphics is null || e.CellStyle is null) return false;
        var lines = WrappedDisplayLines(e.RowIndex, e.ColumnIndex);
        if (lines is null) return false;
        e.Paint(e.CellBounds, e.PaintParts & ~DataGridViewPaintParts.ContentForeground);
        var font = e.CellStyle.Font ?? grid.Font;
        var selected = (e.State & DataGridViewElementStates.Selected) != 0;
        var padding = e.CellStyle.Padding;
        var bounds = new Rectangle(e.CellBounds.Left + padding.Left, e.CellBounds.Top + padding.Top + 2,
            e.CellBounds.Width - padding.Horizontal - 1, font.Height + 2);
        foreach (var line in lines)
        {
            if (bounds.Top >= e.CellBounds.Bottom) break;
            if (!selected) PaintVisibleSpaceHighlights(e.Graphics, font, bounds, line);
            TextRenderer.DrawText(e.Graphics, line, font, bounds,
                selected ? e.CellStyle.SelectionForeColor : e.CellStyle.ForeColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix
                    | TextFormatFlags.SingleLine | TextFormatFlags.PreserveGraphicsClipping);
            bounds.Y += font.Height + 2;
        }
        e.Handled = true;
        return true;
    }

    private void ChooseVisibleFields()
    {
        if (fields.Count == 0)
        {
            MessageBox.Show(this, "先に定義ファイルを読み込んでください。", "項目表示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var form = new Form
        {
            Text = "表示する項目",
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MinimizeBox = false,
            MaximizeBox = false,
            ClientSize = new Size(460, 520)
        };

        var list = new CheckedListBox
        {
            Dock = DockStyle.Fill,
            CheckOnClick = true,
            IntegralHeight = false
        };
        var displayFieldIndexes = DisplayableFieldIndexes();
        if (displayFieldIndexes.Count == 0)
        {
            MessageBox.Show(this, "表示できる項目がありません。", "項目表示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        for (var listIndex = 0; listIndex < displayFieldIndexes.Count; listIndex++)
        {
            var fieldIndex = displayFieldIndexes[listIndex];
            list.Items.Add(FieldVisibilityLabel(fields[fieldIndex]), !hiddenFieldIndexes.Contains(fieldIndex));
        }

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 44,
            Padding = new Padding(8)
        };
        var okButton = new Button { Text = "OK", DialogResult = DialogResult.OK, Width = 90 };
        var cancelButton = new Button { Text = "キャンセル", DialogResult = DialogResult.Cancel, Width = 90 };
        var showAllButton = new Button { Text = "すべて表示", Width = 100 };
        showAllButton.Click += (_, _) =>
        {
            for (var index = 0; index < list.Items.Count; index++)
            {
                list.SetItemChecked(index, true);
            }
        };

        buttonPanel.Controls.Add(okButton);
        buttonPanel.Controls.Add(cancelButton);
        buttonPanel.Controls.Add(showAllButton);
        form.Controls.Add(list);
        form.Controls.Add(buttonPanel);
        form.AcceptButton = okButton;
        form.CancelButton = cancelButton;

        if (form.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        if (list.CheckedItems.Count == 0)
        {
            MessageBox.Show(this, "少なくとも 1 項目は表示してください。", "項目表示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        hiddenFieldIndexes.Clear();
        for (var listIndex = 0; listIndex < list.Items.Count; listIndex++)
        {
            var fieldIndex = displayFieldIndexes[listIndex];
            if (!list.GetItemChecked(listIndex))
            {
                hiddenFieldIndexes.Add(fieldIndex);
            }
        }

        RefreshGrid();
        SetStatus($"表示項目: {VisibleFieldIndexes().Count} / {DisplayableFieldIndexes().Count}");
    }

    private static string FieldVisibilityLabel(FieldDefinition field)
    {
        return $"{field.Name}  ({field.DisplayDefinition})";
    }

    private void ChooseDisplayFont()
    {
        using var dialog = new FontDialog
        {
            Font = displayFont ?? grid.Font,
            ShowEffects = false
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        displayFont?.Dispose();
        displayFont = new Font(dialog.Font.FontFamily, dialog.Font.Size, FontStyle.Regular);
        ApplyDisplayFont();
        SaveSettings();
        SetStatus($"フォントを設定しました: {displayFont.Name} {displayFont.SizeInPoints:0.#}pt");
    }

    private void ApplyDisplayFont()
    {
        if (displayFont is null)
        {
            return;
        }

        grid.Font = displayFont;
        grid.ColumnHeadersDefaultCellStyle.Font = displayFont;
        grid.DefaultCellStyle.Font = displayFont;
        hexTextBox.Font = displayFont;
        if (grid.EditingControl is Control editor) editor.Font = displayFont;
        zoomLabel.Text = $"{displayFont.SizeInPoints:0.#} pt（Ctrl＋ホイール）";
        RefreshGridSizing();
        SizeColumnsToFitData();
    }

    private void ChangeZoomFromWheel(int delta)
    {
        zoomWheelRemainder += delta;
        var steps = zoomWheelRemainder / SystemInformation.MouseWheelScrollDelta;
        zoomWheelRemainder %= SystemInformation.MouseWheelScrollDelta;
        if (steps == 0 || displayFont is null) return;
        var size = Math.Clamp(displayFont.SizeInPoints + steps, 6f, 48f);
        if (Math.Abs(size - displayFont.SizeInPoints) < 0.01f) return;
        var previous = displayFont;
        displayFont = new Font(previous.FontFamily, size, previous.Style, GraphicsUnit.Point);
        ApplyDisplayFont();
        previous.Dispose();
        try { SaveSettings(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetStatus("表示サイズを変更しました（設定を記憶できませんでした）。");
        }
    }

    private static Font CreateDefaultDisplayFont()
    {
        foreach (var fontName in new[] { "MS Gothic", "ＭＳ ゴシック" })
        {
            try
            {
                using var fontFamily = new FontFamily(fontName);
                if (fontFamily.IsStyleAvailable(FontStyle.Regular))
                {
                    return new Font(fontFamily, 12f, FontStyle.Regular);
                }
            }
            catch
            {
                // Try the next common family name, then fall back to generic monospace.
            }
        }

        return new Font(FontFamily.GenericMonospace, 12f, FontStyle.Regular);
    }

    private void RefreshGridSizing()
    {
        if (grid.Columns.Count == 0)
        {
            return;
        }

        grid.ColumnHeadersHeight = Math.Max(28, grid.Font.Height + 12);
        grid.RowTemplate.Height = Math.Max(22, grid.Font.Height + 8);
        foreach (DataGridViewRow row in grid.Rows)
        {
            row.Height = grid.RowTemplate.Height;
        }
        AdjustWrappedRowHeights();
        if (grid.RowHeadersVisible)
            grid.AutoResizeRowHeadersWidth(DataGridViewRowHeadersWidthSizeMode.AutoSizeToAllHeaders);
    }

    private void OpenDefinition()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "定義ファイル (*.csv;*.cbl;*.cpy)|*.csv;*.cbl;*.cpy|CSV ファイル (*.csv)|*.csv|COBOL コピー句 (*.cbl;*.cpy)|*.cbl;*.cpy|すべてのファイル (*.*)|*.*",
            Title = "定義ファイルを選択"
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        LoadDefinitionFromPath(dialog.FileName, "定義ファイルを読み込みました。");
    }

    private void OpenData()
    {
        if (fields.Count == 0)
        {
            MessageBox.Show(this, "先に定義ファイルを読み込んでください。", "データ読込", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dialog = new OpenFileDialog
        {
            Filter = "データファイル (*.dat;*.txt;*.csv)|*.dat;*.txt;*.csv|CSVデータ (*.csv)|*.csv|固定長データ (*.dat;*.txt)|*.dat;*.txt|すべてのファイル (*.*)|*.*",
            Title = "データファイルを選択"
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        LoadDataFromPath(dialog.FileName, "データファイルを読み込みました。");
    }

    private void LoadStartupFiles(string[] args)
    {
        var definitionPath = GetOptionValue(args, "--definition");
        if (string.IsNullOrWhiteSpace(definitionPath))
        {
            return;
        }

        try
        {
            SetDefinitionTextEncoding(DefinitionFileText.Parse(GetOptionValue(args, "--definition-encoding")));
            if (!LoadDefinition(definitionPath, GetOptionValue(args, "--xml-layout"))) return;
            records.Clear();
            records.Add(CreateEmptyRecord());

            var dataPath = GetOptionValue(args, "--data");
            if (!string.IsNullOrWhiteSpace(dataPath))
            {
                var separatorOption = GetOptionValue(args, "--separator");
                var separatorMode = separatorOption is null && currentXmlLayout is not null
                    ? currentSeparatorMode
                    : string.Equals(separatorOption, "none", StringComparison.OrdinalIgnoreCase)
                        ? RecordSeparatorMode.None
                        : RecordSeparatorMode.CrLfOrLf;
                var nationalTextEncoding = ParseNationalTextEncoding(GetOptionValue(args, "--national-encoding"))
                    ?? currentNationalTextEncoding;
                var format = GetOptionValue(args, "--format");
                if (string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase)
                    || (format is null && Path.GetExtension(dataPath).Equals(".csv", StringComparison.OrdinalIgnoreCase)))
                    LoadCsvData(dataPath, ParseCsvEncoding(GetOptionValue(args, "--csv-encoding")), ParseCsvHeader(GetOptionValue(args, "--csv-header")));
                else
                    LoadData(dataPath, separatorMode, nationalTextEncoding, ParseTextEncoding(GetOptionValue(args, "--text-encoding")));
            }

            var startupLayout = ParseStartupLayout(GetOptionValue(args, "--layout"));
            if (startupLayout is not null)
            {
                layout = startupLayout.Value;
            }

            RefreshGrid();
            SetStatus(string.IsNullOrWhiteSpace(dataPath)
                ? "定義ファイルを読み込みました。"
                : "定義ファイルとデータファイルを読み込みました。");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "起動時読込エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private bool LoadDefinition(string path, string? xmlLayoutId = null)
    {
        var definition = ReadDefinition(path, xmlLayoutId);
        if (definition is null) return false;
        var (loadedFields, xmlLayout) = definition.Value;
        // 読み込み・選択が完了してから現在の定義を置き換える。
        currentDataFileFormat = DataFileFormat.FixedLength;
        currentCsvHasHeader = false;
        currentDataEncodingProfile = xmlLayout?.EncodingProfile ?? (IsCopybookPath(path)
            ? DataEncodingProfile.Utf8WithNationalText
            : DataEncodingProfile.ShiftJis);
        if (xmlLayout is not null) currentSeparatorMode = xmlLayout.SeparatorMode;
        currentNationalTextEncoding = NationalTextEncoding.ShiftJis;
        ApplyDefinition(path, loadedFields, xmlLayout);
        AddRecentFile(recentDefinitionFiles, path);
        UpdateSeparatorModeText();
        return true;
    }

    private (IReadOnlyList<FieldDefinition> Fields, XmlLayoutDefinition? XmlLayout)? ReadDefinition(string path, string? xmlLayoutId)
    {
        XmlLayoutDefinition? xmlLayout = null;
        var fromCopybook = IsCopybookPath(path);
        IReadOnlyList<FieldDefinition> loadedFields;
        if (Path.GetExtension(path).Equals(".xml", StringComparison.OrdinalIgnoreCase))
        {
            var layouts = XmlDefinitionReader.Read(path);
            var supported = layouts.Where(layout => layout.IsSupported).ToList();
            if (xmlLayoutId is not null)
            {
                xmlLayout = layouts.FirstOrDefault(layout => layout.Id == xmlLayoutId)
                    ?? throw new InvalidDataException($"XMLレイアウトIDが見つかりません: {xmlLayoutId}");
                if (!xmlLayout.IsSupported) throw new InvalidDataException(xmlLayout.UnsupportedReason);
            }
            else if (supported.Count == 1)
            {
                xmlLayout = supported[0];
            }
            else if (supported.Count == 0)
            {
                throw new InvalidDataException("取り込み可能な固定長レイアウトがありません。\r\n" +
                    string.Join("\r\n", layouts.Select(layout => $"{layout.Name}: {layout.UnsupportedReason}")));
            }
            else
            {
                using var dialog = new XmlLayoutSelectionForm(layouts);
                if (dialog.ShowDialog(this) != DialogResult.OK) return null;
                xmlLayout = dialog.SelectedLayout!;
            }
            loadedFields = xmlLayout.Fields;
        }
        else
        {
            loadedFields = fromCopybook ? CopybookDefinitionReader.Read(path, definitionTextEncoding)
                : DefinitionCsvReader.Read(path, definitionTextEncoding);
        }

        return (loadedFields, xmlLayout);
    }

    private void ApplyDefinition(string path, IReadOnlyList<FieldDefinition> loadedFields, XmlLayoutDefinition? xmlLayout)
    {
        currentXmlLayout = xmlLayout;
        fields.Clear();
        fields.AddRange(loadedFields);
        hiddenFieldIndexes.Clear();
        SetPathText(definitionPathTextBox, path);
        xmlLayoutTextBox.Text = xmlLayout is null ? "—" : $"{xmlLayout.Name} [{xmlLayout.Id}] / {xmlLayout.RecordLength} バイト";
    }

    private void ReloadDefinition()
    {
        if (string.IsNullOrWhiteSpace(GetCurrentDefinitionPath()))
        {
            SetStatus("再読み込みする定義がありません。先に定義ファイルを開いてください。");
            return;
        }
        if (!grid.EndEdit()) return;
        try
        {
            if (ReloadDefinitionCore()) SetStatus("定義だけを再読み込みしました。表示中のデータは保持しています。");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "定義再読込エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private bool ReloadDefinitionCore()
    {
        var path = GetCurrentDefinitionPath();
        if (string.IsNullOrWhiteSpace(path) || path.StartsWith("(Excel) ", StringComparison.Ordinal))
            throw new InvalidOperationException("再読み込みできる定義ファイルがありません。Excelから取り込んだ場合は、全体再読込 (F5) を使ってください。");
        var definition = ReadDefinition(path, currentXmlLayout?.Id);
        if (definition is null) return false;
        var hasData = hasUnsavedChanges || !string.IsNullOrWhiteSpace(GetCurrentDataPath());
        var loadedFields = definition.Value.Fields.Select(field => hasData && field.Type == FieldDataType.FullWidthText
            ? field with { NationalByteWidth = NationalTextEncodingHelper.FixedByteWidth(currentNationalTextEncoding) } : field).ToList();
        if (hasData)
        {
            var compatible = loadedFields.Count == fields.Count;
            for (var index = 0; compatible && index < fields.Count; index++)
            {
                var previous = fields[index];
                var next = loadedFields[index];
                compatible = previous.Type == next.Type && previous.Length == next.Length
                    && previous.DecimalScale == next.DecimalScale && previous.NationalByteWidth == next.NationalByteWidth
                    && previous.RedefinesName == next.RedefinesName
                    && (previous.Name == next.Name || fields.All(field => field.Name != next.Name));
            }
            if (!compatible || currentXmlLayout?.EncodingProfile != definition.Value.XmlLayout?.EncodingProfile
                || currentXmlLayout?.SeparatorMode != definition.Value.XmlLayout?.SeparatorMode)
                throw new InvalidDataException("項目数・順序・型・桁数などが変わっているため、データを保持したまま定義だけを更新できません。\r\n編集中の場合は保存してから、全体再読込 (F5) で定義とデータを読み直してください。");
        }
        else
        {
            currentDataFileFormat = DataFileFormat.FixedLength;
            currentCsvHasHeader = false;
            currentDataEncodingProfile = definition.Value.XmlLayout?.EncodingProfile ?? (IsCopybookPath(path)
                ? DataEncodingProfile.Utf8WithNationalText : DataEncodingProfile.ShiftJis);
            currentNationalTextEncoding = NationalTextEncoding.ShiftJis;
            if (definition.Value.XmlLayout is not null) currentSeparatorMode = definition.Value.XmlLayout.SeparatorMode;
            records.Clear();
        }
        var hidden = hasData ? hiddenFieldIndexes.ToArray() : [];
        ApplyDefinition(path, loadedFields, definition.Value.XmlLayout);
        hiddenFieldIndexes.UnionWith(hidden);
        NormalizeRecords();
        UpdateSeparatorModeText();
        RefreshGrid();
        return true;
    }

    private void ReloadCurrentFiles()
    {
        if (string.IsNullOrWhiteSpace(GetCurrentDefinitionPath()) && string.IsNullOrWhiteSpace(GetCurrentDataPath()))
        {
            SetStatus("再読み込みするファイルがありません。先にファイルを開いてください。");
            return;
        }
        if (!ConfirmDiscardChanges("再読み込み")) return;
        try
        {
            ReloadCurrentFilesCore();
            SetStatus("現在の読込条件でファイルを再読み込みしました。");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "再読み込みエラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ReloadCurrentFilesCore()
    {
        var definitionPath = GetCurrentDefinitionPath();
        var dataPath = GetCurrentDataPath();
        var fromExcel = definitionPath?.StartsWith("(Excel) ", StringComparison.Ordinal) == true;
        if (fromExcel && saveDataPath is null)
        {
            var previousLayout = layout;
            var previousSingleRecordView = isSingleRecordView;
            var previousRecord = singleRecordIndex;
            var previousField = CurrentFieldIndex();
            LoadExcel(definitionPath![8..]);
            layout = previousLayout;
            isSingleRecordView = previousSingleRecordView;
            singleRecordIndex = previousRecord;
            RefreshGrid();
            if (isSingleRecordView) RestoreRecordFieldSelection(singleRecordIndex, previousField);
            return;
        }

        // 定義とデータを両方解析できるまでは、編集中のモデル・グリッドに触れない。
        var definition = !fromExcel && !string.IsNullOrWhiteSpace(definitionPath)
            ? ReadDefinition(definitionPath, currentXmlLayout?.Id) : null;
        var loadedFields = (definition?.Fields ?? fields).ToList();
        if (!IsCsv)
        {
            var width = NationalTextEncodingHelper.FixedByteWidth(currentNationalTextEncoding);
            loadedFields = loadedFields.Select(field => field.Type == FieldDataType.FullWidthText
                ? field with { NationalByteWidth = width } : field).ToList();
        }
        var loadedRecords = string.IsNullOrWhiteSpace(dataPath) ? null
            : IsCsv ? CsvDataReader.Read(dataPath, loadedFields, currentCsvEncoding, currentCsvHasHeader)
            : FixedLengthDataReader.Read(dataPath, loadedFields, currentSeparatorMode,
                currentDataEncodingProfile, currentNationalTextEncoding);

        grid.CancelEdit();
        if (definition is not null)
        {
            var hidden = fields.Select(field => field with { Comment = "" })
                .SequenceEqual(loadedFields.Select(field => field with { Comment = "" })) ? hiddenFieldIndexes.ToArray() : [];
            ApplyDefinition(definitionPath!, loadedFields, definition.Value.XmlLayout);
            hiddenFieldIndexes.UnionWith(hidden);
        }
        records.Clear();
        if (loadedRecords is not null) records.AddRange(loadedRecords);
        NormalizeRecords();
        UpdateSeparatorModeText();
        RefreshGrid();
        SetUnsavedChanges(false);
    }

    private static bool IsCopybookPath(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".cbl", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".cpy", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".cob", StringComparison.OrdinalIgnoreCase);
    }

    private void LoadData(string path, RecordSeparatorMode separatorMode, NationalTextEncoding nationalTextEncoding, DataEncodingProfile? textEncoding = null)
    {
        EnsureKnownSampleSelection(path, separatorMode, nationalTextEncoding);
        var loadedFields = BuildFieldsForNationalTextEncoding(nationalTextEncoding);
        var loadedRecords = FixedLengthDataReader.Read(path, loadedFields, separatorMode, textEncoding ?? currentDataEncodingProfile, nationalTextEncoding);
        currentDataEncodingProfile = textEncoding ?? currentDataEncodingProfile;
        ApplyNationalTextEncoding(nationalTextEncoding);
        currentDataFileFormat = DataFileFormat.FixedLength;
        currentCsvHasHeader = false;

        records.Clear();
        records.AddRange(loadedRecords);
        NormalizeRecords();
        currentSeparatorMode = separatorMode;
        saveDataPath = path;
        SetPathText(dataPathTextBox, path);
        AddRecentFile(recentDataFiles, path);
        UpdateSeparatorModeText();
        SetUnsavedChanges(false);
    }

    private void ApplyNationalTextEncoding(NationalTextEncoding nationalTextEncoding)
    {
        currentNationalTextEncoding = nationalTextEncoding;
        var byteWidth = NationalTextEncodingHelper.FixedByteWidth(nationalTextEncoding);
        for (var index = 0; index < fields.Count; index++)
        {
            if (fields[index].Type == FieldDataType.FullWidthText)
            {
                fields[index] = fields[index] with { NationalByteWidth = byteWidth };
            }
        }
    }

    private void LoadCsvData(string path, CsvTextEncoding encoding, bool hasHeader = false)
    {
        // 解析成功までは現在のデータ・保存先・固定長の文字コードを変更しない。
        var loadedRecords = CsvDataReader.Read(path, fields, encoding, hasHeader);
        records.Clear();
        records.AddRange(loadedRecords);
        NormalizeRecords();
        currentDataFileFormat = DataFileFormat.Csv;
        currentCsvEncoding = encoding;
        currentCsvHasHeader = hasHeader;
        saveDataPath = path;
        SetPathText(dataPathTextBox, path);
        AddRecentFile(recentDataFiles, path);
        UpdateSeparatorModeText();
        SetUnsavedChanges(false);
    }

    private void EnsureKnownSampleSelection(string dataPath, RecordSeparatorMode separatorMode, NationalTextEncoding nationalTextEncoding)
    {
        var samplePattern = FindCopybookSamplePattern(dataPath);
        if (samplePattern is null)
        {
            return;
        }

        var definitionFileName = Path.GetFileName(GetCurrentDefinitionPath());
        var required = new List<string>();
        if (!samplePattern.MatchesDefinition(definitionFileName))
        {
            required.Add($"定義={samplePattern.DefinitionFileName}");
        }

        if (separatorMode != samplePattern.SeparatorMode)
        {
            required.Add($"改行={FormatSeparatorMode(samplePattern.SeparatorMode)}");
        }

        if (nationalTextEncoding != samplePattern.NationalTextEncoding)
        {
            required.Add($"型N={NationalTextEncodingHelper.DisplayName(samplePattern.NationalTextEncoding)}");
        }

        if (required.Count == 0)
        {
            return;
        }

        throw new InvalidOperationException(
            $"{samplePattern.FileName} は {samplePattern.DefinitionFileName} + {FormatSeparatorMode(samplePattern.SeparatorMode)} + 型N={NationalTextEncodingHelper.DisplayName(samplePattern.NationalTextEncoding)} 用です。\r\n" +
            "既存サンプルと追加サンプルの対応パターン以外は、エラーまたは文字化けになります。\r\n" +
            $"必要な選択: {string.Join(" / ", required)}");
    }

    private static void SetPathText(TextBox textBox, string path)
    {
        textBox.Text = path;
        textBox.SelectionStart = textBox.TextLength;
        textBox.SelectionLength = 0;
    }

    private bool LoadDefinitionFromPath(string path, string statusMessage)
    {
        if (!ConfirmDiscardChanges("定義を開く")) return false;
        try
        {
            if (!LoadDefinition(path)) return false;
            ClearPathText(dataPathTextBox);
            saveDataPath = null;
            records.Clear();
            records.Add(CreateEmptyRecord());
            RefreshGrid();
            SetUnsavedChanges(false);
            SetStatus(statusMessage);
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "定義読込エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    private void LoadDataFromPath(string path, string statusMessage)
    {
        if (fields.Count == 0)
        {
            MessageBox.Show(this, "先に定義ファイルを読み込んでください。", "データ読込", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (!ConfirmDiscardChanges("データを開く")) return;
        try
        {
            var loadOptions = SelectDataLoadOptions(path);
            if (loadOptions is null)
            {
                return;
            }

            if (loadOptions.IsCsv)
                LoadCsvData(path, loadOptions.CsvEncoding, loadOptions.CsvHasHeader);
            else
                LoadData(path, loadOptions.SeparatorMode, loadOptions.NationalTextEncoding, loadOptions.TextEncoding);
            RefreshGrid();
            SetStatus(RememberCurrentLoadSettings(path) ? statusMessage : statusMessage + "（読込条件を記憶できませんでした。）");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "データ読込エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void LoadRecentFiles()
    {
        if (!File.Exists(RecentFilesPath))
        {
            return;
        }

        foreach (var line in File.ReadAllLines(RecentFilesPath, Encoding.UTF8))
        {
            if (line.Length < 3 || line[1] != '\t')
            {
                continue;
            }

            var path = line[2..];
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            if (line[0] == 'D')
            {
                recentDefinitionFiles.Add(path);
            }
            else if (line[0] == 'F')
            {
                recentDataFiles.Add(path);
            }
        }
    }

    private void AddRecentFile(List<string> target, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var existingIndex = target.FindIndex(item => string.Equals(item, path, StringComparison.OrdinalIgnoreCase));
        if (existingIndex >= 0)
        {
            target.RemoveAt(existingIndex);
        }

        target.Insert(0, path);
        if (target.Count > 20)
        {
            target.RemoveRange(20, target.Count - 20);
        }

        SaveRecentFiles();
        RefreshRecentFileItems();
    }

    private void SaveRecentFiles()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(RecentFilesPath)!);
        var lines = recentDefinitionFiles
            .Select(path => $"D\t{path}")
            .Concat(recentDataFiles.Select(path => $"F\t{path}"));
        File.WriteAllLines(RecentFilesPath, lines, Encoding.UTF8);
    }

    private void LoadSettings()
    {
        if (!File.Exists(SettingsPath))
        {
            return;
        }

        var settings = File.ReadAllLines(SettingsPath, Encoding.UTF8)
            .Select(line => line.Split('\t', 2))
            .Where(parts => parts.Length == 2)
            .ToDictionary(parts => parts[0], parts => parts[1]);

        if (!settings.TryGetValue("FontName", out var fontName)
            || !settings.TryGetValue("FontSize", out var fontSizeText)
            || !float.TryParse(fontSizeText, NumberStyles.Float, CultureInfo.InvariantCulture, out var fontSize)
            || fontSize <= 0)
        {
            return;
        }

        try
        {
            displayFont = new Font(fontName, fontSize, FontStyle.Regular);
        }
        catch
        {
            displayFont = null;
        }
    }

    private void SaveSettings()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        var lines = displayFont is null
            ? []
            : new[]
            {
                $"FontName\t{displayFont.Name}",
                $"FontSize\t{displayFont.SizeInPoints.ToString(CultureInfo.InvariantCulture)}"
            };
        File.WriteAllLines(SettingsPath, lines, Encoding.UTF8);
    }

    private void RefreshRecentFileItems()
    {
        PopulateRecentMenu(
            recentDefinitionFilesMenu,
            recentDefinitionFiles,
            path => LoadDefinitionFromPath(path, "履歴から定義ファイルを読み込みました。"));
        PopulateRecentMenu(
            recentDataFilesMenu,
            recentDataFiles,
            path => LoadDataFromPath(path, "履歴からデータファイルを読み込みました。"));
    }

    private static void PopulateRecentMenu(ToolStripMenuItem menu, IReadOnlyList<string> files, Action<string> onClick)
    {
        menu.DropDownItems.Clear();
        if (files.Count == 0)
        {
            menu.DropDownItems.Add(new ToolStripMenuItem("履歴なし") { Enabled = false });
            return;
        }

        for (var i = 0; i < files.Count; i++)
        {
            var path = files[i];
            var item = new ToolStripMenuItem($"{i + 1}. {path}") { ToolTipText = path };
            item.Click += (_, _) => onClick(path);
            menu.DropDownItems.Add(item);

            if (i == 8)
            {
                menu.DropDownItems.Add(new ToolStripSeparator());
            }
        }
    }

    private static void ClearPathText(TextBox textBox)
    {
        textBox.Clear();
    }

    private static string? GetOptionValue(string[] args, string optionName)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], optionName, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }

    private static GridLayout? ParseStartupLayout(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().ToLowerInvariant().Replace("_", "-") switch
        {
            "record" or "record-rows" => GridLayout.RecordRows,
            "field" or "field-rows" => GridLayout.FieldRows,
            _ => null
        };
    }

    private static bool ParseCsvHeader(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        null or "none" or "false" => false,
        "present" or "true" => true,
        _ => throw new InvalidDataException("CSVヘッダーはpresent（あり）またはnone（なし）を指定してください。")
    };

    private static CsvTextEncoding ParseCsvEncoding(string? value) => NormalizeEncoding(value) switch
    {
        null or "utf8" => CsvTextEncoding.Utf8,
        "sjis" or "shiftjis" => CsvTextEncoding.StrictShiftJis,
        "ms932" or "cp932" or "windows31j" => CsvTextEncoding.ShiftJis,
        _ => throw new InvalidDataException("CSV文字コードはUTF-8・SJIS・MS932から指定してください。")
    };

    private static DataEncodingProfile? ParseTextEncoding(string? value) => NormalizeEncoding(value) switch
    {
        null => null, "utf8" => DataEncodingProfile.Utf8WithNationalText,
        "sjis" or "shiftjis" => DataEncodingProfile.StrictShiftJis,
        "ms932" or "cp932" or "windows31j" => DataEncodingProfile.ShiftJis,
        _ => throw new InvalidDataException("X文字コードはUTF-8・SJIS・MS932から指定してください。")
    };
    private static string? NormalizeEncoding(string? value) => value?.Trim().ToLowerInvariant().Replace("_", "").Replace("-", "");

    private static NationalTextEncoding? ParseNationalTextEncoding(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().ToLowerInvariant().Replace("_", "").Replace("-", "") switch
        {
            "ms932" or "cp932" or "windows31j" => NationalTextEncoding.ShiftJis,
            "shiftjis" or "sjis" => NationalTextEncoding.StrictShiftJis,
            "utf16" or "utf16le" => NationalTextEncoding.Utf16,
            "utf32" or "utf32le" => NationalTextEncoding.Utf32,
            _ => null
        };
    }

    private DataLoadOptions? SelectDataLoadOptions(string dataPath)
    {
        var remembered = loadSettingsStore.Find(GetCurrentDefinitionPath() ?? string.Empty, dataPath, currentXmlLayout?.Id);
        var samplePattern = FindCopybookSamplePattern(dataPath);
        var canUseSampleDefaults = samplePattern?.MatchesDefinition(Path.GetFileName(GetCurrentDefinitionPath())) == true;
        var defaultSeparatorMode = remembered?.SeparatorMode ?? (canUseSampleDefaults
            ? samplePattern!.SeparatorMode
            : currentSeparatorMode);
        var defaultNationalTextEncoding = remembered?.NationalEncoding ?? (canUseSampleDefaults
            ? samplePattern!.NationalTextEncoding
            : currentNationalTextEncoding);

        using var form = new Form
        {
            Text = "データ読込条件",
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.Sizable,
            MinimizeBox = false,
            MaximizeBox = true,
            ClientSize = new Size(760, 586),
            MinimumSize = new Size(680, 546)
        };

        var formatLabel = new Label { Text = "入力形式", AutoSize = true, Location = new Point(16, 20) };
        var formatCombo = new ComboBox
        {
            Name = "DataFormat", DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(90, 16), Width = 245
        };
        formatCombo.Items.AddRange(["固定長", "可変長 CSV（ヘッダーなし）", "可変長 CSV（ヘッダーあり）"]);
        formatCombo.SelectedIndex = remembered is not null ? (remembered.Format == DataFileFormat.Csv ? (remembered.CsvHasHeader ? 2 : 1) : 0)
            : Path.GetExtension(dataPath).Equals(".csv", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        var csvEncodingLabel = new Label { Text = "CSV文字コード", AutoSize = true, Location = new Point(380, 20) };
        var csvEncodingCombo = new ComboBox
        {
            Name = "CsvEncoding", DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(500, 16), Width = 155
        };
        csvEncodingCombo.Items.AddRange(JapaneseEncodings.CsvNames);
        csvEncodingCombo.SelectedIndex = (int)(remembered?.CsvEncoding ?? CsvTextEncoding.Utf8);

        var separatorGroup = new GroupBox
        {
            Text = "1. レコード区切り",
            Location = new Point(16, 60),
            Size = new Size(340, 126),
            Anchor = AnchorStyles.Top | AnchorStyles.Left
        };

        var lineBreakRadio = new RadioButton
        {
            Text = "改行あり (CRLF/LF)",
            Checked = defaultSeparatorMode == RecordSeparatorMode.CrLfOrLf,
            Location = new Point(14, 24),
            AutoSize = true
        };
        var noLineBreakRadio = new RadioButton
        {
            Text = "改行なし",
            Checked = defaultSeparatorMode == RecordSeparatorMode.None,
            Location = new Point(14, 52),
            AutoSize = true
        };
        separatorGroup.Controls.AddRange([lineBreakRadio, noLineBreakRadio]);
        var textEncodingLabel = new Label { Text = "X文字コード", AutoSize = true, Location = new Point(14, 88) };
        var textEncodingCombo = new ComboBox { Name = "TextEncoding", DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(110, 83), Width = 215 };
        textEncodingCombo.Items.AddRange(JapaneseEncodings.TextNames);
        textEncodingCombo.SelectedIndex = (int)(remembered?.TextEncoding ?? currentDataEncodingProfile);
        separatorGroup.Controls.AddRange([textEncodingLabel, textEncodingCombo]);

        var nationalEncodingGroup = new GroupBox
        {
            Text = "2. 型N 文字コード",
            Location = new Point(380, 60),
            Size = new Size(340, 126),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };

        var options = new[]
        {
            (Encoding: NationalTextEncoding.ShiftJis, Text: "MS932 (CP932)"),
            (Encoding: NationalTextEncoding.Utf16, Text: "UTF-16LE"),
            (Encoding: NationalTextEncoding.Utf32, Text: "UTF-32LE"),
            (Encoding: NationalTextEncoding.StrictShiftJis, Text: "SJIS (JIS X 0208)")
        };
        var buttons = new List<RadioButton>();
        for (var index = 0; index < options.Length; index++)
        {
            var option = options[index];
            var radioButton = new RadioButton
            {
                Text = option.Text,
                Tag = option.Encoding,
                Checked = defaultNationalTextEncoding == option.Encoding,
                Location = new Point(14, 24 + (index * 24)),
                AutoSize = true
            };
            buttons.Add(radioButton);
            nationalEncodingGroup.Controls.Add(radioButton);
        }

        if (!buttons.Any(button => button.Checked))
        {
            buttons[0].Checked = true;
        }

        var okButton = new Button
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            Location = new Point(580, 540),
            Width = 75,
            Anchor = AnchorStyles.Right | AnchorStyles.Bottom
        };
        var cancelButton = new Button
        {
            Text = "キャンセル",
            DialogResult = DialogResult.Cancel,
            Location = new Point(664, 540),
            Width = 75,
            Anchor = AnchorStyles.Right | AnchorStyles.Bottom
        };
        var previewButton = new Button
        {
            Text = "プレビュー更新",
            Location = new Point(16, 540),
            Width = 120,
            Anchor = AnchorStyles.Left | AnchorStyles.Bottom
        };
        var encodingHelp = new Label
        {
            Name = "EncodingHelp",
            Text = "半角の英字・数字はUTF-8・SJIS・MS932で同じです。\r\nSJISは拡張文字なし。①・髙などを含む場合はMS932。",
            Location = new Point(16, 194),
            Size = new Size(724, 40),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        var previewLabel = new Label
        {
            Text = "プレビュー（先頭5レコード）",
            Location = new Point(16, 242),
            AutoSize = true
        };
        var previewStatusLabel = new Label
        {
            Location = new Point(190, 242),
            Size = new Size(550, 20),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            TextAlign = ContentAlignment.MiddleLeft
        };
        var previewGrid = new DataGridView
        {
            Location = new Point(16, 264),
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.CellSelect,
            MultiSelect = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
            Size = new Size(724, 260),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            Font = displayFont ?? CreateDefaultDisplayFont(),
            BackgroundColor = SystemColors.AppWorkspace
        };

        DataLoadOptions CurrentOptions()
        {
            var separatorMode = noLineBreakRadio.Checked
                ? RecordSeparatorMode.None
                : RecordSeparatorMode.CrLfOrLf;
            var nationalTextEncoding = buttons.First(button => button.Checked).Tag is NationalTextEncoding encoding
                ? encoding
                : NationalTextEncoding.ShiftJis;
            return new DataLoadOptions(separatorMode, nationalTextEncoding, formatCombo.SelectedIndex > 0,
                (CsvTextEncoding)csvEncodingCombo.SelectedIndex, (DataEncodingProfile)textEncodingCombo.SelectedIndex, formatCombo.SelectedIndex == 2);
        }

        void UpdatePreview()
        {
            try
            {
                var options = CurrentOptions();
                separatorGroup.Enabled = nationalEncodingGroup.Enabled = !options.IsCsv;
                csvEncodingLabel.Enabled = csvEncodingCombo.Enabled = options.IsCsv;
                if (!options.IsCsv)
                    EnsureKnownSampleSelection(dataPath, options.SeparatorMode, options.NationalTextEncoding);
                var previewFields = options.IsCsv ? fields.ToList() : BuildFieldsForNationalTextEncoding(options.NationalTextEncoding);
                var previewRecords = options.IsCsv
                    ? CsvDataReader.Read(dataPath, previewFields, options.CsvEncoding, options.CsvHasHeader)
                    : FixedLengthDataReader.Read(dataPath, previewFields, options.SeparatorMode,
                        options.TextEncoding ?? currentDataEncodingProfile, options.NationalTextEncoding);
                ShowDataPreview(previewGrid, previewStatusLabel, previewFields, previewRecords, options.IsCsv);
                if (options.IsCsv && options.CsvHasHeader) previewStatusLabel.Text += "（先頭行はヘッダー）";
                if (remembered is not null) previewStatusLabel.Text += "（前回の読込条件）";
                okButton.Enabled = true;
            }
            catch (Exception ex)
            {
                ShowDataPreviewError(previewGrid, previewStatusLabel, ex.Message);
                okButton.Enabled = false;
            }
        }

        previewButton.Click += (_, _) => UpdatePreview();
        formatCombo.SelectedIndexChanged += (_, _) => UpdatePreview();
        csvEncodingCombo.SelectedIndexChanged += (_, _) => UpdatePreview();
        textEncodingCombo.SelectedIndexChanged += (_, _) => UpdatePreview();
        foreach (var radioButton in buttons.Concat([lineBreakRadio, noLineBreakRadio]))
        {
            radioButton.CheckedChanged += (_, _) =>
            {
                if (((RadioButton)radioButton).Checked)
                {
                    UpdatePreview();
                }
            };
        }

        form.Controls.AddRange([formatLabel, formatCombo, csvEncodingLabel, csvEncodingCombo,
            separatorGroup, nationalEncodingGroup, encodingHelp, previewLabel, previewStatusLabel, previewGrid, previewButton, okButton, cancelButton]);
        form.AcceptButton = okButton;
        form.CancelButton = cancelButton;
        UpdatePreview();

        if (form.ShowDialog(this) != DialogResult.OK)
        {
            return null;
        }

        return CurrentOptions();
    }

    private List<FieldDefinition> BuildFieldsForNationalTextEncoding(NationalTextEncoding nationalTextEncoding)
    {
        var byteWidth = NationalTextEncodingHelper.FixedByteWidth(nationalTextEncoding);
        return fields
            .Select(field => field.Type == FieldDataType.FullWidthText
                ? field with { NationalByteWidth = byteWidth }
                : field)
            .ToList();
    }

    private static void ShowDataPreview(
        DataGridView previewGrid,
        Label previewStatusLabel,
        IReadOnlyList<FieldDefinition> previewFields,
        IReadOnlyList<List<string>> previewRecords,
        bool isCsv = false)
    {
        previewGrid.Columns.Clear();
        previewGrid.Rows.Clear();
        previewStatusLabel.ForeColor = SystemColors.ControlText;

        if (previewRecords.Count == 0)
        {
            previewStatusLabel.Text = "レコードがありません。";
            return;
        }

        var displayFieldIndexes = previewFields
            .Select((field, index) => (field, index))
            .Where(item => !item.field.IsRedefines)
            .Take(8)
            .Select(item => item.index)
            .ToList();

        previewGrid.Columns.Add(CreateReadOnlyColumn("Record", "行", 50, frozen: false));
        foreach (var fieldIndex in displayFieldIndexes)
        {
            previewGrid.Columns.Add(CreateReadOnlyColumn($"Field{fieldIndex}", previewFields[fieldIndex].Name, 120, frozen: false));
        }

        for (var recordIndex = 0; recordIndex < previewRecords.Take(5).Count(); recordIndex++)
        {
            var record = previewRecords[recordIndex];
            var rowIndex = previewGrid.Rows.Add();
            var row = previewGrid.Rows[rowIndex];
            row.Cells[0].Value = (recordIndex + 1).ToString(CultureInfo.InvariantCulture);
            for (var visibleIndex = 0; visibleIndex < displayFieldIndexes.Count; visibleIndex++)
            {
                var fieldIndex = displayFieldIndexes[visibleIndex];
                row.Cells[visibleIndex + 1].Value = FormatValueForDisplay(previewFields[fieldIndex], record[fieldIndex], isCsv);
            }
        }

        var omittedRecordText = previewRecords.Count > 5
            ? $" / 他 {previewRecords.Count - 5} レコードあり"
            : string.Empty;
        var omittedFieldText = previewFields.Count(field => !field.IsRedefines) > displayFieldIndexes.Count
            ? " / 表示は先頭8項目まで"
            : string.Empty;
        previewStatusLabel.Text = $"読込可能: {previewRecords.Count} レコード{omittedRecordText}{omittedFieldText}";
    }

    private static void ShowDataPreviewError(DataGridView previewGrid, Label previewStatusLabel, string message)
    {
        previewGrid.Columns.Clear();
        previewGrid.Rows.Clear();
        previewStatusLabel.ForeColor = Color.Firebrick;
        previewStatusLabel.Text = $"プレビューエラー: {message}";
    }

    private List<string> CreateEmptyRecord()
    {
        return fields.Select(field => IsCsv && !field.IsRedefines ? string.Empty : field.Type switch
        {
            FieldDataType.PlainNumber or FieldDataType.SignedNumber or FieldDataType.PackedUnsigned or FieldDataType.PackedSigned => "0",
            FieldDataType.FullWidthText => new string('\u3000', field.Length),
            _ => string.Empty
        }).ToList();
    }

    private void CreateDefinition(bool editCurrent)
    {
        if (editCurrent && fields.Count == 0)
        {
            MessageBox.Show(this, "修正する定義ファイルを先に読み込んでください。", "定義修正", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (!ConfirmDiscardChanges("定義を編集")) return;
        using var form = new DefinitionEditorForm(editCurrent ? fields : [], InitialDefinitionSaveEncoding(editCurrent));
        if (form.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(form.SavedPath))
        {
            return;
        }

        var previousDefinitionEncoding = definitionTextEncoding;
        try
        {
            SetDefinitionTextEncoding(form.SavedEncoding);
            if (!LoadDefinition(form.SavedPath)) return;
            ClearPathText(dataPathTextBox);
            saveDataPath = null;
            records.Clear();
            records.Add(CreateEmptyRecord());
            RefreshGrid();
            SetUnsavedChanges(false);
            SetStatus(editCurrent ? "定義ファイルを修正して読み込みました。" : "定義ファイルを作成して読み込みました。");
        }
        catch (Exception ex)
        {
            SetDefinitionTextEncoding(previousDefinitionEncoding);
            MessageBox.Show(this, ex.Message, "定義読込エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void NormalizeRecords()
    {
        if (fields.Count == 0)
        {
            records.Clear();
            return;
        }

        if (records.Count == 0)
        {
            records.Add(CreateEmptyRecord());
            return;
        }

        foreach (var record in records)
        {
            while (record.Count < fields.Count)
            {
                var field = fields[record.Count];
                record.Add(IsCsv && !field.IsRedefines ? string.Empty : field.Type switch
                {
                    FieldDataType.PlainNumber or FieldDataType.SignedNumber or FieldDataType.PackedUnsigned or FieldDataType.PackedSigned => "0",
                    FieldDataType.FullWidthText => new string('\u3000', field.Length),
                    _ => string.Empty
                });
            }

            if (record.Count > fields.Count)
            {
                record.RemoveRange(fields.Count, record.Count - fields.Count);
            }
        }
    }

    private void AddRecord()
    {
        if (fields.Count == 0)
        {
            return;
        }

        records.Add(CreateEmptyRecord());
        SetUnsavedChanges(true);
        RefreshGrid();
        SelectRecord(records.Count - 1);
    }

    private void DuplicateRecord()
    {
        var index = CurrentRecordIndex();
        if (index < 0)
        {
            return;
        }

        records.Insert(index + 1, [.. records[index]]);
        SetUnsavedChanges(true);
        RefreshGrid();
        SelectRecord(index + 1);
    }

    private void DeleteRecord()
    {
        var index = CurrentRecordIndex();
        if (index < 0 || records.Count <= 1)
        {
            return;
        }

        records.RemoveAt(index);
        SetUnsavedChanges(true);
        RefreshGrid();
        SelectRecord(Math.Min(index, records.Count - 1));
    }

    private bool SaveDataOverwrite()
    {
        grid.EndEdit();
        if (string.IsNullOrWhiteSpace(saveDataPath))
        {
            return SaveDataAs();
        }

        try
        {
            WriteData(saveDataPath, CurrentSaveOptions());
            SetStatus($"上書き保存しました: {Path.GetFileName(saveDataPath)}");
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "保存エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    private bool SaveDataAs()
    {
        grid.EndEdit();
        if (fields.Count == 0 || records.Count == 0)
        {
            MessageBox.Show(this, "保存するデータがありません。", "保存", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }

        using var optionsForm = new DataSaveOptionsForm(CurrentSaveOptions());
        if (optionsForm.ShowDialog(this) != DialogResult.OK) return false;
        var options = optionsForm.Options;
        var isCsv = options.Format == DataFileFormat.Csv;

        using var dialog = new SaveFileDialog
        {
            Filter = isCsv ? "CSVデータ (*.csv)|*.csv" : "固定長データ (*.dat)|*.dat|テキスト (*.txt)|*.txt|すべてのファイル (*.*)|*.*",
            Title = isCsv ? "CSVデータを保存" : "固定長データを保存",
            DefaultExt = isCsv ? "csv" : "dat",
            FileName = string.IsNullOrWhiteSpace(saveDataPath) ? (isCsv ? "sample.csv" : "sample.dat")
                : options.Format == currentDataFileFormat ? Path.GetFileName(saveDataPath)
                : Path.ChangeExtension(Path.GetFileName(saveDataPath), isCsv ? ".csv" : ".dat")
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return false;
        }

        try
        {
            WriteData(dialog.FileName, options);
            SetStatus($"保存しました: {Path.GetFileName(dialog.FileName)}");
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "保存エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    private DataSaveOptions CurrentSaveOptions() => new(currentDataFileFormat, currentCsvEncoding,
        currentSeparatorMode, currentNationalTextEncoding, currentDataEncodingProfile, currentCsvHasHeader);

    private void WriteData(string path, DataSaveOptions options)
    {
        var refreshNeeded = options.Format != currentDataFileFormat
            || (isSingleRecordView && options.Format == DataFileFormat.Csv && options.CsvEncoding != currentCsvEncoding)
            || (options.Format == DataFileFormat.FixedLength && (options.NationalEncoding != currentNationalTextEncoding
                || (options.TextEncoding ?? currentDataEncodingProfile) != currentDataEncodingProfile));
        if (options.Format == DataFileFormat.Csv)
        {
            CsvDataWriter.Write(path, fields, records, options.CsvEncoding, options.CsvHasHeader);
        }
        else
        {
            var outputFields = BuildFieldsForNationalTextEncoding(options.NationalEncoding);
            var structuralErrors = ValidateStructureBeforeSave(outputFields, options.NationalEncoding, options.TextEncoding);
            if (structuralErrors.Count > 0) throw new InvalidDataException(string.Join(Environment.NewLine, structuralErrors.Take(20)));
            var errors = RecordValidator.Validate(outputFields, records, options.TextEncoding ?? currentDataEncodingProfile, options.NationalEncoding);
            if (errors.Count > 0) throw new InvalidDataException(string.Join(Environment.NewLine, errors.Take(20)));
            FixedLengthDataWriter.Write(path, outputFields, records, options.SeparatorMode,
                options.TextEncoding ?? currentDataEncodingProfile, options.NationalEncoding);
            currentDataEncodingProfile = options.TextEncoding ?? currentDataEncodingProfile;
            ApplyNationalTextEncoding(options.NationalEncoding);
            currentSeparatorMode = options.SeparatorMode;
        }
        currentDataFileFormat = options.Format;
        currentCsvEncoding = options.CsvEncoding;
        currentCsvHasHeader = options.Format == DataFileFormat.Csv && options.CsvHasHeader;
        saveDataPath = path;
        SetPathText(dataPathTextBox, path);
        AddRecentFile(recentDataFiles, path);
        UpdateSeparatorModeText();
        if (refreshNeeded) RefreshGrid();
        else UpdateHexView();
        SetUnsavedChanges(false);
        RememberCurrentLoadSettings(path);
    }

    private void ExportExcel()
    {
        grid.EndEdit();
        if (fields.Count == 0 || records.Count == 0)
        {
            MessageBox.Show(this, "出力するデータがありません。", "Excel出力", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var layoutName = layout == GridLayout.FieldRows ? "項目縦" : "レコード縦";
        using var dialog = new SaveFileDialog
        {
            Filter = "Excel ブック (*.xlsx)|*.xlsx",
            Title = "Excelへ出力",
            FileName = BuildExcelFileName(layoutName)
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            ExcelExporter.Write(dialog.FileName, layoutName, BuildExcelRows());
            OpenGeneratedFile(dialog.FileName);
            SetStatus($"Excelへ出力しました: {Path.GetFileName(dialog.FileName)}");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Excel出力エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private string BuildExcelFileName(string layoutName)
    {
        var sourcePath = GetCurrentDataPath();
        if (string.IsNullOrWhiteSpace(sourcePath)) sourcePath = GetCurrentDefinitionPath();
        var sourceName = Path.GetFileNameWithoutExtension(sourcePath);
        var prefix = string.IsNullOrWhiteSpace(sourceName) ? string.Empty : $"{sourceName}_";
        return $"{prefix}FixedDataBuilder_{layoutName}.xlsx";
    }

    private void ImportExcel()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "Excel ブック (*.xlsx)|*.xlsx",
            Title = "Excelから取り込み"
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        ImportExcelFromPath(dialog.FileName);
    }

    private void ImportExcelFromPath(string path)
    {
        if (!ConfirmDiscardChanges("Excelを取り込む")) return;
        try
        {
            LoadExcel(path);
            ValidateRecords(showSuccess: false);
            SetStatus($"Excelから取り込みました: {Path.GetFileName(path)}");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Excel取込エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void LoadExcel(string path)
    {
        var imported = ExcelImporter.Read(path);
        ValidateImportedDefinition(imported.Fields);
        currentDataFileFormat = DataFileFormat.FixedLength;
        currentCsvHasHeader = false;
        // 読み込み済みXMLと同じ項目構造のExcelなら、文字コードと区切りを継承する。
        if (currentXmlLayout is null)
        {
            currentDataEncodingProfile = DataEncodingProfile.ShiftJis;
            currentNationalTextEncoding = NationalTextEncoding.ShiftJis;
        }
        fields.Clear();
        fields.AddRange(imported.Fields);
        hiddenFieldIndexes.Clear();
        records.Clear();
        records.AddRange(imported.Records.Select(record => record.ToList()));
        saveDataPath = null;
        SetPathText(definitionPathTextBox, $"(Excel) {path}");
        SetPathText(dataPathTextBox, path);
        layout = imported.LayoutName == "レコード縦" ? GridLayout.RecordRows : GridLayout.FieldRows;
        isSingleRecordView = false;
        NormalizeRecords();
        UpdateSeparatorModeText();
        RefreshGrid();
        SetUnsavedChanges(false);
    }

    private void ValidateImportedDefinition(IReadOnlyList<FieldDefinition> importedFields)
    {
        if (fields.Count == 0)
        {
            return;
        }

        if (fields.Count != importedFields.Count)
        {
            throw new InvalidDataException($"現在の定義項目数 {fields.Count} と Excel の定義項目数 {importedFields.Count} が一致しません。");
        }

        for (var index = 0; index < fields.Count; index++)
        {
            var current = fields[index];
            var imported = importedFields[index];
            if (!string.Equals(current.Name, imported.Name, StringComparison.Ordinal)
                || !string.Equals(current.DisplayDefinition, imported.DisplayDefinition, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"Excel の定義が現在の定義と一致しません。{Environment.NewLine}"
                    + $"{index + 1} 番目: 現在={current.Name},{current.DisplayDefinition} / Excel={imported.Name},{imported.DisplayDefinition}");
            }
        }
    }

    private IReadOnlyList<IReadOnlyList<ExcelCell>> BuildExcelRows()
    {
        if (isSingleRecordView) return BuildSingleRecordExcelRows();
        var rows = new List<IReadOnlyList<ExcelCell>>();
        rows.Add(grid.Columns
            .Cast<DataGridViewColumn>()
            .Select(column => new ExcelCell(column.HeaderText, ExcelCellKind.Header))
            .ToList());

        foreach (DataGridViewRow gridRow in grid.Rows)
        {
            if (gridRow.IsNewRow)
            {
                continue;
            }

            var row = new List<ExcelCell>();
            for (var columnIndex = 0; columnIndex < grid.Columns.Count; columnIndex++)
            {
                var kind = layout == GridLayout.RecordRows && gridRow.Index < 2
                    ? ExcelCellKind.Header
                    : IsDefinitionCellForExcel(columnIndex) ? ExcelCellKind.Definition : ExcelCellKind.Normal;
                row.Add(new ExcelCell(gridRow.Cells[columnIndex].Value?.ToString() ?? string.Empty, kind));
            }

            rows.Add(row);
        }

        return rows;
    }

    private bool IsDefinitionCellForExcel(int columnIndex)
    {
        return layout == GridLayout.FieldRows
            ? columnIndex < 2
            : columnIndex == 0 || FieldIndexFromRecordColumn(columnIndex) < 0;
    }

    private static void OpenGeneratedFile(string path)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
    }

    private IReadOnlyList<string> ValidateStructureBeforeSave(IReadOnlyList<FieldDefinition> outputFields,
        NationalTextEncoding nationalEncoding, DataEncodingProfile? textEncoding = null)
    {
        var errors = new List<string>();
        if (outputFields.Count == 0)
        {
            errors.Add("定義ファイルが読み込まれていません。");
        }
        if (records.Count == 0)
        {
            errors.Add("保存するレコードがありません。");
        }

        for (var recordIndex = 0; recordIndex < records.Count; recordIndex++)
        {
            if (records[recordIndex].Count != outputFields.Count)
            {
                errors.Add($"Rec {recordIndex + 1}: 定義項目数 {outputFields.Count} とデータ項目数 {records[recordIndex].Count} が一致しません。");
                continue;
            }

            try
            {
                FixedLengthDataWriter.EncodeRecord(outputFields, records[recordIndex], textEncoding ?? currentDataEncodingProfile, nationalEncoding);
            }
            catch (Exception ex)
            {
                errors.Add($"Rec {recordIndex + 1}: {ex.Message}");
            }
        }

        return errors;
    }

    private bool ValidateRecords(bool showSuccess)
    {
        grid.EndEdit();
        IReadOnlyList<string> errors;
        if (IsCsv)
        {
            try { CsvDataWriter.Encode(fields, records, currentCsvEncoding, currentCsvHasHeader); errors = []; }
            catch (Exception ex) { errors = [ex.Message]; }
        }
        else errors = RecordValidator.Validate(fields, records, currentDataEncodingProfile, currentNationalTextEncoding);
        if (errors.Count == 0)
        {
            SetStatus("検証 OK");
            if (showSuccess)
            {
                MessageBox.Show(this, "検証 OK です。", "検証", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            return true;
        }

        MessageBox.Show(this, string.Join(Environment.NewLine, errors.Take(20)), "検証エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        SetStatus($"検証エラー: {errors.Count} 件");
        return false;
    }

    private List<int> VisibleFieldIndexes()
    {
        var indexes = new List<int>();
        for (var index = 0; index < fields.Count; index++)
        {
            if (IsDisplayableField(fields[index]) && !hiddenFieldIndexes.Contains(index))
            {
                indexes.Add(index);
            }
        }

        return indexes;
    }

    private List<int> DisplayableFieldIndexes()
    {
        var indexes = new List<int>();
        for (var index = 0; index < fields.Count; index++)
        {
            if (IsDisplayableField(fields[index]))
            {
                indexes.Add(index);
            }
        }

        return indexes;
    }

    private static bool IsDisplayableField(FieldDefinition field)
    {
        return !field.IsRedefines;
    }

    private int FieldIndexFromVisibleIndex(int visibleIndex)
    {
        if (visibleIndex < 0)
        {
            return -1;
        }

        var currentVisibleIndex = 0;
        for (var fieldIndex = 0; fieldIndex < fields.Count; fieldIndex++)
        {
            if (!IsDisplayableField(fields[fieldIndex]) || hiddenFieldIndexes.Contains(fieldIndex))
            {
                continue;
            }

            if (currentVisibleIndex == visibleIndex)
            {
                return fieldIndex;
            }

            currentVisibleIndex++;
        }

        return -1;
    }

    private int FieldIndexFromFieldRow(int rowIndex)
    {
        return rowIndex >= 0 && rowIndex < fieldGridRows.Count
            ? fieldGridRows[rowIndex].FieldIndex
            : -1;
    }

    private int FieldIndexFromRecordColumn(int columnIndex)
    {
        return columnIndex >= 0 && columnIndex < recordGridColumnFieldIndexes.Count
            ? recordGridColumnFieldIndexes[columnIndex]
            : -1;
    }

    private int RecordRowsDataStartIndex()
    {
        return 2 + (occursDisplayInfos.Count > 0 ? 1 : 0);
    }

    private int RecordIndexFromGridRow(int rowIndex)
    {
        return rowIndex - RecordRowsDataStartIndex();
    }

    private void ChangeLayout(GridLayout nextLayout)
    {
        if (layout == nextLayout && !isSingleRecordView)
        {
            return;
        }

        grid.EndEdit();
        var record = CurrentRecordIndex();
        var field = CurrentFieldIndex();
        var wasSingleRecordView = isSingleRecordView;
        isSingleRecordView = false;
        layout = nextLayout;
        RefreshGrid();
        if (wasSingleRecordView) RestoreRecordFieldSelection(record, field);
    }

    private void UpdateRecordValue(int rowIndex, int columnIndex)
    {
        if (rowIndex < 0 || columnIndex < 0)
        {
            return;
        }

        NormalizeRecords();

        if (layout == GridLayout.FieldRows)
        {
            var fieldRowsFieldIndex = FieldIndexFromFieldRow(rowIndex);
            if (columnIndex < 2 || (isSingleRecordView && columnIndex != SingleRecordValueColumnIndex) || fieldRowsFieldIndex < 0)
            {
                return;
            }

            var recordIndex = isSingleRecordView ? singleRecordIndex : columnIndex - 2;
            if (recordIndex < 0 || recordIndex >= records.Count)
            {
                return;
            }

            var field = fields[fieldRowsFieldIndex];
            if (field.IsRedefines)
            {
                return;
            }

            var formattedValue = FormatValueForDisplay(field, grid[columnIndex, rowIndex].Value?.ToString() ?? string.Empty, IsCsv);
            if (records[recordIndex][fieldRowsFieldIndex] != formattedValue) SetUnsavedChanges(true);
            records[recordIndex][fieldRowsFieldIndex] = formattedValue;
            grid[columnIndex, rowIndex].Value = formattedValue;
            ApplyCellValidationStyle(grid[columnIndex, rowIndex], field, formattedValue);
            UpdateSingleRecordDetails(rowIndex);
            UpdateHexView();
            return;
        }

        var recordRowsRecordIndex = RecordIndexFromGridRow(rowIndex);
        if (columnIndex < 1 || recordRowsRecordIndex < 0 || recordRowsRecordIndex >= records.Count)
        {
            return;
        }

        var fieldIndex = FieldIndexFromRecordColumn(columnIndex);
        if (fieldIndex < 0 || fieldIndex >= fields.Count)
        {
            return;
        }

        var recordRowsField = fields[fieldIndex];
        if (recordRowsField.IsRedefines)
        {
            return;
        }

        var recordRowsFormattedValue = FormatValueForDisplay(recordRowsField, grid[columnIndex, rowIndex].Value?.ToString() ?? string.Empty, IsCsv);
        if (records[recordRowsRecordIndex][fieldIndex] != recordRowsFormattedValue) SetUnsavedChanges(true);
        records[recordRowsRecordIndex][fieldIndex] = recordRowsFormattedValue;
        grid[columnIndex, rowIndex].Value = recordRowsFormattedValue;
        ApplyCellValidationStyle(grid[columnIndex, rowIndex], recordRowsField, recordRowsFormattedValue);
        UpdateHexView();
    }

    private int CurrentRecordIndex()
    {
        if (isSingleRecordView) return records.Count == 0 ? -1 : singleRecordIndex;
        if (grid.CurrentCell is null)
        {
            return records.Count - 1;
        }

        return layout == GridLayout.FieldRows
            ? grid.CurrentCell.ColumnIndex >= 2 ? grid.CurrentCell.ColumnIndex - 2 : records.Count - 1
            : RecordIndexFromGridRow(grid.CurrentCell.RowIndex);
    }

    private int CurrentFieldIndex()
    {
        if (grid.CurrentCell is null || fields.Count == 0)
        {
            return -1;
        }

        return layout == GridLayout.FieldRows
            ? FieldIndexFromFieldRow(grid.CurrentCell.RowIndex)
            : FieldIndexFromRecordColumn(grid.CurrentCell.ColumnIndex);
    }

    private void SelectRecord(int recordIndex)
    {
        if (isSingleRecordView)
        {
            ChangeSingleRecord(recordIndex);
            var firstRow = fieldGridRows.FindIndex(row => !row.IsOccursHeader && row.FieldIndex >= 0);
            if (firstRow >= 0 && records.Count > 0) grid.CurrentCell = grid[SingleRecordValueColumnIndex, firstRow];
            return;
        }
        if (recordIndex < 0 || fields.Count == 0 || records.Count == 0 || VisibleFieldIndexes().Count == 0)
        {
            return;
        }

        if (layout == GridLayout.FieldRows)
        {
            var firstFieldRowIndex = fieldGridRows.FindIndex(row => !row.IsOccursHeader && row.FieldIndex >= 0);
            if (firstFieldRowIndex < 0 || recordIndex + 2 >= grid.Columns.Count)
            {
                return;
            }

            grid.CurrentCell = grid[recordIndex + 2, firstFieldRowIndex];
        }
        else
        {
            var firstFieldColumnIndex = recordGridColumnFieldIndexes.FindIndex(fieldIndex => fieldIndex >= 0);
            if (firstFieldColumnIndex < 0)
            {
                return;
            }

            grid.CurrentCell = grid[firstFieldColumnIndex, recordIndex + RecordRowsDataStartIndex()];
        }
        UpdateHexView();
    }

    private void RefreshGrid()
    {
        isRefreshingGrid = true;
        try
        {
            NormalizeRecords();
            UpdateSingleRecordControls();
            grid.Columns.Clear();
            grid.Rows.Clear();
            fieldGridRows.Clear();
            recordGridColumnFieldIndexes.Clear();
            RebuildOccursDisplayInfos();
            UpdateLayoutButtons();
            grid.RowHeadersVisible = layout == GridLayout.FieldRows;
            grid.TopLeftHeaderCell.Value = layout == GridLayout.FieldRows ? "No." : null;

            if (layout == GridLayout.FieldRows)
            {
                RefreshFieldRowsGrid();
            }
            else
            {
                RefreshRecordRowsGrid();
            }

            SetStatus($"{VisibleFieldIndexes().Count} / {DisplayableFieldIndexes().Count} 項目表示 / {records.Count} レコード");
            RefreshGridSizing();
            SizeColumnsToFitData();
        }
        finally
        {
            isRefreshingGrid = false;
        }
        UpdateHexView();
    }

    private void SizeColumnsToFitData()
    {
        if (isSingleRecordView) { SizeSingleRecordColumns(); return; }
        if (displayWrapOptions.Enabled) { SizeWrappedColumns(); return; }
        foreach (DataGridViewColumn column in grid.Columns)
        {
            var width = column.GetPreferredWidth(DataGridViewAutoSizeColumnMode.AllCells, fixedHeight: false);
            foreach (DataGridViewRow row in grid.Rows)
            {
                if (layout == GridLayout.RecordRows && RecordIndexFromGridRow(row.Index) < 0) continue;
                if (!TryGetCellField(row.Index, column.Index, out var field)
                    || (!IsCsv && field.Type is not (FieldDataType.Text or FieldDataType.HalfWidthText or FieldDataType.FullWidthText))) continue;
                var cell = row.Cells[column.Index];
                var value = cell.FormattedValue?.ToString() ?? string.Empty;
                if (GetValidationError(field, value) is not null) continue;
                var style = cell.InheritedStyle;
                var visibleValue = GetVisibleTextValue(field, value);
                var textWidth = TextRenderer.MeasureText(visibleValue, style.Font ?? grid.Font,
                    Size.Empty, TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine).Width;
                width = Math.Max(width, textWidth + style.Padding.Horizontal + 2);
            }
            column.Width = Math.Clamp(width, column.MinimumWidth, 65536);
        }
    }

    private void RefreshFieldRowsGrid()
    {
        grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.EnableResizing;
        grid.ColumnHeadersHeight = 28;
        grid.Columns.Add(CreateReadOnlyColumn("Name", "項目名", 180, frozen: true));
        grid.Columns.Add(CreateReadOnlyColumn("Definition", isSingleRecordView ? "属性" : "定義", 150, frozen: true));
        ApplyDefinitionColumnStyle(grid.Columns[0]);
        ApplyDefinitionColumnStyle(grid.Columns[1]);

        if (isSingleRecordView)
        {
            grid.Columns.Add(CreateReadOnlyColumn("FieldStartPosition", "開始位置", 100, frozen: true));
            ApplyDefinitionColumnStyle(grid.Columns[2]);
            grid.Columns[2].Visible = !IsCsv;
            grid.Columns[2].HeaderCell.ToolTipText = "レコード先頭を1とする開始バイト位置";
            grid.Columns.Add(CreateEditableColumn("SingleRecordValue", "内容", 200));
            grid.Columns[SingleRecordValueColumnIndex].ReadOnly = records.Count == 0;
            grid.Columns.Add(CreateReadOnlyColumn("FieldHex", "HEX", 300, frozen: false));
            grid.Columns[SingleRecordHexColumnIndex].DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            grid.Columns.Add(CreateReadOnlyColumn("FieldSpaces", "空白 半角 / 全角", 140, frozen: false));
            if (fields.Any(field => !string.IsNullOrEmpty(field.Comment)))
            {
                var commentColumn = CreateReadOnlyColumn("FieldComment", "期待値・コメント", 240, frozen: false);
                commentColumn.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
                grid.Columns.Add(commentColumn);
            }
        }
        else for (var recordIndex = 0; recordIndex < records.Count; recordIndex++)
        {
            grid.Columns.Add(CreateEditableColumn($"Record{recordIndex + 1}", $"Rec {recordIndex + 1}", 140));
        }

        string? lastGroupInstanceKey = null;
        foreach (var fieldIndex in VisibleFieldIndexes())
        {
            var field = fields[fieldIndex];
            occursDisplayInfos.TryGetValue(fieldIndex, out var occursInfo);
            if (occursInfo is not null && !string.Equals(lastGroupInstanceKey, occursInfo.GroupInstanceKey, StringComparison.Ordinal))
            {
                AddOccursHeaderRow(occursInfo);
                lastGroupInstanceKey = occursInfo.GroupInstanceKey;
            }
            else if (occursInfo is null)
            {
                lastGroupInstanceKey = null;
            }

            var rowIndex = grid.Rows.Add();
            fieldGridRows.Add(new FieldGridRow(fieldIndex, occursInfo, IsOccursHeader: false));
            var row = grid.Rows[rowIndex];
            row.HeaderCell.Value = (fieldIndex + 1).ToString(CultureInfo.InvariantCulture);
            row.HeaderCell.ToolTipText = $"定義順の項目番号: {fieldIndex + 1}";
            row.Cells[0].Value = occursInfo is null ? field.Name : $"  {occursInfo.ChildName}";
            row.Cells[0].ToolTipText = FieldDefinitionToolTip(field);
            row.Cells[1].Value = field.DisplayDefinition;
            if (isSingleRecordView) row.Cells[2].Value = IsCsv ? "" : FieldStartPosition(fieldIndex);
            if (isSingleRecordView && grid.Columns.Contains("FieldComment"))
            {
                row.Cells["FieldComment"].Value = field.Comment;
                row.Cells["FieldComment"].ToolTipText = field.Comment;
            }
            if (occursInfo is not null)
            {
                row.DefaultCellStyle.BackColor = OccursBackColor(occursInfo);
            }

            var displayedRecords = isSingleRecordView
                ? records.Count > 0 ? new[] { singleRecordIndex } : Array.Empty<int>()
                : Enumerable.Range(0, records.Count);
            foreach (var recordIndex in displayedRecords)
            {
                var value = FormatValueForDisplay(field, records[recordIndex][fieldIndex], IsCsv);
                var columnIndex = isSingleRecordView ? SingleRecordValueColumnIndex : recordIndex + 2;
                row.Cells[columnIndex].Value = value;
                row.Cells[columnIndex].ReadOnly = field.IsRedefines;
                ApplyCellValidationStyle(row.Cells[columnIndex], field, value, occursInfo is null ? null : OccursBackColor(occursInfo));
            }
            UpdateSingleRecordDetails(rowIndex);
        }
    }

    private void AddOccursHeaderRow(OccursDisplayInfo occursInfo)
    {
        var rowIndex = grid.Rows.Add();
        fieldGridRows.Add(new FieldGridRow(FieldIndex: -1, occursInfo, IsOccursHeader: true));
        var row = grid.Rows[rowIndex];
        row.ReadOnly = true;
        row.DefaultCellStyle.BackColor = OccursHeaderBackColor;
        row.HeaderCell.Style.BackColor = OccursHeaderBackColor;
        row.HeaderCell.Style.SelectionBackColor = OccursHeaderBackColor;
        row.DefaultCellStyle.ForeColor = OccursHeaderForeColor;
        row.DefaultCellStyle.SelectionBackColor = OccursHeaderBackColor;
        row.DefaultCellStyle.SelectionForeColor = OccursHeaderForeColor;
        row.Cells[0].Value = $"▼ {occursInfo.GroupName}";
        row.Cells[1].Value = $"OCCURS {occursInfo.OccurrenceIndex}/{occursInfo.OccurrenceCount}";
        for (var columnIndex = 2; columnIndex < grid.Columns.Count; columnIndex++)
        {
            row.Cells[columnIndex].Value = string.Empty;
        }
    }

    private void RefreshRecordRowsGrid()
    {
        grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.EnableResizing;
        grid.ColumnHeadersHeight = 28;
        grid.Columns.Add(CreateReadOnlyColumn("Record", "行", 50, frozen: true));
        recordGridColumnFieldIndexes.Add(-1);
        ApplyDefinitionColumnStyle(grid.Columns[0]);

        var definitions = new List<string> { "型" };
        var positions = new List<string> { "位置" };
        var byteStart = 1;
        for (var fieldIndex = 0; fieldIndex < fields.Count; fieldIndex++)
        {
            var field = fields[fieldIndex];
            var byteEnd = byteStart + field.StorageByteLength - 1;
            if (IsDisplayableField(field) && !hiddenFieldIndexes.Contains(fieldIndex))
            {
                occursDisplayInfos.TryGetValue(fieldIndex, out var occursInfo);
                var ruler = IsCsv ? $"CSV列{fields.Take(fieldIndex + 1).Count(item => !item.IsRedefines)}" : $"[{byteStart}-{byteEnd}]";
                var header = occursInfo is null ? field.Name : occursInfo.ChildName;
                definitions.Add(field.DisplayDefinition);
                positions.Add(ruler);
                var column = CreateEditableColumn(
                    $"Field{fieldIndex + 1}",
                    header,
                    170);
                column.HeaderCell.Style.BackColor = occursInfo is null ? DefinitionHeaderBackColor : OccursHeaderBackColor;
                column.HeaderCell.ToolTipText = FieldDefinitionToolTip(field);

                grid.Columns.Add(column);
                recordGridColumnFieldIndexes.Add(fieldIndex);
            }

            if (!field.IsRedefines)
            {
                byteStart = byteEnd + 1;
            }
        }

        AddRecordDefinitionRow(definitions);
        AddRecordDefinitionRow(positions);

        if (occursDisplayInfos.Count > 0)
        {
            AddOccursRecordHeaderRow();
        }

        for (var recordIndex = 0; recordIndex < records.Count; recordIndex++)
        {
            var rowIndex = grid.Rows.Add();
            var row = grid.Rows[rowIndex];
            row.Cells[0].Value = (recordIndex + 1).ToString(CultureInfo.InvariantCulture);

            for (var columnIndex = 1; columnIndex < grid.Columns.Count; columnIndex++)
            {
                var fieldIndex = FieldIndexFromRecordColumn(columnIndex);
                if (fieldIndex < 0)
                {
                    row.Cells[columnIndex].ReadOnly = true;
                    row.Cells[columnIndex].Value = string.Empty;
                    continue;
                }

                var field = fields[fieldIndex];
                var value = FormatValueForDisplay(field, records[recordIndex][fieldIndex], IsCsv);
                row.Cells[columnIndex].Value = value;
                row.Cells[columnIndex].ReadOnly = field.IsRedefines;
                ApplyCellValidationStyle(row.Cells[columnIndex], field, value);
            }
        }
    }

    private void AddRecordDefinitionRow(IReadOnlyList<string> values)
    {
        var row = grid.Rows[grid.Rows.Add(values.Cast<object>().ToArray())];
        row.ReadOnly = true;
        row.Frozen = true;
        for (var columnIndex = 0; columnIndex < grid.Columns.Count; columnIndex++)
        {
            var style = row.Cells[columnIndex].Style;
            style.BackColor = grid.Columns[columnIndex].HeaderCell.Style.BackColor;
            style.SelectionBackColor = style.BackColor;
            style.ForeColor = grid.ForeColor;
            style.SelectionForeColor = grid.ForeColor;
        }
    }

    private void AddOccursRecordHeaderRow()
    {
        var rowIndex = grid.Rows.Add();
        var row = grid.Rows[rowIndex];
        row.ReadOnly = true;
        row.DefaultCellStyle.BackColor = OccursHeaderBackColor;
        row.DefaultCellStyle.ForeColor = OccursHeaderForeColor;
        row.DefaultCellStyle.SelectionBackColor = OccursHeaderBackColor;
        row.DefaultCellStyle.SelectionForeColor = OccursHeaderForeColor;
        row.Cells[0].Value = "OCCURS";

        string? lastGroupInstanceKey = null;
        for (var columnIndex = 1; columnIndex < grid.Columns.Count; columnIndex++)
        {
            var fieldIndex = FieldIndexFromRecordColumn(columnIndex);
            if (fieldIndex < 0 || !occursDisplayInfos.TryGetValue(fieldIndex, out var occursInfo))
            {
                row.Cells[columnIndex].Value = string.Empty;
                continue;
            }

            row.Cells[columnIndex].Style.BackColor = OccursBackColor(occursInfo);
            row.Cells[columnIndex].Style.SelectionBackColor = OccursBackColor(occursInfo);
            if (!string.Equals(lastGroupInstanceKey, occursInfo.GroupInstanceKey, StringComparison.Ordinal))
            {
                row.Cells[columnIndex].Value = $"{occursInfo.GroupName}  {occursInfo.OccurrenceIndex}/{occursInfo.OccurrenceCount}";
                lastGroupInstanceKey = occursInfo.GroupInstanceKey;
            }
            else
            {
                row.Cells[columnIndex].Value = string.Empty;
            }
        }
    }

    private void UpdateHexView()
    {
        UpdateSelectionSpaceCount();
        var recordIndex = CurrentRecordIndex();
        var fieldIndex = CurrentFieldIndex();
        if (recordIndex < 0
            || recordIndex >= records.Count
            || fieldIndex < 0
            || fieldIndex >= fields.Count
            || fieldIndex >= records[recordIndex].Count)
        {
            hexTextBox.Clear();
            return;
        }

        hexTextBox.Text = FieldHexText(recordIndex, fieldIndex);
    }

    private void UpdateSelectionSpaceCount()
    {
        var record = CurrentRecordIndex();
        var field = CurrentFieldIndex();
        if (grid.CurrentCell is null || (layout == GridLayout.FieldRows && grid.CurrentCell.ColumnIndex < 2)
            || record < 0 || record >= records.Count || field < 0 || field >= fields.Count || field >= records[record].Count)
        {
            selectionSpaceLabel.Text = "半角スペース: — / 全角スペース: —";
            return;
        }
        var summary = SelectionSpaceSummary.Count(records[record][field], fields[field], IsCsv, TextDataEncoding);
        selectionSpaceLabel.Text = summary.DisplayText;
    }

    private void ApplyCellValidationStyle(DataGridViewCell cell, FieldDefinition field, string value, Color? validBackColor = null)
    {
        var error = GetValidationError(field, value);
        cell.Style.BackColor = error is not null
            ? ErrorBackColor
            : validBackColor.GetValueOrDefault(Color.Empty);
        cell.ToolTipText = error ?? string.Empty;
    }

    private void PaintTextPaddingArea(DataGridViewCellPaintingEventArgs e)
    {
        if (TryPaintWrappedCell(e)) return;
        if (TryPaintOccursRecordHeaderArea(e))
        {
            return;
        }

        if (e.RowIndex < 0
            || e.ColumnIndex < 0
            || e.Graphics is null
            || e.CellStyle is null
            || (layout == GridLayout.RecordRows && RecordIndexFromGridRow(e.RowIndex) < 0)
            || !TryGetCellField(e.RowIndex, e.ColumnIndex, out var field)
            || (!IsCsv && field.Type is not (FieldDataType.Text or FieldDataType.HalfWidthText or FieldDataType.FullWidthText)))
        {
            return;
        }

        var value = e.FormattedValue?.ToString() ?? string.Empty;
        if (GetValidationError(field, value) is not null)
        {
            return;
        }

        var visibleValue = GetVisibleTextValue(field, value);
        if (visibleValue == value)
        {
            return;
        }

        e.Paint(e.CellBounds, e.PaintParts & ~DataGridViewPaintParts.ContentForeground);

        var padding = e.CellStyle.Padding;
        // 標準のDataGridViewTextBoxCellと同じ余白にする。TextRenderer自身の余白に加算しない。
        var textBounds = new Rectangle(
            e.CellBounds.Left + padding.Left,
            e.CellBounds.Top + padding.Top + 2,
            e.CellBounds.Width - padding.Horizontal - 1,
            e.CellBounds.Height - padding.Vertical - 4);
        var font = e.CellStyle.Font ?? grid.Font;
        var selected = (e.State & DataGridViewElementStates.Selected) != 0;
        if (!selected) PaintVisibleSpaceHighlights(e.Graphics, font, textBounds, visibleValue);
        TextRenderer.DrawText(
            e.Graphics,
            visibleValue,
            font,
            textBounds,
            selected ? e.CellStyle.SelectionForeColor : e.CellStyle.ForeColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix
                | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.PreserveGraphicsClipping);
        e.Handled = true;
    }

    private bool TryPaintOccursRecordHeaderArea(DataGridViewCellPaintingEventArgs e)
    {
        if (layout != GridLayout.RecordRows
            || occursDisplayInfos.Count == 0
            || e.RowIndex != 2
            || e.ColumnIndex < 0
            || e.Graphics is null
            || e.CellStyle is null)
        {
            return false;
        }

        if (e.ColumnIndex == 0)
        {
            using var background = new SolidBrush(DefinitionBackColor);
            e.Graphics.FillRectangle(background, e.CellBounds);
            DrawCellBorder(e.Graphics, e.CellBounds, drawLeft: true, drawRight: true);
            TextRenderer.DrawText(
                e.Graphics,
                "OCCURS",
                e.CellStyle.Font ?? grid.Font,
                e.CellBounds,
                OccursHeaderForeColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            e.Handled = true;
            return true;
        }

        var fieldIndex = FieldIndexFromRecordColumn(e.ColumnIndex);
        if (fieldIndex < 0 || !occursDisplayInfos.TryGetValue(fieldIndex, out var occursInfo))
        {
            using var background = new SolidBrush(e.CellStyle.BackColor.IsEmpty ? grid.DefaultCellStyle.BackColor : e.CellStyle.BackColor);
            e.Graphics.FillRectangle(background, e.CellBounds);
            DrawCellBorder(e.Graphics, e.CellBounds, drawLeft: true, drawRight: true);
            e.Handled = true;
            return true;
        }

        var firstColumnIndex = FirstRecordColumnInOccursGroup(e.ColumnIndex, occursInfo);
        if (e.ColumnIndex != firstColumnIndex)
        {
            e.Handled = true;
            return true;
        }

        var spanBounds = OccursGroupBounds(e.RowIndex, firstColumnIndex, occursInfo);
        using var groupBackground = new SolidBrush(OccursBackColor(occursInfo));
        e.Graphics.FillRectangle(groupBackground, spanBounds);
        DrawCellBorder(e.Graphics, spanBounds, drawLeft: true, drawRight: true);

        TextRenderer.DrawText(
            e.Graphics,
            $"{occursInfo.GroupName}  {occursInfo.OccurrenceIndex}/{occursInfo.OccurrenceCount}",
            e.CellStyle.Font ?? grid.Font,
            spanBounds,
            OccursHeaderForeColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        e.Handled = true;
        return true;
    }

    private int FirstRecordColumnInOccursGroup(int columnIndex, OccursDisplayInfo occursInfo)
    {
        for (var currentColumnIndex = columnIndex - 1; currentColumnIndex >= 1; currentColumnIndex--)
        {
            var fieldIndex = FieldIndexFromRecordColumn(currentColumnIndex);
            if (fieldIndex < 0
                || !occursDisplayInfos.TryGetValue(fieldIndex, out var currentOccursInfo)
                || !string.Equals(currentOccursInfo.GroupInstanceKey, occursInfo.GroupInstanceKey, StringComparison.Ordinal))
            {
                return currentColumnIndex + 1;
            }
        }

        return 1;
    }

    private int LastRecordColumnInOccursGroup(int columnIndex, OccursDisplayInfo occursInfo)
    {
        for (var currentColumnIndex = columnIndex + 1; currentColumnIndex < grid.Columns.Count; currentColumnIndex++)
        {
            var fieldIndex = FieldIndexFromRecordColumn(currentColumnIndex);
            if (fieldIndex < 0
                || !occursDisplayInfos.TryGetValue(fieldIndex, out var currentOccursInfo)
                || !string.Equals(currentOccursInfo.GroupInstanceKey, occursInfo.GroupInstanceKey, StringComparison.Ordinal))
            {
                return currentColumnIndex - 1;
            }
        }

        return grid.Columns.Count - 1;
    }

    private Rectangle OccursGroupBounds(int rowIndex, int firstColumnIndex, OccursDisplayInfo occursInfo)
    {
        var lastColumnIndex = LastRecordColumnInOccursGroup(firstColumnIndex, occursInfo);
        var firstBounds = grid.GetCellDisplayRectangle(firstColumnIndex, rowIndex, cutOverflow: false);
        var lastBounds = grid.GetCellDisplayRectangle(lastColumnIndex, rowIndex, cutOverflow: false);
        if (firstBounds.Width <= 0 || lastBounds.Width <= 0)
        {
            return grid.GetCellDisplayRectangle(firstColumnIndex, rowIndex, cutOverflow: true);
        }

        return new Rectangle(
            firstBounds.Left,
            firstBounds.Top,
            lastBounds.Right - firstBounds.Left,
            firstBounds.Height);
    }

    private static void DrawCellBorder(Graphics graphics, Rectangle bounds, bool drawLeft, bool drawRight)
    {
        using var pen = new Pen(SystemColors.ControlDark);
        graphics.DrawLine(pen, bounds.Left, bounds.Top, bounds.Right - 1, bounds.Top);
        graphics.DrawLine(pen, bounds.Left, bounds.Bottom - 1, bounds.Right - 1, bounds.Bottom - 1);
        if (drawLeft)
        {
            graphics.DrawLine(pen, bounds.Left, bounds.Top, bounds.Left, bounds.Bottom - 1);
        }

        if (drawRight)
        {
            graphics.DrawLine(pen, bounds.Right - 1, bounds.Top, bounds.Right - 1, bounds.Bottom - 1);
        }
    }

    private static void PaintVisibleSpaceHighlights(Graphics graphics, Font font, Rectangle textBounds, string visibleValue)
    {
        var runStart = -1;
        for (var index = 0; index <= visibleValue.Length; index++)
        {
            var isSpaceMarker = index < visibleValue.Length && IsVisibleSpaceMarker(visibleValue[index]);
            if (isSpaceMarker && runStart < 0)
            {
                runStart = index;
            }

            if (runStart < 0)
            {
                continue;
            }

            if (isSpaceMarker)
            {
                continue;
            }

            var beforeText = visibleValue[..runStart];
            var runText = visibleValue[runStart..index];
            var runX = textBounds.Left + MeasureTextWidth(graphics, beforeText, font);
            var runWidth = MeasureTextWidth(graphics, runText, font);
            var clippedWidth = Math.Min(runWidth, textBounds.Right - runX);
            if (clippedWidth > 0)
            {
                using var brush = new SolidBrush(HalfWidthSpaceBackColor);
                graphics.FillRectangle(brush, runX, textBounds.Top, clippedWidth, textBounds.Height);
            }

            runStart = -1;
        }
    }

    private static int MeasureTextWidth(Graphics graphics, string text, Font font)
    {
        return string.IsNullOrEmpty(text)
            ? 0
            : TextRenderer.MeasureText(
                graphics,
                text,
                font,
                Size.Empty,
                TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width;
    }

    private static bool IsVisibleSpaceMarker(char value)
    {
        return value is '\uFF65' or '\u25A1';
    }

    private string GetVisibleTextValue(FieldDefinition field, string value)
    {
        if (IsCsv) return BuildVisibleTextValue(field, value, 0);
        int usedLength;
        try { usedLength = field.Type is FieldDataType.FullWidthText or FieldDataType.HalfWidthText
            ? value.Length : TextDataEncoding.GetByteCount(value); }
        catch (EncoderFallbackException) { return BuildVisibleTextValue(field, value, 0); }
        return BuildVisibleTextValue(field, value, field.Length - usedLength);
    }

    private static string BuildVisibleTextValue(FieldDefinition field, string value, int remainingLength)
    {
        var displayValue = value.Replace(' ', '\uFF65').Replace('\u3000', '\u25A1');
        return remainingLength > 0
            ? displayValue + new string(VisibleSpaceMarker(field), remainingLength)
            : displayValue;
    }

    private static char VisibleSpaceMarker(FieldDefinition field)
    {
        return field.Type == FieldDataType.FullWidthText ? '\u25A1' : '\uFF65';
    }

    private bool TryGetCellField(int rowIndex, int columnIndex, out FieldDefinition field)
    {
        if (layout == GridLayout.FieldRows)
        {
            var fieldIndex = FieldIndexFromFieldRow(rowIndex);
            if (columnIndex >= 2 && (!isSingleRecordView || columnIndex == SingleRecordValueColumnIndex) && fieldIndex >= 0)
            {
                field = fields[fieldIndex];
                return true;
            }
        }
        else if (columnIndex >= 1)
        {
            var fieldIndex = FieldIndexFromRecordColumn(columnIndex);
            if (fieldIndex >= 0 && fieldIndex < fields.Count)
            {
                field = fields[fieldIndex];
                return true;
            }
        }

        field = null!;
        return false;
    }

    private string? GetValidationError(FieldDefinition field, string value)
    {
        if (IsCsv) return null; // CSVには固定長領域の補完・桁数検証を適用しない。
        value = value.Trim();
        try { FixedLengthDataWriter.EncodeField(field, value, currentDataEncodingProfile, currentNationalTextEncoding); }
        catch (EncoderFallbackException ex) { return ex.Message; }
        catch (InvalidDataException) { /* Existing type/length messages below. */ }
        switch (field.Type)
        {
            case FieldDataType.PlainNumber:
            case FieldDataType.PackedUnsigned:
                return NumericValueFormatter.TryFormatDigits(value, field, signed: false, out _, out _, out var unsignedError)
                    ? null
                    : unsignedError;

            case FieldDataType.SignedNumber:
            case FieldDataType.PackedSigned:
                return NumericValueFormatter.TryFormatDigits(value, field, signed: true, out _, out _, out var signedError)
                    ? null
                    : signedError;

            case FieldDataType.Text:
                return TextDataEncoding.GetByteCount(value) <= field.Length
                    ? null
                    : $"{TextDataEncoding.WebName} バイト長 {field.Length} を超えています。";

            case FieldDataType.HalfWidthText:
                if (Encoding.ASCII.GetByteCount(value) != value.Length)
                {
                    return "半角文字のみ入力してください。";
                }

                return value.Length <= field.Length ? null : $"桁数 {field.Length} を超えています。";

            case FieldDataType.FullWidthText:
                return value.Length <= field.Length ? null : $"桁数 {field.Length} を超えています。";

            default:
                return null;
        }
    }

    private void UpdateLayoutButtons()
    {
        fieldRowsButton.Checked = !isSingleRecordView && layout == GridLayout.FieldRows;
        recordRowsButton.Checked = !isSingleRecordView && layout == GridLayout.RecordRows;
    }

    private void UpdateSeparatorModeText()
    {
        if (IsCsv)
        {
            separatorModeTextBox.Text = $"可変長CSV / {JapaneseEncodings.Name(currentCsvEncoding)} / {(currentCsvHasHeader ? "ヘッダーあり" : "ヘッダーなし")} / 空白を保持";
            return;
        }
        var separatorText = currentSeparatorMode == RecordSeparatorMode.None
            ? "改行なし"
            : "改行あり (CRLF/LF)";
        var encodingText = JapaneseEncodings.Name(currentDataEncodingProfile);
        separatorModeTextBox.Text = $"{separatorText} / X項目: {encodingText} / 型N: {NationalTextEncodingHelper.DisplayName(currentNationalTextEncoding)}";
    }

    private string? GetCurrentDefinitionPath()
    {
        return definitionPathTextBox.Text;
    }

    private string? GetCurrentDataPath()
    {
        return dataPathTextBox.Text;
    }

    private static SamplePattern? FindCopybookSamplePattern(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var fileName = Path.GetFileName(path);
        return CopybookSamplePatterns.FirstOrDefault(pattern => pattern.MatchesFile(fileName));
    }

    private static string FormatSeparatorMode(RecordSeparatorMode separatorMode)
    {
        return separatorMode == RecordSeparatorMode.None
            ? "改行なし"
            : "改行あり (CRLF/LF)";
    }

    private void RebuildOccursDisplayInfos()
    {
        occursDisplayInfos.Clear();
        var parsedNames = new List<ParsedOccursName>();
        var occurrenceIndexesByBaseName = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);

        for (var fieldIndex = 0; fieldIndex < fields.Count; fieldIndex++)
        {
            if (!TryParseOccursFieldName(fields[fieldIndex].Name, out var parsedName))
            {
                continue;
            }

            parsedName = parsedName with { FieldIndex = fieldIndex };
            parsedNames.Add(parsedName);
            if (!occurrenceIndexesByBaseName.TryGetValue(parsedName.BaseName, out var occurrenceIndexes))
            {
                occurrenceIndexes = [];
                occurrenceIndexesByBaseName[parsedName.BaseName] = occurrenceIndexes;
            }

            occurrenceIndexes.Add(parsedName.OccurrenceIndex);
        }

        foreach (var parsedName in parsedNames)
        {
            var occurrenceCount = occurrenceIndexesByBaseName[parsedName.BaseName].Count;
            if (occurrenceCount <= 1)
            {
                continue;
            }

            occursDisplayInfos[parsedName.FieldIndex] = new OccursDisplayInfo(
                GroupName: parsedName.GroupName,
                GroupInstanceKey: $"{parsedName.BaseName}:{parsedName.OccurrenceIndex.ToString(CultureInfo.InvariantCulture)}",
                ChildName: parsedName.ChildName,
                OccurrenceIndex: parsedName.OccurrenceIndex,
                OccurrenceCount: occurrenceCount);
        }
    }

    private static bool TryParseOccursFieldName(string fieldName, out ParsedOccursName parsedName)
    {
        parsedName = default;
        var separatorIndex = fieldName.IndexOf('.');
        if (separatorIndex <= 0 || separatorIndex == fieldName.Length - 1)
        {
            return false;
        }

        var groupName = fieldName[..separatorIndex];
        var childName = fieldName[(separatorIndex + 1)..];
        var occurrenceSeparatorIndex = groupName.LastIndexOf('-');
        if (occurrenceSeparatorIndex <= 0 || occurrenceSeparatorIndex == groupName.Length - 1)
        {
            return false;
        }

        var occurrenceText = groupName[(occurrenceSeparatorIndex + 1)..];
        if (!int.TryParse(occurrenceText, NumberStyles.None, CultureInfo.InvariantCulture, out var occurrenceIndex)
            || occurrenceIndex <= 0)
        {
            return false;
        }

        parsedName = new ParsedOccursName(
            FieldIndex: -1,
            BaseName: groupName[..occurrenceSeparatorIndex],
            GroupName: groupName,
            ChildName: childName,
            OccurrenceIndex: occurrenceIndex);
        return true;
    }

    private static Color OccursBackColor(OccursDisplayInfo occursInfo)
    {
        return occursInfo.OccurrenceIndex % 2 == 0
            ? OccursGroupAlternateBackColor
            : OccursGroupBackColor;
    }

    private sealed record SamplePattern(
        string FileName,
        string DefinitionFileName,
        RecordSeparatorMode SeparatorMode,
        NationalTextEncoding NationalTextEncoding)
    {
        public bool MatchesFile(string? fileName)
        {
            return string.Equals(fileName, FileName, StringComparison.OrdinalIgnoreCase);
        }

        public bool MatchesDefinition(string? fileName)
        {
            return string.Equals(fileName, DefinitionFileName, StringComparison.OrdinalIgnoreCase);
        }
    }

    private sealed record FileDropRequest(string? DefinitionPath, string? DataPath, string? ExcelPath);

    private sealed record DataLoadOptions(
        RecordSeparatorMode SeparatorMode,
        NationalTextEncoding NationalTextEncoding,
        bool IsCsv = false,
        CsvTextEncoding CsvEncoding = CsvTextEncoding.Utf8,
        DataEncodingProfile? TextEncoding = null,
        bool CsvHasHeader = false);

    private sealed record FieldGridRow(
        int FieldIndex,
        OccursDisplayInfo? OccursInfo,
        bool IsOccursHeader);

    private readonly record struct ParsedOccursName(
        int FieldIndex,
        string BaseName,
        string GroupName,
        string ChildName,
        int OccurrenceIndex);

    private sealed record OccursDisplayInfo(
        string GroupName,
        string GroupInstanceKey,
        string ChildName,
        int OccurrenceIndex,
        int OccurrenceCount);

    private static DataGridViewTextBoxColumn CreateReadOnlyColumn(string name, string header, int width, bool frozen)
    {
        return new DataGridViewTextBoxColumn
        {
            Name = name,
            HeaderText = header,
            Width = width,
            ReadOnly = true,
            Frozen = frozen,
            SortMode = DataGridViewColumnSortMode.NotSortable
        };
    }

    private static DataGridViewTextBoxColumn CreateEditableColumn(string name, string header, int width)
    {
        return new DataGridViewTextBoxColumn
        {
            Name = name,
            HeaderText = header,
            Width = width,
            SortMode = DataGridViewColumnSortMode.NotSortable
        };
    }

    private static void ApplyDefinitionColumnStyle(DataGridViewColumn column)
    {
        column.DefaultCellStyle.BackColor = DefinitionBackColor;
        column.HeaderCell.Style.BackColor = DefinitionHeaderBackColor;
    }

    private static string FormatValueForDisplay(FieldDefinition field, string value, bool isCsv = false)
    {
        if (isCsv) return value;
        return field.Type switch
        {
            FieldDataType.PlainNumber or FieldDataType.PackedUnsigned =>
                NumericValueFormatter.TryFormatDisplayValue(value, field, signed: false, out var displayValue, out _) ? displayValue : value,
            FieldDataType.SignedNumber or FieldDataType.PackedSigned =>
                NumericValueFormatter.TryFormatDisplayValue(value, field, signed: true, out var displayValue, out _) ? displayValue : value,
            _ => value
        };
    }

    private void SetStatus(string message)
    {
        statusLabel.Text = message;
    }

    private enum GridLayout
    {
        FieldRows,
        RecordRows
    }
}
