using FixedDataBuilder;

internal static partial class Program
{
    private static void RunPaneAndRowHeightTests(string repo)
    {
        Test("HEX splitter changes lower height, preserves data and selection, and holds height on window growth", () =>
        {
            using var form = CreateCellDisplayForm(recordRows: true);
            var split = Get<SplitContainer>(form, "dataHexSplit");
            var grid = Get<DataGridView>(form, "grid");
            var values = Get<List<List<string>>>(form, "records").SelectMany(row => row).ToArray();
            Invoke(form, "SelectRecord", 0);
            var selected = grid.CurrentCell;
            Equal(Orientation.Horizontal, split.Orientation);
            Equal(split.Panel1, grid.Parent);
            split.SplitterDistance = split.Height - split.SplitterWidth - 180;
            Application.DoEvents();
            Equal(180, split.Panel2.Height);
            form.Height += 80;
            Application.DoEvents();
            Equal(180, split.Panel2.Height);
            Equal(selected, grid.CurrentCell);
            Equal(true, values.SequenceEqual(Get<List<List<string>>>(form, "records").SelectMany(row => row)));
            Equal(false, Get<bool>(form, "hasUnsavedChanges"));
            form.Size = form.MinimumSize;
            Application.DoEvents();
            Equal(true, split.Panel1.Height >= split.Panel1MinSize);
            Equal(true, split.Panel2.Height >= split.Panel2MinSize);
        });
        Test("Multiple selected data rows share a manual height while metadata and other rows stay unchanged", () =>
        {
            using var form = CreateCellDisplayForm(recordRows: true);
            var grid = Get<DataGridView>(form, "grid");
            var first = (int)Invoke(form, "RecordRowsDataStartIndex")!;
            var headerHeight = grid.Rows[0].Height;
            var otherHeight = grid.Rows[first + 1].Height;
            grid.ClearSelection();
            grid[0, 0].Selected = true;
            grid[1, first].Selected = true;
            grid[2, first + 2].Selected = true;
            Invoke(form, "SetSelectedRowHeight", 80);
            Equal(true, grid.MultiSelect);
            Equal(80, grid.Rows[first].Height);
            Equal(80, grid.Rows[first + 2].Height);
            Equal(headerHeight, grid.Rows[0].Height);
            Equal(otherHeight, grid.Rows[first + 1].Height);
            Invoke(form, "RefreshGridSizing");
            Equal(80, grid.Rows[first].Height);
            Equal(false, Get<bool>(form, "hasUnsavedChanges"));
            Invoke(form, "ResetSelectedRowHeights");
            Equal(grid.RowTemplate.Height, grid.Rows[first].Height);
            Equal(0, Get<HashSet<int>>(form, "manualHeightRows").Count);
        });
        Test("Manual row heights override wrapping until automatic adjustment and reset on grid rebuild", () =>
        {
            foreach (var recordRows in new[] { true, false })
            {
                using var form = CreateCellDisplayForm(recordRows);
                var grid = Get<DataGridView>(form, "grid");
                Invoke(form, "ApplyDisplayWrapOptions", new DisplayWrapOptions(true, 1, 5));
                var row = recordRows ? (int)Invoke(form, "RecordRowsDataStartIndex")! : 0;
                var column = recordRows ? 1 : 2;
                grid.ClearSelection();
                grid[column, row].Selected = true;
                var automatic = grid.Rows[row].Height;
                Invoke(form, "SetSelectedRowHeight", 40);
                Invoke(form, "AdjustWrappedRowHeights");
                Equal(40, grid.Rows[row].Height);
                Invoke(form, "ResetSelectedRowHeights");
                Equal(automatic, grid.Rows[row].Height);
                Invoke(form, "SetSelectedRowHeight", 90);
                Invoke(form, "RefreshGrid");
                Equal(0, Get<HashSet<int>>(form, "manualHeightRows").Count);
                Equal(automatic, grid.Rows[row].Height);
            }
        });
        Test("Row height dialog cancels without changing height and accepts one value for selected rows", () =>
        {
            using var form = CreateCellDisplayForm(recordRows: true);
            var grid = Get<DataGridView>(form, "grid");
            var first = (int)Invoke(form, "RecordRowsDataStartIndex")!;
            grid.ClearSelection();
            grid[1, first].Selected = true;
            grid[1, first + 1].Selected = true;
            var original = grid.Rows[first].Height;
            WithDialog("選択行の高さ", DialogResult.Cancel, () => Invoke(form, "ChooseSelectedRowHeight"), dialog =>
                dialog.Controls.OfType<NumericUpDown>().Single().Value = 100);
            Equal(original, grid.Rows[first].Height);
            WithDialog("選択行の高さ", DialogResult.OK, () => Invoke(form, "ChooseSelectedRowHeight"), dialog =>
                dialog.Controls.OfType<NumericUpDown>().Single().Value = 95);
            Equal(95, grid.Rows[first].Height);
            Equal(95, grid.Rows[first + 1].Height);
        });
    }
}
