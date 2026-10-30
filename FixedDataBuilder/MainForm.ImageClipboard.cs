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

    private void CopyAllGridImage()
    {
        if (!grid.EndEdit()) return;
        var previousWaitCursor = UseWaitCursor;
        try
        {
            UseWaitCursor = true;
            CopyAllGridImageTo(image =>
            {
                try { Clipboard.SetImage(image); }
                catch (ExternalException ex)
                {
                    throw new InvalidOperationException("クリップボードへ画像をコピーできませんでした。\r\n"
                        + "他のアプリが使用している可能性があります。少し待ってから、もう一度実行してください。", ex);
                }
            });
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
        finally { UseWaitCursor = previousWaitCursor; }
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
