using System.Reflection;
using System.Text;
using FixedDataBuilder;

internal static partial class Program
{
    private static void VerifyProvidedColumnView(string path)
    {
        var original = File.ReadAllBytes(path);
        foreach (var viewportWidth in new[] { 1600, 1100 })
        {
            using var form = new ColumnTextViewForm { ClientSize = new Size(viewportWidth, 1110) };
            form.PerformLayout();
            form.LoadFile(path);
            ShowColumnViewForTest(form);
            var document = Get<ColumnTextDocument>(form, "document");
            var map = Get<ColumnAdaptivePagination>(form, "pageMap");
            var panel = Get<TableLayoutPanel>(form, "columnsPanel");
            var panes = Get<List<WheelZoomDataGridView>>(form, "panes");
            var visited = 0;
            for (var page = 0; page < map.Count; page++)
            {
                Invoke(form, "MovePage", page);
                Equal(map[page].Start, visited);
                Equal(map[page].Columns, panes.Count);
                var usedWidth = 0f;
                for (var column = 0; column < panes.Count; column++)
                {
                    var pane = panes[column];
                    var required = pane.Columns[1].GetPreferredWidth(DataGridViewAutoSizeColumnMode.AllCells, true);
                    if (pane.Columns[1].Width < required ||
                        pane.ClientSize.Width < pane.Columns[0].Width + pane.Columns[1].Width)
                    {
                        throw new InvalidOperationException(
                            $"Clipped pane at {viewportWidth}px, page {page + 1}, pane {column + 1}: " +
                            $"body width {pane.Columns[1].Width}, preferred {required}, pane width {pane.ClientSize.Width}.");
                    }
                    usedWidth += panel.ColumnStyles[column].Width;
                    for (var row = 0; row < pane.RowCount; row++)
                    {
                        Equal(visited + 1, pane[0, row].Value);
                        Equal(true, Equals(document.Lines[visited], pane[1, row].Value));
                        visited++;
                    }
                }
                Equal(map[page].End, visited);
                if (page + 1 < map.Count && panes.Count < 32)
                    Equal(true, panel.ClientSize.Width - usedWidth < map.PaneWidth(page + 1, 0));
            }
            Equal(document.Lines.Count, visited);
            Invoke(form, "MovePage", 0);
            Console.WriteLine($"{viewportWidth}px: {visited} lines across {map.Count} pages, {panes.Count} panes on page one");
        }
        Equal(true, original.SequenceEqual(File.ReadAllBytes(path)));
    }

    private static void ShowColumnViewForTest(ColumnTextViewForm form)
    {
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(-30000, -30000);
        form.ShowInTaskbar = false;
        form.Show();
        var timer = System.Diagnostics.Stopwatch.StartNew();
        do
        {
            Application.DoEvents();
            if (!Get<bool>(form, "layoutPending") && !Get<bool>(form, "reflowPending")) return;
            Thread.Sleep(5);
        }
        while (timer.Elapsed < TimeSpan.FromSeconds(10));
        throw new TimeoutException("Column view did not finish laying out.");
    }

