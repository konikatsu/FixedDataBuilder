using System.IO.Compression;
using System.Xml.Linq;
using System.Globalization;
using FixedDataBuilder;

internal static partial class Program
{
    private static void RunExcelHorizontalExportTests(string repo)
    {
        MainForm Open(string name)
        {
            var definition = Path.Combine(temporaryDirectory, name + "-definition.csv");
            var data = Path.Combine(temporaryDirectory, name + "-data.csv");
            DefinitionCsvWriter.Write(definition, [new("コード", "X(4)", " 期待値は\"0001\",前回値を保持\r\n空白も確認 "), new("区分", "9(1)", "期待値は3")]);
            File.WriteAllText(data, "0001,3\r\n0002,3\r\n");
            return new MainForm(["--definition", definition, "--data", data, "--layout", "record"]);
        }
        IReadOnlyList<IReadOnlyList<ExcelCell>> Rows(MainForm form) => (IReadOnlyList<IReadOnlyList<ExcelCell>>)Invoke(form, "BuildExcelRows")!;
        Test("Horizontal Excel has separate number and name cells and the same metadata values and colors as the screen", () =>
        {
            using var form = Open("excel-screen");
            var grid = Get<DataGridView>(form, "grid");
            var rows = Rows(form);
            Equal("カラム番号", rows[0][0].Text);
            Equal("No.1", rows[0][1].Text);
            Equal("コード", rows[1][1].Text);
            Equal("X(4)", rows[2][1].Text);
            Equal("コメント", rows[3][0].Text);
            Equal("0001", rows[4][1].Text);
            Equal("FFCCE8CC", rows[0][1].BackgroundColor);
            Equal("FFCCE8CC", rows[1][1].BackgroundColor);
            Equal("FFE1F2FC", rows[3][1].BackgroundColor);
            Equal("FFE2F2E2", rows[4][0].BackgroundColor);
            Equal(ExcelCellKind.Comment, rows[3][1].Kind);
            for (var row = 0; row < grid.Rows.Count; row++)
            for (var column = 0; column < grid.Columns.Count; column++)
                Equal(grid[column, row].Value?.ToString() ?? "", rows[row + 1][column].Text);
            Equal(false, rows.SelectMany(row => row).Any(cell => cell.Text.StartsWith("CSV列")));
        });
        Test("Excel XML uses solid screen colors, separate cells and wrapped comments with explicit row height", () =>
        {
            using var form = Open("excel-styles");
            var fields = Get<List<FieldDefinition>>(form, "fields");
            var comment = "=SUM(A1)\r\n" + string.Concat(Enumerable.Repeat("期待値を確認します。", 40));
            fields[0] = fields[0] with { Comment = comment };
            Invoke(form, "RefreshGrid");
            var path = Path.Combine(temporaryDirectory, "screen-colors.xlsx");
            ExcelExporter.Write(path, "レコード縦", Rows(form));
            using var archive = ZipFile.OpenRead(path);
            var sheet = XDocument.Load(archive.GetEntry("xl/worksheets/sheet1.xml")!.Open());
            var styles = XDocument.Load(archive.GetEntry("xl/styles.xml")!.Open());
            XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var fills = styles.Root!.Element(ns + "fills")!.Elements(ns + "fill").ToArray();
            var formats = styles.Root.Element(ns + "cellXfs")!.Elements(ns + "xf").ToArray();
            string ColorAt(string reference)
            {
                var cell = sheet.Descendants(ns + "c").Single(cell => (string?)cell.Attribute("r") == reference);
                var format = formats[(int)cell.Attribute("s")!];
                Equal("1", (string?)format.Element(ns + "alignment")!.Attribute("wrapText"));
                var fill = fills[(int)format.Attribute("fillId")!].Element(ns + "patternFill")!;
                Equal("solid", (string?)fill.Attribute("patternType"));
                return (string)fill.Element(ns + "fgColor")!.Attribute("rgb")!;
            }
            Equal("FFCCE8CC", ColorAt("B1"));
            Equal("FFCCE8CC", ColorAt("B2"));
            Equal("FFE1F2FC", ColorAt("B4"));
            Equal("FFE2F2E2", ColorAt("A5"));
            var commentRow = sheet.Descendants(ns + "row").Single(row => (string?)row.Attribute("r") == "4");
            Equal("1", (string?)commentRow.Attribute("customHeight"));
            var height = double.Parse((string)commentRow.Attribute("ht")!, CultureInfo.InvariantCulture);
            Equal(true, height > 21 && height <= 409.5);
            Equal(false, sheet.Descendants(ns + "mergeCell").Any());
            Equal(false, sheet.Descendants(ns + "f").Any());
            Equal(comment.Replace("\r\n", "\n"), sheet.Descendants(ns + "c").Single(c => (string?)c.Attribute("r") == "B4").Value.Replace("\r\n", "\n"));
            Equal(true, (double)sheet.Descendants(ns + "col").Single(c => (int)c.Attribute("min")! == 2).Attribute("width")! < 32);
        });
        Test("Edited Excel comments survive import, definition CSV save and reload without becoming data records", () =>
        {
            using var form = Open("excel-import-comments");
            var rows = Rows(form).Select(row => row.ToArray()).ToArray();
            var comment = " 新しい期待値, \"前回値\"\n空白を保持 ";
            rows[3][1] = rows[3][1] with { Text = comment };
            var path = Path.Combine(temporaryDirectory, "edited-comments.xlsx");
            ExcelExporter.Write(path, "レコード縦", rows);
            var imported = ExcelImporter.Read(path);
            Equal(2, imported.Fields.Count);
            Equal(2, imported.Records.Count);
            Equal("コード", imported.Fields[0].Name);
            Equal(comment, imported.Fields[0].Comment);
            Equal("0001", imported.Records[0][0]);
            Equal("0002", imported.Records[1][0]);
            using var target = new MainForm();
            Invoke(target, "LoadExcel", path);
            Equal(comment, Get<List<FieldDefinition>>(target, "fields")[0].Comment);
            Invoke(target, "ReloadCurrentFilesCore");
            Equal(2, Get<List<List<string>>>(target, "records").Count);
            var definition = Path.Combine(temporaryDirectory, "imported-comment-definition.csv");
            DefinitionCsvWriter.Write(definition, imported.Fields.Select(field => new DefinitionCsvRow(field.Name, field.DisplayDefinition, field.Comment)).ToArray());
            Equal(comment, DefinitionCsvReader.Read(definition)[0].Comment);
        });
        Test("Fixed and OCCURS Excel imports skip all metadata rows and preserve comments when hidden ordinals have gaps", () =>
        {
            using var form = new MainForm(["--definition", Path.Combine(repo, "samples", "copybook-group-occurs-definition.cbl"),
                "--data", Path.Combine(repo, "samples", "copybook-group-occurs-data-crlf-utf8-n-utf16le.dat"),
                "--separator", "crlf", "--national-encoding", "utf16", "--layout", "record"]);
            var fields = Get<List<FieldDefinition>>(form, "fields");
            fields[1] = fields[1] with { Comment = "繰返し項目の期待値" };
            Get<HashSet<int>>(form, "hiddenFieldIndexes").Add(0);
            Invoke(form, "RefreshGrid");
            var rows = Rows(form);
            Equal("No.2", rows[0][1].Text);
            Equal("位置", rows[3][0].Text);
            Equal("コメント", rows[4][0].Text);
            Equal("OCCURS", rows[5][0].Text);
            var path = Path.Combine(temporaryDirectory, "occurs-comments.xlsx");
            ExcelExporter.Write(path, "レコード縦", rows);
            var imported = ExcelImporter.Read(path);
            Equal("繰返し項目の期待値", imported.Fields[0].Comment);
            Equal(Get<List<List<string>>>(form, "records").Count, imported.Records.Count);
            Equal(rows[6][1].Text, imported.Records[0][0]);
        });
        Test("Invalid Excel background colors are rejected before an existing destination is replaced", () =>
        {
            var path = Path.Combine(temporaryDirectory, "invalid-color.xlsx");
            File.WriteAllText(path, "original destination");
            var before = File.ReadAllBytes(path);
            Throws<ArgumentException>(() => ExcelExporter.Write(path, "レコード縦", [[new("No.1", ExcelCellKind.Header, "green")]]));
            Equal(true, before.SequenceEqual(File.ReadAllBytes(path)));
        });
    }
}
