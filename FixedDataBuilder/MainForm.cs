namespace FixedDataBuilder;

public sealed class MainForm : Form
{
    private readonly DataGridView grid = new();
    private readonly ToolStrip toolStrip = new();
    private readonly StatusStrip statusStrip = new();
    private readonly ToolStripStatusLabel statusLabel = new();
    private readonly List<FieldDefinition> fields = [];
    private readonly List<List<string>> records = [];

    public MainForm()
    {
        Text = "FixedDataBuilder";
        StartPosition = FormStartPosition.CenterScreen;
        Width = 1100;
        Height = 700;

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
        grid.CellEndEdit += (_, e) =>
        {
            if (e.RowIndex >= 0 && e.ColumnIndex >= 3)
            {
                records[e.ColumnIndex - 3][e.RowIndex] = grid[e.ColumnIndex, e.RowIndex].Value?.ToString() ?? string.Empty;
            }
        };
    }

    private void LoadSampleDefinition()
    {
        fields.Clear();
        fields.AddRange(
        [
            new FieldDefinition("顧客番号", FieldDataType.PlainNumber, 8),
            new FieldDefinition("氏名", FieldDataType.FullWidthText, 20),
            new FieldDefinition("金額", FieldDataType.PackedSigned, 9)
        ]);

        records.Clear();
        records.Add(CreateEmptyRecord());
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

    private int CurrentRecordIndex()
    {
        return grid.CurrentCell is { ColumnIndex: >= 3 } cell ? cell.ColumnIndex - 3 : records.Count - 1;
    }

    private void SelectRecord(int recordIndex)
    {
        if (recordIndex < 0 || fields.Count == 0)
        {
            return;
        }

        grid.CurrentCell = grid[recordIndex + 3, 0];
    }

    private void RefreshGrid()
    {
        grid.Columns.Clear();
        grid.Rows.Clear();

        grid.Columns.Add(CreateReadOnlyColumn("Name", "項目名", 180));
        grid.Columns.Add(CreateReadOnlyColumn("Type", "型", 120));
        grid.Columns.Add(CreateReadOnlyColumn("Length", "桁数", 70));

        for (var recordIndex = 0; recordIndex < records.Count; recordIndex++)
        {
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = $"Record{recordIndex + 1}",
                HeaderText = $"Rec {recordIndex + 1}",
                Width = 140,
                SortMode = DataGridViewColumnSortMode.NotSortable
            });
        }

        for (var fieldIndex = 0; fieldIndex < fields.Count; fieldIndex++)
        {
            var field = fields[fieldIndex];
            var rowIndex = grid.Rows.Add();
            var row = grid.Rows[rowIndex];
            row.Cells[0].Value = field.Name;
            row.Cells[1].Value = DisplayType(field.Type);
            row.Cells[2].Value = field.Length;

            for (var recordIndex = 0; recordIndex < records.Count; recordIndex++)
            {
                row.Cells[recordIndex + 3].Value = records[recordIndex][fieldIndex];
            }
        }

        statusLabel.Text = $"{fields.Count} 項目 / {records.Count} レコード";
    }

    private static DataGridViewTextBoxColumn CreateReadOnlyColumn(string name, string header, int width)
    {
        return new DataGridViewTextBoxColumn
        {
            Name = name,
            HeaderText = header,
            Width = width,
            ReadOnly = true,
            Frozen = true,
            SortMode = DataGridViewColumnSortMode.NotSortable
        };
    }

    private static string DisplayType(FieldDataType type) => type switch
    {
        FieldDataType.PlainNumber => "平数字",
        FieldDataType.PackedUnsigned => "PAC_符号なし",
        FieldDataType.PackedSigned => "PAC_符号あり",
        FieldDataType.HalfWidthText => "文字_半角",
        FieldDataType.FullWidthText => "文字_全角",
        _ => type.ToString()
    };
}
