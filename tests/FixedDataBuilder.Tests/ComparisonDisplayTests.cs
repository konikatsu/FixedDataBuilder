using FixedDataBuilder;

internal static partial class Program
{
    private static void RunComparisonDisplayTests(string repo)
    {
        Test("Both comparison panes paint half and full-width spaces, retaining review color and ruler", () =>
        {
            var fields = new[] { new FieldDefinition("TEXT", FieldDataType.Text, 80) };
            var data = ComparisonDataSet.FromRecords(fields, new[] { new[] { "A B　C" } }, new(DataFileFormat.Csv));
            using var form = new VisualComparisonForm(data) { ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(-30000, -30000), Height = 1100 };
            Invoke(form, "SetInputDataSet", data);
            form.Show(); Application.DoEvents();
            foreach (var input in new[] { true, false })
            {
                var pane = VisualPane(form, input); var grid = VisualGrid(pane);
                var row = Get<int>(pane, "metadataRows"); grid.Columns[1].Width = 300;
                grid.CurrentCell = grid[0, row]; grid.ClearSelection(); grid.Refresh(); Application.DoEvents();
                using var bitmap = new Bitmap(grid.Width, grid.Height);
                grid.DrawToBitmap(bitmap, new Rectangle(Point.Empty, grid.Size));
                Equal(true, HasYellow(bitmap, grid.GetCellDisplayRectangle(1, row, false)));
                Get<CheckBox>(pane, "showSpaces").Checked = false; grid.Refresh(); Application.DoEvents();
                using var without = new Bitmap(grid.Width, grid.Height);
                grid.DrawToBitmap(without, new Rectangle(Point.Empty, grid.Size));
                Equal(false, HasYellow(without, grid.GetCellDisplayRectangle(1, row, false)));
                Equal("A B　C", data.Records[0][0]);
            }
        });
        Test("Literal space marker glyphs are not highlighted as actual spaces", () =>
        {
            var data = ComparisonDataSet.FromRecords([new("TEXT", FieldDataType.Text, 10)], new[] { new[] { "･□" } }, new(DataFileFormat.Csv));
            using var form = new VisualComparisonForm(data) { ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(-30000, -30000) };
            form.Show(); Application.DoEvents();
            var pane = VisualPane(form, false); var grid = VisualGrid(pane); var row = Get<int>(pane, "metadataRows");
            grid.CurrentCell = grid[0, row]; grid.ClearSelection(); grid.Refresh(); Application.DoEvents();
            using var bitmap = new Bitmap(grid.Width, grid.Height);
            grid.DrawToBitmap(bitmap, new Rectangle(Point.Empty, grid.Size));
            Equal(false, HasYellow(bitmap, grid.GetCellDisplayRectangle(1, row, false)));
        });
        Test("Wrap uses each side's encoding and grows data rows without modifying values", () =>
        {
            var fields = new[] { new FieldDefinition("TEXT", FieldDataType.Text, 30) };
            var utf8 = ComparisonDataSet.FromRecords(fields, new[] { new[] { "漢字 A B" } }, new(DataFileFormat.Csv, CsvTextEncoding.Utf8));
            var utf16 = ComparisonDataSet.FromRecords(fields, new[] { new[] { "漢字 A B" } }, new(DataFileFormat.Csv, CsvTextEncoding.Utf16Le));
            using var form = new VisualComparisonForm(utf8, wrapOptions: new(true, 4, 4));
            Invoke(form, "SetInputDataSet", utf16);
            foreach (var pane in new[] { VisualPane(form, true), VisualPane(form, false) })
            {
                var grid = VisualGrid(pane); var row = Get<int>(pane, "metadataRows");
                var lines = (IReadOnlyList<string>)Invoke(pane, "ComparisonDisplayLines", 0, 0)!;
                Equal(true, lines.Count > 1); Equal("漢字 A B", string.Concat(lines));
                Equal(true, grid.Rows[row].Height > grid.RowTemplate.Height);
                Get<CheckBox>(pane, "showByteRuler").Checked = false;
                Equal(true, grid.Rows[Get<int>(pane, "metadataRows")].Height > grid.RowTemplate.Height);
            }
            Equal("漢字 A B", utf8.Records[0][0]); Equal("漢字 A B", utf16.Records[0][0]);
        });
        Test("Fixed national text uses UTF-32LE byte counts and retains full-width padding", () =>
        {
            var fields = new[] { new FieldDefinition("N", FieldDataType.FullWidthText, 4) };
            var data = ComparisonDataSet.FromRecords(fields, new[] { new[] { "漢" } },
                new(DataFileFormat.FixedLength, NationalEncoding: NationalTextEncoding.Utf32));
            using var form = new VisualComparisonForm(data, wrapOptions: new(true, 8, 8));
            var pane = VisualPane(form, false);
            var lines = (IReadOnlyList<string>)Invoke(pane, "ComparisonDisplayLines", 0, 0)!;
            Equal(2, lines.Count); Equal("漢　", lines[0]); Equal("　　", lines[1]);
            Equal("漢", data.Records[0][0]);
        });
        Test("Disabled wrapping preserves single-line comparison behavior", () =>
        {
            var data = ComparisonDataSet.FromRecords([new("TEXT", FieldDataType.Text, 20)], new[] { new[] { "long string here" } }, new(DataFileFormat.Csv));
            using var form = new VisualComparisonForm(data);
            var pane = VisualPane(form, false); var grid = VisualGrid(pane);
            Equal(1, ((IReadOnlyList<string>)Invoke(pane, "ComparisonDisplayLines", 0, 0)!).Count);
            Equal(grid.RowTemplate.Height, grid.Rows[Get<int>(pane, "metadataRows")].Height);
        });
        _ = repo;
    }

    private static bool HasYellow(Bitmap bitmap, Rectangle cell)
    {
        cell = Rectangle.Intersect(cell, new Rectangle(Point.Empty, bitmap.Size));
        for (var y = cell.Top; y < cell.Bottom; y++)
        for (var x = cell.Left; x < cell.Right; x++)
        {
            var color = bitmap.GetPixel(x, y);
            if (color.R == 255 && color.G == 250 && color.B == 205) return true;
        }
        return false;
    }
}
