using System.Reflection;
using FixedDataBuilder;

internal static partial class Program
{
    private static void RunGridAccessibilityTests()
    {
        Test("Accessible row and cell names enumerate visible rows without quadratic state lookups", () =>
        {
            using var grid = new CountedAccessibleGrid();
            grid.Columns.Add(new ResponsiveTextColumn { HeaderText = "内容" });
            grid.RowCount = 128;
            grid.CountedRows.StateReads = 0;
            for (var i = 0; i < grid.RowCount; i++)
            {
                Equal($"行 {i + 1}", grid.Rows[i].AccessibilityObject.Name);
                Equal($"内容、行 {i + 1}", grid[0, i].AccessibilityObject.Name);
            }
            Equal(true, grid.CountedRows.StateReads <= 128 * 4);
        });
        Test("Accessible names follow hidden rows additions removals and sorting without a stale cache", () =>
        {
            using var grid = new ResponsiveDataGridView { AllowUserToAddRows = false };
            grid.Columns.Add(new ResponsiveTextColumn { HeaderText = "内容", SortMode = DataGridViewColumnSortMode.Automatic });
            grid.Rows.Add("C"); grid.Rows.Add("B"); grid.Rows.Add("A");
            grid.Rows[1].Visible = false;
            Equal("行 2", grid.Rows[2].AccessibilityObject.Name);
            Equal("内容、行 2、並べ替えなし", grid[0, 2].AccessibilityObject.Name);
            grid.Rows[1].Visible = true;
            Equal("行 3", grid.Rows[2].AccessibilityObject.Name);
            grid.Rows.RemoveAt(0);
            Equal("行 2", grid.Rows[1].AccessibilityObject.Name);
            grid.Sort(grid.Columns[0], System.ComponentModel.ListSortDirection.Ascending);
            Equal("A", grid[0, 0].Value);
            Equal("内容、行 1、昇順", grid[0, 0].AccessibilityObject.Name);
            grid.Rows.Add("D"); Equal("行 3", grid.Rows[2].AccessibilityObject.Name);
        });
        Test("Accessible row navigation keeps identity hidden rows headers boundaries and children", () =>
        {
            using var grid = new ResponsiveDataGridView { AllowUserToAddRows = false, RowHeadersVisible = false };
            grid.Columns.Add(new ResponsiveTextColumn()); grid.RowCount = 4;
            grid.Rows[0].Frozen = true; grid.Rows[1].Visible = false;
            var first = grid.Rows[0].AccessibilityObject;
            var third = grid.Rows[2].AccessibilityObject;
            Equal(true, ReferenceEquals(third, first.Navigate(AccessibleNavigation.Next)));
            Equal(true, ReferenceEquals(first, third.Navigate(AccessibleNavigation.Previous)));
            Equal(true, ReferenceEquals(grid.AccessibilityObject.GetChild(0), first.Navigate(AccessibleNavigation.Up)));
            Equal(true, grid.Rows[3].AccessibilityObject.Navigate(AccessibleNavigation.Next) is null);
            Equal(true, ReferenceEquals(grid[0, 2].AccessibilityObject, third.Navigate(AccessibleNavigation.FirstChild)));
            grid.ColumnHeadersVisible = false; Equal(true, first.Navigate(AccessibleNavigation.Previous) is null);
        });
        Test("UIA row sibling navigation dispatches through the bounded public override", () =>
        {
            using var grid = new ResponsiveDataGridView { AllowUserToAddRows = false };
            grid.Columns.Add(new ResponsiveTextColumn()); grid.RowCount = 3;
            var type = typeof(DataGridViewRow).GetNestedType("DataGridViewRowAccessibleObject", BindingFlags.Public | BindingFlags.NonPublic)!;
            var method = type.GetMethod("FragmentNavigate", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var direction = Enum.Parse(method.GetParameters()[0].ParameterType, "NextSibling");
            Equal(true, ReferenceEquals(grid.Rows[1].AccessibilityObject, method.Invoke(grid.Rows[0].AccessibilityObject, [direction])));
        });
        Test("Accessible text cells retain value role parent child and readonly behavior", () =>
        {
            using var grid = new ResponsiveDataGridView { AllowUserToAddRows = false, RowHeadersVisible = false };
            grid.Columns.Add(new ResponsiveTextColumn { HeaderText = "内容" }); grid.Rows.Add(" 001 ");
            var cell = grid[0, 0].AccessibilityObject;
            Equal(AccessibleRole.Cell, cell.Role); Equal(" 001 ", cell.Value);
            Equal(true, ReferenceEquals(grid.Rows[0].AccessibilityObject, cell.Parent));
            Equal(AccessibleRole.Row, cell.Parent!.Role);
            grid[0, 0].ReadOnly = true;
            Equal(true, (cell.State & AccessibleStates.ReadOnly) != 0);
            Equal(true, ReferenceEquals(cell, grid.Rows[0].AccessibilityObject.GetChild(0)));
        });
        Test("Detached cloned rows and text cells remain safe and preserve custom accessibility types", () =>
        {
            var cell = (DataGridViewTextBoxCell)new ResponsiveTextCell().Clone();
            var row = (DataGridViewRow)new ResponsiveGridRow().Clone();
            Equal(true, cell is ResponsiveTextCell); Equal(true, row is ResponsiveGridRow);
            Equal(string.Empty, cell.AccessibilityObject.Name); Equal(string.Empty, row.AccessibilityObject.Name);
            Equal(true, row.AccessibilityObject.Navigate(AccessibleNavigation.Next) is null);
            cell.Dispose(); row.Dispose();
        });
        Test("Main and column grids use responsive accessible row and text templates", () =>
        {
            using var main = new MainForm();
            Equal(true, Get<WheelZoomDataGridView>(main, "grid") is ResponsiveDataGridView);
            using var form = new ColumnTextViewForm();
            Invoke(form, "Reflow");
            Equal(true, Get<List<WheelZoomDataGridView>>(form, "panes").All(p => p is ResponsiveDataGridView
                && p.RowTemplate is ResponsiveGridRow && p.Columns.Cast<DataGridViewColumn>().All(c => c.CellTemplate is ResponsiveTextCell)));
        });
        Test("Column page control updates preserve an outer update guard", () =>
        {
            using var form = new ColumnTextViewForm();
            var field = typeof(ColumnTextViewForm).GetField("updating", BindingFlags.Instance | BindingFlags.NonPublic)!;
            field.SetValue(form, true); Invoke(form, "UpdatePageControls"); Equal(true, Get<bool>(form, "updating"));
            field.SetValue(form, false); Invoke(form, "Reflow"); Equal(false, Get<bool>(form, "updating"));
            Equal(false, Get<bool>(form, "reflowing"));
        });
        Test("Canceled column closing permits later reflow", () =>
        {
            using var form = new ColumnTextViewForm();
            form.FormClosing += (_, e) => e.Cancel = true;
            Invoke(form, "OnFormClosing", new FormClosingEventArgs(CloseReason.UserClosing, false));
            Equal(false, Get<bool>(form, "closing"));
            Invoke(form, "Reflow");
            Equal(false, Get<bool>(form, "reflowing"));
            Equal(true, Get<List<WheelZoomDataGridView>>(form, "panes").Count > 0);
        });
    }

    private sealed class CountedAccessibleGrid : WheelZoomDataGridView
    {
        public CountedAccessibleGrid() { AllowUserToAddRows = false; RowTemplate = new ResponsiveGridRow(); }
        public CountedAccessibleRows CountedRows => (CountedAccessibleRows)Rows;
        protected override DataGridViewRowCollection CreateRowsInstance() => new CountedAccessibleRows(this);
    }
    private sealed class CountedAccessibleRows(DataGridView grid) : DataGridViewRowCollection(grid)
    {
        public int StateReads { get; set; }
        public override DataGridViewElementStates GetRowState(int rowIndex) { StateReads++; return base.GetRowState(rowIndex); }
    }
}
