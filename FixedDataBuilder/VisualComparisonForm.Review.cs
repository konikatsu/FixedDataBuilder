using System.Globalization;

namespace FixedDataBuilder;

public sealed partial class VisualComparisonForm
{
    private sealed partial class RecordListPane
    {
        private readonly HashSet<ReviewCell> reviewedCells = [];
        private readonly TextBox fieldSearch = new() { Name = "ReviewFieldSearch", Width = 130, PlaceholderText = "項目名 / No." };
        private readonly Label reviewCount = new() { Name = "ReviewCount", AutoSize = true, Margin = new Padding(6, 7, 3, 0) };
        public event EventHandler? ReviewChanged;
        public event EventHandler? FieldSelected;
        public int SelectedField => grid.CurrentCell is { ColumnIndex: > 0 } cell ? cell.ColumnIndex - 1 : -1;

        private void InitializeReviewActions(FlowLayoutPanel actions)
        {
            Button Action(string name, string caption, System.Action click)
            {
                var button = new Button { Name = name, Text = caption, AutoSize = true };
                button.Click += (_, _) => click();
                return button;
            }
            actions.Controls.AddRange([fieldSearch,
                Action("FindReviewField", "項目を探す", () => FindField(fieldSearch.Text)),
                Action("MarkReviewed", "選択を確認済み", () => MarkSelected(true)),
                Action("ClearReviewed", "確認解除", () => MarkSelected(false)),
                Action("NextUnreviewed", "次の未確認", NextUnreviewed), reviewCount]);
            fieldSearch.KeyDown += (_, e) =>
            {
                if (e.KeyCode != Keys.Enter) return;
                FindField(fieldSearch.Text); e.Handled = e.SuppressKeyPress = true;
            };
            grid.SelectionChanged += (_, _) => { if (!rebuilding) FieldSelected?.Invoke(this, EventArgs.Empty); };
            UpdateReviewCount();
        }

        private bool CanReview(int record, int field) => DataSet is { } data && record >= 0 && record < data.Records.Count
            && field >= 0 && field < data.Fields.Count && !(data.Profile.Format == DataFileFormat.Csv && data.Fields[field].IsRedefines);

        public void MarkSelected(bool mark)
        {
            var changed = false;
            foreach (DataGridViewCell cell in grid.SelectedCells)
            {
                var record = cell.RowIndex - metadataRows;
                var field = cell.ColumnIndex - 1;
                if (!CanReview(record, field)) continue;
                changed |= mark ? reviewedCells.Add(new(record, field)) : reviewedCells.Remove(new(record, field));
            }
            if (!changed) return;
            grid.Invalidate(); UpdateReviewCount(); UpdateSelection();
            ReviewChanged?.Invoke(this, EventArgs.Empty);
        }

        private void UpdateReviewCount()
        {
            var count = DataSet is { } data ? (long)data.Records.Count * data.Fields.Count(field =>
                data.Profile.Format != DataFileFormat.Csv || !field.IsRedefines) : 0;
            reviewCount.Text = $"確認済み {reviewedCells.Count:N0} / {count:N0} セル";
        }

        public bool IsReviewed(int record, int field) => reviewedCells.Contains(new(record, field));

        public void NextUnreviewed()
        {
            if (DataSet is not { } data || data.Records.Count == 0) return;
            var fields = Enumerable.Range(0, data.Fields.Count).Where(field => CanReview(0, field)).ToArray();
            if (fields.Length == 0) return;
            var total = (long)fields.Length * data.Records.Count;
            if (reviewedCells.Count == total) { reviewCount.Text = "全セルを目視確認済みです（合否の自動判定はしません）。"; return; }
            var currentRecord = (grid.CurrentCell?.RowIndex ?? -1) - metadataRows;
            var currentField = Array.IndexOf(fields, SelectedField);
            var start = currentRecord >= 0 && currentField >= 0 ? (long)currentRecord * fields.Length + currentField : -1;
            for (long step = 1; step <= Math.Min(total, (long)reviewedCells.Count + 1); step++)
            {
                var ordinal = (start + step) % total;
                var record = (int)(ordinal / fields.Length); var field = fields[(int)(ordinal % fields.Length)];
                if (IsReviewed(record, field)) continue;
                SelectReviewCell(record, field); return;
            }
        }

