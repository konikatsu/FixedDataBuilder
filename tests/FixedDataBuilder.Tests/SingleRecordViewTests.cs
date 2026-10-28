using FixedDataBuilder;

internal static partial class Program
{
    private static void RunSingleRecordViewTests(string repo)
    {
        Test("Single record view places position, full HEX and space counts beside each field", () =>
        {
            using var form = CreateCellDisplayForm(recordRows: false);
            Invoke(form, "SetSingleRecordView", true);
            var grid = Get<DataGridView>(form, "grid");
            Equal(true, Get<bool>(form, "isSingleRecordView"));
            Equal(5, grid.Columns.Count);
            Equal("No.", grid.TopLeftHeaderCell.Value);
            Equal("項目名", grid.Columns[0].HeaderText);
            Equal("1|2|3|4", string.Join("|", grid.Rows.Cast<DataGridViewRow>().Select(r => r.HeaderCell.Value)));
            Equal("1|23|38|48", string.Join("|", grid.Rows.Cast<DataGridViewRow>().Select(r => r.Cells[1].Value)));
            Equal("1234567890123456789012", grid[2, 0].Value);
            Equal("31 32 33 34 35 36 37 38 39 30 31 32 33 34 35 36 37 38 39 30 31 32", grid[3, 0].Value);
            Equal("0 / 0", grid[4, 0].Value);
            Equal(false, Get<Panel>(form, "hexArea").Visible);
            Equal(false, grid[2, 0].ReadOnly);
            foreach (var column in new[] { 0, 1, 3, 4 }) Equal(true, grid[column, 0].ReadOnly);
            Invoke(form, "ApplyDisplayWrapOptions", new DisplayWrapOptions(true, 1, 10, true, 12));
            Equal("1234567890|12…", string.Join("|", (IReadOnlyList<string>)Invoke(form, "WrappedDisplayLines", 0, 2)!));
            Equal(null, Invoke(form, "WrappedDisplayLines", 0, 3));
            Equal(null, Invoke(form, "WrappedDisplayLines", 0, 4));
            using var bitmap = RenderGrid(form);
            Equal(false, Get<bool>(form, "hasUnsavedChanges"));
        });

        Test("Single CSV view keeps raw Unicode, quoting, all-record Excel and saved data intact", () =>
        {
            using var form = new MainForm(["--definition", Path.Combine(repo, "samples", "csv-header-wrap-definition.csv"),
                "--data", Path.Combine(repo, "samples", "space-count-data-utf8.csv"), "--csv-header", "present", "--layout", "field"]);
            var records = Get<List<List<string>>>(form, "records");
            var value = "A  B　😀,\"Q\"\n ";
            records[0][1] = value;
            Invoke(form, "RefreshGrid");
            Invoke(form, "SelectRecord", 0);
            var excelBefore = (IReadOnlyList<IReadOnlyList<ExcelCell>>)Invoke(form, "BuildExcelRows")!;
            var before = Path.Combine(temporaryDirectory, "single-csv-before.csv");
            var after = Path.Combine(temporaryDirectory, "single-csv-after.csv");
            var saveOptions = Invoke(form, "CurrentSaveOptions");
            Invoke(form, "WriteData", before, saveOptions);
            Invoke(form, "SetSingleRecordView", true);
            var grid = Get<DataGridView>(form, "grid");
            Equal("CSV列1", grid[1, 0].Value);
            Equal("CSV列2", grid[1, 1].Value);
            Equal(value, grid[2, 1].Value);
            Equal("41 20 20 42 E3 80 80 F0 9F 98 80 2C 22 51 22 0A 20", grid[3, 1].Value);
            Equal("3 / 1", grid[4, 1].Value);
            Invoke(form, "ChangeSingleRecord", 2);
            var excelAfter = (IReadOnlyList<IReadOnlyList<ExcelCell>>)Invoke(form, "BuildExcelRows")!;
            Equal(true, excelBefore.SelectMany(r => r).SequenceEqual(excelAfter.SelectMany(r => r)));
            Equal(2 + records.Count, excelAfter[0].Count);
            Equal("定義", excelAfter[0][1].Text);
            Invoke(form, "WriteData", after, saveOptions);
            Equal(true, File.ReadAllBytes(before).SequenceEqual(File.ReadAllBytes(after)));
            Equal(value, records[0][1]);
            Equal(false, Get<bool>(form, "hasUnsavedChanges"));
        });

        Test("Single fixed view uses stored byte positions, national and packed bytes, padding and validation", () =>
        {
            using var form = new MainForm();
            Get<List<FieldDefinition>>(form, "fields").AddRange([
                new("コード", FieldDataType.Text, 4, "X(4)"),
                new("氏名", FieldDataType.FullWidthText, 2, "N(2)"),
                new("金額", FieldDataType.PackedSigned, 5, "S9(5) COMP-3"),
                new("区分", FieldDataType.Text, 2, "X(2)")]);
            Get<List<List<string>>>(form, "records").Add(["A ", "山", "-12345", "Z"]);
            Invoke(form, "RefreshGrid");
            Invoke(form, "SetSingleRecordView", true);
            var grid = Get<DataGridView>(form, "grid");
            Equal("1|5|9|12", string.Join("|", grid.Rows.Cast<DataGridViewRow>().Select(r => r.Cells[1].Value)));
            Equal("41 20 20 20", grid[3, 0].Value);
            Equal("3 / 0", grid[4, 0].Value);
            Equal("8E 52 81 40", grid[3, 1].Value);
            Equal("0 / 1", grid[4, 1].Value);
            Equal("12 34 5D", grid[3, 2].Value);
            grid[2, 0].Value = "ABCDE";
            Invoke(form, "UpdateRecordValue", 0, 2);
            Equal("ABCDE", Get<List<List<string>>>(form, "records")[0][0]);
            Equal(Color.FromArgb(255, 225, 225), grid[2, 0].Style.BackColor);
            Equal(true, (grid[3, 0].Value?.ToString() ?? "").Contains("エラー"));
            Equal(true, Get<bool>(form, "hasUnsavedChanges"));
        });

        Test("Single record navigation commits edits before moving and preserves the selected field", () =>
        {
            using var form = CreateCellDisplayForm(recordRows: false);
            Invoke(form, "SelectRecord", 0);
            Invoke(form, "SetSingleRecordView", true);
            var grid = Get<DataGridView>(form, "grid");
            var records = Get<List<List<string>>>(form, "records");
            grid.CurrentCell = grid[2, 1];
            Equal(true, grid.BeginEdit(false));
            ((TextBox)grid.EditingControl!).Text = "987";
            Get<Button>(form, "nextRecordButton").PerformClick();
            Equal("987", records[0][1]);
            Equal("223456789012345", grid[2, 1].Value);
            Equal(1, (int)Invoke(form, "CurrentFieldIndex")!);
            Equal(1, Get<int>(form, "singleRecordIndex"));
            Equal(2m, Get<NumericUpDown>(form, "singleRecordNumber").Value);
            Get<NumericUpDown>(form, "singleRecordNumber").Value = 5;
            Equal(4, Get<int>(form, "singleRecordIndex"));
            Equal(false, Get<Button>(form, "nextRecordButton").Enabled);
            Invoke(form, "ChangeSingleRecord", int.MaxValue);
            Equal(4, Get<int>(form, "singleRecordIndex"));
            Invoke(form, "ChangeSingleRecord", int.MinValue);
            Equal(0, Get<int>(form, "singleRecordIndex"));
            Equal(false, Get<Button>(form, "previousRecordButton").Enabled);
            Equal("987", grid[2, 1].Value);
            Equal(true, Get<bool>(form, "hasUnsavedChanges"));
        });

        Test("Single view keeps original field ordinals, OCCURS bands and record mutation targets", () =>
        {
            using var form = CreateCellDisplayForm(recordRows: false);
            Get<HashSet<int>>(form, "hiddenFieldIndexes").Add(1);
            Invoke(form, "RefreshGrid");
            Invoke(form, "SetSingleRecordView", true);
            var grid = Get<DataGridView>(form, "grid");
            Equal("1|3|4", string.Join("|", grid.Rows.Cast<DataGridViewRow>().Select(r => r.HeaderCell.Value)));
            Equal("1|38|48", string.Join("|", grid.Rows.Cast<DataGridViewRow>().Select(r => r.Cells[1].Value)));
            Invoke(form, "ChangeSingleRecord", 2);
            var selected = Get<List<List<string>>>(form, "records")[2].ToArray();
            Invoke(form, "DuplicateRecord");
            Equal(6, Get<List<List<string>>>(form, "records").Count);
            Equal(3, Get<int>(form, "singleRecordIndex"));
            Equal(true, selected.SequenceEqual(Get<List<List<string>>>(form, "records")[3]));
            Invoke(form, "DeleteRecord");
            Equal(5, Get<List<List<string>>>(form, "records").Count);
            Equal(3, Get<int>(form, "singleRecordIndex"));
            Invoke(form, "AddRecord");
            Equal(6, Get<List<List<string>>>(form, "records").Count);
            Equal(5, Get<int>(form, "singleRecordIndex"));
            Equal(6m, Get<NumericUpDown>(form, "singleRecordNumber").Maximum);

            using var grouped = new MainForm(["--definition", Path.Combine(repo, "samples", "copybook-group-occurs-definition.cbl"),
                "--data", Path.Combine(repo, "samples", "copybook-group-occurs-data-crlf-utf8-n-utf16le.dat"),
                "--separator", "crlf", "--national-encoding", "utf16"]);
            Invoke(grouped, "SetSingleRecordView", true);
            var groupedGrid = Get<DataGridView>(grouped, "grid");
            var bands = 0;
            foreach (DataGridViewRow row in groupedGrid.Rows)
            {
                var fieldIndex = (int)Invoke(grouped, "FieldIndexFromFieldRow", row.Index)!;
                if (fieldIndex >= 0) Equal((fieldIndex + 1).ToString(), row.HeaderCell.Value);
                else
                {
                    bands++;
                    Equal(null, row.HeaderCell.Value);
                    Equal("", row.Cells[3].Value);
                    Equal("", row.Cells[4].Value);
                }
            }
            Equal(true, bands > 0);
        });

        Test("Single CSV Save As refreshes every displayed field HEX when the encoding changes", () =>
        {
            using var form = new MainForm(["--definition", Path.Combine(repo, "samples", "csv-header-wrap-definition.csv"),
                "--data", Path.Combine(repo, "samples", "space-count-data-utf8.csv"), "--csv-header", "present", "--layout", "field"]);
            var records = Get<List<List<string>>>(form, "records");
            records.Clear();
            records.AddRange([["東京", "大阪"], ["京都", "札幌"]]);
            Invoke(form, "RefreshGrid");
            Invoke(form, "SelectRecord", 0);
            Invoke(form, "SetSingleRecordView", true);
            var grid = Get<DataGridView>(form, "grid");
            grid.CurrentCell = grid[2, 0];
            Equal("E6 9D B1 E4 BA AC", grid[3, 0].Value);
            Equal("E5 A4 A7 E9 98 AA", grid[3, 1].Value);
            var ms932Path = Path.Combine(temporaryDirectory, "single-encoding-ms932.csv");
            Invoke(form, "WriteData", ms932Path, new DataSaveOptions(DataFileFormat.Csv, CsvTextEncoding.ShiftJis,
                RecordSeparatorMode.CrLfOrLf, NationalTextEncoding.ShiftJis, CsvHasHeader: true));
            Equal("93 8C 8B 9E", grid[3, 0].Value);
            Equal("91 E5 8D E3", grid[3, 1].Value);
            Equal(0, Get<int>(form, "singleRecordIndex"));
            Equal("東京", records[0][0]);
            Equal("大阪", records[0][1]);
            Equal("東京", CsvDataReader.Read(ms932Path, Get<List<FieldDefinition>>(form, "fields"), CsvTextEncoding.ShiftJis, true)[0][0]);
            var utf8Path = Path.Combine(temporaryDirectory, "single-encoding-utf8.csv");
            Invoke(form, "WriteData", utf8Path, new DataSaveOptions(DataFileFormat.Csv, CsvTextEncoding.Utf8,
                RecordSeparatorMode.CrLfOrLf, NationalTextEncoding.ShiftJis, CsvHasHeader: true));
            Equal("E6 9D B1 E4 BA AC", grid[3, 0].Value);
            Equal("E5 A4 A7 E9 98 AA", grid[3, 1].Value);
            Equal("0 / 0", grid[4, 0].Value);
            Equal("0 / 0", grid[4, 1].Value);
            Equal(false, Get<bool>(form, "hasUnsavedChanges"));
        });

        Test("Reloading an Excel source retains single view and selected record while restoring workbook data", () =>
        {
            using var seed = CreateCellDisplayForm(recordRows: false);
            var workbook = Path.Combine(temporaryDirectory, "single-excel-reload.xlsx");
            ExcelExporter.Write(workbook, "項目縦", (IReadOnlyList<IReadOnlyList<ExcelCell>>)Invoke(seed, "BuildExcelRows")!);
            using var form = new MainForm();
            Invoke(form, "LoadExcel", workbook);
            Invoke(form, "SetSingleRecordView", true);
            Invoke(form, "ChangeSingleRecord", 1);
            Get<List<List<string>>>(form, "records")[1][0] = "edited";
            Invoke(form, "ReloadCurrentFilesCore");
            Equal(true, Get<bool>(form, "isSingleRecordView"));
            Equal(true, Get<ToolStripMenuItem>(form, "singleRecordViewButton").Checked);
            Equal(1, Get<int>(form, "singleRecordIndex"));
            Equal(2m, Get<NumericUpDown>(form, "singleRecordNumber").Value);
            Equal(5, Get<List<List<string>>>(form, "records").Count);
            var grid = Get<DataGridView>(form, "grid");
            Equal(5, grid.Columns.Count);
            Equal("2234567890123456789012", grid[2, 0].Value);
            Equal("32 32 33 34 35 36 37 38 39 30 31 32 33 34 35 36 37 38 39 30 31 32", grid[3, 0].Value);
            Equal("2234567890123456789012", Get<List<List<string>>>(form, "records")[1][0]);
            Equal(false, Get<bool>(form, "hasUnsavedChanges"));
        });

        Test("Single view restores either list layout and handles an empty data set without navigation", () =>
        {
            using var form = CreateCellDisplayForm(recordRows: true);
            Invoke(form, "SetHexAreaHeight", 200);
            Invoke(form, "SetSingleRecordView", true);
            Invoke(form, "ChangeSingleRecord", 3);
            Invoke(form, "SetSingleRecordView", false);
            Equal(true, Get<ToolStripMenuItem>(form, "recordRowsButton").Checked);
            Equal(false, Get<bool>(form, "isSingleRecordView"));
            Equal(3, (int)Invoke(form, "CurrentRecordIndex")!);
            Equal(true, Get<Panel>(form, "hexArea").Visible);
            Equal(200, Get<Panel>(form, "hexArea").Height);
            Invoke(form, "SetSingleRecordView", true);
            Get<ToolStripMenuItem>(form, "fieldRowsButton").PerformClick();
            Equal(false, Get<bool>(form, "isSingleRecordView"));
            Equal(true, Get<ToolStripMenuItem>(form, "fieldRowsButton").Checked);
            Equal(false, Get<bool>(form, "hasUnsavedChanges"));

            using var empty = new MainForm();
            Invoke(empty, "SetSingleRecordView", true);
            Invoke(empty, "ChangeSingleRecord", 3);
            Equal(false, Get<Button>(empty, "previousRecordButton").Enabled);
            Equal(false, Get<Button>(empty, "nextRecordButton").Enabled);
            Equal(false, Get<NumericUpDown>(empty, "singleRecordNumber").Enabled);
            Equal(0, Get<List<List<string>>>(empty, "records").Count);
        });
    }
}
