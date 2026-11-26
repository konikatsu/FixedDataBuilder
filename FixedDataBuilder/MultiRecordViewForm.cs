namespace FixedDataBuilder;

public sealed class MultiRecordViewForm : Form
{
    private sealed record ViewRow(string Label, string Kind, IReadOnlyList<string> Values, string? Warning = null,
        IReadOnlyList<FieldDefinition>? Fields = null, IReadOnlyDictionary<int, string>? FieldWarnings = null);
    private readonly TextBox definitionPath = new() { Name = "MultiDefinitionPath", Dock = DockStyle.Fill };
    private readonly TextBox dataPath = new() { Name = "MultiDataPath", Dock = DockStyle.Fill };
    private readonly NumericUpDown kindPosition = new() { Name = "MultiKindPosition", Minimum = 1, Maximum = 1000000, Value = 4, Width = 75 };
    private readonly ComboBox textEncoding = new() { Name = "MultiTextEncoding", DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
    private readonly ComboBox nationalEncoding = new() { Name = "MultiNationalEncoding", DropDownStyle = ComboBoxStyle.DropDownList, Width = 125 };
    private readonly ComboBox definitionEncoding = new() { Name = "MultiDefinitionEncoding", DropDownStyle = ComboBoxStyle.DropDownList, Width = 165 };
    private readonly ComboBox separator = new() { Name = "MultiSeparator", DropDownStyle = ComboBoxStyle.DropDownList, Width = 125 };
    private readonly Button load = new() { Name = "LoadMultiRecords", Text = "表示", AutoSize = true };
    private readonly Label status = new() { Dock = DockStyle.Fill, AutoEllipsis = true, Text = "定義CSVとデータファイルを指定してください。" };
    private readonly TextBox selectedComment = new() { Name = "MultiSelectedComment", Dock = DockStyle.Fill, ReadOnly = true,
        Multiline = true, ScrollBars = ScrollBars.Vertical, PlaceholderText = "項目を選ぶと備考を表示します。" };
    private readonly DataGridView grid = new()
    {
        Name = "MultiRecordGrid", Dock = DockStyle.Fill, ReadOnly = true, VirtualMode = true,
        AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToOrderColumns = false,
        RowHeadersVisible = false, ColumnHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
        AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None, SelectionMode = DataGridViewSelectionMode.CellSelect,
        ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableWithoutHeaderText, ScrollBars = ScrollBars.Both
    };
    private IReadOnlyList<ViewRow> rows = [];
    private readonly Font dataFont;
    private readonly Font commentFont;

    public MultiRecordViewForm(Font? font = null)
    {
        Text = "ヘッダー・明細表示（確認用）"; StartPosition = FormStartPosition.CenterParent;
        Size = new Size(1400, 850); MinimumSize = new Size(800, 500);
        dataFont = font is null ? new Font("MS Gothic", 10f) : (Font)font.Clone();
        commentFont = CommentDisplayStyle.CreateFont(dataFont);
        grid.Font = dataFont; grid.DefaultCellStyle.Alignment = DataGridViewContentAlignment.TopLeft;
        textEncoding.Items.AddRange(JapaneseEncodings.TextNames.Cast<object>().ToArray()); textEncoding.SelectedIndex = 0;
        nationalEncoding.Items.AddRange(["MS932", "UTF-16LE", "UTF-32LE", "SJIS"]); nationalEncoding.SelectedIndex = 0;
        definitionEncoding.Items.AddRange(DefinitionFileText.Names.Cast<object>().ToArray()); definitionEncoding.SelectedIndex = 0;
        separator.Items.AddRange(["改行あり (CRLF/LF)", "改行なし"]); separator.SelectedIndex = 0;
        var main = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(8) };
        main.RowStyles.Add(new(SizeType.AutoSize)); main.RowStyles.Add(new(SizeType.AutoSize)); main.RowStyles.Add(new(SizeType.AutoSize));
        main.RowStyles.Add(new(SizeType.Percent, 100)); main.RowStyles.Add(new(SizeType.Absolute, 80)); main.RowStyles.Add(new(SizeType.Absolute, 28));
        Control FileRow(string label, TextBox path, string filter)
        {
            var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, AutoSize = true };
            table.ColumnStyles.Add(new(SizeType.Absolute, 100)); table.ColumnStyles.Add(new(SizeType.Percent, 100)); table.ColumnStyles.Add(new(SizeType.AutoSize));
            var browse = new Button { Text = "参照...", AutoSize = true };
            browse.Click += (_, _) => { using var dialog = new OpenFileDialog { Filter = filter, CheckFileExists = true }; if (dialog.ShowDialog(this) == DialogResult.OK) path.Text = dialog.FileName; };
            table.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
            table.Controls.Add(path, 1, 0); table.Controls.Add(browse, 2, 0); return table;
        }
        main.Controls.Add(FileRow("定義CSV", definitionPath, "定義CSV (*.csv)|*.csv|すべてのファイル (*.*)|*.*"), 0, 0);
        main.Controls.Add(FileRow("データ", dataPath, "データファイル (*.*)|*.*"), 0, 1);
        var options = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Dock = DockStyle.Fill };
        void Labelled(string label, Control control) { options.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(8, 8, 3, 0) }); options.Controls.Add(control); }
        Labelled("定義文字コード", definitionEncoding); Labelled("X文字コード", textEncoding); Labelled("N文字コード", nationalEncoding);
        Labelled("区切り", separator); Labelled("区分の位置（1始まり・バイト）", kindPosition); options.Controls.Add(load);
        main.Controls.Add(options, 0, 2); main.Controls.Add(grid, 0, 3); main.Controls.Add(selectedComment, 0, 4); main.Controls.Add(status, 0, 5); Controls.Add(main);
        load.Click += async (_, _) => await LoadRecordsAsync();
        grid.CellValueNeeded += (_, e) =>
        {
            if (e.RowIndex < 0 || e.RowIndex >= rows.Count || e.ColumnIndex < 0) return;
            var row = rows[e.RowIndex];
            e.Value = e.ColumnIndex == 0 ? row.Label : e.ColumnIndex <= row.Values.Count ? row.Values[e.ColumnIndex - 1] : "";
        };
        grid.CellFormatting += (_, e) =>
        {
            if (e.RowIndex < 0 || e.RowIndex >= rows.Count || e.CellStyle is not { } style) return;
            var row = rows[e.RowIndex];
            style.Alignment = DataGridViewContentAlignment.TopLeft;
            if (e.ColumnIndex > 0 && row.FieldWarnings?.ContainsKey(e.ColumnIndex - 1) == true)
            { style.BackColor = Color.FromArgb(255, 190, 170); style.ForeColor = Color.DarkRed; }
            else if (row.Warning is not null) style.BackColor = Color.FromArgb(255, 235, 210);
            else if (row.Kind == "heading") style.BackColor = Color.FromArgb(204, 232, 204);
            else if (row.Kind == "type") style.BackColor = Color.FromArgb(226, 242, 226);
            else if (row.Kind == "comment") { style.BackColor = Color.FromArgb(225, 242, 252); style.ForeColor = CommentDisplayStyle.ForeColor; style.Font = commentFont; }
            else if (e.ColumnIndex == 0) style.BackColor = Color.FromArgb(226, 242, 226);
        };
        grid.CellToolTipTextNeeded += (_, e) =>
        {
            if (e.RowIndex < 0 || e.RowIndex >= rows.Count) return;
            var row = rows[e.RowIndex];
            e.ToolTipText = e.ColumnIndex > 0 && row.FieldWarnings?.TryGetValue(e.ColumnIndex - 1, out var issue) == true ? issue : row.Warning ?? (e.ColumnIndex > 0 && row.Fields is { } fields && e.ColumnIndex <= fields.Count
                ? fields[e.ColumnIndex - 1].Comment : "");
        };
        grid.SelectionChanged += (_, _) =>
        {
            if (grid.CurrentCell is not { RowIndex: >= 0, ColumnIndex: > 0 } cell || cell.RowIndex >= rows.Count)
            { selectedComment.Text = ""; return; }
            var selected = rows[cell.RowIndex];
            if (selected.Warning is not null && selected.Fields is null)
            { selectedComment.Text = selected.Warning + "\r\n" + string.Join(" ", selected.Values); return; }
            if (selected.Fields is not { } fields || cell.ColumnIndex > fields.Count)
            { selectedComment.Text = ""; return; }
            var field = fields[cell.ColumnIndex - 1];
            var warning = selected.FieldWarnings?.GetValueOrDefault(cell.ColumnIndex - 1);
            selectedComment.Text = (warning is null ? "" : "⚠ " + warning + "\r\n") + $"{field.Name} / {field.DisplayDefinition} の備考\r\n{field.Comment}";
        };
    }

    private async Task LoadRecordsAsync()
    {
        var defPath = definitionPath.Text; var filePath = dataPath.Text;
        var defEncoding = (DefinitionTextEncoding)definitionEncoding.SelectedIndex;
        var encoding = (DataEncodingProfile)textEncoding.SelectedIndex;
        var national = (NationalTextEncoding)nationalEncoding.SelectedIndex;
        var recordSeparator = (RecordSeparatorMode)separator.SelectedIndex;
        var position = (int)kindPosition.Value;
        load.Enabled = false;
        try
        {
            status.Text = "読み込んでいます...";
            var result = await Task.Run(() =>
            {
                var definition = MultiRecordDefinition.Read(defPath, defEncoding).WithNationalEncoding(national);
                var data = MultiRecordReader.Read(filePath, definition, position, recordSeparator, encoding, national);
                return (definition, data);
            });
            if (IsDisposed) return;
            Populate(result.definition, result.data);
            var warnings = result.data.Records.Count(r => r.Warning is not null);
            status.Text = $"{result.data.Records.Count:N0}レコード / ヘッダー {result.data.Records.Count(r => r.Kind == '1'):N0}件 / 明細 {result.data.Records.Count(r => r.Kind == '2'):N0}件 / 警告 {warnings:N0}件"
                + (result.data.IncompleteReason is null ? "" : "　⚠ " + result.data.IncompleteReason);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or OverflowException)
        { if (!IsDisposed) { status.Text = "読込エラー: " + ex.Message; MessageBox.Show(this, ex.Message, "ヘッダー・明細の読込", MessageBoxButtons.OK, MessageBoxIcon.Warning); } }
        finally { if (!IsDisposed) load.Enabled = true; }
    }

    private void Populate(MultiRecordDefinition definition, MultiRecordReadResult data)
    {
        var result = new List<ViewRow>(); var detailHeadingShown = false;
        void Headings(string label, IReadOnlyList<FieldDefinition> fields)
        {
            result.Add(new(label + " 見出し", "heading", fields.Select(f => f.Name).ToArray(), Fields: fields));
            result.Add(new("型", "type", fields.Select(f => f.DisplayDefinition).ToArray(), Fields: fields));
            if (fields.Any(f => !string.IsNullOrEmpty(f.Comment))) result.Add(new("備考", "comment", fields.Select(f => f.Comment).ToArray(), Fields: fields));
        }
        foreach (var record in data.Records)
        {
            if (record.Kind == '1') { Headings("ヘッダー", definition.Header); detailHeadingShown = false; }
            if (record.Kind == '2' && !detailHeadingShown) { Headings("明細", definition.Detail); detailHeadingShown = true; }
            var name = record.Kind switch { '1' => "ヘッダー", '2' => "明細", _ => "不明" };
            var fields = record.Kind == '1' ? definition.Header : record.Kind == '2' ? definition.Detail : null;
            result.Add(new($"{name} {record.FileRow}", "value", record.Values, record.Warning, fields, record.FieldWarnings));
        }
        grid.RowCount = 0; grid.Columns.Clear(); rows = result;
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Kind", Width = 130, Frozen = true, SortMode = DataGridViewColumnSortMode.NotSortable });
        var count = Math.Max(definition.Header.Count, definition.Detail.Count);
        for (var i = 0; i < count; i++) grid.Columns.Add(new DataGridViewTextBoxColumn { Name = $"Field{i + 1}", Width = 150, SortMode = DataGridViewColumnSortMode.NotSortable });
        grid.RowCount = rows.Count;
        for (var i = 0; i < rows.Count; i++)
            if (rows[i].Kind == "comment")
            {
                grid.Rows[i].DefaultCellStyle.WrapMode = DataGridViewTriState.True;
                grid.Rows[i].Height = Math.Max(46, commentFont.Height * 3 + 6);
            }
        grid.Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) { commentFont.Dispose(); dataFont.Dispose(); }
    }
}

public sealed partial class MainForm
{
    private void OpenMultiLayoutView()
    {
        using var view = new MultiRecordViewForm(displayFont);
        view.ShowDialog(this);
    }
}
