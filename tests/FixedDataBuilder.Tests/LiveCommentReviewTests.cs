using System.Reflection;
using FixedDataBuilder;

internal static partial class Program
{
    private static object? ReviewInvoke(Control pane, string method, params object?[] args) => pane.GetType()
        .GetMethod(method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(pane, args);

    private static void RunLiveCommentReviewTests(string repo)
    {
        MainForm CommentForm(string layout = "record") => new(["--definition", Path.Combine(repo, "samples", "comment-definition.csv"),
            "--data", Path.Combine(repo, "samples", "comment-data-utf8.csv"), "--csv-header", "present", "--layout", layout]);
        Test("Typing the selected-field comment changes only the definition and preserves every data value and binding", () =>
        {
            using var form = CommentForm();
            Invoke(form, "RestoreRecordFieldSelection", 0, 0);
            var rows = Get<List<List<string>>>(form, "records"); var references = rows.ToArray();
            var values = rows.SelectMany(row => row).ToArray();
            var comment = "  期待値は001, \"確認\"\r\n空白も保持  ";
            Get<TextBox>(form, "selectedCommentEditor").Text = comment;
            Equal(comment, Get<List<FieldDefinition>>(form, "fields")[0].Comment);
            Equal(true, Get<bool>(form, "hasDefinitionEdits")); Equal(false, Get<bool>(form, "hasUnsavedChanges"));
            Equal(true, references.Zip(rows).All(pair => ReferenceEquals(pair.First, pair.Second)));
            Equal(true, values.SequenceEqual(rows.SelectMany(row => row)));
            Equal(comment, Get<DataGridView>(form, "grid")[1, Get<int>(form, "recordCommentRowIndex")].Value);
        });
        Test("The first comment creates its display row while retaining selected field and data values", () =>
        {
            using var form = CreateCellDisplayForm(recordRows: true);
            Invoke(form, "RestoreRecordFieldSelection", 2, 1);
            var before = Get<List<List<string>>>(form, "records").SelectMany(row => row).ToArray();
            Get<TextBox>(form, "selectedCommentEditor").Text = "初めてのコメント";
            Equal("初めてのコメント", Get<List<FieldDefinition>>(form, "fields")[1].Comment);
            Equal(true, Get<int>(form, "recordCommentRowIndex") >= 0);
            Equal(1, Invoke(form, "CurrentFieldIndex")); Equal(2, Invoke(form, "CurrentRecordIndex"));
            Equal(true, before.SequenceEqual(Get<List<List<string>>>(form, "records").SelectMany(row => row)));
        });
        Test("Selecting another item refreshes the comment editor without copying a previous item's text", () =>
        {
            using var form = CommentForm("field");
            Invoke(form, "RestoreRecordFieldSelection", 0, 0);
            Get<TextBox>(form, "selectedCommentEditor").Text = "コードの確認";
            var second = Get<List<FieldDefinition>>(form, "fields")[1].Comment;
            Invoke(form, "RestoreRecordFieldSelection", 0, 1);
            Equal(second, Get<TextBox>(form, "selectedCommentEditor").Text);
            Equal("コードの確認", Get<List<FieldDefinition>>(form, "fields")[0].Comment);
            Equal(second, Get<List<FieldDefinition>>(form, "fields")[1].Comment);
        });
        Test("Inline horizontal comments are editable and never act as record values or row-deletion targets", () =>
        {
            using var form = CommentForm(); var grid = Get<DataGridView>(form, "grid");
            var values = Get<List<List<string>>>(form, "records").SelectMany(row => row).ToArray();
            var row = Get<int>(form, "recordCommentRowIndex");
            Equal(false, grid[1, row].ReadOnly); Equal(true, grid[0, row].ReadOnly);
            grid[1, row].Value = "  更新,コメント  ";
            Invoke(form, "UpdateRecordValue", row, 1);
            Equal("  更新,コメント  ", Get<List<FieldDefinition>>(form, "fields")[0].Comment);
            Equal(-1, Invoke(form, "RowActionRecordFromCell", 1, row));
            Equal(false, Get<bool>(form, "hasUnsavedChanges"));
            Equal(true, values.SequenceEqual(Get<List<List<string>>>(form, "records").SelectMany(row => row)));
        });
        Test("Single-record comment editing updates tooltips and preserves data HEX", () =>
        {
            using var form = CommentForm("field"); Invoke(form, "SetSingleRecordView", true);
            var grid = Get<DataGridView>(form, "grid"); var hex = grid["FieldHex", 0].Value;
            grid["FieldComment", 0].Value = "新しい期待値\r\n次の行";
            Invoke(form, "UpdateRecordValue", 0, grid.Columns["FieldComment"].Index);
            Equal("新しい期待値\r\n次の行", Get<List<FieldDefinition>>(form, "fields")[0].Comment);
            Equal(hex, grid["FieldHex", 0].Value);
            Equal(true, grid[0, 0].ToolTipText.Contains("新しい期待値"));
        });
        Test("Saving a typed comment preserves data edits and source bytes and reloads the exact multiline comment", () =>
        {
            using var form = CommentForm(); Invoke(form, "RestoreRecordFieldSelection", 0, 0);
            var path = Path.Combine(repo, "samples", "comment-data-utf8.csv"); var bytes = File.ReadAllBytes(path);
            Invoke(form, "SetUnsavedChanges", true);
            Get<TextBox>(form, "selectedCommentEditor").Text = "  行1,\"引用\"\r\n行2  ";
            var target = Path.Combine(temporaryDirectory, "live-comments-definition.csv");
            Invoke(form, "SaveCurrentDefinitionToPath", target, DefinitionTextEncoding.Utf8);
            Equal("  行1,\"引用\"\r\n行2  ", DefinitionCsvReader.Read(target)[0].Comment);
            Equal(false, Get<bool>(form, "hasDefinitionEdits")); Equal(true, Get<bool>(form, "hasUnsavedChanges"));
            Equal(true, bytes.SequenceEqual(File.ReadAllBytes(path)));
        });
        Test("An unchanged comment does not mark a clean definition dirty", () =>
        {
            using var form = CommentForm(); var comment = Get<List<FieldDefinition>>(form, "fields")[0].Comment;
            Invoke(form, "SetFieldComment", 0, comment);
            Equal(false, Get<bool>(form, "hasDefinitionEdits"));
        });
        Test("An editor bound to a replaced definition cannot write stale comment text into new fields", () =>
        {
            using var form = CommentForm(); Invoke(form, "RestoreRecordFieldSelection", 0, 0);
            Invoke(form, "LoadDefinition", Path.Combine(repo, "samples", "basic-definition.csv"));
            var previous = Get<List<FieldDefinition>>(form, "fields")[0].Comment;
            Get<TextBox>(form, "selectedCommentEditor").Text = "前の定義の未反映文字";
            Equal(previous, Get<List<FieldDefinition>>(form, "fields")[0].Comment);
        });

        Test("Visual review marks selected data cells only and keeps all original values read-only", () =>
        {
            using var form = new VisualComparisonForm(VisualData("OUT", 3, 3));
            var pane = VisualPane(form, false); var grid = VisualGrid(pane); var start = Get<int>(pane, "metadataRows");
            grid.ClearSelection(); grid[1, start].Selected = true; grid[2, start + 1].Selected = true;
            grid[0, start].Selected = true; grid[1, 0].Selected = true;
            ReviewInvoke(pane, "MarkSelected", true);
            Equal(true, ReviewInvoke(pane, "IsReviewed", 0, 0)); Equal(true, ReviewInvoke(pane, "IsReviewed", 1, 1));
            Equal(2, Get<HashSet<ReviewCell>>(pane, "reviewedCells").Count);
            Equal(true, grid.ReadOnly); Equal("OUT_1_1", grid[1, start].Value);
            Equal(true, Get<bool>(form, "comparisonSetDirty"));
        });
        Test("Review mark clearing and next-unreviewed navigation work without treating metadata as data", () =>
        {
            using var form = new VisualComparisonForm(VisualData("OUT", 2, 2)); var pane = VisualPane(form, false);
            ReviewInvoke(pane, "SelectReviewCell", 0, 0); ReviewInvoke(pane, "MarkSelected", true);
            ReviewInvoke(pane, "NextUnreviewed");
            var grid = VisualGrid(pane); Equal(2, grid.CurrentCell.ColumnIndex);
            Equal(Get<int>(pane, "metadataRows"), grid.CurrentCell.RowIndex);
            ReviewInvoke(pane, "MarkSelected", true); ReviewInvoke(pane, "MarkSelected", false);
            Equal(false, ReviewInvoke(pane, "IsReviewed", 0, 1)); Equal(true, ReviewInvoke(pane, "IsReviewed", 0, 0));
        });
        Test("Every-cell reviewed navigation reports completion and does not change values", () =>
        {
            using var form = new VisualComparisonForm(VisualData("OUT", 2, 2)); var pane = VisualPane(form, false);
            var grid = VisualGrid(pane); var start = Get<int>(pane, "metadataRows");
            grid.ClearSelection();
            for (var row = start; row < start + 2; row++) for (var col = 1; col <= 2; col++) grid[col, row].Selected = true;
            ReviewInvoke(pane, "MarkSelected", true); ReviewInvoke(pane, "NextUnreviewed");
            Equal(4, Get<HashSet<ReviewCell>>(pane, "reviewedCells").Count);
            Equal(true, Get<Label>(pane, "reviewCount").Text.Contains("全セル"));
        });
        Test("Field search accepts No and partial names while preserving the selected record", () =>
        {
            using var form = new VisualComparisonForm(VisualData("OUT", 4, 8)); var pane = VisualPane(form, false);
            ReviewInvoke(pane, "SelectReviewCell", 2, 0);
            ReviewInvoke(pane, "FindField", "No.7");
            var grid = VisualGrid(pane); Equal(7, grid.CurrentCell.ColumnIndex);
            Equal(Get<int>(pane, "metadataRows") + 2, grid.CurrentCell.RowIndex);
            ReviewInvoke(pane, "FindField", "FIELD_3"); Equal(3, grid.CurrentCell.ColumnIndex);
            ReviewInvoke(pane, "FindField", "unknown"); Equal(3, grid.CurrentCell.ColumnIndex);
        });
        Test("Peer lookup moves to one exact named item without synchronizing row numbers", () =>
        {
            var fields = new[] { new FieldDefinition("共通", FieldDataType.Text, 10), new FieldDefinition("出力専用", FieldDataType.Text, 10) };
            using var form = new VisualComparisonForm(ComparisonDataSet.FromRecords(fields, new[] { new[] { "A", "B" }, new[] { "C", "D" } }, new(DataFileFormat.Csv)));
            Invoke(form, "SetInputDataSet", ComparisonDataSet.FromRecords(fields.Reverse(), new[] { new[] { "X", "Y" } }, new(DataFileFormat.Csv)));
            var input = VisualPane(form, true); var output = VisualPane(form, false);
            ReviewInvoke(output, "SelectReviewCell", 1, 0);
            typeof(VisualComparisonForm).GetField("activeReviewPane", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(form, output);
            Invoke(form, "ShowPeerField");
            Equal(2, VisualGrid(input).CurrentCell.ColumnIndex);
            Equal(Get<int>(input, "metadataRows"), VisualGrid(input).CurrentCell.RowIndex);
            Equal(Get<int>(output, "metadataRows") + 1, VisualGrid(output).CurrentCell.RowIndex);
        });
        Test("Ambiguous same-name items require an explicit No rather than silently selecting one", () =>
        {
            var fields = new[] { new FieldDefinition("同名", FieldDataType.Text, 1), new FieldDefinition("同名", FieldDataType.Text, 1) };
            using var form = new VisualComparisonForm(ComparisonDataSet.FromRecords(fields, new[] { new[] { "A", "B" } }, new(DataFileFormat.Csv)));
            var pane = VisualPane(form, false); var cell = VisualGrid(pane).CurrentCell;
            Equal(false, ReviewInvoke(pane, "FindPeerField", "同名")); Equal(cell, VisualGrid(pane).CurrentCell);
        });
        Test("Review marks survive ruler toggling and swapping but reset for changed source values", () =>
        {
            using var form = new VisualComparisonForm(VisualData("OUT", 2, 2)); var pane = VisualPane(form, false);
            ReviewInvoke(pane, "SelectReviewCell", 0, 0); ReviewInvoke(pane, "MarkSelected", true);
            Get<CheckBox>(pane, "showByteRuler").Checked = false;
            Invoke(form, "SwapLists"); Equal(true, ReviewInvoke(pane, "IsReviewed", 0, 0));
            Invoke(form, "SetOutputDataSet", VisualData("CHANGED", 2, 2));
            Equal(0, Get<HashSet<ReviewCell>>(pane, "reviewedCells").Count);
        });
        Test("CSV REDEFINES aliases cannot be marked as physical data cells", () =>
        {
            var fields = new[] { new FieldDefinition("BASE", FieldDataType.Text, 1), new FieldDefinition("ALIAS", FieldDataType.Text, 1, RedefinesName: "BASE") };
            using var form = new VisualComparisonForm(ComparisonDataSet.FromRecords(fields, new[] { new[] { "A", "" } }, new(DataFileFormat.Csv)));
            var pane = VisualPane(form, false); ReviewInvoke(pane, "SelectReviewCell", 0, 1); ReviewInvoke(pane, "MarkSelected", true);
            Equal(0, Get<HashSet<ReviewCell>>(pane, "reviewedCells").Count);
            ReviewInvoke(pane, "NextUnreviewed"); Equal(1, VisualGrid(pane).CurrentCell.ColumnIndex);
        });
    }
}
