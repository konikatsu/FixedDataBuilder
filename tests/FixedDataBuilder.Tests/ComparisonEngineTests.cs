using System.Text;
using FixedDataBuilder;

internal static partial class Program
{
    // Registered by the integrating change in Program.cs. All fixtures are synthetic.
    private static void RunComparisonEngineTests(string repo)
    {
        Test("Comparison snapshots deeply copy models and keep different record counts independent", () =>
        {
            var fields = new List<FieldDefinition> { new("id", FieldDataType.Text, 4, Comment: "確認用") };
            var rows = new List<List<string>> { new() { "0001" }, new() { "0002" } };
            var input = ComparisonDataSet.FromRecords(fields, rows);
            var output = ComparisonFixture(["renamed"], [new[] { "0002" }]);
            fields[0] = fields[0] with { Comment = "変更" };
            rows[0][0] = "changed";
            Equal("確認用", input.Fields[0].Comment);
            Equal("0001", input.Records[0][0]);
            Equal(2, input.Records.Count);
            Equal(1, output.Records.Count);
            var result = ComparisonEngine.Compare(input, output, new(1, 0), new(0, ComparisonMappingKind.Input, [new(0)]));
            Equal(ComparisonResultStatus.DirectValueEqual, result.Status);
            Equal("0001|0002", string.Join("|", input.Records.Select(row => row[0])));
            Equal("0002", output.Records[0][0]);
            Throws<NotSupportedException>(() => ((IList<string>)input.Records[0])[0] = "mutation");
        });

        Test("Comparison CSV source keeps quoted logical values, spaces and leading zeros separate from syntax", () =>
        {
            var paths = ComparisonPaths("csv-source");
            File.WriteAllText(paths.Definition, "項目名,定義\r\nid,X(8)\r\ntext,X(30)\r\n", new UTF8Encoding(false));
            var syntax = "id,text\r\n001,\"  A,\"\"Q\"\"  \"\r\n";
            File.WriteAllText(paths.Data, syntax, new UTF8Encoding(false));
            var data = ComparisonDataSet.Load(paths.Definition, paths.Data, new(DataFileFormat.Csv, CsvHasHeader: true));
            Equal("001", data.Records[0][0]);
            Equal("  A,\"Q\"  ", data.Records[0][1]);
            Equal(syntax, Encoding.UTF8.GetString(data.CopySourceBytes()!));
            Equal(0, data.RawRecords.Count);
            Equal(64, data.SourceFingerprint!.Length);
            var bytes = ComparisonEngine.Evaluate(data, 0, new ComparisonSliceExpression(new ComparisonInputExpression(0),
                1, 2, ComparisonSliceUnit.Bytes, ComparisonIndexOrigin.Zero));
            Equal("01", bytes.ExpectedValue);
            Equal(true, bytes.Trace.Any(step => step.Description.Contains("CSV論理値")));
            var copy = data.CopySourceBytes()!;
            copy[0] = 0;
            Equal((byte)'i', data.CopySourceBytes()![0]);
        });

        Test("Comparison fixed UTF8 storage retains trailing spaces and original absolute record boundaries", () =>
        {
            var data = ComparisonFixedFixture("utf8-raw", "X(6)", Encoding.UTF8.GetBytes("あA  \r\nいB  \r\n"),
                DataEncodingProfile.Utf8WithNationalText);
            Equal("あA", data.Records[0][0]);
            Equal("あA  ", data.ReadSource(0, 0, ComparisonSourceKind.FixedStorageText).Value);
            Equal(0, data.RawRecords[0].ByteOffset);
            Equal(8, data.RawRecords[1].ByteOffset);
            Equal("いB  ", Encoding.UTF8.GetString(data.CopyRawRecordBytes(1)));
            var expression = new ComparisonSliceExpression(new ComparisonInputExpression(0, ComparisonSourceKind.RawFixedBytes),
                4, 3, ComparisonSliceUnit.Bytes, ComparisonIndexOrigin.One);
            var evaluated = ComparisonEngine.Evaluate(data, 0, expression);
            Equal(true, evaluated.Success);
            Equal("A  ", evaluated.ExpectedValue);
            Equal(true, evaluated.Trace.Any(step => step.Description.Contains("元固定長")));
            var fieldCopy = data.CopyRawFieldBytes(0, 0);
            fieldCopy[0] = 0;
            Equal((byte)0xe3, data.CopyRawFieldBytes(0, 0)[0]);
        });

        Test("Comparison UTF8 byte slices reject mid-character boundaries, missing raw source and invalid ranges", () =>
        {
            var data = ComparisonFixedFixture("utf8-boundary", "X(6)", Encoding.UTF8.GetBytes("あA  "),
                DataEncodingProfile.Utf8WithNationalText, RecordSeparatorMode.None);
            var raw = new ComparisonInputExpression(0, ComparisonSourceKind.RawFixedBytes);
            foreach (var slice in new[]
            {
                new ComparisonSliceExpression(raw, 1, 1, ComparisonSliceUnit.Bytes, ComparisonIndexOrigin.Zero),
                new ComparisonSliceExpression(raw, 0, 2, ComparisonSliceUnit.Bytes, ComparisonIndexOrigin.Zero),
                new ComparisonSliceExpression(raw, 6, 1, ComparisonSliceUnit.Bytes, ComparisonIndexOrigin.Zero),
                new ComparisonSliceExpression(new ComparisonInputExpression(0), 0, 1, ComparisonSliceUnit.Bytes, ComparisonIndexOrigin.Zero)
            })
            {
                var evaluated = ComparisonEngine.Evaluate(data, 0, slice);
                Equal(ComparisonEvaluationStatus.CannotEvaluate, evaluated.Status);
                Equal<string?>(null, evaluated.ExpectedValue);
                Equal(true, !string.IsNullOrEmpty(evaluated.Reason));
            }
            var snapshot = ComparisonFixture(["x"], [new[] { "あA" }]);
            Equal(false, ComparisonEngine.Evaluate(snapshot, 0, new ComparisonSliceExpression(raw, 0, 1,
                ComparisonSliceUnit.Bytes, ComparisonIndexOrigin.Zero)).Success);
        });

        Test("Comparison strict SJIS byte slices reject a trail byte mistaken for a standalone character", () =>
        {
            var encoding = JapaneseEncodings.ForCsv(CsvTextEncoding.StrictShiftJis);
            var data = ComparisonFixedFixture("sjis-boundary", "X(5)", encoding.GetBytes("あB  "),
                DataEncodingProfile.StrictShiftJis, RecordSeparatorMode.None);
            var reference = new ComparisonInputExpression(0, ComparisonSourceKind.RawFixedBytes);
            Equal("B  ", ComparisonEngine.Evaluate(data, 0, new ComparisonSliceExpression(reference, 3, 3,
                ComparisonSliceUnit.Bytes, ComparisonIndexOrigin.One)).ExpectedValue);
            Equal(ComparisonEvaluationStatus.CannotEvaluate, ComparisonEngine.Evaluate(data, 0,
                new ComparisonSliceExpression(reference, 2, 1, ComparisonSliceUnit.Bytes, ComparisonIndexOrigin.One)).Status);
        });

        Test("Comparison source loading honors definition encoding and physical REDEFINES storage", () =>
        {
            var paths = ComparisonPaths("definition-sjis");
            File.WriteAllBytes(paths.Definition, JapaneseEncodings.ForCsv(CsvTextEncoding.StrictShiftJis)
                .GetBytes("項目名,定義,コメント\r\n区分,X(2),入力No.1\r\n"));
            File.WriteAllBytes(paths.Data, [(byte)'0', (byte)'1']);
            var loaded = ComparisonDataSet.Load(paths.Definition, paths.Data, new(SeparatorMode: RecordSeparatorMode.None),
                DefinitionTextEncoding.ShiftJis);
            Equal("区分", loaded.Fields[0].Name);
            Equal("01", loaded.Records[0][0]);
            Equal(64, loaded.DefinitionFileFingerprint!.Length);
            var copybook = Path.ChangeExtension(paths.Definition, ".cbl");
            File.WriteAllText(copybook, "       01 TEST-RECORD.\r\n          05 BASE PIC X(2).\r\n          05 VIEW-FIELD REDEFINES BASE PIC X(2).\r\n",
                new UTF8Encoding(false));
            var redefined = ComparisonDataSet.Load(copybook, paths.Data, new(SeparatorMode: RecordSeparatorMode.None));
            Equal(2, redefined.Fields.Count);
            Equal("01", redefined.ReadSource(0, 1, ComparisonSourceKind.FixedStorageText).Value);
            Equal(true, redefined.CopyRawFieldBytes(0, 0).SequenceEqual(redefined.CopyRawFieldBytes(0, 1)));
        });

        Test("Comparison field suggestions retain explicit evidence, duplicate names and unresolved markers", () =>
        {
            var input = ComparisonFixture(["same", "same", "ITEM(1)", "ITEM(2)", "正式名"], [new[] { "a", "b", "c", "d", "e" }]);
            var fields = new[]
            {
                new FieldDefinition("same", FieldDataType.Text, 8),
                new FieldDefinition("renamed", FieldDataType.Text, 8, Comment: "入力No.3;入力参照=No.4"),
                new FieldDefinition("different", FieldDataType.Text, 8, Comment: "入力：正式名"),
                new FieldDefinition("prose", FieldDataType.Text, 8, Comment: "正式名を切り取って結合; No.2; 入力No.99"),
                new FieldDefinition("partial", FieldDataType.Text, 8, Comment: "ITEM")
            };
            var output = ComparisonDataSet.FromRecords(fields, [new[] { "a", "b", "c", "d", "e" }]);
            var suggestions = ComparisonEngine.SuggestFieldMappings(input, output);
            Equal(true, suggestions[0].IsAmbiguous);
            Equal("0|1", string.Join("|", suggestions[0].Candidates.Select(candidate => candidate.InputFieldOrdinal)));
            Equal("2|3", string.Join("|", suggestions[1].Candidates.Select(candidate => candidate.InputFieldOrdinal)));
            Equal(4, suggestions[2].Candidates.Single().InputFieldOrdinal);
            Equal(true, suggestions[2].Candidates[0].Evidence[0].Contains("明示入力名"));
            Equal(0, suggestions[3].Candidates.Count);
            Equal(1, suggestions[3].Issues.Count);
            Equal(0, suggestions[4].Candidates.Count);
            Equal("入力：正式名", output.Fields[2].Comment);
        });

        Test("Comparison composite keys use exact tuples with independent original row indices", () =>
        {
            var input = ComparisonFixture(["id", "sub"], [new[] { "01", "A|B" }, new[] { "02", "A" }, new[] { "only", "in" }]);
            var output = ComparisonFixture(["key", "part"], [new[] { "02", "A" }, new[] { "01", "A|B" }, new[] { "only", "out" }]);
            var pairs = ComparisonEngine.PairByKeys(input, output, [0, 1], [0, 1]);
            Equal(4, pairs.Count);
            Equal<int?>(1, pairs[0].InputRecordIndex);
            Equal<int?>(0, pairs[0].OutputRecordIndex);
            Equal(ComparisonPairKind.KeyUnique, pairs[0].Kind);
            Equal<int?>(0, pairs[1].InputRecordIndex);
            Equal(ComparisonPairKind.OutputOnly, pairs[2].Kind);
            Equal(ComparisonPairKind.InputOnly, pairs[3].Kind);
            Throws<ArgumentException>(() => ComparisonEngine.PairByKeys(input, output, [], []));
            Throws<ArgumentOutOfRangeException>(() => ComparisonEngine.PairByKeys(input, output, [2], [0]));
            var csv = ComparisonDataSet.FromRecords([
                new FieldDefinition("base", FieldDataType.Text, 2),
                new FieldDefinition("view", FieldDataType.Text, 2, RedefinesName: "base")],
                [new[] { "A", "" }], new(DataFileFormat.Csv));
            Equal(false, csv.ReadSource(0, 1).Success);
            Throws<ArgumentException>(() => ComparisonEngine.PairByKeys(csv, csv, [1], [1]));
        });

        Test("Comparison duplicate keys remain candidates on either side and preserve zero and space distinctions", () =>
        {
            var input = ComparisonFixture(["id"], [new[] { "A" }, new[] { "A" }, new[] { "01" }, new[] { "1 " }]);
            var output = ComparisonFixture(["id"], [new[] { "A" }, new[] { "01" }, new[] { "01" }, new[] { "1" }]);
            var pairs = ComparisonEngine.PairByKeys(input, output, [0], [0]);
            Equal(ComparisonPairKind.Ambiguous, pairs[0].Kind);
            Equal<int?>(null, pairs[0].InputRecordIndex);
            Equal("0|1", string.Join("|", pairs[0].InputCandidates!));
            Equal(ComparisonPairKind.Ambiguous, pairs[1].Kind);
            Equal("1|2", string.Join("|", pairs[1].OutputCandidates!));
            Equal(ComparisonPairKind.OutputOnly, pairs[3].Kind);
            Equal(ComparisonPairKind.InputOnly, pairs[4].Kind);
        });

        Test("Comparison character slices count Unicode scalar values with explicit origins", () =>
        {
            var data = ComparisonFixture(["x"], [new[] { "A😀e\u0301Z" }]);
            var reference = new ComparisonInputExpression(0);
            Equal("😀", ComparisonEngine.Evaluate(data, 0, new ComparisonSliceExpression(reference, 2, 1,
                ComparisonSliceUnit.UnicodeCodePoints, ComparisonIndexOrigin.One)).ExpectedValue);
            Equal("e\u0301", ComparisonEngine.Evaluate(data, 0, new ComparisonSliceExpression(reference, 2, 2,
                ComparisonSliceUnit.UnicodeCodePoints, ComparisonIndexOrigin.Zero)).ExpectedValue);
            Equal(ComparisonEvaluationStatus.CannotEvaluate, ComparisonEngine.Evaluate(data, 0,
                new ComparisonLiteralExpression("\ud800")).Status);
            Equal(ComparisonEvaluationStatus.CannotEvaluate, ComparisonEngine.Evaluate(data, 0,
                new ComparisonSliceExpression(reference, 0, 1, ComparisonSliceUnit.UnicodeCodePoints, ComparisonIndexOrigin.One)).Status);
        });

        Test("Comparison compound expressions expose source, slice, ordered concat, pad and branch trace", () =>
        {
            var data = ComparisonFixture(["id", "text", "flag"], [new[] { "7", "zABCq", "4" }]);
            ComparisonExpression expression = new ComparisonConcatExpression([
                new ComparisonPadExpression(new ComparisonInputExpression(0), 3, "0", ComparisonPadSide.Left),
                new ComparisonLiteralExpression("-"),
                new ComparisonSliceExpression(new ComparisonInputExpression(1), 2, 3, ComparisonSliceUnit.UnicodeCodePoints, ComparisonIndexOrigin.One),
                new ComparisonConditionalExpression(new ComparisonInputExpression(2), new ComparisonLiteralExpression("4"),
                    ComparisonEqualityMode.StringOrdinal, new ComparisonLiteralExpression("!"), new ComparisonLiteralExpression("?"))]);
            var evaluated = ComparisonEngine.Evaluate(data, 0, expression);
            Equal("007-ABC!", evaluated.ExpectedValue);
            Equal(true, evaluated.Trace.Any(step => step.Operation == "入力" && step.Value == "zABCq"));
            Equal(true, evaluated.Trace.Any(step => step.Operation == "文字切取" && step.Value == "ABC"));
            Equal(true, evaluated.Trace.Any(step => step.Operation == "条件" && step.Value == "成立"));
            Equal(true, evaluated.Trace.Any(step => step.Operation == "分岐" && step.Description == "一致側を選択"));
            Equal("7xx", ComparisonEngine.Evaluate(data, 0,
                new ComparisonPadExpression(new ComparisonInputExpression(0), 3, "x", ComparisonPadSide.Right)).ExpectedValue);
        });

        Test("Comparison exact decimal conditions avoid rounding and never trim or coerce strings silently", () =>
        {
            var data = ComparisonFixture(["x"], [new[] { "x" }]);
            ComparisonConditionalExpression Condition(string left, string right, ComparisonEqualityMode mode) =>
                new(new ComparisonLiteralExpression(left), new ComparisonLiteralExpression(right), mode,
                    new ComparisonLiteralExpression("yes"), new ComparisonLiteralExpression("no"));
            Equal("yes", ComparisonEngine.Evaluate(data, null, Condition("+0001.000", "1", ComparisonEqualityMode.ExactDecimal)).ExpectedValue);
            Equal("yes", ComparisonEngine.Evaluate(data, null, Condition("-0.00", "0", ComparisonEqualityMode.ExactDecimal)).ExpectedValue);
            Equal("no", ComparisonEngine.Evaluate(data, null, Condition("01", "1", ComparisonEqualityMode.StringOrdinal)).ExpectedValue);
            Equal("no", ComparisonEngine.Evaluate(data, null, Condition("0.12345678901234567890123456781",
                "0.12345678901234567890123456782", ComparisonEqualityMode.ExactDecimal)).ExpectedValue);
            Equal(ComparisonEvaluationStatus.CannotEvaluate, ComparisonEngine.Evaluate(data, null,
                Condition(" 1", "1", ComparisonEqualityMode.ExactDecimal)).Status);
        });

        Test("Comparison unsupported operators invalidate the entire expression including unselected branches", () =>
        {
            var data = ComparisonFixture(["x"], [new[] { "ok" }]);
            ComparisonExpression expression = new ComparisonConditionalExpression(new ComparisonLiteralExpression("A"),
                new ComparisonLiteralExpression("A"), ComparisonEqualityMode.StringOrdinal, new ComparisonLiteralExpression("ok"),
                new ComparisonUnsupportedExpression("未対応の日付式"));
            var result = ComparisonEngine.Evaluate(data, 0, expression);
            Equal(ComparisonEvaluationStatus.Unsupported, result.Status);
            Equal<string?>(null, result.ExpectedValue);
            Equal(false, result.Trace.Any(step => step.Operation == "分岐"));
            Equal(ComparisonEvaluationStatus.Unsupported, ComparisonEngine.Evaluate(data, 0,
                new ComparisonSliceExpression(new ComparisonLiteralExpression("ABC"), 0, 1,
                    ComparisonSliceUnit.Bytes, ComparisonIndexOrigin.Zero)).Status);
        });

        Test("Comparison invalid references, bounds and overlong padding yield reasons rather than empty values", () =>
        {
            var data = ComparisonFixture(["x"], [new[] { "001 " }]);
            foreach (var expression in new ComparisonExpression[]
            {
                new ComparisonInputExpression(9),
                new ComparisonPadExpression(new ComparisonInputExpression(0), 2, "0", ComparisonPadSide.Left),
                new ComparisonPadExpression(new ComparisonInputExpression(0), 8, "ab", ComparisonPadSide.Left),
                new ComparisonSliceExpression(new ComparisonInputExpression(0), 100, 2,
                    ComparisonSliceUnit.UnicodeCodePoints, ComparisonIndexOrigin.Zero)
            })
            {
                var result = ComparisonEngine.Evaluate(data, 0, expression);
                Equal(ComparisonEvaluationStatus.CannotEvaluate, result.Status);
                Equal<string?>(null, result.ExpectedValue);
                Equal(true, !string.IsNullOrWhiteSpace(result.Reason));
            }
            Equal(false, ComparisonEngine.Evaluate(data, 10, new ComparisonInputExpression(0)).Success);
            Equal(false, ComparisonEngine.Evaluate(data, null, new ComparisonInputExpression(0)).Success);
        });

        Test("Comparison result states separate direct values, calculated expectations, unmatched rows and ambiguity", () =>
        {
            var input = ComparisonFixture(["x"], [new[] { "7" }]);
            var output = ComparisonFixture(["y"], [new[] { "007" }]);
            var direct = new ComparisonFieldMapping(0, ComparisonMappingKind.Input, [new(0)]);
            Equal(ComparisonResultStatus.DirectValueDifferent, ComparisonEngine.Compare(input, output, new(0, 0), direct).Status);
            var calculated = direct with { Kind = ComparisonMappingKind.Expression,
                Expression = new ComparisonPadExpression(new ComparisonInputExpression(0), 3, "0", ComparisonPadSide.Left) };
            Equal(ComparisonResultStatus.CalculatedExpectedEqual, ComparisonEngine.Compare(input, output, new(0, 0), calculated).Status);
            Equal(ComparisonResultStatus.InputUnmapped, ComparisonEngine.Compare(input, output, new(null, 0, ComparisonPairKind.OutputOnly), direct).Status);
            Equal(ComparisonResultStatus.OutputUnmapped, ComparisonEngine.Compare(input, output, new(0, null, ComparisonPairKind.InputOnly), direct).Status);
            Equal(ComparisonResultStatus.FieldUnmapped, ComparisonEngine.Compare(input, output, new(0, 0),
                new(0, ComparisonMappingKind.Unmapped, [])).Status);
            Equal(ComparisonResultStatus.Ambiguous, ComparisonEngine.Compare(input, output, new(null, 0, ComparisonPairKind.Ambiguous, [0]), direct).Status);
            Equal(ComparisonResultStatus.CannotEvaluate, ComparisonEngine.Compare(input, output, new(0, 0),
                new(0, ComparisonMappingKind.Input, [new(0), new(0)])).Status);
            Equal(ComparisonResultStatus.CalculatedExpectedEqual, ComparisonEngine.Compare(input, output,
                new(null, 0, ComparisonPairKind.OutputOnly), new(0, ComparisonMappingKind.Fixed, [], new ComparisonLiteralExpression("007"))).Status);
        });

        Test("Comparison settings round-trip expressions, candidates and independent manual record pairs", () =>
        {
            var input = ComparisonFixture(["x"], [new[] { "A" }, new[] { "B" }]);
            var output = ComparisonFixture(["y"], [new[] { "B!" }]);
            var expression = new ComparisonConcatExpression([new ComparisonInputExpression(0), new ComparisonLiteralExpression("!")]);
            var settings = ComparisonSettingsStore.Create(input, output,
                [new(0, ComparisonMappingKind.Expression, [new(0)], expression, Note: "自由文はそのまま")], [new(1, 0)]);
            var path = Path.Combine(temporaryDirectory, "comparison-settings.json");
            ComparisonSettingsStore.Save(path, settings, input, output);
            var restored = ComparisonSettingsStore.Load(path, input, output);
            Equal(true, restored.CanApply);
            Equal<int?>(1, restored.Settings.RecordPairs.Single().InputRecordIndex);
            Equal("自由文はそのまま", restored.Settings.Mappings.Single().Note);
            Equal(ComparisonResultStatus.CalculatedExpectedEqual, ComparisonEngine.Compare(input, output,
                restored.Settings.RecordPairs[0], restored.Settings.Mappings[0]).Status);
            Equal(true, File.ReadAllText(path).Contains("\"$op\": \"concat\""));
        });

        Test("Comparison settings detect definition or row changes and never automatically apply old indices", () =>
        {
            var input = ComparisonFixture(["x"], [new[] { "A" }]);
            var output = ComparisonFixture(["y"], [new[] { "A" }]);
            var settings = ComparisonSettingsStore.Create(input, output, [new(0, ComparisonMappingKind.Input, [new(0)])], [new(0, 0)]);
            var path = Path.Combine(temporaryDirectory, "comparison-fingerprints.json");
            ComparisonSettingsStore.Save(path, settings, input, output);
            var renamed = ComparisonFixture(["renamed"], [new[] { "A" }]);
            var changedDefinition = ComparisonSettingsStore.Load(path, renamed, output);
            Equal(false, changedDefinition.CanApply);
            Equal(false, changedDefinition.DefinitionsMatch);
            Equal(true, changedDefinition.RecordsMatch);
            var changedRows = ComparisonSettingsStore.Load(path, ComparisonFixture(["x"], [new[] { "B" }]), output);
            Equal(false, changedRows.CanApply);
            Equal(false, changedRows.RecordsMatch);
            Equal(true, changedRows.Issues.Count > 0);
            Throws<InvalidDataException>(() => ComparisonSettingsStore.Save(path, settings, renamed, output));
        });

        Test("Comparison settings reject unknown schema, untrusted operator JSON, excess depth and out-of-bounds references", () =>
        {
            var input = ComparisonFixture(["x"], [new[] { "A" }]);
            var output = ComparisonFixture(["y"], [new[] { "A" }]);
            var settings = ComparisonSettingsStore.Create(input, output, [new(0, ComparisonMappingKind.Fixed, [], new ComparisonLiteralExpression("A"))], [new(0, 0)]);
            var path = Path.Combine(temporaryDirectory, "comparison-invalid-settings.json");
            ComparisonSettingsStore.Save(path, settings, input, output);
            var validJson = File.ReadAllText(path);
            File.WriteAllText(path, validJson.Replace("\"literal\"", "\"execute-csharp\""));
            Throws<InvalidDataException>(() => ComparisonSettingsStore.Load(path, input, output));
            File.WriteAllText(path, validJson.Replace("\"SchemaVersion\": 1", "\"SchemaVersion\": 999"));
            Throws<InvalidDataException>(() => ComparisonSettingsStore.Load(path, input, output));
            Throws<InvalidDataException>(() => ComparisonSettingsStore.Create(input, output,
                [new(0, ComparisonMappingKind.Input, [new(9)])], [new(0, 0)]));
            Throws<InvalidDataException>(() => ComparisonSettingsStore.Create(input, output,
                [new(0, ComparisonMappingKind.Fixed, [], new ComparisonInputExpression(0))], [new(0, 0)]));
            Throws<InvalidDataException>(() => ComparisonSettingsStore.Create(input, output,
                [new(0, ComparisonMappingKind.Fixed, [], new ComparisonLiteralExpression("A"))], [new(0, 0), new(0, 0)]));
            ComparisonExpression deep = new ComparisonLiteralExpression("A");
            for (var index = 0; index < ComparisonExpressionEvaluator.MaxDepth + 1; index++)
                deep = new ComparisonConcatExpression([deep]);
            Equal(ComparisonEvaluationStatus.CannotEvaluate, ComparisonEngine.Evaluate(input, 0, deep).Status);
            Throws<InvalidDataException>(() => ComparisonSettingsStore.Create(input, output,
                [new(0, ComparisonMappingKind.Expression, [], deep)], [new(0, 0)]));
        });

        Test("Comparison persisted metadata must name exactly the inputs and source kinds evaluated by the tree", () =>
        {
            var input = ComparisonFixture(["a", "b"], [new[] { "A", "B" }]);
            var output = ComparisonFixture(["result"], [new[] { "BA" }]);
            var expression = new ComparisonConcatExpression([
                new ComparisonInputExpression(1), new ComparisonInputExpression(0), new ComparisonInputExpression(1)]);
            Equal("1|0", string.Join("|", ComparisonEngine.GetInputReferences(expression).Select(reference => reference.FieldOrdinal)));
            Throws<InvalidDataException>(() => ComparisonSettingsStore.Create(input, output,
                [new(0, ComparisonMappingKind.Expression, [new(0)], expression)], [new(0, 0)]));
            Throws<InvalidDataException>(() => ComparisonSettingsStore.Create(input, output,
                [new(0, ComparisonMappingKind.Expression, [new(0), new(1, ComparisonSourceKind.FixedStorageText)], expression)], [new(0, 0)]));
            var settings = ComparisonSettingsStore.Create(input, output,
                [new(0, ComparisonMappingKind.Expression, ComparisonEngine.GetInputReferences(expression), expression)], [new(0, 0)]);
            Equal(2, settings.Mappings[0].Inputs.Count);
        });

        Test("Comparison ambiguous row pairs allow only input-independent expressions and never evaluate a candidate input", () =>
        {
            var input = ComparisonFixture(["source"], [new[] { "FIXED" }, new[] { "different" }]);
            var output = ComparisonFixture(["result"], [new[] { "FIXED" }]);
            var ambiguous = new ComparisonRecordPair(null, 0, ComparisonPairKind.Ambiguous, [0, 1], [0]);
            var fixedMapping = new ComparisonFieldMapping(0, ComparisonMappingKind.Fixed, [], new ComparisonLiteralExpression("FIXED"));
            Equal(ComparisonResultStatus.CalculatedExpectedEqual, ComparisonEngine.Compare(input, output, ambiguous, fixedMapping).Status);
            var concat = new ComparisonConcatExpression([new ComparisonLiteralExpression("FIX"), new ComparisonLiteralExpression("ED")]);
            var concatResult = ComparisonEngine.Compare(input, output, ambiguous,
                new(0, ComparisonMappingKind.Expression, [], concat));
            Equal(ComparisonResultStatus.CalculatedExpectedEqual, concatResult.Status);
            Equal("FIXED", concatResult.ExpectedValue);
            Equal(true, concatResult.Evaluation!.Trace.Any(step => step.Operation == "結合"));
            var referenced = ComparisonEngine.Compare(input, output, ambiguous,
                new(0, ComparisonMappingKind.Input, [new(0)]));
            Equal(ComparisonResultStatus.Ambiguous, referenced.Status);
            Equal<string?>(null, referenced.ExpectedValue);
            Equal<ComparisonEvaluation?>(null, referenced.Evaluation);
            Equal(ComparisonResultStatus.Ambiguous, ComparisonEngine.Compare(input, output, new(0, 0),
                fixedMapping with { Kind = ComparisonMappingKind.Ambiguous }).Status);
            var unsupported = ComparisonEngine.Compare(input, output, ambiguous,
                new(0, ComparisonMappingKind.Expression, [], new ComparisonUnsupportedExpression("未対応の原式")));
            Equal(ComparisonResultStatus.Unsupported, unsupported.Status);
            Equal<string?>(null, unsupported.ExpectedValue);
            ComparisonExpression deep = new ComparisonLiteralExpression("FIXED");
            for (var index = 0; index < ComparisonExpressionEvaluator.MaxDepth + 1; index++)
                deep = new ComparisonConcatExpression([deep]);
            Equal(ComparisonResultStatus.CannotEvaluate, ComparisonEngine.Compare(input, output, ambiguous,
                new(0, ComparisonMappingKind.Expression, [], deep)).Status);
        });
        Test("Comparison ten thousand duplicate keys share complete read-only candidates without selecting a first match", () =>
        {
            const int count = 10_000;
            var input = ComparisonFixture(["key"], Enumerable.Repeat(new[] { "DUP" }, count));
            var output = ComparisonFixture(["key"], Enumerable.Repeat(new[] { "DUP" }, count));
            var pairs = ComparisonEngine.PairByKeys(input, output, [0], [0]);
            Equal(count, pairs.Count);
            Equal(true, pairs.All(pair => pair.Kind == ComparisonPairKind.Ambiguous && pair.InputRecordIndex is null));
            Equal(true, pairs.Select((pair, index) => pair.OutputRecordIndex == index).All(value => value));
            Equal(count, pairs[0].InputCandidates!.Count); Equal(count, pairs[0].OutputCandidates!.Count);
            Equal(0, pairs[0].InputCandidates![0]); Equal(count - 1, pairs[0].InputCandidates![^1]);
            foreach (var pair in pairs)
            {
                Equal(true, ReferenceEquals(pairs[0].InputCandidates, pair.InputCandidates));
                Equal(true, ReferenceEquals(pairs[0].OutputCandidates, pair.OutputCandidates));
            }
            Throws<NotSupportedException>(() => ((IList<int>)pairs[0].InputCandidates!)[0] = 99);
        });
        Test("Comparison candidate totals are rejected before any huge virtual candidate collection is inspected", () =>
        {
            var input = ComparisonFixture(["key"], [new[] { "A" }, new[] { "A" }, new[] { "A" }]);
            var output = ComparisonFixture(["key"], [new[] { "A" }, new[] { "A" }, new[] { "A" }]);
            var candidates = new ComparisonCountOnlyCandidates(ComparisonSettingsStore.MaxRecordPairs);
            var pairs = Enumerable.Range(0, 3).Select(index => new ComparisonRecordPair(null, index, ComparisonPairKind.Ambiguous, candidates, candidates)).ToArray();
            Equal(true, 6L * candidates.Count > ComparisonSettingsStore.MaxTotalCandidateReferences);
            try
            {
                ComparisonSettingsStore.Validate(input, output, [], pairs);
                throw new Exception("Expected excessive candidates to be rejected");
            }
            catch (InvalidDataException exception)
            {
                Equal(true, exception.Message.Contains("キー項目を追加", StringComparison.Ordinal));
            }
            Throws<InvalidDataException>(() => ComparisonSettingsStore.Create(input, output, [], pairs));
        });
        Test("Comparison settings clone and save enforce the UTF8 byte limit while preserving prior output and leaving no temporary files", () =>
        {
            var input = ComparisonFixture(["source"], [new[] { "A" }]);
            var output = ComparisonFixture(["result"], [new[] { "A" }]);
            static ComparisonFieldMapping Mapping(int characters)
            {
                // The settings encoder escapes each BMP marker as six ASCII bytes. Two literals
                // give a near-limit document without exceeding the per-literal character bound.
                var value = new string('漢', characters);
                return new(0, ComparisonMappingKind.Fixed, [],
                    new ComparisonConcatExpression([new ComparisonLiteralExpression(value), new ComparisonLiteralExpression(value)]));
            }
            var near = ComparisonSettingsStore.Create(input, output, [Mapping(333_000)], []);
            Throws<NotSupportedException>(() => ((IList<ComparisonFieldMapping>)near.Mappings).Clear());
            Throws<NotSupportedException>(() => ((IList<ComparisonExpression>)((ComparisonConcatExpression)near.Mappings[0].Expression!).Parts).Clear());
            var path = Path.Combine(temporaryDirectory, "comparison-bounded-settings.json");
            ComparisonSettingsStore.Save(path, near, input, output);
            var before = File.ReadAllBytes(path);
            Equal(true, before.Length > ComparisonSettingsStore.MaxFileBytes - 10_000);
            Equal(true, before.Length <= ComparisonSettingsStore.MaxFileBytes);
            var overMappings = new[] { Mapping(334_000) };
            ComparisonSettingsStore.Validate(input, output, overMappings, []); // Pure validation does not clone or serialize.
            Throws<InvalidDataException>(() => ComparisonSettingsStore.Create(input, output, overMappings, []));
            var over = near with { Mappings = overMappings };
            Throws<InvalidDataException>(() => ComparisonSettingsStore.Save(path, over, input, output));
            Equal(true, before.SequenceEqual(File.ReadAllBytes(path)));
            Equal(0, Directory.GetFiles(temporaryDirectory, Path.GetFileName(path) + ".*.tmp").Length);
            var rejectedPath = Path.Combine(temporaryDirectory, "comparison-bounded-rejected.json");
            Throws<InvalidDataException>(() => ComparisonSettingsStore.Save(rejectedPath, over, input, output));
            Equal(false, File.Exists(rejectedPath));
            Equal(0, Directory.GetFiles(temporaryDirectory, Path.GetFileName(rejectedPath) + ".*.tmp").Length);
        });
        Test("Comparison unconfirmed row pairs never evaluate input references and ambiguous mappings retain obtainable output", () =>
        {
            var input = ComparisonFixture(["source"], [new[] { "OUT" }]);
            var output = ComparisonFixture(["result"], [new[] { "OUT" }]);
            var pair = new ComparisonRecordPair(null, 0, ComparisonPairKind.Unconfirmed);
            var direct = new ComparisonFieldMapping(0, ComparisonMappingKind.Input, [new(0)]);
            ComparisonSettingsStore.Validate(input, output, [direct], [pair]);
            var unconfirmed = ComparisonEngine.Compare(input, output, pair, direct);
            Equal(ComparisonResultStatus.InputUnmapped, unconfirmed.Status);
            Equal<string?>(null, unconfirmed.ExpectedValue);
            Equal<ComparisonEvaluation?>(null, unconfirmed.Evaluation);
            Equal("OUT", unconfirmed.OutputValue);
            Equal(true, unconfirmed.Reason!.Contains("組が未確定", StringComparison.Ordinal));
            var independent = ComparisonEngine.Compare(input, output, pair,
                new(0, ComparisonMappingKind.Fixed, [], new ComparisonLiteralExpression("OUT")));
            Equal(ComparisonResultStatus.CalculatedExpectedEqual, independent.Status);
            Equal("OUT", independent.ExpectedValue);
            var ambiguous = direct with { Kind = ComparisonMappingKind.Ambiguous };
            var ambiguousResult = ComparisonEngine.Compare(input, output, pair, ambiguous);
            Equal(ComparisonResultStatus.Ambiguous, ambiguousResult.Status);
            Equal("OUT", ambiguousResult.OutputValue);
            Equal<ComparisonEvaluation?>(null, ambiguousResult.Evaluation);
            var unavailable = ComparisonEngine.Compare(input, output, pair, ambiguous with { OutputSourceKind = ComparisonSourceKind.RawFixedBytes });
            Equal(ComparisonResultStatus.CannotEvaluate, unavailable.Status);
            Equal<string?>(null, unavailable.OutputValue);
            Equal(ComparisonResultStatus.CannotEvaluate, ComparisonEngine.Compare(input, output, pair,
                ambiguous with { OutputFieldOrdinal = 9 }).Status);
            Throws<InvalidDataException>(() => ComparisonSettingsStore.Validate(input, output, [direct], [new(0, 0, ComparisonPairKind.Unconfirmed)]));
            Throws<InvalidDataException>(() => ComparisonSettingsStore.Validate(input, output, [direct], [new(0, null, ComparisonPairKind.Unconfirmed)]));
        });
    }

