using System.Runtime.InteropServices;

namespace FixedDataBuilder;

public sealed class CaptureImageCopyForm : Form
{
    private readonly IReadOnlyList<string> images;
    private readonly NumericUpDown page = new() { Name = "CaptureCopyPage", Minimum = 1, Width = 75 };
    private readonly Label fileName = new() { Name = "CaptureCopyFileName", AutoSize = true, MaximumSize = new Size(550, 0) };
    private readonly Label message = new() { Name = "CaptureCopyMessage", AutoSize = true, MaximumSize = new Size(550, 0) };
    private readonly Button previous = new() { Name = "CaptureCopyPrevious", Text = "◀ 前へ", AutoSize = true };
    private readonly Button next = new() { Name = "CaptureCopyNext", Text = "次へ ▶", AutoSize = true };

    public CaptureImageCopyForm(IReadOnlyList<string> images)
    {
        ArgumentNullException.ThrowIfNull(images);
        if (images.Count == 0) throw new ArgumentException("コピーする画像がありません。", nameof(images));
        this.images = images.ToArray();
        Text = "保存した画像をコピー";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(600, 190);
        MinimumSize = new Size(480, 220);
        page.Maximum = images.Count;
        var copy = new Button { Name = "CaptureCopyImage", Text = "この画像をコピー", AutoSize = true };
        var close = new Button { Text = "閉じる", AutoSize = true };
        var controls = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(10) };
        controls.Controls.AddRange([previous, page, new Label { Text = $"/ {images.Count} 枚", AutoSize = true, Margin = new Padding(3, 8, 8, 0) }, next, copy, close]);
        var information = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, Padding = new Padding(12) };
        information.Controls.AddRange([fileName, message]);
        Controls.Add(information);
        Controls.Add(controls);
        previous.Click += (_, _) => page.Value = Math.Max(page.Minimum, page.Value - 1);
        next.Click += (_, _) => page.Value = Math.Min(page.Maximum, page.Value + 1);
        page.ValueChanged += (_, _) => UpdatePage();
        copy.Click += (_, _) => CopySelectedImage();
        close.Click += (_, _) => Close();
        UpdatePage();
    }

    private void UpdatePage()
    {
        var index = (int)page.Value - 1;
        previous.Enabled = index > 0;
        next.Enabled = index + 1 < images.Count;
        fileName.Text = Path.GetFileName(images[index]);
        message.Text = "画像をコピーしてExcelなどに貼り付け、次の画像を選んでください。";
    }

    private void CopySelectedImage()
    {
        try
        {
            CopySelectedImageTo(Clipboard.SetImage);
            message.Text = $"{page.Value} / {images.Count} 枚目をコピーしました。貼り付け後、次の画像を選べます。";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ExternalException
            or ArgumentException or OutOfMemoryException)
        {
            MessageBox.Show(this, "画像をコピーできませんでした。\r\n" + ex.Message,
                "画像コピーエラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void CopySelectedImageTo(Action<Image> writeImage)
    {
        using var image = Image.FromFile(images[(int)page.Value - 1]);
        writeImage(image);
    }
}
