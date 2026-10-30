using System.Globalization;
using System.Text;

namespace FixedDataBuilder;

/// <summary>Read-only, independent input/output snapshots. Confirmed mappings and row pairs are explicit.</summary>
public sealed class ComparisonForm : Form
{
    private ComparisonDataSet output;
    private ComparisonDataSet? input;
    private readonly Dictionary<int, ComparisonFieldMapping> mappings = [];
    private readonly List<ComparisonRecordPair> recordPairs = [];
    private IReadOnlyList<ComparisonMappingSuggestion> suggestions = [];
    private readonly Dictionary<int, ComparisonResult> displayedResults = [];
    private readonly DataGridView comparisonGrid = new()
    {
        Name = "ComparisonGrid", Dock = DockStyle.Fill, ReadOnly = true,
        AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToOrderColumns = false,
        RowHeadersVisible = false, MultiSelect = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells, EditMode = DataGridViewEditMode.EditProgrammatically
    };
    private readonly TextBox traceText = new() { Name = "ComparisonTrace", Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false };
    private readonly TextBox inputSource = SourceText("ComparisonInputSource");
    private readonly TextBox outputSource = SourceText("ComparisonOutputSource");
    private readonly NumericUpDown inputRecordNumber = RecordNumber("ComparisonInputRecord");
    private readonly NumericUpDown outputRecordNumber = RecordNumber("ComparisonOutputRecord");
    private readonly Label inputCount = new() { AutoSize = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Label outputCount = new() { AutoSize = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Label pairSummary = new() { Name = "ComparisonPairSummary", Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Label statusText = new() { Name = "ComparisonStatus", Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Button editMappingButton = ActionButton("対応を編集・確定…", "ComparisonEditMapping");
    private readonly Button clearMappingButton = ActionButton("項目対応を解除", "ComparisonClearMapping");
    private readonly Button confirmPairButton = ActionButton("この行の組を確定", "ComparisonConfirmPair");
    private readonly Button clearPairButton = ActionButton("出力行の対応解除", "ComparisonClearPair");
    private readonly Button followPairButton = ActionButton("対応する入力行へ", "ComparisonFollowPair");
    private readonly Button keyPairButton = ActionButton("複合キーで行対応…", "ComparisonKeyPair");
    private readonly Button saveSettingsButton = ActionButton("比較設定を保存…", "ComparisonSaveSettings");
    private readonly Button loadSettingsButton = ActionButton("比較設定を読込…", "ComparisonLoadSettings");
    private readonly Button evidenceButton = ActionButton("比較記録を保存…", "ComparisonSaveEvidence");
    private bool refreshing;

    public ComparisonForm(ComparisonDataSet output)
    {
        this.output = output ?? throw new ArgumentNullException(nameof(output));
        Text = "入力・出力の比較（読み取り専用）";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1340, 900);
        MinimumSize = new Size(1040, 680);
        Font = new Font("Yu Gothic UI", 9F);
        BuildLayout();
        ConfigureGrid();
        inputRecordNumber.ValueChanged += (_, _) => { if (!refreshing) RefreshComparison(); };
        outputRecordNumber.ValueChanged += (_, _) => { if (!refreshing) RefreshComparison(); };
        comparisonGrid.SelectionChanged += (_, _) => { if (!refreshing) ShowSelectedTrace(); };
        comparisonGrid.CellDoubleClick += (_, args) => { if (args.RowIndex >= 0) EditSelectedMapping(); };
        editMappingButton.Click += (_, _) => EditSelectedMapping();
        clearMappingButton.Click += (_, _) => ClearSelectedMapping();
        confirmPairButton.Click += (_, _) => ConfirmCurrentPair();
        clearPairButton.Click += (_, _) => ClearCurrentPair();
        followPairButton.Click += (_, _) => FollowCurrentPair();
        keyPairButton.Click += (_, _) => EditKeyPairs();
        saveSettingsButton.Click += (_, _) => SaveSettings();
        loadSettingsButton.Click += (_, _) => LoadSettings();
        evidenceButton.Click += (_, _) => SaveEvidence();
        ConfigureRecordNumbers();
        RefreshComparison();
    }

    private static TextBox SourceText(string name) => new() { Name = name, ReadOnly = true, Multiline = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical };
    private static NumericUpDown RecordNumber(string name) => new() { Name = name, Minimum = 1, Maximum = 1, Width = 86, ThousandsSeparator = true };
    private static Button ActionButton(string text, string name) => new() { Name = name, Text = text, AutoSize = true, Height = 29, Margin = new Padding(3) };
    private static Label Label(string text) => new() { Text = text, AutoSize = true, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(4, 7, 4, 0) };

    private void BuildLayout()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(10), ColumnCount = 1, RowCount = 8 };
        foreach (var height in new[] { 40F, 74F, 108F, 70F, 40F }) root.RowStyles.Add(new(SizeType.Absolute, height));
        root.RowStyles.Add(new(SizeType.Percent, 65));
        root.RowStyles.Add(new(SizeType.Percent, 35));
        root.RowStyles.Add(new(SizeType.Absolute, 38));
        var note = new Label
        {
            Text = "入力と出力を別々に読み、出力項目の順に確認します。入力を使う比較は、項目と行の対応を確定してから行います。\r\n値と元ファイルは変更しません。入力参照のない固定値・式は、入力行を使わず比較します。",
            Dock = DockStyle.Fill
        };
        root.Controls.Add(note, 0, 0);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = true };
        var openInput = ActionButton("入力をファイルから読込…", "ComparisonLoadInput");
        var openOutput = ActionButton("出力をファイルから読込…", "ComparisonLoadOutput");
        openInput.Click += (_, _) => OpenDataSet(isInput: true);
        openOutput.Click += (_, _) => OpenDataSet(isInput: false);
        actions.Controls.AddRange([openInput, openOutput, editMappingButton, clearMappingButton, keyPairButton, saveSettingsButton, loadSettingsButton, evidenceButton]);
        root.Controls.Add(actions, 0, 1);
        var sources = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
        sources.ColumnStyles.Add(new(SizeType.Absolute, 58)); sources.ColumnStyles.Add(new(SizeType.Percent, 100));
        sources.RowStyles.Add(new(SizeType.Percent, 50)); sources.RowStyles.Add(new(SizeType.Percent, 50));
        sources.Controls.Add(Label("入力"), 0, 0); sources.Controls.Add(inputSource, 1, 0);
        sources.Controls.Add(Label("出力"), 0, 1); sources.Controls.Add(outputSource, 1, 1);
        root.Controls.Add(sources, 0, 2);
        var navigation = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        navigation.RowStyles.Add(new(SizeType.Percent, 50)); navigation.RowStyles.Add(new(SizeType.Percent, 50));
        navigation.Controls.Add(NavigationRow("入力行", inputRecordNumber, inputCount), 0, 0);
        navigation.Controls.Add(NavigationRow("出力行", outputRecordNumber, outputCount), 0, 1);
        root.Controls.Add(navigation, 0, 3);
        var pairActions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        pairActions.ColumnStyles.Add(new(SizeType.AutoSize)); pairActions.ColumnStyles.Add(new(SizeType.Percent, 100));
        var pairButtons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
        pairButtons.Controls.AddRange([confirmPairButton, clearPairButton, followPairButton]);
        pairActions.Controls.Add(pairButtons, 0, 0); pairActions.Controls.Add(pairSummary, 1, 0);
        root.Controls.Add(pairActions, 0, 4);
        root.Controls.Add(comparisonGrid, 0, 5);
        var details = new GroupBox { Text = "選択項目の入力元・途中の値・切取単位と起点・条件の分岐・算出できない理由", Dock = DockStyle.Fill, Padding = new Padding(8, 18, 8, 8) };
        details.Controls.Add(traceText);
        root.Controls.Add(details, 0, 6);
        root.Controls.Add(statusText, 0, 7);
        Controls.Add(root);
    }

