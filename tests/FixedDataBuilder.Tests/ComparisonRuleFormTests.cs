using System.Text;
using FixedDataBuilder;

internal static partial class Program
{
    private static void RunComparisonRuleFormTests(string repo)
    {
        Test("Comparison rule editor distinguishes an unselected input from an explicit empty fixed value", () =>
        {
            var input = ComparisonFixture(["source"], [new[] { "A" }]);
            var output = ComparisonFixture(["result"], [new[] { "" }]);
            using var form = new ComparisonRuleForm(input, output, 0);
            Equal(-1, Get<ComboBox>(form, "inputFieldCombo").SelectedIndex);
            Equal(false, (bool)Invoke(form, "TryBuildAndPreview", false)!);
            Equal<ComparisonFieldMapping?>(null, form.Mapping);
            Equal(true, Get<Label>(form, "validationLabel").Text.Length > 0);
            Get<ComboBox>(form, "operationCombo").SelectedIndex = 1;
            Get<TextBox>(form, "literalText").Text = "";
            Equal(true, (bool)Invoke(form, "TryBuildAndPreview", true)!);
            Equal(ComparisonMappingKind.Fixed, form.Mapping!.Kind);
            Equal("", ((ComparisonLiteralExpression)form.Mapping.Expression!).Value);
        });

        Test("Comparison rule editor keeps duplicate candidates unselected and a sole candidate explicit until confirmation", () =>
        {
            var input = ComparisonFixture(["duplicate", "duplicate", "unique"], [new[] { "A", "B", "C" }]);
            var output = ComparisonFixture(["duplicate", "unique"], [new[] { "A", "C" }]);
            var suggestions = ComparisonEngine.SuggestFieldMappings(input, output);
            using var ambiguous = new ComparisonRuleForm(input, output, 0, suggestion: suggestions[0]);
            Equal(-1, Get<ComboBox>(ambiguous, "inputFieldCombo").SelectedIndex);
            Equal<ComparisonFieldMapping?>(null, ambiguous.Mapping);
            using var unique = new ComparisonRuleForm(input, output, 1, suggestion: suggestions[1]);
            Equal(2, Get<ComboBox>(unique, "inputFieldCombo").SelectedIndex);
            Equal<ComparisonFieldMapping?>(null, unique.Mapping);
            Equal(true, (bool)Invoke(unique, "TryBuildAndPreview", true)!);
            Equal(2, unique.Mapping!.Inputs.Single().FieldOrdinal);
        });

        Test("Comparison rule editor builds Unicode slice controls and preserves blank padding and source data", () =>
        {
            var input = ComparisonFixture(["text"], [new[] { "A😀 B " }]);
            var output = ComparisonFixture(["slice"], [new[] { "😀 " }]);
            using var form = new ComparisonRuleForm(input, output, 0);
            Get<ComboBox>(form, "operationCombo").SelectedIndex = 2;
            Get<ComboBox>(form, "inputFieldCombo").SelectedIndex = 0;
            Get<NumericUpDown>(form, "startNumber").Value = 2;
            Get<NumericUpDown>(form, "lengthNumber").Value = 2;
            Equal(true, (bool)Invoke(form, "TryBuildAndPreview", false)!);
            var mapping = (ComparisonFieldMapping)Invoke(form, "BuildMapping")!;
            var expression = (ComparisonSliceExpression)mapping.Expression!;
            Equal(ComparisonSliceUnit.UnicodeCodePoints, expression.Unit);
            Equal(ComparisonIndexOrigin.One, expression.Origin);
            Equal("😀 ", ComparisonEngine.Evaluate(input, 0, expression).ExpectedValue);
            Equal("A😀 B ", input.Records[0][0]);
            Get<NumericUpDown>(form, "startNumber").Value = 99;
            Equal(false, (bool)Invoke(form, "TryBuildAndPreview", false)!);
            Equal<ComparisonFieldMapping?>(null, form.Mapping);
        });

        Test("Comparison rule byte slicing selects original fixed bytes and blocks a split UTF8 character", () =>
        {
            var input = ComparisonFixedFixture("rule-raw-utf8", "X(6)", Encoding.UTF8.GetBytes("あA  "),
                DataEncodingProfile.Utf8WithNationalText, RecordSeparatorMode.None);
            var output = ComparisonFixture(["result"], [new[] { "A  " }]);
            using var form = new ComparisonRuleForm(input, output, 0);
            Get<ComboBox>(form, "operationCombo").SelectedIndex = 2;
            Get<ComboBox>(form, "inputFieldCombo").SelectedIndex = 0;
            Get<ComboBox>(form, "sliceUnitCombo").SelectedIndex = 1;
            Get<NumericUpDown>(form, "startNumber").Value = 4;
            Get<NumericUpDown>(form, "lengthNumber").Value = 3;
            Equal(true, (bool)Invoke(form, "TryBuildAndPreview", false)!);
            var mapping = (ComparisonFieldMapping)Invoke(form, "BuildMapping")!;
            Equal(ComparisonSourceKind.RawFixedBytes, mapping.Inputs.Single().SourceKind);
            Equal("A  ", ComparisonEngine.Evaluate(input, 0, mapping.Expression!).ExpectedValue);
            Get<NumericUpDown>(form, "startNumber").Value = 2;
            Get<NumericUpDown>(form, "lengthNumber").Value = 1;
            Equal(false, (bool)Invoke(form, "TryBuildAndPreview", false)!);
            Equal(true, Get<Label>(form, "validationLabel").Text.Length > 0);
        });

        Test("Comparison rule GUI creates ordered sliced concatenation and records exactly its source references", () =>
        {
            var input = ComparisonFixture(["a", "b"], [new[] { "ABCD", "7" }]);
            var output = ComparisonFixture(["result"], [new[] { "7BC" }]);
            using var form = new ComparisonRuleForm(input, output, 0);
            Get<ComboBox>(form, "operationCombo").SelectedIndex = 3;
            Get<ComboBox>(form, "inputFieldCombo").SelectedIndex = 0;
            Get<CheckBox>(form, "concatSliceCheck").Checked = true;
            Get<NumericUpDown>(form, "startNumber").Value = 2;
            Get<NumericUpDown>(form, "lengthNumber").Value = 2;
            Invoke(form, "TryAddConcatPart");
            Get<ComboBox>(form, "inputFieldCombo").SelectedIndex = 1;
            Get<CheckBox>(form, "concatSliceCheck").Checked = false;
            Invoke(form, "TryAddConcatPart");
            Invoke(form, "MovePart", -1);
            Equal(true, (bool)Invoke(form, "TryBuildAndPreview", false)!);
            var mapping = (ComparisonFieldMapping)Invoke(form, "BuildMapping")!;
            Equal("7BC", ComparisonEngine.Evaluate(input, 0, mapping.Expression!).ExpectedValue);
            Equal("1|0", string.Join("|", mapping.Inputs.Select(reference => reference.FieldOrdinal)));
            ComparisonSettingsStore.Create(input, output, [mapping], [new(0, 0)]);
        });

        Test("Comparison rule GUI builds exact fill and rejects overlong input or multiple fill code points", () =>
        {
            var input = ComparisonFixture(["source"], [new[] { "7" }]);
            var output = ComparisonFixture(["result"], [new[] { "007" }]);
            using var form = new ComparisonRuleForm(input, output, 0);
            Get<ComboBox>(form, "operationCombo").SelectedIndex = 4;
            Get<ComboBox>(form, "inputFieldCombo").SelectedIndex = 0;
            Get<NumericUpDown>(form, "targetNumber").Value = 3;
            Equal(true, (bool)Invoke(form, "TryBuildAndPreview", false)!);
            var mapping = (ComparisonFieldMapping)Invoke(form, "BuildMapping")!;
            Equal("007", ComparisonEngine.Evaluate(input, 0, mapping.Expression!).ExpectedValue);
            Get<TextBox>(form, "fillText").Text = "ab";
            Equal(false, (bool)Invoke(form, "TryBuildAndPreview", false)!);
            Get<TextBox>(form, "fillText").Text = " ";
            Get<NumericUpDown>(form, "targetNumber").Value = 0;
            Equal(false, (bool)Invoke(form, "TryBuildAndPreview", false)!);
        });

        Test("Comparison rule GUI conditions retain an input branch and use the chosen string or decimal equality", () =>
        {
            var input = ComparisonFixture(["source"], [new[] { "04" }]);
            var output = ComparisonFixture(["result"], [new[] { "1" }]);
            using var form = new ComparisonRuleForm(input, output, 0);
            Get<ComboBox>(form, "operationCombo").SelectedIndex = 5;
            Get<ComboBox>(form, "inputFieldCombo").SelectedIndex = 0;
            Get<TextBox>(form, "compareText").Text = "4";
            ((TextBox)Descendants(form).First(control => control.Name == "ComparisonTrueBranchValue")).Text = "1";
            ((ComboBox)Descendants(form).First(control => control.Name == "ComparisonFalseBranchKind")).SelectedIndex = 1;
            var stringMapping = (ComparisonFieldMapping)Invoke(form, "BuildMapping")!;
            Equal("04", ComparisonEngine.Evaluate(input, 0, stringMapping.Expression!).ExpectedValue);
            Get<ComboBox>(form, "equalityCombo").SelectedIndex = 1;
            Equal(true, (bool)Invoke(form, "TryBuildAndPreview", false)!);
            var decimalMapping = (ComparisonFieldMapping)Invoke(form, "BuildMapping")!;
            Equal("1", ComparisonEngine.Evaluate(input, 0, decimalMapping.Expression!).ExpectedValue);
            Equal(1, decimalMapping.Inputs.Count);
        });

        Test("Comparison rule editor restores compound rules without rewriting original comments", () =>
        {
            var input = ComparisonFixture(["a", "b"], [new[] { "ABCD", "07" }]);
            var fields = new[] { new FieldDefinition("result", FieldDataType.Text, 8, Comment: "自由文の原式を保持") };
            var output = ComparisonDataSet.FromRecords(fields, [new[] { "BC07" }]);
            ComparisonExpression expression = new ComparisonConcatExpression([
                new ComparisonSliceExpression(new ComparisonInputExpression(0), 2, 2, ComparisonSliceUnit.UnicodeCodePoints, ComparisonIndexOrigin.One),
                new ComparisonInputExpression(1)]);
            var existing = new ComparisonFieldMapping(0, ComparisonMappingKind.Expression,
                ComparisonEngine.GetInputReferences(expression), expression, Note: "比較メモ");
            using var form = new ComparisonRuleForm(input, output, 0, existing);
            Equal(3, Get<ComboBox>(form, "operationCombo").SelectedIndex);
            Equal(2, Get<ListBox>(form, "concatParts").Items.Count);
            Equal("比較メモ", Get<TextBox>(form, "noteText").Text);
            var mapping = (ComparisonFieldMapping)Invoke(form, "BuildMapping")!;
            Equal("BC07", ComparisonEngine.Evaluate(input, 0, mapping.Expression!).ExpectedValue);
            Equal("自由文の原式を保持", output.Fields[0].Comment);
        });

        Test("Comparison rule JSON helper refuses arbitrary operators and preserves complex supported trees", () =>
        {
            var input = ComparisonFixture(["a"], [new[] { "7" }]);
            var output = ComparisonFixture(["result"], [new[] { "07!" }]);
            ComparisonExpression expression = new ComparisonConcatExpression([
                new ComparisonPadExpression(new ComparisonInputExpression(0), 2, "0", ComparisonPadSide.Left),
                new ComparisonLiteralExpression("!")]);
            using var form = new ComparisonRuleForm(input, output, 0,
                new(0, ComparisonMappingKind.Expression, ComparisonEngine.GetInputReferences(expression), expression));
            Equal(6, Get<ComboBox>(form, "operationCombo").SelectedIndex);
            Equal(true, (bool)Invoke(form, "TryBuildAndPreview", false)!);
            var mapping = (ComparisonFieldMapping)Invoke(form, "BuildMapping")!;
            Equal("07!", ComparisonEngine.Evaluate(input, 0, mapping.Expression!).ExpectedValue);
            Get<TextBox>(form, "jsonText").Text = "{\"$op\":\"execute-csharp\",\"Value\":\"anything\"}";
            Equal(false, (bool)Invoke(form, "TryBuildAndPreview", false)!);
            Equal<ComparisonFieldMapping?>(null, form.Mapping);
            Get<TextBox>(form, "jsonText").Text = "{\"$op\":\"unsupported\",\"Description\":\"未対応の原式\"}";
            Equal(false, (bool)Invoke(form, "TryBuildAndPreview", false)!);
        });

        Test("Comparison rule empty concatenation round-trips in the normal GUI as an input-independent expression", () =>
        {
            var input = ComparisonFixture(["source"], Array.Empty<string[]>());
            var output = ComparisonFixture(["result"], [new[] { "" }]);
            var existing = new ComparisonFieldMapping(0, ComparisonMappingKind.Expression, [],
                new ComparisonConcatExpression([]), Note: "空文字を結合規則として保持");
            using var form = new ComparisonRuleForm(input, output, 0, existing);
            Equal(3, Get<ComboBox>(form, "operationCombo").SelectedIndex);
            Equal(0, Get<ListBox>(form, "concatParts").Items.Count);
            Equal(true, Descendants(form).OfType<Label>().Any(label => label.Text.Contains("0個") && label.Text.Contains("空文字")));
            Equal(true, (bool)Invoke(form, "TryBuildAndPreview", false)!);
            var preview = Get<TextBox>(form, "previewText").Text;
            Equal(true, preview.Contains("期待値: []"));
            Equal(true, preview.Contains("結合: []"));
            Equal(true, (bool)Invoke(form, "TryBuildAndPreview", true)!);
            var restored = form.Mapping!;
            Equal(ComparisonMappingKind.Expression, restored.Kind);
            Equal(0, restored.Inputs.Count);
            Equal("空文字を結合規則として保持", restored.Note);
            Equal(true, restored.Expression is ComparisonConcatExpression { Parts.Count: 0 });
            Equal(ComparisonResultStatus.CalculatedExpectedEqual, ComparisonEngine.Compare(input, output,
                new(null, 0, ComparisonPairKind.OutputOnly), restored).Status);
        });
    }
}
