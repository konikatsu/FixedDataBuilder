namespace FixedDataBuilder;

public sealed class MainForm : Form
{
    private static readonly Color DefinitionBackColor = Color.FromArgb(226, 242, 226);
    private static readonly Color DefinitionHeaderBackColor = Color.FromArgb(204, 232, 204);

    private readonly DataGridView grid = new();
    private readonly ToolStrip toolStrip = new();
    private readonly StatusStrip statusStrip = new();
    private readonly ToolStripStatusLabel statusLabel = new();
    private readonly ToolStripButton fieldRowsButton;
    private readonly ToolStripButton recordRowsButton;
    private readonly List<FieldDefinition> fields = [];
    private readonly List<List<string>> records = [];
    private GridLayout layout = GridLayout.FieldRows;

    public MainForm()
    {
        Text = "FixedDataBuilder";
        StartPosition = FormStartPosition.CenterScreen;
        Width = 1100;
        Height = 700;

        fieldRowsButton = CreateButton("表示: 項目縦", (_, _) => ChangeLayout(GridLayout.FieldRows));
        recordRowsButton = CreateButton("表示: レコード縦", (_, _) => ChangeLayout(GridLayout.RecordRows));

        BuildToolStrip();
        BuildGrid();

        statusStrip.Items.Add(statusLabel);
        Controls.Add(grid);
        Controls.Add(toolStrip);
        Controls.Add(statusStrip);

        LoadSampleDefinition();
    }

    private void BuildToolStrip()
    {
        toolStrip.GripStyle = ToolStripGripStyle.Hidden;
        toolStrip.Items.Add(CreateButton("定義読込", (_, _) => OpenDefinition()));
        toolStrip.Items.Add(new ToolStripSeparator());
        toolStrip.Items.Add(CreateButton("追加", (_, _) => AddRecord()));
        toolStrip.Items.Add(CreateButton("複製", (_, _) => DuplicateRecord()));
        toolStrip.Items.Add(CreateButton("削除", (_, _) => DeleteRecord()));
        toolStrip.Items.Add(new ToolStripSeparator());
        toolStrip.Items.Add(fieldRowsButton);
        toolStrip.Items.Add(recordRowsButton);
        toolStrip.Items.Add(new ToolStripSeparator());
        toolStrip.Items.Add(CreateButton("検証", (_, _) => ValidateRecords(showSuccess: true)));
        toolStrip.Items.Add(CreateButton("保存", (_, _) => SaveData()));
        toolStrip.Dock = DockStyle.Top;
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
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.RowHeadersVisible = false;
        grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
        grid.MultiSelect = false;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        grid.EnableHeadersVisualStyles = false;
        grid.CellEndEdit += (_, e) => UpdateRecordValue(e.RowIndex, e.ColumnIndex);
    }

    private void LoadSampleDefinition()
    {
        fields.Clear();
        fields.AddRange(
        [
            new FieldDefinition("名前", FieldDataType.FullWidthText, 10, "N(10)"),
            new FieldDefinition("英名", FieldDataType.Text, 10, "X(10)"),
            new FieldDefinition("年齢", FieldDataType.PlainNumber, 3, "9(2V1)", DecimalScale: 1),
            new FieldDefinition("体重", FieldDataType.SignedNumber, 5, "S9(3V2)", DecimalScale: 2),
            new FieldDefinition("攻撃力", FieldDataType.PackedSigned, 9, "S9(9) COMP-3")
        ]);

        records.Clear();
        records.Add(["ジナン", "JINAN", "7", "6.7", "100"]);
        records.Add(["キナコ", "KINAKO", "3", "3.8", "50"]);
        records.Add(["オジュン", "OJUN", "18", "49", "999999999"]);
        RefreshGrid();
        statusLabel.Text = "サンプル定義を読み込みました。";
    }

