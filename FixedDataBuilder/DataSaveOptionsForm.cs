namespace FixedDataBuilder;

public sealed record DataSaveOptions(DataFileFormat Format, CsvTextEncoding CsvEncoding,
    RecordSeparatorMode SeparatorMode, NationalTextEncoding NationalEncoding);

public sealed class DataSaveOptionsForm : Form
{
    private readonly ComboBox format = new() { Name = "DataFormat", DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox csvEncoding = new() { Name = "CsvEncoding", DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox separator = new() { Name = "RecordSeparator", DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox national = new() { Name = "NationalEncoding", DropDownStyle = ComboBoxStyle.DropDownList };

    public DataSaveOptions Options => new(
        format.SelectedIndex == 1 ? DataFileFormat.Csv : DataFileFormat.FixedLength,
        csvEncoding.SelectedIndex == 1 ? CsvTextEncoding.ShiftJis : CsvTextEncoding.Utf8,
        separator.SelectedIndex == 1 ? RecordSeparatorMode.None : RecordSeparatorMode.CrLfOrLf,
        national.SelectedIndex switch { 1 => NationalTextEncoding.Utf16, 2 => NationalTextEncoding.Utf32, _ => NationalTextEncoding.ShiftJis });

    public DataSaveOptionsForm(DataSaveOptions current)
    {
        Text = "データ保存条件";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = MaximizeBox = false;
        ClientSize = new Size(510, 300);
        format.Items.AddRange(["固定長", "可変長 CSV（ヘッダーなし）"]);
        csvEncoding.Items.AddRange(["UTF-8", "Shift_JIS"]);
        separator.Items.AddRange(["改行あり (CRLF)", "改行なし"]);
        national.Items.AddRange(["Shift_JIS", "UTF-16LE", "UTF-32LE"]);
        var labels = new List<Label>();
        var controls = new[] { format, csvEncoding, separator, national };
        var names = new[] { "出力形式", "CSV文字コード", "固定長の改行", "固定長の型N文字コード" };
        for (var index = 0; index < controls.Length; index++)
        {
            var label = new Label { Text = names[index], AutoSize = true, Location = new Point(16, 24 + index * 38) };
            labels.Add(label);
            controls[index].SetBounds(190, 20 + index * 38, 300, 26);
            Controls.Add(label);
            Controls.Add(controls[index]);
        }
        format.SelectedIndex = current.Format == DataFileFormat.Csv ? 1 : 0;
        csvEncoding.SelectedIndex = current.CsvEncoding == CsvTextEncoding.ShiftJis ? 1 : 0;
        separator.SelectedIndex = current.SeparatorMode == RecordSeparatorMode.None ? 1 : 0;
        national.SelectedIndex = current.NationalEncoding switch { NationalTextEncoding.Utf16 => 1, NationalTextEncoding.Utf32 => 2, _ => 0 };
        var note = new Label
        {
            Text = "CSV: 空欄・空白・先頭ゼロを保持。桁合わせなし。\r\n全列を引用符で囲み、CRLFで出力します。",
            Location = new Point(16, 182), Size = new Size(478, 50)
        };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(316, 254), Width = 80 };
        var cancel = new Button { Text = "キャンセル", DialogResult = DialogResult.Cancel, Location = new Point(410, 254), Width = 80 };
        Controls.AddRange([note, ok, cancel]);
        AcceptButton = ok;
        CancelButton = cancel;
        void UpdateOptions()
        {
            var isCsv = format.SelectedIndex == 1;
            csvEncoding.Enabled = labels[1].Enabled = isCsv;
            separator.Enabled = national.Enabled = labels[2].Enabled = labels[3].Enabled = !isCsv;
            note.Visible = isCsv;
        }
        format.SelectedIndexChanged += (_, _) => UpdateOptions();
        UpdateOptions();
    }
}
