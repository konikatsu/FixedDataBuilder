using System.Text;
using FixedDataBuilder;

internal static partial class Program
{
    private static void RunFixedPaddingWrapTests()
    {
        Test("UTF32 wraps ordinary characters and emoji at four bytes per code point", () =>
        {
            Equal(16, Encoding.UTF32.GetByteCount("漢A　😀"));
            Equal("漢A|　😀", string.Join("|", ByteTextWrapper.Lines("漢A　😀", Encoding.UTF32, 8)));
        });
        Test("UTF32 N205 wraps long short and empty values including padding into nine equal-budget lines", () =>
        {
            foreach (var recordRows in new[] { false, true })
            {
                using var form = CreateNationalPaddingWrapForm(recordRows);
                Invoke(form, "ApplyDisplayWrapOptions", new DisplayWrapOptions(true, 100, 100));
                var grid = Get<DataGridView>(form, "grid");
                var start = recordRows ? (int)Invoke(form, "RecordRowsDataStartIndex")! : 1;
                for (var record = 0; record < 3; record++)
                {
                    var row = recordRows ? start + record : 1;
                    var column = recordRows ? 2 : 2 + record;
                    var lines = (IReadOnlyList<string>)Invoke(form, "WrappedDisplayLines", row, column)!;
                    Equal(9, lines.Count);
                    Equal(true, lines.Take(8).All(line => line.Length == 25));
                    Equal(5, lines[^1].Length);
                    var raw = Get<List<List<string>>>(form, "records")[record][1];
                    Equal(raw + new string('　', 205 - raw.Length), string.Concat(lines).Replace('□', '　'));
                    Equal(true, grid.Rows[row].Height >= 9 * (grid.Font.Height + 2) + 6);
                }
                if (recordRows) Equal(grid.Rows[start].Height, grid.Rows[start + 2].Height);
            }
        });
        Test("Fixed padding threshold is inclusive and uses the selected national byte width", () =>
        {
            using var form = CreateNationalPaddingWrapForm(true);
            var row = (int)Invoke(form, "RecordRowsDataStartIndex")! + 2;
            Invoke(form, "ApplyDisplayWrapOptions", new DisplayWrapOptions(true, 821, 100));
            Equal(null, Invoke(form, "WrappedDisplayLines", row, 2));
            Invoke(form, "ApplyDisplayWrapOptions", new DisplayWrapOptions(true, 820, 100));
            Equal(9, ((IReadOnlyList<string>)Invoke(form, "WrappedDisplayLines", row, 2)!).Count);
        });
        Test("X padding uses encoded byte length for UTF8 MS932 and strict SJIS", () =>
        {
            foreach (var profile in new[] { DataEncodingProfile.Utf8WithNationalText, DataEncodingProfile.ShiftJis, DataEncodingProfile.StrictShiftJis })
            {
                using var form = new MainForm();
                Get<List<FieldDefinition>>(form, "fields").Add(new("文字", FieldDataType.Text, 8));
                Get<List<List<string>>>(form, "records").Add(new() { "漢" });
                typeof(MainForm).GetField("currentDataEncodingProfile", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(form, profile);
                Invoke(form, "RefreshGrid");
                Invoke(form, "ApplyDisplayWrapOptions", new DisplayWrapOptions(true, 8, 4));
                var lines = (IReadOnlyList<string>)Invoke(form, "WrappedDisplayLines", 0, 2)!;
                var encoding = JapaneseEncodings.ForData(profile);
                var raw = string.Concat(lines).Replace('･', ' ');
                Equal("漢" + new string(' ', 8 - encoding.GetByteCount("漢")), raw);
                Equal(2, lines.Count);
                Equal(true, lines.All(line => encoding.GetByteCount(line.Replace('･', ' ')) <= 4));
            }
        });
        Test("Padding wrapping preserves saved mixed-encoding bytes Excel HEX and raw values and resets on disable", () =>
        {
            foreach (var recordRows in new[] { false, true })
            {
                using var form = CreateNationalPaddingWrapForm(recordRows);
                var grid = Get<DataGridView>(form, "grid");
                var row = recordRows ? (int)Invoke(form, "RecordRowsDataStartIndex")! + 1 : 1;
                var column = recordRows ? 2 : 3;
                grid.CurrentCell = grid[column, row];
                Invoke(form, "UpdateHexView");
                var hex = Get<TextBox>(form, "hexTextBox").Text;
                var excel = (IReadOnlyList<IReadOnlyList<ExcelCell>>)Invoke(form, "BuildExcelRows")!;
                var values = Get<List<List<string>>>(form, "records").SelectMany(record => record).ToArray();
                var height = grid.Rows[row].Height;
                var width = grid.Columns[column].Width;
                var before = Path.Combine(temporaryDirectory, $"padding-before-{recordRows}.dat");
                var after = Path.Combine(temporaryDirectory, $"padding-after-{recordRows}.dat");
                Invoke(form, "WriteData", before, Invoke(form, "CurrentSaveOptions"));
                Invoke(form, "ApplyDisplayWrapOptions", new DisplayWrapOptions(true, 100, 100));
                if (recordRows) Equal(true, grid.Columns[column].Width < width);
                Equal(hex, Get<TextBox>(form, "hexTextBox").Text);
                var actual = (IReadOnlyList<IReadOnlyList<ExcelCell>>)Invoke(form, "BuildExcelRows")!;
                Equal(true, excel.SelectMany(r => r).SequenceEqual(actual.SelectMany(r => r)));
                Invoke(form, "WriteData", after, Invoke(form, "CurrentSaveOptions"));
                Equal(true, File.ReadAllBytes(before).SequenceEqual(File.ReadAllBytes(after)));
                Equal(true, values.SequenceEqual(Get<List<List<string>>>(form, "records").SelectMany(record => record)));
                Equal(false, Get<bool>(form, "hasUnsavedChanges"));
                Invoke(form, "ApplyDisplayWrapOptions", new DisplayWrapOptions(false, 100, 100));
                Equal(null, Invoke(form, "WrappedDisplayLines", row, column));
                Equal(height, grid.Rows[row].Height);
            }
        });
        Test("Display limit omits padding only when the raw value is truncated", () =>
        {
            using var form = CreateNationalPaddingWrapForm(true);
            var start = (int)Invoke(form, "RecordRowsDataStartIndex")!;
            Invoke(form, "ApplyDisplayWrapOptions", new DisplayWrapOptions(true, 100, 100, true, 4));
            Equal("漢字氏名…", string.Join("|", (IReadOnlyList<string>)Invoke(form, "WrappedDisplayLines", start, 2)!));
            Equal(9, ((IReadOnlyList<string>)Invoke(form, "WrappedDisplayLines", start + 1, 2)!).Count);
            Equal(9, ((IReadOnlyList<string>)Invoke(form, "WrappedDisplayLines", start + 2, 2)!).Count);
        });
    }

    private static MainForm CreateNationalPaddingWrapForm(bool recordRows)
    {
        var definition = Path.Combine(temporaryDirectory, $"padding-utf32-{recordRows}.csv");
        var data = Path.Combine(temporaryDirectory, $"padding-utf32-{recordRows}.dat");
        DefinitionCsvWriter.Write(definition, [new("コード", "X(4)"), new("氏名漢字", "N(205)")]);
        var fields = DefinitionCsvReader.Read(definition).Select(field => field.Type == FieldDataType.FullWidthText
            ? field with { NationalByteWidth = 4 } : field).ToList();
        FixedLengthDataWriter.Write(data, fields, [["A", "漢字氏名" + new string('１', 36)], ["B", "短い氏名"], ["C", ""]],
            RecordSeparatorMode.None, DataEncodingProfile.Utf8WithNationalText, NationalTextEncoding.Utf32);
        return new MainForm(["--definition", definition, "--data", data, "--separator", "none",
            "--text-encoding", "utf8", "--national-encoding", "utf32", "--layout", recordRows ? "record" : "field"]);
    }
}