    private static void RunColumnViewTests()
    {
        Test("Adaptive column pages cover every line once when widths change", () =>
        {
            var map = ColumnAdaptivePagination.Build(23, 3, 250,
                (first, _) => first < 9 ? 70 : first < 15 ? 180 : 90);
            var seen = new List<int>();
            for (var p = 0; p < map.Count; p++)
            {
                var page = map[p];
                seen.AddRange(Enumerable.Range(page.Start, page.End - page.Start));
                for (var i = page.Start; i < page.End; i++) Equal(p, map.PageForLine(i));
            }
            Equal(true, seen.SequenceEqual(Enumerable.Range(0, 23)));
            Equal(true, map[0].Columns > map[1].Columns);
            Equal(1, ColumnAdaptivePagination.Build(0, 3, 1, (_, _) => 20).Count);
        });
        string Write(string name, byte[] bytes)
        {
            var path = Path.Combine(temporaryDirectory, name); File.WriteAllBytes(path, bytes); return path;
        }
        Test("Column pagination reads top to bottom then left to right with exact page boundaries", () =>
        {
            var layout = ColumnPageLayout.Fit(900, 830, 100, 10, 30);
            Equal(9, layout.Columns); Equal(80, layout.RowsPerColumn); Equal(720, layout.Capacity);
            Equal(2, layout.PageCount(721)); Equal(1, layout.PageCount(720));
            Equal(0, layout.LineIndex(0, 0, 0, 721)); Equal(79, layout.LineIndex(0, 0, 79, 721));
            Equal(80, layout.LineIndex(0, 1, 0, 721)); Equal(719, layout.LineIndex(0, 8, 79, 721));
            Equal(720, layout.LineIndex(1, 0, 0, 721)); Equal(-1, layout.LineIndex(1, 0, 1, 721));
        });
        Test("Every source line appears exactly once across automatically sized column pages", () =>
        {
            foreach (var size in new[] { new Size(100, 20), new Size(900, 830), new Size(1920, 1000) })
            {
                var layout = ColumnPageLayout.Fit(size.Width, size.Height, 120, 16, 40);
                var seen = new List<int>();
                for (var page = 0; page < layout.PageCount(1537); page++)
                for (var column = 0; column < layout.Columns; column++)
                for (var row = 0; row < layout.RowsPerColumn; row++)
                {
                    var line = layout.LineIndex(page, column, row, 1537); if (line >= 0) seen.Add(line);
                }
                Equal(true, seen.SequenceEqual(Enumerable.Range(0, 1537)));
            }
        });
        Test("Column layout handles empty files tiny windows invalid slots and large page counts", () =>
        {
            var layout = ColumnPageLayout.Fit(0, 0, 10, 10, 30);
            Equal(1, layout.Columns); Equal(1, layout.RowsPerColumn); Equal(1, layout.PageCount(0));
            Equal(-1, layout.LineIndex(0, 0, 0, 0)); Equal(-1, layout.LineIndex(0, -1, 0, 1));
            Equal(-1, layout.LineIndex(0, 1, 0, 1)); Equal(0, layout.ClampPage(9, 0));
            Equal(int.MaxValue, layout.PageCount(int.MaxValue));
            Throws<ArgumentOutOfRangeException>(() => ColumnPageLayout.Fit(100, 100, 0, 10, 10));
        });
        Test("Column widths preserve pane order and leave unused space after content that fits", () =>
        {
            var widths = ColumnPageLayout.FitPaneWidths([80, 0, 20], 150);
            Equal(true, widths.SequenceEqual([80, 0, 20]));
        });
        Test("Column widths fit the viewport and share remaining pixels among wider panes", () =>
        {
            var widths = ColumnPageLayout.FitPaneWidths([300, 30, 50, 0, 300], 291);
            Equal(true, widths.SequenceEqual([105, 30, 50, 0, 106]));
            Equal(true, ColumnPageLayout.FitPaneWidths([300, 30, 0], 0).SequenceEqual([0, 0, 0]));
        });
        Test("Raw column documents preserve CSV quoting blank lines spaces tabs and original file bytes", () =>
        {
            var text = "\"001\",\" A　 \"\r\n\r\n\"002\",\"two\nphysical lines\"\r\n\t end  ";
            var bytes = new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(text)).ToArray();
            var path = Write("column-raw.csv", bytes);
            var document = ColumnTextDocument.Load(path);
            Equal(ColumnTextEncoding.Utf8, document.Encoding); Equal(5, document.Lines.Count);
            Equal("\"001\",\" A　 \"", document.Lines[0]); Equal("", document.Lines[1]);
            Equal("\"002\",\"two", document.Lines[2]); Equal("physical lines\"", document.Lines[3]);
            Equal("\t end  ", document.Lines[4]); Equal(true, bytes.SequenceEqual(File.ReadAllBytes(path)));
        });
        Test("Column documents handle mixed newline styles empty files and final delimiters without a phantom line", () =>
        {
            Equal(0, ColumnTextDocument.Load(Write("column-empty.txt", [])).Lines.Count);
            var path = Write("column-lines.txt", Encoding.UTF8.GetBytes("a\r\nb\nc\rd\n\n"));
            Equal("a|b|c|d|", string.Join("|", ColumnTextDocument.Load(path).Lines));
        });
        Test("Column document encoding selection supports strict SJIS MS932 and BOM national codecs", () =>
        {
            foreach (var encoding in Enum.GetValues<ColumnTextEncoding>())
            {
                var codec = ColumnTextDocument.Codec(encoding);
                var path = Write($"column-{encoding}.txt", codec.GetPreamble().Concat(codec.GetBytes("東京  \r\n次の行")).ToArray());
                var loaded = ColumnTextDocument.Load(path, encoding);
                Equal("東京  ", loaded.Lines[0]); Equal("次の行", loaded.Lines[1]);
                if (encoding != ColumnTextEncoding.ShiftJis) Equal(encoding, ColumnTextDocument.Load(path).Encoding);
            }
            var extended = Write("column-ms932.txt", JapaneseEncodings.ForCsv(CsvTextEncoding.ShiftJis).GetBytes("①髙﨑"));
            Equal(ColumnTextEncoding.Ms932, ColumnTextDocument.Load(extended).Encoding);
            Throws<DecoderFallbackException>(() => ColumnTextDocument.Load(extended, ColumnTextEncoding.ShiftJis));
        });
        Test("Column documents reject binary invalid Unicode unsupported BOMs and oversized files", () =>
        {
            Throws<InvalidDataException>(() => ColumnTextDocument.Load(Write("column-binary.dat", [0, 1, 2])));
            Throws<DecoderFallbackException>(() => ColumnTextDocument.Load(Write("column-invalid.txt", [0xef, 0xbb, 0xbf, 0xff])));
            Throws<InvalidDataException>(() => ColumnTextDocument.Load(Write("column-be.txt", [0xfe, 0xff, 0, 65])));
            var path = Path.Combine(temporaryDirectory, "column-large.dat");
            using (var stream = File.Create(path)) stream.SetLength(ColumnTextDocument.MaximumBytes + 1L);
            Throws<InvalidDataException>(() => ColumnTextDocument.Load(path));
        });
        Test("Column form exposes navigation and readonly panes before a file is opened", () =>
        {
            using var form = new ColumnTextViewForm();
            Equal(false, Get<Button>(form, "previous").Enabled); Equal(false, Get<Button>(form, "next").Enabled);
            Equal(false, Get<NumericUpDown>(form, "pageNumber").Enabled);
            Equal(true, Descendants(form).OfType<Button>().Any(button => button.Name == "OpenColumnFile"));
        });
        Test("Column form paginates source line numbers and clamps navigation at both ends", () =>
        {
            using var form = new ColumnTextViewForm();
            form.LoadFile(Write("column-navigation.txt", Encoding.UTF8.GetBytes(string.Join("\n", Enumerable.Range(1, 2500).Select(i => $"{i:D4},データ")))));
            var layout = Get<ColumnPageLayout>(form, "pagination");
            Invoke(form, "MovePage", 1);
            var panes = Get<List<WheelZoomDataGridView>>(form, "panes");
            var first = Get<ColumnAdaptivePagination>(form, "pageMap")[1].Start + 1;
            Equal(first, panes[0][0, 0].Value);
            Equal($"{first:D4},データ", panes[0][1, 0].Value);
            Equal(true, panes.All(pane => pane.ReadOnly && !pane.AllowUserToAddRows && pane.VirtualMode));
            Invoke(form, "MovePage", int.MaxValue); Equal(false, Get<Button>(form, "next").Enabled);
            Invoke(form, "MovePage", -1); Equal(false, Get<Button>(form, "previous").Enabled);
        });
        Test("Column form recalculates the pane widths when navigating between short and long pages", () =>
        {
            using var form = new ColumnTextViewForm { ClientSize = new Size(900, 500) };
            var lines = Enumerable.Repeat("A", 2500).Concat(Enumerable.Repeat(new string('X', 200), 2500));
            form.LoadFile(Write("column-page-widths.txt", Encoding.UTF8.GetBytes(string.Join("\n", lines))));
            var layout = Get<ColumnPageLayout>(form, "pagination");
            var panel = Get<TableLayoutPanel>(form, "columnsPanel");
            var shortWidth = panel.ColumnStyles[0].Width;
            Invoke(form, "MovePage", Get<ColumnAdaptivePagination>(form, "pageMap").PageForLine(2600));
            Equal(true, panel.ColumnStyles[0].Width > shortWidth);
            Invoke(form, "MovePage", 0);
            Equal(shortWidth, panel.ColumnStyles[0].Width);
        });
        Test("Resizing and zooming the column form reflows while keeping the previous first line on the page", () =>
        {
            using var form = new ColumnTextViewForm();
            form.LoadFile(Write("column-reflow.txt", Encoding.UTF8.GetBytes(string.Join("\n", Enumerable.Range(1, 5000)))));
            Invoke(form, "MovePage", 2);
            var before = Get<ColumnPageLayout>(form, "pagination");
            var anchor = Get<ColumnAdaptivePagination>(form, "pageMap")[Get<int>(form, "pageIndex")].Start;
            form.ClientSize = new Size(900, 500); form.PerformLayout(); Invoke(form, "Reflow");
            var after = Get<ColumnPageLayout>(form, "pagination");
            var first = Get<ColumnAdaptivePagination>(form, "pageMap")[Get<int>(form, "pageIndex")].Start;
            Equal(true, first <= anchor && anchor < first + after.Capacity);
            var oldRows = after.RowsPerColumn;
            Get<NumericUpDown>(form, "fontSize").Value = 18;
            Equal(true, Get<ColumnPageLayout>(form, "pagination").RowsPerColumn < oldRows);
        });
        Test("Failed column loads retain the previous document page and physical source values", () =>
        {
            using var form = new ColumnTextViewForm();
            form.LoadFile(Write("column-good.txt", Encoding.UTF8.GetBytes(string.Join("\n", Enumerable.Range(1, 2000)))));
            Invoke(form, "MovePage", 1);
            var previous = Get<ColumnTextDocument>(form, "document"); var page = Get<int>(form, "pageIndex");
            Throws<InvalidDataException>(() => form.LoadFile(Write("column-bad.txt", [0, 1, 2])));
            Equal(true, ReferenceEquals(previous, Get<ColumnTextDocument>(form, "document")));
            Equal(page, Get<int>(form, "pageIndex"));
        });
        Test("Column view entry points exist without changing main data or editing state", () =>
        {
            using var main = new MainForm();
            Equal(true, Get<Button>(main, "columnViewButton").Enabled);
            Equal(true, Get<ToolStripMenuItem>(main, "columnViewMenu").Enabled);
            Equal(false, Get<bool>(main, "hasUnsavedChanges"));
            Equal(0, Get<List<FieldDefinition>>(main, "fields").Count);
        });
    }
}
