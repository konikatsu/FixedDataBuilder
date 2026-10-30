using System.Reflection;
using System.Text;
using FixedDataBuilder;

internal static partial class Program
{
    private static void RunVisualComparisonTests(string repo)
    {
        Test("Visual comparison starts with the complete output list below the unopened input list", () =>
        {
            using var form = new VisualComparisonForm(VisualData("OUT", 4, 3));
            var input = VisualPane(form, true); var output = VisualPane(form, false);
            var lists = Get<TableLayoutPanel>(form, "lists");
            Equal(0, lists.GetRow(input)); Equal(2, lists.GetRow(output));
            Equal(0, VisualGrid(input).RowCount);
            Equal(4 + Get<int>(output, "metadataRows"), VisualGrid(output).RowCount);
            Equal(4, VisualGrid(output).ColumnCount);
            Equal(true, VisualGrid(output).ReadOnly);
            Equal(true, VisualGrid(output).VirtualMode);
            Equal(ScrollBars.Both, VisualGrid(output).ScrollBars);
            Equal(false, Descendants(form).OfType<Button>().Any(button => button.Text.Contains("確定") || button.Text.Contains("ルール") || button.Text.Contains("対応")));
            Equal(false, Descendants(form).OfType<SplitContainer>().Any());
        });
        Test("Visual lists show different record counts and definitions immediately without pairing", () =>
        {
            using var form = new VisualComparisonForm(VisualData("OUT", 4, 2));
            Invoke(form, "SetInputDataSet", VisualData("IN", 5, 3));
            var input = VisualPane(form, true); var output = VisualPane(form, false);
            Equal(4, VisualGrid(input).ColumnCount);
            Equal(3, VisualGrid(output).ColumnCount);
            Equal("IN_FIELD_1", VisualGrid(input)[1, 0].Value);
            Equal("OUT_FIELD_1", VisualGrid(output)[1, 0].Value);
            Equal("IN_5_1", VisualGrid(input)[1, Get<int>(input, "metadataRows") + 4].Value);
            Equal("OUT_4_1", VisualGrid(output)[1, Get<int>(output, "metadataRows") + 3].Value);
        });
        Test("Visual comparison scrolls independently and swapping preserves both viewports and selections", () =>
        {
            var inputData = VisualData("IN", 200, 20); var outputData = VisualData("OUT", 150, 20);
            using var form = new VisualComparisonForm(outputData) { ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(-30000, -30000), Width = 1000 };
            Invoke(form, "SetInputDataSet", inputData);
            form.Show(); Application.DoEvents();
            var input = VisualPane(form, true); var output = VisualPane(form, false);
            var ig = VisualGrid(input); var og = VisualGrid(output);
            ig.CurrentCell = ig[8, Get<int>(input, "metadataRows") + 110];
            ig.FirstDisplayedScrollingRowIndex = Get<int>(input, "metadataRows") + 100;
            ig.FirstDisplayedScrollingColumnIndex = 8;
            og.CurrentCell = og[3, Get<int>(output, "metadataRows") + 20];
            var before = (ig.FirstDisplayedScrollingRowIndex, ig.FirstDisplayedScrollingColumnIndex,
                og.FirstDisplayedScrollingRowIndex, og.FirstDisplayedScrollingColumnIndex, ig.CurrentCell, og.CurrentCell);
            Invoke(form, "SwapLists"); Application.DoEvents();
            Equal(before.Item1, ig.FirstDisplayedScrollingRowIndex); Equal(before.Item2, ig.FirstDisplayedScrollingColumnIndex);
            Equal(before.Item3, og.FirstDisplayedScrollingRowIndex); Equal(before.Item4, og.FirstDisplayedScrollingColumnIndex);
            Equal(before.Item5, ig.CurrentCell); Equal(before.Item6, og.CurrentCell);
            Equal(2, Get<TableLayoutPanel>(form, "lists").GetRow(input));
            Equal("入力", input.GetType().GetProperty("Side")!.GetValue(input));
            Equal(true, ReferenceEquals(inputData, input.GetType().GetProperty("DataSet")!.GetValue(input)));
            Invoke(form, "SwapLists");
            Equal(0, Get<TableLayoutPanel>(form, "lists").GetRow(input));
        });
        Test("Opening one visual list does not reset or replace the other list", () =>
        {
            var data = VisualData("OUT", 8, 2);
            using var form = new VisualComparisonForm(data);
            var output = VisualPane(form, false); var og = VisualGrid(output);
            og.CurrentCell = og[2, Get<int>(output, "metadataRows") + 3];
            var selected = og.CurrentCell;
            Invoke(form, "SetInputDataSet", VisualData("IN", 3, 1));
            Equal(selected, og.CurrentCell);
            Equal(true, ReferenceEquals(data, output.GetType().GetProperty("DataSet")!.GetValue(output)));
            var input = VisualPane(form, true); var ig = VisualGrid(input);
            ig.CurrentCell = ig[1, Get<int>(input, "metadataRows") + 1];
            var inputSelected = ig.CurrentCell;
            Invoke(form, "SetOutputDataSet", VisualData("NEW", 1, 4));
            Equal(inputSelected, ig.CurrentCell);
            Equal("IN_2_1", ig[1, Get<int>(input, "metadataRows") + 1].Value);
        });
        Test("Empty visual records remain empty and do not create a synthetic data row", () =>
        {
            var empty = ComparisonDataSet.FromRecords([new("EMPTY", FieldDataType.Text, 4)], Array.Empty<string[]>(), new(DataFileFormat.Csv));
            using var form = new VisualComparisonForm(empty);
            var pane = VisualPane(form, false);
            Equal(Get<int>(pane, "metadataRows"), VisualGrid(pane).RowCount);
            Equal(false, Get<NumericUpDown>(pane, "jumpNumber").Enabled);
        });
        Test("Space markers and raw cell details preserve CSV spaces and leading zeros", () =>
        {
            string[][] rows = [[" A\u3000B ", "0007"]];
            var data = ComparisonDataSet.FromRecords([new("TEXT", FieldDataType.Text, 20), new("NUM", FieldDataType.PlainNumber, 4)], rows, new(DataFileFormat.Csv));
            using var form = new VisualComparisonForm(data);
            var pane = VisualPane(form, false); var grid = VisualGrid(pane); var row = Get<int>(pane, "metadataRows");
            Equal("\uFF65A\u25A1B\uFF65", grid[1, row].Value);
            Equal("0007", grid[2, row].Value);
            Get<CheckBox>(pane, "showSpaces").Checked = false;
            Equal(rows[0][0], grid[1, row].Value);
            Equal(true, Get<TextBox>(pane, "selectedValue").Text.Contains(rows[0][0], StringComparison.Ordinal));
            Equal(" A\u3000B ", data.Records[0][0]);
            Equal("0007", rows[0][1]);
        });
        Test("Visual fixed-length source keeps actual leading and trailing storage spaces", () =>
        {
            var definition = Path.Combine(temporaryDirectory, "visual-storage-definition.csv");
            var path = Path.Combine(temporaryDirectory, "visual-storage.dat");
            DefinitionCsvWriter.Write(definition, [new("TEXT", "X(4)"), new("NUM", "9(2)")]);
            var bytes = Encoding.ASCII.GetBytes(" A  07"); File.WriteAllBytes(path, bytes);
            var data = ComparisonDataSet.Load(definition, path, new(SeparatorMode: RecordSeparatorMode.None));
            using var form = new VisualComparisonForm(data);
            var pane = VisualPane(form, false); var grid = VisualGrid(pane); var row = Get<int>(pane, "metadataRows");
            Equal("\uFF65A\uFF65\uFF65", grid[1, row].Value);
            Equal("07", grid[2, row].Value);
            Invoke(form, "SwapLists");
            Equal(true, bytes.SequenceEqual(File.ReadAllBytes(path)));
        });
        Test("The main comparison action constructs the visual record-list view without changing the main document", () =>
        {
            using var main = CreateCellDisplayForm(recordRows: true);
            var rows = Get<List<List<string>>>(main, "records");
            var before = rows.Select(row => row.ToArray()).ToArray();
            Invoke(main, "SetUnsavedChanges", true);
            using var visual = (VisualComparisonForm)Invoke(main, "CreateVisualComparisonForm")!;
            Equal(5 + Get<int>(VisualPane(visual, false), "metadataRows"), VisualGrid(VisualPane(visual, false)).RowCount);
            Equal(true, Get<bool>(main, "hasUnsavedChanges"));
            Equal(true, before.Zip(rows).All(pair => pair.First.SequenceEqual(pair.Second)));
        });
        Test("Comparison decode errors identify whether definition or data failed", () =>
        {
            var definition = Path.Combine(temporaryDirectory, "visual-utf8-definition.csv");
            var path = Path.Combine(temporaryDirectory, "visual-ms932-data.csv");
            DefinitionCsvWriter.Write(definition, [new("項目", "X(8)")]);
            File.WriteAllText(path, "abc", Encoding.ASCII);
            try
            {
                ComparisonDataSet.Load(definition, path, new(DataFileFormat.Csv), DefinitionTextEncoding.Ms932);
                throw new Exception("Expected a definition codec error");
            }
            catch (DecoderFallbackException error)
            {
                Equal(true, error.Message.Contains("定義ファイル", StringComparison.Ordinal));
                Equal(true, error.Message.Contains(Path.GetFileName(definition), StringComparison.Ordinal));
            }
            File.WriteAllText(path, "あ", JapaneseEncodings.ForCsv(CsvTextEncoding.ShiftJis));
            try
            {
                ComparisonDataSet.Load(definition, path, new(DataFileFormat.Csv, CsvTextEncoding.Utf8), DefinitionTextEncoding.Utf8);
                throw new Exception("Expected a data codec error");
            }
            catch (DecoderFallbackException error)
            {
                Equal(true, error.Message.Contains("データファイル", StringComparison.Ordinal));
                Equal(true, error.Message.Contains(Path.GetFileName(path), StringComparison.Ordinal));
            }
        });
        Test("CSV aliases in the visual list are labeled as having no physical column", () =>
        {
            var data = ComparisonDataSet.FromRecords([new("BASE", FieldDataType.Text, 1), new("ALIAS", FieldDataType.PlainNumber, 2, RedefinesName: "BASE")],
                new[] { new[] { "X", "0" } }, new(DataFileFormat.Csv));
            using var form = new VisualComparisonForm(data);
            var pane = VisualPane(form, false);
            Equal("（CSV列なし）", VisualGrid(pane)[2, Get<int>(pane, "metadataRows")].Value);
            Equal("0", data.Records[0][1]);
        });
        _ = repo;
    }

    private static Control VisualPane(VisualComparisonForm form, bool input) => Get<Control>(form, input ? "inputPane" : "outputPane");
    private static DataGridView VisualGrid(Control pane) => Get<DataGridView>(pane, "grid");
    private static ComparisonDataSet VisualData(string prefix, int count, int fields) => ComparisonDataSet.FromRecords(
        Enumerable.Range(1, fields).Select(index => new FieldDefinition($"{prefix}_FIELD_{index}", FieldDataType.Text, 40)),
        Enumerable.Range(1, count).Select(record => Enumerable.Range(1, fields).Select(field => $"{prefix}_{record}_{field}")), new(DataFileFormat.Csv));
}
