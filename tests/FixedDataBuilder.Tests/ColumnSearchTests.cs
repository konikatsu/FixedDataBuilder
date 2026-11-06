using System.Text;
using FixedDataBuilder;

internal static partial class Program
{
    private static void RunColumnSearchTests()
    {
        Test("Column search finds Japanese spaces and repeated overlapping occurrences", () =>
        {
            string[] lines = ["", " 氏名漢字 aaa ", "末尾"];
            var first = ColumnTextSearch.Find(lines, "aa", true, null, true)!;
            Equal(1, first.LineIndex); Equal(6, first.CharacterIndex); Equal(false, first.Wrapped);
            var second = ColumnTextSearch.Find(lines, "aa", true, first, true)!;
            Equal(7, second.CharacterIndex);
            Equal(6, ColumnTextSearch.Find(lines, "aa", true, second, false)!.CharacterIndex);
            Equal(0, ColumnTextSearch.Find(lines, " 氏名漢字 ", true, null, true)!.CharacterIndex);
        });
        Test("Column search wraps forward and backward including a single matching line", () =>
        {
            string[] lines = ["match", "nothing", "match"];
            var first = ColumnTextSearch.Find(lines, "match", true, null, true)!;
            var last = ColumnTextSearch.Find(lines, "match", true, first, true)!;
            Equal(2, last.LineIndex); Equal(false, last.Wrapped);
            var wrapped = ColumnTextSearch.Find(lines, "match", true, last, true)!;
            Equal(0, wrapped.LineIndex); Equal(true, wrapped.Wrapped);
            var backward = ColumnTextSearch.Find(lines, "match", true, first, false)!;
            Equal(2, backward.LineIndex); Equal(true, backward.Wrapped);
            var only = ColumnTextSearch.Find(["x"], "x", true, null, true)!;
            Equal(true, ColumnTextSearch.Find(["x"], "x", true, only, true)!.Wrapped);
        });
        Test("Column search handles case rules empty files empty queries missing text and page anchors", () =>
        {
            string[] lines = ["ABC", "", "abc", "end"];
            Equal(0, ColumnTextSearch.Find(lines, "abc", false, null, true)!.LineIndex);
            Equal(2, ColumnTextSearch.Find(lines, "abc", true, null, true)!.LineIndex);
            Equal(2, ColumnTextSearch.Find(lines, "abc", false, null, true, 1)!.LineIndex);
            Equal(true, ColumnTextSearch.Find(lines, "ABC", true, null, true, 3)!.Wrapped);
            Equal(true, ColumnTextSearch.Find(lines, "absent", false, null, true) is null);
            Equal(true, ColumnTextSearch.Find(lines, "", false, null, true) is null);
            Equal(true, ColumnTextSearch.Find([], "x", false, null, false) is null);
            Equal(true, ColumnTextSearch.Find(["a"], "long", false, null, false) is null);
        });
        Test("Column search keeps UTF16 character positions and preserves source strings", () =>
        {
            string[] lines = ["😀漢字\t 001 ", "\"quoted\",000"];
            var original = lines.ToArray();
            Equal(2, ColumnTextSearch.Find(lines, "漢字", true, null, true)!.CharacterIndex);
            Equal(5, ColumnTextSearch.Find(lines, " 001 ", true, null, false)!.CharacterIndex);
            Equal(true, lines.SequenceEqual(original));
        });
        string FileWithLines(string name, IEnumerable<string> lines)
        {
            var path = Path.Combine(temporaryDirectory, name);
            File.WriteAllText(path, string.Join("\n", lines), new UTF8Encoding(false)); return path;
        }
        Test("Column form searches the whole file and selects the correct page pane and row", () =>
        {
            using var form = new ColumnTextViewForm();
            var path = FileWithLines("column-search-pages.txt", Enumerable.Range(0, 5000).Select(i => i == 4567 ? "target" : $"row{i}"));
            var bytes = File.ReadAllBytes(path); form.LoadFile(path);
            Get<TextBox>(form, "searchBox").Text = "target"; Invoke(form, "Search", true);
            var layout = Get<ColumnPageLayout>(form, "pagination");
            Equal(4567 / layout.Capacity, Get<int>(form, "pageIndex"));
            var panes = Get<List<WheelZoomDataGridView>>(form, "panes");
            var pane = panes[(4567 % layout.Capacity) / layout.RowsPerColumn];
            Equal("target", pane.CurrentCell!.Value); Equal(true, pane.CurrentCell.Selected);
            Equal(true, Get<Label>(form, "searchResult").Text.StartsWith("4568行目"));
            Equal(true, bytes.SequenceEqual(File.ReadAllBytes(path)));
        });
        Test("Column form reports missing searches without moving and resets when query or case changes", () =>
        {
            using var form = new ColumnTextViewForm();
            Equal(false, Get<Button>(form, "findNext").Enabled);
            form.LoadFile(FileWithLines("column-search-missing.txt", Enumerable.Range(0, 2000).Select(i => $"row{i}")));
            Invoke(form, "MovePage", 1); var page = Get<int>(form, "pageIndex");
            Get<TextBox>(form, "searchBox").Text = "missing"; Invoke(form, "Search", true);
            Equal(page, Get<int>(form, "pageIndex")); Equal("見つかりません", Get<Label>(form, "searchResult").Text);
            Get<TextBox>(form, "searchBox").Text = "row1999"; Invoke(form, "Search", true);
            Get<CheckBox>(form, "matchCase").Checked = true;
            Equal(true, Get<ColumnTextMatch?>(form, "searchMatch") is null);
            Get<TextBox>(form, "searchBox").Text = "";
            Equal(false, Get<Button>(form, "findNext").Enabled);
        });
        Test("Column form reflow preserves the located line and successful file loads clear search position", () =>
        {
            using var form = new ColumnTextViewForm();
            var path = FileWithLines("column-search-reflow.txt", Enumerable.Range(0, 2000).Select(i => $"row{i}"));
            form.LoadFile(path); Get<TextBox>(form, "searchBox").Text = "row1999"; Invoke(form, "Search", true);
            form.ClientSize = new Size(800, 450); form.PerformLayout(); Invoke(form, "Reflow");
            var layout = Get<ColumnPageLayout>(form, "pagination");
            Equal(1999 / layout.Capacity, Get<int>(form, "pageIndex"));
            form.LoadFile(path); Equal(true, Get<ColumnTextMatch?>(form, "searchMatch") is null);
            Equal(0, Get<int>(form, "pageIndex"));
        });
        Test("Column form scrolls long matched lines horizontally and retains search after failed loads", () =>
        {
            using var form = new ColumnTextViewForm();
            var path = FileWithLines("column-search-long.txt", [new string('a', 2000) + "needle"]);
            form.LoadFile(path); Get<TextBox>(form, "searchBox").Text = "needle"; Invoke(form, "Search", true);
            Equal(true, Get<List<WheelZoomDataGridView>>(form, "panes")[0].HorizontalScrollingOffset > 0);
            var match = Get<ColumnTextMatch>(form, "searchMatch");
            Throws<FileNotFoundException>(() => form.LoadFile(Path.Combine(temporaryDirectory, "missing-search-file.txt")));
            Equal(match, Get<ColumnTextMatch>(form, "searchMatch"));
        });
    }
}
