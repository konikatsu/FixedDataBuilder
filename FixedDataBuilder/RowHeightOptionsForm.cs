namespace FixedDataBuilder;

public sealed class RowHeightOptionsForm : Form
{
    private readonly NumericUpDown height = new() { Name = "SelectedRowHeight", Minimum = 2, Maximum = 65536, Width = 120, Location = new Point(235, 58) };
    public int RowHeight => (int)height.Value;
    public RowHeightOptionsForm(int rowCount, int currentHeight)
    {
        Text = "選択行の高さ";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ClientSize = new Size(420, 200);
        height.Value = Math.Clamp(currentHeight, 2, 65536);
        Controls.Add(new Label { Text = $"選択した {rowCount} 行の高さをまとめて変更します。", AutoSize = true, Location = new Point(18, 20) });
        Controls.Add(new Label { Text = "高さ（ピクセル）", AutoSize = true, Location = new Point(18, 62) });
        Controls.Add(height);
        Controls.Add(new Label { Text = "元データは変わりません。\r\n「選択行の高さを自動調整」で自動の高さに戻せます。", Location = new Point(18, 98), Size = new Size(384, 42) });
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(232, 157), Width = 80 };
        var cancel = new Button { Text = "キャンセル", DialogResult = DialogResult.Cancel, Location = new Point(324, 157), Width = 80 };
        Controls.AddRange([ok, cancel]);
        AcceptButton = ok;
        CancelButton = cancel;
    }
}
