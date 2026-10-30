using System.Drawing.Drawing2D;
using System.Text;

namespace FixedDataBuilder;

public sealed partial class VisualComparisonForm
{
    private sealed partial class RecordListPane
    {
        private readonly CheckBox showByteRuler = new()
        {
            Name = "ShowByteRuler", Text = "バイト目盛り", Checked = true, AutoSize = true,
            Margin = new Padding(14, 7, 3, 0)
        };
        private readonly Dictionary<int, int> byteRulerLengths = new();
        private readonly Dictionary<int, string> byteRulerErrors = new();
        private int rulerRow = -1;
        private bool sizingMetadata;
        private Font? rulerMetricsFont;
        private int rulerMetricsDpi;
        private float rulerDigitWidth;

        private int? ByteRulerLength(int field)
        {
            if (DataSet is not { } data || field < 0 || field >= data.Fields.Count) return null;
            if (data.Profile.Format == DataFileFormat.Csv && data.Fields[field].IsRedefines) return null;
            if (byteRulerLengths.TryGetValue(field, out var cached)) return cached;
            if (byteRulerErrors.ContainsKey(field)) return null;
            try
            {
                var encoding = data.Profile.Format == DataFileFormat.Csv ? JapaneseEncodings.ForCsv(data.Profile.CsvEncoding) : null;
                var length = data.Profile.Format == DataFileFormat.Csv
                    ? data.Records.Select(record => encoding!.GetByteCount(record[field])).DefaultIfEmpty(0).Max()
                    : data.Fields[field].PhysicalByteLength;
                byteRulerLengths[field] = length;
                return length;
            }
            catch (EncoderFallbackException)
            {
                byteRulerErrors[field] = "選択した文字コードで表せない値があるため、最大バイト長を計測できません。";
                return null;
            }
        }

        private float ByteRulerDigitWidth()
        {
            if (ReferenceEquals(rulerMetricsFont, grid.Font) && rulerMetricsDpi == grid.DeviceDpi) return rulerDigitWidth;
            rulerMetricsFont = grid.Font;
            rulerMetricsDpi = grid.DeviceDpi;
            return rulerDigitWidth = Math.Max(1, "0123456789".Max(digit => TextRenderer.MeasureText(
                digit.ToString(), grid.Font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix).Width));
        }

        private int RulerSlots(int column) => Math.Clamp((int)Math.Floor(Math.Max(1,
            grid.Columns[column].Width - grid.DefaultCellStyle.Padding.Horizontal - 8) / ByteRulerDigitWidth()), 1, 2048);

        private string ByteRulerValue(int field)
        {
            if (DataSet is not { } data) return "";
            if (data.Profile.Format == DataFileFormat.Csv && data.Fields[field].IsRedefines) return "（CSV列なし）";
            return ByteRulerLength(field) is { } length ? ByteRulerText.Format(length, RulerSlots(field + 1)) : "計測エラー";
        }

        private string ByteRulerToolTip(int field)
        {
            if (DataSet is not { } data) return "";
            if (data.Profile.Format == DataFileFormat.Csv && data.Fields[field].IsRedefines) return "REDEFINESの別名項目にはCSVの物理列がありません。";
            var length = ByteRulerLength(field);
            if (length is null) return byteRulerErrors.GetValueOrDefault(field, "計測できません。");
            return $"項目先頭=1 / {(data.Profile.Format == DataFileFormat.Csv ? "読み込んだデータの最大" : "固定長の格納長")} {length} バイト。\r\n"
                + (data.Profile.Format == DataFileFormat.Csv ? $"文字コード: {JapaneseEncodings.Name(data.Profile.CsvEncoding)}。" : "型N・COMP-3は実際の格納バイト数です。")
                + "5バイトごとに数字と青い破線を表示します。文字の画面幅や折り返し後の位置とは別の単位です。…は目盛りの続きです。";
        }