    private void OpenDefinition()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "CSV ファイル (*.csv)|*.csv|すべてのファイル (*.*)|*.*",
            Title = "定義書 CSV を選択"
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            fields.Clear();
            fields.AddRange(DefinitionCsvReader.Read(dialog.FileName));
            records.Clear();
            records.Add(CreateEmptyRecord());
            RefreshGrid();
            statusLabel.Text = $"定義書を読み込みました: {Path.GetFileName(dialog.FileName)}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "定義書読込エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private List<string> CreateEmptyRecord()
    {
        return fields.Select(field => field.Type switch
        {
            FieldDataType.PlainNumber or FieldDataType.PackedUnsigned or FieldDataType.PackedSigned => "0",
            _ => string.Empty
        }).ToList();
    }

    private void AddRecord()
    {
        if (fields.Count == 0)
        {
            return;
        }

        records.Add(CreateEmptyRecord());
        RefreshGrid();
        SelectRecord(records.Count - 1);
    }

    private void DuplicateRecord()
    {
        var index = CurrentRecordIndex();
        if (index < 0)
        {
            return;
        }

        records.Insert(index + 1, [.. records[index]]);
        RefreshGrid();
        SelectRecord(index + 1);
    }

    private void DeleteRecord()
    {
        var index = CurrentRecordIndex();
        if (index < 0 || records.Count <= 1)
        {
            return;
        }

        records.RemoveAt(index);
        RefreshGrid();
        SelectRecord(Math.Min(index, records.Count - 1));
    }

