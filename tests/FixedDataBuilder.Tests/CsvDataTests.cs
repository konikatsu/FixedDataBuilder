using System.Reflection;
using System.Text;
using FixedDataBuilder;

internal static partial class Program
{
    private static void RunCsvDataTests(string repo)
    {
        var definition = Path.Combine(repo, "samples", "csv-import-definition.csv");
        var data = Path.Combine(repo, "samples", "csv-import-data-utf8.csv");
        var fields = DefinitionCsvReader.Read(definition);
        var scratch = Path.Combine(temporaryDirectory, "csv-input.csv");
        Test("CSV imports field order and first data row, quoted commas, escaped quotes and empty cells", () =>
        {
            var rows = CsvDataReader.Read(data, fields);
            Equal(5, rows.Count);
            Equal("000001", rows[0][0]);
            Equal("東京,中央", rows[0][1]);
            Equal("引用符 \"確認\"", rows[1][3]);
            Equal("", rows[2][3]);
            Equal("前後の空白を保持  ", rows[3][3]);
        });
        Test("CSV supports UTF-8 BOM, Japanese and quoted CRLF/newline with trailing empty column", () =>
        {
            foreach (var bom in new[] { false, true })
            {
                File.WriteAllText(scratch, "000001,\"東京\r\n中央\n支店\",12,\r\n", new UTF8Encoding(bom));
                var rows = CsvDataReader.Read(scratch, fields);
                Equal(1, rows.Count);
                Equal("東京\r\n中央\n支店", rows[0][1]);
                Equal("", rows[0][3]);
            }
        });
        Test("CSV preserves all empty cells, spaces, leading zeros and final row without newline", () =>
        {
            File.WriteAllText(scratch, ",,,\n001234, name ,00002, ", new UTF8Encoding(false));
            var rows = CsvDataReader.Read(scratch, fields);
            Equal(2, rows.Count);
            Equal(true, rows[0].All(value => value == ""));
            Equal("001234", rows[1][0]);
            Equal(" name ", rows[1][1]);
            Equal("00002", rows[1][2]);
            Equal(" ", rows[1][3]);
        });
        Test("CSV explicitly reads Shift_JIS and rejects invalid UTF-8 without replacement", () =>
        {
            File.WriteAllText(scratch, "1,東京,2,備考", Encoding.GetEncoding(932));
            Equal("東京", CsvDataReader.Read(scratch, fields, CsvTextEncoding.ShiftJis)[0][1]);
            Throws<DecoderFallbackException>(() => CsvDataReader.Read(scratch, fields));
        });
        Test("CSV rejects incorrect column counts, broken quoting and empty files", () =>
        {
            foreach (var text in new[] { "", "1,2,3", "1,2,3,4,5", "1,\"bad,2,3", "1,b\"ad,2,3", "1,\"bad\"x,2,3" })
            {
                File.WriteAllText(scratch, text, new UTF8Encoding(false));
                Throws<InvalidDataException>(() => CsvDataReader.Read(scratch, fields));
            }
        });
        Test("CSV skips REDEFINES columns while preserving internal field indices", () =>
        {
            var redefined = fields.ToList();
            redefined.Insert(1, new FieldDefinition("別表示", FieldDataType.Text, 6, RedefinesName: fields[0].Name));
            var rows = CsvDataReader.Read(data, redefined);
            Equal(5, rows[0].Count);
            Equal("", rows[0][1]);
            Equal("東京,中央", rows[0][2]);
        });
        Test("CSV UI preview, drag/drop and startup retain every value and CSV save format", () =>
        {
            using var form = new MainForm(["--definition", definition]);
            WithDialog("データ読込条件", DialogResult.OK,
                () => Drop(Get<TextBox>(form, "dataPathTextBox"), data), dialog =>
                {
                    Equal(1, Descendants(dialog).OfType<ComboBox>().Single(control => control.Name == "DataFormat").SelectedIndex);
                    var preview = Descendants(dialog).OfType<DataGridView>().Single();
                    Equal(5, preview.Rows.Count);
                    Equal("000001", preview[1, 0].Value);
                    Equal(true, Descendants(dialog).OfType<Button>().Single(button => button.Text == "OK").Enabled);
                });
            var expected = CsvDataReader.Read(data, fields);
            var actual = Get<List<List<string>>>(form, "records");
            Equal(true, actual.SelectMany(row => row).SequenceEqual(expected.SelectMany(row => row)));
            Equal(data, Get<string?>(form, "saveDataPath"));
            Equal(DataFileFormat.Csv, Get<DataFileFormat>(form, "currentDataFileFormat"));
            Equal(DataEncodingProfile.ShiftJis, Get<DataEncodingProfile>(form, "currentDataEncodingProfile"));
            using var startup = new MainForm(["--definition", definition, "--data", data]);
            Equal(5, Get<List<List<string>>>(startup, "records").Count);
        });
        Test("Failed CSV parse and canceled dialog retain current records, save target and encodings", () =>
        {
            using var form = CreateBasicForm(repo);
            var previous = Get<List<List<string>>>(form, "records").ToArray();
            var savePath = Get<string>(form, "saveDataPath");
            File.WriteAllText(scratch, "bad,columns");
            Throws<TargetInvocationException>(() => Invoke(form, "LoadCsvData", scratch, CsvTextEncoding.Utf8));
            WithDialog("データ読込条件", DialogResult.Cancel,
                () => Drop(Get<TextBox>(form, "dataPathTextBox"), scratch), dialog =>
                    Equal(false, Descendants(dialog).OfType<Button>().Single(button => button.Text == "OK").Enabled));
            Equal(true, previous.SequenceEqual(Get<List<List<string>>>(form, "records")));
            Equal(savePath, Get<string>(form, "saveDataPath"));
            Equal(DataEncodingProfile.ShiftJis, Get<DataEncodingProfile>(form, "currentDataEncodingProfile"));
        });
        Test("CSV imported values can be saved and read as fixed-length without overwriting source", () =>
        {
            var sourceBytes = File.ReadAllBytes(data);
            var rows = CsvDataReader.Read(data, fields);
            var target = Path.Combine(temporaryDirectory, "csv-converted.dat");
            FixedLengthDataWriter.Write(target, fields, rows, RecordSeparatorMode.CrLfOrLf);
            var reread = FixedLengthDataReader.Read(target, fields, RecordSeparatorMode.CrLfOrLf);
            Equal(5, reread.Count);
            Equal("東京,中央", reread[0][1]);
            Equal("00012", reread[0][2]);
            Equal(true, sourceBytes.SequenceEqual(File.ReadAllBytes(data)));
        });
        Test("CSV output preserves empty cells, spaces, zeros, quotes and embedded CRLF with no padding", () =>
        {
            var rows = new List<List<string>> { new() { "000001", " name　 ", "", "a,\"b\"\r\nc" }, new() { "", "", "00002", "" } };
            var target = Path.Combine(temporaryDirectory, "preserved.csv");
            CsvDataWriter.Write(target, fields, rows);
            Equal("\"000001\",\" name　 \",\"\",\"a,\"\"b\"\"\r\nc\"\r\n\"\",\"\",\"00002\",\"\"\r\n", File.ReadAllText(target));
            Equal(false, File.ReadAllBytes(target).AsSpan().StartsWith(Encoding.UTF8.GetPreamble()));
            Equal(true, rows.SelectMany(row => row).SequenceEqual(CsvDataReader.Read(target, fields).SelectMany(row => row)));
        });
        Test("CSV display and editing preserve raw numbers and only mark existing spaces", () =>
        {
            using var form = new MainForm(["--definition", definition, "--data", data, "--layout", "record"]);
            var grid = Get<DataGridView>(form, "grid");
            Equal("12", grid[3, 2].Value);
            Equal("CSV列1", grid[1, 1].Value);
            Equal("ab", Invoke(form, "GetVisibleTextValue", fields[1], "ab"));
            Equal("ab･□", Invoke(form, "GetVisibleTextValue", fields[1], "ab 　"));
            grid[3, 2].Value = "00002";
            Invoke(form, "UpdateRecordValue", 2, 3);
            Equal("00002", Get<List<List<string>>>(form, "records")[0][2]);
        });
        Test("Save format defaults, format conversion, hidden columns and overwrite respect CSV semantics", () =>
        {
            using var form = new MainForm(["--definition", definition, "--data", data]);
            Get<HashSet<int>>(form, "hiddenFieldIndexes").Add(3);
            var csvOptions = new DataSaveOptions(DataFileFormat.Csv, CsvTextEncoding.Utf8, RecordSeparatorMode.CrLfOrLf, NationalTextEncoding.ShiftJis);
            using var dialog = new DataSaveOptionsForm(csvOptions);
            Equal(csvOptions with { TextEncoding = DataEncodingProfile.ShiftJis }, dialog.Options);
            var combos = Descendants(dialog).OfType<ComboBox>().ToDictionary(control => control.Name);
            Equal(true, combos["CsvEncoding"].Enabled);
            Equal(false, combos["NationalEncoding"].Enabled);
            combos["DataFormat"].SelectedIndex = 0;
            Equal(DataFileFormat.FixedLength, dialog.Options.Format);
            Equal(false, combos["CsvEncoding"].Enabled);
            Equal(true, combos["NationalEncoding"].Enabled);
            var target = Path.Combine(temporaryDirectory, "ui-output.csv");
            Invoke(form, "WriteData", target, csvOptions);
            Equal(4, CsvDataReader.Read(target, fields)[0].Count);
            Get<List<List<string>>>(form, "records")[0][3] = "  ";
            Invoke(form, "SaveDataOverwrite");
            Equal("  ", CsvDataReader.Read(target, fields)[0][3]);
            var fixedTarget = Path.Combine(temporaryDirectory, "ui-converted.dat");
            Invoke(form, "WriteData", fixedTarget, csvOptions with { Format = DataFileFormat.FixedLength });
            Equal(DataFileFormat.FixedLength, Get<DataFileFormat>(form, "currentDataFileFormat"));
            Equal("00012", FixedLengthDataReader.Read(fixedTarget, fields, RecordSeparatorMode.CrLfOrLf)[0][2]);
            WithDialog("データ保存条件", DialogResult.Cancel, () => Invoke(form, "SaveDataAs"));
            Equal(fixedTarget, Get<string>(form, "saveDataPath"));
        });
        Test("CSV Shift_JIS output round trips and unencodable values keep existing destination and UI state", () =>
        {
            using var form = new MainForm(["--definition", definition, "--data", data]);
            var options = new DataSaveOptions(DataFileFormat.Csv, CsvTextEncoding.ShiftJis, RecordSeparatorMode.None, NationalTextEncoding.Utf32);
            var target = Path.Combine(temporaryDirectory, "output-sjis.csv");
            Invoke(form, "WriteData", target, options);
            Equal("東京,中央", CsvDataReader.Read(target, fields, CsvTextEncoding.ShiftJis)[0][1]);
            var previousBytes = File.ReadAllBytes(target);
            var previousPath = Get<string>(form, "saveDataPath");
            Get<List<List<string>>>(form, "records")[0][1] = "😀";
            Throws<TargetInvocationException>(() => Invoke(form, "WriteData", target, options));
            Equal(true, previousBytes.SequenceEqual(File.ReadAllBytes(target)));
            Equal(previousPath, Get<string>(form, "saveDataPath"));
            var grid = Get<DataGridView>(form, "grid");
            grid.CurrentCell = grid[2, 1];
            Invoke(form, "UpdateHexView");
            Equal(true, Get<TextBox>(form, "hexTextBox").Text.StartsWith("HEX 表示エラー:"));
        });
        Test("Numeric REDEFINES never blocks CSV to fixed conversion or adds output columns", () =>
        {
            foreach (var type in new[] { FieldDataType.PlainNumber, FieldDataType.SignedNumber, FieldDataType.PackedUnsigned, FieldDataType.PackedSigned })
            {
                using var form = new MainForm(["--definition", definition]);
                var formFields = Get<List<FieldDefinition>>(form, "fields");
                formFields.Insert(1, new FieldDefinition("別数値", type, 6, RedefinesName: fields[0].Name));
                Invoke(form, "LoadCsvData", data, CsvTextEncoding.Utf8);
                Equal("0", Get<List<List<string>>>(form, "records")[0][1]);
                var target = Path.Combine(temporaryDirectory, "numeric-redefines.dat");
                Invoke(form, "WriteData", target, new DataSaveOptions(DataFileFormat.FixedLength, CsvTextEncoding.Utf8,
                    RecordSeparatorMode.CrLfOrLf, NationalTextEncoding.ShiftJis));
                Equal(5, FixedLengthDataReader.Read(target, fields, RecordSeparatorMode.CrLfOrLf).Count);
                var csvTarget = Path.Combine(temporaryDirectory, "numeric-redefines.csv");
                Invoke(form, "WriteData", csvTarget, new DataSaveOptions(DataFileFormat.Csv, CsvTextEncoding.Utf8,
                    RecordSeparatorMode.CrLfOrLf, NationalTextEncoding.ShiftJis));
                Equal(4, CsvDataReader.Read(csvTarget, fields)[0].Count);
            }
        });
        Test("New CSV rows use empty cells for text, N and numbers while fixed defaults remain unchanged", () =>
        {
            var newDefinition = Path.Combine(temporaryDirectory, "new-row-definition.csv");
            File.WriteAllText(newDefinition, "項目名,定義\n文字,X(6)\n全角,N(4)\n数字,9(3)\n");
            File.WriteAllText(scratch, "\"code\",\"\",\"\"\r\n");
            using var form = new MainForm(["--definition", newDefinition, "--data", scratch]);
            Invoke(form, "AddRecord");
            var rows = Get<List<List<string>>>(form, "records");
            Equal(true, rows[1].All(value => value == ""));
            var target = Path.Combine(temporaryDirectory, "new-empty-row.csv");
            Invoke(form, "WriteData", target, Invoke(form, "CurrentSaveOptions"));
            Equal(true, CsvDataReader.Read(target, Get<List<FieldDefinition>>(form, "fields"))[1].All(value => value == ""));
            using var fixedForm = new MainForm(["--definition", newDefinition]);
            Equal("　　　　", Get<List<List<string>>>(fixedForm, "records")[0][1]);
            Equal("0", Get<List<List<string>>>(fixedForm, "records")[0][2]);
        });
        Test("Saving the same format retains manual column widths and selected cell in both modes", () =>
        {
            foreach (var csvMode in new[] { false, true })
            {
                using var form = csvMode ? new MainForm(["--definition", definition, "--data", data]) : CreateBasicForm(repo);
                var grid = Get<DataGridView>(form, "grid");
                grid.Columns[2].Width += 71;
                var width = grid.Columns[2].Width;
                grid.CurrentCell = grid[2, 1];
                var target = Path.Combine(temporaryDirectory, csvMode ? "width.csv" : "width.dat");
                Invoke(form, "WriteData", target, Invoke(form, "CurrentSaveOptions"));
                Equal(width, grid.Columns[2].Width);
                Equal(2, grid.CurrentCell.ColumnIndex);
                Equal(1, grid.CurrentCell.RowIndex);
            }
        });
    }

    private static void CaptureCsv(string repo, string mainImage, string optionsImage)
    {
        using var form = new MainForm(["--definition", Path.Combine(repo, "samples", "csv-import-definition.csv"), "--layout", "record"]);
        WithDialog("データ読込条件", DialogResult.OK,
            () => Invoke(form, "LoadDataFromPath", Path.Combine(repo, "samples", "csv-import-data-utf8.csv"), "CSVの架空データを読み込みました。"),
            dialog => Capture(dialog, optionsImage, expectedHex: null));
        Get<TextBox>(form, "definitionPathTextBox").Text = "（CSV表示確認用サンプル）";
        Get<TextBox>(form, "dataPathTextBox").Text = "（架空CSVデータ・5行）";
        Capture(form, mainImage, expectedHex: null);
        using var outputDialog = new DataSaveOptionsForm((DataSaveOptions)Invoke(form, "CurrentSaveOptions")!);
        var version = typeof(MainForm).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
        Capture(outputDialog, Path.Combine(Path.GetDirectoryName(optionsImage)!, $"screen-image-csv-save-options-v{version}.png"), expectedHex: null);
    }
}
