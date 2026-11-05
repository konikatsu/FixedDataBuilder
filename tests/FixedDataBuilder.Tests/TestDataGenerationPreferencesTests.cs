using System.Reflection;
using System.Text.Json;
using FixedDataBuilder;

internal static partial class Program
{
    private static void RunTestDataGenerationPreferencesTests(string repo)
    {
        Test("Generation first use checks all physical targets and all requirements and initializes enough normal rows once", () =>
        {
            using var form = new TestDataGenerationForm(GenerationFields(), GenerationRows(), 0, 0, GenerationCsvOptions());
            var targets = Get<CheckedListBox>(form, "targetFields");
            Equal(3, targets.Items.Count);
            Equal(targets.Items.Count, targets.CheckedItems.Count);
            var checks = Get<Dictionary<GenerationRequirement, CheckBox>>(form, "requirementChecks");
            Equal(17, checks.Count);
            Equal(true, checks.Values.All(check => check.Checked));
            var plan = Get<TestDataGenerationPlan>(form, "plan");
            var count = Get<NumericUpDown>(form, "normalCount");
            Equal((decimal)Math.Max(4, plan.RequiredNormalCount), count.Value);
            count.Value = 1;
            checks[GenerationRequirement.Identifier].Checked = false;
            Equal(1m, count.Value);
            Equal(false, Get<Button>(form, "execute").Enabled);
        });
        Test("Generation restores every remembered choice and clamps only a baseline that no longer exists", () =>
        {
            var saved = new TestDataGenerationSettings(99, [2], GenerationRequirement.TextLeadingSpace | GenerationRequirement.TextTrailingSpace,
                7, GenerationCsvOptions() with { CsvEncoding = CsvTextEncoding.StrictShiftJis, CsvHasHeader = true });
            using var form = new TestDataGenerationForm(GenerationFields(), GenerationRows(), 0, 0,
                new(DataFileFormat.FixedLength, CsvTextEncoding.Utf8, RecordSeparatorMode.None, NationalTextEncoding.Utf32), saved);
            var actual = (TestDataGenerationSettings)Invoke(form, "BuildSettings")!;
            Equal(1, actual.BaselineRecordIndex);
            Equal(true, actual.TargetFieldIndexes.SequenceEqual(saved.TargetFieldIndexes));
            Equal(saved.Requirements, actual.Requirements);
            Equal(saved.NormalRecordCount, actual.NormalRecordCount);
            Equal(saved.OutputOptions, actual.OutputOptions);
            Equal(1, Get<CheckedListBox>(form, "targetFields").CheckedItems.Count);
            using var invalidTarget = new TestDataGenerationForm(GenerationFields(), GenerationRows(), 0, 0, GenerationCsvOptions(), saved with { TargetFieldIndexes = [1] });
            Equal(3, Get<CheckedListBox>(invalidTarget, "targetFields").CheckedItems.Count);
        });
        Test("Generation settings survive a new store instance and remain isolated by definition with no record values", () =>
        {
            var path = Path.Combine(temporaryDirectory, "generation-preferences.json");
            var store = new TestDataGenerationSettingsStore(path);
            var indexes = new[] { 0, 2 };
            var first = new TestDataGenerationSettings(1, indexes, GenerationRequirement.NumericMaximum | GenerationRequirement.TextInternalSpace,
                9, GenerationCsvOptions() with { CsvHasHeader = true });
            Equal<TestDataGenerationSettings?>(null, store.Find("definition-a"));
            Equal(true, store.Remember("definition-a", first));
            indexes[0] = 3;
            Equal(true, store.Remember("definition-b", first with { TargetFieldIndexes = [2], NormalRecordCount = 12 }));
            var reopened = new TestDataGenerationSettingsStore(path);
            var found = reopened.Find("definition-a")!;
            Equal(true, found.TargetFieldIndexes.SequenceEqual(new[] { 0, 2 }));
            Equal(first.Requirements, found.Requirements);
            Equal(first.OutputOptions, found.OutputOptions);
            Equal(9, found.NormalRecordCount);
            Equal(12, reopened.Find("definition-b")!.NormalRecordCount);
            Equal<TestDataGenerationSettings?>(null, reopened.Find("definition-c"));
            using var json = JsonDocument.Parse(File.ReadAllBytes(path));
            var settings = json.RootElement[0].GetProperty("Settings");
            Equal(false, settings.TryGetProperty("Records", out _));
            Equal(false, File.ReadAllText(path).Contains("alpha", StringComparison.Ordinal));
        });
        Test("Generation settings ignore corrupt files and reject invalid values without replacing a valid saved entry", () =>
        {
            var path = Path.Combine(temporaryDirectory, "generation-invalid-preferences.json");
            var store = new TestDataGenerationSettingsStore(path);
            File.WriteAllText(path, "{broken");
            Equal<TestDataGenerationSettings?>(null, store.Find("definition"));
            var valid = new TestDataGenerationSettings(0, [0], GenerationRequirement.NumericMaximum, 4, GenerationCsvOptions());
            Equal(true, store.Remember("definition", valid));
            var original = File.ReadAllBytes(path);
            foreach (var invalid in new[] { valid with { TargetFieldIndexes = [0, 0] }, valid with { TargetFieldIndexes = [-1] },
                valid with { Requirements = (GenerationRequirement)(1 << 28) }, valid with { NormalRecordCount = 0 },
                valid with { OutputOptions = valid.OutputOptions with { CsvEncoding = (CsvTextEncoding)999 } } })
            {
                Equal(false, store.Remember("definition", invalid));
                Equal(true, original.SequenceEqual(File.ReadAllBytes(path)));
            }
            Equal(false, store.Remember("", valid));
            File.WriteAllBytes(path, new byte[4 * 1024 * 1024 + 1]);
            Equal<TestDataGenerationSettings?>(null, store.Find("definition"));
        });
        Test("Generation remembers conditions only after opening generated data and restores them on the next dialog", () =>
        {
            using var main = CreateCellDisplayForm(recordRows: true);
            var store = new TestDataGenerationSettingsStore(Path.Combine(temporaryDirectory, "generation-main-preferences.json"));
            SetGenerationPreferenceStore(main, store);
            var result = GenerationIntegrationResult(main);
            var key = GenerationPreferenceKey(main);
            WithDialog("テストデータ作成条件", DialogResult.OK, () => Invoke(main, "GenerateTestData"), dialog =>
                Invoke(dialog, "TryGenerate", (Func<TestDataGenerationResult>)(() => result)));
            var found = store.Find(key)!;
            Equal(result.Settings.Requirements, found.Requirements);
            Equal(true, result.Settings.TargetFieldIndexes.SequenceEqual(found.TargetFieldIndexes));
            Equal(result.Records.Count, Get<List<List<string>>>(main, "records").Count);
            WithDialog("テストデータ作成条件", DialogResult.Cancel, () => Invoke(main, "GenerateTestData"), dialog =>
            {
                var restored = (TestDataGenerationSettings)Invoke(dialog, "BuildSettings")!;
                Equal(found.NormalRecordCount, restored.NormalRecordCount);
                Equal(found.Requirements, restored.Requirements);
                Equal(found.OutputOptions, restored.OutputOptions);
                Equal(true, found.TargetFieldIndexes.SequenceEqual(restored.TargetFieldIndexes));
            });
        });
        Test("Generation cancelled choices and failed generation do not overwrite remembered conditions", () =>
        {
            using var main = CreateCellDisplayForm(recordRows: true);
            var path = Path.Combine(temporaryDirectory, "generation-cancel-preferences.json");
            var store = new TestDataGenerationSettingsStore(path);
            SetGenerationPreferenceStore(main, store);
            var key = GenerationPreferenceKey(main);
            Equal(true, store.Remember(key, GenerationIntegrationResult(main).Settings));
            var before = File.ReadAllBytes(path);
            WithDialog("テストデータ作成条件", DialogResult.Cancel, () => Invoke(main, "GenerateTestData"), dialog =>
            {
                Invoke(dialog, "SetAllTargets", true);
                Get<NumericUpDown>(dialog, "normalCount").Value = 20;
            });
            Equal(true, before.SequenceEqual(File.ReadAllBytes(path)));
            WithDialog("テストデータ作成条件", DialogResult.OK, () => Invoke(main, "GenerateTestData"), dialog =>
                Invoke(dialog, "TryGenerate", (Func<TestDataGenerationResult>)(() => throw new InvalidOperationException("架空の生成失敗"))));
            Equal(true, before.SequenceEqual(File.ReadAllBytes(path)));
        });
        Test("Generation preference save failure preserves generated data and reports the problem in the main status", () =>
        {
            using var main = CreateCellDisplayForm(recordRows: true);
            var directory = Path.Combine(temporaryDirectory, "generation-preference-write-failure");
            Directory.CreateDirectory(directory);
            SetGenerationPreferenceStore(main, new TestDataGenerationSettingsStore(directory));
            var result = GenerationIntegrationResult(main);
            WithDialog("テストデータ作成条件", DialogResult.OK, () => Invoke(main, "GenerateTestData"), dialog =>
                Invoke(dialog, "TryGenerate", (Func<TestDataGenerationResult>)(() => result)));
            Equal(result.Records.Count, Get<List<List<string>>>(main, "records").Count);
            Equal(true, Get<bool>(main, "hasUnsavedChanges"));
            Equal(true, Get<ToolStripStatusLabel>(main, "statusLabel").Text?.Contains("記憶できません") == true);
            Equal(true, Directory.Exists(directory));
        });
        Test("Generation disabled reason stays in the fixed footer and row shortage is red with required and specified counts", () =>
        {
            using var form = new TestDataGenerationForm(GenerationFields(), GenerationRows(), 0, 0, GenerationCsvOptions());
            var status = Get<Label>(form, "executionStatus");
            Equal("GenerationExecutionStatus", status.Name);
            Equal("GenerationFooter", status.Parent!.Name);
            Equal(false, status.Parent.Parent!.Controls.OfType<Panel>().Any(panel => panel.Name == "GenerationBody" && panel.Contains(status)));
            var required = Get<TestDataGenerationPlan>(form, "plan").RequiredNormalCount;
            Get<NumericUpDown>(form, "normalCount").Value = 1;
            Equal(false, Get<Button>(form, "execute").Enabled);
            Equal(Color.Firebrick, status.ForeColor);
            Equal(Color.Firebrick, Get<Label>(form, "countSummary").ForeColor);
            Equal(true, status.Text.Contains($"必要：{required}行"));
            Equal(true, status.Text.Contains("指定：1行"));
            Get<NumericUpDown>(form, "normalCount").Value = required;
            Equal(true, Get<Button>(form, "execute").Enabled);
            Equal(true, status.ForeColor != Color.Firebrick);
            Equal(true, status.Text.Contains("実行できます"));
        });
        Test("Generation fixed-footer reasons update for missing targets requirements incomplete numbers and exceptions", () =>
        {
            using var form = new TestDataGenerationForm(GenerationFields(), GenerationRows(), 0, 0, GenerationCsvOptions());
            var status = Get<Label>(form, "executionStatus");
            Invoke(form, "SetAllTargets", false);
            Equal(true, status.Text.Contains("対象項目")); Equal(Color.Firebrick, status.ForeColor);
            Invoke(form, "SetAllTargets", true);
            SetGenerationRequirements(form, GenerationRequirement.None);
            Equal(true, status.Text.Contains("1つ以上")); Equal(Color.Firebrick, status.ForeColor);
            SetGenerationRequirements(form, GenerationRequirement.NumericMaximum);
            Get<NumericUpDown>(form, "normalCount").Text = "";
            Equal(true, status.Text.Contains("通常行数")); Equal(Color.Firebrick, status.ForeColor);
            Get<NumericUpDown>(form, "normalCount").Text = "4";
            Equal(true, Get<Button>(form, "execute").Enabled);
            Invoke(form, "TryGenerate", (Func<TestDataGenerationResult>)(() => throw new InvalidOperationException("確認用エラー")));
            Equal(true, status.Text.Contains("確認用エラー")); Equal(Color.Firebrick, status.ForeColor);
        });
        Test("Generation ready with inapplicable conditions shows an amber warning and plain ready clears it", () =>
        {
            var saved = new TestDataGenerationSettings(0, [2], GenerationRequirement.NumericMaximum, 4, GenerationCsvOptions());
            using var form = new TestDataGenerationForm(GenerationFields(), GenerationRows(), 0, 0, GenerationCsvOptions(),
                saved with { Requirements = GenerationRequirement.TextMaximum | GenerationRequirement.NumericMaximum });
            Equal(true, Get<Button>(form, "execute").Enabled);
            var status = Get<Label>(form, "executionStatus");
            Equal(Color.DarkGoldenrod, status.ForeColor);
            Equal(true, status.Text.Contains("確認事項"));
            SetGenerationRequirements(form, GenerationRequirement.TextMaximum);
            Equal(true, Get<Button>(form, "execute").Enabled);
            Equal(SystemColors.ControlText, status.ForeColor);
            Equal(SystemColors.ControlText, Get<TextBox>(form, "issues").ForeColor);
        });
        _ = repo;
    }

    private static string GenerationPreferenceKey(MainForm main) => TestDataGenerator.DefinitionFingerprint(
        Get<List<FieldDefinition>>(main, "fields").Select(field => field.Type == FieldDataType.FullWidthText ? field with { NationalByteWidth = 2 } : field).ToArray());

    private static void SetGenerationPreferenceStore(MainForm main, TestDataGenerationSettingsStore store) =>
        typeof(MainForm).GetField("generationSettingsStore", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(main, store);
}
