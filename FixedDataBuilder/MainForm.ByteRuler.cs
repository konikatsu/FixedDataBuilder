namespace FixedDataBuilder;

public sealed partial class MainForm
{
    private readonly ToolStripMenuItem byteRulerButton = new("目盛り") { Name = "ByteRuler", CheckOnClick = true };
    private int recordRulerRowIndex = -1;
    private bool changingByteRuler;
    private Font? byteRulerMetricsFont;
    private int byteRulerMetricsDpi;
    private float byteRulerMetricsWidth;

    private void ChangeByteRulerVisibility()
    {
        if (changingByteRuler) return;
        if (!grid.EndEdit())
        {
            changingByteRuler = true;
            byteRulerButton.Checked = !byteRulerButton.Checked;
            changingByteRuler = false;
            return;
        }
        var record = CurrentRecordIndex();
        var field = CurrentFieldIndex();
        RefreshGrid();
        if (record >= 0 && field >= 0) RestoreRecordFieldSelection(record, field);
    }

    private void AddByteRulerRow()
    {
        if (!byteRulerButton.Checked) return;
        recordRulerRowIndex = grid.Rows.Count;
        for (var index = 0; index < recordRulerRowIndex; index++) grid.Rows[index].Frozen = true;
        var row = grid.Rows[grid.Rows.Add()];
        row.ReadOnly = true;
        row.Frozen = true;
        row.DefaultCellStyle.BackColor = Color.FromArgb(242, 245, 248);
        row.DefaultCellStyle.SelectionBackColor = row.DefaultCellStyle.BackColor;
        row.DefaultCellStyle.ForeColor = Color.FromArgb(125, 132, 142);
        row.DefaultCellStyle.SelectionForeColor = row.DefaultCellStyle.ForeColor;
        row.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        row.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
        row.Cells[0].Value = "目盛り";
        row.Cells[0].ToolTipText = DisplayRulerText.Description;
        RefreshByteRulers();
    }

    private void RefreshByteRulers(int? changedField = null)
    {
        if (layout != GridLayout.RecordRows || recordRulerRowIndex < 0 || recordRulerRowIndex >= grid.Rows.Count) return;
        UpdateByteRulerDisplay();
    }

    private void UpdateByteRulerDisplay(int? onlyColumn = null)
    {
        if (layout != GridLayout.RecordRows || recordRulerRowIndex < 0 || recordRulerRowIndex >= grid.Rows.Count) return;
        var row = grid.Rows[recordRulerRowIndex];
        row.Height = Math.Max(grid.RowTemplate.Height, grid.Font.Height * 2 + 8);
        var digitWidth = ByteRulerDigitWidth();
        for (var column = 1; column < grid.Columns.Count; column++)
        {
            if (onlyColumn is not null && onlyColumn != column) continue;
            var cell = row.Cells[column];
            var slots = (int)Math.Floor(Math.Max(1, grid.Columns[column].Width - cell.InheritedStyle.Padding.Horizontal - 8) / digitWidth);
            cell.Value = DisplayRulerText.Format(slots);
            cell.ToolTipText = DisplayRulerText.Description;
        }
        grid.Invalidate();
    }

    private float ByteRulerDigitWidth()
    {
        if (ReferenceEquals(byteRulerMetricsFont, grid.Font) && byteRulerMetricsDpi == grid.DeviceDpi) return byteRulerMetricsWidth;
        byteRulerMetricsFont = grid.Font;
        byteRulerMetricsDpi = grid.DeviceDpi;
        byteRulerMetricsWidth = Math.Max(1, "0123456789".Max(digit =>
            TextRenderer.MeasureText(digit.ToString(), grid.Font, Size.Empty,
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix).Width));
        return byteRulerMetricsWidth;
    }

    private bool TryPaintByteRulerCell(DataGridViewCellPaintingEventArgs e)
    {
        if (layout != GridLayout.RecordRows || recordRulerRowIndex < 0 || e.RowIndex != recordRulerRowIndex
            || e.ColumnIndex < 1 || e.Graphics is null || e.CellStyle is null) return false;
        e.Paint(e.ClipBounds, e.PaintParts & ~DataGridViewPaintParts.ContentForeground);
        var text = e.FormattedValue?.ToString() ?? "";
        var font = e.CellStyle.Font ?? grid.Font;
        var left = e.CellBounds.Left + e.CellStyle.Padding.Left + 4;
        var top = e.CellBounds.Top + (e.CellBounds.Height - 2 * font.Height) / 2;
        var width = ByteRulerDigitWidth();
        var flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine
            | TextFormatFlags.PreserveGraphicsClipping;
        if (!text.Contains('\n'))
            TextRenderer.DrawText(e.Graphics, text, font, new Point(left, top), e.CellStyle.ForeColor, flags);
        else
        {
            var lines = text.Split('\n');
            for (var line = 0; line < lines.Length; line++)
            for (var slot = 0; slot < lines[line].Length; slot++)
                if (lines[line][slot] != ' ')
                    TextRenderer.DrawText(e.Graphics, lines[line][slot].ToString(), font,
                        new Point(left + (int)Math.Round(slot * width), top + line * font.Height), e.CellStyle.ForeColor, flags);
        }
        e.Handled = true;
        return true;
    }

    private void PaintByteGuideLines(DataGridViewCellPaintingEventArgs e)
    {
        if (layout != GridLayout.RecordRows || recordRulerRowIndex < 0 || e.RowIndex < recordRulerRowIndex
            || e.ColumnIndex < 1 || e.Graphics is null || e.CellStyle is null) return;
        if (!e.Handled) { e.Paint(e.ClipBounds, e.PaintParts); e.Handled = true; }
        var width = ByteRulerDigitWidth();
        var left = e.CellBounds.Left + e.CellStyle.Padding.Left + 4;
        var slots = Math.Max(0, (int)Math.Floor((e.CellBounds.Width - e.CellStyle.Padding.Horizontal - 8) / width));
        using var pen = new Pen(Color.FromArgb(135, 70, 125, 205)) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
        var state = e.Graphics.Save();
        try
        {
            e.Graphics.SetClip(Rectangle.Intersect(e.ClipBounds, e.CellBounds), System.Drawing.Drawing2D.CombineMode.Intersect);
            foreach (var position in DisplayRulerText.GuidePositions(slots))
            {
                var x = left + (int)Math.Round(position * width);
                e.Graphics.DrawLine(pen, x, e.CellBounds.Top + 1, x, e.CellBounds.Bottom - 2);
            }
        }
        finally { e.Graphics.Restore(state); }
    }
}
