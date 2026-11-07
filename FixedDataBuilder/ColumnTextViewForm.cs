namespace FixedDataBuilder;

public sealed class ColumnTextViewForm : Form
{
    private const int DefaultFontSize = 10;
    private readonly TextBox pathBox = new() { Name = "ColumnFilePath", ReadOnly = true, Dock = DockStyle.Fill };
    private readonly ComboBox encodingBox = new() { Name = "ColumnEncoding", DropDownStyle = ComboBoxStyle.DropDownList, Width = 135 };
    private readonly NumericUpDown fontSize = new() { Name = "ColumnFontSize", Minimum = 6, Maximum = 24, Value = DefaultFontSize, Width = 55 };
    private readonly NumericUpDown columnCharacters = new() { Name = "ColumnMinimumWidth", Minimum = 10, Maximum = 200, Value = 30, Width = 65 };
    private readonly NumericUpDown pageNumber = new() { Name = "ColumnPageNumber", Minimum = 1, Maximum = 1, Value = 1, Width = 65 };
    private readonly Button previous = new() { Name = "PreviousColumnPage", Text = "◀ 前のページ", AutoSize = true };
    private readonly Button next = new() { Name = "NextColumnPage", Text = "次のページ ▶", AutoSize = true };
    private readonly Label pageCount = new() { AutoSize = true, Margin = new Padding(2, 7, 8, 0) };
    private readonly Label status = new() { Name = "ColumnStatus", Dock = DockStyle.Bottom, Height = 28, TextAlign = ContentAlignment.MiddleLeft };
    private readonly TableLayoutPanel columnsPanel = new() { Name = "TextColumns", Dock = DockStyle.Fill, RowCount = 1, Margin = Padding.Empty };
    private readonly List<WheelZoomDataGridView> panes = new();
    private readonly TextBox searchBox = new() { Name = "ColumnSearchText", Width = 230 };
    private readonly CheckBox matchCase = new() { Name = "ColumnSearchMatchCase", Text = "大文字・小文字を区別", AutoSize = true };
    private readonly Button findNext = new() { Name = "ColumnFindNext", Text = "次を検索 (F3)", AutoSize = true };
    private readonly Button findPrevious = new() { Name = "ColumnFindPrevious", Text = "前を検索", AutoSize = true };
    private readonly Label searchResult = new() { Name = "ColumnSearchResult", AutoSize = true, Margin = new Padding(6, 7, 0, 0) };
    private ColumnTextMatch? searchMatch;
    private ColumnTextDocument? document;
    private ColumnPageLayout pagination = new(1, 1);
    private ColumnAdaptivePagination pageMap = ColumnAdaptivePagination.Build(0, 1, 1, (_, _) => 1);
    private CancellationTokenSource? layoutCancellation;
    private int layoutGeneration;
    private int anchorLine;
    private Font textFont = MainForm.CreateDefaultDisplayFont(DefaultFontSize);
    private int pageIndex;
    private bool updating;
    private bool reflowPending;
    private bool reflowing;
    private bool layoutPending;
    private bool closing;
    private Size appliedSize;

