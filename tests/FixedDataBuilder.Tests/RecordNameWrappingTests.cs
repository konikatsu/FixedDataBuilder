using System.Globalization;
using System.Text.Json;
using FixedDataBuilder;

internal static partial class Program
{
    private static void RunRecordNameWrappingTests(string repo)
    {
        Test("Header wrapping uses five fullwidth characters per ten halfwidth slots and limits to three lines", () =>
        {
            static int Measure(string text)
            {
                var count = 0;
                var parts = StringInfo.GetTextElementEnumerator(text);
                while (parts.MoveNext()) count += parts.GetTextElement()[0] < 128 || parts.GetTextElement() == "…" ? 1 : 2;
                return count;
            }
            Equal("漢字項目名", string.Join('|', HeaderTextWrapper.Lines("漢字項目名", 10, Measure)));
            Equal("漢字項目名|称", string.Join('|', HeaderTextWrapper.Lines("漢字項目名称", 10, Measure)));
            Equal("1234567890|AB", string.Join('|', HeaderTextWrapper.Lines("1234567890AB", 10, Measure)));
            var longName = HeaderTextWrapper.Lines(new string('漢', 1000), 10, Measure);
            Equal(3, longName.Count);
            Equal(true, longName[^1].EndsWith("…"));
            Equal(true, longName.All(line => Measure(line) <= 10));
            Equal("👩‍👩‍👧‍👦|e\u0301", string.Join('|', HeaderTextWrapper.Lines("👩‍👩‍👧‍👦e\u0301", 2, Measure)));
            Equal("AB||CD", string.Join('|', HeaderTextWrapper.Lines("AB\r\n\r\nCD", 10, Measure)));
            Throws<ArgumentOutOfRangeException>(() => HeaderTextWrapper.Lines("a", 0, Measure));
            Throws<ArgumentOutOfRangeException>(() => HeaderTextWrapper.Lines("a", 10, Measure, 0));
        });
        Test("Long item names do not widen short CSV columns and retain complete names in tooltips and Excel", () =>
        {
            using var form = CreateRecordNameTestForm("name-width");
            var grid = Get<DataGridView>(form, "grid");
            Equal(grid.Columns[2].Width, grid.Columns[1].Width);
            Equal(grid.Columns[1].MinimumWidth, grid.Columns[1].Width);
            var name = Get<List<FieldDefinition>>(form, "fields")[0].Name;
            Equal(name, grid[1, 0].Value);
            Equal(true, grid[1, 0].ToolTipText.Contains(name));
            var lines = (IReadOnlyList<string>)Invoke(form, "RecordItemNameLines", 1)!;
            Equal(3, lines.Count);
            Equal(true, lines[^1].EndsWith("…"));
            Equal(true, grid.Rows[0].Height <= 3 * (grid.Font.Height + 2) + 6);
            var excel = (IReadOnlyList<IReadOnlyList<ExcelCell>>)Invoke(form, "BuildExcelRows")!;
            Equal(name, excel[1][1].Text);
            Equal(false, Get<bool>(form, "hasUnsavedChanges"));
        });
        Test("Manual width, ten-slot minimum and zoom recompute the name height without losing the source text", () =>
        {
            using var form = CreateRecordNameTestForm("name-resize");
            var grid = Get<DataGridView>(form, "grid");
            var name = grid[1, 0].Value!.ToString()!;
            var minimum = grid.Columns[1].MinimumWidth;
            grid.Columns[1].Width = TextRenderer.MeasureText(name, grid.Font, Size.Empty,
                TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine).Width + 12;
            Equal(1, ((IReadOnlyList<string>)Invoke(form, "RecordItemNameLines", 1)!).Count);
            Equal(grid.RowTemplate.Height, grid.Rows[0].Height);
            grid.Columns[1].Width = 1;
            Equal(minimum, grid.Columns[1].Width);
            Equal(3, ((IReadOnlyList<string>)Invoke(form, "RecordItemNameLines", 1)!).Count);
            Invoke(form, "ChangeZoomFromWheel", grid.Font.SizeInPoints >= 48 ? -120 : 120);
            Equal(true, grid.Columns[1].MinimumWidth != minimum);
            Equal(name, grid[1, 0].Value);
            Equal(false, Get<bool>(form, "hasUnsavedChanges"));
        });
        Test("Data width and wrapping remain independent of the capped name row and both image outputs use the same header", () =>
        {
            using var form = CreateRecordNameTestForm("name-image");
            var records = Get<List<List<string>>>(form, "records");
            records[0][0] = new string('A', 50);
            Invoke(form, "RefreshGrid");
            var grid = Get<DataGridView>(form, "grid");
            var width = grid.Columns[1].Width;
            var before = JsonSerializer.Serialize(records);
            Invoke(form, "ApplyDisplayWrapOptions", new DisplayWrapOptions(true, 1, 10));
            Equal(true, grid.Columns[1].Width < width);
            Equal(true, grid.Rows[0].Height <= 3 * (grid.Font.Height + 2) + 6);
            var start = (int)Invoke(form, "RecordRowsDataStartIndex")!;
            Equal(true, grid.Rows[start].Height >= 5 * (grid.Font.Height + 2) + 6);
            var header = grid.Rows[0].Height;
            var plan = Invoke(form, "BuildGridCapturePlan")!;
            var (columns, rows) = CaptureAxes(plan);
            foreach (var page in rows)
                Equal(header, page.Slices.Single(slice => slice.Index == 0).Length);
            using var png = (Bitmap)Invoke(form, "RenderGridCapturePage", columns[0], rows[0], "test")!;
            CopyToImageTestSink(form, image => Equal(true, image.Width > 0 && image.Height > 0));
            Equal(before, JsonSerializer.Serialize(records));
        });
        Test("Names and original column numbers survive hiding and view switches with minimums confined to horizontal data columns", () =>
        {
            using var form = CreateRecordNameTestForm("name-modes");
            Get<HashSet<int>>(form, "hiddenFieldIndexes").Add(1);
            Invoke(form, "RefreshGrid");
            var grid = Get<DataGridView>(form, "grid");
            Equal("No.3", grid.Columns[2].HeaderText);
            Equal("管理番号", grid[2, 0].Value);
            Invoke(form, "SetSingleRecordView", true);
            Equal(true, grid.Columns[0].MinimumWidth < 10);
            Invoke(form, "SetSingleRecordView", false);
            Equal(true, grid.Columns[1].MinimumWidth >= 10);
            var fields = Get<List<FieldDefinition>>(form, "fields");
            fields[0] = fields[0] with { Name = "番号" };
            Invoke(form, "RefreshGrid");
            Equal("番号", string.Concat((IReadOnlyList<string>)Invoke(form, "RecordItemNameLines", 1)!));
            Equal(grid.RowTemplate.Height, grid.Rows[0].Height);
            Equal(false, Get<bool>(form, "hasUnsavedChanges"));
        });
    }

    private static MainForm CreateRecordNameTestForm(string name)
    {
        var definition = Path.Combine(temporaryDirectory, name + "-definition.csv");
        var data = Path.Combine(temporaryDirectory, name + "-data.csv");
        DefinitionCsvWriter.Write(definition, [new(string.Concat(Enumerable.Repeat("申告報告義務者以外の所在地", 8)), "X(1)"),
            new("区分", "9(1)"), new("管理番号", "X(1)")]);
        File.WriteAllText(data, "A,1,B\r\nC,2,D\r\n");
        return new MainForm(["--definition", definition, "--data", data, "--layout", "record"]);
    }
}
