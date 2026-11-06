using System.Reflection;
using System.Text;
using FixedDataBuilder;

internal static partial class Program
{
    private static void RunCsvUnicodeTests()
    {
        var encodings = new[] { CsvTextEncoding.Utf16Le, CsvTextEncoding.Utf32Le };
        var path = Path.Combine(temporaryDirectory, "unicode-data.csv");
        var definition = Path.Combine(temporaryDirectory, "unicode-definition.csv");
        DefinitionCsvWriter.Write(definition, [new("番号", "X(6)"), new("氏名", "N(30)"), new("備考", "X(80)")]);
        var fields = DefinitionCsvReader.Read(definition);
        byte[] Bytes(string text, CsvTextEncoding encoding, bool bom)
        {
            var prefix = !bom ? Array.Empty<byte>() : encoding == CsvTextEncoding.Utf16Le ? new byte[] { 0xff, 0xfe } : new byte[] { 0xff, 0xfe, 0, 0 };
            return prefix.Concat(JapaneseEncodings.ForCsv(encoding).GetBytes(text)).ToArray();
        }
        const string text = "\"000001\",\" 前東京　 展開\",\"引用符 \"\"確認\"\"\r\n次の行😀\"\r\n\"000002\",\"\",\" \"";
        Test("CSV Unicode reader accepts both little-endian codecs with and without BOM while preserving logical values", () =>
        {
            foreach (var encoding in encodings)
            foreach (var bom in new[] { false, true })
            {
                var bytes = Bytes(text, encoding, bom); File.WriteAllBytes(path, bytes);
                var rows = CsvDataReader.Read(path, fields, encoding);
                Equal(2, rows.Count); Equal("000001", rows[0][0]); Equal(" 前東京　 展開", rows[0][1]);
                Equal("引用符 \"確認\"\r\n次の行😀", rows[0][2]); Equal("", rows[1][1]); Equal(" ", rows[1][2]);
                Equal(true, bytes.SequenceEqual(File.ReadAllBytes(path)));
            }
        });
        Test("CSV Unicode header and headerless imports strip one file BOM without dropping the first record", () =>
        {
            foreach (var encoding in encodings)
            foreach (var bom in new[] { false, true })
            {
                File.WriteAllBytes(path, Bytes("番号,氏名,備考\n000001,東京,\n", encoding, bom));
                Equal("番号", CsvDataReader.Read(path, fields, encoding)[0][0]);
                var rows = CsvDataReader.Read(path, fields, encoding, hasHeader: true);
                Equal(1, rows.Count); Equal("000001", rows[0][0]); Equal("", rows[0][2]);
            }
        });
        Test("CSV Unicode detection prefers UTF32 BOM over the shared UTF16 prefix and recognizes BOMless candidates", () =>
        {
            foreach (var encoding in encodings)
            foreach (var bom in new[] { false, true })
            {
                File.WriteAllBytes(path, Bytes(text, encoding, bom));
                var detected = DataEncodingDetector.Csv(path);
                Equal(encoding, detected.Encoding!.Value);
                Equal(true, detected.Description.Contains(bom ? "BOMあり" : "BOMなし"));
                Equal("000001", CsvDataReader.ReadRows(path, detected.Encoding.Value)[0][0]);
            }
        });
        Test("CSV Unicode autodetection does not claim certainty for BOMless single-column Japanese without zero bytes", () =>
        {
            File.WriteAllBytes(path, Bytes("東京", CsvTextEncoding.Utf16Le, false));
            Equal("東京", CsvDataReader.ReadRows(path, CsvTextEncoding.Utf16Le)[0][0]);
            // Explicit selection works even where several byte encodings are plausible.
            Equal(null, CsvUnicodeEncoding.BomlessCandidate(File.ReadAllBytes(path)));
        });
        Test("Malformed Unicode BOM payloads fail strict decoding without falling back to MS932", () =>
        {
            foreach (var bytes in new byte[][] { [0xff, 0xfe, 65], [0xff, 0xfe, 0, 0, 65, 0, 0],
                [0xff, 0xfe, 0, 0, 0, 0, 0x11, 0], [0xff, 0xfe, 0, 0xd8] })
            {
                File.WriteAllBytes(path, bytes);
                Throws<DecoderFallbackException>(() => DataEncodingDetector.Csv(path));
            }
        });
        Test("CSV reader rejects BOM codec mismatches and unsupported big-endian prefixes before parsing", () =>
        {
            foreach (var encoding in encodings)
            {
                File.WriteAllBytes(path, Bytes(text, encoding, true));
                Throws<InvalidDataException>(() => CsvDataReader.ReadRows(path, CsvTextEncoding.Utf8));
                Throws<InvalidDataException>(() => CsvDataReader.ReadRows(path, encoding == CsvTextEncoding.Utf16Le ? CsvTextEncoding.Utf32Le : CsvTextEncoding.Utf16Le));
            }
            foreach (var bytes in new byte[][] { [0xfe, 0xff, 0, 65], [0, 0, 0xfe, 0xff, 0, 0, 0, 65] })
            {
                File.WriteAllBytes(path, bytes); Throws<InvalidDataException>(() => DataEncodingDetector.Csv(path));
                Throws<InvalidDataException>(() => CsvDataReader.ReadRows(path, CsvTextEncoding.Utf16Le));
            }
        });
        Test("CSV data UFEFF is retained after removing exactly one optional file BOM", () =>
        {
            foreach (var encoding in encodings)
            foreach (var bom in new[] { false, true })
            {
                File.WriteAllBytes(path, Bytes("\"\uFEFFID\",\"A\uFEFFB\",\"\"", encoding, bom));
                var rows = CsvDataReader.Read(path, fields, encoding);
                Equal("\uFEFFID", rows[0][0]); Equal("A\uFEFFB", rows[0][1]);
            }
        });
        Test("CSV Unicode inference measures the actual byte widths and records the correct codec name", () =>
        {
            foreach (var encoding in encodings)
            {
                File.WriteAllBytes(path, Bytes("番号,氏名,備考\r\n000001,東京,😀\r\n000002,大阪,\r\n", encoding, false));
                var inferred = CsvDefinitionInferer.Analyze(path).CreateDefinition(true);
                Equal(encoding, inferred.Encoding); Equal("000001", inferred.Records[0][0]);
                Equal(encoding == CsvTextEncoding.Utf32Le ? 24 : 12, inferred.Fields[0].Length);
                Equal(encoding == CsvTextEncoding.Utf32Le ? 8 : 4, inferred.Fields[1].Length);
                Equal(true, inferred.Fields.All(field => field.Comment.Contains(JapaneseEncodings.Name(encoding))));
            }
        });
        Test("Normal main CSV startup supports Unicode CLI aliases automatic loading reload HEX and BOMless save", () =>
        {
            foreach (var encoding in encodings)
            foreach (var option in new[] { encoding == CsvTextEncoding.Utf16Le ? "UTF-16LE" : "UTF-32LE", "auto" })
            {
                File.WriteAllBytes(path, Bytes(text, encoding, true));
                using var main = new MainForm(["--definition", definition, "--data", path, "--csv-encoding", option, "--layout", "record"]);
                Equal(encoding, Get<CsvTextEncoding>(main, "currentCsvEncoding"));
                Equal("000001", Get<List<List<string>>>(main, "records")[0][0]);
                Invoke(main, "ReloadCurrentFilesCore");
                Equal("000002", Get<List<List<string>>>(main, "records")[1][0]);
                var grid = Get<DataGridView>(main, "grid");
                grid.CurrentCell = grid[1, (int)Invoke(main, "RecordRowsDataStartIndex")!]; Invoke(main, "UpdateHexView");
                Equal(true, Get<TextBox>(main, "hexTextBox").Text.StartsWith("30 00"));
                var saved = Path.Combine(temporaryDirectory, $"unicode-save-{encoding}.csv");
                Invoke(main, "WriteData", saved, Invoke(main, "CurrentSaveOptions"));
                Equal(null, CsvUnicodeEncoding.Bom(File.ReadAllBytes(saved)).Encoding);
                Equal("000001", CsvDataReader.Read(saved, fields, encoding)[0][0]);
                Equal(false, Get<bool>(main, "hasUnsavedChanges"));
            }
        });
        Test("Comparison datasets load Unicode CSV from either side with logical values and codec profiles intact", () =>
        {
            foreach (var encoding in encodings)
            {
                File.WriteAllBytes(path, Bytes(text, encoding, true));
                var set = ComparisonDataSet.Load(definition, path, new(DataFileFormat.Csv, encoding));
                Equal(encoding, set.Profile.CsvEncoding); Equal("000001", set.Records[0][0]);
                Equal(" 前東京　 展開", set.ReadSource(0, 1).Value);
            }
        });
        Test("CSV Unicode save roundtrips values without BOM and rejects unpaired surrogates before overwriting", () =>
        {
            foreach (var encoding in encodings)
            {
                var rows = new List<List<string>> { new() { "000001", "東京😀", " \"quoted\" " } };
                CsvDataWriter.Write(path, fields, rows, encoding, true);
                Equal(null, CsvUnicodeEncoding.Bom(File.ReadAllBytes(path)).Encoding);
                Equal(true, rows[0].SequenceEqual(CsvDataReader.Read(path, fields, encoding, true)[0]));
                var original = File.ReadAllBytes(path); rows[0][1] = "\uD800";
                Throws<EncoderFallbackException>(() => CsvDataWriter.Write(path, fields, rows, encoding));
                Equal(true, original.SequenceEqual(File.ReadAllBytes(path)));
            }
        });
        Test("CSV Unicode enum values append to legacy choices and survive remembered load and save options", () =>
        {
            Equal(0, (int)CsvTextEncoding.Utf8); Equal(1, (int)CsvTextEncoding.ShiftJis); Equal(2, (int)CsvTextEncoding.StrictShiftJis);
            var store = new DataLoadSettingsStore(Path.Combine(temporaryDirectory, "unicode-settings.json"));
            foreach (var encoding in encodings)
            {
                var settings = new DataLoadSettings(DataFileFormat.Csv, encoding, RecordSeparatorMode.CrLfOrLf, NationalTextEncoding.ShiftJis);
                Equal(true, store.Remember(definition, path, settings)); Equal(encoding, store.Find(definition, path)!.CsvEncoding);
                using var dialog = new DataSaveOptionsForm(new(DataFileFormat.Csv, encoding, RecordSeparatorMode.CrLfOrLf, NationalTextEncoding.ShiftJis));
                Equal(encoding, dialog.Options.CsvEncoding);
                var choices = Get<ComboBox>(dialog, "csvEncoding"); Equal(5, choices.Items.Count);
                Equal(JapaneseEncodings.Name(encoding), choices.SelectedItem!.ToString());
            }
        });
    }
}
