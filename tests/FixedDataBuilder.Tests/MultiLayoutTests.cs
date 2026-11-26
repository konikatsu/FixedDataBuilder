using System.Text;
using FixedDataBuilder;

internal static partial class Program
{
    private static void RunMultiLayoutTests(string repo)
    {
        var definitionPath = Path.Combine(temporaryDirectory, "multi-definition.csv");
        File.WriteAllText(definitionPath, "レコード区分,項目名,定義,備考\r\n1,区分,9(2),\"番号,備考\"\r\n1,種別,X(1),\r\n1,レコード区分,X(1),\r\n1,ヘッダー名,X(4),ヘッダー備考\r\n2,区分,9(2),\r\n2,種別,X(1),\r\n2,レコード区分,X(1),\r\n2,明細名,X(4),\"明細\r\n備考\"\r\n", new UTF8Encoding(false));
        var definition = MultiRecordDefinition.Read(definitionPath);
        Test("Multi definition retains independent field names, quoted notes and physical lengths", () =>
        {
            Equal(4, definition.Header.Count); Equal(4, definition.Detail.Count);
            Equal("番号,備考", definition.Header[0].Comment);
            Equal("明細\r\n備考", definition.Detail[3].Comment);
            Equal(8, definition.Header.Sum(x => x.StorageByteLength)); Equal(8, definition.Detail.Sum(x => x.StorageByteLength));
        });
        Test("Multiple header groups preserve record order, literal spaces, notes and source bytes", () =>
        {
            var path = Path.Combine(temporaryDirectory, "multi-groups.dat");
            var original = Encoding.ASCII.GetBytes("01A1H  1\r\n01A2D  1\r\n01A1H  2\r\n01A2D  2\r\n01A2D  3\r\n");
            File.WriteAllBytes(path, original);
            var data = MultiRecordReader.Read(path, definition, 4, RecordSeparatorMode.CrLfOrLf, DataEncodingProfile.ShiftJis, NationalTextEncoding.ShiftJis);
            Equal("12122", string.Concat(data.Records.Select(x => x.Kind)));
            Equal("H  1", data.Records[0].Values[3]); Equal("D  3", data.Records[4].Values[3]);
            Equal(true, original.SequenceEqual(File.ReadAllBytes(path))); Equal(null, data.IncompleteReason);
            using var form = new MultiRecordViewForm();
            Invoke(form, "Populate", definition, data);
            var grid = Get<DataGridView>(form, "grid");
            Equal(5, grid.ColumnCount);
            Equal("ヘッダー 見出し", grid[0, 0].Value); Equal("ヘッダー名", grid[4, 0].Value);
            Equal("備考", grid[0, 2].Value); Equal("ヘッダー備考", grid[4, 2].Value);
            Equal("明細 見出し", grid[0, 4].Value); Equal("明細名", grid[4, 4].Value);
            Equal("明細\r\n備考", grid[4, 6].Value);
        });
        Test("Unknown equal-length record is highlighted and the following record is retained", () =>
        {
            var path = Path.Combine(temporaryDirectory, "multi-unknown.dat");
            File.WriteAllText(path, "01A1HEAD\r\n01A9FAIL\r\n01A2NEXT\r\n");
            var result = MultiRecordReader.Read(path, definition, 4, RecordSeparatorMode.CrLfOrLf, DataEncodingProfile.ShiftJis, NationalTextEncoding.ShiftJis);
            Equal("1?2", string.Concat(result.Records.Select(x => x.Kind)));
            Equal("303141394641494C", result.Records[1].Values[0]);
            Equal(true, result.Records[1].Warning!.Contains("39")); Equal("NEXT", result.Records[2].Values[3]);
        });
        Test("Invalid numeric fields stay visible with a warning and later records remain available", () =>
        {
            var path = Path.Combine(temporaryDirectory, "multi-invalid-number.dat");
            File.WriteAllText(path, "01A1HEAD\r\nXXA2FAIL\r\n01A2NEXT\r\n");
            var result = MultiRecordReader.Read(path, definition, 4, RecordSeparatorMode.CrLfOrLf, DataEncodingProfile.ShiftJis, NationalTextEncoding.ShiftJis);
            Equal("122", string.Concat(result.Records.Select(x => x.Kind)));
            Equal("XX", result.Records[1].Values[0]);
            Equal(true, result.Records[1].FieldWarnings![0].Contains("5858"));
            Equal("FAIL", result.Records[1].Values[3]); Equal("NEXT", result.Records[2].Values[3]);
            using var form = new MultiRecordViewForm();
            Invoke(form, "Populate", definition, result);
            var grid = Get<DataGridView>(form, "grid");
            Equal("XX", grid[1, 7].Value);
        });
        Test("Invalid packed field shows its raw bytes and later records remain available", () =>
        {
            var packed = new MultiRecordDefinition([new("prefix", FieldDataType.Text, 4), new("packed", FieldDataType.PackedSigned, 1)],
                [new("prefix", FieldDataType.Text, 4), new("packed", FieldDataType.PackedSigned, 1)]);
            var path = Path.Combine(temporaryDirectory, "multi-invalid-packed.dat");
            File.WriteAllBytes(path, [0x30,0x31,0x41,0x31,0x0a,0x0d,0x0a,0x30,0x31,0x41,0x32,0x1c,0x0d,0x0a]);
            var result = MultiRecordReader.Read(path, packed, 4, RecordSeparatorMode.CrLfOrLf, DataEncodingProfile.ShiftJis, NationalTextEncoding.ShiftJis);
            Equal("[HEX:0A]", result.Records[0].Values[1]);
            Equal(true, result.Records[0].FieldWarnings!.ContainsKey(1)); Equal(2, result.Records.Count);
        });
        Test("Blank numeric display fields remain blank instead of being changed to zero", () =>
        {
            var path = Path.Combine(temporaryDirectory, "multi-blank-number.dat");
            File.WriteAllText(path, "  A1HEAD\r\n");
            var result = MultiRecordReader.Read(path, definition, 4, RecordSeparatorMode.CrLfOrLf, DataEncodingProfile.ShiftJis, NationalTextEncoding.ShiftJis);
            Equal("  ", result.Records[0].Values[0]);
        });
        Test("Unknown variable-length record stops safely and retains a visible warning", () =>
        {
            var different = definition with { Detail = definition.Detail.Append(new FieldDefinition("追加", FieldDataType.Text, 1)).ToArray() };
            var path = Path.Combine(temporaryDirectory, "multi-unknown-length.dat");
            File.WriteAllText(path, "01A1HEAD01A9FAIL01A2NEXT!");
            var result = MultiRecordReader.Read(path, different, 4, RecordSeparatorMode.None, DataEncodingProfile.ShiftJis, NationalTextEncoding.ShiftJis);
            Equal("1?", string.Concat(result.Records.Select(x => x.Kind)));
            Equal(true, result.IncompleteReason!.Contains("中止"));
        });
        Test("Malformed record boundaries fail with a record number", () =>
        {
            var path = Path.Combine(temporaryDirectory, "multi-short.dat"); File.WriteAllText(path, "01A1HEADxx");
            Throws<InvalidDataException>(() => MultiRecordReader.Read(path, definition, 4, RecordSeparatorMode.CrLfOrLf, DataEncodingProfile.ShiftJis, NationalTextEncoding.ShiftJis));
            Throws<InvalidDataException>(() => MultiRecordReader.Read(path, definition, 99, RecordSeparatorMode.None, DataEncodingProfile.ShiftJis, NationalTextEncoding.ShiftJis));
        });
        Test("Each packed record is consumed by length even if binary bytes contain CR", () =>
        {
            var packed = new MultiRecordDefinition([new("prefix", FieldDataType.Text, 4), new("packed", FieldDataType.PackedSigned, 1)],
                [new("prefix", FieldDataType.Text, 4), new("packed", FieldDataType.PackedSigned, 1)]);
            var path = Path.Combine(temporaryDirectory, "multi-packed.dat");
            File.WriteAllBytes(path, [0x30, 0x31, 0x41, 0x31, 0x0d, 0x0d, 0x0a]);
            var result = MultiRecordReader.Read(path, packed, 4, RecordSeparatorMode.CrLfOrLf, DataEncodingProfile.ShiftJis, NationalTextEncoding.ShiftJis);
            Equal(1, result.Records.Count); Equal('1', result.Records[0].Kind);
        });
        var realDefinitionPath = Environment.GetEnvironmentVariable("FDB_MULTI_LAYOUT_TEST_DEFINITION");
        var realDataPath = Environment.GetEnvironmentVariable("FDB_MULTI_LAYOUT_TEST_DATA");
        if (!string.IsNullOrWhiteSpace(realDefinitionPath) && !string.IsNullOrWhiteSpace(realDataPath))
        {
            Test("Provided header-detail data stays visible through invalid numeric values", () =>
            {
                var before = System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(realDataPath));
                var realDefinition = MultiRecordDefinition.Read(realDefinitionPath).WithNationalEncoding(NationalTextEncoding.ShiftJis);
                var result = MultiRecordReader.Read(realDataPath, realDefinition, 4, RecordSeparatorMode.CrLfOrLf, DataEncodingProfile.ShiftJis, NationalTextEncoding.ShiftJis);
                Equal(151, result.Records.Count); Equal(null, result.IncompleteReason);
                Equal('1', result.Records[143].Kind);
                Equal(true, result.Records[143].FieldWarnings!.Values.Any(x => x.Contains("銀行コード")));
                Equal(true, before.SequenceEqual(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(realDataPath))));
            });
        }
        _ = repo;
    }
}
