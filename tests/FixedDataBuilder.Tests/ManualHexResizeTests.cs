using System.Reflection;
using FixedDataBuilder;

internal static partial class Program
{
    private static void RunManualHexResizeTests(string repo)
    {
        Test("HEX grip drags by screen coordinates and stops on outside release or lost capture", () =>
        {
            using var form = CreateCellDisplayForm(recordRows: false);
            var area = Get<Panel>(form, "hexArea");
            var grip = Get<Panel>(form, "hexResizeGrip");
            var initialHeight = area.Height;
            var start = grip.PointToScreen(new Point(10, 3));
            SendHexGripMouse(grip, "OnMouseDown", MouseButtons.Right, new Point(10, 3));
            SendHexGripMouse(grip, "OnMouseMove", MouseButtons.Right, new Point(10, -100));
            Equal(initialHeight, area.Height);
            Equal(null, Get<int?>(form, "hexResizeStartScreenY"));

            SendHexGripMouse(grip, "OnMouseDown", MouseButtons.Left, grip.PointToClient(start));
            Equal(true, grip.Capture);
            try
            {
                SendHexGripMouse(grip, "OnMouseMove", MouseButtons.Left,
                    grip.PointToClient(new Point(start.X, start.Y - 100)));
                Equal(initialHeight + 100, area.Height);
                // The grip has moved: local mouse coordinates must not accumulate the first delta.
                SendHexGripMouse(grip, "OnMouseMove", MouseButtons.Left,
                    grip.PointToClient(new Point(start.X, start.Y - 130)));
                Equal(initialHeight + 130, area.Height);
                SendHexGripMouse(grip, "OnMouseUp", MouseButtons.Left, new Point(grip.Width + 50, 200));
                Equal(false, grip.Capture);
                SendHexGripMouse(grip, "OnMouseMove", MouseButtons.None, new Point(10, -200));
                Equal(initialHeight + 130, area.Height);

                SendHexGripMouse(grip, "OnMouseDown", MouseButtons.Left, new Point(10, 3));
                Equal(true, grip.Capture);
                grip.Capture = false;
                Equal(null, Get<int?>(form, "hexResizeStartScreenY"));
                SendHexGripMouse(grip, "OnMouseMove", MouseButtons.Left, new Point(10, -200));
                Equal(initialHeight + 130, area.Height);
            }
            finally { grip.Capture = false; }
        });

        Test("HEX resizing keeps both areas usable at limits and retains chosen height on window expansion", () =>
        {
            using var form = CreateCellDisplayForm(recordRows: false);
            var area = Get<Panel>(form, "hexArea");
            var grip = Get<Panel>(form, "hexResizeGrip");
            var grid = Get<DataGridView>(form, "grid");
            Equal(false, Descendants(form).Any(control => control is SplitContainer or Splitter));
            var minimumHex = Math.Max(64, (int)Math.Round(64 * form.DeviceDpi / 96f));
            var minimumGrid = Math.Max(100, (int)Math.Round(100 * form.DeviceDpi / 96f));
            Invoke(form, "SetHexAreaHeight", int.MinValue);
            Equal(minimumHex, area.Height);
            Equal(true, Get<TextBox>(form, "hexTextBox").Height > 0);
            Invoke(form, "SetHexAreaHeight", int.MaxValue);
            Equal(true, grid.Height >= minimumGrid);
            Equal(grid.Bottom, area.Top);
            Equal(true, area.Height > minimumHex);
            Equal(grip.Bottom, grip.Parent!.Controls.Cast<Control>().Single(c => c != grip).Top);

            form.Size = form.MinimumSize;
            form.PerformLayout();
            Equal(true, area.Height >= minimumHex);
            Equal(true, grid.Height >= minimumGrid);
            Equal(grid.Bottom, area.Top);
            Equal(true, Get<TextBox>(form, "hexTextBox").Height > 0);
            var retainedHeight = area.Height;
            form.Size = new Size(1100, 740);
            form.PerformLayout();
            Equal(retainedHeight, area.Height);
            Equal(true, grid.Height > minimumGrid);
            Invoke(form, "SetHexAreaHeight", 200);
            form.WindowState = FormWindowState.Minimized;
            Invoke(form, "SetHexAreaHeight", 0);
            Equal(200, area.Height);
            form.WindowState = FormWindowState.Normal;
            form.PerformLayout();
            Equal(200, area.Height);
            Equal(true, area.AllowDrop && grip.AllowDrop);
        });

        Test("Manual HEX resizing preserves selection, No., display limits, saved bytes and Excel in both layouts", () =>
        {
            foreach (var recordRows in new[] { false, true })
            {
                using var form = CreateCellDisplayForm(recordRows);
                var area = Get<Panel>(form, "hexArea");
                var grid = Get<DataGridView>(form, "grid");
                var row = recordRows ? (int)Invoke(form, "RecordRowsDataStartIndex")! : 0;
                var column = recordRows ? 1 : 2;
                grid.CurrentCell = grid[column, row];
                Invoke(form, "UpdateHexView");
                Invoke(form, "ApplyDisplayWrapOptions", new DisplayWrapOptions(true, 1, 10, true, 12));
                var selectedCell = grid.CurrentCell;
                var hex = Get<TextBox>(form, "hexTextBox").Text;
                var spaces = Get<Label>(form, "selectionSpaceLabel").Text;
                var values = Get<List<List<string>>>(form, "records").SelectMany(r => r).ToArray();
                var excel = (IReadOnlyList<IReadOnlyList<ExcelCell>>)Invoke(form, "BuildExcelRows")!;
                var before = Path.Combine(temporaryDirectory, $"manual-hex-before-{recordRows}.dat");
                var after = Path.Combine(temporaryDirectory, $"manual-hex-after-{recordRows}.dat");
                var saveOptions = Invoke(form, "CurrentSaveOptions");
                Invoke(form, "WriteData", before, saveOptions);

                Invoke(form, "SetHexAreaHeight", 200);
                Equal(200, area.Height);
                Equal(selectedCell, grid.CurrentCell);
                Equal(hex, Get<TextBox>(form, "hexTextBox").Text);
                Equal(spaces, Get<Label>(form, "selectionSpaceLabel").Text);
                Equal("1234567890|12…", string.Join("|", (IReadOnlyList<string>)Invoke(form, "WrappedDisplayLines", row, column)!));
                if (!recordRows)
                {
                    Equal("No.", grid.TopLeftHeaderCell.Value);
                    Equal("1|2|3|4", string.Join("|", grid.Rows.Cast<DataGridViewRow>().Select(r => r.HeaderCell.Value)));
                }
                else Equal(false, grid.RowHeadersVisible);

                var afterExcel = (IReadOnlyList<IReadOnlyList<ExcelCell>>)Invoke(form, "BuildExcelRows")!;
                Equal(true, excel.SelectMany(r => r).SequenceEqual(afterExcel.SelectMany(r => r)));
                Invoke(form, "WriteData", after, saveOptions);
                Equal(true, File.ReadAllBytes(before).SequenceEqual(File.ReadAllBytes(after)));
                Equal(true, values.SequenceEqual(Get<List<List<string>>>(form, "records").SelectMany(r => r)));
                Get<ToolStripMenuItem>(form, recordRows ? "fieldRowsButton" : "recordRowsButton").PerformClick();
                Equal(200, area.Height);
                Invoke(form, "SelectRecord", 0);
                Equal(hex, Get<TextBox>(form, "hexTextBox").Text);
                Equal(false, Get<bool>(form, "hasUnsavedChanges"));
            }
        });
    }

    private static void SendHexGripMouse(Control grip, string method, MouseButtons button, Point location) =>
        typeof(Control).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(grip,
            [new MouseEventArgs(button, 1, location.X, location.Y, 0)]);
}
