using System.Reflection;
using System.Text;
using FixedDataBuilder;

internal static partial class Program
{
    private static void RunReloadTests(string repo)
    {
        var definition = Path.Combine(temporaryDirectory, "reload-definition.csv");
        var data = Path.Combine(temporaryDirectory, "reload-data.dat");
        void Define(string name = "コード") => File.WriteAllText(definition, $"項目名,定義\r\n{name},X(6)\r\n", new UTF8Encoding(false));
        MainForm OpenFixed() => new(["--definition", definition, "--data", data, "--separator", "none", "--layout", "record"]);
        Test("F5 is wired to the file menu and no-file reload leaves the model empty", () =>
        {
            using var form = new MainForm();
            var menu = Get<MenuStrip>(form, "menuStrip").Items.OfType<ToolStripMenuItem>().First();
            var item = menu.DropDownItems.OfType<ToolStripMenuItem>().Single(item => item.Text == "再読み込み");
            Equal(Keys.F5, item.ShortcutKeys);
            item.PerformClick();
            Equal(0, Get<List<FieldDefinition>>(form, "fields").Count);
            Equal(0, Get<List<List<string>>>(form, "records").Count);
        });
        Test("Fixed reload updates definition and data while preserving read conditions and view", () =>
        {
            Define(); File.WriteAllText(data, "000001", Encoding.ASCII);
            using var form = OpenFixed();
            Get<List<List<string>>>(form, "records")[0][0] = "edited";
            Define("更新コード"); File.WriteAllText(data, "000002000003", Encoding.ASCII);
            Invoke(form, "ReloadCurrentFilesCore");
            Equal("更新コード", Get<List<FieldDefinition>>(form, "fields")[0].Name);
            Equal("000002", Get<List<List<string>>>(form, "records")[0][0]);
            Equal(2, Get<List<List<string>>>(form, "records").Count);
            Equal(RecordSeparatorMode.None, Get<RecordSeparatorMode>(form, "currentSeparatorMode"));
            Equal(DataEncodingProfile.ShiftJis, Get<DataEncodingProfile>(form, "currentDataEncodingProfile"));
            Equal(true, Get<ToolStripMenuItem>(form, "recordRowsButton").Checked);
        });
        Test("CSV reload preserves selected encoding, spaces, empty values and hidden fields", () =>
        {
            foreach (var encoding in new[] { CsvTextEncoding.Utf8, CsvTextEncoding.ShiftJis })
            {
                Define();
                using var form = new MainForm(["--definition", definition]);
                var codec = encoding == CsvTextEncoding.Utf8 ? new UTF8Encoding(false) : Encoding.GetEncoding("shift_jis");
                File.WriteAllText(data, "\"前\"\r\n", codec);
                Invoke(form, "LoadCsvData", data, encoding);
                Get<HashSet<int>>(form, "hiddenFieldIndexes").Add(0);
                File.WriteAllText(data, "\" あ　\"\r\n\"\"\r\n\"000001\"\r\n", codec);
                Invoke(form, "ReloadCurrentFilesCore");
                var records = Get<List<List<string>>>(form, "records");
                Equal(" あ　", records[0][0]); Equal("", records[1][0]); Equal("000001", records[2][0]);
                Equal(DataFileFormat.Csv, Get<DataFileFormat>(form, "currentDataFileFormat"));
                Equal(encoding, Get<CsvTextEncoding>(form, "currentCsvEncoding"));
                Equal(true, Get<HashSet<int>>(form, "hiddenFieldIndexes").Contains(0));
            }
        });
        Test("COPY reload retains UTF-8 and every national encoding with CRLF and no separators", () =>
        {
            foreach (var sample in new[] { "crlf-utf8-n-utf16le", "none-sjis-n-sjis", "none-utf8-n-utf16le", "none-utf8-n-utf32le" })
            {
                var source = Path.Combine(repo, "samples", "copybook-basic-data-" + sample + ".dat");
                File.Copy(source, data, true);
                var national = sample.Contains("sjis-n-sjis") ? "sjis" : sample.EndsWith("utf32le") ? "utf32" : "utf16";
                using var form = new MainForm(["--definition", Path.Combine(repo, "samples", "copybook-basic-definition.cbl"),
                    "--data", data, "--national-encoding", national, "--separator", sample.StartsWith("crlf") ? "crlf" : "none"]);
                var expected = string.Join("|", Get<List<List<string>>>(form, "records").SelectMany(row => row));
                var profile = Get<DataEncodingProfile>(form, "currentDataEncodingProfile");
                var nationalEncoding = Get<NationalTextEncoding>(form, "currentNationalTextEncoding");
                Get<List<List<string>>>(form, "records")[0][0] = "編集";
                Invoke(form, "ReloadCurrentFilesCore");
                Equal(expected, string.Join("|", Get<List<List<string>>>(form, "records").SelectMany(row => row)));
                Equal(profile, Get<DataEncodingProfile>(form, "currentDataEncodingProfile"));
                Equal(nationalEncoding, Get<NationalTextEncoding>(form, "currentNationalTextEncoding"));
            }
        });
        Test("Failed or missing reload leaves edited data, definition, paths and conditions intact", () =>
        {
            Define(); File.WriteAllText(data, "000001", Encoding.ASCII);
            using var form = OpenFixed();
            var originalFields = Get<List<FieldDefinition>>(form, "fields").ToArray();
            Get<List<List<string>>>(form, "records")[0][0] = "edited";
            Define("適用しない定義");
            File.WriteAllText(data, "bad", Encoding.ASCII);
            Throws<TargetInvocationException>(() => Invoke(form, "ReloadCurrentFilesCore"));
            File.Delete(data);
            Throws<TargetInvocationException>(() => Invoke(form, "ReloadCurrentFilesCore"));
            File.WriteAllText(definition, "壊れた定義", new UTF8Encoding(false));
            Throws<TargetInvocationException>(() => Invoke(form, "ReloadCurrentFilesCore"));
            Equal(true, originalFields.SequenceEqual(Get<List<FieldDefinition>>(form, "fields")));
            Equal("edited", Get<List<List<string>>>(form, "records")[0][0]);
            Equal(data, Get<TextBox>(form, "dataPathTextBox").Text);
            Equal(data, Get<string>(form, "saveDataPath"));
            Equal(RecordSeparatorMode.None, Get<RecordSeparatorMode>(form, "currentSeparatorMode"));
        });
        Test("Definition-only reload refreshes fields and clears unsaved records", () =>
        {
            Define(); using var form = new MainForm(["--definition", definition]);
            Get<List<List<string>>>(form, "records")[0][0] = "edited";
            File.AppendAllText(definition, "追加項目,X(2)\r\n", new UTF8Encoding(false));
            Invoke(form, "ReloadCurrentFilesCore");
            Equal(2, Get<List<FieldDefinition>>(form, "fields").Count);
            Equal(1, Get<List<List<string>>>(form, "records").Count);
            Equal("", Get<List<List<string>>>(form, "records")[0][0]);
        });
        Test("Reload follows Save As destination and its CSV format", () =>
        {
            Define(); File.WriteAllText(data, "000001", Encoding.ASCII);
            using var form = OpenFixed();
            var target = Path.Combine(temporaryDirectory, "reload-saved.csv");
            Invoke(form, "WriteData", target, new DataSaveOptions(DataFileFormat.Csv, CsvTextEncoding.Utf8, RecordSeparatorMode.None, NationalTextEncoding.ShiftJis));
            File.WriteAllText(target, "\"000099\"\r\n", new UTF8Encoding(false));
            Invoke(form, "ReloadCurrentFilesCore");
            Equal("000099", Get<List<List<string>>>(form, "records")[0][0]);
            Equal(target, Get<string>(form, "saveDataPath"));
        });
        Test("Excel reload reads workbook values and XML reload keeps the selected layout", () =>
        {
            using var form = CreateSampleForm(repo);
            var workbook = Path.Combine(temporaryDirectory, "reload.xlsx");
            ExcelExporter.Write(workbook, "項目縦", (IReadOnlyList<IReadOnlyList<ExcelCell>>)Invoke(form, "BuildExcelRows")!);
            Invoke(form, "ReloadCurrentFilesCore");
            Equal("customer-fixed", Get<XmlLayoutDefinition>(form, "currentXmlLayout").Id);
            Invoke(form, "LoadExcel", workbook);
            var expected = Get<List<List<string>>>(form, "records")[0][0];
            Get<List<List<string>>>(form, "records")[0][0] = "edited";
            Invoke(form, "ReloadCurrentFilesCore");
            Equal(expected, Get<List<List<string>>>(form, "records")[0][0]);
        });
    }
}
