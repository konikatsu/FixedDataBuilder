using System.Text.Json;
using FixedDataBuilder;

internal static partial class Program
{
    private static void RunVisualComparisonRulerTests(string repo)
    {
        Test("Comparison rulers start visible and each side switches independently after the metadata", () =>
        {
            var fields = new[] { new FieldDefinition("FIELD", FieldDataType.Text, 20, Comment: "期待値を確認") };
            var data = ComparisonDataSet.FromRecords(fields, new[] { new[] { "ABCDE" } }, new(DataFileFormat.Csv));
            using var form = new VisualComparisonForm(data);
            var input = VisualPane(form, true); var output = VisualPane(form, false);
            Get<CheckBox>(input, "showByteRuler").Checked = false;
            Invoke(form, "SetInputDataSet", data);
            Equal(-1, Get<int>(input, "rulerRow"));
            Get<CheckBox>(input, "showByteRuler").Checked = true;
            foreach (var pane in new[] { input, output })
            {
                var toggle = Get<CheckBox>(pane, "showByteRuler");
                Equal("バイト目盛り", toggle.Text); Equal("ShowByteRuler", toggle.Name); Equal(true, toggle.Checked);
                Equal(Get<int>(pane, "commentRow") + 1, Get<int>(pane, "rulerRow"));
                Equal(Get<int>(pane, "metadataRows") - 1, Get<int>(pane, "rulerRow"));
                Equal("バイト目盛り", VisualGrid(pane)[0, Get<int>(pane, "rulerRow")].Value);
                Equal(true, VisualGrid(pane).ReadOnly);
                var format = new DataGridViewCellFormattingEventArgs(1, Get<int>(pane, "rulerRow"), "", typeof(string), new DataGridViewCellStyle());
                Invoke(pane, "FormatCell", null, format);
                Equal(Color.FromArgb(125, 132, 142), format.CellStyle!.ForeColor);
                Equal(format.CellStyle.ForeColor, format.CellStyle.SelectionForeColor);
                Equal(true, ((string)Invoke(pane, "CellToolTip", Get<int>(pane, "rulerRow"), 1)!).Contains("5バイト", StringComparison.Ordinal));
            }
            var inputRows = VisualGrid(input).RowCount; var outputRows = VisualGrid(output).RowCount;
            Get<CheckBox>(input, "showByteRuler").Checked = false;
            Equal(-1, Get<int>(input, "rulerRow"));
            Equal(inputRows - 1, VisualGrid(input).RowCount);
            Equal(outputRows, VisualGrid(output).RowCount);
            Equal(true, Get<CheckBox>(output, "showByteRuler").Checked);
            Get<CheckBox>(input, "showByteRuler").Checked = true;
            Equal(inputRows, VisualGrid(input).RowCount);
        });
        Test("Comparison CSV rulers use the selected codec and maximum logical bytes including spaces", () =>
        {
            foreach (var codec in new[] { CsvTextEncoding.Utf8, CsvTextEncoding.ShiftJis, CsvTextEncoding.StrictShiftJis })
            {
                var data = ComparisonDataSet.FromRecords(
                    [new("TEXT", FieldDataType.Text, 1), new("NUM", FieldDataType.PlainNumber, 1), new("QUOTED", FieldDataType.Text, 1)],
                    new[] { new[] { "あA", " 0007 ", "A,\"B" }, new[] { "カタカナ", "0008", "" } },
                    new(DataFileFormat.Csv, codec));
                var before = JsonSerializer.Serialize(data.Records);
                using var form = new VisualComparisonForm(data);
                var pane = VisualPane(form, false); var grid = VisualGrid(pane); var row = Get<int>(pane, "rulerRow");
                foreach (DataGridViewColumn column in grid.Columns) if (column.Index > 0) column.Width = 500;
                Equal(ByteRulerText.Format(codec == CsvTextEncoding.Utf8 ? 12 : 8, 100), grid[1, row].Value);
                Equal(ByteRulerText.Format(6, 100), grid[2, row].Value);
                Equal(ByteRulerText.Format(4, 100), grid[3, row].Value);
                Get<CheckBox>(pane, "showSpaces").Checked = false;
                Equal(ByteRulerText.Format(6, 100), grid[2, row].Value);
                Equal(before, JsonSerializer.Serialize(data.Records));
            }
        });
        Test("Comparison fixed rulers count national and packed storage bytes rather than displayed characters", () =>
        {
            foreach (var nationalWidth in new[] { 2, 4 })
            {
                FieldDefinition[] fields = [new("TEXT", FieldDataType.Text, 7),
                    new("NATIONAL", FieldDataType.FullWidthText, 3, NationalByteWidth: nationalWidth),
                    new("SIGNED", FieldDataType.PackedSigned, 5), new("UNSIGNED", FieldDataType.PackedUnsigned, 6)];
                var data = ComparisonDataSet.FromRecords(fields, new[] { new[] { "A", "東京", "12345", "123456" } },
                    new(NationalEncoding: nationalWidth == 4 ? NationalTextEncoding.Utf32 : NationalTextEncoding.Utf16));
                using var form = new VisualComparisonForm(data);
                var pane = VisualPane(form, false); var grid = VisualGrid(pane); var row = Get<int>(pane, "rulerRow");
                foreach (DataGridViewColumn column in grid.Columns) if (column.Index > 0) column.Width = 500;
                Equal(ByteRulerText.Format(7, 100), grid[1, row].Value);
                Equal(ByteRulerText.Format(nationalWidth * 3, 100), grid[2, row].Value);
                Equal(ByteRulerText.Format(3, 100), grid[3, row].Value);
                Equal(ByteRulerText.Format(3, 100), grid[4, row].Value);
                Equal(Get<int>(pane, "positionRow") + 1, row);
            }
        });
        Test("Comparison ruler caches follow the newly loaded side while preserving the other side and empty records", () =>
        {
            var fields = new[] { new FieldDefinition("TEXT", FieldDataType.Text, 1) };
            using var form = new VisualComparisonForm(ComparisonDataSet.FromRecords(fields,
                new[] { new[] { "カタカナ" } }, new(DataFileFormat.Csv, CsvTextEncoding.Utf8)));
            var output = VisualPane(form, false); var grid = VisualGrid(output);
            grid.Columns[1].Width = 500;
            Equal(ByteRulerText.Format(12, 100), grid[1, Get<int>(output, "rulerRow")].Value);
            Invoke(form, "SetInputDataSet", ComparisonDataSet.FromRecords(fields, new[] { new[] { "ABCDE" } }, new(DataFileFormat.Csv)));
            var input = VisualPane(form, true); var inputValue = VisualGrid(input)[1, Get<int>(input, "metadataRows")].Value;
            Invoke(form, "SetOutputDataSet", ComparisonDataSet.FromRecords(fields, new[] { new[] { "カタカナ" } }, new(DataFileFormat.Csv, CsvTextEncoding.ShiftJis)));
            grid.Columns[1].Width = 500;
            Equal(ByteRulerText.Format(8, 100), grid[1, Get<int>(output, "rulerRow")].Value);
            Equal(inputValue, VisualGrid(input)[1, Get<int>(input, "metadataRows")].Value);
            Invoke(form, "SetOutputDataSet", ComparisonDataSet.FromRecords(fields, Array.Empty<string[]>(), new(DataFileFormat.Csv)));
            Equal("0バイト", grid[1, Get<int>(output, "rulerRow")].Value);
            Equal(Get<int>(output, "metadataRows"), grid.RowCount);
        });
        Test("Comparison CSV ruler reports unencodable values without replacing data and recovers on reload", () =>
        {
            FieldDefinition[] fields = [new("TEXT", FieldDataType.Text, 1), new("ASCII", FieldDataType.Text, 1)];
            string[][] records = [["😀", "ABCDE"]];
            using var form = new VisualComparisonForm(ComparisonDataSet.FromRecords(fields, records, new(DataFileFormat.Csv, CsvTextEncoding.ShiftJis)));
            var pane = VisualPane(form, false); var grid = VisualGrid(pane);
            foreach (DataGridViewColumn column in grid.Columns) if (column.Index > 0) column.Width = 500;
            Equal("計測エラー", grid[1, Get<int>(pane, "rulerRow")].Value);
            Equal(ByteRulerText.Format(5, 100), grid[2, Get<int>(pane, "rulerRow")].Value);
            Equal("😀", grid[1, Get<int>(pane, "metadataRows")].Value);
            Invoke(form, "SetOutputDataSet", ComparisonDataSet.FromRecords(fields, records, new(DataFileFormat.Csv, CsvTextEncoding.Utf8)));
            grid.Columns[1].Width = 500;
            Equal(ByteRulerText.Format(4, 100), grid[1, Get<int>(pane, "rulerRow")].Value);
            Equal("😀", records[0][0]);
        });
        Test("Comparison CSV aliases have no fabricated physical byte ruler", () =>
        {
            var data = ComparisonDataSet.FromRecords(
                [new("BASE", FieldDataType.Text, 5), new("ALIAS", FieldDataType.PlainNumber, 2, RedefinesName: "BASE")],
                new[] { new[] { "ABCDE", "0" } }, new(DataFileFormat.Csv));
            using var form = new VisualComparisonForm(data);
            var pane = VisualPane(form, false); var grid = VisualGrid(pane);
            Equal("（CSV列なし）", grid[2, Get<int>(pane, "rulerRow")].Value);
            Equal("0", data.Records[0][1]);
        });
        Test("Comparison ruler toggles retain record selection viewport widths heights and independent swap settings", () =>
        {
            using var form = new VisualComparisonForm(VisualData("OUT", 200, 20))
                { ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(-30000, -30000), Width = 1100, Height = 1050 };
            Invoke(form, "SetInputDataSet", VisualData("IN", 150, 20));
            form.Show(); Application.DoEvents();
            var output = VisualPane(form, false); var input = VisualPane(form, true);
            var grid = VisualGrid(output); var inputGrid = VisualGrid(input); var beforeMetadata = Get<int>(output, "metadataRows");
            grid.Columns[8].Width = 320;
            grid.Rows[beforeMetadata + 110].Height = 65;
            grid.CurrentCell = grid[8, beforeMetadata + 110];
            grid.FirstDisplayedScrollingRowIndex = beforeMetadata + 100;
            grid.FirstDisplayedScrollingColumnIndex = 8;
            inputGrid.CurrentCell = inputGrid[3, Get<int>(input, "metadataRows") + 20];
            var inputSelection = inputGrid.CurrentCell; var inputFirst = inputGrid.FirstDisplayedScrollingRowIndex;
            var firstRecord = grid.FirstDisplayedScrollingRowIndex - beforeMetadata;
            var firstColumn = grid.FirstDisplayedScrollingColumnIndex; var horizontalOffset = grid.HorizontalScrollingOffset;
            var footer = Get<TextBox>(output, "selectedValue").Text;
            foreach (var enabled in new[] { false, true })
            {
                Get<CheckBox>(output, "showByteRuler").Checked = enabled;
                Application.DoEvents();
                var metadata = Get<int>(output, "metadataRows");
                Equal(110, grid.CurrentCell!.RowIndex - metadata); Equal(8, grid.CurrentCell.ColumnIndex);
                Equal(firstRecord, grid.FirstDisplayedScrollingRowIndex - metadata);
                Equal(firstColumn, grid.FirstDisplayedScrollingColumnIndex); Equal(horizontalOffset, grid.HorizontalScrollingOffset);
                Equal(320, grid.Columns[8].Width); Equal(65, grid.Rows[metadata + 110].Height);
                Equal(footer, Get<TextBox>(output, "selectedValue").Text);
                Equal(inputSelection, inputGrid.CurrentCell); Equal(inputFirst, inputGrid.FirstDisplayedScrollingRowIndex);
            }
            Get<CheckBox>(input, "showByteRuler").Checked = false;
            Invoke(form, "SwapLists"); Application.DoEvents();
            Equal(false, Get<CheckBox>(input, "showByteRuler").Checked);
            Equal(true, Get<CheckBox>(output, "showByteRuler").Checked);
            Equal(110, grid.CurrentCell!.RowIndex - Get<int>(output, "metadataRows"));
        });
        Test("Comparison ruler fits changed column widths and keeps bounded continuation separate from data", () =>
        {
            var value = new string('A', 80);
            var data = ComparisonDataSet.FromRecords([new("TEXT", FieldDataType.Text, 1)], new[] { new[] { value } }, new(DataFileFormat.Csv));
            using var form = new VisualComparisonForm(data);
            var pane = VisualPane(form, false); var grid = VisualGrid(pane); var row = Get<int>(pane, "rulerRow");
            grid.Columns[1].Width = grid.Columns[1].MinimumWidth;
            Equal(true, grid[1, row].Value!.ToString()!.EndsWith("…", StringComparison.Ordinal));
            grid.Columns[1].Width = 2000;
            Equal(ByteRulerText.Format(80, 1000), grid[1, row].Value);
            Equal(value, data.Records[0][0]);
        });
        Test("Comparison five-byte blue guides reach data cells and disappear with the ruler", () =>
        {
            var data = ComparisonDataSet.FromRecords([new("TEXT", FieldDataType.Text, 40)],
                new[] { new[] { new string('A', 40) } }, new(DataFileFormat.Csv));
            using var form = new VisualComparisonForm(data)
                { ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(-30000, -30000), Height = 1100 };
            form.Show(); Application.DoEvents();
            var pane = VisualPane(form, false); var grid = VisualGrid(pane);
            grid.Columns[1].Width = 500; grid.ClearSelection(); grid.CurrentCell = grid[0, Get<int>(pane, "metadataRows")];
            foreach (var enabled in new[] { true, false })
            {
                Get<CheckBox>(pane, "showByteRuler").Checked = enabled;
                grid.ClearSelection(); grid.CurrentCell = grid[0, Get<int>(pane, "metadataRows")];
                grid.Refresh(); Application.DoEvents();
                using var bitmap = new Bitmap(grid.Width, grid.Height);
                grid.DrawToBitmap(bitmap, new Rectangle(Point.Empty, grid.Size));
                var cell = grid.GetCellDisplayRectangle(1, Get<int>(pane, "metadataRows"), false);
                var x = cell.Left + 4 + (int)Math.Round(5 * (float)Invoke(pane, "ByteRulerDigitWidth")!);
                Equal(enabled, VisualCellHasBlueGuide(bitmap, new Rectangle(x, cell.Top + 3, 1, cell.Height - 6)));
            }
        });
        _ = repo;
    }

    private static bool VisualCellHasBlueGuide(Bitmap bitmap, Rectangle rectangle)
    {
        var bounds = Rectangle.Intersect(rectangle, new Rectangle(Point.Empty, bitmap.Size));
        for (var y = bounds.Top; y < bounds.Bottom; y++)
        for (var x = bounds.Left; x < bounds.Right; x++)
        {
            var color = bitmap.GetPixel(x, y);
            if (color.B > color.R + 30 && color.B > color.G + 10) return true;
        }
        return false;
    }
}
