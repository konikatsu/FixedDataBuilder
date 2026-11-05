using System.Text;
using System.Text.Json;
using FixedDataBuilder;

internal static partial class Program
{
    private static void RunComparisonSetTests(string repo)
    {
        var path = Path.Combine(temporaryDirectory, "work.fdbcompare.json");
        ComparisonSetDocument Document()
        {
            using var form = new VisualComparisonForm(VisualData("OUT", 3, 3));
            Invoke(form, "SetInputDataSet", VisualData("IN", 4, 2));
            var input = VisualPane(form, true); var output = VisualPane(form, false);
            ReviewInvoke(input, "SelectReviewCell", 2, 1); ReviewInvoke(input, "MarkSelected", true);
            ReviewInvoke(output, "SelectReviewCell", 1, 2); ReviewInvoke(output, "MarkSelected", true);
            Get<CheckBox>(input, "showByteRuler").Checked = false;
            Get<CheckBox>(output, "showSpaces").Checked = false;
            VisualGrid(output).Columns[2].Width = 230;
            VisualGrid(output).Rows[Get<int>(output, "metadataRows") + 1].Height = 65;
            Invoke(form, "SwapLists");
            return (ComparisonSetDocument)Invoke(form, "CaptureComparisonSet")!;
        }
        Test("Comparison sets round-trip both datasets, human review marks, selected cells and display settings", () =>
        {
            var document = Document(); ComparisonSetStore.Write(path, document);
            var read = ComparisonSetStore.Read(path);
            Equal(1, read.Version); Equal(false, read.InputOnTop);
            Equal(document.Input!.Data.RecordFingerprint, read.Input!.Data.RecordFingerprint);
            Equal(document.Output!.Data.RecordFingerprint, read.Output!.Data.RecordFingerprint);
            Equal(true, document.Input.Reviewed.SequenceEqual(read.Input.Reviewed));
            Equal(true, document.Output!.Reviewed.SequenceEqual(read.Output!.Reviewed));
            Equal(false, read.Input.View.ShowRuler); Equal(false, read.Output!.View.ShowSpaces);
            Equal(230, read.Output!.View.ColumnWidths[2]); Equal(65, read.Output!.View.RowHeights[1]);
            Equal(1, read.Output!.View.CurrentRecord); Equal(2, read.Output!.View.CurrentField);
        });
        Test("Restoring a comparison set restores both sides and review marks without modifying the main output model", () =>
        {
            var document = Document();
            using var main = CreateCellDisplayForm(recordRows: true);
            var before = Get<List<List<string>>>(main, "records").SelectMany(row => row).ToArray();
            using var form = (VisualComparisonForm)Invoke(main, "CreateVisualComparisonForm")!;
            Invoke(form, "RestoreComparisonSet", document);
            var input = VisualPane(form, true); var output = VisualPane(form, false);
            Equal(true, ReviewInvoke(input, "IsReviewed", 2, 1)); Equal(true, ReviewInvoke(output, "IsReviewed", 1, 2));
            Equal(230, VisualGrid(output).Columns[2].Width);
            Equal(false, Get<bool>(form, "inputOnTop")); Equal(false, Get<bool>(form, "comparisonSetDirty"));
            Equal(4 + Get<int>(input, "metadataRows"), VisualGrid(input).RowCount);
            Equal(true, before.SequenceEqual(Get<List<List<string>>>(main, "records").SelectMany(row => row)));
        });
        Test("Comparison sets preserve fixed source storage spaces even after original files are unavailable", () =>
        {
            var definition = Path.Combine(temporaryDirectory, "set-source-definition.csv");
            var source = Path.Combine(temporaryDirectory, "set-source.dat");
            DefinitionCsvWriter.Write(definition, [new("文字", "X(4)"), new("数量", "9(4)")]);
            var bytes = Encoding.ASCII.GetBytes(" A  0007"); File.WriteAllBytes(source, bytes);
            using var form = new VisualComparisonForm(ComparisonDataSet.Load(definition, source, new(SeparatorMode: RecordSeparatorMode.None)));
            var document = (ComparisonSetDocument)Invoke(form, "CaptureComparisonSet")!;
            ComparisonSetStore.Write(path, document);
            File.Delete(definition); File.Delete(source);
            var restored = ComparisonDataSet.FromSnapshot(ComparisonSetStore.Read(path).Output!.Data);
            Equal(true, bytes.SequenceEqual(restored.CopySourceBytes()!));
            Equal(" A  ", restored.ReadSource(0, 0, ComparisonSourceKind.FixedStorageText).Value);
            Equal("0007", restored.Records[0][1]);
        });
        Test("Saving a comparison set cannot replace any input or output definition or data file", () =>
        {
            var document = Document(); var bytes = Encoding.UTF8.GetBytes("original");
            File.WriteAllBytes(path, bytes);
            var conflicting = document with { Output = document.Output! with { Data = document.Output!.Data with { DataPath = path } } };
            Throws<InvalidDataException>(() => ComparisonSetStore.Write(path, conflicting));
            Equal(true, bytes.SequenceEqual(File.ReadAllBytes(path)));
            conflicting = document with { Input = document.Input! with { Data = document.Input!.Data with { DefinitionPath = path } } };
            Throws<InvalidDataException>(() => ComparisonSetStore.Write(path, conflicting));
            Equal(true, bytes.SequenceEqual(File.ReadAllBytes(path)));
        });
        Test("Changed snapshot values cannot silently reuse old human review marks", () =>
        {
            var document = Document(); var records = document.Output!.Data.Records.Select(row => row.ToArray()).ToArray();
            records[0][0] = "changed";
            var invalid = document with { Output = document.Output! with { Data = document.Output!.Data with { Records = records } } };
            File.WriteAllText(path, JsonSerializer.Serialize(invalid));
            Throws<InvalidDataException>(() => ComparisonSetStore.Read(path));
        });
        Test("Duplicate or out-of-range review marks are rejected before replacing a saved comparison set", () =>
        {
            var document = Document(); ComparisonSetStore.Write(path, document); var bytes = File.ReadAllBytes(path);
            foreach (var cells in new[] { new[] { new ReviewCell(99, 0) }, new[] { new ReviewCell(0, 99) },
                new[] { new ReviewCell(0, 0), new ReviewCell(0, 0) } })
            {
                var invalid = document with { Output = document.Output! with { Reviewed = cells } };
                Throws<InvalidDataException>(() => ComparisonSetStore.Write(path, invalid));
                Equal(true, bytes.SequenceEqual(File.ReadAllBytes(path)));
            }
        });
        Test("Comparison set version, schema and display ranges are validated", () =>
        {
            var document = Document();
            Throws<InvalidDataException>(() => ComparisonSetStore.Validate(document with { Version = 999 }));
            Throws<InvalidDataException>(() => ComparisonSetStore.Validate(document with { Output = document.Output! with
                { View = document.Output!.View with { ColumnWidths = [1] } } }));
            File.WriteAllText(path, "{\"Version\":1,\"Unsupported\":true}");
            Throws<InvalidDataException>(() => ComparisonSetStore.Read(path));
            File.WriteAllText(path, "{broken");
            Throws<InvalidDataException>(() => ComparisonSetStore.Read(path));
        });
        Test("Oversized comparison files are rejected before deserializing their contents", () =>
        {
            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write)) stream.SetLength((long)ComparisonSetStore.MaximumBytes + 1);
            Throws<InvalidDataException>(() => ComparisonSetStore.Read(path));
        });
        Test("A set can be saved before input is opened and clears an existing input on restore", () =>
        {
            using var initial = new VisualComparisonForm(VisualData("OUT", 2, 2));
            var document = (ComparisonSetDocument)Invoke(initial, "CaptureComparisonSet")!;
            ComparisonSetStore.Write(path, document);
            using var target = new VisualComparisonForm(VisualData("OTHER", 3, 3));
            Invoke(target, "SetInputDataSet", VisualData("IN", 5, 4));
            Invoke(target, "RestoreComparisonSet", ComparisonSetStore.Read(path));
            Equal(0, VisualGrid(VisualPane(target, true)).RowCount);
            Equal("OUT_1_1", VisualGrid(VisualPane(target, false))[1, Get<int>(VisualPane(target, false), "metadataRows")].Value);
        });
        Test("An invalid set leaves both current panes and their human review marks intact", () =>
        {
            using var form = new VisualComparisonForm(VisualData("CURRENT", 2, 2)); var pane = VisualPane(form, false);
            ReviewInvoke(pane, "SelectReviewCell", 0, 0); ReviewInvoke(pane, "MarkSelected", true);
            var document = Document(); var invalid = document with { Input = document.Input! with { Reviewed = [new(999, 0)] } };
            Throws<System.Reflection.TargetInvocationException>(() => Invoke(form, "RestoreComparisonSet", invalid));
            Equal("CURRENT_1_1", VisualGrid(pane)[1, Get<int>(pane, "metadataRows")].Value);
            Equal(true, ReviewInvoke(pane, "IsReviewed", 0, 0)); Equal(0, VisualGrid(VisualPane(form, true)).RowCount);
        });
        Test("Form-level comparison set save clears its dirty flag after atomic successful persistence", () =>
        {
            using var form = new VisualComparisonForm(VisualData("OUT", 2, 2)); var pane = VisualPane(form, false);
            ReviewInvoke(pane, "SelectReviewCell", 0, 0); ReviewInvoke(pane, "MarkSelected", true);
            Invoke(form, "SaveComparisonSetToPath", path);
            Equal(false, Get<bool>(form, "comparisonSetDirty"));
            Equal(true, ComparisonSetStore.Read(path).Output!.Reviewed.Contains(new(0, 0)));
        });
        Test("Failed comparison set save preserves unsaved review state", () =>
        {
            var data = ComparisonDataSet.FromRecords([new("A", FieldDataType.Text, 1)], new[] { new[] { "A" } }, new(DataFileFormat.Csv), dataPath: path);
            using var form = new VisualComparisonForm(data); var pane = VisualPane(form, false);
            ReviewInvoke(pane, "SelectReviewCell", 0, 0); ReviewInvoke(pane, "MarkSelected", true);
            Throws<System.Reflection.TargetInvocationException>(() => Invoke(form, "SaveComparisonSetToPath", path));
            Equal(true, Get<bool>(form, "comparisonSetDirty")); Equal(true, ReviewInvoke(pane, "IsReviewed", 0, 0));
        });
        _ = repo;
    }
}
