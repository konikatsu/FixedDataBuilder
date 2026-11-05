using System.Drawing.Imaging;
using System.Security.Cryptography;
using System.Text.Json;

namespace FixedDataBuilder;

public sealed partial class MainForm
{
    private readonly ToolStripMenuItem captureAllImagesButton = new("一覧の全範囲を画像保存...") { Name = "CaptureAllImages", Enabled = false };
    private const int CaptureBannerHeight = 100;
    private const int CaptureFooterHeight = 26;

    private sealed record GridCapturePlan(IReadOnlyList<GridCaptureAxisPage> Columns, IReadOnlyList<GridCaptureAxisPage> Rows)
    {
        public int Count => checked(Columns.Count * Rows.Count);
    }

    private (IReadOnlyList<GridCaptureBand> Columns, IReadOnlyList<GridCaptureBand> Rows) BuildGridCaptureBands()
    {
        if (isSingleRecordView || records.Count == 0 || grid.Columns.Count == 0 || VisibleFieldIndexes().Count == 0)
            throw new InvalidOperationException("データの一覧を表示してから実行してください。");
        var columns = new List<GridCaptureBand>();
        if (grid.RowHeadersVisible) columns.Add(new(-1, grid.RowHeadersWidth, true));
        columns.AddRange(grid.Columns.Cast<DataGridViewColumn>().Where(column => column.Visible)
            .OrderBy(column => column.DisplayIndex).Select(column => new GridCaptureBand(column.Index, column.Width, column.Frozen)));
        var rows = new List<GridCaptureBand>();
        if (grid.ColumnHeadersVisible) rows.Add(new(-1, grid.ColumnHeadersHeight, true));
        rows.AddRange(grid.Rows.Cast<DataGridViewRow>().Where(row => row.Visible && !row.IsNewRow)
            .Select(row => new GridCaptureBand(row.Index, row.Height,
                row.Frozen || (layout == GridLayout.RecordRows && row.Index < RecordRowsDataStartIndex()))));
        return (columns, rows);
    }

    private GridCapturePlan BuildGridCapturePlan()
    {
        var (columns, rows) = BuildGridCaptureBands();
        var result = new GridCapturePlan(
            GridCapturePagination.Paginate(columns, Math.Clamp(grid.ClientSize.Width - SystemInformation.VerticalScrollBarWidth, 64, 4096)),
            GridCapturePagination.Paginate(rows, Math.Clamp(grid.ClientSize.Height - SystemInformation.HorizontalScrollBarHeight, 64, 4096)));
        if ((long)result.Columns.Count * result.Rows.Count > 10000)
            throw new InvalidOperationException("画像が10,000ページを超えます。文字サイズや列幅を小さくして実行してください。");
        return result;
    }

