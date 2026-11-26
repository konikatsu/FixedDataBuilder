namespace FixedDataBuilder;

public sealed partial class MultiRecordViewForm : Form
{
    private sealed record ViewRow(string Label, string Kind, IReadOnlyList<string> Values, string? Warning = null,
        IReadOnlyList<FieldDefinition>? Fields = null, IReadOnlyDictionary<int, string>? FieldWarnings = null, int? RecordNo = null,
        string? Details = null);
    private readonly TextBox definitionPath = new() { Name = "MultiDefinitionPath", Dock = DockStyle.Fill };
    private readonly TextBox dataPath = new() { Name = "MultiDataPath", Dock = DockStyle.Fill };
    private readonly NumericUpDown kindPosition = new() { Name = "MultiKindPosition", Minimum = 1, Maximum = 1000000, Value = 4, Width = 75 };
    private readonly ComboBox textEncoding = new() { Name = "MultiTextEncoding", DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
    private readonly ComboBox nationalEncoding = new() { Name = "MultiNationalEncoding", DropDownStyle = ComboBoxStyle.DropDownList, Width = 125 };
    private readonly ComboBox definitionEncoding = new() { Name = "MultiDefinitionEncoding", DropDownStyle = ComboBoxStyle.DropDownList, Width = 165 };
    private readonly ComboBox separator = new() { Name = "MultiSeparator", DropDownStyle = ComboBoxStyle.DropDownList, Width = 125 };
    private readonly ComboBox commentMode = new() { Name = "MultiCommentMode", DropDownStyle = ComboBoxStyle.DropDownList, Width = 130 };
    private readonly ComboBox headingMode = new() { Name = "MultiHeadingMode", DropDownStyle = ComboBoxStyle.DropDownList, Width = 130 };
    private readonly Button load = new() { Name = "LoadMultiRecords", Text = "表示", AutoSize = true };
    private readonly Button copyImage = new() { Name = "MultiCopyImage", Text = "全範囲を画像コピー", AutoSize = true, Enabled = false };
    private readonly Button saveImages = new() { Name = "MultiSaveImages", Text = "全範囲を画像保存...", AutoSize = true, Enabled = false };
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
    private MultiRecordDefinition? loadedDefinition;
    private MultiRecordReadResult? loadedData;
    private int[]? fieldColumnWidths;
    private readonly Font dataFont;
    private readonly Font commentFont;

    public MultiRecordViewForm(Font? font = null)
    {
        Text = $"ヘッダー・明細表示（確認用） - FixedDataBuilder v{typeof(MainForm).Assembly.GetName().Version?.ToString(3)}"; StartPosition = FormStartPosition.CenterParent;
        Size = new Size(1400, 850); MinimumSize = new Size(800, 500);
        dataFont = font is null ? new Font("MS Gothic", 10f) : (Font)font.Clone();
        commentFont = CommentDisplayStyle.CreateFont(dataFont);
        grid.Font = dataFont; grid.DefaultCellStyle.Alignment = DataGridViewContentAlignment.TopLeft;
        textEncoding.Items.AddRange(JapaneseEncodings.TextNames.Cast<object>().ToArray()); textEncoding.SelectedIndex = 0;
        nationalEncoding.Items.AddRange(["MS932", "UTF-16LE", "UTF-32LE", "SJIS"]); nationalEncoding.SelectedIndex = 0;
        definitionEncoding.Items.AddRange(DefinitionFileText.Names.Cast<object>().ToArray()); definitionEncoding.SelectedIndex = 0;
        separator.Items.AddRange(["改行あり (CRLF/LF)", "改行なし"]); separator.SelectedIndex = 0;
        commentMode.Items.AddRange(["まとまりごと", "毎行", "最初のみ", "非表示"]); commentMode.SelectedIndex = 2;
        headingMode.Items.AddRange(["まとまりごと", "毎行", "最初のみ", "非表示"]); headingMode.SelectedIndex = 0;
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
        Labelled("見出し表示", headingMode); Labelled("備考表示", commentMode); options.Controls.Add(copyImage); options.Controls.Add(saveImages);
        main.Controls.Add(options, 0, 2); main.Controls.Add(grid, 0, 3); main.Controls.Add(selectedComment, 0, 4); main.Controls.Add(status, 0, 5); Controls.Add(main);
        load.Click += async (_, _) => await LoadRecordsAsync();
        copyImage.Click += (_, _) => CopyAllImages();
        saveImages.Click += (_, _) => SaveAllImages();
        commentMode.SelectedIndexChanged += (_, _) => RefreshCommentDisplay();
        headingMode.SelectedIndexChanged += (_, _) => RefreshCommentDisplay();
        grid.CellValueNeeded += (_, e) =>
        {
            if (e.RowIndex < 0 || e.RowIndex >= rows.Count || e.ColumnIndex < 0) return;
            var row = rows[e.RowIndex];
            e.Value = e.ColumnIndex switch
            {
                0 => row.Kind == "heading" ? "行番号" : row.RecordNo?.ToString() ?? "",
                1 => row.Label,
                _ => e.ColumnIndex - 2 < row.Values.Count ? row.Values[e.ColumnIndex - 2] : ""
            };
        };
        grid.CellFormatting += (_, e) =>
        {
            if (e.RowIndex < 0 || e.RowIndex >= rows.Count || e.CellStyle is not { } style) return;
            ApplyRowStyle(e.RowIndex, e.ColumnIndex, style);
        };
        grid.CellPainting += PaintVisibleSpaces;
        grid.CellToolTipTextNeeded += (_, e) =>
        {
            if (e.RowIndex < 0 || e.RowIndex >= rows.Count) return;
            var row = rows[e.RowIndex];
            e.ToolTipText = e.ColumnIndex >= 2 && row.FieldWarnings?.TryGetValue(e.ColumnIndex - 2, out var issue) == true ? issue : row.Warning ?? (e.ColumnIndex >= 2 && row.Fields is { } fields && e.ColumnIndex - 2 < fields.Count
                ? fields[e.ColumnIndex - 2].Comment : "");
        };
        grid.CurrentCellChanged += (_, _) =>
        {
            if (grid.CurrentCell is not { RowIndex: >= 0, ColumnIndex: >= 2 } cell || cell.RowIndex >= rows.Count)
            { selectedComment.Text = ""; return; }
            var selected = rows[cell.RowIndex];
            if (selected.Warning is not null && selected.Fields is null)
            { selectedComment.Text = selected.Warning + "\r\n" + (selected.Details ?? string.Join(" ", selected.Values)); return; }
            if (selected.Fields is not { } fields || cell.ColumnIndex - 2 >= fields.Count)
            { selectedComment.Text = ""; return; }
            var field = fields[cell.ColumnIndex - 2];
            var warning = selected.FieldWarnings?.GetValueOrDefault(cell.ColumnIndex - 2);
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
            using var measurementFont = (Font)dataFont.Clone();
            var dpi = grid.DeviceDpi;
            var result = await Task.Run(() =>
            {
                var definition = MultiRecordDefinition.Read(defPath, defEncoding).WithNationalEncoding(national);
                var data = MultiRecordReader.Read(filePath, definition, position, recordSeparator, encoding, national);
                var widths = MeasureColumnWidths(definition, data, measurementFont, dpi);
                return (definition, data, widths);
            });
            if (IsDisposed) return;
            fieldColumnWidths = result.widths;
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
        loadedDefinition = definition; loadedData = data;
        var result = new List<ViewRow>(); var detailGroupShown = false;
        var headerCommentShown = false; var detailCommentShown = false;
        var headerHeadingShown = false; var detailHeadingShown = false;
        void Headings(string label, IReadOnlyList<FieldDefinition> fields, bool groupStart, ref bool headingShown, ref bool commentShown)
        {
            if (headingMode.SelectedIndex == 1 || headingMode.SelectedIndex == 0 && groupStart || headingMode.SelectedIndex == 2 && !headingShown)
            {
                result.Add(new(label + " 見出し", "heading", fields.Select(f => f.Name).ToArray(), Fields: fields));
                result.Add(new("型", "type", fields.Select(f => f.DisplayDefinition).ToArray(), Fields: fields));
                headingShown = true;
            }
            if ((commentMode.SelectedIndex == 1 || commentMode.SelectedIndex == 0 && groupStart || commentMode.SelectedIndex == 2 && !commentShown) && fields.Any(f => !string.IsNullOrEmpty(f.Comment)))
            {
                result.Add(new("備考", "comment", fields.Select(f => f.Comment).ToArray(), Fields: fields));
                commentShown = true;
            }
        }
        foreach (var record in data.Records)
        {
            if (record.Kind == '1') { Headings("HEAD", definition.Header, true, ref headerHeadingShown, ref headerCommentShown); detailGroupShown = false; }
            if (record.Kind == '2') { Headings("BODY", definition.Detail, !detailGroupShown, ref detailHeadingShown, ref detailCommentShown); detailGroupShown = true; }
            var name = record.Kind switch { '1' => "HEAD", '2' => "BODY", _ => "不明" };
            var fields = record.Kind == '1' ? definition.Header : record.Kind == '2' ? definition.Detail : null;
            var values = record.Kind == '?' ? new[] { "不明" } : record.Values;
            var details = record.Kind == '?' ? string.Join(" ", record.Values) : null;
            result.Add(new(name, "value", values, record.Warning, fields, record.FieldWarnings, record.FileRow, details));
        }
        grid.RowCount = 0; grid.Columns.Clear(); rows = result;
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "RecordNumber", Width = 80, Frozen = true, SortMode = DataGridViewColumnSortMode.NotSortable });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Kind", Width = 130, Frozen = true, SortMode = DataGridViewColumnSortMode.NotSortable });
        var count = Math.Max(definition.Header.Count, definition.Detail.Count);
        for (var i = 0; i < count; i++) grid.Columns.Add(new DataGridViewTextBoxColumn { Name = $"Field{i + 1}", Width = fieldColumnWidths is { } widths && i < widths.Length ? widths[i] : 150, SortMode = DataGridViewColumnSortMode.NotSortable });
        grid.RowCount = rows.Count;
        copyImage.Enabled = saveImages.Enabled = rows.Count > 0;
        for (var i = 0; i < rows.Count; i++)
            if (rows[i].Kind == "comment")
            {
                grid.Rows[i].DefaultCellStyle.WrapMode = DataGridViewTriState.True;
                grid.Rows[i].Height = Math.Max(46, commentFont.Height * 3 + 6);
            }
        grid.Invalidate();
    }