        private void PaintByteRulerAndGuides(object? sender, DataGridViewCellPaintingEventArgs e)
        {
            if (rulerRow < 0 || e.RowIndex < rulerRow || e.ColumnIndex < 1 || e.Graphics is not { } graphics || e.CellStyle is not { } style) return;
            if (e.RowIndex == rulerRow)
            {
                e.Paint(e.ClipBounds, e.PaintParts & ~DataGridViewPaintParts.ContentForeground);
                var text = e.FormattedValue?.ToString() ?? "";
                var font = style.Font ?? grid.Font;
                var left = e.CellBounds.Left + style.Padding.Left + 4;
                var top = e.CellBounds.Top + (e.CellBounds.Height - 2 * font.Height) / 2;
                var flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.PreserveGraphicsClipping;
                var state = graphics.Save();
                try
                {
                    graphics.SetClip(Rectangle.Intersect(e.ClipBounds, e.CellBounds), CombineMode.Intersect);
                    if (!text.Contains('\n')) TextRenderer.DrawText(graphics, text, font, new Point(left, top), style.ForeColor, flags);
                    else
                    {
                        var lines = text.Split('\n');
                        for (var line = 0; line < lines.Length; line++)
                        for (var slot = 0; slot < lines[line].Length; slot++)
                            if (lines[line][slot] != ' ')
                                TextRenderer.DrawText(graphics, lines[line][slot].ToString(), font,
                                    new Point(left + (int)Math.Round(slot * ByteRulerDigitWidth()), top + line * font.Height), style.ForeColor, flags);
                    }
                }
                finally { graphics.Restore(state); }
                e.Handled = true;
            }
            if (ByteRulerLength(e.ColumnIndex - 1) is not { } length || length < 5) return;
            if (!e.Handled) { e.Paint(e.ClipBounds, e.PaintParts); e.Handled = true; }
            var slots = RulerSlots(e.ColumnIndex);
            if (length > slots) slots--;
            using var pen = new Pen(Color.FromArgb(135, 70, 125, 205)) { DashStyle = DashStyle.Dash };
            var saved = graphics.Save();
            try
            {
                graphics.SetClip(Rectangle.Intersect(e.ClipBounds, e.CellBounds), CombineMode.Intersect);
                foreach (var position in ByteRulerText.GuidePositions(length, slots))
                {
                    var x = e.CellBounds.Left + style.Padding.Left + 4 + (int)Math.Round(position * ByteRulerDigitWidth());
                    graphics.DrawLine(pen, x, e.CellBounds.Top + 1, x, e.CellBounds.Bottom - 2);
                }
            }
            finally { graphics.Restore(saved); }
        }

        private void AdjustMetadataRowHeights()
        {
            if (sizingMetadata || DataSet is null || grid.RowCount < metadataRows) return;
            sizingMetadata = true;
            var view = CaptureViewport();
            try
            {
                for (var row = metadataRows - 1; row >= 0; row--) grid.Rows[row].Frozen = false;
                foreach (var row in new[] { 0, commentRow })
                {
                    if (row < 0 || row >= metadataRows) continue;
                    var height = grid.Rows[row].GetPreferredHeight(row, DataGridViewAutoSizeRowMode.AllCellsExceptHeader, fixedWidth: true);
                    grid.Rows[row].Height = Math.Clamp(height, grid.RowTemplate.Height, 65536);
                }
                UpdateHeaderPinning();
                RestoreViewport(view);
            }
            finally { sizingMetadata = false; }
        }

        private void ChangeByteRulerVisibility()
        {
            if (rebuilding || DataSet is not { } data) return;
            var oldMetadata = metadataRows;
            var oldRuler = rulerRow;
            var view = CaptureViewport();
            var current = grid.CurrentCell is { } cell ? new Point(cell.ColumnIndex, cell.RowIndex) : (Point?)null;
            var selected = grid.SelectedCells.Cast<DataGridViewCell>().Select(cell => new Point(cell.ColumnIndex, cell.RowIndex)).ToArray();
            var heights = new Dictionary<int, int>();
            for (var record = 0; record < data.Records.Count; record++)
            {
                var height = grid.Rows.SharedRow(oldMetadata + record).Height;
                if (height != grid.RowTemplate.Height) heights[record] = height;
            }
            int Remap(int row) => row >= oldMetadata ? row - oldMetadata + metadataRows
                : row == oldRuler && rulerRow < 0 ? Math.Min(metadataRows, grid.RowCount - 1) : row;
            rebuilding = true;
            grid.SuspendLayout();
            try
            {
                for (var row = oldMetadata - 1; row >= 0; row--) grid.Rows[row].Frozen = false;
                if (showByteRuler.Checked) rulerRow = metadataRows++;
                else { rulerRow = -1; metadataRows--; }
                grid.RowCount = checked(metadataRows + data.Records.Count);
                for (var record = 0; record < data.Records.Count; record++)
                {
                    var row = metadataRows + record;
                    var height = heights.GetValueOrDefault(record, grid.RowTemplate.Height);
                    if (grid.Rows.SharedRow(row).Height != height) grid.Rows[row].Height = height;
                }
                if (rulerRow >= 0) grid.Rows[rulerRow].Height = ownedFont.Height * 2 + 8;
                UpdateHeaderPinning();
                if (current is { } target && Remap(target.Y) >= 0 && Remap(target.Y) < grid.RowCount)
                    grid.CurrentCell = grid[target.X, Remap(target.Y)];
                grid.ClearSelection();
                foreach (var point in selected)
                    if (!(point.Y == oldRuler && rulerRow < 0) && Remap(point.Y) >= 0 && Remap(point.Y) < grid.RowCount)
                        grid[point.X, Remap(point.Y)].Selected = true;
                RestoreViewport(view with { Row = view.Row >= 0 ? Remap(view.Row) : view.Row });
            }
            finally { grid.ResumeLayout(); rebuilding = false; }
            UpdateSelection();
            grid.Invalidate();
        }
    }
}
