using System.Text;
using System.Diagnostics;

namespace FixedDataBuilder;

public sealed class MainForm : Form
{
    private static readonly Color DefinitionBackColor = Color.FromArgb(226, 242, 226);
    private static readonly Color DefinitionHeaderBackColor = Color.FromArgb(204, 232, 204);
    private static readonly Color ErrorBackColor = Color.FromArgb(255, 225, 225);
    private static readonly Encoding ShiftJisEncoding = Encoding.GetEncoding("shift_jis");
    private static readonly string RecentFilesPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FixedDataBuilder",
        "recent-files.txt");

    private readonly ComboBox definitionPathComboBox = new();
    private readonly ComboBox dataPathComboBox = new();
    private readonly TextBox separatorModeTextBox = new();
    private readonly TextBox hexTextBox = new();
    private readonly DataGridView grid = new();
    private readonly ToolStrip toolStrip = new();
    private readonly StatusStrip statusStrip = new();
    private readonly ToolStripStatusLabel statusLabel = new();
    private readonly ToolStripButton fieldRowsButton;
    private readonly ToolStripButton recordRowsButton;
    private readonly List<string> recentDefinitionFiles = [];
    private readonly List<string> recentDataFiles = [];
    private readonly List<FieldDefinition> fields = [];
    private readonly List<List<string>> records = [];
    private string? saveDataPath;
    private RecordSeparatorMode currentSeparatorMode = RecordSeparatorMode.CrLfOrLf;
    private GridLayout layout = GridLayout.FieldRows;
    private bool isUpdatingPathComboBoxes;

    public MainForm(string[]? args = null)
    {
        Text = $"FixedDataBuilder v{Application.ProductVersion}";
        StartPosition = FormStartPosition.CenterScreen;
        Width = 1100;
        Height = 740;

        fieldRowsButton = CreateButton("表示: 項目縦", (_, _) => ChangeLayout(GridLayout.FieldRows));
        recordRowsButton = CreateButton("表示: レコード縦", (_, _) => ChangeLayout(GridLayout.RecordRows));

        LoadRecentFiles();

        Controls.Add(grid);
        Controls.Add(BuildHexPanel());
        Controls.Add(statusStrip);
        Controls.Add(toolStrip);
        Controls.Add(BuildFilePanel());

        BuildToolStrip();
        BuildGrid();

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
            RowCount = 3,
            Padding = new Padding(8, 6, 8, 4)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));

        ConfigurePathComboBox(definitionPathComboBox);
        ConfigurePathComboBox(dataPathComboBox);
        ConfigureReadOnlyTextBox(separatorModeTextBox);
        RefreshRecentFileItems();

        definitionPathComboBox.SelectionChangeCommitted += (_, _) => LoadDefinitionFromHistory();
        dataPathComboBox.SelectionChangeCommitted += (_, _) => LoadDataFromHistory();

        panel.Controls.Add(CreatePathLabel("定義ファイル"), 0, 0);
        panel.Controls.Add(definitionPathComboBox, 1, 0);
        panel.Controls.Add(CreatePathButton("選択", (_, _) => OpenDefinition()), 2, 0);
        panel.Controls.Add(CreatePathLabel("データファイル"), 0, 1);
        panel.Controls.Add(dataPathComboBox, 1, 1);
        panel.Controls.Add(CreatePathButton("選択", (_, _) => OpenData()), 2, 1);
        panel.Controls.Add(CreatePathLabel("レコード区切り"), 0, 2);
        panel.Controls.Add(separatorModeTextBox, 1, 2);
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

    private void BuildToolStrip()
    {
        toolStrip.Dock = DockStyle.Top;
        toolStrip.GripStyle = ToolStripGripStyle.Hidden;
        toolStrip.Items.Add(CreateButton("追加", (_, _) => AddRecord()));
        toolStrip.Items.Add(CreateButton("複製", (_, _) => DuplicateRecord()));
        toolStrip.Items.Add(CreateButton("削除", (_, _) => DeleteRecord()));
        toolStrip.Items.Add(new ToolStripSeparator());
        toolStrip.Items.Add(CreateButton("定義作成", (_, _) => CreateDefinition(editCurrent: false)));
        toolStrip.Items.Add(CreateButton("定義修正", (_, _) => CreateDefinition(editCurrent: true)));
        toolStrip.Items.Add(new ToolStripSeparator());
        toolStrip.Items.Add(fieldRowsButton);
        toolStrip.Items.Add(recordRowsButton);
        toolStrip.Items.Add(new ToolStripSeparator());
        toolStrip.Items.Add(CreateButton("検証", (_, _) => ValidateRecords(showSuccess: true)));
        toolStrip.Items.Add(CreateButton("上書き保存", (_, _) => SaveDataOverwrite()));
        toolStrip.Items.Add(CreateButton("名前を付けて保存", (_, _) => SaveDataAs()));
        toolStrip.Items.Add(new ToolStripSeparator());
        toolStrip.Items.Add(CreateButton("Excel出力", (_, _) => ExportExcel()));
        toolStrip.Items.Add(CreateButton("Excel取込", (_, _) => ImportExcel()));
    }

    private static ToolStripButton CreateButton(string text, EventHandler onClick)
    {
        var button = new ToolStripButton(text)
        {
            DisplayStyle = ToolStripItemDisplayStyle.Text,
            AutoToolTip = false
        };
        button.Click += onClick;
        return button;
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
        grid.SelectionChanged += (_, _) => UpdateHexView();
    }

    private void OpenDefinition()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "CSV ファイル (*.csv)|*.csv|すべてのファイル (*.*)|*.*",
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
            var separatorMode = SelectRecordSeparatorMode();
            if (separatorMode is null)
            {
                return;
            }

            LoadData(dialog.FileName, separatorMode.Value);
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
                LoadData(dataPath, separatorMode);
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
        fields.Clear();
        fields.AddRange(DefinitionCsvReader.Read(path));
        SetPathText(definitionPathComboBox, path);
        AddRecentFile(recentDefinitionFiles, path);
    }

    private void LoadData(string path, RecordSeparatorMode separatorMode)
    {
        records.Clear();
        records.AddRange(FixedLengthDataReader.Read(path, fields, separatorMode));
        NormalizeRecords();
        currentSeparatorMode = separatorMode;
        saveDataPath = path;
        SetPathText(dataPathComboBox, path);
        AddRecentFile(recentDataFiles, path);
        UpdateSeparatorModeText();
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
        if (isUpdatingPathComboBoxes || string.IsNullOrWhiteSpace(definitionPathComboBox.Text))
        {
            return;
        }

        try
        {
            LoadDefinition(definitionPathComboBox.Text);
            dataPathComboBox.Text = string.Empty;
            saveDataPath = null;
            records.Clear();
            records.Add(CreateEmptyRecord());
            RefreshGrid();
            SetStatus("履歴から定義ファイルを読み込みました。");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "定義読込エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void LoadDataFromHistory()
    {
        if (isUpdatingPathComboBoxes || string.IsNullOrWhiteSpace(dataPathComboBox.Text))
        {
            return;
        }

        if (fields.Count == 0)
        {
            MessageBox.Show(this, "先に定義ファイルを読み込んでください。", "データ読込", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            var separatorMode = SelectRecordSeparatorMode();
            if (separatorMode is null)
            {
                return;
            }

            LoadData(dataPathComboBox.Text, separatorMode.Value);
            RefreshGrid();
            SetStatus("履歴からデータファイルを読み込みました。");
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

    private RecordSeparatorMode? SelectRecordSeparatorMode()
    {
        using var form = new Form
        {
            Text = "レコード区切り",
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MinimizeBox = false,
            MaximizeBox = false,
            ClientSize = new Size(300, 150)
        };

        var lineBreakRadio = new RadioButton
        {
            Text = "改行あり (CRLF/LF)",
            Checked = currentSeparatorMode == RecordSeparatorMode.CrLfOrLf,
            Location = new Point(20, 20),
            AutoSize = true
        };
        var noLineBreakRadio = new RadioButton
        {
            Text = "改行なし",
            Checked = currentSeparatorMode == RecordSeparatorMode.None,
            Location = new Point(20, 50),
            AutoSize = true
        };
        var okButton = new Button
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            Location = new Point(120, 100),
            Width = 75
        };
        var cancelButton = new Button
        {
            Text = "キャンセル",
            DialogResult = DialogResult.Cancel,
            Location = new Point(200, 100),
            Width = 75
        };

        form.Controls.AddRange([lineBreakRadio, noLineBreakRadio, okButton, cancelButton]);
        form.AcceptButton = okButton;
        form.CancelButton = cancelButton;

        if (form.ShowDialog(this) != DialogResult.OK)
        {
            return null;
        }

        return noLineBreakRadio.Checked ? RecordSeparatorMode.None : RecordSeparatorMode.CrLfOrLf;
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
            FixedLengthDataWriter.Write(saveDataPath, fields, records, currentSeparatorMode);
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
            FixedLengthDataWriter.Write(dialog.FileName, fields, records, currentSeparatorMode);
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

            fields.Clear();
            fields.AddRange(imported.Fields);
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
                FixedLengthDataWriter.EncodeRecord(fields, records[recordIndex]);
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
            if (columnIndex < 2 || rowIndex >= fields.Count)
            {
                return;
            }

            var recordIndex = columnIndex - 2;
            if (recordIndex < 0 || recordIndex >= records.Count)
            {
                return;
            }

            var formattedValue = FormatValueForDisplay(fields[rowIndex], grid[columnIndex, rowIndex].Value?.ToString() ?? string.Empty);
            records[recordIndex][rowIndex] = formattedValue;
            grid[columnIndex, rowIndex].Value = formattedValue;
            ApplyCellValidationStyle(grid[columnIndex, rowIndex], fields[rowIndex], formattedValue);
            UpdateHexView();
            return;
        }

        if (columnIndex < 1 || rowIndex >= records.Count)
        {
            return;
        }

        var fieldIndex = columnIndex - 1;
        if (fieldIndex < 0 || fieldIndex >= fields.Count)
        {
            return;
        }

        var recordRowsFormattedValue = FormatValueForDisplay(fields[fieldIndex], grid[columnIndex, rowIndex].Value?.ToString() ?? string.Empty);
        records[rowIndex][fieldIndex] = recordRowsFormattedValue;
        grid[columnIndex, rowIndex].Value = recordRowsFormattedValue;
        ApplyCellValidationStyle(grid[columnIndex, rowIndex], fields[fieldIndex], recordRowsFormattedValue);
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
            ? grid.CurrentCell.RowIndex
            : grid.CurrentCell.ColumnIndex >= 1 ? grid.CurrentCell.ColumnIndex - 1 : -1;
    }

    private void SelectRecord(int recordIndex)
    {
        if (recordIndex < 0 || fields.Count == 0 || records.Count == 0)
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

        SetStatus($"{fields.Count} 項目 / {records.Count} レコード");
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

        for (var fieldIndex = 0; fieldIndex < fields.Count; fieldIndex++)
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
                ApplyCellValidationStyle(row.Cells[recordIndex + 2], field, value);
            }
        }
    }

    private void RefreshRecordRowsGrid()
    {
        grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.EnableResizing;
        grid.ColumnHeadersHeight = 46;
        grid.Columns.Add(CreateReadOnlyColumn("Record", "レコード", 90, frozen: true));
        ApplyDefinitionColumnStyle(grid.Columns[0]);

        for (var fieldIndex = 0; fieldIndex < fields.Count; fieldIndex++)
        {
            var field = fields[fieldIndex];
            var column = CreateEditableColumn($"Field{fieldIndex + 1}", $"{field.Name}{Environment.NewLine}{field.DisplayDefinition}", 170);
            column.HeaderCell.Style.BackColor = DefinitionHeaderBackColor;
            grid.Columns.Add(column);
        }

        for (var recordIndex = 0; recordIndex < records.Count; recordIndex++)
        {
            var rowIndex = grid.Rows.Add();
            var row = grid.Rows[rowIndex];
            row.Cells[0].Value = $"Rec {recordIndex + 1}";

            for (var fieldIndex = 0; fieldIndex < fields.Count; fieldIndex++)
            {
                var field = fields[fieldIndex];
                var value = FormatValueForDisplay(field, records[recordIndex][fieldIndex]);
                row.Cells[fieldIndex + 1].Value = value;
                ApplyCellValidationStyle(row.Cells[fieldIndex + 1], field, value);
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
            var bytes = FixedLengthDataWriter.EncodeField(field, records[recordIndex][fieldIndex]);
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
        cell.Style.BackColor = error is null ? Color.Empty : ErrorBackColor;
        cell.ToolTipText = error ?? string.Empty;
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
        separatorModeTextBox.Text = currentSeparatorMode == RecordSeparatorMode.None
            ? "改行なし"
            : "改行あり (CRLF/LF)";
    }

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
