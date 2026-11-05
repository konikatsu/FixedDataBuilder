using FixedDataBuilder;

internal static partial class Program
{
    private static void RunHeaderRowsTests(string repo)
    {
        Test("Visible reload button works before opening files and encoding help remains visible in save modes", () =>
        {
            using var form = new MainForm();
            form.ShowInTaskbar = false;
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-30000, -30000);
            form.Show();
            var reload = Descendants(form).OfType<Button>().Single(button => button.Name == "ReloadFiles");
            reload.PerformClick();
            Equal(0, Get<List<List<string>>>(form, "records").Count);
            Equal(true, Get<ToolStripStatusLabel>(form, "statusLabel").Text?.Contains("再読み込みするファイルがありません") == true);
            foreach (var format in new[] { DataFileFormat.FixedLength, DataFileFormat.Csv })
            {
                using var save = new DataSaveOptionsForm(new DataSaveOptions(format, CsvTextEncoding.Utf8,
                    RecordSeparatorMode.None, NationalTextEncoding.ShiftJis));
                var help = save.Controls.OfType<Label>().Single(label => label.Name == "EncodingHelp");
                save.ShowInTaskbar = false;
                save.StartPosition = FormStartPosition.Manual;
                save.Location = new Point(-30000, -30000);
                save.Show();
                Equal(true, help.Visible);
                Equal(true, help.Bottom < save.Controls.OfType<Button>().Min(button => button.Top));
            }
        });
        Test("Separated headers stay frozen and read-only, with original byte positions after hiding a field", () =>
        {
            using var form = CreateCellDisplayForm(recordRows: true);
            var grid = Get<DataGridView>(form, "grid");
            Equal(8, grid.Rows.Count);
            for (var index = 0; index < 3; index++)
            {
                Equal(true, grid.Rows[index].Frozen);
                Equal(true, grid.Rows[index].ReadOnly);
                grid.CurrentCell = grid[1, index];
                Equal(false, grid.BeginEdit(false));
                Invoke(form, "DuplicateRecord");
                Invoke(form, "DeleteRecord");
                Equal(5, Get<List<List<string>>>(form, "records").Count);
                Equal("", Get<TextBox>(form, "hexTextBox").Text);
            }
            Get<HashSet<int>>(form, "hiddenFieldIndexes").Add(0);
            Invoke(form, "RefreshGrid");
            Equal("No.2", grid.Columns[1].HeaderText);
            Equal("番号", grid[1, 0].Value);
            Equal("X(15)", grid[1, 1].Value);
            Equal("[23-37]", grid[1, 2].Value);
            Equal("1", grid[0, 3].Value);
            Equal(5, Get<List<List<string>>>(form, "records").Count);
        });
        Test("Record actions and HEX select data rows correctly below the separated headers", () =>
        {
            using var form = CreateCellDisplayForm(recordRows: true);
            var grid = Get<DataGridView>(form, "grid");
            Invoke(form, "SelectRecord", 0);
            Equal(3, grid.CurrentCell.RowIndex);
            Equal(0, Invoke(form, "CurrentRecordIndex"));
            Equal(true, Get<TextBox>(form, "hexTextBox").Text.StartsWith("31 32 33"));
            Invoke(form, "DuplicateRecord");
            Equal(6, Get<List<List<string>>>(form, "records").Count);
            Equal(1, Invoke(form, "CurrentRecordIndex"));
            Invoke(form, "DeleteRecord");
            Equal(5, Get<List<List<string>>>(form, "records").Count);
            Invoke(form, "AddRecord");
            Equal(6, Get<List<List<string>>>(form, "records").Count);
            Equal(5, Invoke(form, "CurrentRecordIndex"));
            Equal("6", grid[0, grid.CurrentCell.RowIndex].Value);
        });
        Test("Excel separates numbers and names and still imports legacy two and three row headers", () =>
        {
            using var form = CreateCellDisplayForm(recordRows: true);
            var rows = (IReadOnlyList<IReadOnlyList<ExcelCell>>)Invoke(form, "BuildExcelRows")!;
            Equal(9, rows.Count);
            Equal("No.1", rows[0][1].Text);
            Equal("識別番号", rows[1][1].Text);
            Equal("X(22)", rows[2][1].Text);
            Equal("[1-22]", rows[3][1].Text);
            Equal("1", rows[4][0].Text);
            foreach (var row in rows.Take(4))
            foreach (var cell in row) Equal(ExcelCellKind.Header, cell.Kind);
            var path = Path.Combine(temporaryDirectory, "three-header-rows.xlsx");
            ExcelExporter.Write(path, "レコード縦", rows);
            var imported = ExcelImporter.Read(path);
            Equal(4, imported.Fields.Count);
            Equal(5, imported.Records.Count);
            Equal("1234567890123456789012", imported.Records[0][0]);
            Equal("123       9012", imported.Records[4][0]);

            var legacyRows = rows.Skip(1).Select(row => row.ToArray()).ToArray();
            legacyRows[0][0] = new ExcelCell("行", ExcelCellKind.Header);
            ExcelExporter.Write(path, "レコード縦", legacyRows);
            Equal(5, ExcelImporter.Read(path).Records.Count);
            var twoRowHeader = legacyRows.Take(2).Select(row => row.ToArray()).ToArray();
            twoRowHeader[1][0] = new ExcelCell("");
            ExcelExporter.Write(path, "レコード縦", twoRowHeader.Concat(legacyRows.Skip(3)).ToArray());
            var oldTwoRows = ExcelImporter.Read(path);
            Equal(5, oldTwoRows.Records.Count);
            Equal(true, imported.Records.SelectMany(row => row).SequenceEqual(oldTwoRows.Records.SelectMany(row => row)));
        });
        Test("OCCURS band and data stay below type and position rows and Excel excludes the band from data", () =>
        {
            using var form = new MainForm([
                "--definition", Path.Combine(repo, "samples", "copybook-group-occurs-definition.cbl"),
                "--data", Path.Combine(repo, "samples", "copybook-group-occurs-data-crlf-utf8-n-utf16le.dat"),
                "--separator", "crlf", "--national-encoding", "utf16", "--layout", "record"]);
            var grid = Get<DataGridView>(form, "grid");
            Equal("項目名", grid[0, 0].Value);
            Equal("型", grid[0, 1].Value);
            Equal("位置", grid[0, 2].Value);
            Equal("OCCURS", grid[0, 3].Value);
            Equal("1", grid[0, 4].Value);
            Invoke(form, "SelectRecord", 0);
            Equal(4, grid.CurrentCell.RowIndex);
            Equal(0, Invoke(form, "CurrentRecordIndex"));
            var path = Path.Combine(temporaryDirectory, "occurs-three-header-rows.xlsx");
            ExcelExporter.Write(path, "レコード縦", (IReadOnlyList<IReadOnlyList<ExcelCell>>)Invoke(form, "BuildExcelRows")!);
            var imported = ExcelImporter.Read(path);
            Equal(Get<List<List<string>>>(form, "records").Count, imported.Records.Count);
            Equal(grid[1, 4].Value?.ToString(), imported.Records[0][0]);
        });
    }
}
