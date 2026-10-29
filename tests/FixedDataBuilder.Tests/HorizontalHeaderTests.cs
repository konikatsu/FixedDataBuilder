using FixedDataBuilder;

internal static partial class Program
{
    private static void RunHorizontalHeaderTests(string repo)
    {
        Test("Horizontal numbers retain definition ordinals after hiding fields and Excel keeps original names", () =>
        {
            using var form = CreateCellDisplayForm(recordRows: true);
            var grid = Get<DataGridView>(form, "grid");
            Equal("No.1\n識別番号", grid.Columns[1].HeaderText);
            Equal(true, grid.ColumnHeadersHeight >= 2 * grid.Font.Height);
            Get<HashSet<int>>(form, "hiddenFieldIndexes").Add(1);
            Invoke(form, "RefreshGrid");
            Equal("No.3\n宛名番号", grid.Columns[2].HeaderText);
            Equal("[38-47]", grid[2, 1].Value);
            var excel = (IReadOnlyList<IReadOnlyList<ExcelCell>>)Invoke(form, "BuildExcelRows")!;
            Equal("識別番号", excel[0][1].Text);
            Equal("宛名番号", excel[0][2].Text);
            Equal(2, Invoke(form, "RecordRowsDataStartIndex"));
        });
        Test("Horizontal CSV comments are metadata while edit, HEX, save and Excel still target the first data record", () =>
        {
            using var form = new MainForm(["--definition", Path.Combine(repo, "samples", "comment-definition.csv"),
                "--data", Path.Combine(repo, "samples", "comment-data-utf8.csv"), "--csv-header", "present", "--layout", "record"]);
            var grid = Get<DataGridView>(form, "grid");
            Equal(2, Invoke(form, "RecordRowsDataStartIndex"));
            Equal("型", grid[0, 0].Value);
            Equal("コメント", grid[0, 1].Value);
            Equal("期待値は3。前回の区分を引き継ぐこと", grid[2, 1].Value);
            Equal(true, grid.Rows[1].ReadOnly && grid.Rows[1].Frozen);
            Equal(Color.FromArgb(225, 242, 252), grid[2, 1].InheritedStyle.BackColor);
            Equal(false, grid.Rows.Cast<DataGridViewRow>().SelectMany(row => row.Cells.Cast<DataGridViewCell>())
                .Any(cell => cell.Value?.ToString()?.StartsWith("CSV列") == true));
            grid.CurrentCell = grid[1, 1];
            Equal(false, grid.BeginEdit(false));
            Invoke(form, "DuplicateRecord");
            Invoke(form, "DeleteRecord");
            Equal(2, Get<List<List<string>>>(form, "records").Count);
            Equal("", Get<TextBox>(form, "hexTextBox").Text);
            Invoke(form, "SelectRecord", 0);
            Equal(2, grid.CurrentCell.RowIndex);
            Equal(true, Get<TextBox>(form, "hexTextBox").Text.StartsWith("30 30 30 31"));
            grid[1, 2].Value = "9999";
            Invoke(form, "UpdateRecordValue", 2, 1);
            Equal("9999", Get<List<List<string>>>(form, "records")[0][0]);
            Equal("0002", Get<List<List<string>>>(form, "records")[1][0]);
            var output = Path.Combine(temporaryDirectory, "horizontal-comments.csv");
            Invoke(form, "WriteData", output, Invoke(form, "CurrentSaveOptions"));
            var fields = Get<List<FieldDefinition>>(form, "fields");
            Equal("9999", CsvDataReader.Read(output, fields, CsvTextEncoding.Utf8, true)[0][0]);
            var excel = (IReadOnlyList<IReadOnlyList<ExcelCell>>)Invoke(form, "BuildExcelRows")!;
            Equal(5, excel.Count);
            Equal("コード", excel[0][1].Text);
            Equal("CSV列1", excel[2][1].Text);
            Equal("9999", excel[3][1].Text);
            var workbook = Path.Combine(temporaryDirectory, "horizontal-comments.xlsx");
            ExcelExporter.Write(workbook, "レコード縦", excel);
            var imported = ExcelImporter.Read(workbook);
            Equal(2, imported.Records.Count);
            Equal("9999", imported.Records[0][0]);
            Equal("コード", imported.Fields[0].Name);
        });
        Test("OCCURS band moves below comments and hidden comments do not leave a phantom metadata row", () =>
        {
            using var form = new MainForm(["--definition", Path.Combine(repo, "samples", "copybook-group-occurs-definition.cbl"),
                "--data", Path.Combine(repo, "samples", "copybook-group-occurs-data-crlf-utf8-n-utf16le.dat"),
                "--separator", "crlf", "--national-encoding", "utf16", "--layout", "record"]);
            var fields = Get<List<FieldDefinition>>(form, "fields");
            var before = (IReadOnlyList<IReadOnlyList<ExcelCell>>)Invoke(form, "BuildExcelRows")!;
            fields[0] = fields[0] with { Comment = "先頭の項目を確認" };
            Invoke(form, "RefreshGrid");
            var grid = Get<DataGridView>(form, "grid");
            Equal("コメント", grid[0, 2].Value);
            Equal("OCCURS", grid[0, 3].Value);
            Equal(4, Invoke(form, "RecordRowsDataStartIndex"));
            Invoke(form, "SelectRecord", 0);
            Equal(4, grid.CurrentCell.RowIndex);
            Equal(0, Invoke(form, "CurrentRecordIndex"));
            using var bitmap = RenderGrid(form);
            var after = (IReadOnlyList<IReadOnlyList<ExcelCell>>)Invoke(form, "BuildExcelRows")!;
            Equal(true, before.SelectMany(row => row).SequenceEqual(after.SelectMany(row => row)));
            Get<HashSet<int>>(form, "hiddenFieldIndexes").Add(0);
            Invoke(form, "RefreshGrid");
            Equal(-1, Get<int>(form, "recordCommentRowIndex"));
            Equal("OCCURS", grid[0, 2].Value);
            Equal(3, Invoke(form, "RecordRowsDataStartIndex"));
        });
        Test("Long comments wrap without widening data columns and survive zoom and display truncation intact", () =>
        {
            using var form = CreateCellDisplayForm(recordRows: true);
            var fields = Get<List<FieldDefinition>>(form, "fields");
            var comment = string.Concat(Enumerable.Repeat("期待値と前回値を確認します。", 50));
            fields[0] = fields[0] with { Comment = comment };
            Invoke(form, "RefreshGrid");
            var grid = Get<DataGridView>(form, "grid");
            using var bitmap = RenderGrid(form);
            Equal(true, grid.Columns[1].Width < 1000);
            var row = Get<int>(form, "recordCommentRowIndex");
            Equal(DataGridViewTriState.True, grid[1, row].InheritedStyle.WrapMode);
            Equal(true, grid.Rows[row].Height > grid.RowTemplate.Height);
            Equal(comment, grid[1, row].ToolTipText);
            grid.Columns[1].Width = 100;
            Invoke(form, "ChangeZoomFromWheel", SystemInformation.MouseWheelScrollDelta);
            Equal(true, grid.ColumnHeadersHeight >= 2 * grid.Font.Height);
            Invoke(form, "ApplyDisplayWrapOptions", new DisplayWrapOptions(true, 1, 10, true, 5));
            Equal(comment, grid[1, row].Value);
            Equal(null, Invoke(form, "WrappedDisplayLines", row, 1));
            Equal(false, Get<bool>(form, "hasUnsavedChanges"));
        });
        Test("Switching CSV and fixed save formats recalculates metadata rows without changing data or comments", () =>
        {
            using var form = new MainForm(["--definition", Path.Combine(repo, "samples", "comment-definition.csv"),
                "--data", Path.Combine(repo, "samples", "comment-data-utf8.csv"), "--csv-header", "present", "--layout", "record"]);
            var values = Get<List<List<string>>>(form, "records").SelectMany(row => row).ToArray();
            var fixedPath = Path.Combine(temporaryDirectory, "horizontal-convert.dat");
            Invoke(form, "WriteData", fixedPath, new DataSaveOptions(DataFileFormat.FixedLength, CsvTextEncoding.Utf8,
                RecordSeparatorMode.CrLfOrLf, NationalTextEncoding.ShiftJis));
            var grid = Get<DataGridView>(form, "grid");
            Equal("[1-4]", grid[1, 1].Value);
            Equal("コメント", grid[0, 2].Value);
            Equal(3, Invoke(form, "RecordRowsDataStartIndex"));
            var csvPath = Path.Combine(temporaryDirectory, "horizontal-convert.csv");
            Invoke(form, "WriteData", csvPath, new DataSaveOptions(DataFileFormat.Csv, CsvTextEncoding.Utf8,
                RecordSeparatorMode.CrLfOrLf, NationalTextEncoding.ShiftJis, CsvHasHeader: true));
            Equal("コメント", grid[0, 1].Value);
            Equal(2, Invoke(form, "RecordRowsDataStartIndex"));
            Equal(true, values.SequenceEqual(Get<List<List<string>>>(form, "records").SelectMany(row => row)));
            Invoke(form, "SetSingleRecordView", true);
            Equal(false, grid.Columns["FieldStartPosition"].Visible);
            Invoke(form, "SetSingleRecordView", false);
            Equal("No.1\nコード", grid.Columns[1].HeaderText);
            Equal(2, Invoke(form, "RecordRowsDataStartIndex"));
        });
    }
}
