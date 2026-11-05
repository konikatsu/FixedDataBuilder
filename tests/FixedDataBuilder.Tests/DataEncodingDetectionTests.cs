using System.Reflection;
using System.Text;
using FixedDataBuilder;

internal static partial class Program
{
    private static void RunDataEncodingDetectionTests(string repo)
    {
        var csv = Path.Combine(temporaryDirectory, "detect-data.csv");
        var fixedPath = Path.Combine(temporaryDirectory, "detect-data.dat");
        var definition = Path.Combine(temporaryDirectory, "detect-definition.csv");
        var ms932 = JapaneseEncodings.ForCsv(CsvTextEncoding.ShiftJis);
        var fields = new[] { new FieldDefinition("文字", FieldDataType.Text, 40, "X(40)") };
        DataLoadSettingsStore Store(string suffix) => new(Path.Combine(temporaryDirectory, "detect-settings-" + suffix + ".json"));

        Test("Input CSV detection accepts UTF8 BOM, BOM-less Japanese and MS932 extensions without changing values", () =>
        {
            foreach (var codec in new Encoding[] { new UTF8Encoding(true), new UTF8Encoding(false), ms932 })
            {
                File.WriteAllText(csv, "文字\r\n\" 前①髙,後　 \"\r\n", codec);
                var result = DataEncodingDetector.Csv(csv);
                Equal(codec is UTF8Encoding ? CsvTextEncoding.Utf8 : CsvTextEncoding.ShiftJis, result.Encoding!.Value);
                Equal(" 前①髙,後　 ", CsvDataReader.Read(csv, fields, result.Encoding.Value, true)[0][0]);
            }
            var comparison = DefinitionCsvReader.Read(Path.Combine(repo, "samples", "encoding-comparison-definition.csv"));
            foreach (var name in new[] { "encoding-utf8.csv", "encoding-ms932.csv" })
            {
                var path = Path.Combine(repo, "samples", name);
                var result = DataEncodingDetector.Csv(path);
                Equal(name.Contains("utf8") ? CsvTextEncoding.Utf8 : CsvTextEncoding.ShiftJis, result.Encoding!.Value);
                Equal("東京", CsvDataReader.Read(path, comparison, result.Encoding.Value)[0][1]);
            }
        });
        Test("ASCII-only CSV chooses UTF8 and ambiguous bytes remain manually overridable", () =>
        {
            File.WriteAllBytes(csv, Encoding.ASCII.GetBytes(" 123 \r\n"));
            Equal(CsvTextEncoding.Utf8, DataEncodingDetector.Csv(csv).Encoding!.Value);
            Equal(true, DataEncodingDetector.Csv(csv).Description.Contains("共通"));
            File.WriteAllBytes(csv, [0xc2, 0xa5, 0x0d, 0x0a]);
            Equal(CsvTextEncoding.Utf8, DataEncodingDetector.Csv(csv).Encoding!.Value);
            Equal(true, DataEncodingDetector.Csv(csv).Description.Contains("MS932でも"));
            Equal("¥", CsvDataReader.Read(csv, fields, CsvTextEncoding.Utf8)[0][0]);
            Equal("ﾂ･", CsvDataReader.Read(csv, fields, CsvTextEncoding.ShiftJis)[0][0]);
        });
        Test("CSV detection checks bytes beyond the preview and rejects malformed data and invalid BOM payloads", () =>
        {
            File.WriteAllBytes(csv, Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("ASCII\r\n", 10000)))
                .Concat(ms932.GetBytes("東京\r\n")).ToArray());
            Equal(CsvTextEncoding.ShiftJis, DataEncodingDetector.Csv(csv).Encoding!.Value);
            File.WriteAllBytes(csv, [0x81]);
            Throws<InvalidDataException>(() => DataEncodingDetector.Csv(csv));
            File.WriteAllBytes(csv, [0xef, 0xbb, 0xbf, 0x81]);
            Throws<DecoderFallbackException>(() => DataEncodingDetector.Csv(csv));
        });
        Test("Input CSV Unicode BOMs outside the supported formats cannot silently become MS932", () =>
        {
            foreach (var codec in new Encoding[] { new UnicodeEncoding(false, true), new UnicodeEncoding(true, true),
                new UTF32Encoding(false, true), new UTF32Encoding(true, true) })
            {
                File.WriteAllText(csv, "東京\r\n", codec);
                Throws<InvalidDataException>(() => DataEncodingDetector.Csv(csv));
            }
        });
        Test("Fixed detection excludes packed and national bytes for both separator modes", () =>
        {
            var fixedFields = new[] { new FieldDefinition("文字", FieldDataType.Text, 6),
                new FieldDefinition("金額", FieldDataType.PackedSigned, 5), new FieldDefinition("名称", FieldDataType.FullWidthText, 2) };
            foreach (var separator in new[] { RecordSeparatorMode.None, RecordSeparatorMode.CrLfOrLf })
            {
                FixedLengthDataWriter.Write(fixedPath, fixedFields, [["東京", "99999", "東京"], ["大阪", "-99999", "大阪"]],
                    separator, DataEncodingProfile.Utf8WithNationalText, NationalTextEncoding.Utf16);
                var before = File.ReadAllBytes(fixedPath);
                var result = DataEncodingDetector.Fixed(fixedPath, fixedFields, separator);
                Equal(CsvTextEncoding.Utf8, result.Encoding!.Value);
                Equal("東京", FixedLengthDataReader.Read(fixedPath, fixedFields, separator,
                    result.FixedEncoding(DataEncodingProfile.ShiftJis), NationalTextEncoding.Utf16)[0][0]);
                Equal(true, before.SequenceEqual(File.ReadAllBytes(fixedPath)));
            }
        });
        Test("Fixed MS932 detection respects field boundaries and examines later records", () =>
        {
            var narrow = new[] { new FieldDefinition("前", FieldDataType.Text, 1), new FieldDefinition("後", FieldDataType.Text, 1) };
            File.WriteAllBytes(fixedPath, [0xc2, 0xa5]);
            Equal(CsvTextEncoding.ShiftJis, DataEncodingDetector.Fixed(fixedPath, narrow, RecordSeparatorMode.None).Encoding!.Value);
            var fixedFields = new[] { new FieldDefinition("文字", FieldDataType.Text, 4) };
            FixedLengthDataWriter.Write(fixedPath, fixedFields, [["1234"], ["東京"]], RecordSeparatorMode.None);
            Equal(CsvTextEncoding.ShiftJis, DataEncodingDetector.Fixed(fixedPath, fixedFields, RecordSeparatorMode.None).Encoding!.Value);
        });
        Test("Fixed ASCII and binary-only definitions retain the chosen default and ignore text REDEFINES", () =>
        {
            File.WriteAllBytes(fixedPath, Encoding.ASCII.GetBytes("1234"));
            var result = DataEncodingDetector.Fixed(fixedPath, [new("文字", FieldDataType.Text, 4)], RecordSeparatorMode.None);
            Equal(null, result.Encoding);
            Equal(DataEncodingProfile.StrictShiftJis, result.FixedEncoding(DataEncodingProfile.StrictShiftJis));
            var overlay = new[] { new FieldDefinition("金額", FieldDataType.PackedSigned, 3),
                new FieldDefinition("別名", FieldDataType.Text, 2, RedefinesName: "金額") };
            File.WriteAllBytes(fixedPath, [0x99, 0x9c]);
            result = DataEncodingDetector.Fixed(fixedPath, overlay, RecordSeparatorMode.None);
            Equal(null, result.Encoding);
            Equal(true, result.Description.Contains("対象のX項目なし"));
            Throws<InvalidDataException>(() => DataEncodingDetector.Fixed(fixedPath, [new("長い", FieldDataType.Text, 3)], RecordSeparatorMode.None));
        });
        Test("CSV dialog starts with a detected concrete encoding and allows manual SJIS override", () =>
        {
            DefinitionCsvWriter.Write(definition, [new("文字", "X(40)")]);
            File.WriteAllText(csv, "①髙\r\n", ms932);
            using var form = new MainForm(["--definition", definition], Store("initial"));
            WithDialog("データ読込条件", DialogResult.Cancel, () => Invoke(form, "LoadDataFromPath", csv, "読込"), dialog =>
            {
                var choice = Descendants(dialog).OfType<ComboBox>().Single(c => c.Name == "CsvEncoding");
                Equal((int)CsvTextEncoding.ShiftJis, choice.SelectedIndex);
                Equal(true, choice.Enabled);
                Equal(true, Descendants(dialog).OfType<Label>().Single(c => c.Name == "DataEncodingDetection").Text.Contains("MS932候補"));
                var ok = (Button)dialog.AcceptButton!;
                choice.SelectedIndex = (int)CsvTextEncoding.StrictShiftJis;
                Equal(false, ok.Enabled);
                choice.SelectedIndex = (int)CsvTextEncoding.ShiftJis;
                Equal(true, ok.Enabled);
            });
        });
        Test("Previously chosen encoding remains the initial value until the user requests redetection", () =>
        {
            DefinitionCsvWriter.Write(definition, [new("文字", "X(40)")]);
            File.WriteAllText(csv, "東京\r\n", ms932);
            var store = Store("remembered");
            var settings = new DataLoadSettings(DataFileFormat.Csv, CsvTextEncoding.StrictShiftJis, RecordSeparatorMode.CrLfOrLf, NationalTextEncoding.ShiftJis);
            store.Remember(definition, csv, settings);
            using var form = new MainForm(["--definition", definition], store);
            WithDialog("データ読込条件", DialogResult.Cancel, () => Invoke(form, "LoadDataFromPath", csv, "読込"), dialog =>
            {
                var choice = Descendants(dialog).OfType<ComboBox>().Single(c => c.Name == "CsvEncoding");
                Equal((int)CsvTextEncoding.StrictShiftJis, choice.SelectedIndex);
                Descendants(dialog).OfType<Button>().Single(c => c.Name == "DetectDataEncoding").PerformClick();
                Equal((int)CsvTextEncoding.ShiftJis, choice.SelectedIndex);
            });
            Equal(settings, store.Find(definition, csv));
        });
        Test("Changing a .dat input to CSV populates its encoding without forcing the initial fixed layout", () =>
        {
            DefinitionCsvWriter.Write(definition, [new("文字", "X(40)")]);
            File.WriteAllText(fixedPath, "東京\r\n", ms932);
            using var form = new MainForm(["--definition", definition], Store("format"));
            WithDialog("データ読込条件", DialogResult.Cancel, () => Invoke(form, "LoadDataFromPath", fixedPath, "読込"), dialog =>
            {
                var format = Descendants(dialog).OfType<ComboBox>().Single(c => c.Name == "DataFormat");
                Equal(0, format.SelectedIndex);
                format.SelectedIndex = 1;
                Equal((int)CsvTextEncoding.ShiftJis, Descendants(dialog).OfType<ComboBox>().Single(c => c.Name == "CsvEncoding").SelectedIndex);
                Equal(true, ((Button)dialog.AcceptButton!).Enabled);
            });
        });
        Test("Wrong national width leaves the dialog usable and redetection works after correcting the conditions", () =>
        {
            DefinitionCsvWriter.Write(definition, [new("文字", "X(6)"), new("名称", "N(2)")]);
            var wideFields = new[] { new FieldDefinition("文字", FieldDataType.Text, 6),
                new FieldDefinition("名称", FieldDataType.FullWidthText, 2, NationalByteWidth: 4) };
            FixedLengthDataWriter.Write(fixedPath, wideFields, [["東京", "東京"]], RecordSeparatorMode.CrLfOrLf,
                DataEncodingProfile.Utf8WithNationalText, NationalTextEncoding.Utf32);
            using var form = new MainForm(["--definition", definition], Store("national"));
            WithDialog("データ読込条件", DialogResult.Cancel, () => Invoke(form, "LoadDataFromPath", fixedPath, "読込"), dialog =>
            {
                Descendants(dialog).OfType<RadioButton>().Single(c => c.Text == "UTF-32LE").Checked = true;
                Descendants(dialog).OfType<Button>().Single(c => c.Name == "DetectDataEncoding").PerformClick();
                Equal((int)DataEncodingProfile.Utf8WithNationalText, Descendants(dialog).OfType<ComboBox>().Single(c => c.Name == "TextEncoding").SelectedIndex);
                Equal(true, ((Button)dialog.AcceptButton!).Enabled);
            });
        });
        Test("Fixed dialog detects X bytes and keeps national encoding and explicit reload settings", () =>
        {
            DefinitionCsvWriter.Write(definition, [new("文字", "X(6)")]);
            File.WriteAllText(fixedPath, "東京\r\n", new UTF8Encoding(false));
            using var form = new MainForm(["--definition", definition], Store("fixed"));
            WithDialog("データ読込条件", DialogResult.OK, () => Invoke(form, "LoadDataFromPath", fixedPath, "読込"), dialog =>
            {
                var choice = Descendants(dialog).OfType<ComboBox>().Single(c => c.Name == "TextEncoding");
                Equal((int)DataEncodingProfile.Utf8WithNationalText, choice.SelectedIndex);
                Equal(true, choice.Enabled);
            });
            Equal("東京", Get<List<List<string>>>(form, "records")[0][0]);
            Equal(NationalTextEncoding.ShiftJis, Get<NationalTextEncoding>(form, "currentNationalTextEncoding"));
            File.WriteAllText(fixedPath, "大阪\r\n", new UTF8Encoding(false));
            Invoke(form, "ReloadCurrentFilesCore");
            Equal("大阪", Get<List<List<string>>>(form, "records")[0][0]);
            Equal(DataEncodingProfile.Utf8WithNationalText, Get<DataEncodingProfile>(form, "currentDataEncodingProfile"));
        });
        Test("CLI auto is opt-in and reload retains the resolved encoding without redetection", () =>
        {
            DefinitionCsvWriter.Write(definition, [new("文字", "X(40)")]);
            File.WriteAllText(csv, "東京\r\n", ms932);
            using var form = new MainForm(["--definition", definition, "--data", csv, "--csv-encoding", "auto"], Store("cli"));
            Equal(CsvTextEncoding.ShiftJis, Get<CsvTextEncoding>(form, "currentCsvEncoding"));
            File.WriteAllBytes(csv, [0xc2, 0xa5, 0x0d, 0x0a]);
            Invoke(form, "ReloadCurrentFilesCore");
            Equal("ﾂ･", Get<List<List<string>>>(form, "records")[0][0]);
            Equal(CsvTextEncoding.ShiftJis, Get<CsvTextEncoding>(form, "currentCsvEncoding"));
        });
        Test("Failed automatic load leaves the previous records, encoding and file paths intact", () =>
        {
            DefinitionCsvWriter.Write(definition, [new("文字", "X(40)")]);
            File.WriteAllText(csv, "東京\r\n", new UTF8Encoding(false));
            using var form = new MainForm(["--definition", definition, "--data", csv, "--csv-encoding", "utf8"], Store("failure"));
            var bad = Path.Combine(temporaryDirectory, "detect-bad.csv");
            File.WriteAllBytes(bad, [0x81]);
            Throws<TargetInvocationException>(() => Invoke(form, "LoadAutomaticData", bad, true, RecordSeparatorMode.CrLfOrLf, NationalTextEncoding.ShiftJis, false));
            Equal("東京", Get<List<List<string>>>(form, "records")[0][0]);
            Equal(csv, Get<TextBox>(form, "dataPathTextBox").Text);
            Equal(CsvTextEncoding.Utf8, Get<CsvTextEncoding>(form, "currentCsvEncoding"));
        });
    }
}
