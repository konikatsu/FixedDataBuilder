namespace FixedDataBuilder;

public sealed partial class MainForm
{
    private readonly Button addDataRowButton = new() { Name = "AddDataRow", Text = "行追加", AutoSize = true };
    private readonly Button duplicateDataRowButton = new() { Name = "DuplicateDataRow", Text = "行複製", AutoSize = true };
    private readonly Button deleteDataRowButton = new() { Name = "DeleteDataRow", Text = "行削除 (Ctrl+Delete)", AutoSize = true };
    private readonly ToolStripMenuItem addDataRowMenu = new("行追加") { Name = "AddDataRowMenu" };
    private readonly ToolStripMenuItem duplicateDataRowMenu = new("行複製") { Name = "DuplicateDataRowMenu" };
    private readonly ToolStripMenuItem deleteDataRowMenu = new("行削除")
    {
        Name = "DeleteDataRowMenu", ShortcutKeyDisplayString = "Ctrl+Delete"
    };
    private readonly ContextMenuStrip rowActionsMenu = new() { Name = "RowActionsMenu" };
    private readonly ToolStripMenuItem contextAddDataRow = new("行追加") { Name = "ContextAddDataRow" };
    private readonly ToolStripMenuItem contextDuplicateDataRow = new("行複製") { Name = "ContextDuplicateDataRow" };
    private readonly ToolStripMenuItem contextDeleteDataRow = new("行削除")
    {
        Name = "ContextDeleteDataRow", ShortcutKeyDisplayString = "Ctrl+Delete"
    };
    private readonly ToolTip rowActionHints = new();
    private Func<int, bool>? confirmRowDeletion;
    private bool rowActionSelectionSuppressed;

