using System.Globalization;
using System.Text;

namespace FixedDataBuilder;

public sealed record DisplayWrapOptions(bool Enabled = false, int ThresholdBytes = 20, int LineBytes = 20);

public sealed class DisplayWrapOptionsForm : Form
{
    private readonly CheckBox enabled = new() { Name = "WrapEnabled", Text = "折り返し表示", AutoSize = true, Location = new Point(20, 20) };
    private readonly NumericUpDown threshold = new() { Name = "WrapThresholdBytes", Minimum = 1, Maximum = 1000000, Location = new Point(330, 60), Width = 120 };
    private readonly NumericUpDown width = new() { Name = "WrapLineBytes", Minimum = 1, Maximum = 1000000, Location = new Point(330, 100), Width = 120 };
    public DisplayWrapOptions Options => new(enabled.Checked, (int)threshold.Value, (int)width.Value);
    public DisplayWrapOptionsForm(DisplayWrapOptions current)
    {
        Text = "折り返し表示設定";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = MaximizeBox = false;
        ClientSize = new Size(550, 290);
        enabled.Checked = current.Enabled;
        threshold.Value = Math.Clamp(current.ThresholdBytes, 1, 1000000);
        width.Value = Math.Clamp(current.LineBytes, 1, 1000000);
        var target = new Label { Text = "折り返す対象の長さ（バイト以上）", AutoSize = true, Location = new Point(20, 64) };
        var line = new Label { Text = "1行の長さ（バイト）", AutoSize = true, Location = new Point(20, 104) };
        var help = new Label
        {
            Text = "例：20バイト以上の値を、20バイトごとに折り返す。\r\n日本語・絵文字などの文字の途中では分割しません。\r\n行の高さは自動調整します。元データ・保存内容は変わりません。",
            Location = new Point(20, 151), Size = new Size(510, 65)
        };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(350, 245), Width = 80 };
        var cancel = new Button { Text = "キャンセル", DialogResult = DialogResult.Cancel, Location = new Point(445, 245), Width = 85 };
        void UpdateEnabled() => threshold.Enabled = width.Enabled = target.Enabled = line.Enabled = enabled.Checked;
        enabled.CheckedChanged += (_, _) => UpdateEnabled();
        Controls.AddRange([enabled, target, threshold, line, width, help, ok, cancel]);
        AcceptButton = ok;
        CancelButton = cancel;
        UpdateEnabled();
    }
}

public static class ByteTextWrapper
{
    /// <summary>Wrap without splitting Unicode text elements. A single element wider
    /// than the byte budget stays intact on its own line.</summary>
    public static IReadOnlyList<string> Lines(string value, Encoding encoding, int lineBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(lineBytes);
        var lines = new List<string>();
        foreach (var sourceLine in value.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var current = new StringBuilder();
            var bytes = 0;
            var elements = StringInfo.GetTextElementEnumerator(sourceLine);
            while (elements.MoveNext())
            {
                var element = elements.GetTextElement();
                var size = encoding.GetByteCount(element);
                if (bytes > 0 && bytes + size > lineBytes)
                {
                    lines.Add(current.ToString());
                    current.Clear();
                    bytes = 0;
                }
                current.Append(element);
                bytes += size;
            }
            lines.Add(current.ToString());
        }
        return lines;
    }
}