    public ColumnTextViewForm()
    {
        Text = $"段組みビュー - FixedDataBuilder v{typeof(MainForm).Assembly.GetName().Version?.ToString(3)}";
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
        var chooseFont = new Button { Name = "ChooseColumnFont", Text = "フォント...", AutoSize = true };
        Label Caption(string text) => new() { Text = text, AutoSize = true, Margin = new Padding(6, 7, 2, 0) };
        encodingBox.Items.AddRange(ColumnTextDocument.EncodingNames);
        encodingBox.SelectedIndex = 0;
        tools.Controls.AddRange([previous, pageNumber, pageCount, next, chooseFont, Caption("文字サイズ"), fontSize,
            Caption("段幅の目安（半角文字）"), columnCharacters, Caption("文字コード"), encodingBox]);
        var help = new Label { Dock = DockStyle.Top, Height = 24,
            Text = "ウィンドウに合わせて自動段組み ／ 左の段の上→下、次の段へ ／ 元ファイルを表示（保存前の編集は含みません）" };
        var searchBar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(4), WrapContents = true };
        searchBar.Controls.AddRange([Caption("検索 (Ctrl+F)"), searchBox, findNext, findPrevious, matchCase, searchResult]);
        Controls.Add(columnsPanel); Controls.Add(status); Controls.Add(help); Controls.Add(searchBar); Controls.Add(tools); Controls.Add(fileBar);
        searchBox.TextChanged += (_, _) => ResetSearch();
        matchCase.CheckedChanged += (_, _) => ResetSearch();
        findNext.Click += (_, _) => Search(true);
        findPrevious.Click += (_, _) => Search(false);
        ResetSearch();
        open.Click += (_, _) => ChooseFile();
        reload.Click += (_, _) => ReloadFile();
        previous.Click += (_, _) => MovePage(pageIndex - 1);
        next.Click += (_, _) => MovePage(pageIndex + 1);
        pageNumber.ValueChanged += (_, _) => { if (!updating) MovePage((int)pageNumber.Value - 1); };
        columnCharacters.ValueChanged += (_, _) => Reflow();
        chooseFont.Click += (_, _) => ChooseTextFont();
        fontSize.ValueChanged += (_, _) =>
        {
            if (!updating) ApplyTextFont(textFont, fontSize.Value);
        };
        encodingBox.SelectedIndexChanged += (_, _) => { if (!updating) ReloadFile(); };
        columnsPanel.SizeChanged += (_, _) => ScheduleReflow();
        Shown += (_, _) => Reflow();
        UpdatePageControls();
    }

    private void ChooseTextFont()
    {
        using var initial = (Font)textFont.Clone();
        using var dialog = new FontDialog
        {
            Font = initial,
            FontMustExist = true,
            ShowEffects = false,
            AllowVerticalFonts = false,
            MinSize = (int)fontSize.Minimum,
            MaxSize = (int)fontSize.Maximum
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        var selected = dialog.Font;
        try
        {
            var points = Math.Clamp((decimal)MathF.Round(selected.SizeInPoints), fontSize.Minimum, fontSize.Maximum);
            ApplyTextFont(selected, points);
        }
        finally
        {
            if (!ReferenceEquals(selected, initial)) selected.Dispose();
        }
    }

    private void ApplyTextFont(Font selected, decimal points)
    {
        var replacement = new Font(selected.FontFamily, (float)points, selected.Style, GraphicsUnit.Point);
        using var old = textFont;
        textFont = replacement;
        var previousUpdating = updating;
        updating = true;
        try
        {
            fontSize.Value = points;
            foreach (var pane in panes) pane.Font = replacement;
        }
        finally { updating = previousUpdating; }
        Reflow();
    }

    public void LoadFile(string path, ColumnTextEncoding? encoding = null)
        => LoadFileAt(path, encoding, 0);

    private void LoadFileAt(string path, ColumnTextEncoding? encoding, int anchor)
    {
        // Decode first: failed loads must leave the current file/page intact.
        var loaded = ColumnTextDocument.Load(path, encoding);
        document = loaded; pageIndex = 0;
        anchorLine = Math.Clamp(anchor, 0, Math.Max(0, loaded.Lines.Count - 1));
        ResetSearch();
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
        var anchor = pageMap[pageIndex].Start;
        try
        {
            LoadFileAt(document.Path, (ColumnTextEncoding)encodingBox.SelectedIndex, anchor);
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
        if (updating || reflowing || closing || reflowPending || !IsHandleCreated || IsDisposed) return;
        reflowPending = true;
        BeginInvoke((Action)(() =>
        {
            reflowPending = false;
            if (!IsDisposed && !closing && columnsPanel.ClientSize != appliedSize) Reflow();
        }));
    }

    private void Reflow()
    {
        if (updating || reflowing || closing || IsDisposed) return;
        var size = columnsPanel.ClientSize;
        var anchor = document is null ? 0 : anchorLine;
        var rowHeight = Math.Max(12, textFont.Height + 2);
        var digitWidth = Math.Max(1, TextRenderer.MeasureText("0000000000", textFont, Size.Empty, TextFormatFlags.NoPadding).Width / 10);
        var gutter = digitWidth * Math.Max(3, (document?.Lines.Count ?? 0).ToString().Length) + 16;
        var rows = ColumnPageLayout.Fit(size.Width, size.Height, 1, rowHeight,
            rowHeight + SystemInformation.HorizontalScrollBarHeight + 8).RowsPerColumn;
        var snapshot = document;
        var generation = ++layoutGeneration;
        layoutCancellation?.Cancel();
        layoutCancellation = new CancellationTokenSource();
        var token = layoutCancellation.Token;
        var measuredFont = (Font)textFont.Clone();
        var minimumWidth = Math.Max(80, digitWidth * (int)columnCharacters.Value);
        ColumnAdaptivePagination Calculate()
        {
            using (measuredFont)
            {
                var lines = snapshot?.Lines ?? Array.Empty<string>();
                var available = Math.Max(1, size.Width);
                var cachedStart = -1; var cachedWidth = 0;
                return ColumnAdaptivePagination.Build(lines.Count, rows, available, (first, last) =>
                {
                    token.ThrowIfCancellationRequested();
                    if (first == cachedStart) return cachedWidth;
                    var header = $"{first + 1}～{last}行";
                    var width = Math.Max(minimumWidth, TextRenderer.MeasureText(header, measuredFont, Size.Empty,
                        TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix).Width + 20);
                    for (var i = first; i < last && width < available; i++)
                    {
                        if ((i & 63) == 0) token.ThrowIfCancellationRequested();
                        var line = lines[i];
                        // A very long physical line needs horizontal scrolling; avoid measuring unbounded text.
                        var sample = line.Length > 4096 ? line[..4096] : line;
                        var measured = TextRenderer.MeasureText(sample, measuredFont, Size.Empty,
                            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.ExpandTabs).Width + 24;
                        width = Math.Max(width, line.Length > 4096 ? Math.Max(measured, available) : measured);
                    }
                    cachedStart = first;
                    return cachedWidth = (int)Math.Min(int.MaxValue, (long)gutter + width + 4);
                }, token);
            }
        }
        void Apply(ColumnAdaptivePagination map)
        {
            if (generation != layoutGeneration || closing || IsDisposed) return;
            pageMap = map;
            pageIndex = map.PageForLine(anchor);
            layoutPending = false;
            columnsPanel.Enabled = true;
            searchBox.Enabled = true;
            ApplyPageLayout(size, rowHeight, gutter);
        }
        if (!IsHandleCreated)
        {
            Apply(Calculate());
            return;
        }
        columnsPanel.Enabled = false;
        layoutPending = true;
        searchBox.Enabled = false;
        previous.Enabled = next.Enabled = pageNumber.Enabled = findNext.Enabled = findPrevious.Enabled = false;
        foreach (var pane in panes) pane.RowCount = 0;
        status.Text = "段組みのページを計算しています...";
        _ = Task.Run(Calculate).ContinueWith(task =>
        {
            if (task.IsCanceled || IsDisposed || closing || generation != layoutGeneration) return;
            try
            {
                BeginInvoke((Action)(() =>
                {
                    if (generation != layoutGeneration || closing || IsDisposed) return;
                    if (task.IsFaulted)
                    {
                        layoutPending = false;
                        columnsPanel.Enabled = true; searchBox.Enabled = true;
                        status.ForeColor = Color.Firebrick;
                        status.Text = "段組みの計算に失敗しました：" + task.Exception!.GetBaseException().Message;
                        return;
                    }
                    Apply(task.Result);
                    findNext.Enabled = findPrevious.Enabled = document is not null && searchBox.Text.Length > 0;
                }));
            }
            catch (InvalidOperationException) { /* Form was closed before dispatch. */ }
        }, TaskScheduler.Default);
    }

    private int LineIndex(int column, int row)
    {
        if (document is null || row < 0 || row >= pagination.RowsPerColumn) return -1;
        var page = pageMap[pageIndex];
        var line = (long)page.Start + (long)column * pagination.RowsPerColumn + row;
        return line >= page.Start && line < page.End ? (int)line : -1;
    }

    private void ApplyPageLayout(Size size, int rowHeight, int gutter)
    {
        var previousUpdating = updating;
        pagination = new ColumnPageLayout(pageMap[pageIndex].Columns, pageMap.RowsPerColumn);
        reflowing = true;
        updating = true;
        columnsPanel.SuspendLayout();
        try
        {
            while (panes.Count > pagination.Columns)
            {
                var pane = panes[^1]; panes.RemoveAt(panes.Count - 1); columnsPanel.Controls.Remove(pane); pane.Dispose();
            }
            columnsPanel.ColumnCount = pagination.Columns + 1;
            columnsPanel.ColumnStyles.Clear();
            for (var column = 0; column < pagination.Columns; column++)
            {
                columnsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute));
                if (column >= panes.Count)
                {
                    var index = column;
                    var pane = CreatePane(); panes.Add(pane); columnsPanel.Controls.Add(pane, column, 0);
                    pane.CellValueNeeded += (_, e) =>
                    {
                        if (document is null) return;
                        var line = LineIndex(index, e.RowIndex);
                        if (line >= 0) e.Value = e.ColumnIndex == 0 ? (object)(line + 1) : document.Lines[line];
                    };
                }
                var current = panes[column];
                current.Font = textFont; current.RowTemplate.Height = rowHeight;
                current.ColumnHeadersHeight = rowHeight + 4;
                current.Columns[0].Width = gutter;
            }
            columnsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            FillPage();
        }
        finally
        {
            try { columnsPanel.ResumeLayout(); }
            finally
            {
                updating = previousUpdating;
                reflowing = false;
                appliedSize = size;
                if (columnsPanel.ClientSize != appliedSize) ScheduleReflow();
            }
        }
        SelectSearchMatch();
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
        var preferredWidths = new int[panes.Count];
        columnsPanel.SuspendLayout();
        try
        {
            for (var column = 0; column < panes.Count; column++)
            {
                var pane = panes[column];
                var first = pageMap[pageIndex].Start + column * pagination.RowsPerColumn;
                pane.RowCount = 0;
                pane.RowCount = Math.Clamp(lineCount - first, 0, pagination.RowsPerColumn);
                pane.Columns[1].HeaderText = pane.RowCount == 0 ? "—" : $"{first + 1}～{first + pane.RowCount}行";
                var dataWidth = pane.Columns[1].GetPreferredWidth(DataGridViewAutoSizeColumnMode.AllCells, true);
                pane.Columns[1].Width = (int)Math.Clamp(dataWidth + 6L, 80L, 65_536L);
                if (pane.RowCount > 0 || (lineCount == 0 && column == 0))
                    preferredWidths[column] = pane.Columns[0].Width + pane.Columns[1].Width + pane.Margin.Horizontal;
                pane.ClearSelection(); pane.Invalidate();
            }

            var widths = ColumnPageLayout.FitPaneWidths(preferredWidths, columnsPanel.ClientSize.Width);
            for (var column = 0; column < widths.Length; column++)
                columnsPanel.ColumnStyles[column].Width = widths[column];
        }
        finally { columnsPanel.ResumeLayout(); }
        UpdatePageControls();
    }

    private ColumnTextMatch? VisibleMatch() => document is not null && searchMatch is not null
        && pageMap.PageForLine(searchMatch.LineIndex) == pageIndex ? searchMatch : null;

    private void ResetSearch()
    {
        searchMatch = null;
        findNext.Enabled = findPrevious.Enabled = document is not null && searchBox.Text.Length > 0;
        searchResult.Text = searchBox.Text.Length == 0 ? "ファイル全体を検索" : "Enter：次 ／ Shift+Enter：前";
        searchResult.ForeColor = SystemColors.ControlText;
        foreach (var pane in panes) pane.ClearSelection();
    }

    private void Search(bool forward)
    {
        if (layoutPending || document is null || searchBox.Text.Length == 0) return;
        var match = ColumnTextSearch.Find(document.Lines, searchBox.Text, matchCase.Checked, searchMatch, forward,
            pageMap[pageIndex].Start);
        if (match is null)
        {
            searchResult.Text = "見つかりません";
            searchResult.ForeColor = Color.Firebrick;
            return;
        }
        searchMatch = match;
        MovePage(pageMap.PageForLine(match.LineIndex));
        anchorLine = match.LineIndex;
        searchResult.ForeColor = SystemColors.ControlText;
        searchResult.Text = $"{match.LineIndex + 1}行目・{match.CharacterIndex + 1}文字目"
            + (match.Wrapped ? forward ? "（先頭に戻りました）" : "（末尾に戻りました）" : "");
    }

    private void SelectSearchMatch()
    {
        var match = VisibleMatch();
        if (match is null || document is null) return;
        var offset = match.LineIndex - pageMap[pageIndex].Start;
        var pane = panes[offset / pagination.RowsPerColumn];
        var row = offset % pagination.RowsPerColumn;
        pane.CurrentCell = pane[1, row];
        pane[1, row].Selected = true;
        // Bring matches beyond the visible part of a long physical line into view.
        var prefix = document.Lines[match.LineIndex][..match.CharacterIndex];
        var x = TextRenderer.MeasureText(prefix, textFont, Size.Empty,
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix).Width;
        var available = Math.Max(1, pane.ClientSize.Width - pane.Columns[0].Width);
        pane.HorizontalScrollingOffset = Math.Min(Math.Max(0, x - available / 3),
            Math.Max(0, pane.Columns[1].Width - available));
    }

    private void MovePage(int requested)
    {
        if (layoutPending) return;
        pageIndex = Math.Clamp(requested, 0, pageMap.Count - 1);
        anchorLine = pageMap[pageIndex].Start;
        var rowHeight = Math.Max(12, textFont.Height + 2);
        var digitWidth = Math.Max(1, TextRenderer.MeasureText("0000000000", textFont, Size.Empty, TextFormatFlags.NoPadding).Width / 10);
        var gutter = digitWidth * Math.Max(3, (document?.Lines.Count ?? 0).ToString().Length) + 16;
        ApplyPageLayout(columnsPanel.ClientSize, rowHeight, gutter);
        SelectSearchMatch();
    }

    private void UpdatePageControls()
    {
        var count = document?.Lines.Count ?? 0;
        var previousUpdating = updating;
        updating = true;
        try
        {
            pageNumber.Maximum = pageMap.Count; pageNumber.Value = pageIndex + 1;
            pageNumber.Enabled = count > 0;
            pageCount.Text = $"/ {pageMap.Count} ページ";
            previous.Enabled = count > 0 && pageIndex > 0;
            next.Enabled = count > 0 && pageIndex + 1 < pageMap.Count;
            var first = count == 0 ? 0 : pageMap[pageIndex].Start + 1;
            var last = pageMap[pageIndex].End;
            status.Text = $"{first}～{last}行 / 全{count}行　{pagination.Columns}段 × {pagination.RowsPerColumn}行　（PgUp / PgDnでページ送り、Ctrl＋ホイールで拡大縮小）";
            if (panes.Any(p => p.RowCount > 0 && p.Columns[0].Width + p.Columns[1].Width > p.ClientSize.Width))
                status.Text += "　画面に収まらない行は下のスクロールバーで確認できます。";
            status.ForeColor = SystemColors.ControlText;
        }
        finally { updating = previousUpdating; }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        closing = true;
        layoutCancellation?.Cancel();
        base.OnFormClosing(e);
        if (e.Cancel) { closing = false; ScheduleReflow(); }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.F)) { searchBox.Focus(); searchBox.SelectAll(); return true; }
        if (keyData == Keys.F3 || (searchBox.Focused && keyData == Keys.Enter)) { Search(true); return true; }
        if (keyData == (Keys.Shift | Keys.F3) || (searchBox.Focused && keyData == (Keys.Shift | Keys.Enter))) { Search(false); return true; }
        if (keyData is Keys.PageDown or Keys.PageUp) { MovePage(pageIndex + (keyData == Keys.PageDown ? 1 : -1)); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) { layoutCancellation?.Cancel(); layoutCancellation?.Dispose(); textFont.Dispose(); }
    }
}
