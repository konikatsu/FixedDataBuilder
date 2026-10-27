using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using FixedDataBuilder;

internal static partial class Program
{
    private static void RunJapaneseEncodingTests(string repo)
    {
        var sjis = JapaneseEncodings.ForCsv(CsvTextEncoding.StrictShiftJis);
        var ms932 = JapaneseEncodings.ForCsv(CsvTextEncoding.ShiftJis);
        var path = Path.Combine(temporaryDirectory, "encoding.csv");
        var fields = new[] { new FieldDefinition("文字", FieldDataType.Text, 40) };
        Test("Strict SJIS entire decoding repertoire matches independent CPython shift_jis oracle", () =>
        {
            // Oracle generated independently using CPython's shift_jis codec, not this implementation.
            using var all = new MemoryStream();
            var count = 0;
            for (var code = 0; code <= 0xffff; code++)
            {
                var lead = code >> 8;
                if (code > 0xff && !(lead is >= 0x81 and <= 0x9f or >= 0xe0 and <= 0xfc)) continue;
                var bytes = code <= 0xff ? new[] { (byte)code } : new[] { (byte)lead, (byte)code };
                string value;
                try { value = sjis.GetString(bytes); }
                catch (DecoderFallbackException) { continue; }
                Equal(1, value.Length);
                Equal(true, sjis.GetBytes(value).SequenceEqual(bytes));
                all.Write([(byte)(code >> 8), (byte)code, (byte)(value[0] >> 8), (byte)value[0]]);
                count++;
            }
            Equal(7070, count); // ASCII/control 128 + kana 63 + JIS X 0208 6879
            Equal("0B7AC5B05848C7DCF919E70E4FD1C64C8516EC9CDDA6C4E41481256CB4B19253", Convert.ToHexString(SHA256.HashData(all.ToArray())));
        });
        Test("SJIS and MS932 distinguish wave dash, parallel sign, minus and currency symbols", () =>
        {
            var codes = new[] { 0x8160, 0x8161, 0x817c, 0x8191, 0x8192, 0x81ca };
            var jis = "\u301c\u2016\u2212\u00a2\u00a3\u00ac";
            var windows = "\uff5e\u2225\uff0d\uffe0\uffe1\uffe2";
            for (var index = 0; index < codes.Length; index++)
            {
                var bytes = new[] { (byte)(codes[index] >> 8), (byte)codes[index] };
                Equal(jis[index].ToString(), sjis.GetString(bytes));
                Equal(windows[index].ToString(), ms932.GetString(bytes));
                Equal(true, sjis.GetBytes(jis[index].ToString()).SequenceEqual(bytes));
                Throws<EncoderFallbackException>(() => sjis.GetBytes(windows[index].ToString()));
            }
            Equal("\\~", sjis.GetString([0x5c, 0x7e]));
        });
        Test("SJIS rejects NEC/IBM extensions, gaiji, undefined bytes and malformed sequences", () =>
        {
            foreach (var value in new[] { "①", "髙", "﨑", "\ue000", "😀", "～", "¥", "‾" })
                Throws<EncoderFallbackException>(() => sjis.GetBytes(value));
            foreach (var bytes in new byte[][] { [0x87,0x40], [0xfb,0xfc], [0xf0,0x40], [0x80], [0xa0], [0xfd], [0x81], [0x81,0x7f], [0x81,0xad] })
                Throws<DecoderFallbackException>(() => sjis.GetString(bytes));
            foreach (var value in new[] { "①", "髙", "﨑", "\ue000" }) Equal(value, ms932.GetString(ms932.GetBytes(value)));
        });
        Test("SJIS streaming decoder preserves split double-byte characters and detects truncated EOF", () =>
        {
            var decoder = sjis.GetDecoder();
            var output = new char[1];
            Equal(0, decoder.GetChars([0x93], 0, 1, output, 0, false));
            Equal(1, decoder.GetCharCount([0x8c], 0, 1, false)); // must not consume pending lead
            Equal(1, decoder.GetChars([0x8c], 0, 1, output, 0, false));
            Equal('東', output[0]);
            decoder.GetChars([0x81], 0, 1, output, 0, false);
            Throws<DecoderFallbackException>(() => decoder.GetChars([], 0, 0, output, 0, true));
            decoder.Reset();
            Equal(1, decoder.GetChars([0x41], 0, 1, output, 0, true));
        });
        Test("SJIS CSV round trip preserves empty fields, spaces, leading zeros, quotes and embedded newline", () =>
        {
            var csvFields = Enumerable.Range(0, 4).Select(i => new FieldDefinition($"項目{i}", FieldDataType.Text, 2000)).ToArray();
            List<List<string>> records = [["001", " 東京ｶﾅ ", "", "\"波〜\"\r\n次行"]];
            CsvDataWriter.Write(path, csvFields, records, CsvTextEncoding.StrictShiftJis);
            Equal(true, records[0].SequenceEqual(CsvDataReader.Read(path, csvFields, CsvTextEncoding.StrictShiftJis)[0]));
            // Double-byte character straddles StreamReader's default 1024-byte buffer boundary.
            File.WriteAllBytes(path, sjis.GetBytes(new string('A', 1023) + "東"));
            Equal(new string('A', 1023) + "東", CsvDataReader.Read(path, fields, CsvTextEncoding.StrictShiftJis)[0][0]);
        });
        Test("Failed SJIS CSV conversion preserves destination while MS932 accepts extensions", () =>
        {
            File.WriteAllText(path, "original");
            Throws<EncoderFallbackException>(() => CsvDataWriter.Write(path, fields, [["①髙"]], CsvTextEncoding.StrictShiftJis));
            Equal("original", File.ReadAllText(path));
            CsvDataWriter.Write(path, fields, [["①髙"]], CsvTextEncoding.ShiftJis);
            Equal("①髙", CsvDataReader.Read(path, fields, CsvTextEncoding.ShiftJis)[0][0]);
            Throws<DecoderFallbackException>(() => CsvDataReader.Read(path, fields, CsvTextEncoding.StrictShiftJis));
        });
        var fixedFields = new[] { new FieldDefinition("X", FieldDataType.Text, 4), new FieldDefinition("N", FieldDataType.FullWidthText, 2) };
        Test("Fixed-length X and N independently select SJIS or MS932 without changing padding", () =>
        {
            FixedLengthDataWriter.Write(path, fixedFields, [["東京", "〜日"]], RecordSeparatorMode.None,
                DataEncodingProfile.StrictShiftJis, NationalTextEncoding.StrictShiftJis);
            Equal(8, File.ReadAllBytes(path).Length);
            Equal(true, new[] { "東京", "〜日" }.SequenceEqual(FixedLengthDataReader.Read(path, fixedFields, RecordSeparatorMode.None,
                DataEncodingProfile.StrictShiftJis, NationalTextEncoding.StrictShiftJis)[0]));
            FixedLengthDataWriter.Write(path, fixedFields, [["①", "髙"]], RecordSeparatorMode.None,
                DataEncodingProfile.ShiftJis, NationalTextEncoding.ShiftJis);
            Equal("髙", FixedLengthDataReader.Read(path, fixedFields, RecordSeparatorMode.None)[0][1]);
            var before = File.ReadAllBytes(path);
            Throws<EncoderFallbackException>(() => FixedLengthDataWriter.Write(path, fixedFields, [["①", "日"]], RecordSeparatorMode.None,
                DataEncodingProfile.StrictShiftJis, NationalTextEncoding.StrictShiftJis));
            Equal(true, before.SequenceEqual(File.ReadAllBytes(path)));
            Throws<DecoderFallbackException>(() => FixedLengthDataReader.Read(path, fixedFields, RecordSeparatorMode.None,
                DataEncodingProfile.StrictShiftJis, NationalTextEncoding.StrictShiftJis));
        });
        Test("SJIS validation reports extensions in both X and N while MS932 retains previous acceptance", () =>
        {
            Equal(2, RecordValidator.Validate(fixedFields, [["①", "髙"]], DataEncodingProfile.StrictShiftJis, NationalTextEncoding.StrictShiftJis).Count);
            Equal(0, RecordValidator.Validate(fixedFields, [["①", "髙"]], DataEncodingProfile.ShiftJis, NationalTextEncoding.ShiftJis).Count);
        });
        Test("Save dialog offers separate SJIS and MS932 for CSV, X and N with matching options", () =>
        {
            using var dialog = new DataSaveOptionsForm(new DataSaveOptions(DataFileFormat.FixedLength, CsvTextEncoding.StrictShiftJis,
                RecordSeparatorMode.None, NationalTextEncoding.StrictShiftJis, DataEncodingProfile.StrictShiftJis));
            var combos = Descendants(dialog).OfType<ComboBox>().ToDictionary(c => c.Name);
            Equal("SJIS (JIS X 0208)", combos["TextEncoding"].SelectedItem);
            Equal(NationalTextEncoding.StrictShiftJis, dialog.Options.NationalEncoding);
            Equal(DataEncodingProfile.StrictShiftJis, dialog.Options.TextEncoding);
            combos["DataFormat"].SelectedIndex = 1;
            Equal(false, combos["TextEncoding"].Enabled);
            Equal(CsvTextEncoding.StrictShiftJis, dialog.Options.CsvEncoding);
            combos["CsvEncoding"].SelectedIndex = 1;
            Equal(CsvTextEncoding.ShiftJis, dialog.Options.CsvEncoding);
        });
        Test("Legacy saved settings retain MS932 and SJIS choices persist separately", () =>
        {
            var settingsPath = Path.Combine(temporaryDirectory, "encoding-settings.json");
            var definition = Path.Combine(repo, "samples", "csv-import-definition.csv");
            File.WriteAllText(settingsPath, System.Text.Json.JsonSerializer.Serialize(new[] { new {
                DefinitionPath = definition, DataPath = path, LayoutId = (string?)null,
                Settings = new { Format = 1, CsvEncoding = 1, SeparatorMode = 0, NationalEncoding = 0 }
            } }));
            var store = new DataLoadSettingsStore(settingsPath);
            Equal(CsvTextEncoding.ShiftJis, store.Find(definition, path)!.CsvEncoding);
            Equal(null, store.Find(definition, path)!.TextEncoding);
            var settings = new DataLoadSettings(DataFileFormat.FixedLength, CsvTextEncoding.StrictShiftJis,
                RecordSeparatorMode.None, NationalTextEncoding.StrictShiftJis, DataEncodingProfile.StrictShiftJis);
            Equal(true, store.Remember(definition, path, settings));
            Equal(settings, new DataLoadSettingsStore(settingsPath).Find(definition, path));
        });
        Test("Fixed-length SJIS UI load, settings restore, save conversion and F5 retain selected codec", () =>
        {
            var definition = Path.Combine(temporaryDirectory, "encoding-definition.csv");
            var data = Path.Combine(temporaryDirectory, "encoding-fixed.dat");
            var settings = new DataLoadSettingsStore(Path.Combine(temporaryDirectory, "encoding-ui-settings.json"));
            File.WriteAllText(definition, "文字,X(4)\r\n");
            File.WriteAllBytes(data, sjis.GetBytes("東京\r\n"));
            using (var form = new MainForm(["--definition", definition], settings))
            {
                WithDialog("データ読込条件", DialogResult.OK, () => Invoke(form, "LoadDataFromPath", data, "読込完了"), dialog =>
                {
                    Descendants(dialog).OfType<ComboBox>().Single(c => c.Name == "TextEncoding").SelectedIndex = 2;
                    Descendants(dialog).OfType<RadioButton>().Single(c => c.Text == "SJIS (JIS X 0208)").Checked = true;
                });
                Equal(DataEncodingProfile.StrictShiftJis, Get<DataEncodingProfile>(form, "currentDataEncodingProfile"));
                File.WriteAllBytes(data, sjis.GetBytes("〜日\r\n"));
                Invoke(form, "ReloadCurrentFilesCore");
                Equal("〜日", Get<List<List<string>>>(form, "records")[0][0]);
                Equal(true, Get<TextBox>(form, "separatorModeTextBox").Text.Contains("SJIS (JIS X 0208)"));
                Get<List<List<string>>>(form, "records")[0][0] = "①";
                Equal(true, ((string?)Invoke(form, "GetValidationError", Get<List<FieldDefinition>>(form, "fields")[0], "①"))?.Contains("SJIS") == true);
                var before = File.ReadAllBytes(data);
                Throws<TargetInvocationException>(() => Invoke(form, "WriteData", data, Invoke(form, "CurrentSaveOptions")));
                Equal(true, before.SequenceEqual(File.ReadAllBytes(data)));
                var converted = Path.Combine(temporaryDirectory, "encoding-converted-ms932.dat");
                Invoke(form, "WriteData", converted, new DataSaveOptions(DataFileFormat.FixedLength, CsvTextEncoding.Utf8,
                    RecordSeparatorMode.CrLfOrLf, NationalTextEncoding.StrictShiftJis, DataEncodingProfile.ShiftJis));
                Equal(DataEncodingProfile.ShiftJis, Get<DataEncodingProfile>(form, "currentDataEncodingProfile"));
                Equal("①", FixedLengthDataReader.Read(converted, Get<List<FieldDefinition>>(form, "fields"), RecordSeparatorMode.CrLfOrLf)[0][0]);
            }
            using var next = new MainForm(["--definition", definition], settings);
            WithDialog("データ読込条件", DialogResult.Cancel, () => Invoke(next, "LoadDataFromPath", data, "読込完了"), dialog =>
                Equal(2, Descendants(dialog).OfType<ComboBox>().Single(c => c.Name == "TextEncoding").SelectedIndex));
        });
        Test("CSV preview rejects extensions as SJIS, accepts MS932 and failed loads retain prior data", () =>
        {
            var definition = Path.Combine(temporaryDirectory, "encoding-csv-definition.csv");
            var data = Path.Combine(temporaryDirectory, "encoding-extension.csv");
            File.WriteAllText(definition, "文字,X(40)\r\n");
            CsvDataWriter.Write(data, fields, [["①髙"]], CsvTextEncoding.ShiftJis);
            var store = new DataLoadSettingsStore(Path.Combine(temporaryDirectory, "encoding-csv-settings.json"));
            using var form = new MainForm(["--definition", definition], store);
            WithDialog("データ読込条件", DialogResult.OK, () => Invoke(form, "LoadDataFromPath", data, "読込完了"), dialog =>
            {
                var combo = Descendants(dialog).OfType<ComboBox>().Single(c => c.Name == "CsvEncoding");
                var ok = Descendants(dialog).OfType<Button>().Single(c => c.Text == "OK");
                combo.SelectedIndex = 2;
                Equal(false, ok.Enabled);
                combo.SelectedIndex = 1;
                Equal(true, ok.Enabled);
            });
            Equal("①髙", Get<List<List<string>>>(form, "records")[0][0]);
            Equal(CsvTextEncoding.ShiftJis, Get<CsvTextEncoding>(form, "currentCsvEncoding"));
            Throws<TargetInvocationException>(() => Invoke(form, "LoadCsvData", data, CsvTextEncoding.StrictShiftJis));
            Equal("①髙", Get<List<List<string>>>(form, "records")[0][0]);
            Equal(CsvTextEncoding.ShiftJis, store.Find(definition, data)!.CsvEncoding);
        });
    }
}