        public void SelectReviewCell(int record, int field)
        {
            if (DataSet is not { } data || field < 0 || field >= data.Fields.Count) return;
            var row = record >= 0 && record < data.Records.Count ? metadataRows + record : 0;
            grid.ClearSelection();
            grid.CurrentCell = grid[field + 1, row];
            grid.CurrentCell.Selected = true;
            if (!grid.Columns[field + 1].Frozen) grid.FirstDisplayedScrollingColumnIndex = field + 1;
            if (row >= metadataRows && !grid.Rows[row].Frozen) grid.FirstDisplayedScrollingRowIndex = row;
            UpdateSelection();
        }

        public void FindField(string query)
        {
            if (DataSet is not { } data) return;
            query = query.Trim();
            var ordinalText = query.StartsWith("No.", StringComparison.OrdinalIgnoreCase) ? query[3..] : query;
            var matches = int.TryParse(ordinalText, NumberStyles.None, CultureInfo.InvariantCulture, out var ordinal)
                ? ordinal >= 1 && ordinal <= data.Fields.Count ? new[] { ordinal - 1 } : []
                : query.Length == 0 ? [] : Enumerable.Range(0, data.Fields.Count)
                    .Where(index => data.Fields[index].Name.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length == 0) { reviewCount.Text = "項目が見つかりません。項目名かNo.を入力してください。"; return; }
            var next = matches.FirstOrDefault(index => index > SelectedField, matches[0]);
            var record = (grid.CurrentCell?.RowIndex ?? metadataRows) - metadataRows;
            SelectReviewCell(record, next);
            reviewCount.Text = $"No.{next + 1} {data.Fields[next].Name} ／ 検索候補 {matches.Length}件";
        }

        public bool FindPeerField(string name)
        {
            if (DataSet is not { } data) return false;
            var matches = Enumerable.Range(0, data.Fields.Count).Where(index =>
                string.Equals(data.Fields[index].Name, name, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length != 1)
            {
                reviewCount.Text = matches.Length == 0 ? "同名項目がありません。項目検索で選んでください。"
                    : "同名項目が複数あります。No.で選んでください。";
                return false;
            }
            SelectReviewCell((grid.CurrentCell?.RowIndex ?? metadataRows) - metadataRows, matches[0]);
            return true;
        }

        public ComparisonSetSide? CaptureSetSide()
        {
            if (DataSet is not { } data) return null;
            var view = CaptureViewport();
            var heights = new Dictionary<int, int>();
            for (var record = 0; record < data.Records.Count; record++)
            {
                var height = grid.Rows.SharedRow(metadataRows + record).Height;
                if (height != grid.RowTemplate.Height) heights[record] = height;
            }
            return new(ComparisonSnapshot.FromData(data), reviewedCells.OrderBy(cell => cell.Record).ThenBy(cell => cell.Field).ToArray(),
                new(showSpaces.Checked, showByteRuler.Checked,
                    Math.Clamp((grid.CurrentCell?.RowIndex ?? -1) - metadataRows, -1, Math.Max(-1, data.Records.Count - 1)), SelectedField,
                    Math.Max(0, view.Row - metadataRows), Math.Max(0, view.Column), Math.Max(0, view.HorizontalOffset),
                    grid.Columns.Cast<DataGridViewColumn>().Select(column => column.Width).ToArray(), heights));
        }

        public void RestoreSetSide(ComparisonSetSide? side, ComparisonDataSet? data)
        {
            DataSet = data;
            reviewedCells.Clear();
            showSpaces.Checked = side?.View.ShowSpaces ?? true;
            // Avoid event rebuilding before all saved state is ready.
            rebuilding = true;
            try { showByteRuler.Checked = side?.View.ShowRuler ?? true; }
            finally { rebuilding = false; }
            RefreshGrid();
            if (side is null) { UpdateReviewCount(); return; }
            foreach (var cell in side.Reviewed) reviewedCells.Add(cell);
            for (var column = 0; column < grid.ColumnCount; column++)
                grid.Columns[column].Width = Math.Max(grid.Columns[column].MinimumWidth, side.View.ColumnWidths[column]);
            foreach (var item in side.View.RowHeights) grid.Rows[metadataRows + item.Key].Height = item.Value;
            SelectReviewCell(side.View.CurrentRecord, side.View.CurrentField);
            RestoreViewport(new(metadataRows + side.View.FirstRecord, side.View.FirstField, side.View.HorizontalOffset));
            UpdateReviewCount(); grid.Invalidate();
        }

        private void ResetReviewIfDataChanged(ComparisonDataSet? previous)
        {
            if (previous?.DefinitionFingerprint != DataSet?.DefinitionFingerprint || previous?.RecordFingerprint != DataSet?.RecordFingerprint)
                reviewedCells.Clear();
            UpdateReviewCount(); grid.Invalidate();
        }
    }
}
