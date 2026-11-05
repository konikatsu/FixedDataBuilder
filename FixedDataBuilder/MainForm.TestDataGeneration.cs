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
    private TestDataGenerationSettingsStore generationSettingsStore = new(TestDataGenerationSettingsStore.DefaultPath);

    private void GenerateTestData()
    {
        if (!grid.EndEdit() || records.Count == 0 || !fields.Any(field => !field.IsRedefines)) return;
        var originalFields = fields.ToArray();
        var settingsKey = GenerationDefinitionKey(originalFields);
        var initialRecord = isSingleRecordView ? singleRecordIndex
            : layout == GridLayout.FieldRows && (grid.CurrentCell?.ColumnIndex ?? -1) < 2 ? 0 : CurrentRecordIndex();
        using var dialog = new TestDataGenerationForm(fields, records,
            Math.Clamp(initialRecord, 0, records.Count - 1), CurrentFieldIndex(), CurrentSaveOptions(), generationSettingsStore.Find(settingsKey));
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.GeneratedResult is not { } result) return;
        // Replacement is deferred until both generation and the user's unsaved-data choice succeed.
        if (!ConfirmDiscardChanges("生成データを開く")) return;
        try
        {
            ApplyGeneratedTestDataCore(result, originalFields);
            if (!generationSettingsStore.Remember(settingsKey, result.Settings))
                SetStatus($"{records.Count}行を生成しました。作成条件を記憶できませんでした。内容を確認し、名前を付けて保存してください。");
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "テストデータ作成", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    private void ApplyGeneratedTestData(TestDataGenerationResult result) => ApplyGeneratedTestDataCore(result, fields.ToArray());

    // N storage width follows the selected output encoding; it does not change field/row case correspondence.
    private static string GenerationDefinitionKey(IReadOnlyList<FieldDefinition> definition) =>
        TestDataGenerator.DefinitionFingerprint(definition.Select(field => field.Type == FieldDataType.FullWidthText
            ? field with { NationalByteWidth = 2 } : field).ToArray());

    private void ApplyGeneratedTestDataCore(TestDataGenerationResult result, IReadOnlyList<FieldDefinition> sourceFields)
    {
        if (result.Records.Count == 0 || result.Records.Any(row => row.Count != fields.Count)
            || result.DefinitionFingerprint != TestDataGenerator.DefinitionFingerprint(sourceFields)
            || GenerationDefinitionKey(sourceFields) != GenerationDefinitionKey(fields))
            throw new InvalidDataException("生成後に定義が変わったため、生成データを開けません。もう一度生成してください。");
        var newRecords = result.Records.Select(row => row.ToList()).ToArray();
        var options = result.Settings.OutputOptions;
        var previousFields = fields.ToArray();
        var previousRecords = records.ToArray();
        var previousOptions = CurrentSaveOptions();
        var previousSavePath = saveDataPath;
        var previousDataPath = GetCurrentDataPath();
        var previousDirty = hasUnsavedChanges;
        var previousRecord = singleRecordIndex;
        var previousSelection = (CurrentRecordIndex(), CurrentFieldIndex());
        var previousReport = (generatedTestData, generatedRecordBindings, generatedDefinitionFingerprint,
            generationSourceDefinition, generationSourceData);
        try
        {
            generationSourceDefinition = GetCurrentDefinitionPath();
            generationSourceData = GetCurrentDataPath();
            currentDataFileFormat = options.Format;
            currentCsvEncoding = options.CsvEncoding;
            currentCsvHasHeader = options.Format == DataFileFormat.Csv && options.CsvHasHeader;
            currentSeparatorMode = options.SeparatorMode;
            currentDataEncodingProfile = options.TextEncoding ?? currentDataEncodingProfile;
            ApplyNationalTextEncoding(options.NationalEncoding);
            records.Clear();
            records.AddRange(newRecords);
            // Generated data is a new document. Saving must never overwrite its baseline file implicitly.
            saveDataPath = null;
            ClearPathText(dataPathTextBox);
            singleRecordIndex = 0;
            generatedTestData = result;
            generatedRecordBindings = newRecords;
            generatedDefinitionFingerprint = GenerationDefinitionKey(fields);
            UpdateSeparatorModeText();
            SetUnsavedChanges(true);
            RefreshGrid();
            RestoreRecordFieldSelection(0, result.Settings.TargetFieldIndexes.FirstOrDefault());
            SetStatus($"{records.Count}行を生成しました。内容を確認し、名前を付けて保存してください。生成条件はツールから別途保存できます。");
        }
        catch
        {
            fields.Clear(); fields.AddRange(previousFields);
            records.Clear(); records.AddRange(previousRecords);
            currentDataFileFormat = previousOptions.Format; currentCsvEncoding = previousOptions.CsvEncoding;
            currentCsvHasHeader = previousOptions.CsvHasHeader; currentSeparatorMode = previousOptions.SeparatorMode;
            currentDataEncodingProfile = previousOptions.TextEncoding ?? DataEncodingProfile.ShiftJis;
            currentNationalTextEncoding = previousOptions.NationalEncoding;
            saveDataPath = previousSavePath;
            if (string.IsNullOrEmpty(previousDataPath)) ClearPathText(dataPathTextBox);
            else SetPathText(dataPathTextBox, previousDataPath);
            singleRecordIndex = previousRecord;
            (generatedTestData, generatedRecordBindings, generatedDefinitionFingerprint,
                generationSourceDefinition, generationSourceData) = previousReport;
            UpdateSeparatorModeText();
            SetUnsavedChanges(previousDirty);
            RefreshGrid();
            RestoreRecordFieldSelection(previousSelection.Item1, previousSelection.Item2);
            throw;
        }
    }

    private string? generatedDefinitionFingerprint;

    private bool HasCurrentGenerationReport()
    {
        if (generatedTestData is null) return false;
        if (generatedDefinitionFingerprint == GenerationDefinitionKey(fields)
            && generatedRecordBindings.Length == records.Count
            && generatedRecordBindings.Where((row, index) => !ReferenceEquals(row, records[index])).Any() == false) return true;
        // A reload, row insertion/deletion, or definition change loses the row-to-case correspondence.
        generatedTestData = null;
        generatedRecordBindings = [];
        return false;
    }

    private static readonly JsonSerializerOptions GenerationReportJsonOptions = new()
    {
        WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private string BuildGenerationReportJson() => JsonSerializer.Serialize(BuildGenerationReport(), GenerationReportJsonOptions);

    private object BuildGenerationReport()
    {
        if (!grid.EndEdit() || !HasCurrentGenerationReport())
            throw new InvalidDataException("現在のデータに対応する生成条件がありません。再生成してください。");
        var result = generatedTestData!;
        var changed = !records.Select(row => (IEnumerable<string>)row)
            .Zip(result.Records, (actual, original) => actual.SequenceEqual(original)).All(equal => equal);
        return new
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
        };
    }

    private void WriteGenerationReport(string path)
    {
        var report = BuildGenerationReport();
        var target = Path.GetFullPath(path);
        foreach (var source in new[] { generationSourceDefinition, generationSourceData, saveDataPath })
            if (!string.IsNullOrEmpty(source) && File.Exists(source)
                && string.Equals(target, Path.GetFullPath(source), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("元の定義・基準データ・生成データとは別の保存先を選んでください。");
        var temporaryPath = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                JsonSerializer.Serialize(stream, report, GenerationReportJsonOptions);
            File.Move(temporaryPath, target, overwrite: true);
        }
        finally { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
    }

    private void SaveGenerationReport()
    {
        try
        {
            if (!grid.EndEdit()) return;
            if (!HasCurrentGenerationReport()) throw new InvalidDataException("現在のデータに対応する生成条件がありません。再生成してください。");
            using var dialog = new SaveFileDialog
            {
                Title = "生成条件・行と項目の対応表を保存", Filter = "生成条件 JSON (*.json)|*.json",
                FileName = string.IsNullOrEmpty(saveDataPath) ? "generated-test-data-cases.json"
                    : Path.GetFileNameWithoutExtension(saveDataPath) + "-cases.json"
            };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            WriteGenerationReport(dialog.FileName);
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
