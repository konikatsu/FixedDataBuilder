using System.Text.Json;
using FixedDataBuilder;

internal static partial class Program
{
    private static void RunTestDataGenerationIntegrationTests(string repo)
    {
        Test("Generation opens a new document, preserves source baseline and applies CSV profile", () =>
        {
            using var form = CreateCellDisplayForm(recordRows: true);
            var rows = Get<List<List<string>>>(form, "records");
            var original = rows[0];
            var baseline = original.ToArray();
            var result = GenerationIntegrationResult(form);
            form.GetType().GetField("saveDataPath", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(form, "baseline.dat");
            Get<TextBox>(form, "dataPathTextBox").Text = "baseline.dat";
            Invoke(form, "ApplyGeneratedTestData", result);
            Equal(true, baseline.SequenceEqual(original));
            Equal<string?>(null, Get<string?>(form, "saveDataPath"));
            Equal("", Get<TextBox>(form, "dataPathTextBox").Text);
            Equal(true, Get<bool>(form, "hasUnsavedChanges"));
            Equal(DataFileFormat.Csv, Get<DataFileFormat>(form, "currentDataFileFormat"));
            Equal(true, Get<bool>(form, "currentCsvHasHeader"));
            Equal(false, ReferenceEquals(result.Records[0], rows[0]));
            Equal(true, Get<ToolStripMenuItem>(form, "generationReportMenu").Enabled);
            for (var row = 0; row < rows.Count; row++) Equal(baseline[0], rows[row][0]);
        });
        Test("Generation report tracks edits without claiming processing or validation success", () =>
        {
            using var form = CreateCellDisplayForm(recordRows: false);
            Invoke(form, "ApplyGeneratedTestData", GenerationIntegrationResult(form));
            using (var json = JsonDocument.Parse((string)Invoke(form, "BuildGenerationReportJson")!))
                Equal("generated_not_processed", json.RootElement.GetProperty("status").GetString());
            Get<List<List<string>>>(form, "records")[0][3] = "77";
            using var edited = JsonDocument.Parse((string)Invoke(form, "BuildGenerationReportJson")!);
            Equal("modified_after_generation_not_revalidated", edited.RootElement.GetProperty("status").GetString());
            var value = edited.RootElement.GetProperty("rows")[0].GetProperty("values")[3];
            Equal("77", value.GetProperty("currentValue").GetString());
            Equal(true, value.GetProperty("valueChanged").GetBoolean());
        });
        Test("Generation report survives view changes and detects row structure changes", () =>
        {
            using var form = CreateCellDisplayForm(recordRows: false);
            Invoke(form, "ApplyGeneratedTestData", GenerationIntegrationResult(form));
            Invoke(form, "SetSingleRecordView", true);
            Equal(true, (bool)Invoke(form, "HasCurrentGenerationReport")!);
            var rows = Get<List<List<string>>>(form, "records");
            rows.Add(rows[0].ToList());
            Invoke(form, "RefreshGrid");
            Equal(false, Get<ToolStripMenuItem>(form, "generationReportMenu").Enabled);
            Equal(false, (bool)Invoke(form, "HasCurrentGenerationReport")!);
        });
        Test("Definition edits invalidate generation correspondence even when row count stays equal", () =>
        {
            using var form = CreateCellDisplayForm(recordRows: true);
            Invoke(form, "ApplyGeneratedTestData", GenerationIntegrationResult(form));
            var fields = Get<List<FieldDefinition>>(form, "fields");
            fields[0] = fields[0] with { Comment = "changed" };
            Equal(false, (bool)Invoke(form, "HasCurrentGenerationReport")!);
        });
        Test("Generated data CSV save retains case correspondence and selected header option", () =>
        {
            using var form = CreateCellDisplayForm(recordRows: true);
            var result = GenerationIntegrationResult(form);
            Invoke(form, "ApplyGeneratedTestData", result);
            var path = Path.Combine(temporaryDirectory, "generated-integration.csv");
            Invoke(form, "WriteData", path, result.Settings.OutputOptions);
            Equal(false, Get<bool>(form, "hasUnsavedChanges"));
            Equal(true, (bool)Invoke(form, "HasCurrentGenerationReport")!);
            var loaded = CsvDataReader.Read(path, Get<List<FieldDefinition>>(form, "fields"), CsvTextEncoding.Utf8, true);
            Equal(result.Records.Count, loaded.Count);
            Equal(true, result.Records[0].SequenceEqual(loaded[0]));
        });
        Test("Fixed numeric display normalization without an edit retains the original generated value", () =>
        {
            foreach (var recordRows in new[] { false, true })
            {
                using var form = CreateCellDisplayForm(recordRows);
                var rows = Get<List<List<string>>>(form, "records");
                rows[0][3] = "7";
                Invoke(form, "RefreshGrid");
                Invoke(form, "SetUnsavedChanges", false);
                var row = recordRows ? (int)Invoke(form, "RecordRowsDataStartIndex")! : 3;
                var column = recordRows ? 4 : 2;
                Invoke(form, "UpdateRecordValue", row, column);
                Equal("7", rows[0][3]);
                Equal(false, Get<bool>(form, "hasUnsavedChanges"));
            }
        });
        Test("Generation report survives N encoding conversions that keep field and row correspondence", () =>
        {
            using var form = new MainForm();
            var fields = Get<List<FieldDefinition>>(form, "fields");
            fields.Add(new("N", FieldDataType.FullWidthText, 2));
            Get<List<List<string>>>(form, "records").Add(["漢字"]);
            Invoke(form, "RefreshGrid");
            var source = fields.ToArray();
            var options = new DataSaveOptions(DataFileFormat.FixedLength, CsvTextEncoding.Utf8,
                RecordSeparatorMode.None, NationalTextEncoding.Utf32, DataEncodingProfile.ShiftJis);
            var result = TestDataGenerator.Generate(fields, ["漢字"], new(0, [0], GenerationRequirement.TextMaximum, 1, options));
            // Saving the baseline from an unsaved-data prompt can change only its selected N width.
            Invoke(form, "ApplyNationalTextEncoding", NationalTextEncoding.Utf32);
            Invoke(form, "ApplyGeneratedTestDataCore", result, source);
            var path = Path.Combine(temporaryDirectory, "generated-n-utf16.dat");
            Invoke(form, "WriteData", path, options with { NationalEncoding = NationalTextEncoding.Utf16 });
            Equal(true, (bool)Invoke(form, "HasCurrentGenerationReport")!);
            using var json = JsonDocument.Parse((string)Invoke(form, "BuildGenerationReportJson")!);
            Equal("generated_not_processed", json.RootElement.GetProperty("status").GetString());
            Equal(4L, new FileInfo(path).Length);
        });
        Test("Generation supports more than 653 records in the field-rows grid", () =>
        {
            using var form = new MainForm();
            var fields = Get<List<FieldDefinition>>(form, "fields");
            fields.Add(new("TEXT", FieldDataType.Text, 1));
            Get<List<List<string>>>(form, "records").Add(["X"]);
            Invoke(form, "RefreshGrid");
            var result = TestDataGenerator.Generate(fields, ["X"], new(0, [0], GenerationRequirement.TextMaximum, 660,
                new(DataFileFormat.Csv, CsvTextEncoding.Utf8, RecordSeparatorMode.CrLfOrLf, NationalTextEncoding.ShiftJis)));
            Invoke(form, "ApplyGeneratedTestData", result);
            Equal(662, Get<DataGridView>(form, "grid").ColumnCount);
            Equal(true, Get<bool>(form, "hasUnsavedChanges"));
        });
        Test("Generation rolls back the document and report if grid construction fails", () =>
        {
            using var form = CreateCellDisplayForm(recordRows: true);
            var rows = Get<List<List<string>>>(form, "records");
            var original = rows.ToArray();
            var result = GenerationIntegrationResult(form);
            var failOnce = true;
            Get<DataGridView>(form, "grid").ColumnAdded += (_, _) =>
            {
                if (!failOnce) return;
                failOnce = false;
                throw new InvalidOperationException("Simulated column construction failure");
            };
            Throws<System.Reflection.TargetInvocationException>(() => Invoke(form, "ApplyGeneratedTestData", result));
            Equal(original.Length, rows.Count);
            Equal(true, original.Zip(rows).All(pair => ReferenceEquals(pair.First, pair.Second)));
            Equal(false, Get<bool>(form, "hasUnsavedChanges"));
            Equal(false, (bool)Invoke(form, "HasCurrentGenerationReport")!);
        });
        Test("Generation report streams valid JSON and refuses to overwrite the saved data", () =>
        {
            using var form = CreateCellDisplayForm(recordRows: true);
            var result = GenerationIntegrationResult(form);
            Invoke(form, "ApplyGeneratedTestData", result);
            var dataPath = Path.Combine(temporaryDirectory, "report-protected-data.csv");
            Invoke(form, "WriteData", dataPath, result.Settings.OutputOptions);
            var before = File.ReadAllBytes(dataPath);
            Throws<System.Reflection.TargetInvocationException>(() => Invoke(form, "WriteGenerationReport", dataPath));
            Equal(true, before.SequenceEqual(File.ReadAllBytes(dataPath)));
            var path = Path.Combine(temporaryDirectory, "report-streamed.json");
            Invoke(form, "WriteGenerationReport", path);
            using var json = JsonDocument.Parse(File.ReadAllBytes(path));
            Equal(result.Records.Count, json.RootElement.GetProperty("rows").GetArrayLength());
            Equal("generated_not_processed", json.RootElement.GetProperty("status").GetString());
        });
        Test("Generation controls require source records and toolbar can wrap at narrow widths", () =>
        {
            using var form = new MainForm();
            Invoke(form, "UpdateGenerationControls");
            Equal(false, Get<Button>(form, "generateTestDataButton").Enabled);
            var panel = form.Controls.OfType<FlowLayoutPanel>().Single(control => control.Name == "ViewModePanel");
            Equal(true, panel.WrapContents);
        });
    }

    private static TestDataGenerationResult GenerationIntegrationResult(MainForm form)
    {
        var fields = Get<List<FieldDefinition>>(form, "fields");
        var baseline = Get<List<List<string>>>(form, "records")[0];
        return TestDataGenerator.Generate(fields, baseline, new TestDataGenerationSettings(0, [3],
            GenerationRequirement.NumericLeadingZero | GenerationRequirement.NumericAllZero
                | GenerationRequirement.NumericFullLength | GenerationRequirement.NumericMaximum, 4,
            new DataSaveOptions(DataFileFormat.Csv, CsvTextEncoding.Utf8, RecordSeparatorMode.CrLfOrLf,
                NationalTextEncoding.ShiftJis, DataEncodingProfile.ShiftJis, true)));
    }
}
