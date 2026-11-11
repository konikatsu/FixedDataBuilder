using System.Drawing.Drawing2D;
using System.Text;

namespace FixedDataBuilder;

public sealed partial class VisualComparisonForm
{
    private sealed partial class RecordListPane
    {
        // Lines retain the original spaces so literal marker characters are never mistaken for blanks.
        private IReadOnlyList<string> ComparisonDisplayLines(int record, int field)
        {
            var data = DataSet!;
            var value = LogicalDisplayValue(record, field);
            if (!wrapOptions.Enabled || (data.Profile.Format == DataFileFormat.Csv && data.Fields[field].IsRedefines))
                return [value];
            try
            {
                var encoding = data.Profile.Format == DataFileFormat.Csv ? JapaneseEncodings.ForCsv(data.Profile.CsvEncoding)
                    : data.Fields[field].Type == FieldDataType.FullWidthText ? NationalTextEncodingHelper.GetEncoding(data.Profile.NationalEncoding)
                    : JapaneseEncodings.ForData(data.Profile.DataEncoding);
                var truncated = false;
                var displayed = wrapOptions.LimitDisplay ? DisplayTextLimiter.Limit(value, wrapOptions.MaxCharacters, out truncated) : value;
                var shouldWrap = encoding.GetByteCount(value) >= wrapOptions.ThresholdBytes || value.Contains('\r') || value.Contains('\n');
                var lines = (shouldWrap ? ByteTextWrapper.Lines(displayed, encoding, wrapOptions.LineBytes) : [displayed]).ToArray();
                if (truncated) lines[^1] += "…";
                return lines;
            }
            catch (EncoderFallbackException) { return [value]; }
        }

        private void AdjustComparisonDataRows()
        {
            if (!wrapOptions.Enabled || DataSet is not { } data) return;
            // Preserve virtual/shared rows at the default height; unshare only rows that actually wrap.
            for (var record = 0; record < data.Records.Count; record++)
            {
                var maxLines = 1;
                for (var field = 0; field < data.Fields.Count; field++)
                {
                    var lines = ComparisonDisplayLines(record, field);
                    maxLines = Math.Max(maxLines, lines.Count);
                    foreach (var line in lines)
                    {
                        var visible = showSpaces.Checked ? line.Replace(' ', '\uFF65').Replace('\u3000', '\u25A1') : line;
                        var width = TextRenderer.MeasureText(visible, ownedFont, Size.Empty, TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine).Width + 12;
                        if (width > grid.Columns[field + 1].Width) grid.Columns[field + 1].Width = Math.Min(65536, width);
                    }
                }
                var height = (int)Math.Min(65536L, (long)maxLines * (ownedFont.Height + 2) + 6);
                if (height > grid.RowTemplate.Height) grid.Rows[metadataRows + record].Height = height;
            }
        }

        private void PaintComparisonValue(DataGridViewCellPaintingEventArgs e)
        {
            if (DataSet is null || e.Graphics is not { } graphics || e.CellStyle is not { } style) return;
            var record = e.RowIndex - metadataRows;
            var field = e.ColumnIndex - 1;
            if (record < 0 || record >= DataSet.Records.Count || field < 0 || field >= DataSet.Fields.Count) return;
            var lines = ComparisonDisplayLines(record, field);
            if (!wrapOptions.Enabled && (!showSpaces.Checked || !lines[0].Contains(' ') && !lines[0].Contains('\u3000'))) return;
            e.Paint(e.ClipBounds, e.PaintParts & ~DataGridViewPaintParts.ContentForeground);
            var selected = (e.State & DataGridViewElementStates.Selected) != 0;
            var font = style.Font ?? grid.Font;
            var flags = TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.PreserveGraphicsClipping;
            var state = graphics.Save();
            try
            {
                graphics.SetClip(Rectangle.Intersect(e.CellBounds, e.ClipBounds), CombineMode.Intersect);
                var x = e.CellBounds.Left + style.Padding.Left + 4;
                var y = e.CellBounds.Top + style.Padding.Top + 2;
                foreach (var line in lines)
                {
                    if (y >= e.CellBounds.Bottom) break;
                    var visible = showSpaces.Checked ? line.Replace(' ', '\uFF65').Replace('\u3000', '\u25A1') : line;
                    if (showSpaces.Checked && !selected)
                    {
                        using var brush = new SolidBrush(Color.FromArgb(255, 250, 205));
                        var start = -1;
                        for (var i = 0; i <= line.Length; i++)
                        {
                            var space = i < line.Length && (line[i] == ' ' || line[i] == '\u3000');
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
                    y += font.Height + 2;
                }
            }
            finally { graphics.Restore(state); }
            e.Handled = true;
        }
    }
}
