using System.Globalization;

namespace FixedDataBuilder;

public sealed partial class MainForm
{
    private static readonly Color CommentHeaderBackColor = Color.FromArgb(225, 242, 252);
    private int recordRowsDataStartIndex = 2;
    private int recordCommentRowIndex = -1;
    private int recordOccursRowIndex = -1;

    private void AddRecordCommentRow(IReadOnlyList<string> comments)
    {
        recordCommentRowIndex = grid.Rows.Count;
        var row = grid.Rows[grid.Rows.Add(comments.Cast<object>().ToArray())];
        row.ReadOnly = true;
        row.Frozen = true;
        row.DefaultCellStyle.BackColor = CommentHeaderBackColor;
        row.DefaultCellStyle.SelectionBackColor = CommentHeaderBackColor;
        row.DefaultCellStyle.SelectionForeColor = grid.ForeColor;
        row.DefaultCellStyle.Alignment = DataGridViewContentAlignment.TopLeft;
        row.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        row.DefaultCellStyle.Padding = new Padding(4, 3, 4, 3);
        foreach (DataGridViewCell cell in row.Cells)
            cell.ToolTipText = cell.Value?.ToString() ?? string.Empty;
    }

    private void AdjustRecordCommentHeight()
    {
        if (layout != GridLayout.RecordRows || recordCommentRowIndex < 0 || recordCommentRowIndex >= grid.Rows.Count) return;
        var row = grid.Rows[recordCommentRowIndex];
        var preferred = row.GetPreferredHeight(recordCommentRowIndex, DataGridViewAutoSizeRowMode.AllCellsExceptHeader, fixedWidth: true);
        // Keep data visible even for a very long comment; ToolTip retains the complete text.
        var maximum = Math.Max(grid.RowTemplate.Height, Math.Min(6 * (grid.Font.Height + 2) + 8,
            Math.Max(grid.RowTemplate.Height, grid.ClientSize.Height / 3)));
        row.Height = Math.Clamp(preferred, grid.RowTemplate.Height, maximum);
    }

    private string RecordItemName(int fieldIndex) => occursDisplayInfos.TryGetValue(fieldIndex, out var info)
        ? info.ChildName : fields[fieldIndex].Name;

    private string ExcelColumnHeader(DataGridViewColumn column)
    {
        var fieldIndex = layout == GridLayout.RecordRows ? FieldIndexFromRecordColumn(column.Index) : -1;
        return fieldIndex < 0 ? column.HeaderText : RecordItemName(fieldIndex);
    }

    private IReadOnlyList<ExcelCell> CsvPositionsForExcel()
    {
        var row = new List<ExcelCell> { new("位置", ExcelCellKind.Header) };
        for (var column = 1; column < grid.Columns.Count; column++)
        {
            var fieldIndex = FieldIndexFromRecordColumn(column);
            var number = fields.Take(fieldIndex + 1).Count(field => !field.IsRedefines);
            row.Add(new ExcelCell("CSV列" + number.ToString(CultureInfo.InvariantCulture), ExcelCellKind.Header));
        }
        return row;
    }
}
