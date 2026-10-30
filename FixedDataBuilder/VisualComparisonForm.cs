using System.Globalization;
using System.Text;

namespace FixedDataBuilder;

/// <summary>Two independent record lists for human inspection. No mapping or verdict is applied.</summary>
public sealed class VisualComparisonForm : Form
{
    private readonly TableLayoutPanel lists = new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
    private readonly RecordListPane inputPane;
    private readonly RecordListPane outputPane;
    private readonly Button swapButton = new() { Name = "SwapRecordLists", Text = "入力・出力の上下を入れ替え", AutoSize = true };
    private bool inputOnTop = true;

    public VisualComparisonForm(ComparisonDataSet output, Font? dataFont = null)
    {
        ArgumentNullException.ThrowIfNull(output);
        Text = "入力・出力を並べて表示（目視確認）";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(1400, 920);
        MinimumSize = new Size(900, 650);
        AutoScaleMode = AutoScaleMode.Font;
        inputPane = new RecordListPane("入力", dataFont);
        outputPane = new RecordListPane("出力", dataFont);
        inputPane.OpenRequested += (_, _) => OpenSide(inputPane);
        outputPane.OpenRequested += (_, _) => OpenSide(outputPane);
        var shell = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(8) };
        shell.ColumnStyles.Add(new(SizeType.Percent, 100));
        shell.RowStyles.Add(new(SizeType.AutoSize));
        shell.RowStyles.Add(new(SizeType.Percent, 100));
        var toolbar = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
        toolbar.Controls.Add(swapButton);
        toolbar.Controls.Add(new Label { Text = "上と下の表は、それぞれ縦横にスクロールできます。値は読み取り専用です。", AutoSize = true, Margin = new Padding(12, 8, 3, 4) });
        swapButton.Click += (_, _) => SwapLists();
        lists.ColumnStyles.Add(new(SizeType.Percent, 100));
        lists.RowStyles.Add(new(SizeType.Percent, 50));
        lists.RowStyles.Add(new(SizeType.Absolute, 8));
        lists.RowStyles.Add(new(SizeType.Percent, 50));
        lists.Controls.Add(inputPane, 0, 0);
        lists.Controls.Add(new Panel { Dock = DockStyle.Fill, BackColor = SystemColors.ControlDark }, 0, 1);
        lists.Controls.Add(outputPane, 0, 2);
        shell.Controls.Add(toolbar, 0, 0);
        shell.Controls.Add(lists, 0, 1);
        Controls.Add(shell);
        outputPane.SetDataSet(output);
    }

    private void OpenSide(RecordListPane pane)
    {
        using var loader = new ComparisonForm.ComparisonLoadForm(pane.Side, pane.DataSet);
        if (loader.ShowDialog(this) == DialogResult.OK && loader.DataSet is { } data) pane.SetDataSet(data);
    }

    private void SetInputDataSet(ComparisonDataSet data) => inputPane.SetDataSet(data);
    private void SetOutputDataSet(ComparisonDataSet data) => outputPane.SetDataSet(data);

    private void SwapLists()
    {
        var inputView = inputPane.CaptureViewport();
        var outputView = outputPane.CaptureViewport();
        lists.SuspendLayout();
        try
        {
            inputOnTop = !inputOnTop;
            lists.SetRow(inputPane, inputOnTop ? 0 : 2);
            lists.SetRow(outputPane, inputOnTop ? 2 : 0);
        }
        finally { lists.ResumeLayout(performLayout: true); }
        inputPane.RestoreViewport(inputView);
        outputPane.RestoreViewport(outputView);
    }

    /// <summary>The original snapshots, selections, and scroll positions belong to each pane.</summary>
    private sealed class RecordListPane : UserControl
    {
        private readonly DataGridView grid = new()
        {
            Name = "RecordList", Dock = DockStyle.Fill, ReadOnly = true, VirtualMode = true,
            AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToOrderColumns = false,
            RowHeadersVisible = false, MultiSelect = true, SelectionMode = DataGridViewSelectionMode.CellSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None, AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None,
            ScrollBars = ScrollBars.Both, EditMode = DataGridViewEditMode.EditProgrammatically,
            EnableHeadersVisualStyles = false, ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableWithoutHeaderText
        };
        private readonly Label title = new() { AutoSize = true, Margin = new Padding(3, 8, 10, 0) };
        private readonly TextBox source = new() { Dock = DockStyle.Fill, ReadOnly = true };
        private readonly Label condition = new() { Dock = DockStyle.Fill, AutoEllipsis = true };
        private readonly Label empty = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter };
        private readonly NumericUpDown jumpNumber = new() { Minimum = 1, Maximum = 1, Width = 80, ThousandsSeparator = true };
        private readonly Button jumpButton = new() { Text = "行へ移動", AutoSize = true };
        private readonly CheckBox showSpaces = new() { Text = "空白を表示", Checked = true, AutoSize = true, Margin = new Padding(14, 7, 3, 0) };
        private readonly TextBox selectedValue = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, WordWrap = false, ScrollBars = ScrollBars.Both };
        private readonly ToolTip hints = new();
        private readonly Font ownedFont;
        private int metadataRows;
        private int positionRow = -1;
        private int commentRow = -1;
        private bool rebuilding;
        public string Side { get; }
        public ComparisonDataSet? DataSet { get; private set; }
        public event EventHandler? OpenRequested;

        public RecordListPane(string side, Font? dataFont)
        {
            Side = side;
            Name = side == "入力" ? "InputRecordPane" : "OutputRecordPane";
            Dock = DockStyle.Fill;
            Margin = Padding.Empty;
            ownedFont = dataFont is null ? new Font("MS Gothic", 10.5f) : (Font)dataFont.Clone();
            grid.Font = ownedFont;
            grid.DefaultCellStyle.Alignment = DataGridViewContentAlignment.TopLeft;
            grid.DefaultCellStyle.SelectionBackColor = SystemColors.Highlight;
            grid.DefaultCellStyle.SelectionForeColor = SystemColors.HighlightText;
            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(204, 232, 204);
            grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.TopLeft;
            grid.ColumnHeadersHeight = 26;
            grid.RowTemplate.Height = Math.Max(24, ownedFont.Height + 6);
            var shell = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5 };
            shell.ColumnStyles.Add(new(SizeType.Percent, 100));
            shell.RowStyles.Add(new(SizeType.AutoSize));
            shell.RowStyles.Add(new(SizeType.Absolute, 26));
            shell.RowStyles.Add(new(SizeType.Absolute, 24));
            shell.RowStyles.Add(new(SizeType.Percent, 100));
            shell.RowStyles.Add(new(SizeType.Absolute, 48));
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Margin = Padding.Empty };
            var open = new Button { Name = side == "入力" ? "OpenInputRecords" : "OpenOutputRecords", Text = side + "を開く…", AutoSize = true };
            open.Click += (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty);
            actions.Controls.AddRange([title, open, new Label { Text = "行", AutoSize = true, Margin = new Padding(12, 8, 3, 0) }, jumpNumber, jumpButton, showSpaces]);
            var display = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            display.Controls.Add(grid);
            display.Controls.Add(empty);
            shell.Controls.Add(actions, 0, 0);
            shell.Controls.Add(source, 0, 1);
            shell.Controls.Add(condition, 0, 2);
            shell.Controls.Add(display, 0, 3);
            shell.Controls.Add(selectedValue, 0, 4);
            Controls.Add(shell);
            grid.CellValueNeeded += (_, e) => e.Value = CellValue(e.RowIndex, e.ColumnIndex);
            grid.CellFormatting += FormatCell;
            grid.CellToolTipTextNeeded += (_, e) => e.ToolTipText = CellToolTip(e.RowIndex, e.ColumnIndex);
            grid.SelectionChanged += (_, _) => { if (!rebuilding) UpdateSelection(); };
            grid.SizeChanged += (_, _) => { if (!rebuilding) UpdateHeaderPinning(); };
            jumpButton.Click += (_, _) => JumpToRecord();
            showSpaces.CheckedChanged += (_, _) => { grid.Invalidate(); UpdateSelection(); };
            RefreshGrid();
        }

        public void SetDataSet(ComparisonDataSet data)
        {
            ArgumentNullException.ThrowIfNull(data);
            var previous = DataSet;
            try { DataSet = data; RefreshGrid(); }
            catch { DataSet = previous; RefreshGrid(); throw; }
        }

        private void RefreshGrid()
        {
            rebuilding = true;
            grid.SuspendLayout();
            try
            {
                grid.RowCount = 0;
                grid.Columns.Clear();
                metadataRows = 0;
                positionRow = commentRow = -1;
                var data = DataSet;
                title.Text = $"{Side}レコード一覧　{data?.Records.Count ?? 0:N0}件";
                empty.Text = $"「{Side}を開く…」から、定義ファイルとデータを選んでください。";
                empty.Visible = data is null;
                grid.Visible = data is not null;
                jumpNumber.Enabled = jumpButton.Enabled = data?.Records.Count > 0;
                jumpNumber.Maximum = Math.Max(1, data?.Records.Count ?? 0);
                jumpNumber.Value = 1;
                selectedValue.Clear();
                if (data is null) { source.Clear(); condition.Text = ""; return; }
                source.Text = $"定義: {data.DefinitionPath ?? "画面の定義"}　／　データ: {data.DataPath ?? "メイン画面の内容"}";
                source.SelectionStart = 0;
                source.SelectionLength = 0;
                hints.SetToolTip(source, source.Text);
                condition.Text = data.Profile.Format == DataFileFormat.Csv
                    ? $"CSV / {JapaneseEncodings.Name(data.Profile.CsvEncoding)} / {(data.Profile.CsvHasHeader ? "ヘッダーあり" : "ヘッダーなし")}"
                    : $"固定長 / X: {JapaneseEncodings.Name(data.Profile.DataEncoding)} / N: {NationalTextEncodingHelper.DisplayName(data.Profile.NationalEncoding)}";
                if (!data.HasSourceBytes) condition.Text += " / メイン画面の値";
                metadataRows = 2;
                if (data.Profile.Format == DataFileFormat.FixedLength) positionRow = metadataRows++;
                if (data.Fields.Any(field => !string.IsNullOrEmpty(field.Comment))) commentRow = metadataRows++;
                grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "RecordNumber", HeaderText = "行", Width = 72, Frozen = true, ReadOnly = true, FillWeight = 1, SortMode = DataGridViewColumnSortMode.NotSortable });
                var minimumWidth = Math.Max(96, TextRenderer.MeasureText("0000000000", ownedFont, Size.Empty, TextFormatFlags.NoPadding).Width + 12);
                for (var index = 0; index < data.Fields.Count; index++)
                {
                    var field = data.Fields[index];
                    var width = minimumWidth;
                    for (var record = 0; record < Math.Min(100, data.Records.Count); record++)
                    {
                        var text = MainForm.FormatValueForDisplay(field, data.Records[record][index], data.Profile.Format == DataFileFormat.Csv);
                        // Measuring a bounded preview must not copy or truncate the underlying cell.
                        text = text[..Math.Min(text.Length, 120)];
                        width = Math.Max(width, Math.Min(360, TextRenderer.MeasureText(text, ownedFont, Size.Empty, TextFormatFlags.NoPadding).Width + 12));
                    }
                    grid.Columns.Add(new DataGridViewTextBoxColumn { Name = $"Field{index + 1}", HeaderText = $"No.{index + 1}", Width = width, MinimumWidth = minimumWidth, ReadOnly = true, FillWeight = 1, SortMode = DataGridViewColumnSortMode.NotSortable,
                        ToolTipText = $"{field.Name} / {field.DisplayDefinition}\r\n{field.Comment}" });
                }
                grid.RowCount = checked(metadataRows + data.Records.Count);
                for (var row = 0; row < metadataRows; row++)
                {
                    grid.Rows[row].Height = row == 0 || row == commentRow ? Math.Max(38, ownedFont.Height * 2 + 6) : ownedFont.Height + 8;
                }
                UpdateHeaderPinning();
                if (data.Records.Count > 0 && data.Fields.Count > 0) grid.CurrentCell = grid[1, metadataRows];
            }
            finally { grid.ResumeLayout(); rebuilding = false; }
            UpdateSelection();
        }

        private object? CellValue(int row, int column)
        {
            var data = DataSet;
            if (data is null || row < 0 || column < 0 || column > data.Fields.Count
                || row >= metadataRows + data.Records.Count) return null;
            if (row < metadataRows)
            {
                if (column == 0) return row == 0 ? "項目名" : row == 1 ? "型" : row == positionRow ? "位置" : "コメント";
                var field = data.Fields[column - 1];
                if (row == 0) return field.Name;
                if (row == 1) return field.DisplayDefinition;
                if (row == commentRow) return field.Comment;
                var start = 1L + data.Fields.Take(column - 1).Sum(item => (long)item.StorageByteLength);
                return field.IsRedefines ? "REDEFINES" : $"{start}-{start + field.StorageByteLength - 1}[{field.StorageByteLength}]";
            }
            if (column == 0) return (row - metadataRows + 1).ToString(CultureInfo.InvariantCulture);
            return VisibleValue(row - metadataRows, column - 1);
        }

        private string LogicalDisplayValue(int record, int field)
        {
            var data = DataSet!;
            var definition = data.Fields[field];
            if (data.Profile.Format == DataFileFormat.Csv && definition.IsRedefines) return "（CSV列なし）";
            if (data.Profile.Format == DataFileFormat.FixedLength && data.HasSourceBytes
                && definition.Type is FieldDataType.Text or FieldDataType.HalfWidthText or FieldDataType.FullWidthText)
            {
                var sourceValue = data.ReadSource(record, field, ComparisonSourceKind.FixedStorageText);
                return sourceValue.Success ? sourceValue.Value! : $"（読取エラー: {sourceValue.Reason}）";
            }
            var value = MainForm.FormatValueForDisplay(definition, data.Records[record][field], data.Profile.Format == DataFileFormat.Csv);
            if (data.Profile.Format == DataFileFormat.FixedLength && definition.Type is FieldDataType.Text or FieldDataType.HalfWidthText or FieldDataType.FullWidthText)
            {
                int used;
                try { used = definition.Type == FieldDataType.Text ? JapaneseEncodings.ForData(data.Profile.DataEncoding).GetByteCount(value) : value.Length; }
                catch (EncoderFallbackException) { return value; }
                if (definition.Length > used)
                {
                    var padding = definition.Length - used;
                    value += new string(definition.Type == FieldDataType.FullWidthText ? '\u3000' : ' ', Math.Min(100_000, padding));
                    if (padding > 100_000) value += "…";
                }
            }
            return value;
        }

        private string VisibleValue(int record, int field)
        {
            var value = LogicalDisplayValue(record, field);
            return showSpaces.Checked ? value.Replace(' ', '\uFF65').Replace('\u3000', '\u25A1') : value;
        }

        private void FormatCell(object? sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0) return;
            if (e.CellStyle is not { } style) return;
            style.Alignment = DataGridViewContentAlignment.TopLeft;
            if (e.RowIndex < metadataRows)
            {
                style.BackColor = e.RowIndex == commentRow ? Color.FromArgb(226, 242, 252) : Color.FromArgb(226, 242, 226);
                style.WrapMode = e.RowIndex == 0 || e.RowIndex == commentRow ? DataGridViewTriState.True : DataGridViewTriState.False;
            }
            else if (e.ColumnIndex == 0) style.BackColor = Color.FromArgb(226, 242, 226);
        }

        private string CellToolTip(int row, int column)
        {
            if (DataSet is not { } data || column < 1 || column > data.Fields.Count || row < 0
                || row >= metadataRows + data.Records.Count) return "";
            var field = data.Fields[column - 1];
            if (row < metadataRows) return $"{field.Name} / {field.DisplayDefinition}\r\n{field.Comment}";
            return data.Records[row - metadataRows][column - 1];
        }

        private void JumpToRecord()
        {
            if (DataSet is not { } data || data.Records.Count == 0 || data.Fields.Count == 0) return;
            var row = metadataRows + (int)jumpNumber.Value - 1;
            var column = Math.Clamp(grid.CurrentCell?.ColumnIndex ?? 1, 1, data.Fields.Count);
            grid.CurrentCell = grid[column, row];
            grid.FirstDisplayedScrollingRowIndex = row;
        }

        private void UpdateHeaderPinning()
        {
            if (metadataRows == 0 || grid.RowCount < metadataRows) return;
            var height = grid.ColumnHeadersHeight;
            for (var row = 0; row < metadataRows; row++) height += grid.Rows[row].Height;
            // A small window or a large inherited font must still leave a scrollable data area.
            var pin = grid.ClientSize.Height >= height + grid.RowTemplate.Height + SystemInformation.HorizontalScrollBarHeight;
            if (pin)
                for (var row = 0; row < metadataRows; row++) grid.Rows[row].Frozen = true;
            else
                for (var row = metadataRows - 1; row >= 0; row--) grid.Rows[row].Frozen = false;
        }

        private void UpdateSelection()
        {
            if (DataSet is not { } data || grid.CurrentCell is not { } cell || cell.RowIndex < metadataRows || cell.ColumnIndex < 1
                || cell.RowIndex >= metadataRows + data.Records.Count || cell.ColumnIndex > data.Fields.Count)
            { selectedValue.Text = "確認したいデータのセルをクリックしてください。"; return; }
            var record = cell.RowIndex - metadataRows;
            var field = cell.ColumnIndex - 1;
            var raw = data.Records[record][field];
            var definition = data.Fields[field];
            var spaces = LogicalDisplayValue(record, field);
            selectedValue.Text = $"{Side} 行{record + 1} / No.{field + 1} {definition.Name}　半角空白: {spaces.Count(ch => ch == ' ')} / 全角空白: {spaces.Count(ch => ch == '\u3000')}\r\n読込値: {raw}";
        }

        public readonly record struct Viewport(int Row, int Column, int HorizontalOffset);
        public Viewport CaptureViewport() => new(grid.FirstDisplayedScrollingRowIndex, grid.FirstDisplayedScrollingColumnIndex, grid.HorizontalScrollingOffset);
        public void RestoreViewport(Viewport view)
        {
            if (view.Row >= metadataRows && view.Row < grid.RowCount && grid.Height > grid.ColumnHeadersHeight)
                grid.FirstDisplayedScrollingRowIndex = view.Row;
            if (view.Column > 0 && view.Column < grid.ColumnCount) grid.FirstDisplayedScrollingColumnIndex = view.Column;
            if (view.HorizontalOffset >= 0) grid.HorizontalScrollingOffset = view.HorizontalOffset;
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) { hints.Dispose(); ownedFont.Dispose(); }
        }
    }
}
