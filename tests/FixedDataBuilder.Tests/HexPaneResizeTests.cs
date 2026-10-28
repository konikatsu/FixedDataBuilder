using FixedDataBuilder;

internal static partial class Program
{
    private static void RunHexPaneResizeTests(string repo)
    {
        Test("HEX splitter resizes the selected item area and preserves selection, data and HEX in both layouts", () =>
        {
            foreach (var recordRows in new[] { false, true })
            {
                using var form = CreateCellDisplayForm(recordRows);
                var split = Get<SplitContainer>(form, "dataHexSplit");
                var grid = Get<DataGridView>(form, "grid");
                var row = recordRows ? (int)Invoke(form, "RecordRowsDataStartIndex")! : 0;
                var column = recordRows ? 1 : 2;
                grid.CurrentCell = grid[column, row];
                Invoke(form, "UpdateHexView");
                var selected = grid.CurrentCell;
                var values = Get<List<List<string>>>(form, "records").SelectMany(r => r).ToArray();
                var hex = Get<TextBox>(form, "hexTextBox").Text;
                var height = split.Panel2.Height;
                split.SplitterDistance -= 120;
                Application.DoEvents();
                Equal(height + 120, split.Panel2.Height);
                Equal(selected, grid.CurrentCell);
                Equal(hex, Get<TextBox>(form, "hexTextBox").Text);
                Equal(true, values.SequenceEqual(Get<List<List<string>>>(form, "records").SelectMany(r => r)));
                Equal(false, Get<bool>(form, "hasUnsavedChanges"));
                Equal(false, grid.MultiSelect);
                var resized = split.Panel2.Height;
                form.Height += 100;
                Application.DoEvents();
                Equal(resized, split.Panel2.Height);
            }
        });
        Test("HEX pane remains usable at minimum window size and display limiting keeps the resized height", () =>
        {
            using var form = CreateCellDisplayForm(recordRows: true);
            var split = Get<SplitContainer>(form, "dataHexSplit");
            split.SplitterDistance -= 100;
            var height = split.Panel2.Height;
            Invoke(form, "ApplyDisplayWrapOptions", new DisplayWrapOptions(true, 20, 10, true, 12));
            Equal(height, split.Panel2.Height);
            var row = (int)Invoke(form, "RecordRowsDataStartIndex")!;
            Equal("1234567890|12…", string.Join("|", (IReadOnlyList<string>)Invoke(form, "WrappedDisplayLines", row, 1)!));
            form.Size = form.MinimumSize;
            Application.DoEvents();
            Equal(true, split.Panel1.Height >= split.Panel1MinSize);
            Equal(true, split.Panel2.Height >= split.Panel2MinSize);
            Equal(true, Get<TextBox>(form, "hexTextBox").Height > 0);
        });
    }
}
