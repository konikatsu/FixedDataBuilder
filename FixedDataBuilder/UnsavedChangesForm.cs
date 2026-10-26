namespace FixedDataBuilder;

public sealed class UnsavedChangesForm : Form
{
    public UnsavedChangesForm(string action)
    {
        Text = "未保存の変更";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = MaximizeBox = false;
        ClientSize = new Size(510, 150);
        var message = new Label
        {
            Text = $"未保存の変更があります。\r\n{action}する前に、編集内容を保存しますか？",
            Location = new Point(16, 18), Size = new Size(478, 60)
        };
        var save = new Button { Text = "保存して続ける", DialogResult = DialogResult.Yes, Location = new Point(16, 100), Size = new Size(150, 30) };
        var discard = new Button { Text = "保存せずに続ける", DialogResult = DialogResult.No, Location = new Point(174, 100), Size = new Size(160, 30) };
        var cancel = new Button { Text = "キャンセル", DialogResult = DialogResult.Cancel, Location = new Point(342, 100), Size = new Size(150, 30) };
        Controls.AddRange([message, save, discard, cancel]);
        AcceptButton = cancel;
        CancelButton = cancel;
        ActiveControl = cancel;
    }
}
