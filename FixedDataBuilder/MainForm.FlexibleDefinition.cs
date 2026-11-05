namespace FixedDataBuilder;

public sealed partial class MainForm
{
    private string? pendingDataPath;
    private bool hasDefinitionEdits;
    private string? definitionSaveSuggestionPath;
    private readonly ToolStripMenuItem saveCurrentDefinitionMenu = new("現在の定義を保存...")
    {
        Name = "SaveCurrentDefinition", Enabled = false
    };

    private void UpdateDocumentTitle() => Text = WindowTitle
        + (hasUnsavedChanges ? " 【未保存】" : string.Empty)
        + (hasDefinitionEdits ? " 【定義未保存】" : string.Empty);

    private void ClearFlexibleDefinitionState()
    {
        pendingDataPath = null;
        hasDefinitionEdits = false;
        definitionSaveSuggestionPath = null;
        UpdateDocumentTitle();
    }

    private void StageDataBeforeDefinition(string path)
    {
        if (fields.Count != 0) throw new InvalidOperationException("読み込み済みの定義を使用してください。");
        if (!File.Exists(path)) throw new FileNotFoundException("データファイルが見つかりません。", path);
        pendingDataPath = path;
        saveDataPath = null;
        SetPathText(dataPathTextBox, path);
        separatorModeTextBox.Text = "データ選択済み・定義の読み込み待ち";
        SetStatus("データを選択しました。「ファイル → 定義を開く...」で定義を選ぶと、読込条件を確認して読み込みます。");
    }

    private bool ConfirmDefinitionReplacement(string action)
    {
        if (!hasDefinitionEdits) return true;
        var answer = MessageBox.Show(this, $"変更した定義を保存してから{action}しますか？",
            "定義の変更が未保存です", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button3);
        return answer == DialogResult.No || answer == DialogResult.Yes && SaveCurrentDefinitionDialog();
    }

