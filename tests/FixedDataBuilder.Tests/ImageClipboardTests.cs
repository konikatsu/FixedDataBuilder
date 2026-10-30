using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using FixedDataBuilder;

internal static partial class Program
{
    private static void RunImageClipboardTests(string repo)
    {
        Test("Single-image layout includes all columns and rows with headings exactly once", () =>
        {
            var image = GridCaptureImageLayout.Create([new(-1, 40, true), new(0, 120, true), new(1, 300, false), new(2, 300, false)],
                [new(-1, 30, true), new(0, 40, true), new(1, 120, false), new(2, 180, false)], 126);
            Equal(new Size(760, 496), image.ImageSize);
            Equal("-1,0,1,2", string.Join(',', image.Columns.Slices.Select(slice => slice.Index)));
            Equal("-1,0,1,2", string.Join(',', image.Rows.Slices.Select(slice => slice.Index)));
            Equal(true, image.Columns.Slices.Concat(image.Rows.Slices).All(slice => slice.Offset == 0 && slice.Length == slice.FullLength && !slice.Repeated));
            Equal(460, image.Columns.Slices[^1].Position);
            Equal(190, image.Rows.Slices[^1].Position);
        });
        Test("Clipboard image sizing guards dimensions, pixel area and malformed bands before allocation", () =>
        {
            Equal(32767, GridCaptureImageLayout.Create([new(0, 32767, false)], [new(0, 1, false)], 126).ImageSize.Width);
            Equal(32767, GridCaptureImageLayout.Create([new(0, 1, false)], [new(0, 32641, false)], 126).ImageSize.Height);
            Equal(new Size(8000, 4000), GridCaptureImageLayout.Create([new(0, 8000, false)], [new(0, 3874, false)], 126).ImageSize);
            Throws<InvalidOperationException>(() => GridCaptureImageLayout.Create([new(0, 32768, false)], [new(0, 1, false)], 126));
            Throws<InvalidOperationException>(() => GridCaptureImageLayout.Create([new(0, 1, false)], [new(0, 32642, false)], 126));
            Throws<InvalidOperationException>(() => GridCaptureImageLayout.Create([new(0, 8000, false)], [new(0, 3875, false)], 126));
            Throws<InvalidOperationException>(() => GridCaptureImageLayout.Create([new(0, int.MaxValue, false), new(1, int.MaxValue, false)], [new(0, int.MaxValue, false)], 126));
            Throws<ArgumentException>(() => GridCaptureImageLayout.Create([], [new(0, 10, false)], 126));
            Throws<ArgumentException>(() => GridCaptureImageLayout.Create([new(0, 0, false)], [new(0, 10, false)], 126));
            Throws<ArgumentException>(() => GridCaptureImageLayout.Create([new(0, 1, false), new(0, 1, false)], [new(0, 10, false)], 126));
            Throws<ArgumentOutOfRangeException>(() => GridCaptureImageLayout.Create([new(0, 10, false)], [new(0, 10, false)], -1));
        });
        Test("Both list layouts copy the farthest cells without moving selection, scrolling or changing data", () =>
        {
            foreach (var horizontal in new[] { false, true })
            {
                using var form = CreateCaptureTestForm(horizontal);
                var grid = Get<DataGridView>(form, "grid");
                grid.CurrentCell = grid[horizontal ? 2 : 4, horizontal ? 7 : 1];
                grid.FirstDisplayedScrollingRowIndex = horizontal ? 5 : 1;
                grid.HorizontalScrollingOffset = 60;
                Invoke(form, "SetUnsavedChanges", true);
                var cell = grid.CurrentCell;
                var row = grid.FirstDisplayedScrollingRowIndex;
                var offset = grid.HorizontalScrollingOffset;
                var records = JsonSerializer.Serialize(Get<List<List<string>>>(form, "records"));
                var excel = JsonSerializer.Serialize(Invoke(form, "BuildExcelRows"));
                var layout = (GridCaptureImageLayout)Invoke(form, "BuildClipboardImageLayout")!;
                Bitmap? retained = null;
                // Substitute only the OS clipboard sink; leave the complete renderer intact.
                CopyToImageTestSink(form, image => retained = new Bitmap(image));
                using var copied = retained!;
                Equal(layout.ImageSize, copied.Size);
                Equal(true, copied.Width > grid.Width);
                Equal(true, copied.Height > grid.Height);
                var lastColumn = layout.Columns.Slices[^1];
                var lastRow = layout.Rows.Slices[^1];
                using var expected = new Bitmap(lastColumn.Length, lastRow.Length);
                expected.SetResolution(grid.DeviceDpi, grid.DeviceDpi);
                using (var graphics = Graphics.FromImage(expected))
                    Invoke(form, "PaintGridCaptureCell", graphics, lastColumn.Index, lastRow.Index,
                        new Rectangle(Point.Empty, expected.Size), new Rectangle(Point.Empty, expected.Size));
                for (var y = 0; y < expected.Height; y++)
                for (var x = 0; x < expected.Width; x++)
                    Equal(expected.GetPixel(x, y), copied.GetPixel(lastColumn.Position + x, 100 + lastRow.Position + y));
                Equal(cell, grid.CurrentCell);
                Equal(row, grid.FirstDisplayedScrollingRowIndex);
                Equal(offset, grid.HorizontalScrollingOffset);
                Equal(records, JsonSerializer.Serialize(Get<List<List<string>>>(form, "records")));
                Equal(excel, JsonSerializer.Serialize(Invoke(form, "BuildExcelRows")));
                Equal(true, Get<bool>(form, "hasUnsavedChanges"));
                Equal(true, Get<ToolStripStatusLabel>(form, "statusLabel").Text?.Contains("画像コピーしました") == true);
            }
        });
        Test("Single-image copy keeps comments, OCCURS, rulers and hidden-field mapping", () =>
        {
            using var form = new MainForm(["--definition", Path.Combine(repo, "samples", "copybook-group-occurs-definition.cbl"),
                "--data", Path.Combine(repo, "samples", "copybook-group-occurs-data-crlf-utf8-n-utf16le.dat"),
                "--separator", "crlf", "--national-encoding", "utf16", "--layout", "record"]);
            var fields = Get<List<FieldDefinition>>(form, "fields");
            fields[1] = fields[1] with { Comment = "期待値・共通コメント\r\n改行も表示" };
            Get<HashSet<int>>(form, "hiddenFieldIndexes").Add(0);
            Invoke(form, "RefreshGrid");
            Get<ToolStripMenuItem>(form, "byteRulerButton").PerformClick();
            Invoke(form, "ApplyDisplayWrapOptions", new DisplayWrapOptions(true, 1, 4, true, 5));
            var layout = (GridCaptureImageLayout)Invoke(form, "BuildClipboardImageLayout")!;
            foreach (var name in new[] { "recordCommentRowIndex", "recordOccursRowIndex", "recordRulerRowIndex" })
                Equal(1, layout.Rows.Slices.Count(slice => slice.Index == Get<int>(form, name)));
            var indexes = Get<List<int>>(form, "recordGridColumnFieldIndexes");
            Equal(false, indexes.Contains(0));
            Equal(indexes.Count, layout.Columns.Slices.Count);
            var values = JsonSerializer.Serialize(Get<List<List<string>>>(form, "records"));
            var writes = 0;
            CopyToImageTestSink(form, image => { Equal(layout.ImageSize, image.Size); writes++; });
            Equal(1, writes);
            Equal(values, JsonSerializer.Serialize(Get<List<List<string>>>(form, "records")));
        });
        Test("Failed clipboard writes and oversized images do not report success or alter data", () =>
        {
            using var form = CreateCellDisplayForm(true);
            var status = Get<ToolStripStatusLabel>(form, "statusLabel").Text;
            var data = JsonSerializer.Serialize(Get<List<List<string>>>(form, "records"));
            Throws<ExternalException>(() => CopyToImageTestSink(form, _ => throw new ExternalException("clipboard unavailable")));
            Equal(status, Get<ToolStripStatusLabel>(form, "statusLabel").Text);
            var grid = Get<DataGridView>(form, "grid");
            foreach (DataGridViewColumn column in grid.Columns) column.Width = 12000;
            var writes = 0;
            Throws<InvalidOperationException>(() => CopyToImageTestSink(form, _ => writes++));
            Equal(0, writes);
            Equal(data, JsonSerializer.Serialize(Get<List<List<string>>>(form, "records")));
        });
        Test("Image-copy button and menu are available only for a nonempty visible list", () =>
        {
            using var form = CreateCellDisplayForm(true);
            var button = Get<Button>(form, "copyAllGridImageButton");
            var menu = Get<ToolStripMenuItem>(form, "copyAllGridImageMenu");
            form.Size = form.MinimumSize;
            form.PerformLayout();
            Equal(true, button.Enabled && menu.Enabled && button.Visible);
            Equal(true, button.Right <= button.Parent!.ClientSize.Width);
            Invoke(form, "SetSingleRecordView", true);
            Equal(false, button.Enabled || menu.Enabled);
            Invoke(form, "SetSingleRecordView", false);
            Equal(true, button.Enabled && menu.Enabled);
            foreach (var index in Enumerable.Range(0, Get<List<FieldDefinition>>(form, "fields").Count))
                Get<HashSet<int>>(form, "hiddenFieldIndexes").Add(index);
            Invoke(form, "RefreshGrid");
            Equal(false, button.Enabled || menu.Enabled);
            using var empty = new MainForm();
            Equal(false, Get<Button>(empty, "copyAllGridImageButton").Enabled);
            Equal(false, Get<ToolStripMenuItem>(empty, "copyAllGridImageMenu").Enabled);
        });
    }

    private static void CopyToImageTestSink(MainForm form, Action<Image> sink)
    {
        try { Invoke(form, "CopyAllGridImageTo", sink); }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
        }
    }
}
