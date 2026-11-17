using FixedDataBuilder;

internal static partial class Program
{
    private static void RunByteRulerTests(string repo)
    {
        Test("The seventeenth CSV field fits an eleven-digit value and the full display ruler", () =>
        {
            var definition = Path.Combine(temporaryDirectory, "ruler-17-definition.csv");
            var data = Path.Combine(temporaryDirectory, "ruler-17-data.csv");
            var columns = Enumerable.Range(1, 17)
                .Select(index => new DefinitionCsvRow($"項目{index}", index == 17 ? "X(11)" : "X(1)"))
                .ToArray();
            DefinitionCsvWriter.Write(definition, columns);
            var values = Enumerable.Repeat("1", 16).Append("01234567890").ToArray();
            File.WriteAllText(data, string.Join(",", values) + "\r\n");
            using var form = new MainForm(["--definition", definition, "--data", data, "--csv-encoding", "utf8", "--layout", "record"]);
            var grid = Get<DataGridView>(form, "grid");
            using var font = new Font("MS Gothic", 9f);
            grid.Font = font;
            Get<ToolStripMenuItem>(form, "byteRulerButton").PerformClick();
            foreach (var wrapped in new[] { false, true })
            {
                Invoke(form, "ApplyDisplayWrapOptions", new DisplayWrapOptions(wrapped, 20, 20, false, 100, 10));
                var ruler = Get<int>(form, "recordRulerRowIndex");
                var start = (int)Invoke(form, "RecordRowsDataStartIndex")!;
                Equal("01234567890", grid[17, start].Value);
                Equal(MainDisplayRuler(form, 17), grid[17, ruler].Value);
                var cell = grid[17, start];
                var textWidth = TextRenderer.MeasureText("01234567890", cell.InheritedStyle.Font ?? grid.Font,
                    Size.Empty, TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine).Width;
                Equal(true, grid.Columns[17].Width >= textWidth + cell.InheritedStyle.Padding.Horizontal + 8);
            }
        });
        Test("Long wrapped values do not force the display ruler to make the column unreasonably wide", () =>
        {
            var definition = Path.Combine(temporaryDirectory, "ruler-wrapped-definition.csv");
            var data = Path.Combine(temporaryDirectory, "ruler-wrapped-data.csv");
            DefinitionCsvWriter.Write(definition, [new("長い文字", "X(100)")]);
            var value = new string('1', 100);
            File.WriteAllText(data, value + "\r\n");
            using var form = new MainForm(["--definition", definition, "--data", data, "--csv-encoding", "utf8", "--layout", "record"]);
            Get<ToolStripMenuItem>(form, "byteRulerButton").PerformClick();
            Invoke(form, "ApplyDisplayWrapOptions", new DisplayWrapOptions(true, 20, 10, false, 100, 10));
            var grid = Get<DataGridView>(form, "grid");
            var ruler = Get<int>(form, "recordRulerRowIndex");
            var start = (int)Invoke(form, "RecordRowsDataStartIndex")!;
            Equal(value, grid[1, start].Value);
            Equal(MainDisplayRuler(form, 1), grid[1, ruler].Value);
            var digitWidth = (float)Invoke(form, "ByteRulerDigitWidth")!;
            Equal(true, grid.Columns[1].Width < digitWidth * 100);
        });
        Test("UTF32 national text wraps 100 bytes at 25 full-width characters without a 100-column ruler", () =>
        {
            var definition = Path.Combine(temporaryDirectory, "display-ruler-utf32-definition.csv");
            var data = Path.Combine(temporaryDirectory, "display-ruler-utf32.dat");
            DefinitionCsvWriter.Write(definition, [new("名称", "N(205)")]);
            var fields = DefinitionCsvReader.Read(definition).Select(field => field with { NationalByteWidth = 4 }).ToArray();
            var value = new string('漢', 205);
            FixedLengthDataWriter.Write(data, fields, [[value]], RecordSeparatorMode.None,
                DataEncodingProfile.Utf8WithNationalText, NationalTextEncoding.Utf32);
            var before = File.ReadAllBytes(data);
            using var font = new Font("MS Gothic", 12f);
            using var form = new MainForm(["--definition", definition, "--data", data, "--separator", "none",
                "--text-encoding", "utf8", "--national-encoding", "utf32", "--layout", "record"]);
            var previousFont = Get<Font>(form, "displayFont");
            typeof(MainForm).GetField("displayFont", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(form, font);
            Invoke(form, "ApplyDisplayFont");
            previousFont.Dispose();
            Invoke(form, "ApplyDisplayWrapOptions", new DisplayWrapOptions(true, 100, 100));
            Get<ToolStripMenuItem>(form, "byteRulerButton").PerformClick();
            var grid = Get<DataGridView>(form, "grid");
            var start = (int)Invoke(form, "RecordRowsDataStartIndex")!;
            var lines = (IReadOnlyList<string>)Invoke(form, "WrappedDisplayLines", start, 1)!;
            Equal(9, lines.Count);
            Equal(25, lines[0].Length);
            Equal(value, string.Concat(lines));
            var digitWidth = (float)Invoke(form, "ByteRulerDigitWidth")!;
            Equal(true, grid.Columns[1].Width >= 50 * digitWidth);
            Equal(true, grid.Columns[1].Width < 75 * digitWidth);
            Equal(MainDisplayRuler(form, 1), grid[1, Get<int>(form, "recordRulerRowIndex")].Value);
            Equal(true, before.SequenceEqual(File.ReadAllBytes(data)));
        });
        Test("Display ruler shows screen positions, five-column labels and bounded continuation", () =>
        {
            var lines = DisplayRulerText.Format(22).Split('\n');
            Equal('5', lines[0][4]);
            Equal("10", lines[0].Substring(8, 2));
            Equal("15", lines[0].Substring(13, 2));
            Equal("20", lines[0].Substring(18, 2));
            Equal("1234567890123456789012", lines[1]);
            Equal(true, DisplayRulerText.Format(100).Split('\n')[0].EndsWith("100"));
            Equal(true, DisplayRulerText.Format(3000).EndsWith("…"));
            Equal(true, DisplayRulerText.Format(int.MaxValue).Length <= 4097);
            Equal("0桁", DisplayRulerText.Format(0));
            Throws<ArgumentOutOfRangeException>(() => DisplayRulerText.Format(-1));
        });
        Test("Display ruler is independent of national and packed storage while retaining edits, selection, HEX and Excel", () =>
        {
            foreach (var national in new[] { NationalTextEncoding.Utf16, NationalTextEncoding.Utf32 })
            {
                var definition = Path.Combine(temporaryDirectory, $"ruler-{national}.csv");
                var data = Path.Combine(temporaryDirectory, $"ruler-{national}.dat");
                DefinitionCsvWriter.Write(definition, [new("コード", "X(4)"), new("名称", "N(2)"), new("金額", "S9(5) COMP-3")]);
                var fields = DefinitionCsvReader.Read(definition).Select(field => field.Type == FieldDataType.FullWidthText
                    ? field with { NationalByteWidth = national == NationalTextEncoding.Utf32 ? 4 : 2 } : field).ToList();
                FixedLengthDataWriter.Write(data, fields, [["AB", "東京", "-12345"]], RecordSeparatorMode.None,
                    DataEncodingProfile.Utf8WithNationalText, national);
                using var form = new MainForm(["--definition", definition, "--data", data, "--separator", "none",
                    "--text-encoding", "utf8", "--national-encoding", national == NationalTextEncoding.Utf32 ? "utf32" : "utf16", "--layout", "record"]);
                var grid = Get<DataGridView>(form, "grid");
                var originalStart = (int)Invoke(form, "RecordRowsDataStartIndex")!;
                grid[1, originalStart].Value = "CD";
                Invoke(form, "UpdateRecordValue", originalStart, 1);
                grid.CurrentCell = grid[2, originalStart];
                var hex = Get<TextBox>(form, "hexTextBox").Text;
                var excel = (IReadOnlyList<IReadOnlyList<ExcelCell>>)Invoke(form, "BuildExcelRows")!;
                var menu = Get<ToolStripMenuItem>(form, "byteRulerButton");
                Equal(false, menu.Checked);
                menu.PerformClick();
                var ruler = Get<int>(form, "recordRulerRowIndex");
                for (var column = 1; column < grid.ColumnCount; column++) Equal(MainDisplayRuler(form, column), grid[column, ruler].Value);
                Equal(originalStart, ruler);
                Equal(true, grid.Rows[ruler].Frozen && grid.Rows[ruler].ReadOnly);
                Equal(originalStart + 1, grid.CurrentCell.RowIndex);
                Equal(2, grid.CurrentCell.ColumnIndex);
                Equal(hex, Get<TextBox>(form, "hexTextBox").Text);
                Equal(true, Get<bool>(form, "hasUnsavedChanges"));
                var actualExcel = (IReadOnlyList<IReadOnlyList<ExcelCell>>)Invoke(form, "BuildExcelRows")!;
                Equal(true, excel.SelectMany(row => row).SequenceEqual(actualExcel.SelectMany(row => row)));
                grid.CurrentCell = grid[1, ruler];
                Equal(false, grid.BeginEdit(false));
                Invoke(form, "DuplicateRecord");
                Invoke(form, "DeleteRecord");
                Equal(1, Get<List<List<string>>>(form, "records").Count);
            }
        });
        Test("CSV ruler follows screen width independently of codec, edits and empty cells", () =>
        {
            var definition = Path.Combine(temporaryDirectory, "ruler-csv-definition.csv");
            var data = Path.Combine(temporaryDirectory, "ruler-csv-data.csv");
            DefinitionCsvWriter.Write(definition, [new("文字", "X(1)")]);
            File.WriteAllText(data, "あA\r\nカタカナ\r\n");
            using var form = new MainForm(["--definition", definition, "--data", data, "--csv-encoding", "utf8", "--layout", "record"]);
            Get<ToolStripMenuItem>(form, "byteRulerButton").PerformClick();
            var grid = Get<DataGridView>(form, "grid");
            Equal(MainDisplayRuler(form, 1), grid[1, Get<int>(form, "recordRulerRowIndex")].Value);
            var path = Path.Combine(temporaryDirectory, "ruler-ms932.csv");
            Invoke(form, "WriteData", path, new DataSaveOptions(DataFileFormat.Csv, CsvTextEncoding.ShiftJis,
                RecordSeparatorMode.CrLfOrLf, NationalTextEncoding.ShiftJis));
            Equal(MainDisplayRuler(form, 1), grid[1, Get<int>(form, "recordRulerRowIndex")].Value);
            var start = (int)Invoke(form, "RecordRowsDataStartIndex")!;
            grid[1, start + 1].Value = "B";
            Invoke(form, "UpdateRecordValue", start + 1, 1);
            Equal(MainDisplayRuler(form, 1), grid[1, Get<int>(form, "recordRulerRowIndex")].Value);
            grid[1, start].Value = "😀";
            Invoke(form, "UpdateRecordValue", start, 1);
            Equal(MainDisplayRuler(form, 1), grid[1, Get<int>(form, "recordRulerRowIndex")].Value);
            Equal("😀", Get<List<List<string>>>(form, "records")[0][0]);
            Invoke(form, "WriteData", Path.Combine(temporaryDirectory, "ruler-utf8.csv"), new DataSaveOptions(DataFileFormat.Csv,
                CsvTextEncoding.Utf8, RecordSeparatorMode.CrLfOrLf, NationalTextEncoding.ShiftJis));
            Equal(MainDisplayRuler(form, 1), grid[1, Get<int>(form, "recordRulerRowIndex")].Value);
            foreach (var row in new[] { start, start + 1 })
            {
                grid[1, row].Value = "";
                Invoke(form, "UpdateRecordValue", row, 1);
            }
            Equal(MainDisplayRuler(form, 1), grid[1, Get<int>(form, "recordRulerRowIndex")].Value);
        });
        Test("Ruler resizes with columns and zoom while preserving display-wrapped data", () =>
        {
            using var form = CreateCellDisplayForm(recordRows: true);
            Get<ToolStripMenuItem>(form, "byteRulerButton").PerformClick();
            var grid = Get<DataGridView>(form, "grid");
            var row = Get<int>(form, "recordRulerRowIndex");
            using var bitmap = RenderGrid(form);
            var values = Get<List<List<string>>>(form, "records").SelectMany(record => record).ToArray();
            grid.Columns[1].Width = 50;
            Equal(MainDisplayRuler(form, 1), grid[1, row].Value);
            Invoke(form, "ChangeZoomFromWheel", SystemInformation.MouseWheelScrollDelta);
            Equal(true, grid.Rows[row].Height >= grid.Font.Height * 2 + 8);
            Invoke(form, "ApplyDisplayWrapOptions", new DisplayWrapOptions(true, 1, 10, true, 5));
            Equal(null, Invoke(form, "WrappedDisplayLines", row, 1));
            Equal(true, values.SequenceEqual(Get<List<List<string>>>(form, "records").SelectMany(record => record)));
            Equal(false, Get<bool>(form, "hasUnsavedChanges"));
        });
        Test("OCCURS and hidden fields stay mapped when switching ruler and list or single-record modes", () =>
        {
            using var form = new MainForm(["--definition", Path.Combine(repo, "samples", "copybook-group-occurs-definition.cbl"),
                "--data", Path.Combine(repo, "samples", "copybook-group-occurs-data-crlf-utf8-n-utf16le.dat"),
                "--separator", "crlf", "--national-encoding", "utf16", "--layout", "record"]);
            var start = (int)Invoke(form, "RecordRowsDataStartIndex")!;
            var menu = Get<ToolStripMenuItem>(form, "byteRulerButton");
            menu.PerformClick();
            Equal(start + 1, Invoke(form, "RecordRowsDataStartIndex"));
            var grid = Get<DataGridView>(form, "grid");
            Equal("OCCURS", grid[0, Get<int>(form, "recordOccursRowIndex")].Value);
            Equal("目盛り", grid[0, Get<int>(form, "recordRulerRowIndex")].Value);
            Get<HashSet<int>>(form, "hiddenFieldIndexes").Add(0);
            Invoke(form, "RefreshGrid");
            Equal(false, Get<List<int>>(form, "recordGridColumnFieldIndexes").Contains(0));
            Invoke(form, "SelectRecord", 0);
            Invoke(form, "SetSingleRecordView", true);
            Equal(false, menu.Enabled);
            Equal(-1, Get<int>(form, "recordRulerRowIndex"));
            Invoke(form, "SetSingleRecordView", false);
            Equal(true, menu.Enabled && menu.Checked);
            Equal(0, Invoke(form, "CurrentRecordIndex"));
            menu.PerformClick();
            Equal(-1, Get<int>(form, "recordRulerRowIndex"));
            Equal(start, Invoke(form, "RecordRowsDataStartIndex"));
        });
    }
    private static string MainDisplayRuler(MainForm form, int column)
    {
        var grid = Get<DataGridView>(form, "grid");
        var row = Get<int>(form, "recordRulerRowIndex");
        var padding = grid[column, row].InheritedStyle.Padding.Horizontal;
        var width = (float)Invoke(form, "ByteRulerDigitWidth")!;
        return DisplayRulerText.Format((int)Math.Floor(Math.Max(1, grid.Columns[column].Width - padding - 8) / width));
    }

}