    private void EditDefinitionWhileKeepingData()
    {
        if (fields.Count == 0) { SetStatus("編集する定義を読み込むか、CSVを定義なしで開いてください。"); return; }
        if (!grid.EndEdit()) return;
        using var editor = new DefinitionEditorForm(fields, InitialDefinitionSaveEncoding(true), applyToData: true);
        if (editor.ShowDialog(this) != DialogResult.OK || editor.EditedFields is not { } nextFields) return;
        try
        {
            ApplyEditedDefinition(nextFields);
            SetStatus("定義を表示へ適用しました。データの値は保持しています。「定義 → 現在の定義を保存...」で保存してください。");
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "定義の適用エラー", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void ApplyEditedDefinition(IReadOnlyList<FieldDefinition> nextFields)
    {
        var suggestion = definitionSaveSuggestionPath ?? GetCurrentDefinitionPath();
        var originalData = inferredCsvSourcePath;
        ApplyDefinitionPreservingValues("（定義編集中・未保存）", nextFields, appliedDefinitionTextEncoding);
        inferredCsvSourcePath = originalData;
        hasDefinitionEdits = true;
        definitionSaveSuggestionPath = suggestion;
        UpdateDocumentTitle();
    }

    private void ApplyDefinitionPreservingValues(string path, IReadOnlyList<FieldDefinition> nextFields,
        DefinitionTextEncoding encoding)
    {
        var loadedFields = nextFields.Select(field => !IsCsv && field.Type == FieldDataType.FullWidthText
            ? field with { NationalByteWidth = NationalTextEncodingHelper.FixedByteWidth(currentNationalTextEncoding) } : field).ToArray();
        if (loadedFields.Length == 0 || loadedFields.Any(field => field.Length <= 0 || !field.IsRedefines && field.StorageByteLength <= 0))
            throw new InvalidDataException("定義の項目数と長さを確認してください。");
        var indexes = fields.Select((field, index) => (field, index)).Where(item => !item.field.IsRedefines)
            .Select(item => item.index).ToArray();
        if (loadedFields.Count(field => !field.IsRedefines) != indexes.Length)
            throw new InvalidDataException($"データの列数 {indexes.Length} と定義の列数 {loadedFields.Count(field => !field.IsRedefines)} が一致しません。現在のデータは保持しています。");
        if (records.Any(row => row.Count != fields.Count)) throw new InvalidDataException("現在のデータの項目数が一致しません。");
        var copied = records.Select(row =>
        {
            var column = 0;
            return loadedFields.Select(field => field.IsRedefines
                ? field.Type is FieldDataType.PlainNumber or FieldDataType.SignedNumber or FieldDataType.PackedUnsigned or FieldDataType.PackedSigned ? "0" : string.Empty
                : row[indexes[column++]]).ToList();
        }).ToArray();
        var previousDirty = hasUnsavedChanges;
        var previousRecord = singleRecordIndex;
        var changedBytes = IsCsv ? currentCsvHasHeader && !fields.Where(field => !field.IsRedefines).Select(field => field.Name)
            .SequenceEqual(loadedFields.Where(field => !field.IsRedefines).Select(field => field.Name))
            : !fields.Select(field => field with { Name = "", Comment = "" })
                .SequenceEqual(loadedFields.Select(field => field with { Name = "", Comment = "" }));
        grid.CancelEdit();
        ApplyDefinition(path, loadedFields, encoding);
        records.Clear();
        records.AddRange(copied);
        singleRecordIndex = Math.Clamp(previousRecord, 0, Math.Max(0, records.Count - 1));
        UpdateSeparatorModeText();
        RefreshGrid();
        SetUnsavedChanges(previousDirty || changedBytes);
    }

    private bool LoadDefinitionFromPathCore(string path, string statusMessage, bool preserveData)
    {
        if (!ConfirmDefinitionReplacement("定義を開く")) return false;
        // Replacing the definition over existing data is an ordinal application, not a data reload.
        var keepValues = preserveData && pendingDataPath is null && fields.Count > 0 && records.Count > 0
            && (hasUnsavedChanges || !string.IsNullOrWhiteSpace(GetCurrentDataPath()));
        if (!keepValues && !ConfirmDiscardChanges("定義を開く")) return false;
        try
        {
            var definition = ReadDefinition(path);
            if (definition is null) return false;
            var (loadedFields, loadedEncoding) = definition.Value;
            if (preserveData && pendingDataPath is { } stagedPath)
            {
                var defaultEncoding = (IsCopybookPath(path)
                    ? DataEncodingProfile.Utf8WithNationalText : DataEncodingProfile.ShiftJis);
                var options = SelectDataLoadOptionsCore(stagedPath, loadedFields, path, defaultEncoding);
                if (options is null) return false;
                ApplyDataAndDefinition(path, loadedFields, loadedEncoding, stagedPath,
                    new DataSaveOptions(options.IsCsv ? DataFileFormat.Csv : DataFileFormat.FixedLength,
                        options.CsvEncoding, options.SeparatorMode, options.NationalTextEncoding, options.TextEncoding, options.CsvHasHeader));
                SetStatus("先に選択したデータと定義を読み込みました。");
                return true;
            }
            if (keepValues)
            {
                ApplyDefinitionPreservingValues(path, loadedFields, loadedEncoding);
                RememberCsvInferenceHistory(recentDefinitionFiles, path);
                SetStatus(statusMessage + " データは項目順で保持しています。");
                return true;
            }
            // A blank/new document keeps the existing definition-first workflow.
            currentDataFileFormat = DataFileFormat.FixedLength;
            currentCsvHasHeader = false;
            currentDataEncodingProfile = (IsCopybookPath(path)
                ? DataEncodingProfile.Utf8WithNationalText : DataEncodingProfile.ShiftJis);
            currentNationalTextEncoding = NationalTextEncoding.ShiftJis;
            ApplyDefinition(path, loadedFields, loadedEncoding);
            ClearPathText(dataPathTextBox);
            saveDataPath = null;
            records.Clear();
            records.Add(CreateEmptyRecord());
            RememberCsvInferenceHistory(recentDefinitionFiles, path);
            UpdateSeparatorModeText();
            RefreshGrid();
            SetUnsavedChanges(false);
            SetStatus(statusMessage);
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "定義読込エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    private void ApplyDataAndDefinition(string definitionPath, IReadOnlyList<FieldDefinition> loadedFields,
        DefinitionTextEncoding definitionEncoding, string dataPath, DataSaveOptions options)
    {
        var isCsv = options.Format == DataFileFormat.Csv;
        var preparedFields = loadedFields.Select(field => !isCsv && field.Type == FieldDataType.FullWidthText
            ? field with { NationalByteWidth = NationalTextEncodingHelper.FixedByteWidth(options.NationalEncoding) } : field).ToArray();
        var profile = options.TextEncoding ?? DataEncodingProfile.ShiftJis;
        if (!isCsv) EnsureSampleSelectionForDefinition(dataPath, options.SeparatorMode, options.NationalEncoding, definitionPath);
        var preparedRecords = isCsv ? CsvDataReader.Read(dataPath, preparedFields, options.CsvEncoding, options.CsvHasHeader)
            : FixedLengthDataReader.Read(dataPath, preparedFields, options.SeparatorMode, profile, options.NationalEncoding);
        // Both files and selected conditions are valid before changing the current document.
        grid.CancelEdit();
        ApplyDefinition(definitionPath, preparedFields, definitionEncoding);
        currentDataFileFormat = options.Format;
        currentCsvEncoding = options.CsvEncoding;
        currentCsvHasHeader = isCsv && options.CsvHasHeader;
        currentSeparatorMode = options.SeparatorMode;
        currentDataEncodingProfile = profile;
        currentNationalTextEncoding = options.NationalEncoding;
        records.Clear();
        records.AddRange(preparedRecords);
        saveDataPath = dataPath;
        SetPathText(dataPathTextBox, dataPath);
        RememberCsvInferenceHistory(recentDefinitionFiles, definitionPath);
        RememberCsvInferenceHistory(recentDataFiles, dataPath);
        UpdateSeparatorModeText();
        RefreshGrid();
        SetUnsavedChanges(false);
        RememberCurrentLoadSettings(dataPath);
    }

    private bool SaveCurrentDefinitionDialog()
    {
        if (fields.Count == 0 || !grid.EndEdit()) return false;
        var source = definitionSaveSuggestionPath ?? GetCurrentDefinitionPath();
        using var dialog = new SaveFileDialog
        {
            Title = "現在の定義を保存（保存形式で文字コードを選択）",
            Filter = "定義CSV UTF-8 (*.csv)|*.csv|定義CSV MS932 (CP932) (*.csv)|*.csv|定義CSV SJIS (JIS X 0208) (*.csv)|*.csv",
            FileName = !string.IsNullOrWhiteSpace(source) && File.Exists(source)
                ? Path.GetFileNameWithoutExtension(source) + "_修正.csv"
                : !string.IsNullOrWhiteSpace(GetCurrentDataPath()) ? Path.GetFileNameWithoutExtension(GetCurrentDataPath()) + "_定義.csv" : "definition.csv",
            DefaultExt = "csv", AddExtension = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return false;
        try
        {
            SaveCurrentDefinitionToPath(dialog.FileName, dialog.FilterIndex switch
            {
                2 => DefinitionTextEncoding.Ms932, 3 => DefinitionTextEncoding.ShiftJis, _ => DefinitionTextEncoding.Utf8
            });
            SetStatus("定義を保存しました。データの値と編集状態は保持しています。" + CsvInferenceHistoryNotice());
            return true;
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "定義保存エラー", MessageBoxButtons.OK, MessageBoxIcon.Error); return false; }
    }
}
