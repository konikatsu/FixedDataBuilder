using System.Globalization;

namespace FixedDataBuilder;

public sealed partial class MainForm
{
    private bool isSingleRecordView;
    private int singleRecordIndex;
    private bool isUpdatingRecordNavigator;
    private GridLayout previousListLayout = GridLayout.FieldRows;
    private readonly ToolStripMenuItem singleRecordViewButton = new("1件表示");
    private readonly Button listViewButton = new() { Text = "一覧", AutoSize = true };
    private readonly Button singleViewButton = new() { Text = "1件表示", AutoSize = true };
    private readonly Button previousRecordButton = new() { Text = "◀ 前へ", AutoSize = true };
    private readonly Button nextRecordButton = new() { Text = "次へ ▶", AutoSize = true };
    private readonly NumericUpDown singleRecordNumber = new()
    {
        Minimum = 1, Maximum = 1, Value = 1, Width = 80, ThousandsSeparator = true,
        AccessibleName = "表示する行番号"
    };
    private readonly Label singleRecordCountLabel = new() { AutoSize = true, Margin = new Padding(3, 7, 3, 0) };
    private readonly FlowLayoutPanel singleRecordNavigator = new() { AutoSize = true, WrapContents = false, Margin = Padding.Empty };

    private Control BuildViewPanel()
    {
        var panel = new FlowLayoutPanel
        {
            Name = "ViewModePanel", Dock = DockStyle.Top, AutoSize = true,
            WrapContents = false, Padding = new Padding(8, 2, 8, 2)
        };
        panel.Controls.AddRange([listViewButton, singleViewButton, singleRecordNavigator]);
        singleRecordNavigator.Controls.AddRange([previousRecordButton,
            new Label { Text = "行", AutoSize = true, Margin = new Padding(5, 7, 3, 0) },
            singleRecordNumber, singleRecordCountLabel, nextRecordButton]);
        listViewButton.Click += (_, _) => SetSingleRecordView(false);
        singleViewButton.Click += (_, _) => SetSingleRecordView(true);
        singleRecordViewButton.Click += (_, _) => SetSingleRecordView(true);
        previousRecordButton.Click += (_, _) => ChangeSingleRecord(singleRecordIndex - 1);
        nextRecordButton.Click += (_, _) => ChangeSingleRecord(singleRecordIndex + 1);
        singleRecordNumber.ValueChanged += (_, _) =>
        {
            if (!isUpdatingRecordNavigator) ChangeSingleRecord((int)singleRecordNumber.Value - 1);
        };
        UpdateSingleRecordControls();
        return panel;
    }

    private void UpdateSingleRecordControls()
    {
        isUpdatingRecordNavigator = true;
        try
        {
            singleRecordIndex = Math.Clamp(singleRecordIndex, 0, Math.Max(0, records.Count - 1));
            singleRecordNumber.Maximum = Math.Max(1, records.Count);
            singleRecordNumber.Value = singleRecordIndex + 1;
            singleRecordNumber.Enabled = records.Count > 0;
            singleRecordCountLabel.Text = $"/ {records.Count}";
            previousRecordButton.Enabled = records.Count > 0 && singleRecordIndex > 0;
            nextRecordButton.Enabled = singleRecordIndex < records.Count - 1;
            singleRecordNavigator.Visible = isSingleRecordView;
            singleViewButton.Enabled = !isSingleRecordView;
            listViewButton.Enabled = isSingleRecordView;
            hexArea.Visible = !isSingleRecordView;
            singleRecordViewButton.Checked = isSingleRecordView;
        }
        finally { isUpdatingRecordNavigator = false; }
    }

    private void SetSingleRecordView(bool enabled)
    {
        if (isSingleRecordView == enabled) return;
        grid.EndEdit();
        var record = CurrentRecordIndex();
        if (enabled && layout == GridLayout.FieldRows && (grid.CurrentCell?.ColumnIndex ?? -1) < 2) record = 0;
        var field = CurrentFieldIndex();
        if (enabled)
        {
            previousListLayout = layout;
            singleRecordIndex = Math.Clamp(record, 0, Math.Max(0, records.Count - 1));
            layout = GridLayout.FieldRows;
        }
        else layout = previousListLayout;
        isSingleRecordView = enabled;
        RefreshGrid();
        RestoreRecordFieldSelection(record, field);
    }

    private void ChangeSingleRecord(int recordIndex)
    {
        if (!isSingleRecordView) return;
        grid.EndEdit();
        var next = Math.Clamp(recordIndex, 0, Math.Max(0, records.Count - 1));
        if (singleRecordIndex == next) { UpdateSingleRecordControls(); return; }
        var field = CurrentFieldIndex();
        singleRecordIndex = next;
        RefreshGrid();
        RestoreRecordFieldSelection(next, field);
    }

