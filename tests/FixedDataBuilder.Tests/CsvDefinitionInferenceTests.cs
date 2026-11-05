using System.Reflection;
using System.Text;
using FixedDataBuilder;

internal static partial class Program
{
    private static void RunCsvDefinitionInferenceTests(string repo)
    {
        var source = Path.Combine(temporaryDirectory, "reverse-source.csv");
        var definition = Path.Combine(temporaryDirectory, "reverse-definition.csv");
        void Write(string text, CsvTextEncoding encoding = CsvTextEncoding.Utf8) =>
            File.WriteAllText(source, text, JapaneseEncodings.ForCsv(encoding));
        void SameValues(IReadOnlyList<List<string>> expected, IReadOnlyList<List<string>> actual)
        {
            Equal(expected.Count, actual.Count);
            for (var row = 0; row < expected.Count; row++)
                Equal(true, expected[row].SequenceEqual(actual[row]));
        }
        void WriteDefinition(CsvDefinitionInference inference) => DefinitionCsvWriter.Write(definition,
            inference.Fields.Select(field => new DefinitionCsvRow(field.Name, field.DisplayDefinition, field.Comment)).ToArray());

        Test("Definition-free CSV parser preserves quoted delimiters, quotes, line breaks and empty trailing columns", () =>
        {
            Write("\" 001 \",\"東京,中央\",\"引用\"\"符\r\n次行\",\r\n\"\",\"\",\"\",\"\"");
            var rows = CsvDataReader.ReadRows(source);
            Equal(2, rows.Count);
            Equal(4, rows[0].Count);
            Equal(" 001 ", rows[0][0]);
            Equal("東京,中央", rows[0][1]);
            Equal("引用\"符\r\n次行", rows[0][2]);
            Equal("", rows[0][3]);
            Equal(true, rows[1].All(value => value == ""));
        });
        Test("Definition-free CSV rejects inconsistent column counts, malformed quoting and empty files", () =>
        {
            foreach (var text in new[] { "", "1,2\r\n3", "1,2\r\n3,4,5", "1,\"未完", "1,未\"完", "1,\"完\"続" })
            {
                Write(text);
                Throws<InvalidDataException>(() => CsvDataReader.ReadRows(source));
            }
        });
        Test("CSV inference measures every data row using UTF-8 byte lengths and ignores syntax bytes", () =>
        {
            File.WriteAllText(source, "番号,内容\r\n01,A\r\n02,\"東京,\"\"中央\"\"\"\r\n", new UTF8Encoding(true));
            var analysis = CsvDefinitionInferer.Analyze(source);
            Equal(CsvTextEncoding.Utf8, analysis.Encoding);
            var result = analysis.CreateDefinition(true);
            Equal(2, result.Records.Count);
            Equal("番号", result.Fields[0].Name);
            Equal(2, result.Fields[0].Length);
            Equal(Encoding.UTF8.GetByteCount("東京,\"中央\""), result.Fields[1].Length);
            Equal(result.Fields[1].Length, result.Columns[1].MaximumByteLength);
            Equal("東京,\"中央\"", result.Records[1][1]);
        });
        Test("CSV inference supports explicit SJIS and MS932 plus automatic Japanese encoding candidates", () =>
        {
            foreach (var encoding in new[] { CsvTextEncoding.StrictShiftJis, CsvTextEncoding.ShiftJis, CsvTextEncoding.Utf8 })
            {
                Write("名称,値\r\n東京,0001\r\n", encoding);
                var result = CsvDefinitionInferer.Analyze(source, encoding).CreateDefinition(true);
                Equal(encoding, result.Encoding);
                Equal(JapaneseEncodings.ForCsv(encoding).GetByteCount("東京"), result.Fields[0].Length);
                Equal("東京", result.Records[0][0]);
                Equal("0001", result.Records[0][1]);
                Equal("東京", CsvDefinitionInferer.Analyze(source).CreateDefinition(true).Records[0][0]);
            }
            Write("名前\r\n髙橋\r\n", CsvTextEncoding.ShiftJis);
            Equal("髙橋", CsvDefinitionInferer.Analyze(source, CsvTextEncoding.ShiftJis).CreateDefinition(true).Records[0][0]);
            Throws<DecoderFallbackException>(() => CsvDefinitionInferer.Analyze(source, CsvTextEncoding.Utf8));
        });
        Test("CSV inference keeps numeric-looking data as text including leading zeros and edge spaces", () =>
        {
            Write("001, 02 ,1.20,-3,\r\n002, 00 ,0.00,+4,\r\n");
            var result = CsvDefinitionInferer.Analyze(source).CreateDefinition(false);
            Equal(true, result.Fields.All(field => field.Type == FieldDataType.Text));
            Equal("001", result.Records[0][0]);
            Equal(" 02 ", result.Records[0][1]);
            Equal("1.20", result.Records[0][2]);
            Equal("-3", result.Records[0][3]);
            Equal(true, result.Columns[0].IsDigitsOnly);
            Equal(false, result.Columns[1].IsDigitsOnly);
            Equal(false, result.Columns[2].IsDigitsOnly);
            Equal(false, result.Columns[4].IsDigitsOnly);
        });
        Test("CSV inference assigns positive widths to entirely empty columns without fabricating values", () =>
        {
            Write(",,\r\n,,\r\n");
            var result = CsvDefinitionInferer.Analyze(source).CreateDefinition(false);
            Equal(3, result.Fields.Count);
            Equal(true, result.Fields.All(field => field.Length == 1));
            Equal(true, result.Columns.All(column => column.MaximumByteLength == 0));
            Equal(true, result.Records.All(row => row.All(value => value == "")));
            Equal(3, result.Fields.Select(field => field.Name).Distinct().Count());
        });
        Test("Explicit header selection changes names and row count while no-header keeps the first row", () =>
        {
            Write("コード,名前\r\n001,山田\r\n002,佐藤\r\n");
            var analysis = CsvDefinitionInferer.Analyze(source);
            var withHeader = analysis.CreateDefinition(true);
            var withoutHeader = analysis.CreateDefinition(false);
            Equal(true, withHeader.HasHeader);
            Equal(false, withoutHeader.HasHeader);
            Equal(2, withHeader.Records.Count);
            Equal(3, withoutHeader.Records.Count);
            Equal("名前", withHeader.Fields[1].Name);
            Equal("コード", withoutHeader.Records[0][0]);
            Equal("名前", withoutHeader.Records[0][1]);
            Equal(false, withoutHeader.Fields.Any(field => field.Name == "名前"));
        });
        Test("Blank and repeated CSV headers produce nonblank unique definition names in original column order", () =>
        {
            Write("名称,,名称,名称_2\r\nA,B,C,D\r\n");
            var result = CsvDefinitionInferer.Analyze(source).CreateDefinition(true);
            Equal(4, result.Fields.Count);
            Equal(true, result.Fields.All(field => !string.IsNullOrWhiteSpace(field.Name)));
            Equal(4, result.Fields.Select(field => field.Name).Distinct(StringComparer.Ordinal).Count());
            Equal("名称", result.Fields[0].Name);
            Equal(true, result.Records[0].SequenceEqual(new[] { "A", "B", "C", "D" }));
        });
        Test("Header-only CSV cannot discard its only record but can be loaded as one data record", () =>
        {
            Write("列A,列B\r\n");
            var analysis = CsvDefinitionInferer.Analyze(source);
            Throws<InvalidDataException>(() => analysis.CreateDefinition(true));
            Equal(1, analysis.CreateDefinition(false).Records.Count);
        });
        Test("Reverse-generated definitions reload and read every CSV value without normalizing content", () =>
        {
            Write("識別,内容,空欄\r\n001,\" 前後　 \r\n改行\",\r\n002,\"a,\"\"b\"\"\",\r\n");
            var result = CsvDefinitionInferer.Analyze(source).CreateDefinition(true);
            WriteDefinition(result);
            var fields = DefinitionCsvReader.Read(definition);
            Equal(true, result.Fields.SequenceEqual(fields));
            SameValues(result.Records, CsvDataReader.Read(source, fields, result.Encoding, true));
            var output = Path.Combine(temporaryDirectory, "reverse-output.csv");
            CsvDataWriter.Write(output, fields, result.Records, result.Encoding, true);
            SameValues(result.Records, CsvDataReader.Read(output, fields, result.Encoding, true));
        });
        Test("Applying inferred CSV replaces the complete Main model and keeps the source file untouched", () =>
        {
            Write("番号,名前\r\n001, 東京 \r\n002,大阪\r\n");
            var bytes = File.ReadAllBytes(source);
            var result = CsvDefinitionInferer.Analyze(source).CreateDefinition(true);
            using var form = CreateBasicForm(repo);
            Invoke(form, "ApplyInferredCsv", source, result);
            Equal(true, result.Fields.SequenceEqual(Get<List<FieldDefinition>>(form, "fields")));
            SameValues(result.Records, Get<List<List<string>>>(form, "records"));
            Equal(DataFileFormat.Csv, Get<DataFileFormat>(form, "currentDataFileFormat"));
            Equal(result.Encoding, Get<CsvTextEncoding>(form, "currentCsvEncoding"));
            Equal(true, Get<bool>(form, "currentCsvHasHeader"));
            Equal(source, Get<string>(form, "saveDataPath"));
            Equal(true, bytes.SequenceEqual(File.ReadAllBytes(source)));
        });
        Test("Saving inferred definition preserves edited data and unsaved state without rereading CSV", () =>
        {
            Write("番号,名前\r\n001,東京\r\n");
            var bytes = File.ReadAllBytes(source);
            using var form = new MainForm();
            Invoke(form, "ApplyInferredCsv", source, CsvDefinitionInferer.Analyze(source).CreateDefinition(true));
            Get<List<List<string>>>(form, "records")[0][1] = " 大阪 ";
            Invoke(form, "SetUnsavedChanges", true);
            Invoke(form, "SaveCurrentDefinitionToPath", definition, DefinitionTextEncoding.Utf8);
            Equal(" 大阪 ", Get<List<List<string>>>(form, "records")[0][1]);
            Equal(true, Get<bool>(form, "hasUnsavedChanges"));
            Equal(source, Get<string>(form, "saveDataPath"));
            Equal(definition, Invoke(form, "GetCurrentDefinitionPath"));
            Equal(2, DefinitionCsvReader.Read(definition).Count);
            Equal(true, bytes.SequenceEqual(File.ReadAllBytes(source)));
        });
        Test("Reverse definition export rejects overwriting current CSV and leaves both model and bytes intact", () =>
        {
            Write("001, 東京 \r\n");
            var bytes = File.ReadAllBytes(source);
            using var form = new MainForm();
            Invoke(form, "ApplyInferredCsv", source, CsvDefinitionInferer.Analyze(source).CreateDefinition(false));
            var oldDefinitionPath = Invoke(form, "GetCurrentDefinitionPath");
            var fields = Get<List<FieldDefinition>>(form, "fields").ToArray();
            Throws<TargetInvocationException>(() => Invoke(form, "SaveCurrentDefinitionToPath", source, DefinitionTextEncoding.Utf8));
            Equal(oldDefinitionPath, Invoke(form, "GetCurrentDefinitionPath"));
            Equal(true, fields.SequenceEqual(Get<List<FieldDefinition>>(form, "fields")));
            Equal(true, bytes.SequenceEqual(File.ReadAllBytes(source)));
            Equal(" 東京 ", Get<List<List<string>>>(form, "records")[0][1]);
        });
        Test("Failed definition encoding conversion retains an existing file and inferred document state", () =>
        {
            Write("名前😀\r\n東京\r\n");
            using var form = new MainForm();
            Invoke(form, "ApplyInferredCsv", source, CsvDefinitionInferer.Analyze(source).CreateDefinition(true));
            File.WriteAllText(definition, "既存の定義", new UTF8Encoding(false));
            var previous = File.ReadAllBytes(definition);
            var oldDefinitionPath = Invoke(form, "GetCurrentDefinitionPath");
            Throws<TargetInvocationException>(() => Invoke(form, "SaveCurrentDefinitionToPath", definition, DefinitionTextEncoding.Ms932));
            Equal(true, previous.SequenceEqual(File.ReadAllBytes(definition)));
            Equal(oldDefinitionPath, Invoke(form, "GetCurrentDefinitionPath"));
            Equal("東京", Get<List<List<string>>>(form, "records")[0][0]);
        });
        Test("F5 for an unsaved inferred definition retains header choice and updates observed lengths atomically", () =>
        {
            Write("番号,名前\r\n001,東京\r\n");
            using var form = new MainForm();
            Invoke(form, "ApplyInferredCsv", source, CsvDefinitionInferer.Analyze(source).CreateDefinition(true));
            Equal(null, Invoke(form, "GetCurrentDefinitionPath"));
            Write("番号,名前\r\n001,東京都新宿区\r\n002,大阪\r\n");
            Invoke(form, "ReloadCurrentFilesCore");
            Equal(2, Get<List<List<string>>>(form, "records").Count);
            Equal("東京都新宿区", Get<List<List<string>>>(form, "records")[0][1]);
            Equal(Encoding.UTF8.GetByteCount("東京都新宿区"), Get<List<FieldDefinition>>(form, "fields")[1].Length);
            Equal(true, Get<bool>(form, "currentCsvHasHeader"));
            var fields = Get<List<FieldDefinition>>(form, "fields").ToArray();
            var values = Get<List<List<string>>>(form, "records").Select(row => row.ToList()).ToList();
            Write("番号,名前\r\n001\r\n");
            Throws<TargetInvocationException>(() => Invoke(form, "ReloadCurrentFilesCore"));
            Equal(true, fields.SequenceEqual(Get<List<FieldDefinition>>(form, "fields")));
            SameValues(values, Get<List<List<string>>>(form, "records"));
        });
        Test("F5 after reverse definition export reads the saved definition and CSV with their original formats", () =>
        {
            Write("番号,名前\r\n001,東京\r\n");
            using var form = new MainForm();
            Invoke(form, "ApplyInferredCsv", source, CsvDefinitionInferer.Analyze(source).CreateDefinition(true));
            Invoke(form, "SaveCurrentDefinitionToPath", definition, DefinitionTextEncoding.Utf8);
            var savedFields = DefinitionCsvReader.Read(definition);
            Write("番号,名前\r\n002,大阪\r\n");
            Invoke(form, "ReloadCurrentFilesCore");
            Equal(definition, Invoke(form, "GetCurrentDefinitionPath"));
            Equal(true, savedFields.SequenceEqual(Get<List<FieldDefinition>>(form, "fields")));
            Equal("002", Get<List<List<string>>>(form, "records")[0][0]);
            Equal("大阪", Get<List<List<string>>>(form, "records")[0][1]);
            Equal(DataFileFormat.Csv, Get<DataFileFormat>(form, "currentDataFileFormat"));
            Equal(CsvTextEncoding.Utf8, Get<CsvTextEncoding>(form, "currentCsvEncoding"));
            Equal(true, Get<bool>(form, "currentCsvHasHeader"));
        });
        Test("Header inference suggests label-to-digit transitions while keeping ambiguous all-text rows as data", () =>
        {
            Write("コード,名前\r\n001,東京\r\n002,大阪\r\n");
            var labeled = CsvDefinitionInferer.Analyze(source);
            Equal(true, labeled.SuggestedHasHeader);
            Equal(false, string.IsNullOrWhiteSpace(labeled.HeaderReason));
            Write("山田,東京\r\n佐藤,大阪\r\n");
            var ambiguous = CsvDefinitionInferer.Analyze(source);
            Equal(false, ambiguous.SuggestedHasHeader);
            Equal(2, ambiguous.CreateDefinition(ambiguous.SuggestedHasHeader).Records.Count);
            Write("001,東京\r\n002,大阪\r\n");
            Equal(false, CsvDefinitionInferer.Analyze(source).SuggestedHasHeader);
        });
        Test("CSV inference dialog previews header changes but commits the inference only on import confirmation", () =>
        {
            Write("コード,名前\r\n001, 東京 \r\n002,大阪\r\n");
            using var dialog = new CsvDefinitionImportForm(source);
            var header = Descendants(dialog).OfType<CheckBox>().Single(control => control.Name == "CsvInferenceHasHeader");
            var import = Descendants(dialog).OfType<Button>().Single(control => control.Name == "CsvInferenceImport");
            var definitions = Descendants(dialog).OfType<DataGridView>().Single(control => control.Name == "CsvInferenceDefinitions");
            var preview = Descendants(dialog).OfType<DataGridView>().Single(control => control.Name == "CsvInferencePreview");
            Equal(true, header.Checked);
            Equal(true, import.Enabled);
            Equal(null, dialog.Inference);
            Equal(3, preview.Rows.Count);
            Equal("見出し (1)", preview[0, 0].Value);
            Equal("コード", definitions["FieldName", 0].Value);
            Equal(import, dialog.AcceptButton);
            header.Checked = false;
            Equal(null, dialog.Inference);
            Equal("1", preview[0, 0].Value);
            Equal("項目1", definitions["FieldName", 0].Value);
            header.Checked = true;
            Equal(null, dialog.Inference);
            Invoke(dialog, "ConfirmImport");
            var committed = dialog.Inference ?? throw new Exception("Import did not commit the inference.");
            Equal(DialogResult.OK, dialog.DialogResult);
            Equal(true, committed.HasHeader);
            Equal(2, committed.Records.Count);
            Equal("001", committed.Records[0][0]);
            Equal(" 東京 ", committed.Records[0][1]);
        });
        Test("Malformed CSV shows a persistent red error and cannot commit an inference", () =>
        {
            Write("コード,名前\r\n001,\"未完\r\n");
            using var dialog = new CsvDefinitionImportForm(source);
            var error = Descendants(dialog).OfType<Label>().Single(control => control.Name == "CsvInferenceError");
            var import = Descendants(dialog).OfType<Button>().Single(control => control.Name == "CsvInferenceImport");
            Equal(Color.Firebrick, error.ForeColor);
            Equal(false, string.IsNullOrWhiteSpace(error.Text));
            Equal(false, import.Enabled);
            Equal(null, dialog.Inference);
            Invoke(dialog, "ConfirmImport");
            Equal(null, dialog.Inference);
            Equal(DialogResult.None, dialog.DialogResult);
        });
        Test("Selecting a sole CSV row as header disables import while undoing that choice restores a valid candidate", () =>
        {
            Write("列A,列B\r\n");
            using var dialog = new CsvDefinitionImportForm(source);
            var header = Descendants(dialog).OfType<CheckBox>().Single(control => control.Name == "CsvInferenceHasHeader");
            var import = Descendants(dialog).OfType<Button>().Single(control => control.Name == "CsvInferenceImport");
            var error = Descendants(dialog).OfType<Label>().Single(control => control.Name == "CsvInferenceError");
            var preview = Descendants(dialog).OfType<DataGridView>().Single(control => control.Name == "CsvInferencePreview");
            Equal(false, header.Checked);
            Equal(true, import.Enabled);
            header.Checked = true;
            Equal(false, import.Enabled);
            Equal(Color.Firebrick, error.ForeColor);
            Equal(false, string.IsNullOrWhiteSpace(error.Text));
            Equal(1, preview.Rows.Count);
            Equal("列A", preview[1, 0].Value);
            Invoke(dialog, "ConfirmImport");
            Equal(null, dialog.Inference);
            header.Checked = false;
            Equal(true, import.Enabled);
            Equal("", error.Text);
            Equal(null, dialog.Inference);
            Invoke(dialog, "ConfirmImport");
            var committed = dialog.Inference ?? throw new Exception("Restored candidate did not commit.");
            Equal(false, committed.HasHeader);
            Equal(1, committed.Records.Count);
            Equal("列A", committed.Records[0][0]);
        });
    }
}