    private void ApplyRowStyle(int rowIndex, int columnIndex, DataGridViewCellStyle style)
    {
        var row = rows[rowIndex];
        style.Alignment = DataGridViewContentAlignment.TopLeft;
        if (columnIndex >= 2 && row.FieldWarnings?.ContainsKey(columnIndex - 2) == true)
        { style.BackColor = Color.FromArgb(255, 190, 170); style.ForeColor = Color.DarkRed; }
        else if (row.Warning is not null && (row.FieldWarnings is null || columnIndex < 2)) style.BackColor = Color.FromArgb(255, 235, 210);
        else if (row.Kind == "value" && row.Label == "HEAD") style.BackColor = Color.FromArgb(220, 240, 250);
        else if (row.Kind == "heading") style.BackColor = Color.FromArgb(204, 232, 204);
        else if (row.Kind == "type") style.BackColor = Color.FromArgb(226, 242, 226);
        else if (row.Kind == "comment") { style.BackColor = Color.FromArgb(255, 248, 220); style.ForeColor = CommentDisplayStyle.ForeColor; style.Font = commentFont; }
        else if (columnIndex < 2) style.BackColor = Color.FromArgb(226, 242, 226);
    }

    internal static string ShowSpaces(string value) => value.Replace(' ', '\uFF65').Replace('\u3000', '\u25A1');

