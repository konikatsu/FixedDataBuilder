using System.Text;
using FixedDataBuilder;

internal static partial class Program
{
    private static void RunDisplayLimitTests(string repo)
    {
        Test("Display limit counts Unicode text elements without splitting emoji or combining marks", () =>
        {
            Equal("東京😀e\u0301", DisplayTextLimiter.Limit("東京😀e\u0301Z", 4, out var cut));
            Equal(true, cut);
            Equal("東京😀e\u0301", DisplayTextLimiter.Limit("東京😀e\u0301", 4, out cut));
            Equal(false, cut);
            Equal("", DisplayTextLimiter.Limit("", 1, out cut));
            Equal(false, cut);
        });
        Test("Display truncation wraps only a prefix while original CSV, Excel, HEX and space count remain intact", () =>
        {
            using var form = new MainForm(["--definition", Path.Combine(repo, "samples", "csv-header-wrap-definition.csv"),
                "--data", Path.Combine(repo, "samples", "space-count-data-utf8.csv"), "--csv-header", "present", "--layout", "record"]);
            var rows = Get<List<List<string>>>(form, "records");
            var fields = Get<List<FieldDefinition>>(form, "fields");
            var value = "ABCDEFGHIJKLMNOPQRST 😀 　";
            rows[0][1] = value;
            Invoke(form, "RefreshGrid");
            var grid = Get<DataGridView>(form, "grid");
            var row = (int)Invoke(form, "RecordRowsDataStartIndex")!;
            grid.CurrentCell = grid[2, row];
            Invoke(form, "UpdateHexView");
            var hex = Get<TextBox>(form, "hexTextBox").Text;
            var spaces = Get<Label>(form, "selectionSpaceLabel").Text;
            var bytes = CsvDataWriter.Encode(fields, rows, hasHeader: true);
            var excel = (IReadOnlyList<IReadOnlyList<ExcelCell>>)Invoke(form, "BuildExcelRows")!;
            Invoke(form, "ApplyDisplayWrapOptions", new DisplayWrapOptions(true, 20, 10, true, 12));
            var lines = (IReadOnlyList<string>)Invoke(form, "WrappedDisplayLines", row, 2)!;
            Equal("ABCDEFGHIJ|KL…", string.Join("|", lines));
            Equal(value, grid[2, row].Value);
            Equal(value, rows[0][1]);
            Equal(hex, Get<TextBox>(form, "hexTextBox").Text);
            Equal(spaces, Get<Label>(form, "selectionSpaceLabel").Text);
            Equal(true, bytes.SequenceEqual(CsvDataWriter.Encode(fields, rows, hasHeader: true)));
            var after = (IReadOnlyList<IReadOnlyList<ExcelCell>>)Invoke(form, "BuildExcelRows")!;
            Equal(true, excel.SelectMany(r => r).SequenceEqual(after.SelectMany(r => r)));
            Equal(false, Get<bool>(form, "hasUnsavedChanges"));
        });
        Test("Character limit also applies below byte wrapping threshold and is disabled with display wrapping", () =>
        {
            using var form = CreateCellDisplayForm(recordRows: true);
            var row = (int)Invoke(form, "RecordRowsDataStartIndex")!;
            Invoke(form, "ApplyDisplayWrapOptions", new DisplayWrapOptions(true, 100, 20, true, 8));
            Equal("12345678…", ((IReadOnlyList<string>)Invoke(form, "WrappedDisplayLines", row, 1)!).Single());
            Invoke(form, "ApplyDisplayWrapOptions", new DisplayWrapOptions(false, 100, 20, true, 8));
            Equal(null, Invoke(form, "WrappedDisplayLines", row, 1));
        });
        Test("Fixed-length display truncation preserves saved bytes, Excel and HEX in both grid layouts", () =>
        {
            foreach (var recordRows in new[] { false, true })
            {
                using var form = CreateCellDisplayForm(recordRows);
                var grid = Get<DataGridView>(form, "grid");
                var row = recordRows ? (int)Invoke(form, "RecordRowsDataStartIndex")! : 0;
                var column = recordRows ? 1 : 2;
                grid.CurrentCell = grid[column, row];
                Invoke(form, "UpdateHexView");
                var originalValue = grid[column, row].Value;
                var hex = Get<TextBox>(form, "hexTextBox").Text;
                var excel = (IReadOnlyList<IReadOnlyList<ExcelCell>>)Invoke(form, "BuildExcelRows")!;
                var before = Path.Combine(temporaryDirectory, $"limit-fixed-before-{recordRows}.dat");
                var after = Path.Combine(temporaryDirectory, $"limit-fixed-after-{recordRows}.dat");
                var saveOptions = Invoke(form, "CurrentSaveOptions");
                Invoke(form, "WriteData", before, saveOptions);
                Invoke(form, "ApplyDisplayWrapOptions", new DisplayWrapOptions(true, 1, 10, true, 12));
                Equal("1234567890|12…", string.Join("|", (IReadOnlyList<string>)Invoke(form, "WrappedDisplayLines", row, column)!));
                Equal(originalValue, grid[column, row].Value);
                Equal(hex, Get<TextBox>(form, "hexTextBox").Text);
                var afterExcel = (IReadOnlyList<IReadOnlyList<ExcelCell>>)Invoke(form, "BuildExcelRows")!;
                Equal(true, excel.SelectMany(r => r).SequenceEqual(afterExcel.SelectMany(r => r)));
                Invoke(form, "WriteData", after, saveOptions);
                Equal(true, File.ReadAllBytes(before).SequenceEqual(File.ReadAllBytes(after)));
                Equal(false, Get<bool>(form, "hasUnsavedChanges"));
            }
        });
        Test("Display limit form keeps character and byte limits separate and honors enable toggles", () =>
        {
            using var dialog = new DisplayWrapOptionsForm(new DisplayWrapOptions(false, 20, 10, true, 12));
            var checks = dialog.Controls.OfType<CheckBox>().ToDictionary(c => c.Name);
            var values = dialog.Controls.OfType<NumericUpDown>().ToDictionary(c => c.Name);
            Equal(false, values["MaxDisplayCharacters"].Enabled);
            checks["WrapEnabled"].Checked = true;
            Equal(true, values["MaxDisplayCharacters"].Enabled);
            Equal(new DisplayWrapOptions(true, 20, 10, true, 12), dialog.Options);
            checks["LimitDisplay"].Checked = false;
            Equal(false, values["MaxDisplayCharacters"].Enabled);
        });
    }
}