    private void InitializeRowActions()
    {
        addDataRowButton.Click += (_, _) => RequestAddRow();
        duplicateDataRowButton.Click += (_, _) => RequestDuplicateRow();
        deleteDataRowButton.Click += (_, _) => RequestDeleteRow();
        foreach (var item in new[] { addDataRowMenu, contextAddDataRow }) item.Click += (_, _) => RequestAddRow();
        foreach (var item in new[] { duplicateDataRowMenu, contextDuplicateDataRow }) item.Click += (_, _) => RequestDuplicateRow();
        foreach (var item in new[] { deleteDataRowMenu, contextDeleteDataRow }) item.Click += (_, _) => RequestDeleteRow();
        rowActionsMenu.Items.AddRange([contextAddDataRow, contextDuplicateDataRow, new ToolStripSeparator(), contextDeleteDataRow]);
        rowActionsMenu.Opening += (_, _) => UpdateRowActionControls();
        grid.ContextMenuStrip = rowActionsMenu;
        grid.CellMouseDown += RowActionsCellMouseDown;
        grid.CurrentCellChanged += (_, _) =>
        {
            if (isRefreshingGrid) return;
            rowActionSelectionSuppressed = false;
            UpdateRowActionControls();
        };
        confirmRowDeletion = row => MessageBox.Show(this,
            $"行 {row} を削除しますか？\r\n保存するまで元のファイルは変更されません。", "行の削除",
            MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        Disposed += (_, _) => { rowActionHints.Dispose(); rowActionsMenu.Dispose(); };
        UpdateRowActionControls();
    }

    // A heading or definition cell never chooses a fallback record for an action.
    private int RowActionRecordFromCell(int column, int row)
    {
        if (column < 0 || column >= grid.ColumnCount || row < 0 || row >= grid.RowCount) return -1;
        int record;
        if (layout == GridLayout.FieldRows)
        {
            if (row >= fieldGridRows.Count || fieldGridRows[row].IsOccursHeader || FieldIndexFromFieldRow(row) < 0) return -1;
            if (isSingleRecordView)
            {
                if (column < SingleRecordValueColumnIndex) return -1;
                record = singleRecordIndex;
            }
            else
            {
                if (column < 2) return -1;
                record = column - 2;
            }
        }
        else record = RecordIndexFromGridRow(row);
        return record >= 0 && record < records.Count ? record : -1;
    }

    private int SelectedRowActionRecord() => !rowActionSelectionSuppressed && grid.CurrentCell is { Selected: true } cell
        ? RowActionRecordFromCell(cell.ColumnIndex, cell.RowIndex) : -1;

    private void RowActionsCellMouseDown(object? sender, DataGridViewCellMouseEventArgs e)
    {
        var record = RowActionRecordFromCell(e.ColumnIndex, e.RowIndex);
        rowActionSelectionSuppressed = record < 0;
        if (e.Button == MouseButtons.Right && record >= 0)
        {
            if (!grid.EndEdit()) { rowActionSelectionSuppressed = true; UpdateRowActionControls(); return; }
            grid.CurrentCell = grid[e.ColumnIndex, e.RowIndex];
            grid.ClearSelection();
            grid.CurrentCell.Selected = true;
            rowActionSelectionSuppressed = false;
        }
        UpdateRowActionControls();
    }

    private void UpdateRowActionControls()
    {
        var record = isRefreshingGrid ? -1 : SelectedRowActionRecord();
        var selected = record >= 0;
        var canAdd = fields.Count > 0;
        var canDelete = selected && records.Count > 1;
        addDataRowButton.Enabled = addDataRowMenu.Enabled = contextAddDataRow.Enabled = canAdd;
        duplicateDataRowButton.Enabled = duplicateDataRowMenu.Enabled = contextDuplicateDataRow.Enabled = selected;
        deleteDataRowButton.Enabled = deleteDataRowMenu.Enabled = contextDeleteDataRow.Enabled = canDelete;
        addDataRowMenu.Text = contextAddDataRow.Text = "行追加（末尾）";
        duplicateDataRowMenu.Text = contextDuplicateDataRow.Text = selected ? $"行複製（行 {record + 1}）" : "行複製";
        deleteDataRowMenu.Text = contextDeleteDataRow.Text = selected ? $"行削除（行 {record + 1}）" : "行削除";
        var addHint = canAdd ? $"末尾に行 {records.Count + 1} を追加します。" : "定義ファイルを開くと行を追加できます。";
        var duplicateHint = selected ? $"行 {record + 1} をコピーし、その直後へ追加します。" : "複製するデータのセルを選択してください。";
        var deleteHint = records.Count <= 1 ? "最後の1行は削除できません。"
            : selected ? $"行 {record + 1} を確認してから削除します。Ctrl+Deleteも使えます。" : "削除するデータのセルを選択してください。";
        rowActionHints.SetToolTip(addDataRowButton, addHint);
        rowActionHints.SetToolTip(duplicateDataRowButton, duplicateHint);
        rowActionHints.SetToolTip(deleteDataRowButton, deleteHint);
        addDataRowMenu.ToolTipText = contextAddDataRow.ToolTipText = addHint;
        duplicateDataRowMenu.ToolTipText = contextDuplicateDataRow.ToolTipText = duplicateHint;
        deleteDataRowMenu.ToolTipText = contextDeleteDataRow.ToolTipText = deleteHint;
    }

    private bool RequestAddRow()
    {
        if (fields.Count == 0 || !grid.EndEdit()) return false;
        AddRecord();
        SetStatus($"末尾に行 {records.Count} を追加しました。内容を確認して保存してください。");
        return true;
    }

    private bool RequestDuplicateRow()
    {
        var record = SelectedRowActionRecord();
        if (record < 0 || !grid.EndEdit() || SelectedRowActionRecord() != record) return false;
        DuplicateRecord();
        SetStatus($"行 {record + 1} を複製し、行 {record + 2} に追加しました。内容を確認して保存してください。");
        return true;
    }

    private bool RequestDeleteRow()
    {
        var record = SelectedRowActionRecord();
        if (record < 0 || records.Count <= 1 || !grid.EndEdit() || SelectedRowActionRecord() != record) return false;
        var original = records[record];
        if (confirmRowDeletion?.Invoke(record + 1) != true) return false;
        // Do not act on a different row if a callback changed the selection or the document.
        if (records.Count <= 1 || SelectedRowActionRecord() != record || !ReferenceEquals(records[record], original)) return false;
        DeleteRecord();
        SetStatus($"行 {record + 1} を削除しました。残り {records.Count} 行です。保存するまで元ファイルは変更されません。");
        return true;
    }

    private bool TryHandleRowDeletionShortcut(Keys keyData)
    {
        if (keyData != (Keys.Control | Keys.Delete) || !grid.ContainsFocus || grid.IsCurrentCellInEditMode) return false;
        RequestDeleteRow();
        return true;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData) => TryHandleRowDeletionShortcut(keyData)
        || base.ProcessCmdKey(ref msg, keyData);
}
