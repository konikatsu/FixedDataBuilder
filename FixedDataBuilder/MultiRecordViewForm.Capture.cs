using System.Drawing.Imaging;

namespace FixedDataBuilder;

public sealed partial class MultiRecordViewForm
{
    private const int CaptureHeaderHeight = 64;
    private (IReadOnlyList<GridCaptureBand> Columns, IReadOnlyList<GridCaptureBand> Rows) CaptureBands()
    {
        if (rows.Count == 0) throw new InvalidOperationException("データを表示してから実行してください。");
        var columns = grid.Columns.Cast<DataGridViewColumn>().Where(c => c.Visible).OrderBy(c => c.DisplayIndex)
            .Select(c => new GridCaptureBand(c.Index, c.Width, c.Frozen)).ToArray();
        var bands = Enumerable.Range(0, grid.RowCount).Select(i => new GridCaptureBand(i, grid.Rows[i].Height, false)).ToArray();
        return (columns, bands);
    }

    private void CopyAllImages()
    {
        try
        {
            var (columns, rows) = CaptureBands();
            GridCaptureImageLayout layout;
            try { layout = GridCaptureImageLayout.Create(columns, rows, CaptureHeaderHeight); }
            catch (InvalidOperationException) { throw new InvalidOperationException("画像が大きいため1枚にコピーできません。「全範囲を画像保存...」で分割画像を保存してください。"); }
            using var progress = new GridCaptureProgressForm(1, copying: true);
            Exception? error = null; var cancelled = false;
            progress.Shown += async (_, _) =>
            {
                try
                {
                    progress.SetCopyTotal(layout.Rows.Slices.Count);
                    using var bitmap = await RenderCaptureAsync(layout.Columns, layout.Rows, "全範囲", progress.Token, progress.ReportCells);
                    progress.Token.ThrowIfCancellationRequested(); progress.BeginClipboardTransfer(); Clipboard.SetImage(bitmap);
                }
                catch (OperationCanceledException) { cancelled = true; }
                catch (Exception ex) { error = ex; }
                finally { progress.Finish(); }
            };
            progress.ShowDialog(this);
            if (error is not null) throw error;
            status.Text = cancelled ? "画像コピーを中止しました。" : "全範囲を画像コピーしました。Excelなどへ貼り付けできます。";
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or OutOfMemoryException or System.Runtime.InteropServices.ExternalException)
        { MessageBox.Show(this, ex.Message, "画像コピー", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    private void SaveAllImages()
    {
        string? directory = null;
        try
        {
            var (columns, rows) = CaptureBands();
            var columnPages = GridCapturePagination.Paginate(columns, Math.Clamp(grid.ClientSize.Width - SystemInformation.VerticalScrollBarWidth, 256, 4096));
            var widest = Math.Max(640, columnPages.Max(page => page.Length));
            var rowLimit = Math.Max(256, (int)Math.Min(GridCaptureImageLayout.MaximumDimension - CaptureHeaderHeight, GridCaptureImageLayout.MaximumPixels / widest - CaptureHeaderHeight));
            var rowPages = GridCapturePagination.Paginate(rows, rowLimit);
            var count = checked(columnPages.Count * rowPages.Count);
            if (count > 10000) throw new InvalidOperationException("画像が10,000枚を超えます。表示範囲を見直してください。");
            using var folder = new FolderBrowserDialog { Description = $"全範囲の画像（{count}枚）の保存先を選んでください。", UseDescriptionForTitle = true };
            if (folder.ShowDialog(this) != DialogResult.OK) return;
            directory = Path.Combine(folder.SelectedPath, $"HEAD_BODY_capture_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid().ToString("N")[..6]}");
            Directory.CreateDirectory(directory);
            var images = new List<string>(); Exception? error = null; var cancelled = false;
            using var progress = new GridCaptureProgressForm(count);
            progress.Shown += async (_, _) =>
            {
                try
                {
                    for (var y = 0; y < rowPages.Count; y++)
                    for (var x = 0; x < columnPages.Count; x++)
                    {
                        progress.Token.ThrowIfCancellationRequested();
                        var name = $"capture_p{images.Count + 1:D4}_y{y + 1:D3}_x{x + 1:D3}.png";
                        using var bitmap = await RenderCaptureAsync(columnPages[x], rowPages[y], $"縦 {y + 1}/{rowPages.Count} 横 {x + 1}/{columnPages.Count}", progress.Token, null);
                        var path = Path.Combine(directory, name);
                        using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) bitmap.Save(stream, ImageFormat.Png);
                        images.Add(path); progress.Report(images.Count, name);
                    }
                }
                catch (OperationCanceledException) { cancelled = true; }
                catch (Exception ex) { error = ex; }
                finally { progress.Finish(); }
            };
            progress.ShowDialog(this);
            if (error is not null) throw error;
            status.Text = cancelled ? $"画像保存を中止しました。保存済み: {directory}" : $"画像を{images.Count}枚保存しました: {directory}";
            if (images.Count > 0) new CaptureImageCopyForm(images).Show(this);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or IOException or UnauthorizedAccessException or OutOfMemoryException or OverflowException)
        { MessageBox.Show(this, ex.Message + (directory is null ? "" : $"\r\n保存先: {directory}"), "画像保存", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    private async Task<Bitmap> RenderCaptureAsync(GridCaptureAxisPage columns, GridCaptureAxisPage rowPage, string label,
        CancellationToken cancellation, Action<int>? report)
    {
        var bitmap = new Bitmap(Math.Max(640, columns.Length), rowPage.Length + CaptureHeaderHeight, PixelFormat.Format32bppArgb);
        try
        {
            bitmap.SetResolution(grid.DeviceDpi, grid.DeviceDpi);
            using var graphics = Graphics.FromImage(bitmap); graphics.Clear(Color.White);
            var flags = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
            TextRenderer.DrawText(graphics, Text + " / " + label, Font, new Rectangle(4, 1, bitmap.Width - 8, 20), Color.Black, flags);
            TextRenderer.DrawText(graphics, "定義: " + definitionPath.Text, Font, new Rectangle(4, 21, bitmap.Width - 8, 20), Color.Black, flags);
            TextRenderer.DrawText(graphics, "データ: " + dataPath.Text, Font, new Rectangle(4, 41, bitmap.Width - 8, 20), Color.Black, flags);
            var completed = 0;
            foreach (var row in rowPage.Slices)
            {
                cancellation.ThrowIfCancellationRequested();
                foreach (var column in columns.Slices)
                {
                    var bounds = new Rectangle(column.Position - column.Offset, CaptureHeaderHeight + row.Position - row.Offset, column.FullLength, row.FullLength);
                    var clip = new Rectangle(column.Position, CaptureHeaderHeight + row.Position, column.Length, row.Length);
                    var cell = grid[column.Index, row.Index];
                    var style = cell.InheritedStyle.Clone(); ApplyRowStyle(row.Index, column.Index, style);
                    var args = new DataGridViewCellPaintingEventArgs(grid, graphics, clip, bounds, row.Index, column.Index,
                        cell.State & ~DataGridViewElementStates.Selected, cell.Value, cell.FormattedValue, cell.ErrorText, style,
                        grid.AdvancedCellBorderStyle, DataGridViewPaintParts.All & ~(DataGridViewPaintParts.Focus | DataGridViewPaintParts.SelectionBackground));
                    var state = graphics.Save();
                    try { graphics.SetClip(clip); PaintGroupCell(grid, args); if (!args.Handled) args.Paint(clip, args.PaintParts); }
                    finally { graphics.Restore(state); }
                }
                completed++; report?.Invoke(completed);
                if (completed % 8 == 0) await Task.Delay(1, cancellation);
            }
            return bitmap;
        }
        catch { bitmap.Dispose(); throw; }
    }
}
