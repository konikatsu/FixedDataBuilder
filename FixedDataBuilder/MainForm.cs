using System.Globalization;
using System.Text;
using System.Diagnostics;

namespace FixedDataBuilder;

public sealed class MainForm : Form
{
    private static readonly Color DefinitionBackColor = Color.FromArgb(226, 242, 226);
    private static readonly Color DefinitionHeaderBackColor = Color.FromArgb(204, 232, 204);
    private static readonly Color ErrorBackColor = Color.FromArgb(255, 225, 225);
    private static readonly Color HalfWidthSpaceBackColor = Color.FromArgb(255, 250, 205);
    private static readonly Encoding ShiftJisEncoding = Encoding.GetEncoding("shift_jis");
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
        new("sample-copybook-utf16.dat", "definition-english.cbl", RecordSeparatorMode.CrLfOrLf, NationalTextEncoding.Utf16),
        new("sample-copybook-crlf-utf8-nutf16.dat", "definition-english.cbl", RecordSeparatorMode.CrLfOrLf, NationalTextEncoding.Utf16),
        new("sample-copybook-crlf-utf8-nutf8.dat", "definition-english.cbl", RecordSeparatorMode.CrLfOrLf, NationalTextEncoding.Utf8),
        new("sample-copybook-none-sjis-nsjis.dat", "definition-english.cbl", RecordSeparatorMode.None, NationalTextEncoding.ShiftJis),
        new("sample-copybook-none-utf8-nutf8.dat", "definition-english.cbl", RecordSeparatorMode.None, NationalTextEncoding.Utf8),
        new("sample-copybook-none-utf8-nutf16.dat", "definition-english.cbl", RecordSeparatorMode.None, NationalTextEncoding.Utf16),
        new("sample-copybook-none-utf8-nutf32.dat", "definition-english.cbl", RecordSeparatorMode.None, NationalTextEncoding.Utf32)
    ];

    private readonly ComboBox definitionPathComboBox = new();
    private readonly ComboBox dataPathComboBox = new();
    private readonly TextBox separatorModeTextBox = new();
    private readonly TextBox sampleHintTextBox = new();
    private readonly TextBox hexTextBox = new();
    private readonly DataGridView grid = new();
    private readonly MenuStrip menuStrip = new();
    private readonly StatusStrip statusStrip = new();
    private readonly ToolStripStatusLabel statusLabel = new();
    private readonly ToolStripMenuItem fieldRowsButton;
    private readonly ToolStripMenuItem recordRowsButton;
    private readonly List<string> recentDefinitionFiles = [];
    private readonly List<string> recentDataFiles = [];
    private readonly List<FieldDefinition> fields = [];
    private readonly List<List<string>> records = [];
    private readonly HashSet<int> hiddenFieldIndexes = [];
    private string? saveDataPath;
    private RecordSeparatorMode currentSeparatorMode = RecordSeparatorMode.CrLfOrLf;
    private DataEncodingProfile currentDataEncodingProfile = DataEncodingProfile.ShiftJis;
    private NationalTextEncoding currentNationalTextEncoding = NationalTextEncoding.ShiftJis;
    private bool definitionLoadedFromCopybook;
    private GridLayout layout = GridLayout.FieldRows;
    private bool isUpdatingPathComboBoxes;
    private bool isRefreshingGrid;
    private Font? displayFont;

    public MainForm(string[]? args = null)
    {
        Text = $"FixedDataBuilder v{Application.ProductVersion}";
        StartPosition = FormStartPosition.CenterScreen;
        Width = 1100;
        Height = 740;

        fieldRowsButton = CreateMenuItem("項目縦", (_, _) => ChangeLayout(GridLayout.FieldRows));
        recordRowsButton = CreateMenuItem("レコード縦", (_, _) => ChangeLayout(GridLayout.RecordRows));

        LoadRecentFiles();
        LoadSettings();
        displayFont ??= CreateDefaultDisplayFont();

        Controls.Add(grid);
        Controls.Add(BuildHexPanel());
        Controls.Add(statusStrip);
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
        SetStatus("定義ファイルを選択してください。");
        LoadStartupFiles(args ?? []);
    }

    private Control BuildFilePanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 3,
            RowCount = 4,
            Padding = new Padding(8, 6, 8, 4)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));

        ConfigurePathComboBox(definitionPathComboBox);
        ConfigurePathComboBox(dataPathComboBox);
        ConfigureReadOnlyTextBox(separatorModeTextBox);
        ConfigureReadOnlyTextBox(sampleHintTextBox);
        RefreshRecentFileItems();

        definitionPathComboBox.SelectionChangeCommitted += (_, _) => LoadDefinitionFromHistory();
        dataPathComboBox.SelectionChangeCommitted += (_, _) => LoadDataFromHistory();
        definitionPathComboBox.KeyDown += (_, e) => LoadDefinitionFromEnteredPath(e);
        dataPathComboBox.KeyDown += (_, e) => LoadDataFromEnteredPath(e);

        panel.Controls.Add(CreatePathLabel("定義ファイル"), 0, 0);
        panel.Controls.Add(definitionPathComboBox, 1, 0);
        panel.Controls.Add(CreatePathButton("選択", (_, _) => OpenDefinition()), 2, 0);
        panel.Controls.Add(CreatePathLabel("データファイル"), 0, 1);
        panel.Controls.Add(dataPathComboBox, 1, 1);
        panel.Controls.Add(CreatePathButton("選択", (_, _) => OpenData()), 2, 1);
        panel.Controls.Add(CreatePathLabel("レコード区切り"), 0, 2);
        panel.Controls.Add(separatorModeTextBox, 1, 2);
        panel.SetColumnSpan(separatorModeTextBox, 2);
        panel.Controls.Add(CreatePathLabel("サンプル条件"), 0, 3);
        panel.Controls.Add(sampleHintTextBox, 1, 3);
        panel.SetColumnSpan(sampleHintTextBox, 2);
        return panel;
    }

    private Control BuildHexPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 82,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(8, 2, 8, 4)
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        panel.Controls.Add(new Label
        {
            Text = "選択項目 HEX",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);

        hexTextBox.Dock = DockStyle.Fill;
        hexTextBox.ReadOnly = true;
        hexTextBox.Multiline = true;
        hexTextBox.ScrollBars = ScrollBars.Vertical;
        hexTextBox.Font = new Font(FontFamily.GenericMonospace, 9);
        panel.Controls.Add(hexTextBox, 0, 1);
        return panel;
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

    private static Button CreatePathButton(string text, EventHandler onClick)
    {
        var button = new Button
        {
            Text = text,
            Dock = DockStyle.Fill,
            Margin = new Padding(6, 1, 0, 3)
        };
        button.Click += onClick;
        return button;
    }

    private static void ConfigureReadOnlyTextBox(TextBox textBox)
    {
        textBox.Dock = DockStyle.Fill;
        textBox.ReadOnly = true;
        textBox.Margin = new Padding(0, 1, 0, 3);
    }

    private static void ConfigurePathComboBox(ComboBox comboBox)
    {
        comboBox.Dock = DockStyle.Fill;
        comboBox.DropDownStyle = ComboBoxStyle.DropDown;
        comboBox.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
        comboBox.AutoCompleteSource = AutoCompleteSource.ListItems;
        comboBox.Margin = new Padding(0, 1, 0, 3);
    }

    private void BuildMenuStrip()
    {
        menuStrip.Dock = DockStyle.Top;
        menuStrip.Items.Add(CreateMenu("ファイル",
            CreateMenuItem("定義を開く...", (_, _) => OpenDefinition()),
            CreateMenuItem("データを開く...", (_, _) => OpenData()),
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
            CreateMenuItem("定義修正...", (_, _) => CreateDefinition(editCurrent: true))));
        menuStrip.Items.Add(CreateMenu("表示",
            fieldRowsButton,
            recordRowsButton,
            new ToolStripSeparator(),
            CreateMenuItem("表示項目...", (_, _) => ChooseVisibleFields()),
            CreateMenuItem("フォント設定...", (_, _) => ChooseDisplayFont())));
        menuStrip.Items.Add(CreateMenu("ツール",
            CreateMenuItem("検証", (_, _) => ValidateRecords(showSuccess: true))));
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
        grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
        grid.MultiSelect = false;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
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
        RefreshGridSizing();
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

        grid.ColumnHeadersHeight = layout == GridLayout.RecordRows
            ? Math.Max(64, (grid.Font.Height * 3) + 16)
            : Math.Max(28, grid.Font.Height + 12);
        grid.RowTemplate.Height = Math.Max(22, grid.Font.Height + 8);
        foreach (DataGridViewRow row in grid.Rows)
        {
            row.Height = grid.RowTemplate.Height;
        }
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

        try
        {
            LoadDefinition(dialog.FileName);
            dataPathComboBox.Text = string.Empty;
            saveDataPath = null;
            records.Clear();
            records.Add(CreateEmptyRecord());
            RefreshGrid();
            SetStatus("定義ファイルを読み込みました。");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "定義読込エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
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
            Filter = "固定長データ (*.dat;*.txt)|*.dat;*.txt|すべてのファイル (*.*)|*.*",
            Title = "データファイルを選択"
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            var loadOptions = SelectDataLoadOptions(dialog.FileName);
            if (loadOptions is null)
            {
                return;
            }

            LoadData(dialog.FileName, loadOptions.SeparatorMode, loadOptions.NationalTextEncoding);
            RefreshGrid();
            SetStatus("データファイルを読み込みました。");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "データ読込エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
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
            LoadDefinition(definitionPath);
            records.Clear();
            records.Add(CreateEmptyRecord());

            var dataPath = GetOptionValue(args, "--data");
            if (!string.IsNullOrWhiteSpace(dataPath))
            {
                var separatorMode = string.Equals(GetOptionValue(args, "--separator"), "none", StringComparison.OrdinalIgnoreCase)
                    ? RecordSeparatorMode.None
                    : RecordSeparatorMode.CrLfOrLf;
                var nationalTextEncoding = ParseNationalTextEncoding(GetOptionValue(args, "--national-encoding"))
                    ?? currentNationalTextEncoding;
                LoadData(dataPath, separatorMode, nationalTextEncoding);
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

    private void LoadDefinition(string path)
    {
        definitionLoadedFromCopybook = IsCopybookPath(path);
        currentDataEncodingProfile = definitionLoadedFromCopybook
            ? DataEncodingProfile.Utf8WithNationalText
            : DataEncodingProfile.ShiftJis;
        currentNationalTextEncoding = NationalTextEncoding.ShiftJis;
        fields.Clear();
        fields.AddRange(definitionLoadedFromCopybook
            ? CopybookDefinitionReader.Read(path)
            : DefinitionCsvReader.Read(path));
        hiddenFieldIndexes.Clear();
        SetPathText(definitionPathComboBox, path);
        AddRecentFile(recentDefinitionFiles, path);
        UpdateSeparatorModeText();
        UpdateSampleHintText();
    }

    private static bool IsCopybookPath(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".cbl", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".cpy", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".cob", StringComparison.OrdinalIgnoreCase);
    }

    private void LoadData(string path, RecordSeparatorMode separatorMode, NationalTextEncoding nationalTextEncoding)
    {
        EnsureKnownSampleSelection(path, separatorMode, nationalTextEncoding);
        ApplyNationalTextEncoding(nationalTextEncoding);

        records.Clear();
        records.AddRange(FixedLengthDataReader.Read(path, fields, separatorMode, currentDataEncodingProfile, currentNationalTextEncoding));
        NormalizeRecords();
        currentSeparatorMode = separatorMode;
        saveDataPath = path;
        SetPathText(dataPathComboBox, path);
        AddRecentFile(recentDataFiles, path);
        UpdateSeparatorModeText();
        UpdateSampleHintText();
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
        UpdateSampleHintText();
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

    private void SetPathText(ComboBox comboBox, string path)
    {
        isUpdatingPathComboBoxes = true;
        try
        {
            comboBox.Text = path;
            comboBox.SelectionStart = comboBox.Text.Length;
            comboBox.SelectionLength = 0;
        }
        finally
        {
            isUpdatingPathComboBoxes = false;
        }
    }

    private void LoadDefinitionFromHistory()
    {
        if (isUpdatingPathComboBoxes)
        {
            return;
        }

        var path = definitionPathComboBox.SelectedItem?.ToString();
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        LoadDefinitionFromPath(path, "履歴から定義ファイルを読み込みました。");
    }

    private void LoadDefinitionFromEnteredPath(KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Enter || isUpdatingPathComboBoxes)
        {
            return;
        }

        e.SuppressKeyPress = true;
        var path = definitionPathComboBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        LoadDefinitionFromPath(path, "入力された定義ファイルを読み込みました。");
    }

    private void LoadDefinitionFromPath(string path, string statusMessage)
    {
        try
        {
            LoadDefinition(path);
            dataPathComboBox.Text = string.Empty;
            saveDataPath = null;
            records.Clear();
            records.Add(CreateEmptyRecord());
            RefreshGrid();
            SetStatus(statusMessage);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "定義読込エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void LoadDataFromHistory()
    {
        if (isUpdatingPathComboBoxes)
        {
            return;
        }

        var path = dataPathComboBox.SelectedItem?.ToString();
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        LoadDataFromPath(path, "履歴からデータファイルを読み込みました。");
    }

    private void LoadDataFromEnteredPath(KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Enter || isUpdatingPathComboBoxes)
        {
            return;
        }

        e.SuppressKeyPress = true;
        var path = dataPathComboBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        LoadDataFromPath(path, "入力されたデータファイルを読み込みました。");
    }

    private void LoadDataFromPath(string path, string statusMessage)
    {
        if (fields.Count == 0)
        {
            MessageBox.Show(this, "先に定義ファイルを読み込んでください。", "データ読込", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            var loadOptions = SelectDataLoadOptions(path);
            if (loadOptions is null)
            {
                return;
            }

            LoadData(path, loadOptions.SeparatorMode, loadOptions.NationalTextEncoding);
            RefreshGrid();
            SetStatus(statusMessage);
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
        isUpdatingPathComboBoxes = true;
        try
        {
            var definitionText = definitionPathComboBox.Text;
            var dataText = dataPathComboBox.Text;
            definitionPathComboBox.Items.Clear();
            dataPathComboBox.Items.Clear();
            definitionPathComboBox.Items.AddRange(recentDefinitionFiles.Cast<object>().ToArray());
            dataPathComboBox.Items.AddRange(recentDataFiles.Cast<object>().ToArray());
            definitionPathComboBox.Text = definitionText;
            dataPathComboBox.Text = dataText;
        }
        finally
        {
            isUpdatingPathComboBoxes = false;
        }
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

    private static NationalTextEncoding? ParseNationalTextEncoding(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().ToLowerInvariant().Replace("_", "").Replace("-", "") switch
        {
            "shiftjis" or "sjis" => NationalTextEncoding.ShiftJis,
            "utf8" => NationalTextEncoding.Utf8,
            "utf16" or "utf16le" => NationalTextEncoding.Utf16,
            "utf32" or "utf32le" => NationalTextEncoding.Utf32,
            _ => null
        };
    }

    private DataLoadOptions? SelectDataLoadOptions(string dataPath)
    {
        var samplePattern = FindCopybookSamplePattern(dataPath);
        var canUseSampleDefaults = samplePattern?.MatchesDefinition(Path.GetFileName(GetCurrentDefinitionPath())) == true;
        var defaultSeparatorMode = canUseSampleDefaults
            ? samplePattern!.SeparatorMode
            : currentSeparatorMode;
        var defaultNationalTextEncoding = canUseSampleDefaults
            ? samplePattern!.NationalTextEncoding
            : currentNationalTextEncoding;

        using var form = new Form
        {
            Text = "データ読込条件",
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.Sizable,
            MinimizeBox = false,
            MaximizeBox = true,
            ClientSize = new Size(760, 540),
            MinimumSize = new Size(680, 500)
        };

        var separatorGroup = new GroupBox
        {
            Text = "1. レコード区切り",
            Location = new Point(16, 14),
            Size = new Size(340, 86),
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

        var nationalEncodingGroup = new GroupBox
        {
            Text = "2. 型N 文字コード",
            Location = new Point(380, 14),
            Size = new Size(340, 126),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };

        var options = new[]
        {
            (Encoding: NationalTextEncoding.ShiftJis, Text: "Shift_JIS"),
            (Encoding: NationalTextEncoding.Utf8, Text: "UTF-8"),
            (Encoding: NationalTextEncoding.Utf16, Text: "UTF-16LE"),
            (Encoding: NationalTextEncoding.Utf32, Text: "UTF-32LE")
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
            Location = new Point(580, 494),
            Width = 75,
            Anchor = AnchorStyles.Right | AnchorStyles.Bottom
        };
        var cancelButton = new Button
        {
            Text = "キャンセル",
            DialogResult = DialogResult.Cancel,
            Location = new Point(664, 494),
            Width = 75,
            Anchor = AnchorStyles.Right | AnchorStyles.Bottom
        };
        var previewButton = new Button
        {
            Text = "プレビュー更新",
            Location = new Point(16, 494),
            Width = 120,
            Anchor = AnchorStyles.Left | AnchorStyles.Bottom
        };
        var previewLabel = new Label
        {
            Text = "プレビュー（先頭5レコード）",
            Location = new Point(16, 152),
            AutoSize = true
        };
        var previewStatusLabel = new Label
        {
            Location = new Point(190, 152),
            Size = new Size(550, 20),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            TextAlign = ContentAlignment.MiddleLeft
        };
        var previewGrid = new DataGridView
        {
            Location = new Point(16, 174),
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.CellSelect,
            MultiSelect = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
            Size = new Size(724, 304),
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
            return new DataLoadOptions(separatorMode, nationalTextEncoding);
        }

        void UpdatePreview()
        {
            try
            {
                var options = CurrentOptions();
                EnsureKnownSampleSelection(dataPath, options.SeparatorMode, options.NationalTextEncoding);
                var previewFields = BuildFieldsForNationalTextEncoding(options.NationalTextEncoding);
                var previewRecords = FixedLengthDataReader.Read(
                    dataPath,
                    previewFields,
                    options.SeparatorMode,
                    currentDataEncodingProfile,
                    options.NationalTextEncoding);
                ShowDataPreview(previewGrid, previewStatusLabel, previewFields, previewRecords);
            }
            catch (Exception ex)
            {
                ShowDataPreviewError(previewGrid, previewStatusLabel, ex.Message);
            }
        }

        previewButton.Click += (_, _) => UpdatePreview();
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

        form.Controls.AddRange([separatorGroup, nationalEncodingGroup, previewLabel, previewStatusLabel, previewGrid, previewButton, okButton, cancelButton]);
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
        IReadOnlyList<List<string>> previewRecords)
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

        previewGrid.Columns.Add(CreateReadOnlyColumn("Record", "レコード", 80, frozen: false));
        foreach (var fieldIndex in displayFieldIndexes)
        {
            previewGrid.Columns.Add(CreateReadOnlyColumn($"Field{fieldIndex}", previewFields[fieldIndex].Name, 120, frozen: false));
        }

        for (var recordIndex = 0; recordIndex < previewRecords.Take(5).Count(); recordIndex++)
        {
            var record = previewRecords[recordIndex];
            var rowIndex = previewGrid.Rows.Add();
            var row = previewGrid.Rows[rowIndex];
            row.Cells[0].Value = $"Rec {recordIndex + 1}";
            for (var visibleIndex = 0; visibleIndex < displayFieldIndexes.Count; visibleIndex++)
            {
                var fieldIndex = displayFieldIndexes[visibleIndex];
                row.Cells[visibleIndex + 1].Value = FormatValueForDisplay(previewFields[fieldIndex], record[fieldIndex]);
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
        return fields.Select(field => field.Type switch
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

        using var form = new DefinitionEditorForm(editCurrent ? fields : []);
        if (form.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(form.SavedPath))
        {
            return;
        }

        try
        {
            LoadDefinition(form.SavedPath);
            dataPathComboBox.Text = string.Empty;
            saveDataPath = null;
            records.Clear();
            records.Add(CreateEmptyRecord());
            RefreshGrid();
            SetStatus(editCurrent ? "定義ファイルを修正して読み込みました。" : "定義ファイルを作成して読み込みました。");
        }
        catch (Exception ex)
        {
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
                record.Add(field.Type switch
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
        RefreshGrid();
        SelectRecord(Math.Min(index, records.Count - 1));
    }

    private void SaveDataOverwrite()
    {
        grid.EndEdit();
        if (!ValidateBeforeSave())
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(saveDataPath))
        {
            SaveDataAs();
            return;
        }

        try
        {
            FixedLengthDataWriter.Write(saveDataPath, fields, records, currentSeparatorMode, currentDataEncodingProfile, currentNationalTextEncoding);
            SetStatus($"上書き保存しました: {Path.GetFileName(saveDataPath)}");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "保存エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void SaveDataAs()
    {
        grid.EndEdit();
        if (!ValidateBeforeSave())
        {
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Filter = "固定長データ (*.dat)|*.dat|テキスト (*.txt)|*.txt|すべてのファイル (*.*)|*.*",
            Title = "固定長データを保存",
            FileName = string.IsNullOrWhiteSpace(saveDataPath) ? "sample.dat" : Path.GetFileName(saveDataPath)
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            FixedLengthDataWriter.Write(dialog.FileName, fields, records, currentSeparatorMode, currentDataEncodingProfile, currentNationalTextEncoding);
            saveDataPath = dialog.FileName;
            SetPathText(dataPathComboBox, dialog.FileName);
            AddRecentFile(recentDataFiles, dialog.FileName);
            SetStatus($"保存しました: {Path.GetFileName(dialog.FileName)}");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "保存エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
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
            FileName = $"FixedDataBuilder_{layoutName}.xlsx"
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

        try
        {
            var imported = ExcelImporter.Read(dialog.FileName);
            ValidateImportedDefinition(imported.Fields);

            definitionLoadedFromCopybook = false;
            currentDataEncodingProfile = DataEncodingProfile.ShiftJis;
            currentNationalTextEncoding = NationalTextEncoding.ShiftJis;
            fields.Clear();
            fields.AddRange(imported.Fields);
            hiddenFieldIndexes.Clear();
            records.Clear();
            records.AddRange(imported.Records.Select(record => record.ToList()));
            saveDataPath = null;
            SetPathText(definitionPathComboBox, $"(Excel) {dialog.FileName}");
            SetPathText(dataPathComboBox, dialog.FileName);
            layout = imported.LayoutName == "レコード縦" ? GridLayout.RecordRows : GridLayout.FieldRows;
            NormalizeRecords();
            RefreshGrid();
            ValidateRecords(showSuccess: false);
            SetStatus($"Excelから取り込みました: {Path.GetFileName(dialog.FileName)}");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Excel取込エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
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
                var kind = IsDefinitionCellForExcel(columnIndex) ? ExcelCellKind.Definition : ExcelCellKind.Normal;
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
            : columnIndex == 0;
    }

    private static void OpenGeneratedFile(string path)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
    }

    private bool ValidateBeforeSave()
    {
        var structuralErrors = ValidateStructureBeforeSave();
        if (structuralErrors.Count > 0)
        {
            MessageBox.Show(this, string.Join(Environment.NewLine, structuralErrors), "保存前チェックエラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            SetStatus($"保存前チェックエラー: {structuralErrors.Count} 件");
            return false;
        }

        return ValidateRecords(showSuccess: false);
    }

    private IReadOnlyList<string> ValidateStructureBeforeSave()
    {
        var errors = new List<string>();
        if (fields.Count == 0)
        {
            errors.Add("定義ファイルが読み込まれていません。");
        }
        if (records.Count == 0)
        {
            errors.Add("保存するレコードがありません。");
        }

        for (var recordIndex = 0; recordIndex < records.Count; recordIndex++)
        {
            if (records[recordIndex].Count != fields.Count)
            {
                errors.Add($"Rec {recordIndex + 1}: 定義項目数 {fields.Count} とデータ項目数 {records[recordIndex].Count} が一致しません。");
                continue;
            }

            try
            {
                FixedLengthDataWriter.EncodeRecord(fields, records[recordIndex], currentDataEncodingProfile, currentNationalTextEncoding);
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
        var errors = RecordValidator.Validate(fields, records);
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

    private void ChangeLayout(GridLayout nextLayout)
    {
        if (layout == nextLayout)
        {
            return;
        }

        grid.EndEdit();
        layout = nextLayout;
        RefreshGrid();
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
            var fieldRowsFieldIndex = FieldIndexFromVisibleIndex(rowIndex);
            if (columnIndex < 2 || fieldRowsFieldIndex < 0)
            {
                return;
            }

            var recordIndex = columnIndex - 2;
            if (recordIndex < 0 || recordIndex >= records.Count)
            {
                return;
            }

            var field = fields[fieldRowsFieldIndex];
            if (field.IsRedefines)
            {
                return;
            }

            var formattedValue = FormatValueForDisplay(field, grid[columnIndex, rowIndex].Value?.ToString() ?? string.Empty);
            records[recordIndex][fieldRowsFieldIndex] = formattedValue;
            grid[columnIndex, rowIndex].Value = formattedValue;
            ApplyCellValidationStyle(grid[columnIndex, rowIndex], field, formattedValue);
            UpdateHexView();
            return;
        }

        if (columnIndex < 1 || rowIndex >= records.Count)
        {
            return;
        }

        var fieldIndex = FieldIndexFromVisibleIndex(columnIndex - 1);
        if (fieldIndex < 0 || fieldIndex >= fields.Count)
        {
            return;
        }

        var recordRowsField = fields[fieldIndex];
        if (recordRowsField.IsRedefines)
        {
            return;
        }

        var recordRowsFormattedValue = FormatValueForDisplay(recordRowsField, grid[columnIndex, rowIndex].Value?.ToString() ?? string.Empty);
        records[rowIndex][fieldIndex] = recordRowsFormattedValue;
        grid[columnIndex, rowIndex].Value = recordRowsFormattedValue;
        ApplyCellValidationStyle(grid[columnIndex, rowIndex], recordRowsField, recordRowsFormattedValue);
        UpdateHexView();
    }

    private int CurrentRecordIndex()
    {
        if (grid.CurrentCell is null)
        {
            return records.Count - 1;
        }

        return layout == GridLayout.FieldRows
            ? grid.CurrentCell.ColumnIndex >= 2 ? grid.CurrentCell.ColumnIndex - 2 : records.Count - 1
            : grid.CurrentCell.RowIndex;
    }

    private int CurrentFieldIndex()
    {
        if (grid.CurrentCell is null || fields.Count == 0)
        {
            return -1;
        }

        return layout == GridLayout.FieldRows
            ? FieldIndexFromVisibleIndex(grid.CurrentCell.RowIndex)
            : grid.CurrentCell.ColumnIndex >= 1 ? FieldIndexFromVisibleIndex(grid.CurrentCell.ColumnIndex - 1) : -1;
    }

    private void SelectRecord(int recordIndex)
    {
        if (recordIndex < 0 || fields.Count == 0 || records.Count == 0 || VisibleFieldIndexes().Count == 0)
        {
            return;
        }

        grid.CurrentCell = layout == GridLayout.FieldRows
            ? grid[recordIndex + 2, 0]
            : grid[1, recordIndex];
        UpdateHexView();
    }

    private void RefreshGrid()
    {
        isRefreshingGrid = true;
        try
        {
            NormalizeRecords();
            grid.Columns.Clear();
            grid.Rows.Clear();
            UpdateLayoutButtons();

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
        }
        finally
        {
            isRefreshingGrid = false;
        }
        UpdateHexView();
    }

    private void RefreshFieldRowsGrid()
    {
        grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.EnableResizing;
        grid.ColumnHeadersHeight = 28;
        grid.Columns.Add(CreateReadOnlyColumn("Name", "項目名", 180, frozen: true));
        grid.Columns.Add(CreateReadOnlyColumn("Definition", "定義", 150, frozen: true));
        ApplyDefinitionColumnStyle(grid.Columns[0]);
        ApplyDefinitionColumnStyle(grid.Columns[1]);

        for (var recordIndex = 0; recordIndex < records.Count; recordIndex++)
        {
            grid.Columns.Add(CreateEditableColumn($"Record{recordIndex + 1}", $"Rec {recordIndex + 1}", 140));
        }

        foreach (var fieldIndex in VisibleFieldIndexes())
        {
            var field = fields[fieldIndex];
            var rowIndex = grid.Rows.Add();
            var row = grid.Rows[rowIndex];
            row.Cells[0].Value = field.Name;
            row.Cells[1].Value = field.DisplayDefinition;

            for (var recordIndex = 0; recordIndex < records.Count; recordIndex++)
            {
                var value = FormatValueForDisplay(field, records[recordIndex][fieldIndex]);
                row.Cells[recordIndex + 2].Value = value;
                row.Cells[recordIndex + 2].ReadOnly = field.IsRedefines;
                ApplyCellValidationStyle(row.Cells[recordIndex + 2], field, value);
            }
        }
    }

    private void RefreshRecordRowsGrid()
    {
        grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.EnableResizing;
        grid.ColumnHeadersHeight = 64;
        grid.Columns.Add(CreateReadOnlyColumn("Record", "レコード", 90, frozen: true));
        ApplyDefinitionColumnStyle(grid.Columns[0]);

        var byteStart = 1;
        for (var fieldIndex = 0; fieldIndex < fields.Count; fieldIndex++)
        {
            var field = fields[fieldIndex];
            var byteEnd = byteStart + field.StorageByteLength - 1;
            if (IsDisplayableField(field) && !hiddenFieldIndexes.Contains(fieldIndex))
            {
                var ruler = $"byte {byteStart}-{byteEnd}";
                var column = CreateEditableColumn(
                    $"Field{fieldIndex + 1}",
                    $"{field.Name}{Environment.NewLine}{field.DisplayDefinition}{Environment.NewLine}{ruler}",
                    170);
                column.HeaderCell.Style.BackColor = DefinitionHeaderBackColor;
                grid.Columns.Add(column);
            }

            if (!field.IsRedefines)
            {
                byteStart = byteEnd + 1;
            }
        }

        var visibleFieldIndexes = VisibleFieldIndexes();
        for (var recordIndex = 0; recordIndex < records.Count; recordIndex++)
        {
            var rowIndex = grid.Rows.Add();
            var row = grid.Rows[rowIndex];
            row.Cells[0].Value = $"Rec {recordIndex + 1}";

            for (var visibleIndex = 0; visibleIndex < visibleFieldIndexes.Count; visibleIndex++)
            {
                var fieldIndex = visibleFieldIndexes[visibleIndex];
                var field = fields[fieldIndex];
                var value = FormatValueForDisplay(field, records[recordIndex][fieldIndex]);
                row.Cells[visibleIndex + 1].Value = value;
                row.Cells[visibleIndex + 1].ReadOnly = field.IsRedefines;
                ApplyCellValidationStyle(row.Cells[visibleIndex + 1], field, value);
            }
        }
    }

    private void UpdateHexView()
    {
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

        try
        {
            var field = fields[fieldIndex];
            var bytes = FixedLengthDataWriter.EncodeField(field, records[recordIndex][fieldIndex], currentDataEncodingProfile, currentNationalTextEncoding);
            hexTextBox.Text = string.Join(" ", bytes.Select(value => value.ToString("X2")));
        }
        catch (Exception ex)
        {
            hexTextBox.Text = $"HEX 表示エラー: {ex.Message}";
        }
    }

    private static void ApplyCellValidationStyle(DataGridViewCell cell, FieldDefinition field, string value)
    {
        var error = GetValidationError(field, value);
        cell.Style.BackColor = error is not null
            ? ErrorBackColor
            : Color.Empty;
        cell.ToolTipText = error ?? string.Empty;
    }

    private void PaintTextPaddingArea(DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex < 0
            || e.ColumnIndex < 0
            || e.Graphics is null
            || e.CellStyle is null
            || !TryGetCellField(e.RowIndex, e.ColumnIndex, out var field)
            || field.Type is not (FieldDataType.Text or FieldDataType.HalfWidthText or FieldDataType.FullWidthText))
        {
            return;
        }

        var value = e.FormattedValue?.ToString() ?? string.Empty;
        if (GetValidationError(field, value) is not null)
        {
            return;
        }

        var usedLength = field.Type switch
        {
            FieldDataType.FullWidthText => value.Length,
            FieldDataType.HalfWidthText => value.Length,
            _ => ShiftJisEncoding.GetByteCount(value)
        };
        var remainingLength = field.Length - usedLength;
        if (remainingLength <= 0 && !ContainsVisibleSpace(value))
        {
            return;
        }

        e.Paint(e.CellBounds, e.PaintParts & ~DataGridViewPaintParts.ContentForeground);

        var padding = e.CellStyle.Padding;
        var textBounds = new Rectangle(
            e.CellBounds.Left + padding.Left + 3,
            e.CellBounds.Top + padding.Top + 1,
            e.CellBounds.Width - padding.Left - padding.Right - 6,
            e.CellBounds.Height - padding.Top - padding.Bottom - 2);
        var font = e.CellStyle.Font ?? grid.Font;
        var visibleValue = BuildVisibleTextValue(field, value, remainingLength);
        PaintVisibleSpaceHighlights(e.Graphics, font, textBounds, visibleValue);
        TextRenderer.DrawText(
            e.Graphics,
            visibleValue,
            font,
            textBounds,
            e.CellStyle.ForeColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        e.Handled = true;
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

    private static bool ContainsVisibleSpace(string value)
    {
        return value.Contains(' ') || value.Contains('\u3000');
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
            var fieldIndex = FieldIndexFromVisibleIndex(rowIndex);
            if (columnIndex >= 2 && fieldIndex >= 0)
            {
                field = fields[fieldIndex];
                return true;
            }
        }
        else if (columnIndex >= 1)
        {
            var fieldIndex = FieldIndexFromVisibleIndex(columnIndex - 1);
            if (fieldIndex >= 0 && fieldIndex < fields.Count)
            {
                field = fields[fieldIndex];
                return true;
            }
        }

        field = null!;
        return false;
    }

    private static string? GetValidationError(FieldDefinition field, string value)
    {
        value = value.Trim();
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
                return ShiftJisEncoding.GetByteCount(value) <= field.Length
                    ? null
                    : $"Shift_JIS バイト長 {field.Length} を超えています。";

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
        fieldRowsButton.Checked = layout == GridLayout.FieldRows;
        recordRowsButton.Checked = layout == GridLayout.RecordRows;
    }

    private void UpdateSeparatorModeText()
    {
        var separatorText = currentSeparatorMode == RecordSeparatorMode.None
            ? "改行なし"
            : "改行あり (CRLF/LF)";
        separatorModeTextBox.Text = $"{separatorText} / 型N: {NationalTextEncodingHelper.DisplayName(currentNationalTextEncoding)}";
        UpdateSampleHintText();
    }

    private void UpdateSampleHintText()
    {
        var samplePattern = FindCopybookSamplePattern(GetCurrentDataPath());
        sampleHintTextBox.Text = samplePattern is null
            ? "コピー句サンプルは、ファイル名に合う定義・改行・型N文字コードで読み込んでください。対象外はエラーまたは文字化けします。"
            : $"{samplePattern.FileName}: {samplePattern.DefinitionFileName} + {FormatSeparatorMode(samplePattern.SeparatorMode)} + 型N={NationalTextEncodingHelper.DisplayName(samplePattern.NationalTextEncoding)} 用 / 対象外はエラーまたは文字化けします。";
        sampleHintTextBox.SelectionStart = sampleHintTextBox.TextLength;
    }

    private string? GetCurrentDefinitionPath()
    {
        return definitionPathComboBox.Text;
    }

    private string? GetCurrentDataPath()
    {
        return dataPathComboBox.Text;
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

    private sealed record DataLoadOptions(
        RecordSeparatorMode SeparatorMode,
        NationalTextEncoding NationalTextEncoding);

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

    private static string FormatValueForDisplay(FieldDefinition field, string value)
    {
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
