namespace FixedDataBuilder;

/// <summary>Keeps standard accessibility while avoiding quadratic visible-row lookups in names/navigation.</summary>
public sealed class ResponsiveDataGridView : WheelZoomDataGridView
{
    public ResponsiveDataGridView() => RowTemplate = new ResponsiveGridRow();
}

public sealed class ResponsiveTextColumn : DataGridViewTextBoxColumn
{
    public ResponsiveTextColumn() => CellTemplate = new ResponsiveTextCell();
}

internal static class GridVisibleOrdinal
{
    public static int Find(DataGridView? grid, int rowIndex)
    {
        if (grid is null || grid.IsDisposed || rowIndex < 0 || rowIndex >= grid.Rows.Count) return -1;
        var rows = grid.Rows;
        if ((rows.GetRowState(rowIndex) & DataGridViewElementStates.Visible) == 0) return -1;
        // GetRowCount caches the visible count; don't keep a second cache that can become stale.
        if (rows.GetRowCount(DataGridViewElementStates.Visible) == rows.Count) return rowIndex;
        var ordinal = 0;
        for (var index = 0; index < rowIndex; index++)
            if ((rows.GetRowState(index) & DataGridViewElementStates.Visible) != 0) ordinal++;
        return ordinal;
    }
}

public sealed class ResponsiveTextCell : DataGridViewTextBoxCell
{
    protected override AccessibleObject CreateAccessibilityInstance() => new CellAccessibility(this);

    private sealed class CellAccessibility(ResponsiveTextCell cell) : DataGridViewTextBoxCellAccessibleObject(cell)
    {
        public override string? Name
        {
            get
            {
                var grid = cell.DataGridView;
                var ordinal = GridVisibleOrdinal.Find(grid, cell.RowIndex);
                if (ordinal < 0 || cell.OwningColumn is not { } column) return string.Empty;
                var name = $"{column.HeaderText}、行 {ordinal + 1}";
                if (column.SortMode != DataGridViewColumnSortMode.NotSortable)
                    name += grid!.SortedColumn == column
                        ? grid.SortOrder == SortOrder.Ascending ? "、昇順" : "、降順"
                        : "、並べ替えなし";
                return name;
            }
        }
    }
}

public sealed class ResponsiveGridRow : DataGridViewRow
{
    protected override AccessibleObject CreateAccessibilityInstance() => new RowAccessibility(this);

    private sealed class RowAccessibility(ResponsiveGridRow row) : DataGridViewRowAccessibleObject(row)
    {
        public override string Name
        {
            get
            {
                var ordinal = GridVisibleOrdinal.Find(row.DataGridView, row.Index);
                return ordinal < 0 ? string.Empty : $"行 {ordinal + 1}";
            }
        }

        public override AccessibleObject? Navigate(AccessibleNavigation direction)
        {
            var grid = row.DataGridView;
            if (grid is null || grid.IsDisposed || row.Index < 0) return null;
            if (direction is AccessibleNavigation.Next or AccessibleNavigation.Down)
            {
                var next = grid.Rows.GetNextRow(row.Index, DataGridViewElementStates.Visible);
                return next < 0 ? null : grid.Rows[next].AccessibilityObject;
            }
            if (direction is AccessibleNavigation.Previous or AccessibleNavigation.Up)
            {
                var previous = grid.Rows.GetPreviousRow(row.Index, DataGridViewElementStates.Visible);
                return previous >= 0 ? grid.Rows[previous].AccessibilityObject
                    : grid.ColumnHeadersVisible ? grid.AccessibilityObject.GetChild(0) : null;
            }
            var count = GetChildCount();
            return direction switch
            {
                AccessibleNavigation.FirstChild when count > 0 => GetChild(0),
                AccessibleNavigation.LastChild when count > 0 => GetChild(count - 1),
                _ => null
            };
        }
    }
}
