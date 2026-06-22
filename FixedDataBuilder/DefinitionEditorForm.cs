namespace FixedDataBuilder;

public sealed class DefinitionEditorForm : Form
{
    private const string TypeHalfWidthText = "X 半角文字";
    private const string TypeFullWidthText = "N 全角文字";
    private const string TypeNumber = "9 数字";
    private const string TypeSignedNumber = "S9 符号付き数字";

    private readonly DataGridView grid = new();
    private readonly IReadOnlyList<FieldDefinition> initialFields;

    public string? SavedPath { get; private set; }

    public DefinitionEditorForm(IReadOnlyList<FieldDefinition> fields)
    {
        initialFields = fields;

        Text = "定義ファイル作成";
        StartPosition = FormStartPosition.CenterParent;
        Width = 860;
        Height = 520;
        MinimumSize = new Size(760, 420);

        Controls.Add(grid);
        Controls.Add(BuildToolStrip());

        BuildGrid();
        LoadInitialRows();
        UpdateAllPreviewCells();
    }

    private ToolStrip BuildToolStrip()
    {
        var toolStrip = new ToolStrip
        {
            Dock = DockStyle.Top,
            GripStyle = ToolStripGripStyle.Hidden
        };
        toolStrip.Items.Add(CreateButton("追加", (_, _) => AddRow()));
        toolStrip.Items.Add(CreateButton("複製", (_, _) => DuplicateCurrentRow()));
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
        grid.CellValueChanged += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0)
            {
                return;
            }

            if (grid.Columns[e.ColumnIndex].Name == "Type")
            {
                UpdateRowEditability(e.RowIndex);
            }

