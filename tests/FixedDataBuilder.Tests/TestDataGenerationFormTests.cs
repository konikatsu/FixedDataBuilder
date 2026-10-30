using FixedDataBuilder;

internal static partial class Program
{
    private static void RunTestDataGenerationFormTests(string repo)
    {
        _ = repo; // All fixtures in these tests are synthetic and stay in memory.
        Test("Generation dialog starts with only the selected physical field and five independent defaults", () =>
        {
            using var form = NewGenerationForm(initialFieldIndex: 0);
            var settings = (TestDataGenerationSettings)Invoke(form, "BuildSettings")!;
            Equal(true, settings.TargetFieldIndexes.SequenceEqual(new[] { 0 }));
            Equal(GenerationRequirement.NumericLeadingZero | GenerationRequirement.NumericAllZero
                | GenerationRequirement.NumericFullLength | GenerationRequirement.NumericMaximum
                | GenerationRequirement.TextMaximum, settings.Requirements);
            Equal(4, settings.NormalRecordCount);
            Equal(1m, Get<NumericUpDown>(form, "normalCount").Minimum);
            Equal(10000m, Get<NumericUpDown>(form, "normalCount").Maximum);
            Equal(3, Get<CheckedListBox>(form, "targetFields").Items.Count);
            Equal(17, Get<Dictionary<GenerationRequirement, CheckBox>>(form, "requirementChecks").Count);
            Equal(true, Get<Button>(form, "execute").Enabled);
            Equal<TestDataGenerationResult?>(null, form.GeneratedResult);
        });
        Test("Generation dialog never substitutes another target for REDEFINES or an absent initial field", () =>
        {
            foreach (var selected in new[] { -1, 1, 100 })
            {
                using var form = NewGenerationForm(initialFieldIndex: selected);
                Equal(0, Get<CheckedListBox>(form, "targetFields").CheckedItems.Count);
                Equal(false, Get<Button>(form, "execute").Enabled);
                Equal(true, Get<TextBox>(form, "issues").Text.Contains("対象項目を選択"));
                Equal(false, Get<CheckedListBox>(form, "targetFields").Items.Cast<object>().Any(item => item.ToString()!.Contains("別表示")));
            }
        });
        Test("Generation dialog baseline selector is one based and retains the selected row in its result", () =>
        {
            using var form = NewGenerationForm(initialRecordIndex: 1);
            Equal(2m, Get<NumericUpDown>(form, "baselineRecord").Value);
            Equal(1, ((TestDataGenerationSettings)Invoke(form, "BuildSettings")!).BaselineRecordIndex);
            Invoke(form, "ExecuteGeneration");
            Equal(DialogResult.OK, form.DialogResult);
            Equal(1, form.GeneratedResult!.Settings.BaselineRecordIndex);
            Equal(true, form.GeneratedResult.Records.All(record => record[2] == "beta" && record[3] == "20261002"));
            using var clamped = NewGenerationForm(initialRecordIndex: 500);
            Equal(2m, Get<NumericUpDown>(clamped, "baselineRecord").Value);
        });
        Test("Changing baseline and targets updates analysis immediately and generation uses the same settings", () =>
        {
            using var form = NewGenerationForm(initialFieldIndex: -1, initialRecordIndex: 1);
            var targets = Get<CheckedListBox>(form, "targetFields");
            targets.SetItemChecked(1, true); // Physical field index 2; REDEFINES is absent from the list.
            Get<NumericUpDown>(form, "baselineRecord").Value = 1;
            var settings = (TestDataGenerationSettings)Invoke(form, "BuildSettings")!;
            Equal(true, settings.TargetFieldIndexes.SequenceEqual(new[] { 2 }));
            Equal(true, Get<TestDataGenerationPlan>(form, "plan").CasesByField.ContainsKey(2));
            Equal(true, Get<Button>(form, "execute").Enabled);
            Invoke(form, "ExecuteGeneration");
            var result = form.GeneratedResult!;
            Equal(0, result.Settings.BaselineRecordIndex);
            Equal(true, result.Settings.TargetFieldIndexes.SequenceEqual(new[] { 2 }));
            Equal(settings.Requirements, result.Settings.Requirements);
            Equal(settings.OutputOptions, result.Settings.OutputOptions);
            Equal(true, result.Records.All(record => record[0] == "00000123" && record[3] == "20261001"));
        });
        Test("Select all and clear change only physical targets and do not change requirements", () =>
        {
            using var form = NewGenerationForm();
            var original = ((TestDataGenerationSettings)Invoke(form, "BuildSettings")!).Requirements;
            Invoke(form, "SetAllTargets", true);
            var all = (TestDataGenerationSettings)Invoke(form, "BuildSettings")!;
            Equal(true, all.TargetFieldIndexes.SequenceEqual(new[] { 0, 2, 3 }));
            Equal(original, all.Requirements);
            Invoke(form, "SetAllTargets", false);
            Equal(0, ((TestDataGenerationSettings)Invoke(form, "BuildSettings")!).TargetFieldIndexes.Count);
            Equal(false, Get<Button>(form, "execute").Enabled);
        });
        Test("Every generation requirement can be selected and cleared individually", () =>
        {
            using var form = NewGenerationForm();
            SetGenerationRequirements(form, GenerationRequirement.None);
            foreach (var pair in Get<Dictionary<GenerationRequirement, CheckBox>>(form, "requirementChecks"))
            {
                pair.Value.Checked = true;
                Equal(pair.Key, ((TestDataGenerationSettings)Invoke(form, "BuildSettings")!).Requirements);
                Equal(true, pair.Value.Checked); // Inapplicable requirements remain visible and selected.
                pair.Value.Checked = false;
            }
            Equal(false, Get<Button>(form, "execute").Enabled);
            Equal(true, Get<TextBox>(form, "issues").Text.Contains("1つ以上"));
        });
        Test("Insufficient normal count disables execute, explains the needed count and never raises the input", () =>
        {
            using var form = NewGenerationForm();
            var count = Get<NumericUpDown>(form, "normalCount");
            var required = Get<TestDataGenerationPlan>(form, "plan").RequiredNormalCount;
            Equal(true, required > 1);
            count.Value = 1;
            Equal(1m, count.Value);
            Equal(false, Get<Button>(form, "execute").Enabled);
            Equal(true, Get<TextBox>(form, "issues").Text.Contains($"{required} 行以上"));
            Invoke(form, "ExecuteGeneration");
            Equal<TestDataGenerationResult?>(null, form.GeneratedResult);
            Equal(DialogResult.None, form.DialogResult);
            count.Value = required;
            Equal(true, Get<Button>(form, "execute").Enabled);
        });
        Test("One digit numeric leading zero is reported with its field name without hiding the requirement", () =>
        {
            var fields = new[] { new FieldDefinition("一桁数値", FieldDataType.PlainNumber, 1) };
            using var form = new TestDataGenerationForm(fields, [new List<string> { "1" }], 0, 0, GenerationCsvOptions());
            var plan = Get<TestDataGenerationPlan>(form, "plan");
            Equal(true, plan.Issues.Any(issue => issue.FieldIndex == 0));
            Equal(true, Get<TextBox>(form, "issues").Text.Contains("一桁数値"));
            Equal(true, Get<Dictionary<GenerationRequirement, CheckBox>>(form, "requirementChecks")[GenerationRequirement.NumericLeadingZero].Checked);
        });
        Test("Direct count text commits immediately and enables execute from an insufficient count", () =>
        {
            using var form = NewGenerationForm();
            Get<Dictionary<GenerationRequirement, CheckBox>>(form, "requirementChecks")[GenerationRequirement.Identifier].Checked = true;
            var required = Get<TestDataGenerationPlan>(form, "plan").RequiredNormalCount;
            Equal(true, required > 4);
            Equal(false, Get<Button>(form, "execute").Enabled);
            var count = Get<NumericUpDown>(form, "normalCount");
            count.Text = ""; // A replacement is temporarily empty while typing.
            Equal(false, Get<Button>(form, "execute").Enabled);
            Equal(true, Get<Label>(form, "generationError").Text.Contains("通常行数"));
            count.Text = required.ToString(System.Globalization.CultureInfo.CurrentCulture);
            Equal((decimal)required, count.Value);
            Equal(true, Get<Button>(form, "execute").Enabled);
            Invoke(form, "ExecuteGeneration");
            Equal(DialogResult.OK, form.DialogResult);
            Equal(required, form.GeneratedResult!.Settings.NormalRecordCount);
            Equal(required, form.GeneratedResult.Records.Count);
        });
        Test("Direct baseline text uses the latest row and unfinished numbers cannot generate", () =>
        {
            using var form = NewGenerationForm();
            var baseline = Get<NumericUpDown>(form, "baselineRecord");
            baseline.Text = "";
            Equal(false, Get<Button>(form, "execute").Enabled);
            Invoke(form, "ExecuteGeneration");
            Equal<TestDataGenerationResult?>(null, form.GeneratedResult);
            Equal(DialogResult.None, form.DialogResult);
            baseline.Text = "2";
            var count = Get<NumericUpDown>(form, "normalCount");
            count.Text = "100001";
            Equal(false, Get<Button>(form, "execute").Enabled);
            Equal("100001", count.Text); // Do not clamp a partial edit during analysis.
            Invoke(form, "ExecuteGeneration");
            Equal<TestDataGenerationResult?>(null, form.GeneratedResult);
            count.Text = "4";
            Equal(true, Get<Button>(form, "execute").Enabled);
            Invoke(form, "ExecuteGeneration");
            Equal(1, form.GeneratedResult!.Settings.BaselineRecordIndex);
            Equal(true, form.GeneratedResult.Records.All(row => row[2] == "beta" && row[3] == "20261002"));
            Equal(false, Get<Button>(form, "cancel").CausesValidation);
        });
        Test("Pending numeric text survives base validation paths without clamping or generating", () =>
        {
            using var form = NewGenerationForm();
            var count = Get<NumericUpDown>(form, "normalCount");
            var baseline = Get<NumericUpDown>(form, "baselineRecord");
            foreach (var (control, text, previousValue) in new[]
            {
                (count, "100001", 4m), (count, "0", 4m),
                (baseline, "3", 1m), (baseline, "", 1m)
            })
            {
                control.Text = text;
                Equal(text, control.Text); // UpDownBase.Text setter must not clamp after TextChanged.
                Invoke(control, "OnTextBoxLostFocus", null, EventArgs.Empty);
                Invoke(control, "OnLostFocus", EventArgs.Empty); // NumericUpDown calls UpdateEditText directly.
                form.ValidateChildren();
                Equal(previousValue, control.Value); // Value getter must leave the pending text intact too.
                control.UpButton();
                control.DownButton();
                Equal(text, control.Text);
                Equal(false, Get<Button>(form, "execute").Enabled);
                Equal<TestDataGenerationPlan?>(null, Get<TestDataGenerationPlan?>(form, "plan"));
                Equal(true, Get<Label>(form, "countSummary").Text.Contains("未計算"));
                Invoke(form, "ExecuteGeneration");
                Equal<TestDataGenerationResult?>(null, form.GeneratedResult);
                Equal(DialogResult.None, form.DialogResult);
                control.Text = previousValue.ToString(System.Globalization.CultureInfo.CurrentCulture);
                Equal(true, Get<Button>(form, "execute").Enabled);
            }
            count.UpButton();
            Equal(5m, count.Value);
            count.DownButton();
            Equal(4m, count.Value);
            baseline.UpButton();
            Equal(2m, baseline.Value);
            Invoke(form, "ExecuteGeneration");
            Equal(1, form.GeneratedResult!.Settings.BaselineRecordIndex);
            Equal(4, form.GeneratedResult.Settings.NormalRecordCount);
        });
        Test("Fixed whitespace limitations remain visible and switching to CSV enables the requested space case", () =>
        {
            using var form = NewGenerationForm(initialFieldIndex: 2, options: GenerationCsvOptions() with { Format = DataFileFormat.FixedLength });
            SetGenerationRequirements(form, GenerationRequirement.TextTrailingSpace);
            Equal(true, Get<TestDataGenerationPlan>(form, "plan").Issues.Any(issue => issue.FieldIndex == 2));
            Equal(true, Get<TextBox>(form, "issues").Text.Contains("説明"));
            Equal(false, Get<Button>(form, "execute").Enabled);
            Invoke(form, "ExecuteGeneration");
            Equal<TestDataGenerationResult?>(null, form.GeneratedResult);
            Get<ComboBox>(form, "format").SelectedIndex = 1;
            Equal(true, Get<Button>(form, "execute").Enabled);
            Invoke(form, "ExecuteGeneration");
            Equal(true, form.GeneratedResult!.Records.Any(record => record[2].EndsWith(' ')));
        });
        Test("Format and encoding switches preserve inherited fixed options and CSV header choices", () =>
        {
            var initial = new DataSaveOptions(DataFileFormat.Csv, CsvTextEncoding.StrictShiftJis,
                RecordSeparatorMode.None, NationalTextEncoding.Utf32, DataEncodingProfile.Utf8WithNationalText, CsvHasHeader: true);
            using var form = NewGenerationForm(options: initial);
            Equal(initial, (DataSaveOptions)Invoke(form, "BuildOutputOptions")!);
            Equal(true, Get<ComboBox>(form, "csvEncoding").Enabled);
            Equal(false, Get<ComboBox>(form, "national").Enabled);
            Get<ComboBox>(form, "format").SelectedIndex = 0;
            Equal(initial with { Format = DataFileFormat.FixedLength, CsvHasHeader = false }, (DataSaveOptions)Invoke(form, "BuildOutputOptions")!);
            Equal(false, Get<ComboBox>(form, "csvEncoding").Enabled);
            Equal(true, Get<ComboBox>(form, "textEncoding").Enabled);
            Equal(true, Get<ComboBox>(form, "national").Enabled);
            Equal(true, Get<ComboBox>(form, "separator").Enabled);
            Get<ComboBox>(form, "csvEncoding").SelectedIndex = (int)CsvTextEncoding.ShiftJis;
            Get<ComboBox>(form, "format").SelectedIndex = 1;
            Equal(initial with { CsvEncoding = CsvTextEncoding.ShiftJis, CsvHasHeader = false }, (DataSaveOptions)Invoke(form, "BuildOutputOptions")!);
            Get<ComboBox>(form, "format").SelectedIndex = 2;
            Equal(true, ((DataSaveOptions)Invoke(form, "BuildOutputOptions")!).CsvHasHeader);
        });
        Test("Invalid CSV cases are added to the normal count and result settings match the controls", () =>
        {
            using var form = NewGenerationForm();
            var checks = Get<Dictionary<GenerationRequirement, CheckBox>>(form, "requirementChecks");
            checks[GenerationRequirement.InvalidNumeric].Checked = true;
            checks[GenerationRequirement.Overlength].Checked = true;
            var plan = Get<TestDataGenerationPlan>(form, "plan");
            Equal(true, plan.InvalidRecordCount > 0);
            var total = (int)Get<NumericUpDown>(form, "normalCount").Value + plan.InvalidRecordCount;
            Equal(true, Get<Label>(form, "countSummary").Text.Contains($"合計：{total} 行"));
            Invoke(form, "ExecuteGeneration");
            Equal(total, form.GeneratedResult!.Records.Count);
            Equal(true, form.GeneratedResult.Cases.Any(item => item.IntentionalInvalid));
            Equal(GenerationRequirement.InvalidNumeric | GenerationRequirement.Overlength,
                form.GeneratedResult.Settings.Requirements & (GenerationRequirement.InvalidNumeric | GenerationRequirement.Overlength));
        });
        Test("Fixed N encoding switches use the selected byte width without changing source definitions", () =>
        {
            var fields = new[] { new FieldDefinition("全角文字", FieldDataType.FullWidthText, 2, NationalByteWidth: 2) };
            var rows = new List<List<string>> { new() { "漢字" } };
            using var form = new TestDataGenerationForm(fields, rows, 0, 0,
                GenerationCsvOptions() with { Format = DataFileFormat.FixedLength });
            SetGenerationRequirements(form, GenerationRequirement.TextMaximum);
            Get<ComboBox>(form, "national").SelectedIndex = (int)NationalTextEncoding.Utf32;
            Equal(false, Get<TestDataGenerationPlan>(form, "plan").HasBlockingIssues);
            Equal(true, Get<Button>(form, "execute").Enabled);
            Invoke(form, "ExecuteGeneration");
            Equal(NationalTextEncoding.Utf32, form.GeneratedResult!.Settings.OutputOptions.NationalEncoding);
            Equal(TestDataGenerator.DefinitionFingerprint(fields), form.GeneratedResult.DefinitionFingerprint);
            Equal(2, fields[0].NationalByteWidth);
            var wideFields = new[] { fields[0] with { NationalByteWidth = 4 } };
            using var reverse = new TestDataGenerationForm(wideFields, rows, 0, 0,
                GenerationCsvOptions() with { Format = DataFileFormat.FixedLength, NationalEncoding = NationalTextEncoding.Utf32 });
            SetGenerationRequirements(reverse, GenerationRequirement.TextMaximum);
            Get<ComboBox>(reverse, "national").SelectedIndex = (int)NationalTextEncoding.Utf16;
            Equal(false, Get<TestDataGenerationPlan>(reverse, "plan").HasBlockingIssues);
            Equal(true, Get<Button>(reverse, "execute").Enabled);
            Equal(4, wideFields[0].NationalByteWidth);
        });
        Test("Missing baseline, target or requirements prevents execution with an inline explanation", () =>
        {
            using var empty = new TestDataGenerationForm(GenerationFields(), [], 0, 0, GenerationCsvOptions());
            Equal(false, Get<NumericUpDown>(empty, "baselineRecord").Enabled);
            Equal(false, Get<Button>(empty, "execute").Enabled);
            Equal(true, Get<TextBox>(empty, "issues").Text.Contains("基準行がありません"));
            Invoke(empty, "ExecuteGeneration");
            Equal<TestDataGenerationResult?>(null, empty.GeneratedResult);
            using var form = NewGenerationForm(initialFieldIndex: -1);
            SetGenerationRequirements(form, GenerationRequirement.None);
            Equal(true, Get<TextBox>(form, "issues").Text.Contains("対象項目を選択"));
            Equal(true, Get<TextBox>(form, "issues").Text.Contains("1つ以上"));
            Equal(false, Get<Button>(form, "execute").Enabled);
        });
        Test("Dialog snapshots input and cancellation leaves all source records and definitions unchanged", () =>
        {
            var fields = GenerationFields().ToList();
            var rows = GenerationRows();
            var originalRows = rows.Select(row => row.ToArray()).ToArray();
            var originalFields = fields.ToArray();
            using var form = new TestDataGenerationForm(fields, rows, 0, 0, GenerationCsvOptions());
            fields[0] = fields[0] with { Name = "後から変更" };
            rows[0][2] = "changed";
            Equal("番号", Get<IReadOnlyList<FieldDefinition>>(form, "fields")[0].Name);
            Equal("alpha", Get<IReadOnlyList<List<string>>>(form, "records")[0][2]);
            fields[0] = originalFields[0];
            rows[0][2] = originalRows[0][2];
            Invoke(form, "SetAllTargets", true);
            Invoke(form, "SetAllTargets", false);
            form.DialogResult = DialogResult.Cancel;
            Invoke(form, "OnFormClosing", new FormClosingEventArgs(CloseReason.UserClosing, false));
            Equal<TestDataGenerationResult?>(null, form.GeneratedResult);
            Equal(true, originalFields.SequenceEqual(fields));
            Equal(true, originalRows.SelectMany(row => row).SequenceEqual(rows.SelectMany(row => row)));
            Equal(form.CancelButton, Get<Button>(form, "cancel"));
            Equal(true, Descendants(form).OfType<Panel>().Single(control => control.Name == "GenerationBody").AutoScroll);
            Equal(true, Get<Button>(form, "execute").Parent!.Name == "GenerationFooter");
        });
        Test("Attempted generation failure stays open with no result and displays the engine error inline", () =>
        {
            using var form = NewGenerationForm();
            Func<TestDataGenerationResult> failure = () => throw new InvalidOperationException("架空の作成エラー");
            Equal(false, (bool)Invoke(form, "TryGenerate", failure)!);
            Equal(DialogResult.None, form.DialogResult);
            Equal<TestDataGenerationResult?>(null, form.GeneratedResult);
            Equal(false, form.IsDisposed);
            Equal(false, form.UseWaitCursor);
            Equal(true, Get<Label>(form, "generationError").Text.Contains("架空の作成エラー"));
            Equal(4m, Get<NumericUpDown>(form, "normalCount").Value);
        });
    }

