namespace FixedDataBuilder;

/// <summary>Confirm inferred CSV metadata before replacing the current document.</summary>
public sealed class CsvDefinitionImportForm : Form
{
    private readonly string path;
    private readonly ComboBox encoding = new()
    {
        Name = "CsvInferenceEncoding", DropDownStyle = ComboBoxStyle.DropDownList, Width = 200
    };
    private readonly CheckBox hasHeader = new()
    {
        Name = "CsvInferenceHasHeader", Text = "先頭行を項目名として使う（ヘッダーあり）", AutoSize = true
    };
    private readonly Label headerReason = new()
    {
        Name = "CsvInferenceHeaderReason", Dock = DockStyle.Fill, AutoSize = true,
        ForeColor = Color.FromArgb(100, 70, 0), Padding = new Padding(0, 3, 0, 3)
    };
    private readonly Label summary = new()
    {
        Name = "CsvInferenceSummary", Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(0, 4, 0, 4)
    };
    private readonly Label error = new()
    {
        Name = "CsvInferenceError", Dock = DockStyle.Fill, AutoSize = true, ForeColor = Color.Firebrick,
        Padding = new Padding(0, 3, 0, 3)
    };
    private readonly DataGridView definitionGrid = CreateGrid("CsvInferenceDefinitions");
    private readonly DataGridView previewGrid = CreateGrid("CsvInferencePreview");
    private readonly Button import = new()
    {
        Name = "CsvInferenceImport", Text = "この定義で取り込む", AutoSize = true, Enabled = false
    };
    private CsvDefinitionAnalysis? analysis;
    private CsvDefinitionInference? candidate;
    private bool changingOptions;

    public CsvDefinitionInference? Inference { get; private set; }

