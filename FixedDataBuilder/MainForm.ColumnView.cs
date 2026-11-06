namespace FixedDataBuilder;

public sealed partial class MainForm
{
    private readonly Button columnViewButton = new() { Name = "OpenColumnView", Text = "段組みビュー...", AutoSize = true };
    private readonly ToolStripMenuItem columnViewMenu = new("段組みビュー...");

    private void OpenColumnView()
    {
        using var form = new ColumnTextViewForm();
        var path = GetCurrentDataPath();
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            try { form.LoadFile(path); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or InvalidDataException)
            {
                MessageBox.Show(this, "文字ファイルとして開けませんでした。段組みビューでファイルと文字コードを選んでください。\r\n" + ex.Message,
                    "段組みビュー", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        form.ShowDialog(this);
    }
}