    private static int[] MeasureColumnWidths(MultiRecordDefinition definition, MultiRecordReadResult data, Font font, int dpi)
    {
        var count = Math.Max(definition.Header.Count, definition.Detail.Count);
        var widths = Enumerable.Repeat(80, count).ToArray();
        using var bitmap = new Bitmap(1, 1);
        bitmap.SetResolution(dpi, dpi);
        using var graphics = Graphics.FromImage(bitmap);
        var flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
        var cache = new Dictionary<string, int>();
        void Include(int index, string text)
        {
            if (index >= widths.Length || widths[index] >= 65536) return;
            var visible = ShowSpaces(text);
            foreach (var line in visible.Split(['\r', '\n']))
            {
                if (!cache.TryGetValue(line, out var width))
                {
                    width = line.Length == 0 ? 0 : TextRenderer.MeasureText(graphics, line, font, Size.Empty, flags).Width;
                    if (cache.Count < 4096 && line.Length < 256) cache[line] = width;
                }
                widths[index] = (int)Math.Min(65536L, Math.Max(widths[index], (long)width + 16));
            }
        }
        foreach (var fields in new[] { definition.Header, definition.Detail })
            for (var i = 0; i < fields.Count; i++) { Include(i, fields[i].Name); Include(i, fields[i].DisplayDefinition); }
        foreach (var record in data.Records)
            if (record.Kind != '?')
                for (var i = 0; i < record.Values.Count && i < count; i++) Include(i, record.Values[i]);
        return widths;
    }

