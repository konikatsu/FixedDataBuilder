namespace FixedDataBuilder;

public sealed partial class MainForm
{
    private readonly TextBox selectedCommentEditor = new()
    {
        Name = "SelectedFieldCommentEditor", Dock = DockStyle.Fill, Multiline = true,
        AcceptsReturn = true, ScrollBars = ScrollBars.Vertical, ReadOnly = true
    };
    private readonly Label selectedCommentCaption = new()
    {
        Name = "SelectedCommentField", Text = "コメント\r\n（項目を選択）", Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true
    };
    private int selectedCommentFieldIndex = -1;
    private FieldDefinition? selectedCommentField;
    private bool updatingCommentEditor;

    private void AddCommentEditor(TableLayoutPanel panel)
    {
        panel.RowCount = 6;
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        panel.Controls.Add(selectedCommentCaption, 0, 5);
        panel.Controls.Add(selectedCommentEditor, 1, 5);
        var save = new Button { Name = "SaveCommentDefinition", Text = "定義を保存...", Dock = DockStyle.Fill, Margin = new Padding(6, 2, 0, 2) };
        save.Click += (_, _) => SaveCurrentDefinitionDialog();
        panel.Controls.Add(save, 2, 5);
        selectedCommentEditor.TextChanged += (_, _) =>
        {
            if (updatingCommentEditor || selectedCommentEditor.ReadOnly || selectedCommentFieldIndex < 0
                || selectedCommentFieldIndex >= fields.Count || !ReferenceEquals(fields[selectedCommentFieldIndex], selectedCommentField)) return;
            SetFieldComment(selectedCommentFieldIndex, selectedCommentEditor.Text);
        };
    }

    private void UpdateCommentEditor()
    {
        if (updatingCommentEditor) return;
        var field = CurrentFieldIndex();
        updatingCommentEditor = true;
        try
        {
            selectedCommentFieldIndex = field >= 0 && field < fields.Count ? field : -1;
            selectedCommentField = selectedCommentFieldIndex < 0 ? null : fields[selectedCommentFieldIndex];
            selectedCommentEditor.ReadOnly = selectedCommentField is null;
            selectedCommentEditor.Text = selectedCommentField?.Comment ?? "";
            selectedCommentEditor.AccessibleName = selectedCommentField is null ? "選択項目のコメント" : selectedCommentField.Name + "のコメント";
            selectedCommentCaption.Text = selectedCommentField is null ? "コメント\r\n（項目を選択）"
                : $"コメント\r\nNo.{selectedCommentFieldIndex + 1} {selectedCommentField.Name}";
        }
        finally { updatingCommentEditor = false; }
    }

    private bool IsCommentCell(int row, int column) => row >= 0 && column >= 0 && row < grid.RowCount && column < grid.ColumnCount
        && (layout == GridLayout.RecordRows ? row == recordCommentRowIndex && column > 0
            : isSingleRecordView && grid.Columns[column].Name == "FieldComment" && FieldIndexFromFieldRow(row) >= 0);

    private bool UpdateCommentCell(int row, int column)
    {
        if (!IsCommentCell(row, column)) return false;
        var field = layout == GridLayout.RecordRows ? FieldIndexFromRecordColumn(column) : FieldIndexFromFieldRow(row);
        SetFieldComment(field, grid[column, row].Value?.ToString() ?? "");
        return true;
    }

    private void SetFieldComment(int fieldIndex, string comment)
    {
        if (fieldIndex < 0 || fieldIndex >= fields.Count) throw new ArgumentOutOfRangeException(nameof(fieldIndex));
        if (fields[fieldIndex].Comment == comment) return;
        var sourcePath = definitionSaveSuggestionPath ?? GetCurrentDefinitionPath();
        var oldGuard = updatingCommentEditor;
        updatingCommentEditor = true;
        try
        {
            fields[fieldIndex] = fields[fieldIndex] with { Comment = comment };
            hasDefinitionEdits = true;
            hasInferredCsvDefinition = false; // F5 must keep the edited definition rather than infer again.
            definitionSaveSuggestionPath = sourcePath;
            UpdateDocumentTitle();
            if (selectedCommentFieldIndex == fieldIndex) selectedCommentField = fields[fieldIndex];
            if (layout == GridLayout.RecordRows && recordCommentRowIndex >= 0)
            {
                var column = recordGridColumnFieldIndexes.IndexOf(fieldIndex);
                if (column > 0)
                {
                    grid[column, recordCommentRowIndex].Value = comment;
                    grid[column, recordCommentRowIndex].ToolTipText = comment;
                    grid[column, 0].ToolTipText = grid.Columns[column].HeaderCell.ToolTipText = FieldDefinitionToolTip(fields[fieldIndex]);
                }
                AdjustRecordCommentHeight();
            }
            else if (layout == GridLayout.FieldRows && (!isSingleRecordView || grid.Columns.Contains("FieldComment")))
            {
                var row = fieldGridRows.FindIndex(item => !item.IsOccursHeader && item.FieldIndex == fieldIndex);
                if (row >= 0)
                {
                    grid[0, row].ToolTipText = FieldDefinitionToolTip(fields[fieldIndex]);
                    if (isSingleRecordView)
                    {
                        grid["FieldComment", row].Value = comment;
                        grid["FieldComment", row].ToolTipText = comment;
                        AdjustSingleRecordRowHeights();
                    }
                }
            }
            else
            {
                // Create the first visible comment row/column once, retaining selection and editor focus.
                var record = Math.Max(0, CurrentRecordIndex());
                var widths = grid.Columns.Cast<DataGridViewColumn>().ToDictionary(column => column.Name, column => column.Width);
                var offset = grid.HorizontalScrollingOffset;
                RefreshGrid();
                foreach (DataGridViewColumn column in grid.Columns)
                    if (widths.TryGetValue(column.Name, out var width)) column.Width = Math.Max(column.MinimumWidth, width);
                RestoreRecordFieldSelection(record, fieldIndex);
                grid.HorizontalScrollingOffset = offset;
            }
            if (selectedCommentFieldIndex == fieldIndex && selectedCommentEditor.Text != comment) selectedCommentEditor.Text = comment;
            UpdateGenerationControls();
            SetStatus("コメントを定義へ反映しました。データの値は変更していません。「定義を保存...」で保存してください。");
        }
        finally { updatingCommentEditor = oldGuard; }
    }
}
