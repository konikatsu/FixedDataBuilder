using System.Reflection;
using System.Text;
using FixedDataBuilder;

internal static partial class Program
{
    private static void RunCsvHeaderTests(string repo)
    {
        var fields = new[] { new FieldDefinition("番号", FieldDataType.Text, 6), new FieldDefinition("名称", FieldDataType.Text, 40) };
        var path = Path.Combine(temporaryDirectory, "header.csv");
        var definition = Path.Combine(temporaryDirectory, "header-definition.csv");
        File.WriteAllText(definition, "番号,X(6)\r\n名称,X(40)\r\n");
        Test("CSV header is explicit, excluded from data and mapped by definition order rather than name", () =>
        {
            File.WriteAllText(path, "名称,番号\r\n001, 東京 \r\n002,\r\n");
            var rows = CsvDataReader.Read(path, fields, hasHeader: true);
            Equal(2, rows.Count);
            Equal("001", rows[0][0]);
            Equal(" 東京 ", rows[0][1]);
            Equal("", rows[1][1]);
            var noHeader = CsvDataReader.Read(path, fields);
            Equal(3, noHeader.Count);
            Equal("名称", noHeader[0][0]);
        });
        Test("CSV header uses the first logical record with BOM, escaped quotes and embedded CRLF", () =>
        {
            File.WriteAllText(path, "\"番,号\",\"名\"\"称\r\n補足\"\r\n001,東京", new UTF8Encoding(true));
            var rows = CsvDataReader.Read(path, fields, hasHeader: true);
            Equal(1, rows.Count);
            Equal("東京", rows[0][1]);
            File.WriteAllText(path, "\"番\r\n号\",名称\r\n001,東京,余分", new UTF8Encoding(true));
            try { CsvDataReader.Read(path, fields, hasHeader: true); throw new Exception("Expected invalid column count"); }
            catch (InvalidDataException ex)
            {
                Equal(true, ex.Message.Contains("CSV 1 行目（ファイル 3 行目）"));
            }
        });
        Test("CSV validates header syntax and column count and rejects missing data after a header", () =>
        {
            foreach (var text in new[] { "番号\n001,東京", "番号,名称,余分\n001,東京", "\"番号,名称\n001,東京", "番\"号,名称\n001,東京", "番号,名称\r\n", "" })
            {
                File.WriteAllText(path, text);
                Throws<InvalidDataException>(() => CsvDataReader.Read(path, fields, hasHeader: true));
            }
            File.WriteAllText(path, "番号\n001,東京");
            try { CsvDataReader.Read(path, fields, hasHeader: true); throw new Exception("Expected invalid header"); }
            catch (InvalidDataException ex) { Equal(true, ex.Message.StartsWith("CSV ヘッダー（ファイル 1 行目）")); }
        });
        Test("CSV writes quoted definition names in physical order and skips REDEFINES in header and data", () =>
        {
            var exportFields = new[] { fields[0] with { Name = "番,号" },
                new FieldDefinition("別表示", FieldDataType.Text, 6, RedefinesName: "番,号"),
                fields[1] with { Name = "名\"称\r\n補足" } };
            List<List<string>> rows = [["001", "ignored", " 東京 "], ["002", "ignored", ""]];
            CsvDataWriter.Write(path, exportFields, rows, hasHeader: true);
            Equal(true, File.ReadAllText(path).StartsWith("\"番,号\",\"名\"\"称\r\n補足\"\r\n"));
            var actual = CsvDataReader.Read(path, exportFields, hasHeader: true);
            Equal(2, actual.Count);
            Equal("001", actual[0][0]);
            Equal("", actual[0][1]);
            Equal(" 東京 ", actual[0][2]);
        });
        Test("Header CSV round trips UTF-8, SJIS and MS932 without altering empty cells or leading zeros", () =>
        {
            foreach (var encoding in Enum.GetValues<CsvTextEncoding>())
            {
                List<List<string>> rows = [["000001", " 東京ｶﾅ "], ["000002", ""]];
                CsvDataWriter.Write(path, fields, rows, encoding, hasHeader: true);
                Equal(true, rows.SelectMany(r => r).SequenceEqual(CsvDataReader.Read(path, fields, encoding, hasHeader: true).SelectMany(r => r)));
                Equal("番号", CsvDataReader.Read(path, fields, encoding)[0][0]);
            }
        });
        Test("Unencodable header names fail validation before overwriting an existing CSV", () =>
        {
            var exportFields = new[] { fields[0] with { Name = "①番号" }, fields[1] };
            File.WriteAllText(path, "original");
            Throws<EncoderFallbackException>(() => CsvDataWriter.Write(path, exportFields, [["001", "東京"]], CsvTextEncoding.StrictShiftJis, true));
            Equal("original", File.ReadAllText(path));
            CsvDataWriter.Write(path, exportFields, [["001", "東京"]], CsvTextEncoding.ShiftJis, true);
            Equal("①番号", CsvDataReader.Read(path, exportFields, CsvTextEncoding.ShiftJis)[0][0]);
        });
        Test("Save dialog selects header mode independently of CSV encoding and fixed-length mode", () =>
        {
            using var dialog = new DataSaveOptionsForm(new DataSaveOptions(DataFileFormat.Csv, CsvTextEncoding.StrictShiftJis,
                RecordSeparatorMode.None, NationalTextEncoding.Utf16, DataEncodingProfile.ShiftJis, CsvHasHeader: true));
            var combos = Descendants(dialog).OfType<ComboBox>().ToDictionary(c => c.Name);
            Equal(2, combos["DataFormat"].SelectedIndex);
            Equal(true, dialog.Options.CsvHasHeader);
            Equal(CsvTextEncoding.StrictShiftJis, dialog.Options.CsvEncoding);
            combos["DataFormat"].SelectedIndex = 1;
            Equal(false, dialog.Options.CsvHasHeader);
            combos["DataFormat"].SelectedIndex = 0;
            Equal(false, dialog.Options.CsvHasHeader);
            Equal(false, combos["CsvEncoding"].Enabled);
        });
        Test("Header CSV preview, GUI load, settings restore and startup exclude the header from row numbers", () =>
        {
            var sampleDefinition = Path.Combine(repo, "samples", "csv-import-definition.csv");
            var data = Path.Combine(repo, "samples", "csv-import-data-with-header-utf8.csv");
            var store = new DataLoadSettingsStore(Path.Combine(temporaryDirectory, "header-load-settings.json"));
            using (var form = new MainForm(["--definition", sampleDefinition], store))
            {
                WithDialog("データ読込条件", DialogResult.OK, () => Invoke(form, "LoadDataFromPath", data, "読込完了"), dialog =>
                {
                    var format = Descendants(dialog).OfType<ComboBox>().Single(c => c.Name == "DataFormat");
                    Equal(1, format.SelectedIndex); // Existing/default behavior remains headerless.
                    format.SelectedIndex = 2;
                    var grid = Descendants(dialog).OfType<DataGridView>().Single();
                    Equal("1", grid[0, 0].Value);
                    Equal("000001", grid[1, 0].Value);
                });
                Equal(true, Get<bool>(form, "currentCsvHasHeader"));
                Equal(5, Get<List<List<string>>>(form, "records").Count);
                Equal(true, Get<TextBox>(form, "separatorModeTextBox").Text.Contains("ヘッダーあり"));
                Equal(true, store.Find(sampleDefinition, data)!.CsvHasHeader);
            }
            using var next = new MainForm(["--definition", sampleDefinition], store);
            WithDialog("データ読込条件", DialogResult.Cancel, () => Invoke(next, "LoadDataFromPath", data, "読込完了"), dialog =>
                Equal(2, Descendants(dialog).OfType<ComboBox>().Single(c => c.Name == "DataFormat").SelectedIndex));
            using var startup = new MainForm(["--definition", sampleDefinition, "--data", data, "--csv-header", "present"]);
            Equal(5, Get<List<List<string>>>(startup, "records").Count);
            Equal("000001", Get<List<List<string>>>(startup, "records")[0][0]);
        });
        Test("Header CSV overwrite and F5 retain one header, and explicit headerless save removes it", () =>
        {
            CsvDataWriter.Write(path, fields, [["001", " 東京 "]], hasHeader: true);
            using var form = new MainForm(["--definition", definition, "--data", path, "--csv-header", "present"]);
            Get<List<List<string>>>(form, "records")[0][0] = "000002";
            Invoke(form, "SaveDataOverwrite");
            Equal(2, CsvDataReader.Read(path, fields).Count);
            Equal("番号", CsvDataReader.Read(path, fields)[0][0]);
            Equal("000002", CsvDataReader.Read(path, fields, hasHeader: true)[0][0]);
            CsvDataWriter.Write(path, fields, [["000003", " 次 "]], hasHeader: true);
            Invoke(form, "ReloadCurrentFilesCore");
            Equal(1, Get<List<List<string>>>(form, "records").Count);
            Equal("000003", Get<List<List<string>>>(form, "records")[0][0]);
            Equal(true, ((DataSaveOptions)Invoke(form, "CurrentSaveOptions")!).CsvHasHeader);
            Invoke(form, "WriteData", path, new DataSaveOptions(DataFileFormat.Csv, CsvTextEncoding.Utf8,
                RecordSeparatorMode.CrLfOrLf, NationalTextEncoding.ShiftJis));
            Equal(1, CsvDataReader.Read(path, fields).Count);
            Equal(false, Get<bool>(form, "currentCsvHasHeader"));
        });
        Test("Canceled or failed header changes retain current records, mode, path and remembered settings", () =>
        {
            CsvDataWriter.Write(path, fields, [["001", "東京"]], hasHeader: true);
            var store = new DataLoadSettingsStore(Path.Combine(temporaryDirectory, "header-cancel-settings.json"));
            store.Remember(definition, path, new DataLoadSettings(DataFileFormat.Csv, CsvTextEncoding.Utf8,
                RecordSeparatorMode.CrLfOrLf, NationalTextEncoding.ShiftJis, CsvHasHeader: true));
            using var form = new MainForm(["--definition", definition, "--data", path, "--csv-header", "present"], store);
            WithDialog("データ読込条件", DialogResult.Cancel, () => Invoke(form, "LoadDataFromPath", path, "読込完了"), dialog =>
                Descendants(dialog).OfType<ComboBox>().Single(c => c.Name == "DataFormat").SelectedIndex = 1);
            Equal(true, Get<bool>(form, "currentCsvHasHeader"));
            var bad = Path.Combine(temporaryDirectory, "bad-header.csv");
            File.WriteAllText(bad, "too,few,columns\n001,東京");
            Throws<TargetInvocationException>(() => Invoke(form, "LoadCsvData", bad, CsvTextEncoding.Utf8, true));
            Equal("001", Get<List<List<string>>>(form, "records")[0][0]);
            Equal(path, Get<string>(form, "saveDataPath"));
            Equal(true, store.Find(definition, path)!.CsvHasHeader);
        });
        Test("Older settings without header property default to headerless and header choice persists separately", () =>
        {
            var settingsPath = Path.Combine(temporaryDirectory, "legacy-header-settings.json");
            File.WriteAllText(settingsPath, System.Text.Json.JsonSerializer.Serialize(new[] { new {
                DefinitionPath = definition, DataPath = path, LayoutId = (string?)null,
                Settings = new { Format = 1, CsvEncoding = 0, SeparatorMode = 0, NationalEncoding = 0 }
            } }));
            var store = new DataLoadSettingsStore(settingsPath);
            Equal(false, store.Find(definition, path)!.CsvHasHeader);
            var updated = store.Find(definition, path)! with { CsvHasHeader = true };
            Equal(true, store.Remember(definition, path, updated));
            Equal(true, new DataLoadSettingsStore(settingsPath).Find(definition, path)!.CsvHasHeader);
        });
    }
}