    private void ExportGridImages()
    {
        if (!grid.EndEdit()) return;
        string? directory = null;
        try
        {
            var plan = BuildGridCapturePlan();
            using var folder = new FolderBrowserDialog
            {
                Description = $"一覧の全範囲（縦 {plan.Rows.Count} × 横 {plan.Columns.Count}、PNG {plan.Count}枚）の保存先を選んでください。\r\nファイル名と日時を付けたフォルダーを作成します。",
                UseDescriptionForTitle = true
            };
            if (folder.ShowDialog(this) != DialogResult.OK) return;
            var prefix = CaptureFilePrefix();
            directory = Path.Combine(folder.SelectedPath, $"{prefix}_capture_{DateTime.Now:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid().ToString("N")[..6]}");
            Directory.CreateDirectory(directory);
            Exception? error = null;
            var cancelled = false;
            using var progress = new GridCaptureProgressForm(plan.Count);
            progress.Shown += async (_, _) =>
            {
                try { await WriteGridCaptureAsync(directory, prefix, plan, progress.Token, progress.Report); }
                catch (OperationCanceledException) { cancelled = true; }
                catch (Exception ex) { error = ex; }
                finally { progress.Finish(); }
            };
            progress.ShowDialog(this);
            if (error is not null) throw error;
            SetStatus($"画像保存{(cancelled ? "を中止しました。保存済み画像" : "しました")}: {directory}");
            MessageBox.Show(this, $"{(cancelled ? "中止しました。保存済みの画像は残しています。" : $"PNG {plan.Count}枚を保存しました。")}{Environment.NewLine}{directory}",
                "一覧の画像保存", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message + (directory is null ? "" : $"\r\n保存先: {directory}\r\n保存済みの画像は残しています。"),
                "画像保存エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private string CaptureFilePrefix()
    {
        var name = Path.GetFileNameWithoutExtension(saveDataPath ?? "data");
        var safe = new string(name.Select(character => Path.GetInvalidFileNameChars().Contains(character) || char.IsControl(character) ? '_' : character).ToArray()).Trim().TrimEnd('.');
        if (safe.Length == 0) safe = "data";
        // Leave room for page numbers and the dated folder in ordinary Windows paths.
        if (safe.Length > 48) safe = safe[..(char.IsHighSurrogate(safe[47]) ? 47 : 48)].TrimEnd('.', ' ');
        return safe;
    }

    private async Task WriteGridCaptureAsync(string directory, string prefix, GridCapturePlan plan,
        CancellationToken cancellation, Action<int, string> report)
    {
        var capturedAt = DateTimeOffset.Now;
        var pages = new List<object>();
        var status = "incomplete";
        string? error = null;
        try
        {
            for (var y = 0; y < plan.Rows.Count; y++)
            for (var x = 0; x < plan.Columns.Count; x++)
            {
                cancellation.ThrowIfCancellationRequested();
                var name = $"{prefix}_p{pages.Count + 1:D4}_y{y + 1:D3}_x{x + 1:D3}.png";
                var path = Path.Combine(directory, name);
                using (var bitmap = RenderGridCapturePage(plan.Columns[x], plan.Rows[y],
                    $"{capturedAt:yyyy-MM-dd HH:mm:ss zzz}  縦 {y + 1}/{plan.Rows.Count}  横 {x + 1}/{plan.Columns.Count}  ページ {pages.Count + 1}/{plan.Count}"))
                {
                    WriteGridCaptureImage(bitmap, path);
                }
                using var input = File.OpenRead(path);
                pages.Add(new { file = name, sha256 = Convert.ToHexString(SHA256.HashData(input)),
                    vertical_page = y + 1, horizontal_page = x + 1, rows = plan.Rows[y].Slices, columns = plan.Columns[x].Slices });
                report(pages.Count, name);
                await Task.Delay(1); // Return to the modal progress dialog so cancellation can be processed.
            }
            status = "complete";
        }
        catch (OperationCanceledException) { status = "cancelled"; throw; }
        catch (Exception ex) { status = "error"; error = ex.Message; throw; }
        finally
        {
            var manifest = new
            {
                schema_version = 1, status, error, captured_at = capturedAt, application = WindowTitle,
                definition_file = definitionPathTextBox.Text, data_file = dataPathTextBox.Text,
                data_format = separatorModeTextBox.Text, unsaved_changes = hasUnsavedChanges,
                view = layout.ToString(), font = new { grid.Font.Name, grid.Font.Size, grid.Font.Style },
                display_wrap = displayWrapOptions, byte_ruler = recordRulerRowIndex >= 0,
                horizontal_pages = plan.Columns.Count, vertical_pages = plan.Rows.Count,
                planned_images = plan.Count, saved_images = pages.Count,
                note = "画面上の一覧表を描画。非表示項目、表示上限で省略された文字、ToolTipは含みません。青線はバイト目盛りの補助線です。",
                pages
            };
            using var output = new FileStream(Path.Combine(directory, "capture.json"), FileMode.CreateNew, FileAccess.Write, FileShare.None);
            JsonSerializer.Serialize(output, manifest, new JsonSerializerOptions { WriteIndented = true });
        }
    }

    private static void WriteGridCaptureImage(Bitmap bitmap, string path)
    {
        var partial = path + "." + Guid.NewGuid().ToString("N") + ".partial";
        try
        {
            using (var stream = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                bitmap.Save(stream, ImageFormat.Png);
            File.Move(partial, path, overwrite: false);
        }
        finally
        {
            try { if (File.Exists(partial)) File.Delete(partial); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* Keep the original save error. */ }
        }
    }

    private Bitmap RenderGridCapturePage(GridCaptureAxisPage columns, GridCaptureAxisPage rows, string pageLabel)
    {
        var bitmap = new Bitmap(Math.Max(640, columns.Length), rows.Length + CaptureBannerHeight + CaptureFooterHeight, PixelFormat.Format32bppArgb);
        bitmap.SetResolution(grid.DeviceDpi, grid.DeviceDpi);
        try
        {
            using var graphics = Graphics.FromImage(bitmap);
            graphics.Clear(Color.White);
            using var headingFont = new Font(FontFamily.GenericSansSerif, 12f, FontStyle.Regular, GraphicsUnit.Pixel);
            var flags = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis
                | TextFormatFlags.PreserveGraphicsClipping;
            var headings = new[] { WindowTitle + (hasUnsavedChanges ? " 【未保存の編集を含む】" : ""),
                "定義: " + definitionPathTextBox.Text, "データ: " + dataPathTextBox.Text,
                separatorModeTextBox.Text, pageLabel };
            for (var line = 0; line < headings.Length; line++)
                TextRenderer.DrawText(graphics, headings[line], headingFont, new Rectangle(4, line * 20, bitmap.Width - 8, 20), Color.Black, flags);
            foreach (var row in rows.Slices)
            foreach (var column in columns.Slices)
            {
                var bounds = new Rectangle(column.Position - column.Offset,
                    CaptureBannerHeight + row.Position - row.Offset, column.FullLength, row.FullLength);
                var clip = new Rectangle(column.Position, CaptureBannerHeight + row.Position, column.Length, row.Length);
                var state = graphics.Save();
                try
                {
                    graphics.SetClip(clip);
                    PaintGridCaptureCell(graphics, column.Index, row.Index, bounds, clip);
                }
                finally { graphics.Restore(state); }
            }
            TextRenderer.DrawText(graphics, pageLabel, headingFont,
                new Rectangle(4, bitmap.Height - CaptureFooterHeight, bitmap.Width - 8, CaptureFooterHeight), Color.DimGray, flags);
            return bitmap;
        }
        catch { bitmap.Dispose(); throw; }
    }

    private void PaintGridCaptureCell(Graphics graphics, int column, int row, Rectangle bounds, Rectangle clip)
    {
        var cell = column < 0 ? row < 0 ? grid.TopLeftHeaderCell : grid.Rows[row].HeaderCell
            : row < 0 ? grid.Columns[column].HeaderCell : grid[column, row];
        var border = row < 0 ? grid.AdvancedColumnHeadersBorderStyle
            : column < 0 ? grid.AdvancedRowHeadersBorderStyle : grid.AdvancedCellBorderStyle;
        var args = new DataGridViewCellPaintingEventArgs(grid, graphics, clip, bounds, row, column,
            cell.State & ~DataGridViewElementStates.Selected, cell.Value, cell.FormattedValue, cell.ErrorText, cell.InheritedStyle,
            border, DataGridViewPaintParts.All & ~(DataGridViewPaintParts.Focus | DataGridViewPaintParts.SelectionBackground));
        if (layout == GridLayout.RecordRows && row == recordOccursRowIndex && column > 0
            && occursDisplayInfos.TryGetValue(FieldIndexFromRecordColumn(column), out var occurs))
        {
            // Use the same merged OCCURS band, with virtual page coordinates.
            var first = FirstRecordColumnInOccursGroup(column, occurs);
            var last = LastRecordColumnInOccursGroup(column, occurs);
            var before = Enumerable.Range(first, column - first).Sum(index => grid.Columns[index].Visible ? grid.Columns[index].Width : 0);
            var width = Enumerable.Range(first, last - first + 1).Sum(index => grid.Columns[index].Visible ? grid.Columns[index].Width : 0);
            var span = new Rectangle(bounds.Left - before, bounds.Top, width, bounds.Height);
            using var background = new SolidBrush(OccursBackColor(occurs));
            graphics.FillRectangle(background, span);
            DrawCellBorder(graphics, span, true, true);
            TextRenderer.DrawText(graphics, $"{occurs.GroupName}  {occurs.OccurrenceIndex}/{occurs.OccurrenceCount}",
                cell.InheritedStyle.Font ?? grid.Font, span, OccursHeaderForeColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis
                    | TextFormatFlags.PreserveGraphicsClipping);
            return;
        }
        PaintTextPaddingArea(args);
        PaintByteGuideLines(args);
        if (!args.Handled) args.Paint(clip, args.PaintParts);
    }
}