    private Control NavigationRow(string title, NumericUpDown number, Label count)
    {
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        var previous = ActionButton("前", number.Name + "Previous");
        var next = ActionButton("次", number.Name + "Next");
        previous.Click += (_, _) => { if (number.Enabled && number.Value > number.Minimum) number.Value--; };
        next.Click += (_, _) => { if (number.Enabled && number.Value < number.Maximum) number.Value++; };
        panel.Controls.AddRange([Label(title), previous, number, next, count]);
        return panel;
    }

    private void ConfigureGrid()
    {
        foreach (var (name, title, width) in new[]
        {
            ("OutputOrdinal", "出力No.", 58), ("OutputName", "項目名", 145), ("OutputAttribute", "属性", 100),
            ("InputReferences", "入力参照（候補は未確定）", 150), ("InputValues", "入力値", 160),
            ("Expression", "対応・式", 180), ("ExpectedValue", "期待値", 130), ("OutputValue", "出力値", 130),
            ("ComparisonState", "状態", 125), ("ComparisonReason", "理由", 230)
        })
        {
            comparisonGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = name, HeaderText = title, Width = width, SortMode = DataGridViewColumnSortMode.NotSortable });
        }
        comparisonGrid.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        comparisonGrid.Columns["OutputValue"].DefaultCellStyle.BackColor = Color.FromArgb(235, 245, 255);
        comparisonGrid.Columns["ExpectedValue"].DefaultCellStyle.BackColor = Color.FromArgb(240, 248, 235);
    }

    private void OpenDataSet(bool isInput)
    {
        using var loader = new ComparisonLoadForm(isInput ? "入力" : "出力", isInput ? input : output);
        if (loader.ShowDialog(this) != DialogResult.OK || loader.DataSet is null) return;
        if (isInput) SetInputDataSet(loader.DataSet); else SetOutputDataSet(loader.DataSet);
    }

    private void SetInputDataSet(ComparisonDataSet data)
    {
        input = data ?? throw new ArgumentNullException(nameof(data));
        ResetCorrespondence();
        ConfigureRecordNumbers(resetInput: true, resetOutput: false);
        statusText.Text = "入力を読み込みました。項目の候補と、入力行・出力行の組を確認して確定してください。";
        RefreshComparison();
    }

    private void SetOutputDataSet(ComparisonDataSet data)
    {
        output = data ?? throw new ArgumentNullException(nameof(data));
        ResetCorrespondence();
        ConfigureRecordNumbers(resetInput: false, resetOutput: true);
        statusText.Text = "出力をファイルから読み込み直しました。前の項目対応と行対応を解除しました。";
        RefreshComparison();
    }

    private void ResetCorrespondence()
    {
        mappings.Clear(); recordPairs.Clear(); displayedResults.Clear();
        suggestions = input is null ? [] : ComparisonEngine.SuggestFieldMappings(input, output);
    }

    private void ConfigureRecordNumbers(bool resetInput = true, bool resetOutput = true)
    {
        refreshing = true;
        try
        {
            SetNumber(inputRecordNumber, input?.Records.Count ?? 0, resetInput);
            SetNumber(outputRecordNumber, output.Records.Count, resetOutput);
        }
        finally { refreshing = false; }
        static void SetNumber(NumericUpDown number, int count, bool reset)
        {
            var value = reset ? 1 : Math.Min(number.Value, Math.Max(1, count));
            number.Maximum = Math.Max(1, count); number.Value = value; number.Enabled = count > 0;
        }
    }

    private int? InputIndex => inputRecordNumber.Enabled ? (int)inputRecordNumber.Value - 1 : null;
    private int? OutputIndex => outputRecordNumber.Enabled ? (int)outputRecordNumber.Value - 1 : null;
    private ComparisonRecordPair? StoredCurrentPair => OutputIndex is int outputIndex ? recordPairs.LastOrDefault(pair => pair.OutputRecordIndex == outputIndex) : null;
    private ComparisonRecordPair? CurrentPair => StoredCurrentPair is { } pair && pair.Kind is ComparisonPairKind.Manual or ComparisonPairKind.KeyUnique
        && pair.InputRecordIndex == InputIndex ? pair : null;
    private ComparisonRecordPair? EvidencePair => CurrentPair
        ?? (StoredCurrentPair is { Kind: ComparisonPairKind.Ambiguous or ComparisonPairKind.OutputOnly } stored ? stored
            : OutputIndex is int index ? new ComparisonRecordPair(null, index, ComparisonPairKind.Unconfirmed) : null);
    private static bool HasNoInputReferences(ComparisonFieldMapping? mapping) => mapping?.Kind is ComparisonMappingKind.Fixed or ComparisonMappingKind.Expression
        && mapping.Expression is not null && ComparisonEngine.GetInputReferences(mapping.Expression).Count == 0;

    private void RefreshComparison()
    {
        if (refreshing) return;
        var selected = comparisonGrid.CurrentRow?.Index ?? 0;
        refreshing = true;
        try
        {
            inputSource.Text = input is null ? "未読込。入力定義（CSV・COPY）とデータを別途選択してください。" : DescribeDataSet(input);
            outputSource.Text = DescribeDataSet(output);
            inputCount.Text = $"/ {input?.Records.Count ?? 0:N0} 件（入力だけを移動）";
            outputCount.Text = $"/ {output.Records.Count:N0} 件（出力だけを移動）";
            editMappingButton.Enabled = clearMappingButton.Enabled = input is not null && output.Fields.Count > 0;
            confirmPairButton.Enabled = input is not null && InputIndex.HasValue && OutputIndex.HasValue;
            saveSettingsButton.Enabled = loadSettingsButton.Enabled = keyPairButton.Enabled = input is not null;
            clearPairButton.Enabled = StoredCurrentPair is not null;
            followPairButton.Enabled = StoredCurrentPair is { InputRecordIndex: not null, Kind: ComparisonPairKind.Manual or ComparisonPairKind.KeyUnique };
            evidenceButton.Enabled = input is not null && OutputIndex.HasValue;
            pairSummary.Text = PairDescription();
            comparisonGrid.Rows.Clear(); displayedResults.Clear();
            for (var ordinal = 0; ordinal < output.Fields.Count; ordinal++) AddComparisonRow(ordinal);
            if (comparisonGrid.Rows.Count > 0)
                comparisonGrid.CurrentCell = comparisonGrid[0, Math.Min(selected, comparisonGrid.Rows.Count - 1)];
        }
        finally { refreshing = false; }
        ShowSelectedTrace();
    }

    private void AddComparisonRow(int ordinal)
    {
        var field = output.Fields[ordinal];
        mappings.TryGetValue(ordinal, out var mapping);
        var suggestion = suggestions.FirstOrDefault(item => item.OutputFieldOrdinal == ordinal);
        var references = mapping?.Inputs ?? [];
        var candidate = mapping is null || mapping.Kind == ComparisonMappingKind.Unmapped;
        var referenceText = candidate && suggestion?.Candidates.Count > 0
            ? "候補: " + string.Join(", ", suggestion.Candidates.Select(item => $"No.{item.InputFieldOrdinal + 1}"))
            : string.Join(", ", references.Select(item => $"No.{item.FieldOrdinal + 1}"));
        var actualSource = OutputIndex is int outputIndex ? output.ReadSource(outputIndex, ordinal, mapping?.OutputSourceKind ?? ComparisonSourceKind.LogicalValue) : null;
        var actual = actualSource?.Value;
        ComparisonResult result;
        if (actualSource is { Success: false }) result = new(ComparisonResultStatus.CannotEvaluate, null, null, actualSource.Reason);
        else if (input is null) result = new(ComparisonResultStatus.InputUnmapped, null, actual, "入力ファイルを読み込んでください。");
        else if (candidate)
            result = new(suggestion?.IsAmbiguous == true ? ComparisonResultStatus.Ambiguous : ComparisonResultStatus.FieldUnmapped,
                null, actual, suggestion?.Candidates.Count > 0 ? "入力候補です。対応を編集して確定してください。" : "対応する入力・固定値・式が未設定です。");
        else if (HasNoInputReferences(mapping) && OutputIndex is int independentOutput)
        {
            result = ComparisonEngine.Compare(input, output, new(null, independentOutput, ComparisonPairKind.Unconfirmed), mapping!);
            result = result with { Reason = result.Reason + "／入力参照なし（入力行の対応は不要）" };
        }
        else if (StoredCurrentPair?.Kind == ComparisonPairKind.Ambiguous)
            result = new(ComparisonResultStatus.Ambiguous, null, actual, "キーが重複しています。入力行を確認して手動で行対応を確定してください。");
        else if (StoredCurrentPair?.Kind == ComparisonPairKind.OutputOnly)
            result = new(ComparisonResultStatus.InputUnmapped, null, actual, "この出力行のキーに対応する入力行がありません。");
        else if (CurrentPair is not { } pair)
            result = new(ComparisonResultStatus.InputUnmapped, null, actual, "現在の入力行と出力行の組が未確定です。");
        else result = ComparisonEngine.Compare(input, output, pair, mapping!);
        displayedResults[ordinal] = result;
        var inputValue = input is null || InputIndex is not int inputIndex ? "" : candidate
            ? string.Join(" / ", (suggestion?.Candidates ?? []).Select(item =>
            {
                var source = input.ReadSource(inputIndex, item.InputFieldOrdinal);
                return $"候補No.{item.InputFieldOrdinal + 1}=[{(source.Success ? source.Value : "取得不可: " + source.Reason)}]";
            }))
            : string.Join(" / ", references.Select(item =>
            {
                var source = input.ReadSource(inputIndex, item.FieldOrdinal, item.SourceKind);
                return $"No.{item.FieldOrdinal + 1}=[{(source.Success ? source.Value : "取得不可")}]";
            }));
        var state = candidate && result.Status == ComparisonResultStatus.FieldUnmapped && suggestion?.Candidates.Count > 0
            ? "候補（未確定）" : StateText(result.Status);
        var row = comparisonGrid.Rows[comparisonGrid.Rows.Add(ordinal + 1, field.Name, field.DisplayDefinition, referenceText,
            inputValue, mapping is null ? "未設定" : ExpressionText(mapping), result.ExpectedValue, result.OutputValue, state, result.Reason)];
        row.Tag = ordinal;
        row.Cells["ComparisonState"].Style.BackColor = result.Status is ComparisonResultStatus.DirectValueEqual or ComparisonResultStatus.CalculatedExpectedEqual
            ? Color.FromArgb(220, 242, 220) : result.Status is ComparisonResultStatus.DirectValueDifferent or ComparisonResultStatus.CalculatedExpectedDifferent
                ? Color.FromArgb(255, 225, 225) : Color.FromArgb(255, 248, 217);
    }

    private static string DescribeDataSet(ComparisonDataSet data)
    {
        var origin = !data.HasSourceBytes ? "現在の表示値の控え（元ファイルのバイトなし）" : data.Profile.Format == DataFileFormat.FixedLength
            ? "ファイル読込値（元固定長バイト保持）" : "ファイル読込値（CSVの引用符を除いた論理値）";
        var profile = data.Profile.Format == DataFileFormat.Csv
            ? $"CSV / {JapaneseEncodings.Name(data.Profile.CsvEncoding)} / ヘッダー{(data.Profile.CsvHasHeader ? "あり" : "なし")}" :
            $"固定長 / X: {JapaneseEncodings.Name(data.Profile.DataEncoding)} / N: {NationalTextEncodingHelper.DisplayName(data.Profile.NationalEncoding)} / {(data.Profile.SeparatorMode == RecordSeparatorMode.None ? "改行なし" : "改行あり")}";
        return $"{data.DefinitionPath ?? "定義の控え"} | {data.DataPath ?? "データの控え"}\r\n{profile} / {origin}";
    }

    private string PairDescription()
    {
        if (input is null) return "入力未読込";
        if (StoredCurrentPair is { Kind: ComparisonPairKind.Ambiguous } ambiguous)
            return "キー重複・未確定。入力候補: " + string.Join(", ", ambiguous.InputCandidates?.Select(index => (index + 1).ToString(CultureInfo.InvariantCulture)) ?? []);
        if (StoredCurrentPair?.Kind == ComparisonPairKind.OutputOnly) return "出力のみ。この出力行のキーに対応する入力行がありません。";
        if (CurrentPair is { } pair) return $"確定: 入力 {pair.InputRecordIndex + 1} ↔ 出力 {pair.OutputRecordIndex + 1}（{(pair.Kind == ComparisonPairKind.Manual ? "手動" : "一意キー")}）";
        if (StoredCurrentPair is { InputRecordIndex: int index }) return $"この出力行の確定入力は {index + 1} 行です。現在の入力行とは異なります。";
        return "現在の行の組は未確定。入力・出力の行番号を別々に選び、組を確定してください。";
    }

    private static string StateText(ComparisonResultStatus status) => ComparisonEvidence.StateText(status);

    private static string ExpressionText(ComparisonFieldMapping mapping) => mapping.Kind switch
    {
        ComparisonMappingKind.Unmapped => "未設定", ComparisonMappingKind.Ambiguous => "曖昧（未確定）",
        ComparisonMappingKind.Input => "入力をそのまま", ComparisonMappingKind.Fixed => "固定値: " + ExpressionText(mapping.Expression),
        _ => ExpressionText(mapping.Expression)
    };

    private static string ExpressionText(ComparisonExpression? expression) => expression switch
    {
        ComparisonInputExpression reference => $"入力No.{reference.FieldOrdinal + 1}（{SourceKindText(reference.SourceKind)}）",
        ComparisonLiteralExpression literal => $"[{literal.Value}]",
        ComparisonSliceExpression slice => $"切取({ExpressionText(slice.Source)}, 開始{slice.Start}, 長さ{slice.Length}, {(slice.Unit == ComparisonSliceUnit.Bytes ? "バイト" : "文字")}, {(int)slice.Origin}起点)",
        ComparisonConcatExpression concat => "結合(" + string.Join(" + ", concat.Parts.Select(ExpressionText)) + ")",
        ComparisonPadExpression pad => $"{(pad.Side == ComparisonPadSide.Left ? "左" : "右")}埋め({ExpressionText(pad.Source)}, {pad.TargetLength}, [{pad.Fill}])",
        ComparisonConditionalExpression conditional => $"条件({ExpressionText(conditional.Left)} = {ExpressionText(conditional.Right)} → {ExpressionText(conditional.WhenEqual)} / {ExpressionText(conditional.WhenNotEqual)})",
        ComparisonUnsupportedExpression unsupported => "未対応: " + unsupported.Description, _ => "式なし"
    };

    private static string SourceKindText(ComparisonSourceKind kind) => kind switch
    {
        ComparisonSourceKind.LogicalValue => "論理値", ComparisonSourceKind.FixedStorageText => "元固定長文字列",
        ComparisonSourceKind.RawFixedBytes => "元固定長バイト", ComparisonSourceKind.EncodedCsvLogicalValue => "CSV論理値の符号化", _ => "未対応"
    };

    private void ShowSelectedTrace()
    {
        if (comparisonGrid.CurrentRow?.Tag is not int ordinal) { traceText.Text = "比較する出力項目がありません。"; return; }
        var builder = new StringBuilder();
        var field = output.Fields[ordinal];
        builder.AppendLine($"出力No.{ordinal + 1} {field.Name} / {field.DisplayDefinition}");
        builder.AppendLine($"選択行: 入力 {(InputIndex is int i ? (i + 1).ToString() : "なし")} / 出力 {(OutputIndex is int o ? (o + 1).ToString() : "なし")}");
        builder.AppendLine(PairDescription());
        if (mappings.TryGetValue(ordinal, out var mapping))
        {
            builder.AppendLine("確定した対応: " + ExpressionText(mapping));
            if (HasNoInputReferences(mapping)) builder.AppendLine("入力参照なし。固定値・式だけで期待値を計算するため、入力行の対応は必要ありません。");
            builder.AppendLine("出力値の種別: " + SourceKindText(mapping.OutputSourceKind));
            if (!string.IsNullOrEmpty(mapping.Note)) builder.AppendLine("確認メモ: " + mapping.Note);
            if (input is not null && InputIndex is int recordIndex)
                foreach (var reference in mapping.Inputs)
                {
                    var source = input.ReadSource(recordIndex, reference.FieldOrdinal, reference.SourceKind);
                    builder.AppendLine($"入力No.{reference.FieldOrdinal + 1} {input.Fields[reference.FieldOrdinal].Name}: [{source.Value}] / {source.Description}");
                    if (!source.Success) builder.AppendLine("取得不可: " + source.Reason);
                }
        }
        else
        {
            builder.AppendLine("項目対応は未確定です。候補の値が同じでも一致として扱いません。");
            var suggestion = suggestions.FirstOrDefault(item => item.OutputFieldOrdinal == ordinal);
            if (suggestion is not null)
            {
                foreach (var candidate in suggestion.Candidates)
                    builder.AppendLine($"候補 入力No.{candidate.InputFieldOrdinal + 1} {candidate.InputName}: {string.Join(" / ", candidate.Evidence)}");
                foreach (var issue in suggestion.Issues) builder.AppendLine("候補の注意: " + issue);
            }
        }
        if (displayedResults.TryGetValue(ordinal, out var result))
        {
            builder.AppendLine("状態: " + StateText(result.Status));
            builder.AppendLine("理由: " + result.Reason);
            if (result.Evaluation is not null)
                foreach (var step in result.Evaluation.Trace) builder.AppendLine($"{step.Operation}: [{step.Value}] / {step.Description}");
            builder.AppendLine($"期待値: [{result.ExpectedValue}] / 出力値: [{result.OutputValue}]");
        }
        builder.AppendLine("空白・先頭ゼロは除かず比較します。固定長の論理値には既存読込時の整形があり、元格納値とは区別します。");
        traceText.Text = builder.ToString();
    }

    private void EditSelectedMapping()
    {
        if (input is null || comparisonGrid.CurrentRow?.Tag is not int ordinal) return;
        mappings.TryGetValue(ordinal, out var existing);
        using var editor = new ComparisonRuleForm(input, output, ordinal, existing, suggestions.FirstOrDefault(item => item.OutputFieldOrdinal == ordinal));
        if (editor.ShowDialog(this) != DialogResult.OK || editor.Mapping is null) return;
        RunAction(() => ApplyMapping(editor.Mapping));
    }

    private void ApplyMapping(ComparisonFieldMapping mapping)
    {
        if (input is null) throw new InvalidOperationException("入力を先に読み込んでください。");
        var proposed = mappings.Values.Where(item => item.OutputFieldOrdinal != mapping.OutputFieldOrdinal).Append(mapping).ToArray();
        ComparisonSettingsStore.Validate(input, output, proposed, recordPairs);
        mappings[mapping.OutputFieldOrdinal] = mapping;
        statusText.Text = $"出力No.{mapping.OutputFieldOrdinal + 1}の対応を確定しました。";
        RefreshComparison();
    }

    private void ClearSelectedMapping()
    {
        if (comparisonGrid.CurrentRow?.Tag is not int ordinal) return;
        mappings.Remove(ordinal);
        statusText.Text = $"出力No.{ordinal + 1}の確定対応を解除しました。";
        RefreshComparison();
    }

    private void ConfirmCurrentPair()
    {
        if (input is null || InputIndex is not int inputIndex || OutputIndex is not int outputIndex) return;
        recordPairs.RemoveAll(pair => pair.OutputRecordIndex == outputIndex
            || pair.Kind == ComparisonPairKind.InputOnly && pair.InputRecordIndex == inputIndex);
        recordPairs.Add(new(inputIndex, outputIndex, ComparisonPairKind.Manual));
        statusText.Text = $"入力{inputIndex + 1}行と出力{outputIndex + 1}行の組を手動で確定しました。";
        RefreshComparison();
    }

    private void ClearCurrentPair()
    {
        if (OutputIndex is not int index) return;
        recordPairs.RemoveAll(pair => pair.OutputRecordIndex == index);
        RefreshComparison();
    }

    private void FollowCurrentPair()
    {
        if (StoredCurrentPair is { InputRecordIndex: int index, Kind: ComparisonPairKind.Manual or ComparisonPairKind.KeyUnique }) inputRecordNumber.Value = index + 1;
    }

    private void EditKeyPairs()
    {
        if (input is null) return;
        using var dialog = new ComparisonKeyForm(input.Fields, output.Fields);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        RunAction(() => ApplyKeyPairs(dialog.InputKeys, dialog.OutputKeys));
    }

    private void ApplyKeyPairs(IReadOnlyList<int> inputKeys, IReadOnlyList<int> outputKeys)
    {
        if (input is null) throw new InvalidOperationException("入力を先に読み込んでください。");
        var pairs = ComparisonEngine.PairByKeys(input, output, inputKeys, outputKeys);
        ComparisonSettingsStore.Validate(input, output, mappings.Values.ToArray(), pairs);
        recordPairs.Clear(); recordPairs.AddRange(pairs);
        statusText.Text = $"キーの行対応: 一意{pairs.Count(pair => pair.Kind == ComparisonPairKind.KeyUnique)}件 / 曖昧{pairs.Count(pair => pair.Kind == ComparisonPairKind.Ambiguous)}件 / 入力のみ{pairs.Count(pair => pair.Kind == ComparisonPairKind.InputOnly)}件 / 出力のみ{pairs.Count(pair => pair.Kind == ComparisonPairKind.OutputOnly)}件。重複は手動確認してください。";
        RefreshComparison();
    }

    private void SaveSettings()
    {
        using var dialog = new SaveFileDialog { Title = "比較設定を保存", Filter = "比較設定 (*.comparison.json)|*.comparison.json|JSON (*.json)|*.json", FileName = "comparison.comparison.json" };
        if (dialog.ShowDialog(this) == DialogResult.OK) RunAction(() => SaveSettingsToPath(dialog.FileName));
    }

    private void SaveSettingsToPath(string path)
    {
        if (input is null) throw new InvalidOperationException("入力を先に読み込んでください。");
        EnsureSeparateDestination(path);
        ComparisonSettingsStore.Save(path, ComparisonSettingsStore.Create(input, output, mappings.Values.OrderBy(item => item.OutputFieldOrdinal).ToArray(), recordPairs), input, output);
        statusText.Text = "項目対応・式・行対応と両側の指紋を保存しました。";
    }

    private void LoadSettings()
    {
        using var dialog = new OpenFileDialog { Title = "比較設定を読込", Filter = "比較設定 (*.json)|*.json|すべてのファイル (*.*)|*.*" };
        if (dialog.ShowDialog(this) == DialogResult.OK) LoadSettingsWithFeedback(dialog.FileName);
    }

    private void LoadSettingsWithFeedback(string path) => RunAction(() => LoadSettingsFromPath(path));

    private bool LoadSettingsFromPath(string path)
    {
        if (input is null) throw new InvalidOperationException("入力を先に読み込んでください。");
        var loaded = ComparisonSettingsStore.Load(path, input, output);
        if (!loaded.CanApply)
        {
            statusText.Text = "比較設定は適用していません。定義・データ・設定内容を確認してください。";
            traceText.Text = "読込設定は適用していません。現在の対応は保持しています。\r\n" + string.Join("\r\n", loaded.Issues);
            return false;
        }
        mappings.Clear(); foreach (var mapping in loaded.Settings.Mappings) mappings[mapping.OutputFieldOrdinal] = mapping;
        recordPairs.Clear(); recordPairs.AddRange(loaded.Settings.RecordPairs);
        statusText.Text = "定義とレコードの指紋が一致した比較設定を読み込みました。";
        RefreshComparison();
        return true;
    }

    private void SaveEvidence()
    {
        if (input is null || EvidencePair is null) return;
        using var dialog = new SaveFileDialog { Title = "現在の行の比較記録を保存", Filter = "比較記録 (*.json)|*.json", FileName = "comparison-evidence.json" };
        if (dialog.ShowDialog(this) == DialogResult.OK) RunAction(() => SaveEvidenceToPath(dialog.FileName));
    }

    private void SaveEvidenceToPath(string path)
    {
        if (input is null || EvidencePair is not { } pair) throw new InvalidOperationException("入力を読み込み、比較する出力行を選択してください。");
        EnsureSeparateDestination(path);
        var allMappings = Enumerable.Range(0, output.Fields.Count).Select(ordinal => mappings.TryGetValue(ordinal, out var mapping)
            ? mapping : new ComparisonFieldMapping(ordinal, ComparisonMappingKind.Unmapped, [])).ToArray();
        ComparisonEvidence.Write(path, input, output, pair, allMappings,
            new ComparisonEvidenceContext(InputIndex, CurrentPair is not null, StoredCurrentPair, suggestions));
        statusText.Text = "現在の行対応・式・期待値・出力値・算出過程の記録を保存しました。";
    }

    private void EnsureSeparateDestination(string path)
    {
        var target = Path.GetFullPath(path);
        foreach (var source in new[] { input?.DefinitionPath, input?.DataPath, output.DefinitionPath, output.DataPath }
            .Where(item => !string.IsNullOrWhiteSpace(item) && File.Exists(item)))
            if (string.Equals(target, Path.GetFullPath(source!), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("元の定義・データファイルには保存できません。別のファイル名を指定してください。");
    }

    private void RunAction(Action action)
    {
        try { action(); }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { statusText.Text = exception.Message; }
    }

    private sealed class ComparisonLoadForm : Form
    {
        private readonly TextBox definitionPath = new() { Dock = DockStyle.Fill };
        private readonly TextBox dataPath = new() { Dock = DockStyle.Fill };
        private readonly ComboBox definitionEncoding = Combo();
        private readonly ComboBox format = Combo();
        private readonly ComboBox csvEncoding = Combo();
        private readonly ComboBox separator = Combo();
        private readonly ComboBox national = Combo();
        private readonly ComboBox textEncoding = Combo();
        private readonly Label detectionLabel = new() { Name = "ComparisonEncodingDetection", Dock = DockStyle.Fill, AutoEllipsis = true };
        private readonly Label error = new() { Dock = DockStyle.Fill, ForeColor = Color.DarkRed };
        private bool updatingEncoding;
        private bool csvManuallySelected;
        private bool textManuallySelected;
        public ComparisonDataSet? DataSet { get; private set; }
        private static ComboBox Combo() => new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };

        public ComparisonLoadForm(string side, ComparisonDataSet? current)
        {
            Text = side + "ファイルの読込"; StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(820, 548); MinimumSize = new Size(690, 550);
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14), ColumnCount = 3, RowCount = 11 };
            layout.ColumnStyles.Add(new(SizeType.Absolute, 180)); layout.ColumnStyles.Add(new(SizeType.Percent, 100)); layout.ColumnStyles.Add(new(SizeType.Absolute, 80));
            for (var row = 0; row < 8; row++) layout.RowStyles.Add(new(SizeType.Absolute, 36));
            layout.RowStyles.Add(new(SizeType.Absolute, 62)); layout.RowStyles.Add(new(SizeType.Percent, 100)); layout.RowStyles.Add(new(SizeType.Absolute, 40));
            definitionEncoding.Items.AddRange(DefinitionFileText.Names);
            format.Items.AddRange(["固定長", "CSV（ヘッダーなし）", "CSV（ヘッダーあり）"]);
            csvEncoding.Items.AddRange(JapaneseEncodings.CsvNames);
            separator.Items.AddRange(["改行あり（CRLF / LF）", "改行なし"]);
            national.Items.AddRange(["MS932 (CP932)", "UTF-16LE", "UTF-32LE", "SJIS (JIS X 0208)"]);
            textEncoding.Items.AddRange(JapaneseEncodings.TextNames);
            var names = new[] { "定義（CSV・COPY）", "データ", "定義の文字コード", "データ形式", "CSV文字コード", "固定長の改行", "固定長のN文字コード", "固定長のX文字コード" };
            Control[] controls = [definitionPath, dataPath, definitionEncoding, format, csvEncoding, separator, national, textEncoding];
            for (var row = 0; row < controls.Length; row++)
            {
                layout.Controls.Add(Label(names[row]), 0, row); layout.Controls.Add(controls[row], 1, row);
                if (row >= 2) layout.SetColumnSpan(controls[row], 2);
            }
            var browseDefinition = ActionButton("参照…", "ComparisonBrowseDefinition");
            var browseData = ActionButton("参照…", "ComparisonBrowseData");
            browseDefinition.Dock = browseData.Dock = DockStyle.Fill;
            browseDefinition.Click += (_, _) => Browse(definitionPath, "定義CSV・COPY (*.csv;*.cbl;*.cpy;*.cob)|*.csv;*.cbl;*.cpy;*.cob|すべてのファイル (*.*)|*.*");
            browseData.Click += (_, _) => Browse(dataPath, "データ (*.csv;*.dat;*.txt)|*.csv;*.dat;*.txt|すべてのファイル (*.*)|*.*");
            layout.Controls.Add(browseDefinition, 2, 0); layout.Controls.Add(browseData, 2, 1);
            var detection = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
            detection.ColumnStyles.Add(new(SizeType.Absolute, 160)); detection.ColumnStyles.Add(new(SizeType.Percent, 100));
            var detect = ActionButton("文字コードを再判別", "ComparisonDetectEncoding");
            detect.Click += (_, _) => DetectInitialEncoding(force: true);
            detection.Controls.Add(detect, 0, 0); detection.Controls.Add(detectionLabel, 1, 0);
            layout.Controls.Add(detection, 0, 8); layout.SetColumnSpan(detection, 3);
            layout.Controls.Add(error, 0, 9); layout.SetColumnSpan(error, 3);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
            var cancel = ActionButton("キャンセル", "ComparisonCancelLoad"); cancel.DialogResult = DialogResult.Cancel;
            var load = ActionButton("読み込む", "ComparisonConfirmLoad");
            load.Click += (_, _) => LoadData();
            buttons.Controls.AddRange([cancel, load]); layout.Controls.Add(buttons, 0, 10); layout.SetColumnSpan(buttons, 3);
            Controls.Add(layout); AcceptButton = load; CancelButton = cancel;
            var profile = current?.Profile ?? new ComparisonDataProfile();
            definitionPath.Text = current?.DefinitionPath ?? ""; dataPath.Text = current?.DataPath ?? "";
            definitionEncoding.SelectedIndex = (int)(current?.DefinitionEncoding ?? DefinitionTextEncoding.Auto);
            format.SelectedIndex = profile.Format == DataFileFormat.Csv ? profile.CsvHasHeader ? 2 : 1 : 0;
            csvEncoding.SelectedIndex = (int)profile.CsvEncoding; separator.SelectedIndex = profile.SeparatorMode == RecordSeparatorMode.None ? 1 : 0;
            national.SelectedIndex = (int)profile.NationalEncoding; textEncoding.SelectedIndex = (int)profile.DataEncoding;
            // A previously selected side profile remains explicit. New choices are detected only
            // until the corresponding codec is edited; loading never performs another detection.
            csvManuallySelected = textManuallySelected = current is not null;
            csvEncoding.SelectedIndexChanged += (_, _) => { if (!updatingEncoding) csvManuallySelected = true; };
            textEncoding.SelectedIndexChanged += (_, _) => { if (!updatingEncoding) textManuallySelected = true; };
            format.SelectedIndexChanged += (_, _) => { UpdateFormat(); DetectInitialEncoding(force: false); };
            definitionEncoding.SelectedIndexChanged += (_, _) => DetectInitialEncoding(force: false);
            separator.SelectedIndexChanged += (_, _) => DetectInitialEncoding(force: false);
            national.SelectedIndexChanged += (_, _) => DetectInitialEncoding(force: false);
            definitionPath.Leave += (_, _) => DetectInitialEncoding(force: false);
            dataPath.Leave += (_, _) => DetectInitialEncoding(force: false);
            UpdateFormat(); DetectInitialEncoding(force: false);
        }

        private void UpdateFormat()
        {
            csvEncoding.Enabled = format.SelectedIndex > 0;
            separator.Enabled = national.Enabled = textEncoding.Enabled = format.SelectedIndex == 0;
            error.Text = "選択した側の読込条件です。もう一方のデータ・メイン画面のデータは変更しません。";
        }

        private void Browse(TextBox target, string filter)
        {
            using var dialog = new OpenFileDialog { Filter = filter, FileName = target.Text };
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                target.Text = dialog.FileName;
                DetectInitialEncoding(force: false);
            }
        }

        private void DetectInitialEncoding(bool force)
        {
            if (!File.Exists(dataPath.Text)) { detectionLabel.Text = "データ選択後に初期候補を表示します。型N・改行は手動指定です。"; return; }
            try
            {
                DataEncodingDetection detection;
                if (format.SelectedIndex > 0) detection = DataEncodingDetector.Csv(dataPath.Text);
                else
                {
                    if (!File.Exists(definitionPath.Text)) { detectionLabel.Text = "固定長の判別には定義も選択してください。型N・改行は手動指定です。"; return; }
                    var extension = Path.GetExtension(definitionPath.Text);
                    var definitionMode = (DefinitionTextEncoding)definitionEncoding.SelectedIndex;
                    IReadOnlyList<FieldDefinition> fields = extension.Equals(".csv", StringComparison.OrdinalIgnoreCase)
                        ? DefinitionCsvReader.Read(definitionPath.Text, definitionMode)
                        : CopybookDefinitionReader.Read(definitionPath.Text, definitionMode);
                    var width = NationalTextEncodingHelper.FixedByteWidth((NationalTextEncoding)national.SelectedIndex);
                    fields = fields.Select(field => field.Type == FieldDataType.FullWidthText ? field with { NationalByteWidth = width } : field).ToArray();
                    detection = DataEncodingDetector.Fixed(dataPath.Text, fields,
                        separator.SelectedIndex == 1 ? RecordSeparatorMode.None : RecordSeparatorMode.CrLfOrLf);
                }
                var manual = format.SelectedIndex > 0 ? csvManuallySelected : textManuallySelected;
                updatingEncoding = true;
                try
                {
                    // ASCII identifies no unique producer encoding; preserve either side's selection.
                    if ((force || !manual) && detection.Encoding is not null && !detection.Description.StartsWith("ASCIIのみ", StringComparison.Ordinal))
                    {
                        if (format.SelectedIndex > 0) csvEncoding.SelectedIndex = (int)detection.Encoding.Value;
                        else textEncoding.SelectedIndex = (int)detection.FixedEncoding((DataEncodingProfile)textEncoding.SelectedIndex);
                    }
                    if (force) { if (format.SelectedIndex > 0) csvManuallySelected = false; else textManuallySelected = false; }
                }
                finally { updatingEncoding = false; }
                var description = detection.Description.StartsWith("ASCIIのみ", StringComparison.Ordinal)
                    ? "ASCIIのみ（UTF-8・SJIS・MS932共通。現在の指定を保持）" : detection.Description;
                detectionLabel.Text = description + (manual && !force ? "／手動指定を保持" : "") + "\r\n型N・改行・定義の文字コードは変更しません。";
            }
            catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException or OverflowException or FormatException)
            { detectionLabel.Text = "判別できません：" + exception.Message; }
        }

        private void LoadData()
        {
            DataSet = null;
            DialogResult = DialogResult.None;
            try
            {
                if (string.IsNullOrWhiteSpace(definitionPath.Text) || string.IsNullOrWhiteSpace(dataPath.Text)) throw new InvalidDataException("定義とデータを選択してください。");
                var profile = new ComparisonDataProfile(format.SelectedIndex > 0 ? DataFileFormat.Csv : DataFileFormat.FixedLength,
                    (CsvTextEncoding)csvEncoding.SelectedIndex, format.SelectedIndex == 2,
                    separator.SelectedIndex == 1 ? RecordSeparatorMode.None : RecordSeparatorMode.CrLfOrLf,
                    (DataEncodingProfile)textEncoding.SelectedIndex, (NationalTextEncoding)national.SelectedIndex);
                var loaded = ComparisonDataSet.Load(definitionPath.Text, dataPath.Text, profile, (DefinitionTextEncoding)definitionEncoding.SelectedIndex);
                DataSet = loaded;
                DialogResult = DialogResult.OK;
            }
            catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException or OverflowException or FormatException)
            { error.Text = exception.Message; }
        }
    }

    private sealed class ComparisonKeyForm : Form
    {
        private readonly TextBox inputKeys = new() { Dock = DockStyle.Fill };
        private readonly TextBox outputKeys = new() { Dock = DockStyle.Fill };
        private readonly Label error = new() { Dock = DockStyle.Fill, ForeColor = Color.DarkRed };
        public IReadOnlyList<int> InputKeys { get; private set; } = [];
        public IReadOnlyList<int> OutputKeys { get; private set; } = [];

        public ComparisonKeyForm(IReadOnlyList<FieldDefinition> inputFields, IReadOnlyList<FieldDefinition> outputFields)
        {
            Text = "複合キーによる行対応の補助"; StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(820, 540); MinimumSize = new Size(680, 450);
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 2, RowCount = 5 };
            root.ColumnStyles.Add(new(SizeType.Percent, 50)); root.ColumnStyles.Add(new(SizeType.Percent, 50));
            root.RowStyles.Add(new(SizeType.Absolute, 62)); root.RowStyles.Add(new(SizeType.Percent, 100));
            root.RowStyles.Add(new(SizeType.Absolute, 32)); root.RowStyles.Add(new(SizeType.Absolute, 45)); root.RowStyles.Add(new(SizeType.Absolute, 40));
            var help = new Label { Dock = DockStyle.Fill, Text = "入力・出力のキー項目番号を同じ順で指定します（例: 1,3）。空白・先頭ゼロも含め完全一致で照合します。\r\nキーが重複した行は曖昧として残し、候補の先頭を自動選択しません。既存の行対応を置き換えます。" };
            root.Controls.Add(help, 0, 0); root.SetColumnSpan(help, 2);
            root.Controls.Add(FieldList("入力項目", inputFields), 0, 1); root.Controls.Add(FieldList("出力項目", outputFields), 1, 1);
            root.Controls.Add(inputKeys, 0, 2); root.Controls.Add(outputKeys, 1, 2);
            root.Controls.Add(error, 0, 3); root.SetColumnSpan(error, 2);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
            var cancel = ActionButton("キャンセル", "ComparisonCancelKeys"); cancel.DialogResult = DialogResult.Cancel;
            var apply = ActionButton("一意キーを対応・重複を保留", "ComparisonApplyKeys");
            apply.Click += (_, _) =>
            {
                try
                {
                    var proposedInput = Parse(inputKeys.Text, inputFields.Count);
                    var proposedOutput = Parse(outputKeys.Text, outputFields.Count);
                    if (proposedInput.Length != proposedOutput.Length) throw new InvalidDataException("入力と出力のキー項目数をそろえてください。");
                    InputKeys = proposedInput; OutputKeys = proposedOutput; DialogResult = DialogResult.OK;
                }
                catch (Exception exception) when (exception is InvalidDataException or FormatException or OverflowException) { error.Text = exception.Message; }
            };
            buttons.Controls.AddRange([cancel, apply]); root.Controls.Add(buttons, 0, 4); root.SetColumnSpan(buttons, 2);
            Controls.Add(root); AcceptButton = apply; CancelButton = cancel;
        }

        private static Control FieldList(string title, IReadOnlyList<FieldDefinition> fields) => new TextBox
        {
            Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
            Text = title + "\r\n" + string.Join("\r\n", fields.Select((field, index) => $"No.{index + 1} {field.Name} ({field.DisplayDefinition})"))
        };
        private static int[] Parse(string text, int count)
        {
            var values = text.Split([',', '、', ' ', '\u3000'], StringSplitOptions.RemoveEmptyEntries)
                .Select(part => int.Parse(part, NumberStyles.None, CultureInfo.InvariantCulture) - 1).ToArray();
            if (values.Length == 0 || values.Any(index => index < 0 || index >= count) || values.Distinct().Count() != values.Length)
                throw new InvalidDataException("各側の有効な項目番号を重複なしで指定してください。");
            return values;
        }
    }
}
