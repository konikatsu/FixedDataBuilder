namespace FixedDataBuilder;

public sealed class ColumnTextViewForm : Form
{
    private readonly TextBox pathBox = new() { Name = "ColumnFilePath", ReadOnly = true, Dock = DockStyle.Fill };
    private readonly ComboBox encodingBox = new() { Name = "ColumnEncoding", DropDownStyle = ComboBoxStyle.DropDownList, Width = 135 };
    private readonly NumericUpDown fontSize = new() { Name = "ColumnFontSize", Minimum = 6, Maximum = 24, Value = 9, Width = 55 };
    private readonly NumericUpDown columnCharacters = new() { Name = "ColumnMinimumWidth", Minimum = 10, Maximum = 200, Value = 30, Width = 65 };
    private readonly NumericUpDown pageNumber = new() { Name = "ColumnPageNumber", Minimum = 1, Maximum = 1, Value = 1, Width = 65 };
    private readonly Button previous = new() { Name = "PreviousColumnPage", Text = "◀ 前のページ", AutoSize = true };
    private readonly Button next = new() { Name = "NextColumnPage", Text = "次のページ ▶", AutoSize = true };
    private readonly Label pageCount = new() { AutoSize = true, Margin = new Padding(2, 7, 8, 0) };
    private readonly Label status = new() { Name = "ColumnStatus", Dock = DockStyle.Bottom, Height = 28, TextAlign = ContentAlignment.MiddleLeft };
    private readonly TableLayoutPanel columnsPanel = new() { Name = "TextColumns", Dock = DockStyle.Fill, RowCount = 1, Margin = Padding.Empty };
    private readonly List<WheelZoomDataGridView> panes = new();
    private ColumnTextDocument? document;
    private ColumnPageLayout pagination = new(1, 1);
    private Font textFont = new("MS Gothic", 9);
    private int pageIndex;
    private bool updating;
    private bool reflowPending;

