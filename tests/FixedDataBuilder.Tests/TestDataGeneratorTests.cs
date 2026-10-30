using System.Text;
using FixedDataBuilder;

internal static partial class Program
{
    private static void RunTestDataGeneratorTests(string repo)
    {
        _ = repo; // These tests deliberately use only synthetic definitions and values.
        var csv = new DataSaveOptions(DataFileFormat.Csv, CsvTextEncoding.Utf8, RecordSeparatorMode.CrLfOrLf, NationalTextEncoding.ShiftJis);
        var fixedOptions = csv with { Format = DataFileFormat.FixedLength, TextEncoding = DataEncodingProfile.ShiftJis };
        const GenerationRequirement fourNumbers = GenerationRequirement.NumericLeadingZero | GenerationRequirement.NumericAllZero
            | GenerationRequirement.NumericFullLength | GenerationRequirement.NumericMaximum;
        const GenerationRequirement spacePositions = GenerationRequirement.TextLeadingSpace | GenerationRequirement.TextInternalSpace | GenerationRequirement.TextTrailingSpace;
        TestDataGenerationSettings Settings(GenerationRequirement requirements, int count = 1, int[]? targets = null, DataSaveOptions? options = null)
            => new(0, targets ?? [0], requirements, count, options ?? csv);
        static string[] Values(TestDataGenerationPlan plan, int field = 0) => plan.CasesByField[field].Select(item => item.Value).ToArray();
        static GenerationRequirement SpaceRequirements(int mask) => ((mask & 1) != 0 ? GenerationRequirement.TextLeadingSpace : GenerationRequirement.None)
            | ((mask & 2) != 0 ? GenerationRequirement.TextInternalSpace : GenerationRequirement.None)
            | ((mask & 4) != 0 ? GenerationRequirement.TextTrailingSpace : GenerationRequirement.None);
        static int ActualSpaceMask(string value, char space, char marker)
        {
            var mask = value.Length >= 2 && value[0] == space && value[1] == marker ? 1 : 0;
            if (value.Length >= 3 && Enumerable.Range(1, value.Length - 2).Any(index => value[index] == space && value[index - 1] == marker && value[index + 1] == marker)) mask |= 2;
            if (value.Length >= 2 && value[^1] == space && value[^2] == marker) mask |= 4;
            return mask;
        }

        Test("Generation keeps four numeric requirements independent and preserves literal zeros", () =>
        {
            FieldDefinition[] fields = [new("N", FieldDataType.PlainNumber, 8)];
            var settings = Settings(fourNumbers, 4);
            var plan = TestDataGenerator.Analyze(fields, ["456"], settings);
            Equal(false, plan.HasBlockingIssues);
            Equal(4, plan.RequiredNormalCount);
            Equal("01234567|00000000|12345678|99999999", string.Join("|", Values(plan)));
            var result = TestDataGenerator.Generate(fields, ["456"], settings);
            Equal("01234567|00000000|12345678|99999999", string.Join("|", result.Records.Select(row => row[0])));
            Equal(4, result.Cases.Select(item => item.CaseId).Distinct().Count());
        });
        Test("Generation cycles numeric digits after nine without floating-point conversion", () =>
        {
            FieldDefinition[] fields = [new("LONG", FieldDataType.PackedSigned, 50, DecimalScale: 20)];
            var plan = TestDataGenerator.Analyze(fields, ["0"], Settings(fourNumbers | GenerationRequirement.NumericNegativeMaximum, 5));
            Equal(false, plan.HasBlockingIssues);
            Equal("012345678901234567890123456789.01234567890123456789", Values(plan)[0]);
            Equal("123456789012345678901234567890.12345678901234567890", Values(plan)[2]);
            Equal("-" + new string('9', 30) + "." + new string('9', 20), Values(plan)[4]);
        });
        Test("Generation handles decimal precision, short seven and signed negative maximum", () =>
        {
            FieldDefinition[] fields = [new("DECIMAL", FieldDataType.SignedNumber, 5, DecimalScale: 3)];
            var plan = TestDataGenerator.Analyze(fields, ["0.000"], Settings(fourNumbers | GenerationRequirement.NumericShort
                | GenerationRequirement.NumericNegativeMaximum | GenerationRequirement.NumericMinimumUnit, 7));
            Equal(false, plan.HasBlockingIssues);
            Equal("01.234|00.000|12.345|99.999|7|-99.999|0.001", string.Join("|", Values(plan)));
            Equal(7, plan.CasesByField[0].Count);
        });
        Test("Generation reports impossible one-digit leading-zero case without making overflowing 01", () =>
        {
            FieldDefinition[] fields = [new("ONE", FieldDataType.PlainNumber, 1)];
            var plan = TestDataGenerator.Analyze(fields, ["3"], Settings(fourNumbers, 3));
            Equal(3, plan.RequiredNormalCount);
            Equal("0|1|9", string.Join("|", Values(plan)));
            Equal(true, plan.Issues.Any(issue => issue.Requirement == nameof(GenerationRequirement.NumericLeadingZero) && !issue.IsBlocking));
            Equal(false, Values(plan).Contains("01"));
        });
        Test("Generation reports numeric requirements that do not apply to unsigned integer fields", () =>
        {
            var plan = TestDataGenerator.Analyze([new("N", FieldDataType.PackedUnsigned, 4)], ["12"],
                Settings(GenerationRequirement.NumericNegativeMaximum | GenerationRequirement.NumericMinimumUnit));
            Equal(true, plan.HasBlockingIssues);
            Equal(0, plan.RequiredNormalCount);
            Equal(2, plan.Issues.Count(issue => issue.FieldIndex == 0));
        });
        Test("Generation combines or splits selected space positions at lengths one through five", () =>
        {
            var expected = new[] { "", " X|X ", " X |X X", " X X|XXX ", " X X " };
            for (var length = 1; length <= 5; length++)
            {
                var plan = TestDataGenerator.Analyze([new("X", FieldDataType.Text, length)], ["X"], Settings(spacePositions, 2));
                Equal(length == 1, plan.HasBlockingIssues);
                Equal(expected[length - 1], string.Join("|", Values(plan)));
                Equal(true, plan.Issues.Any(issue => issue.Requirement == nameof(GenerationRequirement.TextInternalSpace)) == (length < 3));
                foreach (var value in Values(plan)) Equal(length, value.Length);
            }
        });
        Test("Generation uses fullwidth BMP markers and U3000 spaces for type N", () =>
        {
            var plan = TestDataGenerator.Analyze([new("N", FieldDataType.FullWidthText, 5)], ["元"],
                Settings(spacePositions | GenerationRequirement.TextMaximum, 2));
            Equal(false, plan.HasBlockingIssues);
            Equal("漢漢漢漢漢|　漢　漢　", string.Join("|", Values(plan)));
            Equal(false, Values(plan).Any(value => value.Any(char.IsSurrogate)));
        });
        Test("Generation every selected CSV space subset has exact leading, interior and trailing positions at lengths one through five", () =>
        {
            foreach (var type in new[] { FieldDataType.Text, FieldDataType.HalfWidthText, FieldDataType.FullWidthText })
                for (var length = 1; length <= 5; length++)
                    for (var mask = 1; mask <= 7; mask++)
                    {
                        var field = new FieldDefinition("SPACES", type, length);
                        var marker = type == FieldDataType.FullWidthText ? '漢' : 'X';
                        var space = type == FieldDataType.FullWidthText ? '\u3000' : ' ';
                        var settings = Settings(SpaceRequirements(mask), 3);
                        var plan = TestDataGenerator.Analyze([field], [marker.ToString()], settings);
                        var expectedMask = mask & (length < 2 ? 0 : length < 3 ? 5 : 7);
                        Equal(expectedMask == 0, plan.HasBlockingIssues);
                        var covered = 0;
                        foreach (var item in plan.CasesByField[0])
                        {
                            var declaredMask = int.Parse(item.CaseId[(item.CaseId.LastIndexOf('-') + 1)..]);
                            var actualMask = ActualSpaceMask(item.Value, space, marker);
                            Equal(declaredMask, actualMask);
                            Equal(0, actualMask & ~mask);
                            Equal(length, item.Value.Length);
                            Equal(System.Numerics.BitOperations.PopCount((uint)declaredMask), item.Value.Count(character => character == space));
                            covered |= actualMask;
                        }
                        Equal(expectedMask, covered);
                        foreach (var (bit, requirement) in new[] { (1, GenerationRequirement.TextLeadingSpace), (2, GenerationRequirement.TextInternalSpace), (4, GenerationRequirement.TextTrailingSpace) })
                            if ((mask & bit) != 0 && (expectedMask & bit) == 0) Equal(true, plan.Issues.Any(issue => issue.Requirement == requirement.ToString()));
                    }
            var regression = TestDataGenerator.Analyze([new("INTERNAL_AND_TRAILING", FieldDataType.Text, 4)], ["base"], Settings(SpaceRequirements(6)));
            Equal("X X ", Values(regression).Single());
        });
        Test("Generation fixed whitespace subsets cover selected leading internal and trailing positions at lengths one through five", () =>
        {
            foreach (var type in new[] { FieldDataType.Text, FieldDataType.HalfWidthText, FieldDataType.FullWidthText })
                for (var length = 1; length <= 5; length++)
                    for (var mask = 1; mask <= 7; mask++)
                    {
                        var marker = type == FieldDataType.FullWidthText ? '漢' : 'X';
                        var space = type == FieldDataType.FullWidthText ? '\u3000' : ' ';
                        var field = new FieldDefinition("FIXED", type, length);
                        var plan = TestDataGenerator.Analyze([field], [marker.ToString()], Settings(SpaceRequirements(mask), 3, options: fixedOptions));
                        var expectedMask = mask & (length < 2 ? 0 : length < 3 ? 5 : 7);
                        Equal(expectedMask == 0, plan.HasBlockingIssues);
                        Equal(plan.CasesByField[0].Count, plan.RequiredNormalCount);
                        var covered = 0;
                        foreach (var item in plan.CasesByField[0])
                        {
                            var declaredMask = int.Parse(item.CaseId[(item.CaseId.LastIndexOf('-') + 1)..]);
                            var actualMask = ActualSpaceMask(item.Value, space, marker);
                            Equal(declaredMask, actualMask);
                            Equal(0, actualMask & ~mask);
                            Equal(length, item.Value.Length);
                            Equal(System.Numerics.BitOperations.PopCount((uint)declaredMask), item.Value.Count(character => character == space));
                            var encoded = FixedLengthDataWriter.EncodeField(field, item.Value);
                            var encoding = type == FieldDataType.FullWidthText ? NationalTextEncodingHelper.GetEncoding(NationalTextEncoding.ShiftJis)
                                : JapaneseEncodings.ForData(DataEncodingProfile.ShiftJis);
                            Equal(item.Value, encoding.GetString(encoded));
                            covered |= actualMask;
                        }
                        Equal(expectedMask, covered);
                        foreach (var (bit, requirement) in new[] { (1, GenerationRequirement.TextLeadingSpace), (2, GenerationRequirement.TextInternalSpace), (4, GenerationRequirement.TextTrailingSpace) })
                            if ((mask & bit) != 0 && (expectedMask & bit) == 0)
                                Equal(true, plan.Issues.Any(issue => issue.Requirement == requirement.ToString()));
                    }
        });
        Test("Generation distinguishes CSV empty, half-space-only and full-space-only values", () =>
        {
            var plan = TestDataGenerator.Analyze([new("TEXT", FieldDataType.Text, 6)], ["base"],
                Settings(GenerationRequirement.TextEmpty | GenerationRequirement.TextHalfSpaces | GenerationRequirement.TextFullSpaces, 3));
            Equal(false, plan.HasBlockingIssues);
            Equal("", Values(plan)[0]);
            Equal("      ", Values(plan)[1]);
            Equal("　　", Values(plan)[2]);
        });
        Test("Generation applies space-only type and output-byte constraints", () =>
        {
            var requirements = GenerationRequirement.TextHalfSpaces | GenerationRequirement.TextFullSpaces;
            var tiny = TestDataGenerator.Analyze([new("T", FieldDataType.Text, 1)], ["X"], Settings(requirements, 2));
            Equal(1, tiny.CasesByField[0].Count);
            Equal(true, tiny.Issues.Any(issue => issue.Requirement == nameof(GenerationRequirement.TextFullSpaces)));
            var half = TestDataGenerator.Analyze([new("A", FieldDataType.HalfWidthText, 4)], ["X"], Settings(requirements, 2));
            Equal("    ", Values(half).Single());
            Equal(true, half.Issues.Any(issue => issue.Requirement == nameof(GenerationRequirement.TextFullSpaces)));
            var national = TestDataGenerator.Analyze([new("N", FieldDataType.FullWidthText, 2)], ["漢"], Settings(requirements, 2));
            Equal("　　", Values(national).Single());
            Equal(true, national.Issues.Any(issue => issue.Requirement == nameof(GenerationRequirement.TextHalfSpaces)));
            var sjis = TestDataGenerator.Analyze([new("T", FieldDataType.Text, 6)], ["X"],
                Settings(GenerationRequirement.TextFullSpaces, options: csv with { CsvEncoding = CsvTextEncoding.StrictShiftJis }));
            Equal("　　　", Values(sjis).Single());
        });
        Test("Generation keeps fixed whitespace positions while explaining cases that require distinct CSV empty values", () =>
        {
            var plan = TestDataGenerator.Analyze([new("X", FieldDataType.Text, 5)], ["base"],
                Settings(spacePositions | GenerationRequirement.TextEmpty | GenerationRequirement.TextHalfSpaces | GenerationRequirement.TextFullSpaces,
                    options: fixedOptions));
            Equal(false, plan.HasBlockingIssues);
            Equal(" X X ", Values(plan).Single());
            Equal(3, plan.Issues.Count);
            Equal(true, plan.Issues.All(issue => issue.Message.Contains("固定長保存", StringComparison.Ordinal)));
        });
        Test("Generation clones only selected fields, excludes REDEFINES and keeps expanded OCCURS physical indices", () =>
        {
            var fields = new List<FieldDefinition> { new("NUM", FieldDataType.PlainNumber, 4),
                new("ALIAS", FieldDataType.Text, 4, RedefinesName: "NUM"), new("ITEM(1)", FieldDataType.Text, 3), new("ITEM(2)", FieldDataType.Text, 3) };
            var baseline = new List<string> { "0042", "unchanged alias", "one", "two" };
            var targets = new List<int> { 1, 3, 3 };
            var settings = new TestDataGenerationSettings(7, targets, GenerationRequirement.TextMaximum, 2, csv);
            var fingerprint = TestDataGenerator.DefinitionFingerprint(fields);
            var result = TestDataGenerator.Generate(fields, baseline, settings);
            Equal("0042|unchanged alias|one|XXX", string.Join("|", result.Records[0]));
            Equal("0042|unchanged alias|one|two", string.Join("|", result.Records[1]));
            Equal("0042|unchanged alias|one|two", string.Join("|", baseline));
            Equal(fingerprint, TestDataGenerator.DefinitionFingerprint(fields));
            Equal(true, result.Issues.Any(issue => issue.Requirement == "REDEFINES"));
            Equal(4, result.Cases[0].Cells.Count);
            Equal("基準値のみ（追加行）", result.Cases[1].Goal);
            targets.Clear();
            Equal(3, result.Settings.TargetFieldIndexes.Count);
            result.Records[0][2] = "new";
            Equal("one", baseline[2]);
            Equal("one", result.Records[1][2]);
        });
        Test("Generation shares row slots across fields and blocks an insufficient requested normal count atomically", () =>
        {
            FieldDefinition[] fields = [new("NUM", FieldDataType.PlainNumber, 8), new("TEXT", FieldDataType.Text, 5)];
            string[] baseline = ["42", "base"];
            var settings = Settings(fourNumbers | GenerationRequirement.TextMaximum, 3, [0, 1]);
            var plan = TestDataGenerator.Analyze(fields, baseline, settings);
            Equal(4, plan.RequiredNormalCount);
            Equal(true, plan.HasBlockingIssues);
            Throws<InvalidDataException>(() => TestDataGenerator.Generate(fields, baseline, settings));
            Equal("42|base", string.Join("|", baseline));
            var result = TestDataGenerator.Generate(fields, baseline, settings with { NormalRecordCount = 5 });
            Equal(5, result.Records.Count);
            Equal("XXXXX", result.Records[0][1]);
            Equal("base", result.Records[1][1]);
            Equal("42|base", string.Join("|", result.Records[4]));
            Equal("基準値のみ（追加行）", result.Cases[4].Goal);
        });
        Test("Generation appends one intentional CSV error per field and reason from a normal generated row", () =>
        {
            FieldDefinition[] fields = [new("NUM", FieldDataType.PlainNumber, 3), new("TEXT", FieldDataType.Text, 4), new("KEEP", FieldDataType.Text, 8)];
            string[] baseline = ["12", "base", "retain"];
            var settings = Settings(GenerationRequirement.NumericMaximum | GenerationRequirement.TextMaximum
                | GenerationRequirement.InvalidNumeric | GenerationRequirement.Overlength, targets: [0, 1]);
            var plan = TestDataGenerator.Analyze(fields, baseline, settings);
            Equal(3, plan.InvalidRecordCount);
            Equal(true, plan.CasesByField.Values.SelectMany(values => values).All(item => !item.IntentionalInvalid));
            var result = TestDataGenerator.Generate(fields, baseline, settings);
            Equal(1 + plan.InvalidRecordCount, result.Records.Count);
            Equal(3, result.Cases.Count(item => item.IntentionalInvalid));
            foreach (var item in result.Cases.Skip(1))
            {
                Equal(1, item.Cells.Count(cell => cell.IntentionalInvalid));
                var index = item.Cells.Single(cell => cell.IntentionalInvalid).FieldIndex;
                var row = result.Records[item.RecordNumber - 1];
                Equal(1, row.Where((value, column) => value != result.Records[0][column]).Count());
                Equal(true, row[index] != result.Records[0][index]);
                Equal("retain", row[2]);
                Equal(row[index], item.Cells[index].Value);
                Equal(1, RecordValidator.Validate(fields, [row]).Count);
            }
        });
        Test("Generation never includes invalid rows in fixed output plans", () =>
        {
            var settings = Settings(GenerationRequirement.NumericMaximum | GenerationRequirement.InvalidNumeric | GenerationRequirement.Overlength,
                options: fixedOptions);
            var plan = TestDataGenerator.Analyze([new("N", FieldDataType.PlainNumber, 3)], ["1"], settings);
            Equal(0, plan.InvalidRecordCount);
            Equal(2, plan.Issues.Count);
            Equal(1, TestDataGenerator.Generate([new("N", FieldDataType.PlainNumber, 3)], ["1"], settings).Records.Count);
        });
        Test("Generation rejects invalid settings and unsafe allocation sizes before generation", () =>
        {
            FieldDefinition[] fields = [new("N", FieldDataType.PlainNumber, 8)];
            foreach (var settings in new[] { Settings(GenerationRequirement.None), Settings(fourNumbers, 0), Settings(fourNumbers, 10_001),
                Settings(fourNumbers, 4, [-1]), Settings(fourNumbers, 4, []), Settings((GenerationRequirement)(1 << 24)),
                Settings(fourNumbers, 4) with { BaselineRecordIndex = -1 },
                Settings(fourNumbers, 4, options: csv with { Format = (DataFileFormat)99 }) })
            {
                Equal(true, TestDataGenerator.Analyze(fields, ["1"], settings).HasBlockingIssues);
                Throws<InvalidDataException>(() => TestDataGenerator.Generate(fields, ["1"], settings));
            }
            Equal(true, TestDataGenerator.Analyze(fields, [], Settings(fourNumbers, 4)).HasBlockingIssues);
            Equal(true, TestDataGenerator.Analyze([new("HUGE", FieldDataType.Text, int.MaxValue)], ["x"], Settings(GenerationRequirement.TextMaximum)).HasBlockingIssues);
            Equal(true, TestDataGenerator.Analyze([new("BIG", FieldDataType.Text, 100_000)], ["x"], Settings(GenerationRequirement.TextMaximum, 201)).HasBlockingIssues);
            var manyFields = Enumerable.Range(0, 101).Select(index => new FieldDefinition($"F{index}", FieldDataType.Text, 1)).ToArray();
            Equal(true, TestDataGenerator.Analyze(manyFields, manyFields.Select(_ => "").ToArray(), Settings(GenerationRequirement.TextMaximum, 10_000)).HasBlockingIssues);
            var paddingRequirements = fourNumbers | GenerationRequirement.NumericShort | GenerationRequirement.NumericNegativeMaximum
                | GenerationRequirement.NumericMinimumUnit | GenerationRequirement.TextMaximum | GenerationRequirement.Identifier;
            var paddingGuard = TestDataGenerator.Analyze([new("DECIMAL", FieldDataType.SignedNumber, 8, DecimalScale: 2), new("TEXT", FieldDataType.Text, 5)],
                ["1.00", new string('x', 2_500_000)], Settings(paddingRequirements, targets: [0, 1]));
            Equal(true, paddingGuard.HasBlockingIssues);
            Equal(9, paddingGuard.RequiredNormalCount);
            Equal(0, paddingGuard.CasesByField.Count); // Common padding + two identifier rows were budgeted before case strings.
        });
        Test("Generation bounds the combined normal and invalid row count", () =>
        {
            var plan = TestDataGenerator.Analyze([new("N", FieldDataType.PlainNumber, 1)], ["1"],
                Settings(GenerationRequirement.NumericMaximum | GenerationRequirement.InvalidNumeric, 10_000));
            Equal(true, plan.HasBlockingIssues);
            Equal(1, plan.InvalidRecordCount);
        });
        Test("Generation preflights unencodable untouched baseline cells without silently replacing characters", () =>
        {
            FieldDefinition[] fields = [new("CHANGED", FieldDataType.PlainNumber, 2), new("UNCHANGED", FieldDataType.Text, 8)];
            foreach (var options in new[] { csv with { CsvEncoding = CsvTextEncoding.ShiftJis }, fixedOptions })
            {
                var settings = Settings(GenerationRequirement.NumericMaximum, options: options);
                var plan = TestDataGenerator.Analyze(fields, ["1", "😀"], settings);
                Equal(true, plan.HasBlockingIssues);
                Equal(true, plan.Issues.Any(issue => issue.IsBlocking && issue.FieldIndex == 1));
                Throws<InvalidDataException>(() => TestDataGenerator.Generate(fields, ["1", "😀"], settings));
            }
        });
        Test("Generation uses strict CSV header encoding when a header is requested", () =>
        {
            var plan = TestDataGenerator.Analyze([new("😀", FieldDataType.PlainNumber, 2)], ["1"],
                Settings(GenerationRequirement.NumericMaximum, options: csv with { CsvEncoding = CsvTextEncoding.StrictShiftJis, CsvHasHeader = true }));
            Equal(true, plan.HasBlockingIssues);
            Equal(true, plan.Issues.Any(issue => issue.Requirement == "CSVヘッダー"));
        });
        Test("Generation does not infer business rules for untouched CSV baseline cells", () =>
        {
            FieldDefinition[] fields = [new("CHANGE", FieldDataType.Text, 3), new("BASELINE", FieldDataType.PlainNumber, 2)];
            var result = TestDataGenerator.Generate(fields, ["abc", "original literal"], Settings(GenerationRequirement.TextMaximum));
            Equal("original literal", result.Records[0][1]);
            Equal(false, result.Issues.Any(issue => issue.IsBlocking));
        });
        Test("Generation preflights the selected national encoding width without mutating source definitions", () =>
        {
            foreach (var sourceWidth in new[] { 2, 4 })
            {
                FieldDefinition[] fields = [new("N", FieldDataType.FullWidthText, 3, NationalByteWidth: sourceWidth)];
                var fingerprint = TestDataGenerator.DefinitionFingerprint(fields);
                foreach (var encoding in new[] { NationalTextEncoding.ShiftJis, NationalTextEncoding.StrictShiftJis, NationalTextEncoding.Utf16, NationalTextEncoding.Utf32 })
                {
                    var settings = Settings(GenerationRequirement.TextMaximum, options: fixedOptions with { NationalEncoding = encoding });
                    Equal(false, TestDataGenerator.Analyze(fields, ["元"], settings).HasBlockingIssues);
                    var generated = TestDataGenerator.Generate(fields, ["元"], settings);
                    var outputField = fields[0] with { NationalByteWidth = NationalTextEncodingHelper.FixedByteWidth(encoding) };
                    Equal(3 * outputField.NationalByteWidth, FixedLengthDataWriter.EncodeField(outputField, generated.Records[0][0],
                        fixedOptions.TextEncoding!.Value, encoding).Length);
                    Equal(fingerprint, generated.DefinitionFingerprint);
                    Equal(sourceWidth, fields[0].NationalByteWidth);
                }
            }
        });
        Test("Generation CSV round trips leading zeros and literal whitespace through existing serializers", () =>
        {
            FieldDefinition[] fields = [new("NUM", FieldDataType.PlainNumber, 8), new("TEXT", FieldDataType.Text, 6), new("N", FieldDataType.FullWidthText, 5)];
            var requirements = fourNumbers | spacePositions | GenerationRequirement.TextEmpty | GenerationRequirement.TextHalfSpaces | GenerationRequirement.TextFullSpaces;
            var initial = Settings(requirements, 6, [0, 1, 2]);
            var plan = TestDataGenerator.Analyze(fields, ["1", "base", "元"], initial);
            var result = TestDataGenerator.Generate(fields, ["1", "base", "元"], initial with { NormalRecordCount = Math.Max(1, plan.RequiredNormalCount) });
            var path = Path.Combine(temporaryDirectory, "generated-synthetic.csv");
            foreach (var encoding in new[] { CsvTextEncoding.Utf8, CsvTextEncoding.ShiftJis, CsvTextEncoding.StrictShiftJis })
            {
                CsvDataWriter.Write(path, fields, result.Records, encoding, hasHeader: true);
                var restored = CsvDataReader.Read(path, fields, encoding, hasHeader: true);
                Equal(true, result.Records.SelectMany(row => row).SequenceEqual(restored.SelectMany(row => row)));
            }
        });
        Test("Generation fixed round trips signed and packed decimal cases with existing serializers", () =>
        {
            FieldDefinition[] fields = [new("UNSIGNED", FieldDataType.PlainNumber, 8), new("SIGNED", FieldDataType.SignedNumber, 5, DecimalScale: 2),
                new("PACKED", FieldDataType.PackedSigned, 7, DecimalScale: 3), new("TEXT", FieldDataType.Text, 5), new("N", FieldDataType.FullWidthText, 5)];
            var settings = Settings(fourNumbers | GenerationRequirement.NumericNegativeMaximum | GenerationRequirement.NumericMinimumUnit
                | GenerationRequirement.TextMaximum | GenerationRequirement.TextInternalSpace, 6, [0, 1, 2, 3, 4], fixedOptions);
            var result = TestDataGenerator.Generate(fields, ["1", "1.00", "1.000", "base", "元"], settings);
            var path = Path.Combine(temporaryDirectory, "generated-synthetic-fixed.dat");
            FixedLengthDataWriter.Write(path, fields, result.Records, fixedOptions.SeparatorMode, fixedOptions.TextEncoding!.Value, fixedOptions.NationalEncoding);
            var restored = FixedLengthDataReader.Read(path, fields, fixedOptions.SeparatorMode, fixedOptions.TextEncoding.Value, fixedOptions.NationalEncoding);
            Equal(result.Records.Count, restored.Count);
            for (var row = 0; row < result.Records.Count; row++)
                for (var column = 0; column < fields.Length; column++)
                {
                    var expected = result.Records[row][column];
                    if (column <= 2)
                    {
                        Equal(true, NumericValueFormatter.TryFormatDisplayValue(expected, fields[column], column is 1 or 2, out expected, out _));
                    }
                    Equal(expected, restored[row][column]);
                }
        });
        Test("Generation identifiers are two normal cases and numeric identifiers contain only digit markers", () =>
        {
            FieldDefinition[] fields = [new("NUM", FieldDataType.PlainNumber, 4), new("TEXT", FieldDataType.Text, 5), new("N", FieldDataType.FullWidthText, 5)];
            var plan = TestDataGenerator.Analyze(fields, ["1", "base", "元"], Settings(GenerationRequirement.Identifier, 2, [0, 1, 2]));
            Equal(2, plan.RequiredNormalCount);
            Equal("0001|0002", string.Join("|", Values(plan)));
            Equal(true, Values(plan).All(value => value.All(character => character is >= '0' and <= '9')));
            Equal("C0002|R0002", string.Join("|", Values(plan, 1)));
            Equal("Ｃ０００３|Ｒ０００２", string.Join("|", Values(plan, 2)));
            Equal(true, plan.CasesByField.Values.All(values => values.Count == 2 && values.All(item => !item.IntentionalInvalid)));
        });
        Test("Generation aligns identifiers after all normal patterns across mixed physical and OCCURS fields", () =>
        {
            FieldDefinition[] fields = [new("NUMBER", FieldDataType.PlainNumber, 4), new("ALIAS", FieldDataType.Text, 4, RedefinesName: "NUMBER"),
                new("ITEM(1)", FieldDataType.Text, 5), new("ITEM(2)", FieldDataType.FullWidthText, 5), new("KEEP", FieldDataType.Text, 4)];
            string[] baseline = ["0042", "alias baseline", "first", "元", "keep"];
            var requirements = fourNumbers | GenerationRequirement.TextMaximum | GenerationRequirement.Identifier;
            var settings = Settings(requirements, 7, [0, 1, 2, 3]);
            var plan = TestDataGenerator.Analyze(fields, baseline, settings);
            Equal(false, plan.HasBlockingIssues);
            Equal(6, plan.RequiredNormalCount);
            Equal(false, plan.CasesByField.ContainsKey(1));
            Equal("0123|0000|1234|9999", string.Join("|", Values(plan).Take(4)));
            foreach (var index in new[] { 0, 2, 3 })
            {
                Equal(6, plan.CasesByField[index].Count);
                Equal(true, plan.CasesByField[index][4].CaseId.EndsWith(".identifier.column", StringComparison.Ordinal));
                Equal(true, plan.CasesByField[index][5].CaseId.EndsWith(".identifier.row", StringComparison.Ordinal));
            }
            Equal("XXXXX|first|first|first|C0003|R0006", string.Join("|", Values(plan, 2)));
            Equal("漢漢漢漢漢|元|元|元|Ｃ０００４|Ｒ０００６", string.Join("|", Values(plan, 3)));
            Equal(true, plan.CasesByField[2][1].Goal.StartsWith("基準値保持", StringComparison.Ordinal));
            var generated = TestDataGenerator.Generate(fields, baseline, settings);
            Equal("0001|alias baseline|C0003|Ｃ０００４|keep", string.Join("|", generated.Records[4]));
            Equal("0006|alias baseline|R0006|Ｒ０００６|keep", string.Join("|", generated.Records[5]));
            Equal("基準値のみ（追加行）", generated.Cases[6].Goal);
            Equal(true, generated.Records[6].SequenceEqual(baseline));
            Equal(true, generated.Cases[1].Cells[2].Goal.StartsWith("基準値保持", StringComparison.Ordinal));
            Equal(TestDataGenerator.DefinitionFingerprint(fields), generated.DefinitionFingerprint);
            var tooFew = settings with { NormalRecordCount = 5 };
            Equal(6, TestDataGenerator.Analyze(fields, baseline, tooFew).RequiredNormalCount);
            Throws<InvalidDataException>(() => TestDataGenerator.Generate(fields, baseline, tooFew));
            var sameNumeric = TestDataGenerator.Analyze([new("KEEP", FieldDataType.Text, 2), new("SECOND", FieldDataType.PlainNumber, 2)], ["ok", "1"],
                Settings(GenerationRequirement.Identifier, 2, [1]));
            Equal("02|02", string.Join("|", Values(sameNumeric, 1)));
            Equal(true, sameNumeric.Issues.Any(issue => issue.FieldIndex == 1 && issue.Requirement == "Identifier" && issue.Message.Contains("同じ値", StringComparison.Ordinal)));
        });
        Test("Generation keeps short numeric seven while documenting fixed serializer zero padding", () =>
        {
            FieldDefinition[] fields = [new("SHORT", FieldDataType.PlainNumber, 4)];
            var settings = Settings(GenerationRequirement.NumericShort, options: fixedOptions);
            var plan = TestDataGenerator.Analyze(fields, ["1"], settings);
            Equal(false, plan.HasBlockingIssues);
            Equal("7", Values(plan).Single());
            Equal(true, plan.Issues.Any(issue => issue.Requirement == nameof(GenerationRequirement.NumericShort) && !issue.IsBlocking && issue.Message.Contains("ゼロ埋め", StringComparison.Ordinal)));
            var generated = TestDataGenerator.Generate(fields, ["1"], settings);
            Equal("7", generated.Records[0][0]);
            Equal("0007", Encoding.ASCII.GetString(FixedLengthDataWriter.EncodeRecord(fields, generated.Records[0])));
            var csvPlan = TestDataGenerator.Analyze(fields, ["1"], settings with { OutputOptions = csv });
            Equal(false, csvPlan.Issues.Any(issue => issue.Requirement == nameof(GenerationRequirement.NumericShort)));
        });
        Test("Generation reports unavoidable short column identifier collisions", () =>
        {
            var fields = Enumerable.Range(1, 11).Select(index => new FieldDefinition($"N{index}", FieldDataType.PlainNumber, 1)).ToArray();
            var plan = TestDataGenerator.Analyze(fields, fields.Select(_ => "0").ToArray(),
                Settings(GenerationRequirement.Identifier, 2, Enumerable.Range(0, fields.Length).ToArray()));
            Equal(false, plan.HasBlockingIssues);
            Equal(Values(plan, 0)[0], Values(plan, 10)[0]);
            Equal(true, plan.Issues.Any(issue => issue.FieldIndex == 0 && issue.Requirement == "Identifier"));
            Equal(true, plan.Issues.Any(issue => issue.FieldIndex == 10 && issue.Requirement == "Identifier"));
            var text = TestDataGenerator.Analyze([new("A", FieldDataType.Text, 1), new("B", FieldDataType.Text, 1)], ["X", "Y"], Settings(GenerationRequirement.Identifier, 2, [0, 1]));
            Equal(true, text.Issues.Any(issue => issue.FieldIndex == 1 && issue.Requirement == "Identifier"));
            var roomyFields = Enumerable.Range(1, 20).Select(index => new FieldDefinition($"T{index}", FieldDataType.Text, 5)).ToArray();
            var roomy = TestDataGenerator.Analyze(roomyFields, roomyFields.Select(_ => "base").ToArray(), Settings(GenerationRequirement.Identifier, 2, [1, 19]));
            Equal("C0002", Values(roomy, 1)[0]);
            Equal("C0020", Values(roomy, 19)[0]);
            Equal(false, roomy.Issues.Any(issue => issue.FieldIndex >= 0 && issue.Requirement == "Identifier"));
        });
        Test("Generation blocks when all selected requirements are unavailable instead of cloning only the baseline", () =>
        {
            var fields = new[] { new FieldDefinition("one digit", FieldDataType.PlainNumber, 1) };
            var settings = Settings(GenerationRequirement.NumericLeadingZero, 4);
            var plan = TestDataGenerator.Analyze(fields, ["7"], settings);
            Equal(0, plan.RequiredNormalCount);
            Equal(true, plan.HasBlockingIssues);
            Throws<InvalidDataException>(() => TestDataGenerator.Generate(fields, ["7"], settings));
        });
        Test("Generation case IDs and structure fingerprints are stable and field-order-sensitive", () =>
        {
            FieldDefinition[] fields = [new("A", FieldDataType.PlainNumber, 4), new("B", FieldDataType.Text, 3)];
            var settings = Settings(GenerationRequirement.NumericMaximum | GenerationRequirement.Identifier | GenerationRequirement.InvalidNumeric, 3, [0, 1]);
            var first = TestDataGenerator.Generate(fields, ["1", "bas"], settings);
            var second = TestDataGenerator.Generate(fields, ["1", "bas"], settings);
            Equal(true, first.Cases.Select(item => item.CaseId).SequenceEqual(second.Cases.Select(item => item.CaseId)));
            Equal(true, first.Cases.SelectMany(item => item.Cells).Select(item => item.CaseId).SequenceEqual(second.Cases.SelectMany(item => item.Cells).Select(item => item.CaseId)));
            Equal(64, first.DefinitionFingerprint.Length);
            Equal(first.DefinitionFingerprint, second.DefinitionFingerprint);
            Equal(false, first.DefinitionFingerprint == TestDataGenerator.DefinitionFingerprint(fields.Reverse().ToArray()));
            Equal(false, first.DefinitionFingerprint == TestDataGenerator.DefinitionFingerprint([fields[0] with { DecimalScale = 1 }, fields[1]]));
            Equal(false, first.DefinitionFingerprint == TestDataGenerator.DefinitionFingerprint([fields[0], fields[1] with { RedefinesName = "A" }]));
        });
    }
}
