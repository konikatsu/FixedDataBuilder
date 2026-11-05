using FixedDataBuilder;

internal static partial class Program
{
    private static void RunFieldNumberTests(string repo)
    {
        Test("Field rows show definition ordinals beside names without shifting data columns", () =>
        {
            using var form = CreateCellDisplayForm(recordRows: false);
            var grid = Get<DataGridView>(form, "grid");
            Equal(true, grid.RowHeadersVisible);
            Equal("No.", grid.TopLeftHeaderCell.Value);
            Equal("項目名", grid.Columns[0].HeaderText);
            Equal("定義", grid.Columns[1].HeaderText);
            Equal("識別番号", grid[0, 0].Value);
            Equal("1234567890123456789012", grid[2, 0].Value);
            Equal("1|2|3|4", string.Join("|", grid.Rows.Cast<DataGridViewRow>().Select(r => r.HeaderCell.Value)));
            using var bitmap = RenderGrid(form);
        });
        Test("Hidden fields retain original ordinals and OCCURS band rows have no field number", () =>
        {
            using var simple = CreateCellDisplayForm(recordRows: false);
            Get<HashSet<int>>(simple, "hiddenFieldIndexes").Add(1);
            Invoke(simple, "RefreshGrid");
            var grid = Get<DataGridView>(simple, "grid");
            Equal("1|3|4", string.Join("|", grid.Rows.Cast<DataGridViewRow>().Select(r => r.HeaderCell.Value)));
            Equal("宛名番号", grid[0, 1].Value);
            using var grouped = new MainForm(["--definition", Path.Combine(repo, "samples", "copybook-group-occurs-definition.cbl"),
                "--data", Path.Combine(repo, "samples", "copybook-group-occurs-data-crlf-utf8-n-utf16le.dat"),
                "--separator", "crlf", "--national-encoding", "utf16"]);
            var groupedGrid = Get<DataGridView>(grouped, "grid");
            var bands = 0;
            foreach (DataGridViewRow row in groupedGrid.Rows)
            {
                var index = (int)Invoke(grouped, "FieldIndexFromFieldRow", row.Index)!;
                if (index < 0) { Equal(null, row.HeaderCell.Value); bands++; }
                else Equal((index + 1).ToString(), row.HeaderCell.Value);
            }
            Equal(true, bands > 0);
        });
        Test("Field number display and layout switches preserve records, saved bytes, Excel and HEX", () =>
        {
            using var form = CreateCellDisplayForm(recordRows: true);
            var grid = Get<DataGridView>(form, "grid");
            Invoke(form, "SelectRecord", 0);
            var hex = Get<TextBox>(form, "hexTextBox").Text;
            var excel = (IReadOnlyList<IReadOnlyList<ExcelCell>>)Invoke(form, "BuildExcelRows")!;
            var values = Get<List<List<string>>>(form, "records").SelectMany(r => r).ToArray();
            var before = Path.Combine(temporaryDirectory, "field-number-before.dat");
            var after = Path.Combine(temporaryDirectory, "field-number-after.dat");
            var options = Invoke(form, "CurrentSaveOptions");
            Invoke(form, "WriteData", before, options);
            Get<ToolStripMenuItem>(form, "fieldRowsButton").PerformClick();
            Equal(true, grid.RowHeadersVisible);
            Invoke(form, "SelectRecord", 0);
            Equal(hex, Get<TextBox>(form, "hexTextBox").Text);
            Get<ToolStripMenuItem>(form, "recordRowsButton").PerformClick();
            Equal(false, grid.RowHeadersVisible);
            Invoke(form, "SelectRecord", 0);
            Equal(hex, Get<TextBox>(form, "hexTextBox").Text);
            var currentExcel = (IReadOnlyList<IReadOnlyList<ExcelCell>>)Invoke(form, "BuildExcelRows")!;
            Equal(true, excel.SelectMany(r => r).SequenceEqual(currentExcel.SelectMany(r => r)));
            Invoke(form, "WriteData", after, options);
            Equal(true, File.ReadAllBytes(before).SequenceEqual(File.ReadAllBytes(after)));
            Equal(true, values.SequenceEqual(Get<List<List<string>>>(form, "records").SelectMany(r => r)));
            Equal(false, Get<bool>(form, "hasUnsavedChanges"));
        });
        Test("Editing field rows with ordinal headers still updates the intended record and field", () =>
        {
            using var form = CreateCellDisplayForm(recordRows: false);
            var grid = Get<DataGridView>(form, "grid");
            var records = Get<List<List<string>>>(form, "records");
            var first = records[0][0];
            grid[3, 1].Value = "987654321098765";
            Invoke(form, "UpdateRecordValue", 1, 3);
            Equal("987654321098765", records[1][1]);
            Equal(first, records[0][0]);
            Equal("123456789012345", records[0][1]);
            Equal("2", grid.Rows[1].HeaderCell.Value);
        });
    }
}
