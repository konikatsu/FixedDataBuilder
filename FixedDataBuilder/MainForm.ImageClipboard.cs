using System.Runtime.InteropServices;

namespace FixedDataBuilder;

public sealed partial class MainForm
{
    private readonly Button copyAllGridImageButton = new()
    {
        Name = "CopyAllGridImage", Text = "全範囲を画像コピー", AutoSize = true, Enabled = false,
        AccessibleDescription = "一覧の縦横全範囲を1枚の画像にしてクリップボードへコピーします。"
    };
    private readonly ToolStripMenuItem copyAllGridImageMenu = new("一覧の全範囲を画像コピー")
    {
        Name = "CopyAllGridImageMenu", Enabled = false
    };

    private GridCaptureImageLayout BuildClipboardImageLayout()
    {
        var (columns, rows) = BuildGridCaptureBands();
        return GridCaptureImageLayout.Create(columns, rows, CaptureBannerHeight + CaptureFooterHeight);
    }

    private void CopyAllGridImage() => CopyAllGridImageWith(Clipboard.SetImage);

    private void CopyAllGridImageWith(Action<Image> clipboardWriter)
    {
        if (!grid.EndEdit()) return;
        try
        {
            GridCaptureImageLayout? capture = null;
            using var progress = new GridCaptureProgressForm(1, copying: true);
            Exception? error = null;
            var cancelled = false;
            var copied = false;
            progress.Shown += async (_, _) =>
            {
                try
                {
                    await Task.Yield();
                    progress.Token.ThrowIfCancellationRequested();
                    capture = BuildClipboardImageLayout();
                    progress.Token.ThrowIfCancellationRequested();
                    var cellCount = checked(capture.Columns.Slices.Count * capture.Rows.Slices.Count);
                    progress.SetCopyTotal(cellCount);
                    var label = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}  一覧の全範囲（1枚）";
                    using var bitmap = await RenderGridCapturePageAsync(capture.Columns, capture.Rows, label,
                        progress.Token, progress.ReportCells);
                    progress.Token.ThrowIfCancellationRequested();
                    progress.BeginClipboardTransfer();
                    await WriteClipboardImageAsync(bitmap, image =>
                    {
                        try { clipboardWriter(image); }
                        catch (ExternalException ex)
                        {
                            throw new InvalidOperationException("クリップボードへ画像をコピーできませんでした。\r\n"
                                + "他のアプリが使用している可能性があります。少し待ってから、もう一度実行してください。", ex);
                        }
                    });
                    copied = true;
                }
                catch (OperationCanceledException) { cancelled = true; }
                catch (Exception ex) { error = ex; }
                finally { progress.Finish(); }
            };
            progress.ShowDialog(this);
            if (error is not null) throw error;
            if (cancelled) SetStatus("画像コピーを中止しました。");
            else if (copied && capture is not null) SetStatus($"一覧の全範囲を画像コピーしました（{capture.ImageSize.Width:N0} × {capture.ImageSize.Height:N0} ピクセル）。Excelなどへ貼り付けできます。");
        }
        catch (OutOfMemoryException)
        {
            MessageBox.Show(this, "画像をコピーするためのメモリが不足しています。\r\n"
                + "文字サイズや列幅を小さくするか、「ファイル → 一覧の全範囲を画像保存...」で分割PNGを保存してください。",
                "画像コピーエラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "画像コピーエラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task<Bitmap> RenderGridCapturePageAsync(GridCaptureAxisPage columns, GridCaptureAxisPage rows,
        string pageLabel, CancellationToken cancellation, Action<int> report)
    {
        // Let the progress dialog paint before allocating and drawing the bitmap.
        await Task.Yield();
        cancellation.ThrowIfCancellationRequested();
        var bitmap = CreateGridCaptureCanvas(columns, rows, pageLabel);
        var completed = 0;
        var timer = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            foreach (var row in rows.Slices)
            {
                for (var first = 0; first < columns.Slices.Count; first += 8)
                {
                    cancellation.ThrowIfCancellationRequested();
                    var end = Math.Min(first + 8, columns.Slices.Count);
                    using (var graphics = Graphics.FromImage(bitmap))
                    {
                        for (var index = first; index < end; index++)
                            PaintGridCaptureSlice(graphics, columns.Slices[index], row);
                    }
                    completed += end - first;
                    if (timer.ElapsedMilliseconds >= 40)
                    {
                        report(completed);
                        await Task.Delay(1);
                        timer.Restart();
                    }
                }
            }
            report(completed);
            cancellation.ThrowIfCancellationRequested();
            return bitmap;
        }
        catch { bitmap.Dispose(); throw; }
    }

    private static Task WriteClipboardImageAsync(Image bitmap, Action<Image> writeImage)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { writeImage(bitmap); completion.SetResult(); }
            catch (Exception ex) { completion.SetException(ex); }
        }) { IsBackground = true, Name = "ImageClipboardWriter" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private void CopyAllGridImageTo(Action<Image> writeImage)
    {
        var capture = BuildClipboardImageLayout();
        using var bitmap = RenderGridCapturePage(capture.Columns, capture.Rows,
            $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}  一覧の全範囲（1枚）");
        writeImage(bitmap);
        SetStatus($"一覧の全範囲を画像コピーしました（{bitmap.Width:N0} × {bitmap.Height:N0} ピクセル）。Excelなどへ貼り付けできます。");
    }
}