    private sealed class ComparisonCountOnlyCandidates(int count) : IReadOnlyList<int>
    {
        public int Count => count;
        public int this[int index] => throw new InvalidOperationException("Candidate budget must be checked before indexing");
        public IEnumerator<int> GetEnumerator() => throw new InvalidOperationException("Candidate budget must be checked before enumeration");
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private static ComparisonDataSet ComparisonFixture(string[] names, IEnumerable<IEnumerable<string>> rows) =>
        ComparisonDataSet.FromRecords(names.Select(name => new FieldDefinition(name, FieldDataType.Text, 32)), rows);

    private static (string Definition, string Data) ComparisonPaths(string label) =>
        (Path.Combine(temporaryDirectory, $"comparison-{label}-definition.csv"), Path.Combine(temporaryDirectory, $"comparison-{label}-data.dat"));

    private static ComparisonDataSet ComparisonFixedFixture(string label, string definition, byte[] bytes,
        DataEncodingProfile encoding, RecordSeparatorMode separator = RecordSeparatorMode.CrLfOrLf)
    {
        var paths = ComparisonPaths(label);
        File.WriteAllText(paths.Definition, $"項目名,定義\r\nsource,{definition}\r\n", new UTF8Encoding(false));
        File.WriteAllBytes(paths.Data, bytes);
        return ComparisonDataSet.Load(paths.Definition, paths.Data,
            new(SeparatorMode: separator, DataEncoding: encoding), DefinitionTextEncoding.Utf8);
    }
}
