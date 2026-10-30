using System.Text;

namespace FixedDataBuilder;

public sealed partial class MainForm
{
    private readonly ToolStripMenuItem byteRulerButton = new("バイトルーラー") { Name = "ByteRuler", CheckOnClick = true };
    private int recordRulerRowIndex = -1;
    private readonly Dictionary<int, int> byteRulerLengths = new();
    private readonly Dictionary<int, string> byteRulerErrors = new();
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
        row.Cells[0].Value = "バイト目盛り";
        row.Cells[0].ToolTipText = "各項目の先頭が1です。目盛りの単位はバイトです。文字の画面幅や折り返し後の位置とは異なります。";
        RefreshByteRulers();
    }

    private void RefreshByteRulers(int? changedField = null)
    {
        if (layout != GridLayout.RecordRows || recordRulerRowIndex < 0 || recordRulerRowIndex >= grid.Rows.Count) return;
        var encoding = IsCsv ? JapaneseEncodings.ForCsv(currentCsvEncoding) : null;
        for (var column = 1; column < grid.Columns.Count; column++)
        {
            var field = FieldIndexFromRecordColumn(column);
            if (changedField is not null && changedField != field) continue;
            try
            {
                var length = IsCsv ? records.Select(record => encoding!.GetByteCount(record[field])).DefaultIfEmpty(0).Max()
                    : fields[field].PhysicalByteLength;
                byteRulerLengths[field] = length;
                byteRulerErrors.Remove(field);
            }
            catch (EncoderFallbackException)
            {
                byteRulerLengths.Remove(field);
                byteRulerErrors[field] = "選択した文字コードで表せない値があるため、最大バイト長を計測できません。";
            }
        }
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
            var field = FieldIndexFromRecordColumn(column);
            if (byteRulerErrors.TryGetValue(field, out var error))
            {
                cell.Value = "計測エラー";
                cell.ToolTipText = error;
                continue;
            }
            var length = byteRulerLengths.GetValueOrDefault(field);
            var slots = (int)Math.Floor(Math.Max(1, grid.Columns[column].Width - cell.InheritedStyle.Padding.Horizontal - 8) / digitWidth);
            cell.Value = ByteRulerText.Format(length, slots);
            cell.ToolTipText = $"項目先頭=1 / {(IsCsv ? "読み込んだデータの最大" : "固定長の格納長")} {length} バイト。\r\n"
                + (IsCsv ? $"文字コード: {JapaneseEncodings.Name(currentCsvEncoding)}。" : "型N・COMP-3は実際の格納バイト数です。")
                + "文字の画面幅とは別の単位です。…は目盛りの続きがあることを示します。";
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
        var field = FieldIndexFromRecordColumn(e.ColumnIndex);
        if (!byteRulerLengths.TryGetValue(field, out var length) || length < 10) return;
        if (!e.Handled) { e.Paint(e.ClipBounds, e.PaintParts); e.Handled = true; }
        var width = ByteRulerDigitWidth();
        var left = e.CellBounds.Left + e.CellStyle.Padding.Left + 4;
        var slots = Math.Clamp((int)Math.Floor((e.CellBounds.Width - e.CellStyle.Padding.Horizontal - 8) / width), 0, 2048);
        if (length > slots) slots--;
        using var pen = new Pen(Color.FromArgb(135, 70, 125, 205)) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
        var state = e.Graphics.Save();
        try
        {
            e.Graphics.SetClip(Rectangle.Intersect(e.ClipBounds, e.CellBounds), System.Drawing.Drawing2D.CombineMode.Intersect);
            foreach (var position in ByteRulerText.GuidePositions(length, slots))
            {
                var x = left + (int)Math.Round(position * width);
                e.Graphics.DrawLine(pen, x, e.CellBounds.Top + 1, x, e.CellBounds.Bottom - 2);
            }
        }
        finally { e.Graphics.Restore(state); }
    }
}
