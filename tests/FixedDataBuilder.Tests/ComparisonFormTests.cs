using System.Reflection;
using System.Text;
using System.Text.Json;
using FixedDataBuilder;

internal static partial class Program
{
    private static void RunComparisonFormTests(string repo)
    {
        _ = repo;
        Test("Comparison window starts with a read-only output snapshot and no implicit input or row mapping", () =>
        {
            var sourceRows = new List<List<string>> { new() { "0001", " base " } };
            var output = ComparisonDataSet.FromRecords([new("ID", FieldDataType.Text, 8), new("VALUE", FieldDataType.Text, 20)], sourceRows, new(DataFileFormat.Csv));
            using var form = new ComparisonForm(output);
            var grid = Get<DataGridView>(form, "comparisonGrid");
            Equal(2, grid.Rows.Count);
            Equal(true, grid.ReadOnly);
            Equal(false, grid.AllowUserToAddRows);
            Equal(false, grid.AllowUserToDeleteRows);
            Equal(false, Descendants(form).OfType<SplitContainer>().Any());
            Equal(false, Get<Button>(form, "editMappingButton").Enabled);
            Equal(false, Get<Button>(form, "confirmPairButton").Enabled);
            Equal("0001", ComparisonCell(form, 0, "OutputValue"));
            Equal(true, Get<TextBox>(form, "outputSource").Text.Contains("元ファイルのバイトなし", StringComparison.Ordinal));
            Equal("入力・行未対応", ComparisonCell(form, 0, "ComparisonState"));
            Equal(" base ", sourceRows[0][1]);
        });
        Test("Comparison candidates remain unconfirmed even for equal values until field and manual row mapping are confirmed", () =>
        {
            var input = ComparisonUiData(["ID"], ["0001"]);
            var output = ComparisonUiData(["ID"], ["0001"]);
            using var form = new ComparisonForm(output);
            Invoke(form, "SetInputDataSet", input);
            Equal("候補（未確定）", ComparisonCell(form, 0, "ComparisonState"));
            Equal(true, ComparisonCell(form, 0, "InputReferences").StartsWith("候補:", StringComparison.Ordinal));
            Equal("", ComparisonCell(form, 0, "ExpectedValue"));
            Equal(true, Get<TextBox>(form, "traceText").Text.Contains("完全一致", StringComparison.Ordinal));
            Invoke(form, "ConfirmCurrentPair");
            Equal("候補（未確定）", ComparisonCell(form, 0, "ComparisonState"));
            Invoke(form, "ApplyMapping", new ComparisonFieldMapping(0, ComparisonMappingKind.Input, [new(0)]));
            Equal("元値一致", ComparisonCell(form, 0, "ComparisonState"));
            Equal("0001", ComparisonCell(form, 0, "ExpectedValue"));
            Invoke(form, "ClearSelectedMapping");
            Equal("候補（未確定）", ComparisonCell(form, 0, "ComparisonState"));
            Equal("0001", input.Records[0][0]);
            Equal("0001", output.Records[0][0]);
        });
        Test("Comparison input and output row selectors and previous/next buttons move independently with different counts", () =>
        {
            using var form = new ComparisonForm(ComparisonUiData(["ID"], ["out1"], ["out2"]));
            Invoke(form, "SetInputDataSet", ComparisonUiData(["ID"], ["in1"], ["in2"], ["in3"]));
            var inputNumber = Get<NumericUpDown>(form, "inputRecordNumber");
            var outputNumber = Get<NumericUpDown>(form, "outputRecordNumber");
            Equal(3M, inputNumber.Maximum); Equal(2M, outputNumber.Maximum);
            inputNumber.Value = 3;
            Equal(1M, outputNumber.Value);
            Equal("out1", ComparisonCell(form, 0, "OutputValue"));
            Equal(true, ComparisonCell(form, 0, "InputValues").Contains("in3", StringComparison.Ordinal));
            form.Show(); Application.DoEvents();
            Descendants(form).OfType<Button>().Single(button => button.Name == "ComparisonInputRecordPrevious").PerformClick();
            Equal(2M, inputNumber.Value); Equal(1M, outputNumber.Value);
            Descendants(form).OfType<Button>().Single(button => button.Name == "ComparisonOutputRecordNext").PerformClick();
            Equal(2M, inputNumber.Value); Equal(2M, outputNumber.Value);
            Equal("out2", ComparisonCell(form, 0, "OutputValue"));
        });
        Test("Comparison direct mapping requires the current row pair and does not follow a changed row implicitly", () =>
        {
            using var form = new ComparisonForm(ComparisonUiData(["ID"], ["B"], ["A"]));
            Invoke(form, "SetInputDataSet", ComparisonUiData(["ID"], ["A"], ["B"]));
            Invoke(form, "ApplyMapping", new ComparisonFieldMapping(0, ComparisonMappingKind.Input, [new(0)]));
            Equal("入力・行未対応", ComparisonCell(form, 0, "ComparisonState"));
            var inputNumber = Get<NumericUpDown>(form, "inputRecordNumber");
            inputNumber.Value = 2;
            Invoke(form, "ConfirmCurrentPair");
            Equal("元値一致", ComparisonCell(form, 0, "ComparisonState"));
            inputNumber.Value = 1;
            Equal("入力・行未対応", ComparisonCell(form, 0, "ComparisonState"));
            Equal(true, Get<Label>(form, "pairSummary").Text.Contains("現在の入力行とは異なります", StringComparison.Ordinal));
            Invoke(form, "FollowCurrentPair");
            Equal(2M, inputNumber.Value);
            Equal("元値一致", ComparisonCell(form, 0, "ComparisonState"));
            Get<NumericUpDown>(form, "outputRecordNumber").Value = 2;
            Equal(2M, inputNumber.Value);
            Equal("入力・行未対応", ComparisonCell(form, 0, "ComparisonState"));
            Invoke(form, "ConfirmCurrentPair");
            Equal("元値不一致", ComparisonCell(form, 0, "ComparisonState"));
            Invoke(form, "ClearCurrentPair");
            Equal("入力・行未対応", ComparisonCell(form, 0, "ComparisonState"));
        });
        Test("Comparison selected-row details expose source, character slicing origin and intermediate expected value", () =>
        {
            using var form = new ComparisonForm(ComparisonUiData(["RENAMED", "KEEP"], ["BC", "unchanged"]));
            Invoke(form, "SetInputDataSet", ComparisonUiData(["SOURCE"], ["ABCDE"]));
            var slice = new ComparisonSliceExpression(new ComparisonInputExpression(0), 1, 2, ComparisonSliceUnit.UnicodeCodePoints, ComparisonIndexOrigin.Zero);
            Invoke(form, "ApplyMapping", new ComparisonFieldMapping(0, ComparisonMappingKind.Expression, ComparisonEngine.GetInputReferences(slice), slice));
            Invoke(form, "ConfirmCurrentPair");
            Equal("期待値一致", ComparisonCell(form, 0, "ComparisonState"));
            Equal("BC", ComparisonCell(form, 0, "ExpectedValue"));
            var trace = Get<TextBox>(form, "traceText").Text;
            Equal(true, trace.Contains("入力No.1 SOURCE: [ABCDE]", StringComparison.Ordinal));
            Equal(true, trace.Contains("文字切取: [BC]", StringComparison.Ordinal));
            Equal(true, trace.Contains("起点=0", StringComparison.Ordinal));
            var grid = Get<DataGridView>(form, "comparisonGrid");
            grid.CurrentCell = grid[0, 1];
            Equal(true, Get<TextBox>(form, "traceText").Text.Contains("出力No.2 KEEP", StringComparison.Ordinal));
            Equal("項目未対応", ComparisonCell(form, 1, "ComparisonState"));
        });
        Test("Comparison conditional traces show branch selection and unsupported expressions never pass", () =>
        {
            using var form = new ComparisonForm(ComparisonUiData(["OUT"], ["X"]));
            Invoke(form, "SetInputDataSet", ComparisonUiData(["IN"], ["A"]));
            var condition = new ComparisonConditionalExpression(new ComparisonInputExpression(0), new ComparisonLiteralExpression("A"),
                ComparisonEqualityMode.StringOrdinal, new ComparisonLiteralExpression("X"), new ComparisonLiteralExpression("Y"));
            Invoke(form, "ApplyMapping", new ComparisonFieldMapping(0, ComparisonMappingKind.Expression, ComparisonEngine.GetInputReferences(condition), condition));
            Invoke(form, "ConfirmCurrentPair");
            Equal("期待値一致", ComparisonCell(form, 0, "ComparisonState"));
            Equal(true, Get<TextBox>(form, "traceText").Text.Contains("分岐: [X]", StringComparison.Ordinal));
            var unsupported = condition with { WhenNotEqual = new ComparisonUnsupportedExpression("未確認の業務式") };
            Invoke(form, "ApplyMapping", new ComparisonFieldMapping(0, ComparisonMappingKind.Expression, ComparisonEngine.GetInputReferences(unsupported), unsupported));
            Equal("未対応の式", ComparisonCell(form, 0, "ComparisonState"));
            Equal("", ComparisonCell(form, 0, "ExpectedValue"));
            Equal(true, Get<TextBox>(form, "traceText").Text.Contains("未確認の業務式", StringComparison.Ordinal));
        });
        Test("Comparison key pairing keeps duplicate candidates ambiguous and only follows a unique pair on request", () =>
        {
            using var form = new ComparisonForm(ComparisonUiData(["ID"], ["A"], ["B"], ["C"]));
            Invoke(form, "SetInputDataSet", ComparisonUiData(["ID"], ["A"], ["A"], ["B"]));
            Invoke(form, "ApplyMapping", new ComparisonFieldMapping(0, ComparisonMappingKind.Input, [new(0)]));
            var inputNumber = Get<NumericUpDown>(form, "inputRecordNumber");
            inputNumber.Value = 2;
            Invoke(form, "ApplyKeyPairs", new int[] { 0 }, new int[] { 0 });
            Equal(2M, inputNumber.Value);
            Equal("曖昧・未確定", ComparisonCell(form, 0, "ComparisonState"));
            Equal(true, Get<Label>(form, "pairSummary").Text.Contains("1, 2", StringComparison.Ordinal));
            Equal(false, Get<Button>(form, "followPairButton").Enabled);
            Get<NumericUpDown>(form, "outputRecordNumber").Value = 2;
            Equal(2M, inputNumber.Value);
            Equal("入力・行未対応", ComparisonCell(form, 0, "ComparisonState"));
            Invoke(form, "FollowCurrentPair");
            Equal(3M, inputNumber.Value);
            Equal("元値一致", ComparisonCell(form, 0, "ComparisonState"));
            Get<NumericUpDown>(form, "outputRecordNumber").Value = 3;
            Equal(true, ComparisonCell(form, 0, "ComparisonReason").Contains("対応する入力行がありません", StringComparison.Ordinal));
        });
        Test("Comparison settings round trip confirmed mappings and row pairs without modifying datasets", () =>
        {
            var input = ComparisonUiData(["IN"], [" 01 "]);
            var output = ComparisonUiData(["OUT"], [" 01 "]);
            using var form = new ComparisonForm(output);
            Invoke(form, "SetInputDataSet", input);
            Invoke(form, "ApplyMapping", new ComparisonFieldMapping(0, ComparisonMappingKind.Input, [new(0)], Note: "確認済み"));
            Invoke(form, "ConfirmCurrentPair");
            var path = Path.Combine(temporaryDirectory, "comparison-ui-settings.json");
            Invoke(form, "SaveSettingsToPath", path);
            Invoke(form, "ClearSelectedMapping"); Invoke(form, "ClearCurrentPair");
            Equal(true, (bool)Invoke(form, "LoadSettingsFromPath", path)!);
            Equal("元値一致", ComparisonCell(form, 0, "ComparisonState"));
            Equal(true, Get<TextBox>(form, "traceText").Text.Contains("確認済み", StringComparison.Ordinal));
            Equal(" 01 ", input.Records[0][0]); Equal(" 01 ", output.Records[0][0]);
        });
        Test("Comparison settings with changed definition or record fingerprints are not applied and preserve current confirmed mapping", () =>
        {
            var output = ComparisonUiData(["OUT"], ["A"]);
            var originalInput = ComparisonUiData(["IN"], ["A"]);
            using var form = new ComparisonForm(output);
            Invoke(form, "SetInputDataSet", originalInput);
            Invoke(form, "ApplyMapping", new ComparisonFieldMapping(0, ComparisonMappingKind.Input, [new(0)], Note: "old"));
            Invoke(form, "ConfirmCurrentPair");
            var path = Path.Combine(temporaryDirectory, "comparison-ui-mismatch.json");
            Invoke(form, "SaveSettingsToPath", path);
            foreach (var replacement in new[] { ComparisonUiData(["CHANGED"], ["A"]), ComparisonUiData(["IN"], ["B"]) })
            {
                Invoke(form, "SetInputDataSet", replacement);
                Invoke(form, "ApplyMapping", new ComparisonFieldMapping(0, ComparisonMappingKind.Input, [new(0)], Note: "current"));
                Invoke(form, "ConfirmCurrentPair");
                Equal(false, (bool)Invoke(form, "LoadSettingsFromPath", path)!);
                Equal("current", Get<Dictionary<int, ComparisonFieldMapping>>(form, "mappings")[0].Note);
                Equal(1, Get<List<ComparisonRecordPair>>(form, "recordPairs").Count);
                Equal(true, Get<Label>(form, "statusText").Text.Contains("適用していません", StringComparison.Ordinal));
                Equal(true, Get<TextBox>(form, "traceText").Text.Contains("指紋", StringComparison.Ordinal));
            }
        });
        Test("Comparison replacing one side resets confirmed rules and pairs and preserves the other side row selection", () =>
        {
            using var form = new ComparisonForm(ComparisonUiData(["ID"], ["A"], ["B"]));
            var input = ComparisonUiData(["ID"], ["A"], ["B"], ["C"]);
            Invoke(form, "SetInputDataSet", input);
            Get<NumericUpDown>(form, "inputRecordNumber").Value = 2;
            Invoke(form, "ApplyMapping", new ComparisonFieldMapping(0, ComparisonMappingKind.Input, [new(0)]));
            Invoke(form, "ConfirmCurrentPair");
            Invoke(form, "SetOutputDataSet", ComparisonUiData(["ID"], ["B"]));
            Equal(2M, Get<NumericUpDown>(form, "inputRecordNumber").Value);
            Equal(0, Get<Dictionary<int, ComparisonFieldMapping>>(form, "mappings").Count);
            Equal(0, Get<List<ComparisonRecordPair>>(form, "recordPairs").Count);
            Equal("候補（未確定）", ComparisonCell(form, 0, "ComparisonState"));
            Equal("B", ComparisonCell(form, 0, "OutputValue"));
            Equal("B", input.Records[1][0]);
        });
        Test("Comparison load uses original fixed bytes and exposes their absence on the initial snapshot", () =>
        {
            var definition = Path.Combine(temporaryDirectory, "comparison-ui-byte-definition.csv");
            var dataPath = Path.Combine(temporaryDirectory, "comparison-ui-byte-input.dat");
            File.WriteAllText(definition, "項目名,定義\r\nID,X(4)\r\n", new UTF8Encoding(false));
            File.WriteAllBytes(dataPath, Encoding.ASCII.GetBytes("AB  \r\n"));
            var loaded = ComparisonDataSet.Load(definition, dataPath, new(DataFileFormat.FixedLength));
            using var form = new ComparisonForm(ComparisonUiData(["OUT"], ["  "]));
            Invoke(form, "SetInputDataSet", loaded);
            var slice = new ComparisonSliceExpression(new ComparisonInputExpression(0, ComparisonSourceKind.RawFixedBytes), 2, 2, ComparisonSliceUnit.Bytes, ComparisonIndexOrigin.Zero);
            Invoke(form, "ApplyMapping", new ComparisonFieldMapping(0, ComparisonMappingKind.Expression, ComparisonEngine.GetInputReferences(slice), slice));
            Invoke(form, "ConfirmCurrentPair");
            Equal("期待値一致", ComparisonCell(form, 0, "ComparisonState"));
            Equal("  ", ComparisonCell(form, 0, "ExpectedValue"));
            Equal(true, Get<TextBox>(form, "traceText").Text.Contains("元固定長", StringComparison.Ordinal));
            Equal(true, Get<TextBox>(form, "traceText").Text.Contains("バイト切取", StringComparison.Ordinal));
            Equal(true, Get<TextBox>(form, "outputSource").Text.Contains("元ファイルのバイトなし", StringComparison.Ordinal));
            Invoke(form, "SetOutputDataSet", loaded);
            Equal(true, Get<TextBox>(form, "outputSource").Text.Contains("元固定長バイト保持", StringComparison.Ordinal));
        });
        Test("Comparison evidence preserves confirmed trace and refuses overwriting original sources", () =>
        {
            var sourcePath = Path.Combine(temporaryDirectory, "comparison-ui-original.csv");
            File.WriteAllText(sourcePath, "original synthetic source", new UTF8Encoding(false));
            var output = ComparisonDataSet.FromRecords([new("OUT", FieldDataType.Text, 4)], [new[] { "A" }], new(DataFileFormat.Csv), dataPath: sourcePath);
            using var form = new ComparisonForm(output);
            Invoke(form, "SetInputDataSet", ComparisonUiData(["IN"], ["A"]));
            Invoke(form, "ApplyMapping", new ComparisonFieldMapping(0, ComparisonMappingKind.Input, [new(0)]));
            Equal(true, Get<Button>(form, "evidenceButton").Enabled);
            Invoke(form, "ConfirmCurrentPair");
            Equal(true, Get<Button>(form, "evidenceButton").Enabled);
            var path = Path.Combine(temporaryDirectory, "comparison-ui-evidence.json");
            Invoke(form, "SaveEvidenceToPath", path);
            using var json = JsonDocument.Parse(File.ReadAllText(path));
            Equal(1, json.RootElement.GetProperty("inputRecordNumber").GetInt32());
            Equal(1, json.RootElement.GetProperty("outputRecordNumber").GetInt32());
            Equal(false, json.RootElement.GetProperty("output").GetProperty("HasSourceBytes").GetBoolean());
            Throws<TargetInvocationException>(() => Invoke(form, "SaveEvidenceToPath", sourcePath));
            Throws<TargetInvocationException>(() => Invoke(form, "SaveSettingsToPath", sourcePath));
            Equal("original synthetic source", File.ReadAllText(sourcePath));
        });
        Test("Comparison definition codec survives loading and reopening independently on each side", () =>
        {
            var definition = Path.Combine(temporaryDirectory, "comparison-ui-explicit-definition-codec.csv");
            var data = Path.Combine(temporaryDirectory, "comparison-ui-explicit-definition-data.csv");
            File.WriteAllText(definition, "項目名,定義\r\nNAME\u301c,X(4)\r\n", DefinitionFileText.ForEncoding(DefinitionTextEncoding.ShiftJis));
            File.WriteAllText(data, "ABCD\r\n", new UTF8Encoding(false));
            using var loader = ComparisonUiLoader();
            Get<ComboBox>(loader, "format").SelectedIndex = 1;
            Get<ComboBox>(loader, "definitionEncoding").SelectedIndex = (int)DefinitionTextEncoding.ShiftJis;
            Get<TextBox>(loader, "definitionPath").Text = definition;
            Get<TextBox>(loader, "dataPath").Text = data;
            Invoke(loader, "LoadData");
            var loaded = (ComparisonDataSet)loader.GetType().GetProperty("DataSet")!.GetValue(loader)!;
            Equal(DefinitionTextEncoding.ShiftJis, loaded.DefinitionEncoding);
            Equal("NAME\u301c", loaded.Fields[0].Name);
            var output = ComparisonDataSet.FromRecords([new("OUT", FieldDataType.Text, 4)], [new[] { "ABCD" }],
                new(DataFileFormat.Csv), definitionEncoding: DefinitionTextEncoding.Utf8);
            using var inputReopened = ComparisonUiLoader("入力", loaded);
            using var outputReopened = ComparisonUiLoader("出力", output);
            Equal((int)DefinitionTextEncoding.ShiftJis, Get<ComboBox>(inputReopened, "definitionEncoding").SelectedIndex);
            Equal((int)DefinitionTextEncoding.Utf8, Get<ComboBox>(outputReopened, "definitionEncoding").SelectedIndex);
            Invoke(inputReopened, "DetectInitialEncoding", false);
            Invoke(inputReopened, "LoadData");
            var reread = (ComparisonDataSet)inputReopened.GetType().GetProperty("DataSet")!.GetValue(inputReopened)!;
            Equal(DefinitionTextEncoding.ShiftJis, reread.DefinitionEncoding);
            Equal(loaded.DefinitionFingerprint, reread.DefinitionFingerprint);
            Get<ComboBox>(inputReopened, "definitionEncoding").SelectedIndex = (int)DefinitionTextEncoding.Ms932;
            Equal((int)DefinitionTextEncoding.Utf8, Get<ComboBox>(outputReopened, "definitionEncoding").SelectedIndex);
            Equal(DefinitionTextEncoding.ShiftJis, loaded.DefinitionEncoding);
            Equal(DefinitionTextEncoding.Auto, ComparisonUiData(["AUTO"], ["A"]).DefinitionEncoding);
        });
        Test("Comparison oversized PIC length stays an inline load error and leaves both current sides and correspondence intact", () =>
        {
            var input = ComparisonUiData(["IN"], ["A"]);
            var output = ComparisonUiData(["OUT"], ["A"]);
            using var comparison = new ComparisonForm(output);
            Invoke(comparison, "SetInputDataSet", input);
            Invoke(comparison, "ApplyMapping", new ComparisonFieldMapping(0, ComparisonMappingKind.Input, [new(0)], Note: "preserve"));
            Invoke(comparison, "ConfirmCurrentPair");
            var mappings = Get<Dictionary<int, ComparisonFieldMapping>>(comparison, "mappings").ToArray();
            var pairs = Get<List<ComparisonRecordPair>>(comparison, "recordPairs").ToArray();
            var definition = Path.Combine(temporaryDirectory, "comparison-ui-overflow-definition.cpy");
            var data = Path.Combine(temporaryDirectory, "comparison-ui-overflow-data.dat");
            File.WriteAllText(definition, "       01 TEST-RECORD.\r\n          05 TOO-LONG PIC X(99999999999).\r\n", new UTF8Encoding(false));
            File.WriteAllText(data, "A", new UTF8Encoding(false));
            foreach (var (side, current) in new[] { ("入力", input), ("出力", output) })
            {
                using var loader = ComparisonUiLoader(side, current);
                Get<ComboBox>(loader, "format").SelectedIndex = 0;
                Get<TextBox>(loader, "definitionPath").Text = definition;
                Get<TextBox>(loader, "dataPath").Text = data;
                Invoke(loader, "DetectInitialEncoding", false);
                Equal(true, Get<Label>(loader, "detectionLabel").Text.StartsWith("判別できません", StringComparison.Ordinal));
                Invoke(loader, "LoadData");
                Equal(null, loader.GetType().GetProperty("DataSet")!.GetValue(loader));
                Equal(DialogResult.None, loader.DialogResult);
                Equal(true, Get<Label>(loader, "error").Text.Length > 0);
                Equal(true, ReferenceEquals(input, Get<ComparisonDataSet?>(comparison, "input")));
                Equal(true, ReferenceEquals(output, Get<ComparisonDataSet>(comparison, "output")));
                Equal(true, mappings.SequenceEqual(Get<Dictionary<int, ComparisonFieldMapping>>(comparison, "mappings")));
                Equal(true, pairs.SequenceEqual(Get<List<ComparisonRecordPair>>(comparison, "recordPairs")));
                Equal("元値一致", ComparisonCell(comparison, 0, "ComparisonState"));
            }
        });
        Test("Comparison CSV loading presets the detected codec but loading preserves a later manual override", () =>
        {
            var definition = Path.Combine(temporaryDirectory, "comparison-ui-detect-csv-definition.csv");
            var dataPath = Path.Combine(temporaryDirectory, "comparison-ui-detect-ms932.csv");
            File.WriteAllText(definition, "項目名,定義\r\nTEXT,X(40)\r\n", new UTF8Encoding(false));
            File.WriteAllText(dataPath, "①髙\r\n", JapaneseEncodings.ForCsv(CsvTextEncoding.ShiftJis));
            using var loader = ComparisonUiLoader();
            Get<ComboBox>(loader, "format").SelectedIndex = 1;
            Get<TextBox>(loader, "definitionPath").Text = definition;
            Get<TextBox>(loader, "dataPath").Text = dataPath;
            Invoke(loader, "DetectInitialEncoding", false);
            var codec = Get<ComboBox>(loader, "csvEncoding");
            Equal((int)CsvTextEncoding.ShiftJis, codec.SelectedIndex);
            Equal(true, Get<Label>(loader, "detectionLabel").Text.Contains("MS932候補", StringComparison.Ordinal));
            codec.SelectedIndex = (int)CsvTextEncoding.StrictShiftJis;
            Invoke(loader, "LoadData");
            Equal((int)CsvTextEncoding.StrictShiftJis, codec.SelectedIndex);
            Equal(null, loader.GetType().GetProperty("DataSet")!.GetValue(loader));
            Equal(true, Get<Label>(loader, "error").Text.Length > 0);
            codec.SelectedIndex = (int)CsvTextEncoding.ShiftJis;
            Invoke(loader, "LoadData");
            var loaded = (ComparisonDataSet)loader.GetType().GetProperty("DataSet")!.GetValue(loader)!;
            Equal(CsvTextEncoding.ShiftJis, loaded.Profile.CsvEncoding);
            Equal("①髙", loaded.Records[0][0]);
        });
        Test("Comparison fixed loading detects X independently of N and keeps a manually selected codec for ASCII", () =>
        {
            var definition = Path.Combine(temporaryDirectory, "comparison-ui-detect-fixed-definition.csv");
            var dataPath = Path.Combine(temporaryDirectory, "comparison-ui-detect-fixed.dat");
            File.WriteAllText(definition, "項目名,定義\r\nTEXT,X(6)\r\nNATIONAL,N(2)\r\n", new UTF8Encoding(false));
            FieldDefinition[] fields = [new("TEXT", FieldDataType.Text, 6), new("NATIONAL", FieldDataType.FullWidthText, 2, NationalByteWidth: 4)];
            FixedLengthDataWriter.Write(dataPath, fields, [["東京", "大阪"]], RecordSeparatorMode.CrLfOrLf,
                DataEncodingProfile.Utf8WithNationalText, NationalTextEncoding.Utf32);
            using var loader = ComparisonUiLoader();
            Get<ComboBox>(loader, "national").SelectedIndex = (int)NationalTextEncoding.Utf32;
            Get<ComboBox>(loader, "definitionEncoding").SelectedIndex = (int)DefinitionTextEncoding.Utf8;
            Get<TextBox>(loader, "definitionPath").Text = definition;
            Get<TextBox>(loader, "dataPath").Text = dataPath;
            Invoke(loader, "DetectInitialEncoding", false);
            Equal((int)DataEncodingProfile.Utf8WithNationalText, Get<ComboBox>(loader, "textEncoding").SelectedIndex);
            Equal((int)NationalTextEncoding.Utf32, Get<ComboBox>(loader, "national").SelectedIndex);
            Equal((int)DefinitionTextEncoding.Utf8, Get<ComboBox>(loader, "definitionEncoding").SelectedIndex);
            FixedLengthDataWriter.Write(dataPath, fields, [["ABCDEF", "大阪"]], RecordSeparatorMode.CrLfOrLf,
                DataEncodingProfile.ShiftJis, NationalTextEncoding.Utf32);
            Get<ComboBox>(loader, "textEncoding").SelectedIndex = (int)DataEncodingProfile.StrictShiftJis;
            Invoke(loader, "DetectInitialEncoding", true);
            Equal((int)DataEncodingProfile.StrictShiftJis, Get<ComboBox>(loader, "textEncoding").SelectedIndex);
            Equal((int)NationalTextEncoding.Utf32, Get<ComboBox>(loader, "national").SelectedIndex);
            Invoke(loader, "LoadData");
            var loaded = (ComparisonDataSet)loader.GetType().GetProperty("DataSet")!.GetValue(loader)!;
            Equal(DataEncodingProfile.StrictShiftJis, loaded.Profile.DataEncoding);
            Equal(NationalTextEncoding.Utf32, loaded.Profile.NationalEncoding);
            Equal("ABCDEF", loaded.Records[0][0]); Equal("大阪", loaded.Records[0][1]);
        });
        Test("Comparison malformed settings and malformed data display validation errors and retain the current comparison", () =>
        {
            using var form = new ComparisonForm(ComparisonUiData(["OUT"], ["A"]));
            var input = ComparisonUiData(["IN"], ["A"]);
            Invoke(form, "SetInputDataSet", input);
            Invoke(form, "ApplyMapping", new ComparisonFieldMapping(0, ComparisonMappingKind.Input, [new(0)], Note: "keep"));
            Invoke(form, "ConfirmCurrentPair");
            var malformed = Path.Combine(temporaryDirectory, "comparison-ui-malformed-settings.json");
            File.WriteAllText(malformed, "{", new UTF8Encoding(false));
            Invoke(form, "LoadSettingsWithFeedback", malformed);
            Equal(true, Get<Label>(form, "statusText").Text.Contains("JSON", StringComparison.Ordinal));
            Equal("keep", Get<Dictionary<int, ComparisonFieldMapping>>(form, "mappings")[0].Note);
            Equal(1, Get<List<ComparisonRecordPair>>(form, "recordPairs").Count);
            Equal("元値一致", ComparisonCell(form, 0, "ComparisonState"));
            Equal("A", input.Records[0][0]);
            var definition = Path.Combine(temporaryDirectory, "comparison-ui-malformed-definition.csv");
            var data = Path.Combine(temporaryDirectory, "comparison-ui-malformed-data.csv");
            File.WriteAllText(definition, "項目名,定義\r\nVALUE,X(4)\r\n", new UTF8Encoding(false));
            File.WriteAllText(data, "A,B\r\n", new UTF8Encoding(false));
            using var loader = ComparisonUiLoader();
            Get<ComboBox>(loader, "format").SelectedIndex = 1;
            Get<TextBox>(loader, "definitionPath").Text = definition;
            Get<TextBox>(loader, "dataPath").Text = data;
            Invoke(loader, "LoadData");
            Equal(null, loader.GetType().GetProperty("DataSet")!.GetValue(loader));
            Equal(true, Get<Label>(loader, "error").Text.Contains("列数", StringComparison.Ordinal));
        });
        Test("Comparison input-free constants evaluate without row pairing and remain independent of ambiguous keys", () =>
        {
            using var form = new ComparisonForm(ComparisonUiData(["ID", "DIRECT"], ["X", "X"]));
            Invoke(form, "SetInputDataSet", ComparisonUiData(["ID"], ["X"], ["X"]));
            Invoke(form, "ApplyMapping", new ComparisonFieldMapping(0, ComparisonMappingKind.Fixed, [], new ComparisonLiteralExpression("X")));
            Invoke(form, "ApplyMapping", new ComparisonFieldMapping(1, ComparisonMappingKind.Input, [new(0)]));
            Equal("期待値一致", ComparisonCell(form, 0, "ComparisonState"));
            Equal("入力・行未対応", ComparisonCell(form, 1, "ComparisonState"));
            Equal(true, Get<TextBox>(form, "traceText").Text.Contains("入力参照なし", StringComparison.Ordinal));
            var inputFree = new ComparisonConcatExpression([new ComparisonLiteralExpression("X"), new ComparisonLiteralExpression("")]);
            Invoke(form, "ApplyMapping", new ComparisonFieldMapping(0, ComparisonMappingKind.Expression, [], inputFree));
            Equal("期待値一致", ComparisonCell(form, 0, "ComparisonState"));
            Equal(true, Get<TextBox>(form, "traceText").Text.Contains("結合", StringComparison.Ordinal));
            Invoke(form, "ApplyKeyPairs", new int[] { 0 }, new int[] { 0 });
            Equal("期待値一致", ComparisonCell(form, 0, "ComparisonState"));
            Equal("曖昧・未確定", ComparisonCell(form, 1, "ComparisonState"));
            Equal(0, Get<List<ComparisonRecordPair>>(form, "recordPairs").Count(pair => pair.Kind == ComparisonPairKind.Manual));
            var path = Path.Combine(temporaryDirectory, "comparison-ui-constant-ambiguous-evidence.json");
            Invoke(form, "SaveEvidenceToPath", path);
            using var json = JsonDocument.Parse(File.ReadAllText(path));
            var results = json.RootElement.GetProperty("results");
            Equal("CalculatedExpectedEqual", results[0].GetProperty("result").GetProperty("Status").GetString());
            Equal("Ambiguous", results[1].GetProperty("result").GetProperty("Status").GetString());
            Invoke(form, "ConfirmCurrentPair");
            Get<NumericUpDown>(form, "inputRecordNumber").Value = 2;
            Equal("入力・行未対応", ComparisonCell(form, 1, "ComparisonState"));
            var differentSelectionPath = Path.Combine(temporaryDirectory, "comparison-ui-different-selection-evidence.json");
            Invoke(form, "SaveEvidenceToPath", differentSelectionPath);
            using var differentSelection = JsonDocument.Parse(File.ReadAllText(differentSelectionPath));
            Equal(JsonValueKind.Null, differentSelection.RootElement.GetProperty("inputRecordNumber").ValueKind);
            Equal("InputUnmapped", differentSelection.RootElement.GetProperty("results")[1].GetProperty("result").GetProperty("Status").GetString());
            Equal("Unconfirmed", differentSelection.RootElement.GetProperty("evaluationPair").GetProperty("Kind").GetString());
            Equal(0, differentSelection.RootElement.GetProperty("configuredPair").GetProperty("InputRecordIndex").GetInt32());
            Equal(2, differentSelection.RootElement.GetProperty("inspectedInputRecordNumber").GetInt32());
            Equal(false, differentSelection.RootElement.GetProperty("currentPairConfirmed").GetBoolean());
        });
        Test("Comparison evidence retains ambiguous field candidates and their unconfirmed previews", () =>
        {
            using var form = new ComparisonForm(ComparisonUiData(["A"], ["one"]));
            Invoke(form, "SetInputDataSet", ComparisonUiData(["A", "A"], ["one", "two"]));
            Equal("曖昧・未確定", ComparisonCell(form, 0, "ComparisonState"));
            var path = Path.Combine(temporaryDirectory, "comparison-candidate-evidence.json");
            Invoke(form, "SaveEvidenceToPath", path);
            using var json = JsonDocument.Parse(File.ReadAllBytes(path));
            var result = json.RootElement.GetProperty("results")[0];
            Equal("Ambiguous", result.GetProperty("result").GetProperty("Status").GetString());
            Equal("曖昧・未確定", result.GetProperty("displayState").GetString());
            Equal(2, result.GetProperty("suggestion").GetProperty("Candidates").GetArrayLength());
            Equal("two", result.GetProperty("candidatePreviews")[1].GetProperty("source").GetProperty("Value").GetString());
            Equal(JsonValueKind.Null, result.GetProperty("result").GetProperty("ExpectedValue").ValueKind);
        });
        Test("Comparison CSV alias source failures stay visible in candidate previews and evidence", () =>
        {
            FieldDefinition[] fields = [new("BASE", FieldDataType.Text, 4), new("ALIAS", FieldDataType.Text, 4, RedefinesName: "BASE")];
            var aliased = ComparisonDataSet.FromRecords(fields, new[] { new[] { "BASE", "invented" } }, new(DataFileFormat.Csv));
            using (var form = new ComparisonForm(ComparisonUiData(["ALIAS"], ["anything"])))
            {
                Invoke(form, "SetInputDataSet", aliased);
                Equal(true, ComparisonCell(form, 0, "InputValues").Contains("取得不可", StringComparison.Ordinal));
                Equal(false, ComparisonCell(form, 0, "InputValues").Contains("invented", StringComparison.Ordinal));
            }
            using var outputForm = new ComparisonForm(aliased);
            Invoke(outputForm, "SetInputDataSet", ComparisonUiData(["ALIAS"], ["anything"]));
            Equal("算出不可", ComparisonCell(outputForm, 1, "ComparisonState"));
            var path = Path.Combine(temporaryDirectory, "comparison-alias-error-evidence.json");
            Invoke(outputForm, "SaveEvidenceToPath", path);
            using var json = JsonDocument.Parse(File.ReadAllBytes(path));
            Equal("CannotEvaluate", json.RootElement.GetProperty("results")[1].GetProperty("result").GetProperty("Status").GetString());
            Equal("算出不可", json.RootElement.GetProperty("results")[1].GetProperty("displayState").GetString());
        });
        Test("Explicit ambiguous mapping stays ambiguous before row pairing in both UI and evidence", () =>
        {
            using var form = new ComparisonForm(ComparisonUiData(["A"], ["value"]));
            Invoke(form, "SetInputDataSet", ComparisonUiData(["A"], ["value"]));
            Invoke(form, "ApplyMapping", new ComparisonFieldMapping(0, ComparisonMappingKind.Ambiguous, [new(0)]));
            Equal("曖昧・未確定", ComparisonCell(form, 0, "ComparisonState"));
            var path = Path.Combine(temporaryDirectory, "comparison-explicit-ambiguous-evidence.json");
            Invoke(form, "SaveEvidenceToPath", path);
            using var json = JsonDocument.Parse(File.ReadAllBytes(path));
            Equal("Ambiguous", json.RootElement.GetProperty("results")[0].GetProperty("result").GetProperty("Status").GetString());
            Equal("value", json.RootElement.GetProperty("results")[0].GetProperty("result").GetProperty("OutputValue").GetString());
            Equal("曖昧・未確定", json.RootElement.GetProperty("results")[0].GetProperty("displayState").GetString());
        });
        Test("Manual comparison pairing removes the stale input-only classification", () =>
        {
            using var form = new ComparisonForm(ComparisonUiData(["ID"], ["X"], ["NEW"]));
            Invoke(form, "SetInputDataSet", ComparisonUiData(["ID"], ["ONLY"], ["X"]));
            Invoke(form, "ApplyKeyPairs", new[] { 0 }, new[] { 0 });
            var pairs = Get<List<ComparisonRecordPair>>(form, "recordPairs");
            Equal(true, pairs.Any(pair => pair.Kind == ComparisonPairKind.InputOnly && pair.InputRecordIndex == 0));
            Get<NumericUpDown>(form, "inputRecordNumber").Value = 1;
            Get<NumericUpDown>(form, "outputRecordNumber").Value = 2;
            Invoke(form, "ConfirmCurrentPair");
            Equal(false, pairs.Any(pair => pair.Kind == ComparisonPairKind.InputOnly && pair.InputRecordIndex == 0));
            Equal(true, pairs.Any(pair => pair.Kind == ComparisonPairKind.Manual && pair.InputRecordIndex == 0 && pair.OutputRecordIndex == 1));
        });
    }

    private static ComparisonDataSet ComparisonUiData(string[] names, params string[][] records) => ComparisonDataSet.FromRecords(
        names.Select(name => new FieldDefinition(name, FieldDataType.Text, 40)), records, new ComparisonDataProfile(DataFileFormat.Csv));
    private static string ComparisonCell(ComparisonForm form, int row, string column) => Convert.ToString(Get<DataGridView>(form, "comparisonGrid")[column, row].Value) ?? "";
    private static Form ComparisonUiLoader(string side = "入力", ComparisonDataSet? current = null) =>
        (Form)Activator.CreateInstance(typeof(ComparisonForm).GetNestedType("ComparisonLoadForm", BindingFlags.NonPublic)!, new object?[] { side, current })!;
}
