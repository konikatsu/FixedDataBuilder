using System.Text.Json;

namespace FixedDataBuilder;

public sealed partial class MainForm
{
    private readonly Button generateTestDataButton = new() { Text = "テストデータ作成...", AutoSize = true, Enabled = false };
    private readonly ToolStripMenuItem generateTestDataMenu = new("テストデータ作成...") { Enabled = false };
    private readonly ToolStripMenuItem generationReportMenu = new("生成条件・対応表を保存...") { Enabled = false };
    private TestDataGenerationResult? generatedTestData;
    private List<string>[] generatedRecordBindings = [];
    private string? generationSourceDefinition;
    private string? generationSourceData;

    private void GenerateTestData()
    {
        if (!grid.EndEdit() || records.Count == 0 || !fields.Any(field => !field.IsRedefines)) return;
        using var dialog = new TestDataGenerationForm(fields.ToArray(), records.Select(row => row.ToList()).ToList(),
            Math.Clamp(CurrentRecordIndex(), 0, records.Count - 1), CurrentFieldIndex(), CurrentSaveOptions());
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.GeneratedResult is not { } result) return;
        // Replacement is deferred until both generation and the user's unsaved-data choice succeed.
        if (!ConfirmDiscardChanges("生成データを開く")) return;
        try { ApplyGeneratedTestData(result); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "テストデータ作成", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    private void ApplyGeneratedTestData(TestDataGenerationResult result)
    {
        if (result.Records.Count == 0 || result.Records.Any(row => row.Count != fields.Count)
            || result.DefinitionFingerprint != TestDataGenerator.DefinitionFingerprint(fields))
            throw new InvalidDataException("生成後に定義が変わったため、生成データを開けません。もう一度生成してください。");
        var newRecords = result.Records.Select(row => row.ToList()).ToArray();
        var options = result.Settings.OutputOptions;
        generationSourceDefinition = GetCurrentDefinitionPath();
        generationSourceData = GetCurrentDataPath();
        currentDataFileFormat = options.Format;
        currentCsvEncoding = options.CsvEncoding;
        currentCsvHasHeader = options.Format == DataFileFormat.Csv && options.CsvHasHeader;
        currentSeparatorMode = options.SeparatorMode;
        currentDataEncodingProfile = options.TextEncoding ?? currentDataEncodingProfile;
        if (options.Format == DataFileFormat.FixedLength) ApplyNationalTextEncoding(options.NationalEncoding);
        else currentNationalTextEncoding = options.NationalEncoding;
        records.Clear();
        records.AddRange(newRecords);
        // Generated data is a new document. Saving must never overwrite its baseline file implicitly.
        saveDataPath = null;
        ClearPathText(dataPathTextBox);
        singleRecordIndex = 0;
        generatedTestData = result;
        generatedRecordBindings = newRecords;
        generatedDefinitionFingerprint = TestDataGenerator.DefinitionFingerprint(fields);
        UpdateSeparatorModeText();
        RefreshGrid();
        RestoreRecordFieldSelection(0, result.Settings.TargetFieldIndexes.FirstOrDefault());
        SetUnsavedChanges(true);
        SetStatus($"{records.Count}行を生成しました。内容を確認し、名前を付けて保存してください。生成条件はツールから別途保存できます。");
    }

    private string? generatedDefinitionFingerprint;

    private bool HasCurrentGenerationReport()
    {
        if (generatedTestData is null) return false;
        if (generatedDefinitionFingerprint == TestDataGenerator.DefinitionFingerprint(fields)
            && generatedRecordBindings.Length == records.Count
            && generatedRecordBindings.Where((row, index) => !ReferenceEquals(row, records[index])).Any() == false) return true;
        // A reload, row insertion/deletion, or definition change loses the row-to-case correspondence.
        generatedTestData = null;
        generatedRecordBindings = [];
        return false;
    }

    private string BuildGenerationReportJson()
    {
        if (!grid.EndEdit() || !HasCurrentGenerationReport())
            throw new InvalidDataException("現在のデータに対応する生成条件がありません。再生成してください。");
        var result = generatedTestData!;
        var changed = !records.Select(row => (IEnumerable<string>)row)
            .Zip(result.Records, (actual, original) => actual.SequenceEqual(original)).All(equal => equal);
        return JsonSerializer.Serialize(new
        {
            schemaVersion = 1, generatedAt = result.GeneratedAt, exportedAt = DateTimeOffset.Now,
            status = changed ? "modified_after_generation_not_revalidated" : "generated_not_processed",
            note = "値の生成条件と行・項目の対応です。業務上の妥当性、対象処理の実行結果・期待値の合格を示しません。",
            sourceDefinition = generationSourceDefinition, sourceData = generationSourceData,
            sourceDefinitionFingerprint = result.DefinitionFingerprint,
            currentDefinitionFingerprint = TestDataGenerator.DefinitionFingerprint(fields),
            settings = result.Settings, currentSaveOptions = CurrentSaveOptions(), dataFile = saveDataPath,
            fields = fields.Select((field, index) => new { number = index + 1, field.Name, field.DisplayDefinition }),
            cases = result.Cases, issues = result.Issues,
            rows = records.Select((row, index) => new
            {
                number = index + 1, caseId = result.Cases[index].CaseId,
                values = row.Select((value, fieldIndex) => new
                {
                    fieldNumber = fieldIndex + 1, generatedValue = result.Records[index][fieldIndex],
                    currentValue = value, valueChanged = value != result.Records[index][fieldIndex]
                })
            })
        }, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }

    private void SaveGenerationReport()
    {
        try
        {
            var json = BuildGenerationReportJson();
            using var dialog = new SaveFileDialog
            {
                Title = "生成条件・行と項目の対応表を保存", Filter = "生成条件 JSON (*.json)|*.json",
                FileName = string.IsNullOrEmpty(saveDataPath) ? "generated-test-data-cases.json"
                    : Path.GetFileNameWithoutExtension(saveDataPath) + "-cases.json"
            };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            File.WriteAllText(dialog.FileName, json, new System.Text.UTF8Encoding(false));
            SetStatus($"生成条件・対応表を保存しました: {Path.GetFileName(dialog.FileName)}");
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "生成条件の保存", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    private void UpdateGenerationControls()
    {
        generateTestDataButton.Enabled = generateTestDataMenu.Enabled = records.Count > 0 && fields.Any(field => !field.IsRedefines);
        generationReportMenu.Enabled = HasCurrentGenerationReport();
    }
}