    public CsvDefinitionImportForm(string path)
    {
        this.path = path;
        Text = "CSVを定義なしで取り込み・定義を自動生成";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1000, 760);
        MinimumSize = new Size(820, 640);
        AutoScaleMode = AutoScaleMode.Font;
        MinimizeBox = false;
        encoding.Items.Add("自動判別（候補）");
        encoding.Items.AddRange(JapaneseEncodings.CsvNames.Cast<object>().ToArray());
        encoding.SelectedIndex = 0;
        Controls.Add(BuildLayout());
        BuildDefinitionColumns();
        encoding.SelectedIndexChanged += (_, _) => AnalyzeFile();
        hasHeader.CheckedChanged += (_, _) => { if (!changingOptions) UpdateInference(); };
        import.Click += (_, _) => ConfirmImport();
        AcceptButton = import;
        AnalyzeFile();
    }

    private Control BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Name = "CsvInferenceLayout", Dock = DockStyle.Fill, Padding = new Padding(12),
            ColumnCount = 1, RowCount = 8
        };
        for (var index = 0; index < 6; index++) root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.Controls.Add(new Label
        {
            Text = "1. 文字コードと先頭行を確認 → 2. 取り込む → 3. メイン画面の「定義 → 自動生成した定義を保存...」で保存\r\n空白・先頭ゼロ・引用符内の改行を保ち、CSVの全データから列数と長さを調べます。",
            AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(0, 0, 0, 5)
        }, 0, 0);
        root.Controls.Add(new TextBox
        {
            Name = "CsvInferencePath", Text = path, ReadOnly = true, Dock = DockStyle.Fill,
            BackColor = SystemColors.Control, TabStop = false
        }, 0, 1);
        var options = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Margin = new Padding(0, 5, 0, 0)
        };
        options.Controls.Add(new Label { Text = "CSV文字コード", AutoSize = true, Margin = new Padding(0, 6, 8, 0) });
        options.Controls.Add(encoding);
        hasHeader.Margin = new Padding(20, 6, 0, 0);
        options.Controls.Add(hasHeader);
        root.Controls.Add(options, 0, 2);
        root.Controls.Add(headerReason, 0, 3);
        root.Controls.Add(summary, 0, 4);
        root.Controls.Add(error, 0, 5);
        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Margin = Padding.Empty
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        content.Controls.Add(new Label { Text = "生成する定義（長さは全データから調査）", AutoSize = true }, 0, 0);
        content.Controls.Add(definitionGrid, 0, 1);
        content.Controls.Add(new Label
        {
            Text = "CSVの先頭5行（先頭行を省かず表示。見出しとして使う行は緑色）／長い値はプレビューだけ省略",
            AutoSize = true, Padding = new Padding(0, 6, 0, 0)
        }, 0, 2);
        content.Controls.Add(previewGrid, 0, 3);
        root.Controls.Add(content, 0, 6);
        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, RowCount = 2, Margin = new Padding(0, 5, 0, 0)
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        footer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var note = new Label
        {
            Name = "CsvInferenceNotice", Text = "長さは読み込んだ実データの観測最大です。正式な仕様の桁数・属性は分かりません。\r\nデータ行の先頭ゼロや空白を保つため、数字だけの列も含めてすべて文字列 X 型で生成します。\r\n見出しの前後空白は除き、空欄・重複名は補います。CSVの保存時は、この項目名を見出しに使います。",
            AutoSize = true, Dock = DockStyle.Fill, ForeColor = Color.FromArgb(55, 70, 85)
        };
        footer.Controls.Add(note, 0, 0);
        footer.SetColumnSpan(note, 2);
        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var cancel = new Button
        {
            Name = "CsvInferenceCancel", Text = "キャンセル", AutoSize = true, DialogResult = DialogResult.Cancel
        };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(import);
        footer.Controls.Add(buttons, 1, 1);
        CancelButton = cancel;
        root.Controls.Add(footer, 0, 7);
        return root;
    }

    private static DataGridView CreateGrid(string name) => new()
    {
        Name = name, Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false,
        AllowUserToDeleteRows = false, AllowUserToOrderColumns = false, RowHeadersVisible = false,
        MultiSelect = false, SelectionMode = DataGridViewSelectionMode.CellSelect,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
        AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None, RowTemplate = { Height = 28 },
        BackgroundColor = SystemColors.Window, EnableHeadersVisualStyles = false,
        ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.FromArgb(204, 232, 204), Alignment = DataGridViewContentAlignment.TopLeft
        },
        DefaultCellStyle = new DataGridViewCellStyle
        {
            Alignment = DataGridViewContentAlignment.TopLeft, WrapMode = DataGridViewTriState.False
        }
    };

    private void BuildDefinitionColumns()
    {
        AddColumn(definitionGrid, "Number", "No.", 55);
        AddColumn(definitionGrid, "FieldName", "項目名", 230);
        AddColumn(definitionGrid, "Definition", "生成する定義", 120);
        AddColumn(definitionGrid, "MaximumByteLength", "観測最大バイト", 125);
        AddColumn(definitionGrid, "DigitsOnly", "数字だけの列", 120);
        AddColumn(definitionGrid, "Example", "データ例", 260);
    }

    private static void AddColumn(DataGridView target, string name, string title, int width) =>
        target.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = name, HeaderText = title, Width = width, MinimumWidth = 45,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });

    private void AnalyzeFile()
    {
        Inference = null;
        candidate = null;
        analysis = null;
        import.Enabled = false;
        try
        {
            UseWaitCursor = true;
            var selectedEncoding = encoding.SelectedIndex == 0 ? (CsvTextEncoding?)null
                : (CsvTextEncoding)(encoding.SelectedIndex - 1);
            analysis = CsvDefinitionInferer.Analyze(path, selectedEncoding);
            changingOptions = true;
            try { hasHeader.Checked = analysis.SuggestedHasHeader; }
            finally { changingOptions = false; }
            headerReason.Text = analysis.HeaderReason + "　先頭行を確認して、チェックを変更できます。";
            UpdateInference();
        }
        catch (Exception ex)
        {
            analysis = null;
            ShowError("読み込めません。文字コードやCSVの形式を確認してください。\r\n" + ex.Message);
        }
        finally { UseWaitCursor = false; }
    }

    private void UpdateInference()
    {
        Inference = null;
        candidate = null;
        import.Enabled = false;
        definitionGrid.Rows.Clear();
        previewGrid.Rows.Clear();
        previewGrid.Columns.Clear();
        if (analysis is null) return;
        // The raw first row remains visible even if it is selected as a header.
        BuildPreview(analysis);
        try
        {
            var result = analysis.CreateDefinition(hasHeader.Checked);
            foreach (var column in result.Columns)
            {
                definitionGrid.Rows.Add(column.Number, column.Name, result.Fields[column.Number - 1].DisplayDefinition,
                    column.MaximumByteLength, column.IsDigitsOnly ? "はい（X型で保持）" : "いいえ", PreviewValue(column.Example));
            }
            summary.Text = $"{(encoding.SelectedIndex == 0 ? "文字コード候補" : "指定文字コード")}: {JapaneseEncodings.Name(result.Encoding)}"
                + $" ／ {result.Fields.Count} 列 ／ データ {result.Records.Count} 行 ／ 先頭行: {(result.HasHeader ? "項目名として使用" : "データとして取り込み")}";
            error.Text = string.Empty;
            candidate = result;
            import.Enabled = true;
        }
        catch (Exception ex) { ShowError("取り込めません。\r\n" + ex.Message, clearPreview: false); }
    }

    private void BuildPreview(CsvDefinitionAnalysis source)
    {
        if (source.Rows.Count == 0) return;
        AddColumn(previewGrid, "SourceRow", "CSV行", 85);
        for (var index = 0; index < source.Rows[0].Count; index++)
            AddColumn(previewGrid, "Column" + (index + 1), "No." + (index + 1), 145);
        for (var index = 0; index < Math.Min(5, source.Rows.Count); index++)
        {
            var isHeader = hasHeader.Checked && index == 0;
            var values = new object[source.Rows[index].Count + 1];
            values[0] = isHeader ? "見出し (1)" : (index + 1).ToString();
            for (var column = 0; column < source.Rows[index].Count; column++)
                values[column + 1] = PreviewValue(source.Rows[index][column]);
            var row = previewGrid.Rows[previewGrid.Rows.Add(values)];
            if (isHeader) row.DefaultCellStyle.BackColor = Color.FromArgb(226, 242, 226);
        }
    }

    private static string PreviewValue(string value)
    {
        // Only the preview is escaped/abbreviated; the inference contains exact strings.
        var text = DisplayTextLimiter.Limit(value, 160, out var truncated);
        return text.Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t") + (truncated ? "…" : string.Empty);
    }

    private void ShowError(string message, bool clearPreview = true)
    {
        Inference = null;
        candidate = null;
        import.Enabled = false;
        summary.Text = string.Empty;
        error.Text = message;
        definitionGrid.Rows.Clear();
        if (clearPreview)
        {
            headerReason.Text = string.Empty;
            previewGrid.Rows.Clear();
            previewGrid.Columns.Clear();
        }
    }

    private void ConfirmImport()
    {
        if (candidate is null || !import.Enabled) return;
        Inference = candidate;
        DialogResult = DialogResult.OK;
    }
}
