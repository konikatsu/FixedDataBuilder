using System.Security.Cryptography;
using System.Text.Json;
using FixedDataBuilder;

internal static partial class Program
{
    private static void RunGridCaptureTests(string repo)
    {
        Test("A capture that fits on screen keeps the original one-page layout", () =>
        {
            using var form = CreateCellDisplayForm(true);
            form.ClientSize = new Size(1400, 900);
            var grid = Get<DataGridView>(form, "grid");
            foreach (DataGridViewColumn column in grid.Columns) column.Width = 80;
            foreach (DataGridViewRow row in grid.Rows) row.Height = 24;
            var bands = ((IReadOnlyList<GridCaptureBand> Columns, IReadOnlyList<GridCaptureBand> Rows))
                Invoke(form, "BuildGridCaptureBands")!;
            var expectedColumns = GridCapturePagination.Paginate(bands.Columns,
                Math.Clamp(grid.ClientSize.Width - SystemInformation.VerticalScrollBarWidth, 64, 4096));
            var expectedRows = GridCapturePagination.Paginate(bands.Rows,
                Math.Clamp(grid.ClientSize.Height - SystemInformation.HorizontalScrollBarHeight, 64, 4096));
            var (columns, rows) = CaptureAxes(Invoke(form, "BuildGridCapturePlan")!);
            Equal(1, expectedColumns.Count);
            Equal(1, expectedRows.Count);
            Equal(true, expectedColumns[0].Slices.SequenceEqual(columns[0].Slices));
            Equal(true, expectedRows[0].Slices.SequenceEqual(rows[0].Slices));
        });
        Test("Capture divides very tall records within bitmap limits without losing row pixels", () =>
        {
            using var form = CreateCaptureTestForm(true);
            var records = Get<List<List<string>>>(form, "records");
            while (records.Count < 450) records.Add(records[records.Count % 5].ToList());
            Invoke(form, "RefreshGrid");
            var grid = Get<DataGridView>(form, "grid");
            foreach (DataGridViewRow row in grid.Rows) if (!row.Frozen) row.Height = 80;
            var bands = ((IReadOnlyList<GridCaptureBand> Columns, IReadOnlyList<GridCaptureBand> Rows))
                Invoke(form, "BuildGridCaptureBands")!;
            var (columns, rows) = CaptureAxes(Invoke(form, "BuildGridCapturePlan")!);
            Equal(true, rows.Count > 1);
            foreach (var y in rows)
            foreach (var x in columns)
            {
                var width = Math.Max(640, x.Length);
                var height = y.Length + 126;
                Equal(true, height <= GridCaptureImageLayout.MaximumDimension);
                Equal(true, (long)width * height <= GridCaptureImageLayout.MaximumPixels);
            }
            AssertCaptureCoverage(bands.Rows.Where(b => !b.Repeat),
                rows.SelectMany(page => page.Slices).Where(slice => !slice.Repeated));
        });
        Test("Capture pagination repeats headings and covers every body pixel once", () =>
        {
            GridCaptureBand[] bands = [new(-1, 20, true), new(0, 30, true), new(1, 35, false), new(2, 240, false), new(3, 50, false)];
            var pages = GridCapturePagination.Paginate(bands, 150);
            Equal(true, pages.Count > 1);
            foreach (var page in pages)
            {
                Equal(true, page.Length <= 150);
                Equal(-1, page.Slices[0].Index);
                Equal(0, page.Slices[1].Index);
                Equal(true, page.Slices.Take(2).All(slice => slice.Repeated));
            }
            AssertCaptureCoverage(bands.Where(band => !band.Repeat), pages.SelectMany(page => page.Slices).Where(slice => !slice.Repeated));
        });
        Test("Oversized frozen headers and cells are split without losing their tails", () =>
        {
            GridCaptureBand[] bands = [new(-1, 15, true), new(0, 500, true), new(1, 390, false)];
            var pages = GridCapturePagination.Paginate(bands, 120);
            Equal(true, pages.SelectMany(page => page.Slices).All(slice => !slice.Repeated));
            AssertCaptureCoverage(bands, pages.SelectMany(page => page.Slices));
            Equal(390, pages.SelectMany(page => page.Slices).Where(slice => slice.Index == 1).Sum(slice => slice.Length));
            using var form = CreateCellDisplayForm(true);
            var dataRow = (int)Invoke(form, "RecordRowsDataStartIndex")!;
            using var full = new Bitmap(300, 240);
            using var joined = new Bitmap(300, 240);
            using (var graphics = Graphics.FromImage(full))
                Invoke(form, "PaintGridCaptureCell", graphics, 1, dataRow, new Rectangle(0, 0, 300, 240), new Rectangle(0, 0, 300, 240));
            using (var target = Graphics.FromImage(joined))
            for (var offset = 0; offset < 240; offset += 80)
            {
                using var tile = new Bitmap(300, 80);
                using (var graphics = Graphics.FromImage(tile))
                    Invoke(form, "PaintGridCaptureCell", graphics, 1, dataRow, new Rectangle(0, -offset, 300, 240), new Rectangle(0, 0, 300, 80));
                target.DrawImageUnscaled(tile, 0, offset);
            }
            for (var y = 0; y < full.Height; y++)
            for (var x = 0; x < full.Width; x++) Equal(full.GetPixel(x, y), joined.GetPixel(x, y));
        });
        Test("Capture rejects invalid sizes, duplicate cells and excessive pages", () =>
        {
            Equal(0, GridCapturePagination.Paginate([], 10).Count);
            Throws<ArgumentOutOfRangeException>(() => GridCapturePagination.Paginate([], 0));
            Throws<ArgumentException>(() => GridCapturePagination.Paginate([new(0, 0, false)], 10));
            Throws<ArgumentException>(() => GridCapturePagination.Paginate([new(0, 1, false), new(0, 2, false)], 10));
            Throws<InvalidOperationException>(() => GridCapturePagination.Paginate([new(0, 10001, false)], 1));
        });
        Test("Captured cells show half-width and full-width space markers", () =>
        {
            using var form = CreateCellDisplayForm(true);
            var grid = Get<DataGridView>(form, "grid");
            var row = (int)Invoke(form, "RecordRowsDataStartIndex")!;
            var field = Get<List<FieldDefinition>>(form, "fields")[0];
            var value = "A B　C";
            grid[1, row].Value = value;
            var visible = (string)Invoke(form, "GetVisibleTextValue", field, value)!;
            Equal(true, visible.StartsWith("A･B□C"));
            using var image = new Bitmap(400, 50);
            using (var graphics = Graphics.FromImage(image))
            {
                graphics.Clear(Color.White);
                Invoke(form, "PaintGridCaptureCell", graphics, 1, row,
                    new Rectangle(0, 0, 400, 50), new Rectangle(0, 0, 400, 50));
            }
            var yellow = Color.FromArgb(255, 250, 205).ToArgb();
            Equal(true, Enumerable.Range(0, image.Width)
                .Any(x => Enumerable.Range(0, image.Height).Any(y => image.GetPixel(x, y).ToArgb() == yellow)));
        });
        Test("Both list layouts save all tiles, metadata and hashes without changing selection, scroll or data", () =>
        {
            foreach (var horizontal in new[] { false, true })
            {
                using var form = CreateCaptureTestForm(horizontal);
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-30000, -30000);
                form.ShowInTaskbar = false;
                form.Show();
                form.ClientSize = new Size(780, 1000);
                Application.DoEvents();
                var grid = Get<DataGridView>(form, "grid");
                grid.CurrentCell = grid[horizontal ? 2 : 4, horizontal ? 7 : 1];
                grid.FirstDisplayedScrollingRowIndex = horizontal ? 5 : 1;
                grid.HorizontalScrollingOffset = 60;
                Invoke(form, "SetUnsavedChanges", true);
                var cell = grid.CurrentCell;
                var first = grid.FirstDisplayedScrollingRowIndex;
                var offset = grid.HorizontalScrollingOffset;
                var records = JsonSerializer.Serialize(Get<List<List<string>>>(form, "records"));
                var excel = JsonSerializer.Serialize(Invoke(form, "BuildExcelRows"));
                var plan = Invoke(form, "BuildGridCapturePlan")!;
                var (columns, rows) = CaptureAxes(plan);
                Equal(true, columns.Count > 1 && rows.Count == 1);
                var expectedRows = grid.Rows.Cast<DataGridViewRow>()
                    .Count(row => row.Visible && !row.IsNewRow) + (grid.ColumnHeadersVisible ? 1 : 0);
                Equal(expectedRows, rows[0].Slices.Count);
                var folder = Path.Combine(temporaryDirectory, "capture-" + horizontal);
                Directory.CreateDirectory(folder);
                CompleteCaptureTask((Task)Invoke(form, "WriteGridCaptureAsync", folder, "test", plan,
                    CancellationToken.None, new Action<int, string>((_, _) => { }))!);
                using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(folder, "capture.json")));
                var root = manifest.RootElement;
                Equal("complete", root.GetProperty("status").GetString());
                Equal(columns.Count * rows.Count, root.GetProperty("saved_images").GetInt32());
                Equal(true, root.GetProperty("unsaved_changes").GetBoolean());
                foreach (var page in root.GetProperty("pages").EnumerateArray())
                {
                    var path = Path.Combine(folder, page.GetProperty("file").GetString()!);
                    Equal(page.GetProperty("sha256").GetString(), Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
                    using var image = new Bitmap(path);
                    Equal(true, image.Width > 0 && image.Height > 126);
                }
                Equal(cell, grid.CurrentCell);
                Equal(first, grid.FirstDisplayedScrollingRowIndex);
                Equal(offset, grid.HorizontalScrollingOffset);
                Equal(records, JsonSerializer.Serialize(Get<List<List<string>>>(form, "records")));
                Equal(excel, JsonSerializer.Serialize(Invoke(form, "BuildExcelRows")));
                Equal(true, Get<bool>(form, "hasUnsavedChanges"));
            }
        });
        Test("Cancelling capture retains completed images and an explicitly incomplete manifest", () =>
        {
            using var form = CreateCaptureTestForm(true);
            var grid = Get<DataGridView>(form, "grid");
            var selection = grid.CurrentCell;
            var folder = Path.Combine(temporaryDirectory, "capture-cancelled");
            Directory.CreateDirectory(folder);
            using var cancellation = new CancellationTokenSource();
            var task = (Task)Invoke(form, "WriteGridCaptureAsync", folder, "test", Invoke(form, "BuildGridCapturePlan")!,
                cancellation.Token, new Action<int, string>((_, _) => cancellation.Cancel()))!;
            Throws<OperationCanceledException>(() => CompleteCaptureTask(task));
            using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(folder, "capture.json")));
            Equal("cancelled", manifest.RootElement.GetProperty("status").GetString());
            Equal(1, manifest.RootElement.GetProperty("saved_images").GetInt32());
            Equal(1, Directory.GetFiles(folder, "*.png").Length);
            Equal(selection, grid.CurrentCell);
        });
        Test("Capture never overwrites an existing image when a destination conflicts", () =>
        {
            using var form = CreateCaptureTestForm(false);
            var folder = Path.Combine(temporaryDirectory, "capture-conflict");
            Directory.CreateDirectory(folder);
            var original = Path.Combine(folder, "test_p0001_y001_x001.png");
            File.WriteAllText(original, "preserve existing file");
            var task = (Task)Invoke(form, "WriteGridCaptureAsync", folder, "test", Invoke(form, "BuildGridCapturePlan")!,
                CancellationToken.None, new Action<int, string>((_, _) => { }))!;
            Throws<IOException>(() => CompleteCaptureTask(task));
            Equal("preserve existing file", File.ReadAllText(original));
            using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(folder, "capture.json")));
            Equal("error", manifest.RootElement.GetProperty("status").GetString());
            Equal(0, manifest.RootElement.GetProperty("saved_images").GetInt32());
        });
        Test("Five-byte guides extend into data cells and ruler text is muted", () =>
        {
            Equal("5,10,15,20", string.Join(',', ByteRulerText.GuidePositions(22, 100)));
            Equal("5,10,15", string.Join(',', ByteRulerText.GuidePositions(100, 19)));
            Equal(0, ByteRulerText.GuidePositions(4, 100).Count());
            Equal(409, ByteRulerText.GuidePositions(int.MaxValue, int.MaxValue).Count());
            using var form = CreateCellDisplayForm(true);
            Get<ToolStripMenuItem>(form, "byteRulerButton").PerformClick();
            var grid = Get<DataGridView>(form, "grid");
            var ruler = Get<int>(form, "recordRulerRowIndex");
            var start = (int)Invoke(form, "RecordRowsDataStartIndex")!;
            Equal(Color.FromArgb(125, 132, 142), grid.Rows[ruler].DefaultCellStyle.ForeColor);
            using var bitmap = new Bitmap(300, 70);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.White);
                Invoke(form, "PaintGridCaptureCell", graphics, 1, start, new Rectangle(0, 0, 300, 70), new Rectangle(0, 0, 300, 70));
            }
            var x = 4 + (int)Math.Round(5 * (float)Invoke(form, "ByteRulerDigitWidth")!);
            Equal(true, Enumerable.Range(2, 65).Any(y => { var color = bitmap.GetPixel(x, y); return color.B > color.R + 30 && color.B > color.G + 10; }));
            Get<ToolStripMenuItem>(form, "byteRulerButton").PerformClick();
            using var plain = new Bitmap(300, 70);
            using (var graphics = Graphics.FromImage(plain))
            {
                graphics.Clear(Color.White);
                Invoke(form, "PaintGridCaptureCell", graphics, 1, (int)Invoke(form, "RecordRowsDataStartIndex")!,
                    new Rectangle(0, 0, 300, 70), new Rectangle(0, 0, 300, 70));
            }
            Equal(false, Enumerable.Range(2, 65).Any(y => { var color = plain.GetPixel(x, y); return color.B > color.R + 30 && color.B > color.G + 10; }));
        });
        Test("Capture includes hidden-field mapping, ruler, wrapped values and OCCURS without exporting them as data", () =>
        {
            using var form = new MainForm(["--definition", Path.Combine(repo, "samples", "copybook-group-occurs-definition.cbl"),
                "--data", Path.Combine(repo, "samples", "copybook-group-occurs-data-crlf-utf8-n-utf16le.dat"),
                "--separator", "crlf", "--national-encoding", "utf16", "--layout", "record"]);
            Get<HashSet<int>>(form, "hiddenFieldIndexes").Add(0);
            Invoke(form, "RefreshGrid");
            Get<ToolStripMenuItem>(form, "byteRulerButton").PerformClick();
            Invoke(form, "ApplyDisplayWrapOptions", new DisplayWrapOptions(true, 1, 4, true, 5));
            var before = JsonSerializer.Serialize(Get<List<List<string>>>(form, "records"));
            var plan = Invoke(form, "BuildGridCapturePlan")!;
            var (columns, rows) = CaptureAxes(plan);
            foreach (var y in rows)
            foreach (var x in columns)
            {
                using var bitmap = (Bitmap)Invoke(form, "RenderGridCapturePage", x, y, "test")!;
                Equal(true, bitmap.Width <= 4096 && bitmap.Height <= GridCaptureImageLayout.MaximumDimension);
                Equal(true, (long)bitmap.Width * bitmap.Height <= GridCaptureImageLayout.MaximumPixels);
            }
            Equal(false, Get<List<int>>(form, "recordGridColumnFieldIndexes").Contains(0));
            Equal(before, JsonSerializer.Serialize(Get<List<List<string>>>(form, "records")));
            var excel = (IReadOnlyList<IReadOnlyList<ExcelCell>>)Invoke(form, "BuildExcelRows")!;
            Equal(false, excel.Any(row => row[0].Text == "バイト目盛り"));
        });
        Test("Capture menu follows list mode and uses the original file name as prefix", () =>
        {
            using var form = CreateCellDisplayForm(true);
            var menu = Get<ToolStripMenuItem>(form, "captureAllImagesButton");
            Equal(true, menu.Enabled);
            form.GetType().GetField("saveDataPath", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(form, @"C:\sample\元データ.csv");
            Equal("元データ", Invoke(form, "CaptureFilePrefix"));
            Invoke(form, "SetSingleRecordView", true);
            Equal(false, menu.Enabled);
            Invoke(form, "SetSingleRecordView", false);
            Equal(true, menu.Enabled);
            using var empty = new MainForm();
            Equal(false, Get<ToolStripMenuItem>(empty, "captureAllImagesButton").Enabled);
        });
    }

    private static void AssertCaptureCoverage(IEnumerable<GridCaptureBand> bands, IEnumerable<GridCaptureSlice> source)
    {
        var slices = source.ToArray();
        foreach (var band in bands)
        {
            var offset = 0;
            foreach (var slice in slices.Where(slice => slice.Index == band.Index))
            {
                Equal(offset, slice.Offset);
                Equal(band.Length, slice.FullLength);
                offset += slice.Length;
            }
            Equal(band.Length, offset);
        }
    }

    private static (IReadOnlyList<GridCaptureAxisPage> Columns, IReadOnlyList<GridCaptureAxisPage> Rows) CaptureAxes(object plan) =>
        ((IReadOnlyList<GridCaptureAxisPage>)plan.GetType().GetProperty("Columns")!.GetValue(plan)!,
         (IReadOnlyList<GridCaptureAxisPage>)plan.GetType().GetProperty("Rows")!.GetValue(plan)!);

    private static MainForm CreateCaptureTestForm(bool horizontal)
    {
        var form = CreateCellDisplayForm(horizontal);
        var records = Get<List<List<string>>>(form, "records");
        while (records.Count < 18) records.Add(records[records.Count % 5].ToList());
        form.ClientSize = new Size(780, 520);
        Invoke(form, "RefreshGrid");
        var grid = Get<DataGridView>(form, "grid");
        foreach (DataGridViewColumn column in grid.Columns) column.Width = column.Frozen ? 95 : 240;
        foreach (DataGridViewRow row in grid.Rows) if (!row.Frozen) row.Height = 80;
        return form;
    }

    private static void CompleteCaptureTask(Task task)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!task.IsCompleted)
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Capture did not finish");
            Application.DoEvents();
            Thread.Sleep(1);
        }
        task.GetAwaiter().GetResult();
    }
}
