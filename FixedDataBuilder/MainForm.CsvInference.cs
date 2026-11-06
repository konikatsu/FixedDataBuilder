namespace FixedDataBuilder;

public sealed partial class MainForm
{
    private bool hasInferredCsvDefinition;
    private string? inferredCsvSourcePath;
    private bool csvInferenceHistoryFailed;
    private readonly ToolStripMenuItem saveInferredDefinitionMenu = new("自動生成した定義を保存...")
    {
        Name = "SaveInferredDefinition", Enabled = false
    };

    private void OpenCsvWithoutDefinition()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "定義なしで読み込むCSVを選択",
            Filter = "CSVデータ (*.csv)|*.csv|すべてのファイル (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) == DialogResult.OK) LoadCsvWithoutDefinitionFromPath(dialog.FileName);
    }

    private void LoadCsvWithoutDefinitionFromPath(string path)
    {
        // The user may save this same CSV here. Analyze only after that choice completes.
        if (!ConfirmDefinitionReplacement("定義なしCSVを開く") || !ConfirmDiscardChanges("定義なしCSVを開く")) return;
        using var dialog = new CsvDefinitionImportForm(path);
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Inference is not { } inference) return;
        try
        {
            ApplyInferredCsv(path, inference);
            SetStatus($"CSVの{fields.Count}列・{records.Count}行を読み込みました。定義は仮生成・未保存です。「定義 → 自動生成した定義を保存...」で保存できます。" + CsvInferenceHistoryNotice());
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "定義なしCSV読込エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ApplyInferredCsv(string path, CsvDefinitionInference inference)
    {
        if (inference.Fields.Count == 0 || inference.Records.Count == 0
            || inference.Records.Any(row => row.Count != inference.Fields.Count))
            throw new InvalidDataException("推定した定義とCSVの列数が一致しません。");
        // Copy first; cancellation or analysis failure must leave the current document untouched.
        var nextFields = inference.Fields.ToArray();
        var nextRecords = inference.Records.Select(row => row.ToList()).ToArray();
        grid.CancelEdit();
        ApplyDefinition("（CSVから自動生成・未保存）", nextFields, DefinitionTextEncoding.Utf8);
        hasInferredCsvDefinition = true;
        inferredCsvSourcePath = path;
        saveInferredDefinitionMenu.Enabled = true;
        records.Clear();
        records.AddRange(nextRecords);
        currentDataFileFormat = DataFileFormat.Csv;
        currentCsvEncoding = inference.Encoding;
        currentCsvHasHeader = inference.HasHeader;
        currentDataEncodingProfile = inference.Encoding switch
        {
            CsvTextEncoding.Utf8 or CsvTextEncoding.Utf16Le or CsvTextEncoding.Utf32Le => DataEncodingProfile.Utf8WithNationalText,
            CsvTextEncoding.StrictShiftJis => DataEncodingProfile.StrictShiftJis,
            _ => DataEncodingProfile.ShiftJis
        };
        currentNationalTextEncoding = NationalTextEncoding.ShiftJis;
        currentSeparatorMode = RecordSeparatorMode.CrLfOrLf;
        saveDataPath = path;
        SetPathText(dataPathTextBox, path);
        layout = GridLayout.RecordRows;
        isSingleRecordView = false;
        singleRecordIndex = 0;
        csvInferenceHistoryFailed = false;
        RememberCsvInferenceHistory(recentDataFiles, path);
        UpdateSeparatorModeText();
        RefreshGrid();
        SetUnsavedChanges(false);
    }

    private void ClearInferredCsvDefinition()
    {
        hasInferredCsvDefinition = false;
        inferredCsvSourcePath = null;
        saveInferredDefinitionMenu.Enabled = false;
    }

    private void RememberCsvInferenceHistory(List<string> history, string path)
    {
        try { AddRecentFile(history, path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // Recent-file persistence is ancillary; the successfully loaded/saved document remains usable.
            csvInferenceHistoryFailed = true;
        }
    }

    private string CsvInferenceHistoryNotice() => csvInferenceHistoryFailed ? "（履歴を保存できませんでした。）" : string.Empty;

    private void ReloadInferredCsv(string path)
    {
        var inference = CsvDefinitionInferer.Analyze(path, currentCsvEncoding).CreateDefinition(currentCsvHasHeader);
        var previousLayout = layout;
        var previousSingleView = isSingleRecordView;
        var previousRecord = singleRecordIndex;
        var previousField = CurrentFieldIndex();
        var hidden = fields.Select(field => field.Name).SequenceEqual(inference.Fields.Select(field => field.Name))
            ? hiddenFieldIndexes.ToArray() : [];
        ApplyInferredCsv(path, inference);
        hiddenFieldIndexes.UnionWith(hidden);
        layout = previousLayout;
        isSingleRecordView = previousSingleView;
        singleRecordIndex = Math.Clamp(previousRecord, 0, records.Count - 1);
        RefreshGrid();
        if (isSingleRecordView) RestoreRecordFieldSelection(singleRecordIndex, previousField);
    }

    private void SaveInferredDefinition()
    {
        if (inferredCsvSourcePath is null || fields.Count == 0) return;
        if (!grid.EndEdit()) return;
        using var dialog = new SaveFileDialog
        {
            Title = "自動生成した定義を保存（保存形式で文字コードを選択）",
            Filter = "定義CSV UTF-8 (*.csv)|*.csv|定義CSV MS932 (CP932) (*.csv)|*.csv|定義CSV SJIS (JIS X 0208) (*.csv)|*.csv",
            FileName = Path.GetFileNameWithoutExtension(inferredCsvSourcePath) + "_定義.csv",
            DefaultExt = "csv", AddExtension = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var encoding = dialog.FilterIndex switch
        {
            2 => DefinitionTextEncoding.Ms932,
            3 => DefinitionTextEncoding.ShiftJis,
            _ => DefinitionTextEncoding.Utf8
        };
        try
        {
            SaveCurrentDefinitionToPath(dialog.FileName, encoding);
            SetStatus($"自動生成した定義を保存しました: {Path.GetFileName(dialog.FileName)}。表示中のデータは保持しています。" + CsvInferenceHistoryNotice());
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "定義保存エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void SaveCurrentDefinitionToPath(string path, DefinitionTextEncoding encoding)
    {
        if (fields.Count == 0 || fields.Any(field => field.IsRedefines))
            throw new InvalidDataException("CSVとして保存できる定義がありません。");
        if (encoding == DefinitionTextEncoding.Auto) encoding = DefinitionTextEncoding.Utf8;
        var target = Path.GetFullPath(path);
        foreach (var source in new[] { inferredCsvSourcePath, GetCurrentDataPath(), saveDataPath })
            if (!string.IsNullOrWhiteSpace(source)
                && string.Equals(target, Path.GetFullPath(source), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("データCSVを定義で上書きできません。別のファイル名を選んでください。");
        // Prepare a complete temporary definition before replacing any existing destination.
        var temporaryPath = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            DefinitionCsvWriter.Write(temporaryPath, fields.Select(field =>
                new DefinitionCsvRow(field.Name, field.DisplayDefinition, field.Comment)).ToArray(), encoding);
            File.Move(temporaryPath, target, overwrite: true);
        }
        finally { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
        hasInferredCsvDefinition = false;
        hasDefinitionEdits = false;
        definitionSaveSuggestionPath = target;
        UpdateDocumentTitle();
        appliedDefinitionTextEncoding = encoding;
        SetDefinitionTextEncoding(encoding);
        SetPathText(definitionPathTextBox, target);
        csvInferenceHistoryFailed = false;
        RememberCsvInferenceHistory(recentDefinitionFiles, target);
        // A definition-only save must not clear pending data edits or change its save target/format.
    }
}