    private void SaveData()
    {
        grid.EndEdit();
        if (!ValidateRecords(showSuccess: false))
        {
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Filter = "固定長データ (*.dat)|*.dat|テキスト (*.txt)|*.txt|すべてのファイル (*.*)|*.*",
            Title = "固定長データを保存",
            FileName = "sample.dat"
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            FixedLengthDataWriter.Write(dialog.FileName, fields, records);
            statusLabel.Text = $"保存しました: {Path.GetFileName(dialog.FileName)}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "保存エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private bool ValidateRecords(bool showSuccess)
    {
        grid.EndEdit();
        var errors = RecordValidator.Validate(fields, records);
        if (errors.Count == 0)
        {
            statusLabel.Text = "検証 OK";
            if (showSuccess)
            {
                MessageBox.Show(this, "検証 OK です。", "検証", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            return true;
        }

        MessageBox.Show(this, string.Join(Environment.NewLine, errors.Take(20)), "検証エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        statusLabel.Text = $"検証エラー: {errors.Count} 件";
        return false;
    }

    private void ChangeLayout(GridLayout nextLayout)
    {
        if (layout == nextLayout)
        {
            return;
        }

        grid.EndEdit();
        layout = nextLayout;
        RefreshGrid();
    }

    private void UpdateRecordValue(int rowIndex, int columnIndex)
    {
        if (rowIndex < 0 || columnIndex < 0)
        {
            return;
        }

        if (layout == GridLayout.FieldRows)
        {
            if (columnIndex < 2)
            {
                return;
            }

            records[columnIndex - 2][rowIndex] = grid[columnIndex, rowIndex].Value?.ToString() ?? string.Empty;
            return;
        }

        if (columnIndex < 1)
        {
            return;
        }

        records[rowIndex][columnIndex - 1] = grid[columnIndex, rowIndex].Value?.ToString() ?? string.Empty;
    }

    private int CurrentRecordIndex()
    {
        if (grid.CurrentCell is null)
        {
            return records.Count - 1;
        }

        return layout == GridLayout.FieldRows
            ? grid.CurrentCell.ColumnIndex >= 2 ? grid.CurrentCell.ColumnIndex - 2 : records.Count - 1
            : grid.CurrentCell.RowIndex;
    }

    private void SelectRecord(int recordIndex)
    {
        if (recordIndex < 0 || fields.Count == 0 || records.Count == 0)
        {
            return;
        }

        grid.CurrentCell = layout == GridLayout.FieldRows
            ? grid[recordIndex + 2, 0]
            : grid[1, recordIndex];
    }

    private void RefreshGrid()
    {
        grid.Columns.Clear();
        grid.Rows.Clear();
        UpdateLayoutButtons();

        if (layout == GridLayout.FieldRows)
        {
            RefreshFieldRowsGrid();
        }
        else
        {
            RefreshRecordRowsGrid();
        }

        statusLabel.Text = $"{fields.Count} 項目 / {records.Count} レコード";
    }

    private void RefreshFieldRowsGrid()
    {
        grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.EnableResizing;
        grid.ColumnHeadersHeight = 28;
        grid.Columns.Add(CreateReadOnlyColumn("Name", "項目名", 180, frozen: true));
        grid.Columns.Add(CreateReadOnlyColumn("Definition", "定義", 150, frozen: true));
        ApplyDefinitionColumnStyle(grid.Columns[0]);
        ApplyDefinitionColumnStyle(grid.Columns[1]);

        for (var recordIndex = 0; recordIndex < records.Count; recordIndex++)
        {
            grid.Columns.Add(CreateEditableColumn($"Record{recordIndex + 1}", $"Rec {recordIndex + 1}", 140));
        }

        for (var fieldIndex = 0; fieldIndex < fields.Count; fieldIndex++)
        {
            var field = fields[fieldIndex];
            var rowIndex = grid.Rows.Add();
            var row = grid.Rows[rowIndex];
            row.Cells[0].Value = field.Name;
            row.Cells[1].Value = field.DisplayDefinition;

            for (var recordIndex = 0; recordIndex < records.Count; recordIndex++)
            {
                row.Cells[recordIndex + 2].Value = records[recordIndex][fieldIndex];
            }
        }
    }

    private void RefreshRecordRowsGrid()
    {
        grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.EnableResizing;
        grid.ColumnHeadersHeight = 46;
        grid.Columns.Add(CreateReadOnlyColumn("Record", "レコード", 90, frozen: true));
        ApplyDefinitionColumnStyle(grid.Columns[0]);

        for (var fieldIndex = 0; fieldIndex < fields.Count; fieldIndex++)
        {
            var field = fields[fieldIndex];
            var column = CreateEditableColumn($"Field{fieldIndex + 1}", $"{field.Name}{Environment.NewLine}{field.DisplayDefinition}", 170);
            column.HeaderCell.Style.BackColor = DefinitionHeaderBackColor;
            grid.Columns.Add(column);
        }

        for (var recordIndex = 0; recordIndex < records.Count; recordIndex++)
        {
            var rowIndex = grid.Rows.Add();
            var row = grid.Rows[rowIndex];
            row.Cells[0].Value = $"Rec {recordIndex + 1}";

            for (var fieldIndex = 0; fieldIndex < fields.Count; fieldIndex++)
            {
                row.Cells[fieldIndex + 1].Value = records[recordIndex][fieldIndex];
            }
        }
    }

    private void UpdateLayoutButtons()
    {
        fieldRowsButton.Checked = layout == GridLayout.FieldRows;
        recordRowsButton.Checked = layout == GridLayout.RecordRows;
    }

    private static DataGridViewTextBoxColumn CreateReadOnlyColumn(string name, string header, int width, bool frozen)
    {
        return new DataGridViewTextBoxColumn
        {
            Name = name,
            HeaderText = header,
            Width = width,
            ReadOnly = true,
            Frozen = frozen,
            SortMode = DataGridViewColumnSortMode.NotSortable
        };
    }

    private static DataGridViewTextBoxColumn CreateEditableColumn(string name, string header, int width)
    {
        return new DataGridViewTextBoxColumn
        {
            Name = name,
            HeaderText = header,
            Width = width,
            SortMode = DataGridViewColumnSortMode.NotSortable
        };
    }

    private static void ApplyDefinitionColumnStyle(DataGridViewColumn column)
    {
        column.DefaultCellStyle.BackColor = DefinitionBackColor;
        column.HeaderCell.Style.BackColor = DefinitionHeaderBackColor;
    }

    private enum GridLayout
    {
        FieldRows,
        RecordRows
    }
}
