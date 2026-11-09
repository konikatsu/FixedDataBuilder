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
    private readonly Panel previewPanel = new() { Name = "CapturePreviewPanel", Dock = DockStyle.Fill, AutoScroll = true,
        BackColor = SystemColors.ControlDark };
    private readonly PictureBox preview = new() { Name = "CapturePreview", SizeMode = PictureBoxSizeMode.AutoSize };
    private readonly NumericUpDown excelColumns = new() { Name = "ExcelImageColumns", Minimum = 1, Maximum = 100, Value = 3, Width = 55 };
    private readonly Label excelTarget = new() { Name = "ExcelCaptureTarget", AutoSize = true, MaximumSize = new Size(900, 0) };
    private readonly Button pasteExcel = new() { Name = "PasteCaptureImagesToExcel", Text = "Excelへ貼り付け", AutoSize = true, Enabled = false };
    private ExcelCaptureTarget? selectedExcelTarget;

    public CaptureImageCopyForm(IReadOnlyList<string> images)
    {
        ArgumentNullException.ThrowIfNull(images);
        if (images.Count == 0) throw new ArgumentException("コピーする画像がありません。", nameof(images));
        this.images = images.ToArray();
        Text = "保存した画像をコピー";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(960, 700);
        MinimumSize = new Size(480, 360);
        page.Maximum = images.Count;
        var copy = new Button { Name = "CaptureCopyImage", Text = "この画像をコピー", AutoSize = true };
        var close = new Button { Text = "閉じる", AutoSize = true };
        var controls = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(10) };
        controls.Controls.AddRange([previous, page, new Label { Text = $"/ {images.Count} 枚", AutoSize = true, Margin = new Padding(3, 8, 8, 0) }, next, copy, close]);
        var excelControls = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(10, 0, 10, 0) };
        var selectExcel = new Button { Name = "SelectExcelCaptureTarget", Text = "Excelの選択位置を取得", AutoSize = true };
        excelControls.Controls.AddRange([selectExcel, new Label { Text = "横に並べる枚数", AutoSize = true, Margin = new Padding(8, 8, 3, 0) }, excelColumns, pasteExcel]);
        var information = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 95, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, Padding = new Padding(12, 4, 12, 4) };
        excelTarget.Text = "Excelのセルを選んで「Excelの選択位置を取得」を押してください。";
        information.Controls.AddRange([fileName, message, excelTarget]);
        previewPanel.Controls.Add(preview);
        Controls.Add(previewPanel);
        Controls.Add(information);
        Controls.Add(excelControls);
        Controls.Add(controls);
        previous.Click += (_, _) => page.Value = Math.Max(page.Minimum, page.Value - 1);
        next.Click += (_, _) => page.Value = Math.Min(page.Maximum, page.Value + 1);
        page.ValueChanged += (_, _) => UpdatePage();
        copy.Click += (_, _) => CopySelectedImage();
        selectExcel.Click += (_, _) => SelectExcelTarget();
        pasteExcel.Click += (_, _) => PasteImagesToExcel();
        close.Click += (_, _) => Close();
        UpdatePage();
    }

    private void SelectExcelTarget()
    {
        try
        {
            selectedExcelTarget = ExcelCapturePlacement.CurrentTarget();
            excelTarget.Text = $"貼り付け先: {selectedExcelTarget.Workbook} / {selectedExcelTarget.Sheet} / {selectedExcelTarget.Address}";
            pasteExcel.Enabled = true;
        }
        catch (Exception ex)
        {
            selectedExcelTarget = null;
            pasteExcel.Enabled = false;
            excelTarget.Text = ex.Message;
        }
    }

    private void PasteImagesToExcel()
    {
        if (selectedExcelTarget is null) return;
        try
        {
            UseWaitCursor = true;
            var count = ExcelCapturePlacement.Insert(selectedExcelTarget, images, (int)excelColumns.Value);
            message.Text = $"{count}枚をExcelへ貼り付けました。ブックを確認して保存してください。";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Excelへの貼り付け", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { UseWaitCursor = false; }
    }

    private void UpdatePage()
    {
        var index = (int)page.Value - 1;
        previous.Enabled = index > 0;
        next.Enabled = index + 1 < images.Count;
        fileName.Text = Path.GetFileName(images[index]);
        var previousImage = preview.Image;
        preview.Image = null;
        previousImage?.Dispose();
        try
        {
            using var stream = new FileStream(images[index], FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var source = Image.FromStream(stream);
            preview.Image = new Bitmap(source);
            previewPanel.AutoScrollPosition = Point.Empty;
            message.Text = "プレビューはスクロールできます。コピー後、Excelなどに貼り付けてください。";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or OutOfMemoryException)
        {
            message.Text = "プレビューを表示できません: " + ex.Message;
        }
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

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            var image = preview.Image;
            preview.Image = null;
            image?.Dispose();
        }
        base.Dispose(disposing);
    }
}