    public ColumnTextViewForm()
    {
        Text = "段組みビュー";
        Icon = AppIcon.Load();
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(1280, 820);
        MinimumSize = new Size(760, 440);
        columnsPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var fileBar = new TableLayoutPanel { Dock = DockStyle.Top, Height = 34, ColumnCount = 3, Padding = new Padding(4) };
        fileBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        fileBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        fileBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        var open = new Button { Name = "OpenColumnFile", Text = "ファイルを開く...", Dock = DockStyle.Fill };
        var reload = new Button { Name = "ReloadColumnFile", Text = "再読み込み", Dock = DockStyle.Fill };
        fileBar.Controls.Add(pathBox, 0, 0); fileBar.Controls.Add(open, 1, 0); fileBar.Controls.Add(reload, 2, 0);
        var tools = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(4), WrapContents = true };
        Label Caption(string text) => new() { Text = text, AutoSize = true, Margin = new Padding(6, 7, 2, 0) };
        encodingBox.Items.AddRange(ColumnTextDocument.EncodingNames);
        encodingBox.SelectedIndex = 0;
        tools.Controls.AddRange([previous, pageNumber, pageCount, next, Caption("文字サイズ"), fontSize,
            Caption("段幅の目安（半角文字）"), columnCharacters, Caption("文字コード"), encodingBox]);
        var help = new Label { Dock = DockStyle.Top, Height = 24,
            Text = "ウィンドウに合わせて自動段組み ／ 左の段の上→下、次の段へ ／ 元ファイルを表示（保存前の編集は含みません）" };
        Controls.Add(columnsPanel); Controls.Add(status); Controls.Add(help); Controls.Add(tools); Controls.Add(fileBar);
        open.Click += (_, _) => ChooseFile();
        reload.Click += (_, _) => ReloadFile();
        previous.Click += (_, _) => MovePage(pageIndex - 1);
        next.Click += (_, _) => MovePage(pageIndex + 1);
        pageNumber.ValueChanged += (_, _) => { if (!updating) MovePage((int)pageNumber.Value - 1); };
        columnCharacters.ValueChanged += (_, _) => Reflow();
        fontSize.ValueChanged += (_, _) =>
        {
            var old = textFont;
            textFont = new Font("MS Gothic", (float)fontSize.Value);
            foreach (var pane in panes) pane.Font = textFont;
            Reflow(); old.Dispose();
        };
        encodingBox.SelectedIndexChanged += (_, _) => { if (!updating) ReloadFile(); };
        columnsPanel.SizeChanged += (_, _) => ScheduleReflow();
        Shown += (_, _) => Reflow();
        UpdatePageControls();
    }

    public void LoadFile(string path, ColumnTextEncoding? encoding = null)
    {
        // Decode first: failed loads must leave the current file/page intact.
        var loaded = ColumnTextDocument.Load(path, encoding);
        document = loaded; pageIndex = 0;
        updating = true;
        try { pathBox.Text = loaded.Path; encodingBox.SelectedIndex = (int)loaded.Encoding; }
        finally { updating = false; }
        Reflow();
    }

    private void ChooseFile()
    {
        using var dialog = new OpenFileDialog { Filter = "文字ファイル (*.csv;*.txt;*.dat)|*.csv;*.txt;*.dat|すべてのファイル (*.*)|*.*", Title = "段組みで表示するファイル" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try { LoadFile(dialog.FileName); }
        catch (Exception ex) when (IsLoadError(ex)) { ShowError(ex); }
    }

    private void ReloadFile()
    {
        if (document is null) return;
        var anchor = pagination.FirstLine(pageIndex, document.Lines.Count);
        try
        {
            LoadFile(document.Path, (ColumnTextEncoding)encodingBox.SelectedIndex);
            MovePage(anchor / pagination.Capacity);
        }
        catch (Exception ex) when (IsLoadError(ex))
        {
            updating = true;
            try { encodingBox.SelectedIndex = (int)document.Encoding; }
            finally { updating = false; }
            ShowError(ex);
        }
    }

    private static bool IsLoadError(Exception ex) => ex is IOException or UnauthorizedAccessException
        or ArgumentException or NotSupportedException or InvalidDataException;
    private void ShowError(Exception ex) { status.Text = "表示できません：" + ex.Message; status.ForeColor = Color.Firebrick; }

    private void ScheduleReflow()
    {
        if (updating || reflowPending || !IsHandleCreated || IsDisposed) return;
        reflowPending = true;
        BeginInvoke((Action)(() => { reflowPending = false; if (!IsDisposed) Reflow(); }));
    }

    private void Reflow()
    {
        if (updating) return;
        var anchor = document is null ? 0 : pagination.FirstLine(pageIndex, document.Lines.Count);
        var rowHeight = Math.Max(12, textFont.Height + 2);
        var digitWidth = Math.Max(1, TextRenderer.MeasureText("0000000000", textFont, Size.Empty, TextFormatFlags.NoPadding).Width / 10);
        var gutter = digitWidth * Math.Max(3, (document?.Lines.Count ?? 0).ToString().Length) + 16;
        pagination = ColumnPageLayout.Fit(columnsPanel.ClientSize.Width, columnsPanel.ClientSize.Height,
            digitWidth * (int)columnCharacters.Value + gutter + 8, rowHeight, rowHeight + SystemInformation.HorizontalScrollBarHeight + 8);
        pageIndex = pagination.ClampPage(anchor / pagination.Capacity, document?.Lines.Count ?? 0);
        updating = true;
        columnsPanel.SuspendLayout();
        try
        {
            while (panes.Count > pagination.Columns)
            {
                var pane = panes[^1]; panes.RemoveAt(panes.Count - 1); columnsPanel.Controls.Remove(pane); pane.Dispose();
            }
            columnsPanel.ColumnCount = pagination.Columns;
            columnsPanel.ColumnStyles.Clear();
            for (var column = 0; column < pagination.Columns; column++)
            {
                columnsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / pagination.Columns));
                if (column >= panes.Count)
                {
                    var index = column;
                    var pane = CreatePane(); panes.Add(pane); columnsPanel.Controls.Add(pane, column, 0);
                    pane.CellValueNeeded += (_, e) =>
                    {
                        if (document is null) return;
                        var line = pagination.LineIndex(pageIndex, index, e.RowIndex, document.Lines.Count);
                        if (line >= 0) e.Value = e.ColumnIndex == 0 ? (object)(line + 1) : document.Lines[line];
                    };
                }
                var current = panes[column];
                current.Font = textFont; current.RowTemplate.Height = rowHeight;
                current.ColumnHeadersHeight = rowHeight + 4;
                current.Columns[0].Width = gutter;
            }
            FillPage();
        }
        finally { columnsPanel.ResumeLayout(); updating = false; }
    }

    private WheelZoomDataGridView CreatePane()
    {
        var pane = new WheelZoomDataGridView
        {
            Dock = DockStyle.Fill, Margin = new Padding(0, 0, 2, 0), BackgroundColor = Color.White,
            ReadOnly = true, VirtualMode = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
            AllowUserToOrderColumns = false, RowHeadersVisible = false, BorderStyle = BorderStyle.None,
            CellBorderStyle = DataGridViewCellBorderStyle.None, ScrollBars = ScrollBars.Horizontal,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None, AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            EnableHeadersVisualStyles = false, ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableWithoutHeaderText
        };
        pane.DefaultCellStyle.Alignment = DataGridViewContentAlignment.TopLeft;
        pane.DefaultCellStyle.WrapMode = DataGridViewTriState.False;
        pane.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(225, 242, 252);
        pane.Columns.Add(new DataGridViewTextBoxColumn { Name = "SourceLine", HeaderText = "行", SortMode = DataGridViewColumnSortMode.NotSortable,
            Frozen = true, DefaultCellStyle = new DataGridViewCellStyle { ForeColor = Color.RoyalBlue } });
        pane.Columns.Add(new DataGridViewTextBoxColumn { Name = "SourceText", HeaderText = "内容", SortMode = DataGridViewColumnSortMode.NotSortable });
        pane.ZoomWheel += (_, e) => fontSize.Value = Math.Clamp(fontSize.Value + Math.Sign(e.Delta), fontSize.Minimum, fontSize.Maximum);
        return pane;
    }

    private void FillPage()
    {
        var lineCount = document?.Lines.Count ?? 0;
        for (var column = 0; column < panes.Count; column++)
        {
            var pane = panes[column];
            var first = pagination.FirstLine(pageIndex, lineCount) + column * pagination.RowsPerColumn;
            pane.RowCount = Math.Clamp(lineCount - first, 0, pagination.RowsPerColumn);
            // Shared rows may keep heights from the previous font setting.
            foreach (DataGridViewRow row in pane.Rows) row.Height = pane.RowTemplate.Height;
            pane.Columns[1].HeaderText = pane.RowCount == 0 ? "—" : $"{first + 1}～{first + pane.RowCount}行";
            pane.Columns[1].Width = Math.Max(80, pane.Columns[1].GetPreferredWidth(DataGridViewAutoSizeColumnMode.AllCells, true) + 6);
            pane.ClearSelection(); pane.Invalidate();
        }
        UpdatePageControls();
    }

    private void MovePage(int requested)
    {
        pageIndex = pagination.ClampPage(requested, document?.Lines.Count ?? 0);
        FillPage();
    }

    private void UpdatePageControls()
    {
        var count = document?.Lines.Count ?? 0;
        updating = true;
        try
        {
            pageNumber.Maximum = pagination.PageCount(count); pageNumber.Value = pageIndex + 1;
            pageNumber.Enabled = count > 0;
            pageCount.Text = $"/ {pagination.PageCount(count)} ページ";
            previous.Enabled = count > 0 && pageIndex > 0;
            next.Enabled = count > 0 && pageIndex + 1 < pagination.PageCount(count);
            var first = count == 0 ? 0 : pagination.FirstLine(pageIndex, count) + 1;
            var last = Math.Min(count, (long)pagination.FirstLine(pageIndex, count) + pagination.Capacity);
            status.Text = $"{first}～{last}行 / 全{count}行　{pagination.Columns}段 × {pagination.RowsPerColumn}行　（PgUp / PgDnでページ送り、Ctrl＋ホイールで拡大縮小）";
            status.ForeColor = SystemColors.ControlText;
        }
        finally { updating = false; }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData is Keys.PageDown or Keys.PageUp) { MovePage(pageIndex + (keyData == Keys.PageDown ? 1 : -1)); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) textFont.Dispose();
    }
}
