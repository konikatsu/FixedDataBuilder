namespace FixedDataBuilder;

public sealed record DataSaveOptions(DataFileFormat Format, CsvTextEncoding CsvEncoding,
    RecordSeparatorMode SeparatorMode, NationalTextEncoding NationalEncoding, DataEncodingProfile? TextEncoding = null);

public sealed class DataSaveOptionsForm : Form
{
    private readonly ComboBox format = new() { Name = "DataFormat", DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox csvEncoding = new() { Name = "CsvEncoding", DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox separator = new() { Name = "RecordSeparator", DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox national = new() { Name = "NationalEncoding", DropDownStyle = ComboBoxStyle.DropDownList };

    private readonly ComboBox textEncoding = new() { Name = "TextEncoding", DropDownStyle = ComboBoxStyle.DropDownList };

    public DataSaveOptions Options => new(
        format.SelectedIndex == 1 ? DataFileFormat.Csv : DataFileFormat.FixedLength,
        (CsvTextEncoding)csvEncoding.SelectedIndex,
        separator.SelectedIndex == 1 ? RecordSeparatorMode.None : RecordSeparatorMode.CrLfOrLf,
        (NationalTextEncoding)national.SelectedIndex, (DataEncodingProfile)textEncoding.SelectedIndex);

    public DataSaveOptionsForm(DataSaveOptions current)
    {
        Text = "データ保存条件";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = MaximizeBox = false;
        ClientSize = new Size(550, 410);
        format.Items.AddRange(["固定長", "可変長 CSV（ヘッダーなし）"]);
        csvEncoding.Items.AddRange(JapaneseEncodings.CsvNames);
        textEncoding.Items.AddRange(JapaneseEncodings.TextNames);
        separator.Items.AddRange(["改行あり (CRLF)", "改行なし"]);
        national.Items.AddRange(["MS932 (CP932)", "UTF-16LE", "UTF-32LE", "SJIS (JIS X 0208)"]);
        var labels = new List<Label>();
        var controls = new[] { format, csvEncoding, separator, national, textEncoding };
        var names = new[] { "出力形式", "CSV文字コード", "固定長の改行", "固定長の型N文字コード", "固定長のX文字コード" };
        for (var index = 0; index < controls.Length; index++)
        {
            var label = new Label { Text = names[index], AutoSize = true, Location = new Point(16, 24 + index * 38) };
            labels.Add(label);
            controls[index].SetBounds(210, 20 + index * 38, 320, 26);
            Controls.Add(label);
            Controls.Add(controls[index]);
        }
        format.SelectedIndex = current.Format == DataFileFormat.Csv ? 1 : 0;
        csvEncoding.SelectedIndex = (int)current.CsvEncoding;
        textEncoding.SelectedIndex = (int)(current.TextEncoding ?? DataEncodingProfile.ShiftJis);
        separator.SelectedIndex = current.SeparatorMode == RecordSeparatorMode.None ? 1 : 0;
        national.SelectedIndex = (int)current.NationalEncoding;
        var encodingHelp = new Label
        {
            Name = "EncodingHelp",
            Text = "半角の英字・数字はUTF-8・SJIS・MS932で同じです。\r\nSJISは拡張文字なし。①・髙などを含む場合はMS932。",
            Location = new Point(16, 222), Size = new Size(518, 44)
        };
        var note = new Label
        {
            Text = "CSV: 空欄・空白・先頭ゼロを保持。桁合わせなし。\r\n全列を引用符で囲み、CRLFで出力します。",
            Location = new Point(16, 275), Size = new Size(518, 50)
        };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(356, 365), Width = 80 };
        var cancel = new Button { Text = "キャンセル", DialogResult = DialogResult.Cancel, Location = new Point(450, 365), Width = 80 };
        Controls.AddRange([encodingHelp, note, ok, cancel]);
        AcceptButton = ok;
        CancelButton = cancel;
        void UpdateOptions()
        {
            var isCsv = format.SelectedIndex == 1;
            csvEncoding.Enabled = labels[1].Enabled = isCsv;
            separator.Enabled = national.Enabled = textEncoding.Enabled = labels[2].Enabled = labels[3].Enabled = labels[4].Enabled = !isCsv;
            note.Visible = isCsv;
        }
        format.SelectedIndexChanged += (_, _) => UpdateOptions();
        UpdateOptions();
    }
}
