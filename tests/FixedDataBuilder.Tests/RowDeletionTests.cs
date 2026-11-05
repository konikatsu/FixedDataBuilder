using System.Reflection;
using System.Text.Json;
using FixedDataBuilder;

internal static partial class Program
{
    private static void RunRowDeletionTests(string repo)
    {
        Test("Row action toolbar and menus expose add duplicate delete without global shortcut interception", () =>
        {
            using var form = CreateCellDisplayForm(recordRows: true);
            Equal("行追加", Get<Button>(form, "addDataRowButton").Text);
            Equal("AddDataRow", Get<Button>(form, "addDataRowButton").Name);
            Equal("DuplicateDataRow", Get<Button>(form, "duplicateDataRowButton").Name);
            Equal("DeleteDataRow", Get<Button>(form, "deleteDataRowButton").Name);
            Equal("行削除 (Ctrl+Delete)", Get<Button>(form, "deleteDataRowButton").Text);
            Equal(Keys.None, Get<ToolStripMenuItem>(form, "deleteDataRowMenu").ShortcutKeys);
            Equal("Ctrl+Delete", Get<ToolStripMenuItem>(form, "deleteDataRowMenu").ShortcutKeyDisplayString);
            Equal(Keys.None, Get<ToolStripMenuItem>(form, "contextDeleteDataRow").ShortcutKeys);
            Equal(4, Get<ContextMenuStrip>(form, "rowActionsMenu").Items.Count);
            Equal(Get<ContextMenuStrip>(form, "rowActionsMenu"), Get<DataGridView>(form, "grid").ContextMenuStrip);
            SelectRowActionRecord(form, 1);
            Equal(true, Get<Button>(form, "deleteDataRowButton").Enabled);
            Equal(true, Get<ToolStripMenuItem>(form, "deleteDataRowMenu").Text?.Contains("行 2") == true);
            Equal(true, Get<ToolTip>(form, "rowActionHints").GetToolTip(Get<Button>(form, "deleteDataRowButton"))?.Contains("行 2") == true);
        });
        Test("Row add appends a blank record and duplicate inserts a separate copy after the selected record in each layout", () =>
        {
            foreach (var mode in new[] { 0, 1, 2 })
            {
                using var form = CreateRowActionForm(mode);
                var rows = Get<List<List<string>>>(form, "records");
                var definition = JsonSerializer.Serialize(Get<List<FieldDefinition>>(form, "fields"));
                var original = rows.Select(row => row.ToArray()).ToArray();
                SelectRowActionRecord(form, 1);
                Equal(true, (bool)Invoke(form, "RequestDuplicateRow")!);
                Equal(original.Length + 1, rows.Count);
                Equal(true, rows[1].SequenceEqual(rows[2]));
                Equal(false, ReferenceEquals(rows[1], rows[2]));
                Equal(true, rows[3].SequenceEqual(original[2]));
                Equal(true, (bool)Invoke(form, "RequestAddRow")!);
                Equal(original.Length + 2, rows.Count);
                Equal("|||0", string.Join("|", rows[^1]));
                Equal(definition, JsonSerializer.Serialize(Get<List<FieldDefinition>>(form, "fields")));
                Equal(true, rows[0].SequenceEqual(original[0]));
                Equal(true, Get<bool>(form, "hasUnsavedChanges"));
            }
        });
        Test("Delete button confirms the actual file row and cancellation preserves the data and dirty state in every layout", () =>
        {
            foreach (var mode in new[] { 0, 1, 2 })
            {
                using var form = CreateRowActionForm(mode);
                var rows = Get<List<List<string>>>(form, "records");
                var before = JsonSerializer.Serialize(rows);
                var definition = JsonSerializer.Serialize(Get<List<FieldDefinition>>(form, "fields"));
                var removed = rows[1];
                var prompted = new List<int>();
                SetRowDeletionConfirmation(form, row => { prompted.Add(row); return false; });
                SelectRowActionRecord(form, 1);
                Get<Button>(form, "deleteDataRowButton").PerformClick();
                Equal("2", string.Join("|", prompted));
                Equal(before, JsonSerializer.Serialize(rows));
                Equal(false, Get<bool>(form, "hasUnsavedChanges"));
                SetRowDeletionConfirmation(form, row => { prompted.Add(row); return true; });
                Get<Button>(form, "deleteDataRowButton").PerformClick();
                Equal("2|2", string.Join("|", prompted));
                Equal(4, rows.Count);
                Equal(false, rows.Any(row => ReferenceEquals(row, removed)));
                Equal(definition, JsonSerializer.Serialize(Get<List<FieldDefinition>>(form, "fields")));
                Equal(true, Get<bool>(form, "hasUnsavedChanges"));
            }
        });
        Test("Metadata headings and cleared selections never fall back to deleting or duplicating the last record", () =>
        {
            foreach (var mode in new[] { 0, 1, 2 })
            {
                using var form = CreateRowActionForm(mode);
                var rows = Get<List<List<string>>>(form, "records");
                var before = JsonSerializer.Serialize(rows);
                var prompts = 0;
                SetRowDeletionConfirmation(form, _ => { prompts++; return true; });
                SelectRowActionRecord(form, 2);
                Invoke(form, "RowActionsCellMouseDown", Get<DataGridView>(form, "grid"),
                    RowActionMouse(-1, -1, MouseButtons.Left));
                Equal(false, (bool)Invoke(form, "RequestDeleteRow")!);
                Equal(false, (bool)Invoke(form, "RequestDuplicateRow")!);
                Equal(false, Get<Button>(form, "deleteDataRowButton").Enabled);
                var grid = Get<DataGridView>(form, "grid");
                grid.CurrentCell = grid[0, 0];
                Equal(false, (bool)Invoke(form, "RequestDeleteRow")!);
                SelectRowActionRecord(form, 2);
                grid.ClearSelection();
                Equal(false, (bool)Invoke(form, "RequestDeleteRow")!);
                Equal(false, (bool)Invoke(form, "RequestDuplicateRow")!);
                Equal(0, prompts);
                Equal(before, JsonSerializer.Serialize(rows));
                Equal(false, Get<bool>(form, "hasUnsavedChanges"));
            }
        });
        Test("Right click selects its data record before the row action menu and rejects metadata targets", () =>
        {
            foreach (var recordRows in new[] { false, true })
            {
                using var form = CreateCellDisplayForm(recordRows);
                var grid = Get<DataGridView>(form, "grid");
                var rows = Get<List<List<string>>>(form, "records");
                var target = rows[2];
                SelectRowActionRecord(form, 0);
                var column = recordRows ? 1 : 4;
                var row = recordRows ? (int)Invoke(form, "RecordRowsDataStartIndex")! + 2 : 0;
                Invoke(form, "RowActionsCellMouseDown", grid, RowActionMouse(column, row, MouseButtons.Right));
                Equal(2, (int)Invoke(form, "SelectedRowActionRecord")!);
                var prompted = -1;
                SetRowDeletionConfirmation(form, number => { prompted = number; return true; });
                Get<ToolStripMenuItem>(form, "contextDeleteDataRow").PerformClick();
                Equal(3, prompted);
                Equal(false, rows.Any(record => ReferenceEquals(record, target)));
                Invoke(form, "SetUnsavedChanges", false);
                var after = JsonSerializer.Serialize(rows);
                Invoke(form, "RowActionsCellMouseDown", grid, RowActionMouse(0, 0, MouseButtons.Right));
                Equal(false, Get<ToolStripMenuItem>(form, "contextDeleteDataRow").Enabled);
                Equal(true, Get<ToolStripMenuItem>(form, "contextAddDataRow").Enabled);
                Equal(false, (bool)Invoke(form, "RequestDeleteRow")!);
                Equal(after, JsonSerializer.Serialize(rows));
                Equal(false, Get<bool>(form, "hasUnsavedChanges"));
            }
        });
        Test("Ctrl Delete deletes rows only from a focused grid outside a cell editor", () =>
        {
            using var form = CreateCellDisplayForm(recordRows: true);
            var grid = Get<DataGridView>(form, "grid");
            var rows = Get<List<List<string>>>(form, "records");
            var before = JsonSerializer.Serialize(rows);
            var prompted = 0;
            SetRowDeletionConfirmation(form, _ => { prompted++; return false; });
            SelectRowActionRecord(form, 1);
            form.Activate(); grid.Focus(); Application.DoEvents();
            Equal(true, grid.ContainsFocus);
            Equal(true, (bool)Invoke(grid, "ProcessCmdKey", new Message(), Keys.Control | Keys.Delete)!);
            Equal(1, prompted);
            Equal(false, (bool)Invoke(form, "TryHandleRowDeletionShortcut", Keys.Delete)!);
            Equal(false, (bool)Invoke(form, "TryHandleRowDeletionShortcut", Keys.Control | Keys.Shift | Keys.Delete)!);
            Equal(true, grid.BeginEdit(true));
            grid.EditingControl!.Focus(); Application.DoEvents();
            Equal(true, grid.IsCurrentCellInEditMode);
            Equal(false, (bool)Invoke(form, "TryHandleRowDeletionShortcut", Keys.Control | Keys.Delete)!);
            grid.EndEdit();
            foreach (var textbox in new[] { Get<TextBox>(form, "dataPathTextBox"), Get<TextBox>(form, "hexTextBox") })
            {
                textbox.Focus(); Application.DoEvents();
                Equal(false, grid.ContainsFocus);
                Equal(false, (bool)Invoke(form, "TryHandleRowDeletionShortcut", Keys.Control | Keys.Delete)!);
            }
            Equal(1, prompted);
            Equal(before, JsonSerializer.Serialize(rows));
            Equal(false, Get<bool>(form, "hasUnsavedChanges"));
        });
        Test("Last record deletion stays disabled and an empty definition cannot add duplicate or delete rows", () =>
        {
            using (var form = CreateCellDisplayForm(recordRows: true))
            {
                var rows = Get<List<List<string>>>(form, "records");
                rows.RemoveRange(1, rows.Count - 1);
                Invoke(form, "RefreshGrid");
                SelectRowActionRecord(form, 0);
                var prompted = false;
                SetRowDeletionConfirmation(form, _ => { prompted = true; return true; });
                Equal(false, Get<Button>(form, "deleteDataRowButton").Enabled);
                Equal(true, Get<ToolTip>(form, "rowActionHints").GetToolTip(Get<Button>(form, "deleteDataRowButton"))?.Contains("最後の1行") == true);
                Equal(false, (bool)Invoke(form, "RequestDeleteRow")!);
                Equal(false, prompted);
                Equal(1, rows.Count);
                Equal(false, Get<bool>(form, "hasUnsavedChanges"));
            }
            using var empty = new MainForm();
            Equal(false, Get<Button>(empty, "addDataRowButton").Enabled);
            Equal(false, Get<Button>(empty, "duplicateDataRowButton").Enabled);
            Equal(false, Get<Button>(empty, "deleteDataRowButton").Enabled);
            Equal(false, (bool)Invoke(empty, "RequestAddRow")!);
            Equal(false, (bool)Invoke(empty, "RequestDuplicateRow")!);
            Equal(false, (bool)Invoke(empty, "RequestDeleteRow")!);
            Equal(false, Get<bool>(empty, "hasUnsavedChanges"));
        });
        Test("Deleting a generated row invalidates generation correspondence without changing the definition", () =>
        {
            using var form = CreateCellDisplayForm(recordRows: true);
            Invoke(form, "ApplyGeneratedTestData", GenerationIntegrationResult(form));
            Equal(true, (bool)Invoke(form, "HasCurrentGenerationReport")!);
            var fields = JsonSerializer.Serialize(Get<List<FieldDefinition>>(form, "fields"));
            SelectRowActionRecord(form, 1);
            SetRowDeletionConfirmation(form, _ => true);
            Equal(true, (bool)Invoke(form, "RequestDeleteRow")!);
            Equal(false, (bool)Invoke(form, "HasCurrentGenerationReport")!);
            Equal(fields, JsonSerializer.Serialize(Get<List<FieldDefinition>>(form, "fields")));
        });
        _ = repo;
    }

    private static MainForm CreateRowActionForm(int mode)
    {
        var form = CreateCellDisplayForm(recordRows: mode == 1);
        if (mode == 2) Invoke(form, "SetSingleRecordView", true);
        return form;
    }

    private static void SelectRowActionRecord(MainForm form, int record)
    {
        Invoke(form, "SelectRecord", record);
        var grid = Get<DataGridView>(form, "grid");
        grid.CurrentCell!.Selected = true;
        Invoke(form, "UpdateRowActionControls");
    }

    private static void SetRowDeletionConfirmation(MainForm form, Func<int, bool> confirm) =>
        form.GetType().GetField("confirmRowDeletion", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(form, confirm);

    private static DataGridViewCellMouseEventArgs RowActionMouse(int column, int row, MouseButtons button) =>
        new(column, row, 4, 4, new MouseEventArgs(button, 1, 4, 4, 0));
}
