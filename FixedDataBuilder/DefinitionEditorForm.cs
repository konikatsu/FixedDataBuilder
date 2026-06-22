namespace FixedDataBuilder;

public sealed class DefinitionEditorForm : Form
{
    private readonly DataGridView grid = new();
    private readonly IReadOnlyList<FieldDefinition> initialFields;

    public string? SavedPath { get; private set; }

    public DefinitionEditorForm(IReadOnlyList<FieldDefinition> fields)
    {
        initialFields = fields;

        Text = "定義ファイル作成";
        StartPosition = FormStartPosition.CenterParent;
        Width = 720;
        Height = 520;
        MinimumSize = new Size(620, 420);

        Controls.Add(grid);
        Controls.Add(BuildToolStrip());

        BuildGrid();
        LoadInitialRows();
    }

    private ToolStrip BuildToolStrip()
    {
        var toolStrip = new ToolStrip
        {
            Dock = DockStyle.Top,
            GripStyle = ToolStripGripStyle.Hidden
        };
        toolStrip.Items.Add(CreateButton("追加", (_, _) => AddRow()));
        toolStrip.Items.Add(CreateButton("削除", (_, _) => DeleteCurrentRow()));
        toolStrip.Items.Add(new ToolStripSeparator());
        toolStrip.Items.Add(CreateButton("保存", (_, _) => SaveDefinition()));
        toolStrip.Items.Add(CreateButton("閉じる", (_, _) => Close()));
        return toolStrip;
    }

    private static ToolStripButton CreateButton(string text, EventHandler onClick)
    {
        var button = new ToolStripButton(text)
        {
            DisplayStyle = ToolStripItemDisplayStyle.Text,
            AutoToolTip = false
        };
        button.Click += onClick;
        return button;
    }

    private void BuildGrid()
    {
        grid.Dock = DockStyle.Fill;
        grid.AllowUserToAddRows = true;
        grid.AllowUserToDeleteRows = true;
        grid.RowHeadersVisible = false;
        grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
        grid.MultiSelect = false;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Name",
            HeaderText = "項目名",
            FillWeight = 45,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Definition",
            HeaderText = "定義",
            FillWeight = 55,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
    }

    private void LoadInitialRows()
    {
        if (initialFields.Count == 0)
        {
            grid.Rows.Add("名前", "N(10)");
            grid.Rows.Add("英名", "X(10)");
            grid.Rows.Add("年齢", "9(2V1)");
            grid.Rows.Add("体重", "S9(3V2)");
            grid.Rows.Add("攻撃力", "S9(9) COMP-3");
            return;
        }

        foreach (var field in initialFields)
        {
            grid.Rows.Add(field.Name, field.DisplayDefinition);
        }
    }

    private void AddRow()
    {
        var rowIndex = grid.Rows.Add();
        grid.CurrentCell = grid[0, rowIndex];
        grid.BeginEdit(selectAll: true);
    }

    private void DeleteCurrentRow()
    {
        if (grid.CurrentRow is null || grid.CurrentRow.IsNewRow)
        {
            return;
        }

        grid.Rows.Remove(grid.CurrentRow);
    }

    private void SaveDefinition()
    {
        grid.EndEdit();
        IReadOnlyList<DefinitionCsvRow> rows;
        try
        {
            rows = DefinitionCsvWriter.NormalizeRows(ReadRows());
            ValidateRows(rows);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "定義チェックエラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Filter = "CSV ファイル (*.csv)|*.csv|すべてのファイル (*.*)|*.*",
            Title = "定義ファイルを保存",
            FileName = "definition.csv"
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            DefinitionCsvWriter.Write(dialog.FileName, rows);
            SavedPath = dialog.FileName;
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "定義保存エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private IEnumerable<DefinitionCsvRow> ReadRows()
    {
        foreach (DataGridViewRow row in grid.Rows)
        {
            if (row.IsNewRow)
            {
                continue;
            }

            yield return new DefinitionCsvRow(
                row.Cells[0].Value?.ToString() ?? string.Empty,
                row.Cells[1].Value?.ToString() ?? string.Empty);
        }
    }

    private static void ValidateRows(IReadOnlyList<DefinitionCsvRow> rows)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"FixedDataBuilder-definition-{Guid.NewGuid():N}.csv");
        try
        {
            DefinitionCsvWriter.Write(tempPath, rows);
            DefinitionCsvReader.Read(tempPath);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }
}
