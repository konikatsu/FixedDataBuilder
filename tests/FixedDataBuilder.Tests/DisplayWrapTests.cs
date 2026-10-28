using System.Text;
using FixedDataBuilder;

internal static partial class Program
{
    private static void RunDisplayWrapTests(string repo)
    {
        Test("Byte wrapping honors ASCII width and preserves every space and zero", () =>
        {
            var value = "00000123456789012345  tail ";
            var lines = ByteTextWrapper.Lines(value, Encoding.UTF8, 20);
            Equal("00000123456789012345", lines[0]);
            Equal("  tail ", lines[1]);
            Equal(value, string.Concat(lines));
            Equal(1, ByteTextWrapper.Lines(new string('A', 20), Encoding.UTF8, 20).Count);
        });
        Test("Byte wrapping counts selected SJIS, MS932, UTF-8 and national UTF-16 bytes", () =>
        {
            var value = "東京ABC";
            Equal("東京AB", ByteTextWrapper.Lines(value, JapaneseEncodings.ForCsv(CsvTextEncoding.StrictShiftJis), 6)[0]);
            Equal("東京AB", ByteTextWrapper.Lines(value, JapaneseEncodings.ForCsv(CsvTextEncoding.ShiftJis), 6)[0]);
            Equal("東京", ByteTextWrapper.Lines(value, Encoding.UTF8, 6)[0]);
            Equal("東京A", ByteTextWrapper.Lines(value, Encoding.Unicode, 6)[0]);
        });
        Test("Byte wrapping never splits emoji or combining elements and retains existing empty lines", () =>
        {
            var lines = ByteTextWrapper.Lines("😀e\u0301Z", Encoding.UTF8, 3);
            Equal("😀", lines[0]);
            Equal("e\u0301", lines[1]);
            Equal("Z", lines[2]);
            Equal("AB||CD|", string.Join("|", ByteTextWrapper.Lines("AB\r\n\r\nCD\n", Encoding.UTF8, 20)));
        });
        Test("Wrapping both grid layouts automatically grows rows, narrows columns and resets on uncheck", () =>
        {
            foreach (var recordRows in new[] { false, true })
            {
                using var form = CreateCellDisplayForm(recordRows);
                var grid = Get<DataGridView>(form, "grid");
                var row = recordRows ? (int)Invoke(form, "RecordRowsDataStartIndex")! : 0;
                var column = recordRows ? 1 : 2;
                var beforeHeight = grid.Rows[row].Height;
                var beforeWidth = grid.Columns[column].Width;
                var beforeValue = grid[column, row].Value;
                var beforeRecords = Get<List<List<string>>>(form, "records").SelectMany(r => r).ToArray();
                Get<ToolStripMenuItem>(form, "wrapDisplayButton").PerformClick();
                Equal(true, grid.Rows[row].Height > beforeHeight);
                // Below-threshold fixed values still display their full padding.
                // Include all values before requiring the column itself to narrow.
                Invoke(form, "ApplyDisplayWrapOptions", new DisplayWrapOptions(true, 1, 10));
                Equal(true, grid.Columns[column].Width < beforeWidth);
                Equal(beforeValue, grid[column, row].Value);
                Equal(true, beforeRecords.SequenceEqual(Get<List<List<string>>>(form, "records").SelectMany(r => r)));
                Equal(false, Get<bool>(form, "hasUnsavedChanges"));
                if (recordRows) Equal(beforeHeight, grid.Rows[0].Height);
                using var bitmap = RenderGrid(form);
                Get<ToolStripMenuItem>(form, "wrapDisplayButton").PerformClick();
                Equal(beforeHeight, grid.Rows[row].Height);
            }
        });
        Test("Byte threshold is inclusive and distinct from the per-line byte budget", () =>
        {
            using var form = new MainForm(["--definition", Path.Combine(repo, "samples", "csv-import-definition.csv"),
                "--data", Path.Combine(repo, "samples", "csv-import-data-utf8.csv"), "--layout", "record"]);
            Get<List<List<string>>>(form, "records")[0][0] = new string('A', 20);
            Invoke(form, "RefreshGrid");
            var row = (int)Invoke(form, "RecordRowsDataStartIndex")!;
            Invoke(form, "ApplyDisplayWrapOptions", new DisplayWrapOptions(true, 21, 10));
            Equal(null, Invoke(form, "WrappedDisplayLines", row, 1));
            Invoke(form, "ApplyDisplayWrapOptions", new DisplayWrapOptions(true, 20, 10));
            var lines = (IReadOnlyList<string>)Invoke(form, "WrappedDisplayLines", row, 1)!;
            Equal(2, lines.Count);
            Equal(new string('A', 10), lines[1]);
        });
        Test("Editing a wrapped cell uses raw text and auto-shrinks its row after a shorter value", () =>
        {
            using var form = CreateCellDisplayForm(recordRows: true);
            var grid = Get<DataGridView>(form, "grid");
            var row = (int)Invoke(form, "RecordRowsDataStartIndex")!;
            Invoke(form, "ApplyDisplayWrapOptions", new DisplayWrapOptions(true, 20, 10));
            var height = grid.Rows[row].Height;
            grid.CurrentCell = grid[1, row];
            Equal(true, grid.BeginEdit(false));
            Equal("1234567890123456789012", ((TextBox)grid.EditingControl!).Text);
            ((TextBox)grid.EditingControl!).Text = "123";
            Equal(true, grid.EndEdit());
            Equal("123", Get<List<List<string>>>(form, "records")[0][0]);
            Equal(true, grid.Rows[row].Height < height);
        });
        Test("Display wrapping does not insert line breaks into CSV or Excel export values", () =>
        {
            using var form = new MainForm(["--definition", Path.Combine(repo, "samples", "csv-import-definition.csv"),
                "--data", Path.Combine(repo, "samples", "csv-import-data-with-header-utf8.csv"), "--csv-header", "present", "--layout", "record"]);
            var fields = Get<List<FieldDefinition>>(form, "fields");
            var records = Get<List<List<string>>>(form, "records");
            var before = CsvDataWriter.Encode(fields, records, hasHeader: true);
            var excelBefore = (IReadOnlyList<IReadOnlyList<ExcelCell>>)Invoke(form, "BuildExcelRows")!;
            Invoke(form, "ApplyDisplayWrapOptions", new DisplayWrapOptions(true, 1, 4));
            var target = Path.Combine(temporaryDirectory, "wrapped-output.csv");
            Invoke(form, "WriteData", target, Invoke(form, "CurrentSaveOptions"));
            Equal(true, before.SequenceEqual(File.ReadAllBytes(target)));
            var excelAfter = (IReadOnlyList<IReadOnlyList<ExcelCell>>)Invoke(form, "BuildExcelRows")!;
            Equal(true, excelBefore.SelectMany(r => r).SequenceEqual(excelAfter.SelectMany(r => r)));
        });
        Test("Invalid SJIS values stay editable when display wrapping is enabled", () =>
        {
            using var form = CreateCellDisplayForm(recordRows: true);
            typeof(MainForm).GetField("currentDataEncodingProfile", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .SetValue(form, DataEncodingProfile.StrictShiftJis);
            Get<List<List<string>>>(form, "records")[0][0] = "😀";
            Invoke(form, "RefreshGrid");
            Invoke(form, "ApplyDisplayWrapOptions", new DisplayWrapOptions(true, 1, 4));
            using var bitmap = RenderGrid(form);
            Equal("😀", Get<List<List<string>>>(form, "records")[0][0]);
        });
        Test("Wrap settings expose enable, threshold and line bytes; cancel preserves the current display", () =>
        {
            using var dialog = new DisplayWrapOptionsForm(new DisplayWrapOptions());
            var enabled = dialog.Controls.OfType<CheckBox>().Single();
            var numbers = dialog.Controls.OfType<NumericUpDown>().ToDictionary(c => c.Name);
            Equal(false, numbers["WrapLineBytes"].Enabled);
            enabled.Checked = true;
            numbers["WrapThresholdBytes"].Value = 30;
            numbers["WrapLineBytes"].Value = 15;
            Equal(new DisplayWrapOptions(true, 30, 15), dialog.Options);
            using var form = CreateCellDisplayForm(recordRows: true);
            WithDialog("折り返し表示設定", DialogResult.Cancel, () => Invoke(form, "ChooseDisplayWrapOptions"), settings =>
                settings.Controls.OfType<CheckBox>().Single().Checked = true);
            Equal(false, Get<DisplayWrapOptions>(form, "displayWrapOptions").Enabled);
        });
    }
}
