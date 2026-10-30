namespace FixedDataBuilder;

public sealed partial class MainForm
{
    private sealed record RecordNameLayout(string Text, int Width, Font Font, int Dpi, IReadOnlyList<string> Lines);
    private readonly Dictionary<int, RecordNameLayout> recordNameLayouts = new();
    private const TextFormatFlags RecordNameTextFlags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;

    private void ConfigureRecordNameRow()
    {
        var row = grid.Rows[0];
        row.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        row.DefaultCellStyle.Alignment = DataGridViewContentAlignment.TopLeft;
        row.DefaultCellStyle.Padding = new Padding(4, 2, 4, 2);
    }

    private void UpdateRecordColumnMinimumWidths()
    {
        if (layout != GridLayout.RecordRows || isSingleRecordView) return;
        var minimum = TextRenderer.MeasureText(new string('0', displayWrapOptions.HeaderMinimumCharacters),
            grid.Font, Size.Empty, RecordNameTextFlags).Width + 10;
        for (var column = 1; column < grid.Columns.Count; column++) grid.Columns[column].MinimumWidth = minimum;
    }

    private bool IsRecordAuxiliaryWidthRow(int row, int column) => layout == GridLayout.RecordRows
        && ((row == 0 && column > 0) || row == recordCommentRowIndex || row == recordRulerRowIndex
            || (row == recordOccursRowIndex && column > 0));

    private IReadOnlyList<string> RecordItemNameLines(int column)
    {
        var cell = grid[column, 0];
        var text = cell.Value?.ToString() ?? "";
        var style = cell.InheritedStyle;
        var font = style.Font ?? grid.Font;
        var width = Math.Max(1, grid.Columns[column].Width - style.Padding.Horizontal - 2);
        if (recordNameLayouts.TryGetValue(column, out var cached) && cached.Text == text && cached.Width == width
            && ReferenceEquals(cached.Font, font) && cached.Dpi == grid.DeviceDpi) return cached.Lines;
        var lines = HeaderTextWrapper.Lines(text, width,
            value => TextRenderer.MeasureText(value, font, Size.Empty, RecordNameTextFlags).Width);
        recordNameLayouts[column] = new(text, width, font, grid.DeviceDpi, lines);
        return lines;
    }

    private void AdjustRecordItemNameHeight()
    {
        if (layout != GridLayout.RecordRows || isSingleRecordView || grid.Rows.Count == 0) return;
        var lines = grid.Columns.Cast<DataGridViewColumn>().Where(column => column.Visible && column.Index > 0)
            .Select(column => RecordItemNameLines(column.Index).Count).DefaultIfEmpty(1).Max();
        grid.Rows[0].Height = Math.Max(grid.RowTemplate.Height, lines * (grid.Font.Height + 2) + 6);
    }

    private bool TryPaintRecordItemName(DataGridViewCellPaintingEventArgs e)
    {
        if (layout != GridLayout.RecordRows || isSingleRecordView || e.RowIndex != 0 || e.ColumnIndex < 1
            || e.Graphics is null || e.CellStyle is null) return false;
        e.Paint(e.ClipBounds, e.PaintParts & ~DataGridViewPaintParts.ContentForeground);
        var font = e.CellStyle.Font ?? grid.Font;
        var padding = e.CellStyle.Padding;
        var bounds = new Rectangle(e.CellBounds.Left + padding.Left, e.CellBounds.Top + padding.Top + 1,
            Math.Max(1, e.CellBounds.Width - padding.Horizontal - 2), font.Height + 2);
        var state = e.Graphics.Save();
        try
        {
            e.Graphics.SetClip(Rectangle.Intersect(e.ClipBounds, e.CellBounds), System.Drawing.Drawing2D.CombineMode.Intersect);
            foreach (var line in RecordItemNameLines(e.ColumnIndex))
            {
                TextRenderer.DrawText(e.Graphics, line, font, bounds, e.CellStyle.ForeColor,
                    RecordNameTextFlags | TextFormatFlags.PreserveGraphicsClipping);
                bounds.Y += font.Height + 2;
            }
        }
        finally { e.Graphics.Restore(state); }
        e.Handled = true;
        return true;
    }
}
