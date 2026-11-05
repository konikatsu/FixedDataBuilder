using FixedDataBuilder;

internal static partial class Program
{
    private static void RunFixedTextSpaceTests(string repo)
    {
        _ = repo;
        Test("Fixed X and halfwidth text retain leading and trailing ASCII spaces in exact stored bytes", () =>
        {
            foreach (var codec in new[] { DataEncodingProfile.ShiftJis, DataEncodingProfile.StrictShiftJis, DataEncodingProfile.Utf8WithNationalText })
            foreach (var type in new[] { FieldDataType.Text, FieldDataType.HalfWidthText })
            {
                var field = new FieldDefinition("SPACE_TEXT", type, 7);
                const string value = " AB C ";
                var expected = JapaneseEncodings.ForData(codec).GetBytes(" AB C  ");
                var actual = FixedLengthDataWriter.EncodeField(field, value, codec, NationalTextEncoding.ShiftJis);
                Equal(true, expected.SequenceEqual(actual));
                Equal(0x20, (int)actual[0]);
                Equal(7, actual.Length);
                var path = Path.Combine(temporaryDirectory, $"fixed-ascii-spaces-{codec}-{type}.dat");
                FixedLengthDataWriter.Write(path, [field], [new List<string> { value }], RecordSeparatorMode.None, codec, NationalTextEncoding.ShiftJis);
                Equal(true, expected.SequenceEqual(File.ReadAllBytes(path)));
                var read = FixedLengthDataReader.Read(path, [field], RecordSeparatorMode.None, codec, NationalTextEncoding.ShiftJis);
                Equal(" AB C", read[0][0]); // Reader retains leading spaces and removes only right padding.
                Equal(true, expected.SequenceEqual(FixedLengthDataWriter.EncodeRecord([field], read[0], codec, NationalTextEncoding.ShiftJis)));
            }
        });
        Test("Fixed national text retains leading fullwidth spaces for MS932 strict SJIS UTF16 and UTF32", () =>
        {
            foreach (var national in new[] { NationalTextEncoding.ShiftJis, NationalTextEncoding.StrictShiftJis, NationalTextEncoding.Utf16, NationalTextEncoding.Utf32 })
            {
                var field = new FieldDefinition("NATIONAL", FieldDataType.FullWidthText, 5,
                    NationalByteWidth: NationalTextEncodingHelper.FixedByteWidth(national));
                const string value = "　漢　";
                var expected = NationalTextEncodingHelper.GetEncoding(national).GetBytes("　漢　　　");
                var actual = FixedLengthDataWriter.EncodeField(field, value, DataEncodingProfile.ShiftJis, national);
                Equal(true, expected.SequenceEqual(actual));
                Equal(field.PhysicalByteLength, actual.Length);
                var path = Path.Combine(temporaryDirectory, $"fixed-national-spaces-{national}.dat");
                FixedLengthDataWriter.Write(path, [field], [new List<string> { value }], RecordSeparatorMode.None,
                    DataEncodingProfile.ShiftJis, national);
                var read = FixedLengthDataReader.Read(path, [field], RecordSeparatorMode.None, DataEncodingProfile.ShiftJis, national);
                Equal("　漢", read[0][0]);
                Equal(true, expected.SequenceEqual(FixedLengthDataWriter.EncodeRecord([field], read[0], DataEncodingProfile.ShiftJis, national)));
            }
        });
        Test("Fixed whitespace-only text is stored as supplied instead of normalized to another space kind", () =>
        {
            foreach (var codec in new[] { DataEncodingProfile.ShiftJis, DataEncodingProfile.StrictShiftJis, DataEncodingProfile.Utf8WithNationalText })
            {
                var encoding = JapaneseEncodings.ForData(codec);
                var supplied = "　　";
                var expected = encoding.GetBytes(supplied).Concat(Enumerable.Repeat((byte)0x20, 6 - encoding.GetByteCount(supplied))).ToArray();
                Equal(true, expected.SequenceEqual(FixedLengthDataWriter.EncodeField(new("SPACE", FieldDataType.Text, 6), supplied, codec, NationalTextEncoding.ShiftJis)));
            }
            foreach (var national in new[] { NationalTextEncoding.Utf16, NationalTextEncoding.Utf32 })
            {
                var field = new FieldDefinition("NATIONAL_SPACES", FieldDataType.FullWidthText, 3,
                    NationalByteWidth: NationalTextEncodingHelper.FixedByteWidth(national));
                // Two supplied halfwidth spaces plus one fullwidth padding character.
                var expected = NationalTextEncodingHelper.GetEncoding(national).GetBytes(new string(' ', 2) + '\u3000');
                Equal(true, expected.SequenceEqual(FixedLengthDataWriter.EncodeField(field, "  ", DataEncodingProfile.ShiftJis, national)));
            }
        });
        Test("Fixed mixed records retain text boundaries and numeric normalization across save read and save", () =>
        {
            foreach (var codec in new[] { DataEncodingProfile.ShiftJis, DataEncodingProfile.StrictShiftJis, DataEncodingProfile.Utf8WithNationalText })
            foreach (var national in new[] { NationalTextEncoding.ShiftJis, NationalTextEncoding.Utf16, NationalTextEncoding.Utf32 })
            foreach (var separator in new[] { RecordSeparatorMode.None, RecordSeparatorMode.CrLfOrLf })
            {
                FieldDefinition[] fields = [new("X", FieldDataType.Text, 5),
                    new("N", FieldDataType.FullWidthText, 3, NationalByteWidth: NationalTextEncodingHelper.FixedByteWidth(national)),
                    new("NUMBER", FieldDataType.PlainNumber, 3), new("SIGNED", FieldDataType.SignedNumber, 3),
                    new("PACKED", FieldDataType.PackedSigned, 5)];
                var row = new List<string> { " AB ", "　漢　", " 7 ", " -12 ", " -7 " };
                var original = row.ToArray();
                var expectedRecord = JapaneseEncodings.ForData(codec).GetBytes(" AB  ")
                    .Concat(NationalTextEncodingHelper.GetEncoding(national).GetBytes("　漢　"))
                    .Concat(new byte[] { 0x30, 0x30, 0x37, 0x30, 0x31, 0x4B, 0x00, 0x00, 0x7D }).ToArray();
                Equal(true, expectedRecord.SequenceEqual(FixedLengthDataWriter.EncodeRecord(fields, row, codec, national)));
                var path = Path.Combine(temporaryDirectory, $"fixed-mixed-spaces-{codec}-{national}-{separator}.dat");
                FixedLengthDataWriter.Write(path, fields, [row, row.ToList()], separator, codec, national);
                var expectedFile = separator == RecordSeparatorMode.None ? expectedRecord.Concat(expectedRecord).ToArray()
                    : expectedRecord.Concat(new byte[] { 13, 10 }).Concat(expectedRecord).Concat(new byte[] { 13, 10 }).ToArray();
                Equal(true, expectedFile.SequenceEqual(File.ReadAllBytes(path)));
                var read = FixedLengthDataReader.Read(path, fields, separator, codec, national);
                Equal(2, read.Count);
                Equal(" AB", read[0][0]); Equal("　漢", read[0][1]);
                Equal("007", read[0][2]); Equal("-012", read[0][3]); Equal("-00007", read[0][4]);
                var second = Path.Combine(temporaryDirectory, $"fixed-mixed-spaces-resaved-{codec}-{national}-{separator}.dat");
                FixedLengthDataWriter.Write(second, fields, read, separator, codec, national);
                Equal(true, expectedFile.SequenceEqual(File.ReadAllBytes(second)));
                Equal(true, original.SequenceEqual(row));
            }
        });
        Test("Generated fixed leading spaces survive file output while unselected baseline values remain untouched", () =>
        {
            foreach (var type in new[] { FieldDataType.Text, FieldDataType.HalfWidthText, FieldDataType.FullWidthText })
            {
                var marker = type == FieldDataType.FullWidthText ? "漢" : "X";
                var space = type == FieldDataType.FullWidthText ? '\u3000' : ' ';
                FieldDefinition[] fields = [new("TARGET", type, 5), new("KEEP", FieldDataType.Text, 4), new("NUMBER", FieldDataType.PlainNumber, 3)];
                var baseline = new[] { marker, " OK ", "007" };
                var before = baseline.ToArray();
                var options = new DataSaveOptions(DataFileFormat.FixedLength, CsvTextEncoding.Utf8,
                    RecordSeparatorMode.None, NationalTextEncoding.ShiftJis, DataEncodingProfile.ShiftJis);
                var settings = new TestDataGenerationSettings(0, [0], GenerationRequirement.TextLeadingSpace
                    | GenerationRequirement.TextInternalSpace | GenerationRequirement.TextTrailingSpace, 1, options);
                var result = TestDataGenerator.Generate(fields, baseline, settings);
                Equal(1, result.Records.Count);
                Equal(space, result.Records[0][0][0]); Equal(space, result.Records[0][0][^1]);
                Equal(" OK ", result.Records[0][1]); Equal("007", result.Records[0][2]);
                var path = Path.Combine(temporaryDirectory, $"generated-leading-space-{type}.dat");
                FixedLengthDataWriter.Write(path, fields, result.Records, options.SeparatorMode, options.TextEncoding!.Value, options.NationalEncoding);
                var bytes = File.ReadAllBytes(path);
                var encoding = type == FieldDataType.FullWidthText ? NationalTextEncodingHelper.GetEncoding(options.NationalEncoding)
                    : JapaneseEncodings.ForData(options.TextEncoding.Value);
                Equal(result.Records[0][0], encoding.GetString(bytes.AsSpan(0, fields[0].PhysicalByteLength)));
                Equal(true, before.SequenceEqual(baseline));
            }
        });
        Test("Validation counts supplied text edge spaces for length but retains numeric trimming", () =>
        {
            FieldDefinition[] fields = [new("X", FieldDataType.Text, 2), new("HALF", FieldDataType.HalfWidthText, 2),
                new("N", FieldDataType.FullWidthText, 2), new("NUMBER", FieldDataType.PlainNumber, 3),
                new("SIGNED", FieldDataType.SignedNumber, 3), new("PACKED", FieldDataType.PackedSigned, 5)];
            var invalid = new List<string> { " A ", " B ", "　漢　", " 7 ", " -12 ", " -7 " };
            var errors = RecordValidator.Validate(fields, [invalid]);
            Equal(3, errors.Count);
            foreach (var name in new[] { "X", "HALF", "N" }) Equal(true, errors.Any(error => error.Contains($" / {name}:")));
            foreach (var field in fields.Take(3))
                Throws<InvalidDataException>(() => FixedLengthDataWriter.EncodeField(field, invalid[Array.IndexOf(fields, field)]));
            var valid = new List<string> { " A", "B ", "　漢", " 7 ", " -12 ", " -7 " };
            Equal(0, RecordValidator.Validate(fields, [valid]).Count);
            using var form = new MainForm();
            foreach (var (field, index) in fields.Select((field, index) => (field, index)))
            {
                var error = (string?)Invoke(form, "GetValidationError", field, invalid[index]);
                Equal(index < 3, error is not null);
            }
        });
        Test("Main fixed grid HEX display and save agree on the retained first space byte", () =>
        {
            using var form = new MainForm();
            var fields = Get<List<FieldDefinition>>(form, "fields");
            fields.AddRange([new("CODE", FieldDataType.Text, 5), new("NAME", FieldDataType.FullWidthText, 3)]);
            var rows = Get<List<List<string>>>(form, "records");
            rows.Add([" AB ", "　漢　"]);
            Invoke(form, "RefreshGrid");
            Equal("･AB･･", Invoke(form, "GetVisibleTextValue", fields[0], rows[0][0]));
            Equal("□漢□", Invoke(form, "GetVisibleTextValue", fields[1], rows[0][1]));
            Equal("20 41 42 20 20", Invoke(form, "FieldHexText", 0, 0));
            Equal("81 40 8A BF 81 40", Invoke(form, "FieldHexText", 0, 1));
            Equal(3, SelectionSpaceSummary.Count(rows[0][0], fields[0], false, JapaneseEncodings.ForData(DataEncodingProfile.ShiftJis)).HalfWidth);
            var before = rows[0].ToArray();
            var path = Path.Combine(temporaryDirectory, "main-fixed-leading-space.dat");
            Invoke(form, "WriteData", path, new DataSaveOptions(DataFileFormat.FixedLength, CsvTextEncoding.Utf8,
                RecordSeparatorMode.None, NationalTextEncoding.ShiftJis, DataEncodingProfile.ShiftJis));
            Equal(true, new byte[] { 0x20, 0x41, 0x42, 0x20, 0x20, 0x81, 0x40, 0x8A, 0xBF, 0x81, 0x40 }.SequenceEqual(File.ReadAllBytes(path)));
            Equal(true, before.SequenceEqual(rows[0]));
            Equal(false, Get<bool>(form, "hasUnsavedChanges"));
        });
    }
}
