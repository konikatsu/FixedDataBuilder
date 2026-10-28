using System.Text;
using FixedDataBuilder;

internal static partial class Program
{
    private static void RunDefinitionEncodingTests(string repo)
    {
        Test("Definition CSV reads and writes UTF8, strict SJIS and MS932 with Japanese comments", () =>
        {
            foreach (var mode in new[] { DefinitionTextEncoding.Utf8, DefinitionTextEncoding.ShiftJis, DefinitionTextEncoding.Ms932 })
            {
                var path = Path.Combine(temporaryDirectory, $"definition-{mode}.csv");
                var comment = "期待値は3, 前回の値を保持";
                DefinitionCsvWriter.Write(path, [new("区分", "9(1)", comment)], mode);
                Equal("区分", DefinitionCsvReader.Read(path, mode)[0].Name);
                Equal(comment, DefinitionCsvReader.Read(path, mode)[0].Comment);
                Equal(comment, DefinitionCsvReader.Read(path)[0].Comment);
                Equal(mode, DefinitionFileText.SuggestedSaveEncoding(path, mode));
            }
            Equal("期待値は3。前回の区分を引き継ぐこと", DefinitionCsvReader.Read(Path.Combine(repo, "samples", "comment-definition-sjis.csv"))[1].Comment);
        });
        Test("SJIS and MS932 definitions preserve their distinct mappings and extension rules", () =>
        {
            var path = Path.Combine(temporaryDirectory, "definition-mapping.csv");
            DefinitionCsvWriter.Write(path, [new("項目", "X(4)", "～")], DefinitionTextEncoding.Ms932);
            Equal("～", DefinitionCsvReader.Read(path)[0].Comment);
            Equal("〜", DefinitionCsvReader.Read(path, DefinitionTextEncoding.ShiftJis)[0].Comment);
            DefinitionCsvWriter.Write(path, [new("項目", "X(4)", "①")], DefinitionTextEncoding.Ms932);
            Equal("①", DefinitionCsvReader.Read(path)[0].Comment);
            Throws<DecoderFallbackException>(() => DefinitionCsvReader.Read(path, DefinitionTextEncoding.ShiftJis));
        });
        Test("Unrepresentable definition characters cannot truncate an existing destination", () =>
        {
            var path = Path.Combine(temporaryDirectory, "definition-preserve.csv");
            File.WriteAllText(path, "original file");
            var before = File.ReadAllBytes(path);
            Throws<EncoderFallbackException>(() => DefinitionCsvWriter.Write(path, [new("項目", "X(4)", "①")], DefinitionTextEncoding.ShiftJis));
            Equal(true, before.SequenceEqual(File.ReadAllBytes(path)));
            Throws<EncoderFallbackException>(() => DefinitionCsvWriter.Write(path, [new("項目", "X(4)", "😀")], DefinitionTextEncoding.Ms932));
            Equal(true, before.SequenceEqual(File.ReadAllBytes(path)));
        });
        Test("Definition BOM handling keeps UTF8 and previous Unicode compatibility while validating malformed bytes", () =>
        {
            var path = Path.Combine(temporaryDirectory, "definition-bom.csv");
            var text = "項目名,定義,コメント\r\n区分,9(1),期待値は3\r\n";
            foreach (var encoding in new Encoding[] { new UTF8Encoding(true), new UnicodeEncoding(false, true),
                new UnicodeEncoding(true, true), new UTF32Encoding(false, true), new UTF32Encoding(true, true) })
            {
                File.WriteAllText(path, text, encoding);
                Equal("期待値は3", DefinitionCsvReader.Read(path)[0].Comment);
            }
            File.WriteAllBytes(path, [0xef, 0xbb, 0xbf, 0xff]);
            Throws<DecoderFallbackException>(() => DefinitionCsvReader.Read(path));
        });
        Test("Manual definition encoding overrides an ambiguous valid UTF8 byte sequence", () =>
        {
            var path = Path.Combine(temporaryDirectory, "definition-ambiguous.csv");
            File.WriteAllBytes(path, Encoding.ASCII.GetBytes("CODE,X(4),").Concat(new byte[] { 0xc2, 0xa5 }).ToArray());
            Equal("¥", DefinitionCsvReader.Read(path)[0].Comment);
            Equal("ﾂ･", DefinitionCsvReader.Read(path, DefinitionTextEncoding.Ms932)[0].Comment);
            Equal(DefinitionTextEncoding.ShiftJis, DefinitionFileText.Parse("shift-jis"));
            Equal(DefinitionTextEncoding.Ms932, DefinitionFileText.Parse("cp932"));
            Throws<ArgumentException>(() => DefinitionFileText.Parse("unexpected"));
        });
        Test("COBOL copybooks accept Japanese SJIS and UTF8 source without changing byte layouts", () =>
        {
            var path = Path.Combine(temporaryDirectory, "definition-encoding.cbl");
            var text = "       01 TEST-RECORD.\r\n          05 名称 PIC X(4).\r\n          05 区分 PIC 9(1).\r\n";
            foreach (var mode in new[] { DefinitionTextEncoding.Utf8, DefinitionTextEncoding.ShiftJis, DefinitionTextEncoding.Ms932 })
            {
                File.WriteAllBytes(path, DefinitionFileText.ForEncoding(mode).GetBytes(text));
                var fields = CopybookDefinitionReader.Read(path, mode);
                Equal("名称", fields[0].Name);
                Equal(5, fields.Sum(field => field.StorageByteLength));
                Equal("名称", CopybookDefinitionReader.Read(path)[0].Name);
            }
        });
        Test("Definition encoding menu and CLI remain separate from data encoding and retain dirty data on reload", () =>
        {
            var path = Path.Combine(temporaryDirectory, "definition-menu.csv");
            DefinitionCsvWriter.Write(path, [new("コード", "X(4)", "期待値"), new("区分", "9(1)")], DefinitionTextEncoding.ShiftJis);
            using var form = new MainForm(["--definition", path, "--definition-encoding", "sjis",
                "--data", Path.Combine(repo, "samples", "comment-data-utf8.csv"), "--csv-header", "present", "--csv-encoding", "utf8", "--layout", "field"]);
            Equal(DefinitionTextEncoding.ShiftJis, Get<DefinitionTextEncoding>(form, "definitionTextEncoding"));
            Equal(CsvTextEncoding.Utf8, Get<CsvTextEncoding>(form, "currentCsvEncoding"));
            var items = Get<Dictionary<DefinitionTextEncoding, ToolStripMenuItem>>(form, "definitionEncodingItems");
            Equal(4, items.Count);
            Equal(true, items[DefinitionTextEncoding.ShiftJis].Checked);
            var grid = Get<DataGridView>(form, "grid");
            grid[2, 0].Value = "9999";
            Invoke(form, "UpdateRecordValue", 0, 2);
            items[DefinitionTextEncoding.Utf8].PerformClick();
            Equal("9999", Get<List<List<string>>>(form, "records")[0][0]);
            Equal(true, Get<bool>(form, "hasUnsavedChanges"));
            items[DefinitionTextEncoding.ShiftJis].PerformClick();
            Equal(true, Invoke(form, "ReloadDefinitionCore"));
            Equal("9999", Get<List<List<string>>>(form, "records")[0][0]);
            Equal(true, Get<bool>(form, "hasUnsavedChanges"));
            Equal(DefinitionTextEncoding.ShiftJis, Invoke(form, "InitialDefinitionSaveEncoding", true));
        });
        Test("Definition editor exposes independent save encoding and auto-read suggests source encoding", () =>
        {
            var path = Path.Combine(temporaryDirectory, "definition-editor-encoding.csv");
            DefinitionCsvWriter.Write(path, [new("区分", "9(1)", "前回値")], DefinitionTextEncoding.Ms932);
            Equal(DefinitionTextEncoding.Ms932, DefinitionFileText.SuggestedSaveEncoding(path, DefinitionTextEncoding.Auto));
            using var editor = new DefinitionEditorForm(DefinitionCsvReader.Read(path), DefinitionTextEncoding.Ms932);
            Equal(DefinitionTextEncoding.Ms932, editor.SavedEncoding);
            var choice = Get<ToolStripComboBox>(editor, "saveEncoding");
            Equal(3, choice.Items.Count);
            choice.SelectedIndex = 0;
            Equal(DefinitionTextEncoding.Utf8, editor.SavedEncoding);
            choice.SelectedIndex = 1;
            Equal(DefinitionTextEncoding.ShiftJis, editor.SavedEncoding);
        });
    }
}
