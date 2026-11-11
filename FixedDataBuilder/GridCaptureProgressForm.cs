namespace FixedDataBuilder;

internal sealed class GridCaptureProgressForm : Form
{
    private readonly CancellationTokenSource cancellation = new();
    private readonly Label message = new() { Dock = DockStyle.Top, Height = 46, TextAlign = ContentAlignment.MiddleLeft };
    private readonly ProgressBar progress = new() { Dock = DockStyle.Top, Height = 24 };
    private readonly Button cancel = new() { Text = "中止", AutoSize = true, Dock = DockStyle.Right };
    private bool finished;
    private bool clipboardTransfer;

    public CancellationToken Token => cancellation.Token;

    public GridCaptureProgressForm(int count, bool copying = false)
    {
        Text = copying ? "一覧の全範囲を画像コピー" : "一覧の全範囲を画像保存";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = MaximizeBox = false;
        ClientSize = new Size(480, 136);
        Padding = new Padding(14);
        progress.Maximum = count;
        if (copying)
        {
            progress.Style = ProgressBarStyle.Marquee;
            message.Text = "画像の範囲を確認しています...";
        }
        else message.Text = $"0 / {count} ページ";
        var buttons = new Panel { Dock = DockStyle.Bottom, Height = 30 };
        buttons.Controls.Add(cancel);
        Controls.AddRange([progress, message, buttons]);
        cancel.Click += (_, _) => RequestCancel();
        FormClosing += (_, e) => { if (!finished) { e.Cancel = true; if (!clipboardTransfer) RequestCancel(); } };
    }

    private void RequestCancel()
    {
        cancellation.Cancel();
        cancel.Enabled = false;
        message.Text = "中止しています。保存済みの画像は残します。";
    }

    public void Report(int count, string file)
    {
        progress.Value = count;
        message.Text = $"{count} / {progress.Maximum} ページ\r\n{file}";
    }

    public void ReportCells(int count)
    {
        progress.Value = count;
        message.Text = $"画像を作成しています... {count:N0} / {progress.Maximum:N0}";
    }

    public void SetCopyTotal(int count)
    {
        progress.Style = ProgressBarStyle.Blocks;
        progress.Maximum = Math.Max(1, count);
        progress.Value = 0;
        message.Text = $"画像を作成しています... 0 / {count:N0}";
    }

    public void Finish() { finished = true; Close(); }

    public void BeginClipboardTransfer()
    {
        clipboardTransfer = true;
        cancel.Enabled = false;
        progress.Style = ProgressBarStyle.Marquee;
        message.Text = "クリップボードへ転送しています...";
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) cancellation.Dispose();
        base.Dispose(disposing);
    }
}