    private void PaintVisibleSpaces(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= rows.Count || e.ColumnIndex < 2 || rows[e.RowIndex].Kind != "value"
            || e.Graphics is not { } graphics || e.CellStyle is not { } style) return;
        var value = e.FormattedValue?.ToString() ?? "";
        if (!value.Contains(' ') && !value.Contains('\u3000')) return;
        var visible = ShowSpaces(value);
        e.Paint(e.ClipBounds, e.PaintParts & ~DataGridViewPaintParts.ContentForeground);
        var selected = (e.State & DataGridViewElementStates.Selected) != 0;
        var font = style.Font ?? grid.Font;
        var flags = TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix
            | TextFormatFlags.SingleLine | TextFormatFlags.PreserveGraphicsClipping;
        var state = graphics.Save();
        try
        {
            graphics.SetClip(Rectangle.Intersect(e.CellBounds, e.ClipBounds), System.Drawing.Drawing2D.CombineMode.Intersect);
            var x = e.CellBounds.Left + style.Padding.Left + 4;
            var y = e.CellBounds.Top + style.Padding.Top + 2;
            if (!selected)
            {
                using var brush = new SolidBrush(Color.FromArgb(255, 250, 205));
                var start = -1;
                for (var i = 0; i <= value.Length; i++)
                {
                    var space = i < value.Length && value[i] is ' ' or '\u3000';
                    if (space && start < 0) start = i;
                    if (space || start < 0) continue;
                    int Measure(string text) => text.Length == 0 ? 0 : TextRenderer.MeasureText(graphics, text, font, Size.Empty, flags).Width;
                    var left = x + Measure(visible[..start]);
                    var width = Measure(visible[start..i]);
                    if (width > 0) graphics.FillRectangle(brush, left, y, width, font.Height + 2);
                    start = -1;
                }
            }
            TextRenderer.DrawText(graphics, visible, font, new Point(x, y), selected ? style.SelectionForeColor : style.ForeColor, flags);
        }
        finally { graphics.Restore(state); }
        e.Handled = true;
    }

    private void RefreshCommentDisplay()
    {
        if (loadedDefinition is null || loadedData is null) return;
        var first = grid.FirstDisplayedScrollingRowIndex;
        var record = first >= 0 ? rows.Skip(first).Select(r => r.RecordNo).FirstOrDefault(n => n.HasValue) : null;
        var horizontal = grid.HorizontalScrollingOffset;
        var widths = grid.Columns.Cast<DataGridViewColumn>().Select(c => c.Width).ToArray();
        Populate(loadedDefinition, loadedData);
        for (var i = 0; i < Math.Min(widths.Length, grid.ColumnCount); i++) grid.Columns[i].Width = widths[i];
        if (record is not null)
        {
            var newRow = rows.ToList().FindIndex(r => r.RecordNo == record);
            if (newRow >= 0) grid.FirstDisplayedScrollingRowIndex = newRow;
        }
        if (grid.IsHandleCreated) grid.HorizontalScrollingOffset = Math.Min(horizontal, Math.Max(0, grid.Columns.GetColumnsWidth(DataGridViewElementStates.Visible) - grid.ClientSize.Width));
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