    private static TestDataGenerationForm NewGenerationForm(int initialFieldIndex = 0, int initialRecordIndex = 0, DataSaveOptions? options = null) =>
        new(GenerationFields(), GenerationRows(), initialRecordIndex, initialFieldIndex, options ?? GenerationCsvOptions());

    private static IReadOnlyList<FieldDefinition> GenerationFields() =>
    [
        new("番号", FieldDataType.PlainNumber, 8),
        new("別表示", FieldDataType.Text, 8, RedefinesName: "番号"),
        new("説明", FieldDataType.Text, 8),
        new("日付", FieldDataType.PlainNumber, 8)
    ];

    private static List<List<string>> GenerationRows() =>
    [
        new() { "00000123", "", "alpha", "20261001" },
        new() { "00000456", "", "beta", "20261002" }
    ];

    private static DataSaveOptions GenerationCsvOptions() => new(DataFileFormat.Csv, CsvTextEncoding.Utf8,
        RecordSeparatorMode.CrLfOrLf, NationalTextEncoding.ShiftJis, DataEncodingProfile.ShiftJis);

    private static void SetGenerationRequirements(TestDataGenerationForm form, GenerationRequirement requirements)
    {
        foreach (var pair in Get<Dictionary<GenerationRequirement, CheckBox>>(form, "requirementChecks"))
            pair.Value.Checked = requirements.HasFlag(pair.Key);
    }
}
