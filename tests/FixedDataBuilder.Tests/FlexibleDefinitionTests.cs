using System.Reflection;
using System.Text;
using FixedDataBuilder;

internal static partial class Program
{
    private static void RunFlexibleDefinitionTests(string repo)
    {
        var source = Path.Combine(temporaryDirectory, "flexible-data.dat");
        var definition = Path.Combine(temporaryDirectory, "flexible-definition.csv");
        var saved = Path.Combine(temporaryDirectory, "flexible-saved-definition.csv");
        FieldDefinition[] Fields() => [new("コード", FieldDataType.Text, 4, "X(4)"), new("数量", FieldDataType.PlainNumber, 4, "9(4)")];
        void Prepare() { File.WriteAllText(source, "ABCD0007", Encoding.ASCII); DefinitionCsvWriter.Write(definition, [new("コード", "X(4)"), new("数量", "9(4)")]); }
        MainForm OpenCsv()
        {
            File.WriteAllText(source, "\"001\",\" 東京 \"\r\n", new UTF8Encoding(false));
            var form = new MainForm();
            Invoke(form, "ApplyInferredCsv", source, CsvDefinitionInferer.Analyze(source).CreateDefinition(false));
            return form;
        }
        DataSaveOptions FixedOptions() => new(DataFileFormat.FixedLength, CsvTextEncoding.Utf8,
            RecordSeparatorMode.CrLfOrLf, NationalTextEncoding.ShiftJis, DataEncodingProfile.ShiftJis);

        Test("A fixed data file can be selected before its definition without inventing fields or changing bytes", () =>
        {
            Prepare(); var bytes = File.ReadAllBytes(source);
            using var form = new MainForm();
            Invoke(form, "LoadDataFromPath", source, "loaded");
            Equal(source, Get<string?>(form, "pendingDataPath"));
            Equal(source, Get<TextBox>(form, "dataPathTextBox").Text);
            Equal(0, Get<List<FieldDefinition>>(form, "fields").Count);
            Equal(0, Get<List<List<string>>>(form, "records").Count);
            Equal(null, Get<string?>(form, "saveDataPath"));
            Equal(true, bytes.SequenceEqual(File.ReadAllBytes(source)));
        });
        Test("Data-first definition loading uses prospective fields in the options preview and loads both files", () =>
        {
            Prepare(); using var form = new MainForm();
            Invoke(form, "StageDataBeforeDefinition", source);
            WithDialog("データ読込条件", DialogResult.OK, () =>
                Equal(true, Invoke(form, "LoadDefinitionFromPath", definition, "loaded")));
            Equal(null, Get<string?>(form, "pendingDataPath"));
            Equal(definition, Invoke(form, "GetCurrentDefinitionPath"));
            Equal("ABCD", Get<List<List<string>>>(form, "records")[0][0]);
            Equal("0007", Get<List<List<string>>>(form, "records")[0][1]);
            Equal(source, Get<string?>(form, "saveDataPath"));
        });
        Test("Cancelling data-first read options retains the staged path and does not apply the prospective definition", () =>
        {
            Prepare(); using var form = new MainForm();
            Invoke(form, "StageDataBeforeDefinition", source);
            WithDialog("データ読込条件", DialogResult.Cancel, () =>
                Equal(false, Invoke(form, "LoadDefinitionFromPath", definition, "loaded")));
            Equal(source, Get<string?>(form, "pendingDataPath"));
            Equal(0, Get<List<FieldDefinition>>(form, "fields").Count);
            Equal(0, Get<List<List<string>>>(form, "records").Count);
        });
        Test("Failed combined definition and data parsing preserves the staged state", () =>
        {
            Prepare(); File.WriteAllText(source, "bad", Encoding.ASCII);
            using var form = new MainForm(); Invoke(form, "StageDataBeforeDefinition", source);
            Throws<TargetInvocationException>(() => Invoke(form, "ApplyDataAndDefinition", definition, Fields(),
                DefinitionTextEncoding.Utf8, source, FixedOptions()));
            Equal(source, Get<string?>(form, "pendingDataPath"));
            Equal(0, Get<List<FieldDefinition>>(form, "fields").Count);
        });
        Test("Definition-first and data-first fixed reading yield the same logical values and profiles", () =>
        {
            Prepare(); using var first = new MainForm(["--definition", definition, "--data", source]);
            using var second = new MainForm();
            Invoke(second, "ApplyDataAndDefinition", definition, Fields(), DefinitionTextEncoding.Utf8, source, FixedOptions());
            Equal(true, Get<List<List<string>>>(first, "records")[0].SequenceEqual(Get<List<List<string>>>(second, "records")[0]));
            Equal(Get<DataEncodingProfile>(first, "currentDataEncodingProfile"), Get<DataEncodingProfile>(second, "currentDataEncodingProfile"));
        });
        Test("Applying a real definition after inferred CSV preserves leading zeros, spaces, data path and selected CSV codec", () =>
        {
            using var form = OpenCsv();
            var next = new[] { new FieldDefinition("識別", FieldDataType.Text, 10, "X(10)"), new FieldDefinition("名称", FieldDataType.Text, 30, "X(30)") };
            Invoke(form, "ApplyDefinitionPreservingValues", definition, next, DefinitionTextEncoding.Utf8);
            Equal("001", Get<List<List<string>>>(form, "records")[0][0]);
            Equal(" 東京 ", Get<List<List<string>>>(form, "records")[0][1]);
            Equal(source, Get<string?>(form, "saveDataPath"));
            Equal(DataFileFormat.Csv, Get<DataFileFormat>(form, "currentDataFileFormat"));
            Equal(CsvTextEncoding.Utf8, Get<CsvTextEncoding>(form, "currentCsvEncoding"));
            Equal("識別", Get<List<FieldDefinition>>(form, "fields")[0].Name);
        });
        Test("A definition with mismatched physical columns is rejected without truncating data", () =>
        {
            using var form = OpenCsv(); var old = Get<List<FieldDefinition>>(form, "fields").ToArray();
            Throws<TargetInvocationException>(() => Invoke(form, "ApplyDefinitionPreservingValues", definition,
                new[] { new FieldDefinition("一列", FieldDataType.Text, 1, "X(1)") }, DefinitionTextEncoding.Utf8));
            Equal(true, old.SequenceEqual(Get<List<FieldDefinition>>(form, "fields")));
            Equal(2, Get<List<List<string>>>(form, "records")[0].Count);
            Equal(" 東京 ", Get<List<List<string>>>(form, "records")[0][1]);
        });
        Test("Live type and length edits retain invalid and overlong values so validation can report them", () =>
        {
            using var form = OpenCsv();
            Invoke(form, "ApplyEditedDefinition", new[] { new FieldDefinition("短い数字", FieldDataType.PlainNumber, 1, "9(1)"), new FieldDefinition("名称", FieldDataType.Text, 1, "X(1)", Comment: "期待値") });
            Equal("001", Get<List<List<string>>>(form, "records")[0][0]);
            Equal(" 東京 ", Get<List<List<string>>>(form, "records")[0][1]);
            Equal(true, Get<bool>(form, "hasDefinitionEdits"));
            Equal(false, Get<bool>(form, "hasInferredCsvDefinition"));
            Equal(null, Invoke(form, "GetCurrentDefinitionPath"));
            Equal(true, form.Text.Contains("定義未保存"));
        });
        Test("Saving a live definition preserves data dirty state and is readable in each supported encoding", () =>
        {
            foreach (var encoding in new[] { DefinitionTextEncoding.Utf8, DefinitionTextEncoding.ShiftJis, DefinitionTextEncoding.Ms932 })
            {
                using var form = OpenCsv(); var bytes = File.ReadAllBytes(source);
                var next = new[] { new FieldDefinition("識別", FieldDataType.Text, 6, "X(6)", Comment: "期待値は001"), new FieldDefinition("名称", FieldDataType.Text, 20, "X(20)") };
                Invoke(form, "SetUnsavedChanges", true);
                Invoke(form, "ApplyEditedDefinition", next);
                Invoke(form, "SaveCurrentDefinitionToPath", saved, encoding);
                Equal(false, Get<bool>(form, "hasDefinitionEdits"));
                Equal(true, Get<bool>(form, "hasUnsavedChanges"));
                Equal(saved, Invoke(form, "GetCurrentDefinitionPath"));
                Equal(true, next.SequenceEqual(DefinitionCsvReader.Read(saved, encoding)));
                Equal(" 東京 ", Get<List<List<string>>>(form, "records")[0][1]);
                Equal(true, bytes.SequenceEqual(File.ReadAllBytes(source)));
            }
        });
        Test("A failed live definition save keeps definition dirty and protects the current data file", () =>
        {
            using var form = OpenCsv(); var bytes = File.ReadAllBytes(source);
            Invoke(form, "ApplyEditedDefinition", Get<List<FieldDefinition>>(form, "fields").ToArray());
            Throws<TargetInvocationException>(() => Invoke(form, "SaveCurrentDefinitionToPath", source, DefinitionTextEncoding.Utf8));
            Equal(true, Get<bool>(form, "hasDefinitionEdits"));
            Equal(true, bytes.SequenceEqual(File.ReadAllBytes(source)));
        });
        Test("F5 reloads data against unsaved edited definitions without replacing those edits with inference", () =>
        {
            using var form = OpenCsv();
            var next = new[] { new FieldDefinition("編集名", FieldDataType.Text, 30, "X(30)"), new FieldDefinition("内容", FieldDataType.Text, 40, "X(40)") };
            Invoke(form, "ApplyEditedDefinition", next);
            File.WriteAllText(source, "\"002\",\"大阪\"\r\n", new UTF8Encoding(false));
            Invoke(form, "ReloadCurrentFilesCore");
            Equal(true, next.SequenceEqual(Get<List<FieldDefinition>>(form, "fields")));
            Equal(true, Get<bool>(form, "hasDefinitionEdits"));
            Equal("002", Get<List<List<string>>>(form, "records")[0][0]);
        });
        Test("The live editor applies names, number attributes, lengths and comments without writing a file", () =>
        {
            using var editor = new DefinitionEditorForm(Fields(), applyToData: true);
            var grid = Get<DataGridView>(editor, "grid");
            Equal(false, grid.AllowUserToAddRows); Equal(false, grid.AllowUserToDeleteRows);
            grid["Name", 0].Value = "更新コード";
            grid["IntegerDigits", 0].Value = "8";
            grid["Comment", 0].Value = "期待値001";
            Invoke(editor, "ApplyDefinitionEdits");
            var edited = editor.EditedFields ?? throw new Exception("Edited fields missing");
            Equal(DialogResult.OK, editor.DialogResult);
            Equal("更新コード", edited[0].Name); Equal(8, edited[0].Length); Equal("期待値001", edited[0].Comment);
            Equal(null, editor.SavedPath);
        });
        Test("Invalid live definition edits show a red explanation and do not return a partial definition", () =>
        {
            using var editor = new DefinitionEditorForm(Fields(), applyToData: true);
            Get<DataGridView>(editor, "grid")["IntegerDigits", 0].Value = "0";
            Invoke(editor, "ApplyDefinitionEdits");
            Equal(null, editor.EditedFields); Equal(DialogResult.None, editor.DialogResult);
            var status = Get<Label>(editor, "editStatus");
            Equal(Color.Firebrick, status.ForeColor); Equal(true, status.Text.Contains("適用できません"));
        });
        Test("Legacy half-width definitions retain their attribute through live edits and definition export", () =>
        {
            var initial = new FieldDefinition("英名", FieldDataType.HalfWidthText, 4, "文字_半角 4");
            using var editor = new DefinitionEditorForm([initial], applyToData: true);
            Get<DataGridView>(editor, "grid")["IntegerDigits", 0].Value = "8";
            Invoke(editor, "ApplyDefinitionEdits");
            var edited = editor.EditedFields ?? throw new Exception("Edited fields missing");
            DefinitionCsvWriter.Write(saved, edited.Select(field => new DefinitionCsvRow(field.Name, field.DisplayDefinition, field.Comment)).ToArray());
            Equal(FieldDataType.HalfWidthText, DefinitionCsvReader.Read(saved)[0].Type); Equal(8, DefinitionCsvReader.Read(saved)[0].Length);
        });
        Test("Live definitions preserve national width and REDEFINES metadata", () =>
        {
            using var editor = new DefinitionEditorForm([new("全角", FieldDataType.FullWidthText, 2, "N(2)", NationalByteWidth: 4),
                new("別名", FieldDataType.Text, 2, "X(2)", RedefinesName: "全角")], applyToData: true);
            Invoke(editor, "ApplyDefinitionEdits");
            var edited = editor.EditedFields ?? throw new Exception("Edited fields missing");
            Equal(4, edited[0].NationalByteWidth); Equal("全角", edited[1].RedefinesName);
        });
        Test("Definition saving is offered for ordinary definitions as well as inferred CSV definitions", () =>
        {
            Prepare(); using var form = new MainForm(["--definition", definition]);
            Equal(true, Get<ToolStripMenuItem>(form, "saveCurrentDefinitionMenu").Enabled);
            Invoke(form, "SaveCurrentDefinitionToPath", saved, DefinitionTextEncoding.Utf8);
            Equal(true, Fields().SequenceEqual(DefinitionCsvReader.Read(saved)));
        });
    }
}