    private void RestoreRecordFieldSelection(int record, int field)
    {
        if (records.Count == 0) return;
        record = Math.Clamp(record, 0, records.Count - 1);
        SelectRecord(record);
        if (layout == GridLayout.FieldRows)
        {
            var row = fieldGridRows.FindIndex(item => item.FieldIndex == field && !item.IsOccursHeader);
            if (row >= 0) grid.CurrentCell = grid[isSingleRecordView ? 2 : record + 2, row];
        }
        else
        {
            var column = recordGridColumnFieldIndexes.IndexOf(field);
            if (column >= 1) grid.CurrentCell = grid[column, record + RecordRowsDataStartIndex()];
        }
    }

    private string FieldStartPosition(int fieldIndex) => IsCsv
        ? $"CSV列{fields.Take(fieldIndex + 1).Count(field => !field.IsRedefines)}"
        : (1L + fields.Take(fieldIndex).Sum(field => (long)field.StorageByteLength)).ToString(CultureInfo.InvariantCulture);

    private string FieldHexText(int recordIndex, int fieldIndex)
    {
        try
        {
            var value = records[recordIndex][fieldIndex];
            var bytes = IsCsv ? JapaneseEncodings.ForCsv(currentCsvEncoding).GetBytes(value)
                : FixedLengthDataWriter.EncodeField(fields[fieldIndex], value, currentDataEncodingProfile, currentNationalTextEncoding);
            return string.Join(" ", bytes.Select(item => item.ToString("X2")));
        }
        catch (Exception ex) { return $"HEX 表示エラー: {ex.Message}"; }
    }

    private void UpdateSingleRecordDetails(int rowIndex)
    {
        if (!isSingleRecordView || records.Count == 0 || rowIndex < 0 || rowIndex >= grid.Rows.Count) return;
        var fieldIndex = FieldIndexFromFieldRow(rowIndex);
        if (fieldIndex < 0) return;
        var row = grid.Rows[rowIndex];
        row.Cells[3].Value = FieldHexText(singleRecordIndex, fieldIndex);
        var summary = SelectionSpaceSummary.Count(records[singleRecordIndex][fieldIndex], fields[fieldIndex], IsCsv, TextDataEncoding);
        row.Cells[4].Value = $"{summary.HalfWidth} / {summary.FullWidth}";
        row.Cells[4].ToolTipText = summary.DisplayText;
    }

    private void SizeSingleRecordColumns()
    {
        if (grid.Columns.Count < 5) return;
        grid.Columns[0].Width = Math.Clamp(grid.Columns[0].GetPreferredWidth(DataGridViewAutoSizeColumnMode.AllCells, false), 100, 320);
        grid.Columns[1].Width = Math.Clamp(grid.Columns[1].GetPreferredWidth(DataGridViewAutoSizeColumnMode.AllCells, false), 100, 160);
        var valueWidth = grid.Columns[2].GetPreferredWidth(DataGridViewAutoSizeColumnMode.AllCells, false);
        grid.Columns[2].Width = Math.Clamp(valueWidth, 140, 340);
        grid.Columns[4].Width = Math.Max(140, TextRenderer.MeasureText(grid.Columns[4].HeaderText, grid.Font).Width + 22);
        grid.Columns[3].Width = Math.Max(200, grid.ClientSize.Width - grid.RowHeadersWidth
            - grid.Columns[0].Width - grid.Columns[1].Width - grid.Columns[2].Width - grid.Columns[4].Width - 22);
        AdjustSingleRecordRowHeights();
    }

    private void AdjustSingleRecordRowHeights()
    {
        if (!isSingleRecordView) return;
        foreach (DataGridViewRow row in grid.Rows)
        {
            var wrapped = WrappedDisplayLines(row.Index, 2);
            var height = Math.Max(grid.RowTemplate.Height,
                row.GetPreferredHeight(row.Index, DataGridViewAutoSizeRowMode.AllCells, true));
            if (wrapped is not null) height = Math.Max(height, wrapped.Count * (grid.Font.Height + 2) + 6);
            row.Height = Math.Clamp(height, row.MinimumHeight, 65536);
        }
    }

    // Excel remains a complete, importable data table, independently of the
    // one-record display and its diagnostic columns.
    private IReadOnlyList<IReadOnlyList<ExcelCell>> BuildSingleRecordExcelRows()
    {
        var rows = new List<IReadOnlyList<ExcelCell>>
        {
            new[] { "項目名", "定義" }.Concat(Enumerable.Range(1, records.Count).Select(index => $"Rec {index}"))
                .Select(text => new ExcelCell(text, ExcelCellKind.Header)).ToList()
        };
        for (var rowIndex = 0; rowIndex < fieldGridRows.Count; rowIndex++)
        {
            var fieldIndex = FieldIndexFromFieldRow(rowIndex);
            var row = new List<ExcelCell>
            {
                new(grid[0, rowIndex].Value?.ToString() ?? "", ExcelCellKind.Definition),
                new(fieldIndex < 0 ? grid[1, rowIndex].Value?.ToString() ?? "" : fields[fieldIndex].DisplayDefinition, ExcelCellKind.Definition)
            };
            row.AddRange(records.Select(record => new ExcelCell(fieldIndex < 0 ? ""
                : FormatValueForDisplay(fields[fieldIndex], record[fieldIndex], IsCsv), ExcelCellKind.Normal)));
            rows.Add(row);
        }
        return rows;
    }
}
