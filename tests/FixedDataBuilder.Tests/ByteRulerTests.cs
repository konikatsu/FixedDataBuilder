using FixedDataBuilder;

internal static partial class Program
{
    private static void RunByteRulerTests(string repo)
    {
        Test("Byte ruler shows relative byte positions, decimal labels and bounded continuation", () =>
        {
            var lines = ByteRulerText.Format(22, 22).Split('\n');
            Equal("10", lines[0].Substring(8, 2));
            Equal("20", lines[0].Substring(18, 2));
            Equal("1234567890123456789012", lines[1]);
            Equal(true, ByteRulerText.Format(100, 100).Split('\n')[0].EndsWith("100"));
            Equal(true, ByteRulerText.Format(1000, 12).EndsWith("…"));
            Equal(true, ByteRulerText.Format(int.MaxValue, int.MaxValue).Length <= 4097);
            Equal("0バイト", ByteRulerText.Format(0, 10));
            Throws<ArgumentOutOfRangeException>(() => ByteRulerText.Format(-1, 10));
        });
        Test("Fixed ruler uses X, national and packed storage bytes while retaining edits, selection, HEX and Excel", () =>
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
                var lengths = Get<Dictionary<int, int>>(form, "byteRulerLengths");
                Equal(4, lengths[0]);
                Equal(national == NationalTextEncoding.Utf32 ? 8 : 4, lengths[1]);
                Equal(3, lengths[2]);
                var ruler = Get<int>(form, "recordRulerRowIndex");
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
        Test("CSV ruler follows actual UTF8 and MS932 bytes, edits, unencodable values and empty cells", () =>
        {
            var definition = Path.Combine(temporaryDirectory, "ruler-csv-definition.csv");
            var data = Path.Combine(temporaryDirectory, "ruler-csv-data.csv");
            DefinitionCsvWriter.Write(definition, [new("文字", "X(1)")]);
            File.WriteAllText(data, "あA\r\nカタカナ\r\n");
            using var form = new MainForm(["--definition", definition, "--data", data, "--csv-encoding", "utf8", "--layout", "record"]);
            Get<ToolStripMenuItem>(form, "byteRulerButton").PerformClick();
            var lengths = Get<Dictionary<int, int>>(form, "byteRulerLengths");
            Equal(12, lengths[0]);
            var path = Path.Combine(temporaryDirectory, "ruler-ms932.csv");
            Invoke(form, "WriteData", path, new DataSaveOptions(DataFileFormat.Csv, CsvTextEncoding.ShiftJis,
                RecordSeparatorMode.CrLfOrLf, NationalTextEncoding.ShiftJis));
            Equal(8, lengths[0]);
            var grid = Get<DataGridView>(form, "grid");
            var start = (int)Invoke(form, "RecordRowsDataStartIndex")!;
            grid[1, start + 1].Value = "B";
            Invoke(form, "UpdateRecordValue", start + 1, 1);
            Equal(3, lengths[0]);
            grid[1, start].Value = "😀";
            Invoke(form, "UpdateRecordValue", start, 1);
            Equal(true, Get<Dictionary<int, string>>(form, "byteRulerErrors").ContainsKey(0));
            Equal("😀", Get<List<List<string>>>(form, "records")[0][0]);
            Invoke(form, "WriteData", Path.Combine(temporaryDirectory, "ruler-utf8.csv"), new DataSaveOptions(DataFileFormat.Csv,
                CsvTextEncoding.Utf8, RecordSeparatorMode.CrLfOrLf, NationalTextEncoding.ShiftJis));
            Equal(4, lengths[0]);
            Equal(0, Get<Dictionary<int, string>>(form, "byteRulerErrors").Count);
            foreach (var row in new[] { start, start + 1 })
            {
                grid[1, row].Value = "";
                Invoke(form, "UpdateRecordValue", row, 1);
            }
            Equal(0, lengths[0]);
            Equal("0バイト", grid[1, Get<int>(form, "recordRulerRowIndex")].Value);
        });
        Test("Ruler resizes with columns and zoom without changing the byte count or display-wrapped data", () =>
        {
            using var form = CreateCellDisplayForm(recordRows: true);
            Get<ToolStripMenuItem>(form, "byteRulerButton").PerformClick();
            var grid = Get<DataGridView>(form, "grid");
            var row = Get<int>(form, "recordRulerRowIndex");
            using var bitmap = RenderGrid(form);
            var values = Get<List<List<string>>>(form, "records").SelectMany(record => record).ToArray();
            grid.Columns[1].Width = 50;
            Equal(true, grid[1, row].Value?.ToString()?.EndsWith("…") == true);
            Equal(22, Get<Dictionary<int, int>>(form, "byteRulerLengths")[0]);
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
            Equal("バイト目盛り", grid[0, Get<int>(form, "recordRulerRowIndex")].Value);
            Get<HashSet<int>>(form, "hiddenFieldIndexes").Add(0);
            Invoke(form, "RefreshGrid");
            Equal(false, Get<Dictionary<int, int>>(form, "byteRulerLengths").ContainsKey(0));
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
}