            UpdatePreviewCell(e.RowIndex);
        };
        grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (grid.IsCurrentCellDirty)
            {
                grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        };

        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Name",
            HeaderText = "項目名",
            FillWeight = 28,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        grid.Columns.Add(new DataGridViewComboBoxColumn
        {
            Name = "Type",
            HeaderText = "型",
            FillWeight = 28,
            FlatStyle = FlatStyle.Flat,
            SortMode = DataGridViewColumnSortMode.NotSortable,
            DataSource = new[] { TypeHalfWidthText, TypeFullWidthText, TypeNumber, TypeSignedNumber }
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "IntegerDigits",
            HeaderText = "整数",
            FillWeight = 12,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "DecimalDigits",
            HeaderText = "小数",
            FillWeight = 12,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        grid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            Name = "Comp3",
            HeaderText = "COMP-3",
            FillWeight = 12,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Preview",
            HeaderText = "定義プレビュー",
            FillWeight = 24,
            ReadOnly = true,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
    }

    private void LoadInitialRows()
    {
        if (initialFields.Count == 0)
        {
            return;
        }

        foreach (var field in initialFields)
        {
            var type = field.Type switch
            {
                FieldDataType.FullWidthText => TypeFullWidthText,
                FieldDataType.Text or FieldDataType.HalfWidthText => TypeHalfWidthText,
                FieldDataType.PlainNumber or FieldDataType.PackedUnsigned => TypeNumber,
                FieldDataType.SignedNumber or FieldDataType.PackedSigned => TypeSignedNumber,
                _ => TypeHalfWidthText
            };
            var comp3 = field.Type is FieldDataType.PackedUnsigned or FieldDataType.PackedSigned;
            AddDefinitionRow(
                field.Name,
                type,
                field.IntegerDigitLength.ToString(),
                field.DecimalScale == 0 ? string.Empty : field.DecimalScale.ToString(),
                comp3);
        }
    }

    private void AddDefinitionRow(string name, string type, string integerDigits, string decimalDigits, bool comp3)
    {
        var rowIndex = grid.Rows.Add();
        var row = grid.Rows[rowIndex];
        row.Cells["Name"].Value = name;
        row.Cells["Type"].Value = type;
        row.Cells["IntegerDigits"].Value = integerDigits;
        row.Cells["DecimalDigits"].Value = decimalDigits;
        row.Cells["Comp3"].Value = comp3;
        UpdateRowEditability(rowIndex);
        UpdatePreviewCell(rowIndex);
    }

    private void AddRow()
    {
        AddDefinitionRow(string.Empty, TypeHalfWidthText, "1", string.Empty, false);
        var rowIndex = grid.Rows.Count - 2;
        if (rowIndex >= 0)
        {
            grid.CurrentCell = grid["Name", rowIndex];
            grid.BeginEdit(selectAll: true);
        }
    }

    private void DuplicateCurrentRow()
    {
        if (grid.CurrentRow is null || grid.CurrentRow.IsNewRow)
        {
            return;
        }

        var row = grid.CurrentRow;
        AddDefinitionRow(
            row.Cells["Name"].Value?.ToString() ?? string.Empty,
            row.Cells["Type"].Value?.ToString() ?? TypeHalfWidthText,
            row.Cells["IntegerDigits"].Value?.ToString() ?? "1",
            row.Cells["DecimalDigits"].Value?.ToString() ?? string.Empty,
            Convert.ToBoolean(row.Cells["Comp3"].Value ?? false));
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
                row.Cells["Name"].Value?.ToString() ?? string.Empty,
                BuildDefinition(row));
        }
    }

    private void UpdateAllPreviewCells()
    {
        foreach (DataGridViewRow row in grid.Rows)
        {
            if (!row.IsNewRow)
            {
                UpdatePreviewCell(row.Index);
            }
        }
    }

    private void UpdatePreviewCell(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= grid.Rows.Count || grid.Rows[rowIndex].IsNewRow)
        {
            return;
        }

        try
        {
            grid.Rows[rowIndex].Cells["Preview"].Value = BuildDefinition(grid.Rows[rowIndex]);
            grid.Rows[rowIndex].Cells["Preview"].Style.BackColor = Color.Empty;
            grid.Rows[rowIndex].Cells["Preview"].ToolTipText = string.Empty;
        }
        catch (Exception ex)
        {
            grid.Rows[rowIndex].Cells["Preview"].Value = string.Empty;
            grid.Rows[rowIndex].Cells["Preview"].Style.BackColor = Color.FromArgb(255, 225, 225);
            grid.Rows[rowIndex].Cells["Preview"].ToolTipText = ex.Message;
        }
    }

    private void UpdateRowEditability(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= grid.Rows.Count || grid.Rows[rowIndex].IsNewRow)
        {
            return;
        }

        var row = grid.Rows[rowIndex];
        var type = row.Cells["Type"].Value?.ToString() ?? TypeHalfWidthText;
        var isNumber = type is TypeNumber or TypeSignedNumber;
        var decimalCell = row.Cells["DecimalDigits"];

        decimalCell.ReadOnly = !isNumber;
        decimalCell.Style.BackColor = isNumber ? Color.Empty : SystemColors.Control;
        decimalCell.ToolTipText = isNumber ? string.Empty : "文字型では小数桁を入力できません。";

        if (!isNumber)
        {
            decimalCell.Value = string.Empty;
        }
    }

    private static string BuildDefinition(DataGridViewRow row)
    {
        var type = row.Cells["Type"].Value?.ToString() ?? TypeHalfWidthText;
        var integerDigitsText = row.Cells["IntegerDigits"].Value?.ToString()?.Trim() ?? string.Empty;
        var decimalDigitsText = row.Cells["DecimalDigits"].Value?.ToString()?.Trim() ?? string.Empty;
        var comp3 = Convert.ToBoolean(row.Cells["Comp3"].Value ?? false);

        if (!int.TryParse(integerDigitsText, out var integerDigits) || integerDigits <= 0)
        {
            throw new InvalidDataException("整数桁数は1以上の数値で入力してください。");
        }

        var hasDecimal = !string.IsNullOrWhiteSpace(decimalDigitsText);
        var decimalDigits = 0;
        if (hasDecimal && (!int.TryParse(decimalDigitsText, out decimalDigits) || decimalDigits <= 0))
        {
            throw new InvalidDataException("小数桁数は空欄、または1以上の数値で入力してください。");
        }

        return type switch
        {
            TypeHalfWidthText => $"X({integerDigits})",
            TypeFullWidthText => $"N({integerDigits})",
            TypeNumber => BuildNumberDefinition("9", integerDigits, decimalDigits, hasDecimal, comp3),
            TypeSignedNumber => BuildNumberDefinition("S9", integerDigits, decimalDigits, hasDecimal, comp3),
            _ => throw new InvalidDataException("型を選択してください。")
        };
    }

    private static string BuildNumberDefinition(string prefix, int integerDigits, int decimalDigits, bool hasDecimal, bool comp3)
    {
        var definition = hasDecimal
            ? $"{prefix}({integerDigits}V{decimalDigits})"
            : $"{prefix}({integerDigits})";
        return comp3 ? $"{definition} COMP-3" : definition;
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
