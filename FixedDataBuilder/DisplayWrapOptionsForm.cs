using System.Globalization;
using System.Text;

namespace FixedDataBuilder;

public sealed record DisplayWrapOptions(bool Enabled = false, int ThresholdBytes = 20, int LineBytes = 20,
    bool LimitDisplay = false, int MaxCharacters = 100);

public sealed class DisplayWrapOptionsForm : Form
{
    private readonly CheckBox enabled = new() { Name = "WrapEnabled", Text = "折り返し表示", AutoSize = true, Location = new Point(20, 20) };
    private readonly NumericUpDown threshold = new() { Name = "WrapThresholdBytes", Minimum = 1, Maximum = 1000000, Location = new Point(330, 60), Width = 120 };
    private readonly NumericUpDown width = new() { Name = "WrapLineBytes", Minimum = 1, Maximum = 1000000, Location = new Point(330, 100), Width = 120 };
    private readonly CheckBox limit = new() { Name = "LimitDisplay", Text = "表示する文字数に上限を設定", AutoSize = true, Location = new Point(20, 143) };
    private readonly NumericUpDown maxCharacters = new() { Name = "MaxDisplayCharacters", Minimum = 1, Maximum = 1000000, Width = 120, Location = new Point(330, 180) };
    public DisplayWrapOptions Options => new(enabled.Checked, (int)threshold.Value, (int)width.Value, limit.Checked, (int)maxCharacters.Value);
    public DisplayWrapOptionsForm(DisplayWrapOptions current)
    {
        Text = "折り返し表示設定";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = MaximizeBox = false;
        ClientSize = new Size(550, 380);
        enabled.Checked = current.Enabled;
        threshold.Value = Math.Clamp(current.ThresholdBytes, 1, 1000000);
        width.Value = Math.Clamp(current.LineBytes, 1, 1000000);
        limit.Checked = current.LimitDisplay;
        maxCharacters.Value = Math.Clamp(current.MaxCharacters, 1, 1000000);
        var maxLabel = new Label { Text = "表示する最大文字数", AutoSize = true, Location = new Point(20, 184) };
        var target = new Label { Text = "折り返す対象の長さ（バイト以上）", AutoSize = true, Location = new Point(20, 64) };
        var line = new Label { Text = "1行の長さ（バイト）", AutoSize = true, Location = new Point(20, 104) };
        var help = new Label
        {
            Text = "例：20バイト以上を20バイトごとに折り返す。\r\n文字数の上限を超えた部分は画面だけ省略し、末尾に…を表示。\r\n日本語・絵文字などの文字の途中では分割しません。\r\n行高は自動調整します。保存・Excel出力・HEXは元の値です。",
            Location = new Point(20, 228), Size = new Size(510, 76)
        };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(350, 333), Width = 80 };
        var cancel = new Button { Text = "キャンセル", DialogResult = DialogResult.Cancel, Location = new Point(445, 333), Width = 85 };
        void UpdateEnabled()
        {
            threshold.Enabled = width.Enabled = target.Enabled = line.Enabled = limit.Enabled = enabled.Checked;
            maxCharacters.Enabled = maxLabel.Enabled = enabled.Checked && limit.Checked;
        }
        enabled.CheckedChanged += (_, _) => UpdateEnabled();
        limit.CheckedChanged += (_, _) => UpdateEnabled();
        Controls.AddRange([enabled, target, threshold, line, width, limit, maxLabel, maxCharacters, help, ok, cancel]);
        AcceptButton = ok;
        CancelButton = cancel;
        UpdateEnabled();
    }
}

public static class DisplayTextLimiter
{
    public static string Limit(string value, int maxCharacters, out bool truncated)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCharacters);
        var starts = StringInfo.ParseCombiningCharacters(value);
        truncated = starts.Length > maxCharacters;
        return truncated ? value[..starts[maxCharacters]] : value;
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
