using System.Text;
using FixedDataBuilder;

internal static partial class Program
{
    private static void RunFieldCommentTests(string repo)
    {
        Test("Definition comments round-trip quoted commas, quotes, newlines, numeric text and surrounding spaces", () =>
        {
            var path = Path.Combine(temporaryDirectory, "comment-roundtrip.csv");
            var comment = "  期待値は3, \"前回値\"を保持" + Environment.NewLine + "空白も確認  ";
            var rows = DefinitionCsvWriter.NormalizeRows([
                new("コード", "X(4)", comment), new("区分", "9(1)", "3")]);
            DefinitionCsvWriter.Write(path, rows);
            var fields = DefinitionCsvReader.Read(path);
            Equal(2, fields.Count);
            Equal(comment, fields[0].Comment);
            Equal("3", fields[1].Comment);
            Equal(FieldDataType.PlainNumber, fields[1].Type);
            Equal(1, fields[1].Length);
            File.WriteAllText(path, DefinitionCsvWriter.BuildCsv(rows), new UTF8Encoding(true));
            Equal(comment, DefinitionCsvReader.Read(path)[0].Comment);
        });
        Test("Existing two-column and legacy type-length definitions remain compatible with optional comments", () =>
        {
            var path = Path.Combine(temporaryDirectory, "comment-legacy.csv");
            DefinitionCsvWriter.Write(path, [new("コード", "X(4)")]);
            Equal("項目名,定義", File.ReadLines(path).First());
            Equal("", DefinitionCsvReader.Read(path)[0].Comment);
            File.WriteAllText(path, "項目名,型,桁数,コメント\r\nコード,文字,4,前回の値\r\n区分,平数字,1\r\n");
            var fields = DefinitionCsvReader.Read(path);
            Equal(FieldDataType.Text, fields[0].Type);
            Equal(4, fields[0].StorageByteLength);
            Equal("前回の値", fields[0].Comment);
            Equal("", fields[1].Comment);
            Equal(FieldDataType.PlainNumber, fields[1].Type);
        });
        Test("Invalid quoted definition comments and comment-only rows cannot be silently saved", () =>
        {
            var path = Path.Combine(temporaryDirectory, "comment-invalid.csv");
            File.WriteAllText(path, "項目名,定義,コメント\r\nコード,X(4),\"閉じていない\r\n");
            Throws<InvalidDataException>(() => DefinitionCsvReader.Read(path));
            Throws<InvalidDataException>(() => DefinitionCsvWriter.NormalizeRows([new("", "", "期待値は3")]));
        });
        Test("Definition editor loads, duplicates and saves field comments without dropping quoted content", () =>
        {
            using var editor = new DefinitionEditorForm([new("区分", FieldDataType.PlainNumber, 1, "9(1)", Comment: "期待値は3, 前回値")]);
            var grid = Get<DataGridView>(editor, "grid");
            Equal("期待値は3, 前回値", grid["Comment", 0].Value);
            grid.CurrentCell = grid["Comment", 0];
            Invoke(editor, "DuplicateCurrentRow");
            Equal("期待値は3, 前回値", grid["Comment", 1].Value);
            grid["Comment", 1].Value = "  別の説明  ";
            var rows = DefinitionCsvWriter.NormalizeRows((IEnumerable<DefinitionCsvRow>)Invoke(editor, "ReadRows")!);
            var path = Path.Combine(temporaryDirectory, "comment-editor.csv");
            DefinitionCsvWriter.Write(path, rows);
            Equal("  別の説明  ", DefinitionCsvReader.Read(path)[1].Comment);
        });
        Test("Single-view comments stay read-only and never change data, saved bytes, Excel, full HEX or display limits", () =>
        {
            using var form = CreateCellDisplayForm(recordRows: false);
            var fields = Get<List<FieldDefinition>>(form, "fields");
            var before = Path.Combine(temporaryDirectory, "comment-before.dat");
            var after = Path.Combine(temporaryDirectory, "comment-after.dat");
            var options = Invoke(form, "CurrentSaveOptions");
            Invoke(form, "WriteData", before, options);
            var excelBefore = (IReadOnlyList<IReadOnlyList<ExcelCell>>)Invoke(form, "BuildExcelRows")!;
            var comment = "期待値は前回値と同じ" + Environment.NewLine + "半角空白も保持";
            fields[0] = fields[0] with { Comment = comment };
            Invoke(form, "SetSingleRecordView", true);
            var grid = Get<DataGridView>(form, "grid");
            Equal(comment, grid["FieldComment", 0].Value);
            Equal(true, grid["FieldComment", 0].ReadOnly);
            Equal(null, Invoke(form, "WrappedDisplayLines", 0, grid.Columns["FieldComment"].Index));
            var hex = grid["FieldHex", 0].Value;
            Invoke(form, "ApplyDisplayWrapOptions", new DisplayWrapOptions(true, 1, 10, true, 5));
            Equal(comment, grid["FieldComment", 0].Value);
            Equal(hex, grid["FieldHex", 0].Value);
            Invoke(form, "ChangeSingleRecord", 1);
            Equal(comment, grid["FieldComment", 0].Value);
            Invoke(form, "WriteData", after, options);
            Equal(true, File.ReadAllBytes(before).SequenceEqual(File.ReadAllBytes(after)));
            var excelAfter = (IReadOnlyList<IReadOnlyList<ExcelCell>>)Invoke(form, "BuildExcelRows")!;
            Equal(true, excelBefore.SelectMany(row => row).SequenceEqual(excelAfter.SelectMany(row => row)));
            Equal(false, Get<bool>(form, "hasUnsavedChanges"));
        });
        Test("Both list directions expose common field comments on item-name tooltips", () =>
        {
            using var form = new MainForm(["--definition", Path.Combine(repo, "samples", "comment-definition.csv"),
                "--data", Path.Combine(repo, "samples", "comment-data-utf8.csv"), "--csv-header", "present", "--layout", "field"]);
            var grid = Get<DataGridView>(form, "grid");
            Equal(true, grid[0, 1].ToolTipText.Contains("期待値は3"));
            Get<ToolStripMenuItem>(form, "recordRowsButton").PerformClick();
            Equal(true, grid.Columns[2].HeaderCell.ToolTipText.Contains("期待値は3"));
            Invoke(form, "SetSingleRecordView", true);
            Equal(false, grid.Columns["FieldStartPosition"].Visible);
            Equal("期待値は3。前回の区分を引き継ぐこと", grid["FieldComment", 1].Value);
            Equal("3", grid["SingleRecordValue", 1].Value);
        });
        Test("Reloading only comments preserves dirty data, hidden fields and single-view record selection", () =>
        {
            var path = Path.Combine(temporaryDirectory, "comment-reload.csv");
            File.Copy(Path.Combine(repo, "samples", "comment-definition.csv"), path);
            using var form = new MainForm(["--definition", path,
                "--data", Path.Combine(repo, "samples", "comment-data-utf8.csv"), "--csv-header", "present", "--layout", "field"]);
            Invoke(form, "SetSingleRecordView", true);
            var grid = Get<DataGridView>(form, "grid");
            grid["SingleRecordValue", 0].Value = "9999";
            Invoke(form, "UpdateRecordValue", 0, grid.Columns["SingleRecordValue"].Index);
            Get<HashSet<int>>(form, "hiddenFieldIndexes").Add(1);
            Invoke(form, "RefreshGrid");
            Invoke(form, "ChangeSingleRecord", 1);
            DefinitionCsvWriter.Write(path, [new("コード", "X(4)", "期待値コメントの更新"), new("区分", "9(1)")]);
            Equal(true, Invoke(form, "ReloadDefinitionCore"));
            Equal("9999", Get<List<List<string>>>(form, "records")[0][0]);
            Equal(true, Get<bool>(form, "hasUnsavedChanges"));
            Equal(1, Get<int>(form, "singleRecordIndex"));
            Equal(true, Get<HashSet<int>>(form, "hiddenFieldIndexes").Contains(1));
            Equal("期待値コメントの更新", grid["FieldComment", 0].Value);
            DefinitionCsvWriter.Write(path, [new("コード", "X(4)", "全体再読込でも更新"), new("区分", "9(1)")]);
            Invoke(form, "ReloadCurrentFilesCore");
            Equal(true, Get<HashSet<int>>(form, "hiddenFieldIndexes").Contains(1));
            Equal("全体再読込でも更新", grid["FieldComment", 0].Value);
            Equal("0001", Get<List<List<string>>>(form, "records")[0][0]);
            Equal(false, Get<bool>(form, "hasUnsavedChanges"));
        });
    }
}
