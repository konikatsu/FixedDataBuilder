using System.Text;

namespace FixedDataBuilder;

public sealed partial class MainForm
{
    private readonly ToolStripMenuItem byteRulerButton = new("バイトルーラー") { Name = "ByteRuler", CheckOnClick = true };
    private int recordRulerRowIndex = -1;
    private readonly Dictionary<int, int> byteRulerLengths = new();
    private readonly Dictionary<int, string> byteRulerErrors = new();
    private bool changingByteRuler;

    private void ChangeByteRulerVisibility()
    {
        if (changingByteRuler) return;
        if (!grid.EndEdit())
        {
            changingByteRuler = true;
            byteRulerButton.Checked = !byteRulerButton.Checked;
            changingByteRuler = false;
            return;
        }
        var record = CurrentRecordIndex();
        var field = CurrentFieldIndex();
        RefreshGrid();
        if (record >= 0 && field >= 0) RestoreRecordFieldSelection(record, field);
    }

    private void AddByteRulerRow()
    {
        if (!byteRulerButton.Checked) return;
        recordRulerRowIndex = grid.Rows.Count;
        for (var index = 0; index < recordRulerRowIndex; index++) grid.Rows[index].Frozen = true;
        var row = grid.Rows[grid.Rows.Add()];
        row.ReadOnly = true;
        row.Frozen = true;
        row.DefaultCellStyle.BackColor = Color.FromArgb(242, 245, 248);
        row.DefaultCellStyle.SelectionBackColor = row.DefaultCellStyle.BackColor;
        row.DefaultCellStyle.SelectionForeColor = grid.ForeColor;
        row.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        row.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
        row.Cells[0].Value = "バイト目盛り";
        row.Cells[0].ToolTipText = "各項目の先頭が1です。目盛りの単位はバイトです。文字の画面幅や折り返し後の位置とは異なります。";
        RefreshByteRulers();
    }

    private void RefreshByteRulers(int? changedField = null)
    {
        if (layout != GridLayout.RecordRows || recordRulerRowIndex < 0 || recordRulerRowIndex >= grid.Rows.Count) return;
        var encoding = IsCsv ? JapaneseEncodings.ForCsv(currentCsvEncoding) : null;
        for (var column = 1; column < grid.Columns.Count; column++)
        {
            var field = FieldIndexFromRecordColumn(column);
            if (changedField is not null && changedField != field) continue;
            try
            {
                var length = IsCsv ? records.Select(record => encoding!.GetByteCount(record[field])).DefaultIfEmpty(0).Max()
                    : fields[field].PhysicalByteLength;
                byteRulerLengths[field] = length;
                byteRulerErrors.Remove(field);
            }
            catch (EncoderFallbackException)
            {
                byteRulerLengths.Remove(field);
                byteRulerErrors[field] = "選択した文字コードで表せない値があるため、最大バイト長を計測できません。";
            }
        }
        UpdateByteRulerDisplay();
    }

    private void UpdateByteRulerDisplay(int? onlyColumn = null)
    {
        if (layout != GridLayout.RecordRows || recordRulerRowIndex < 0 || recordRulerRowIndex >= grid.Rows.Count) return;
        var row = grid.Rows[recordRulerRowIndex];
        row.Height = Math.Max(grid.RowTemplate.Height, grid.Font.Height * 2 + 8);
        var digitWidth = Math.Max(1f, TextRenderer.MeasureText("0123456789", grid.Font, Size.Empty,
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix).Width / 10f);
        for (var column = 1; column < grid.Columns.Count; column++)
        {
            if (onlyColumn is not null && onlyColumn != column) continue;
            var cell = row.Cells[column];
            var field = FieldIndexFromRecordColumn(column);
            if (byteRulerErrors.TryGetValue(field, out var error))
            {
                cell.Value = "計測エラー";
                cell.ToolTipText = error;
                continue;
            }
            var length = byteRulerLengths.GetValueOrDefault(field);
            var slots = (int)Math.Floor(Math.Max(1, grid.Columns[column].Width - cell.InheritedStyle.Padding.Horizontal - 8) / digitWidth);
            cell.Value = ByteRulerText.Format(length, slots);
            cell.ToolTipText = $"項目先頭=1 / {(IsCsv ? "読み込んだデータの最大" : "固定長の格納長")} {length} バイト。\r\n"
                + (IsCsv ? $"文字コード: {JapaneseEncodings.Name(currentCsvEncoding)}。" : "型N・COMP-3は実際の格納バイト数です。")
                + "文字の画面幅とは別の単位です。…は目盛りの続きがあることを示します。";
        }
    }
}
