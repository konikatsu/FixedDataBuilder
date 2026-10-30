using System.Text.Json;
using System.Text.Json.Serialization;

namespace FixedDataBuilder;

/// <summary>Builds an explicit typed rule for one output field without changing either data set.</summary>
public sealed class ComparisonRuleForm : Form
{
    private readonly ComparisonDataSet input;
    private readonly ComparisonDataSet output;
    private readonly int outputFieldOrdinal;
    private readonly ComboBox operationCombo = DropDown("ComparisonOperation");
    private readonly ComboBox inputFieldCombo = DropDown("ComparisonInputField");
    private readonly ComboBox sourceKindCombo = DropDown("ComparisonInputSourceKind");
    private readonly ComboBox outputSourceCombo = DropDown("ComparisonOutputSourceKind");
    private readonly TextBox literalText = new() { Name = "ComparisonLiteral", Multiline = true, Height = 48, ScrollBars = ScrollBars.Vertical };
    private readonly ComboBox sliceUnitCombo = DropDown("ComparisonSliceUnit");
    private readonly ComboBox originCombo = DropDown("ComparisonOrigin");
    private readonly NumericUpDown startNumber = Number("ComparisonSliceStart", 0, int.MaxValue, 1);
    private readonly NumericUpDown lengthNumber = Number("ComparisonSliceLength", 0, ComparisonExpressionEvaluator.MaxValueLength, 1);
    private readonly CheckBox concatSliceCheck = new() { Name = "ComparisonConcatSlice", Text = "追加する入力を切り取る", AutoSize = true };
    private readonly ListBox concatParts = new() { Name = "ComparisonConcatParts", Height = 96, HorizontalScrollbar = true };
    private readonly ComboBox padSideCombo = DropDown("ComparisonPadSide");
    private readonly NumericUpDown targetNumber = Number("ComparisonPadLength", 0, ComparisonExpressionEvaluator.MaxValueLength, 1);
    private readonly TextBox fillText = new() { Name = "ComparisonPadFill", Text = "0", Width = 80 };
    private readonly ComboBox equalityCombo = DropDown("ComparisonEquality");
    private readonly TextBox compareText = new() { Name = "ComparisonConditionValue" };
    private readonly BranchControls trueBranch = new("ComparisonTrueBranch");
    private readonly BranchControls falseBranch = new("ComparisonFalseBranch");
    private readonly TextBox jsonText = new() { Name = "ComparisonExpressionJson", Multiline = true,
        Height = 165, ScrollBars = ScrollBars.Both, WordWrap = false, Font = new Font("Consolas", 10) };
    private readonly TextBox noteText = new() { Name = "ComparisonRuleNote" };
    private readonly NumericUpDown previewRecordNumber = Number("ComparisonPreviewRecord", 1, 1, 1);
    private readonly TextBox previewText = new() { Name = "ComparisonRulePreview", ReadOnly = true, Multiline = true,
        ScrollBars = ScrollBars.Both, WordWrap = false, Dock = DockStyle.Fill };
    private readonly Label validationLabel = new() { Name = "ComparisonRuleValidation", AutoSize = true,
        ForeColor = Color.DarkRed, MaximumSize = new Size(820, 0) };
    private readonly Control inputPanel;
    private readonly Control literalPanel;
    private readonly Control slicePanel;
    private readonly Control concatPanel;
    private readonly Control padPanel;
    private readonly Control conditionPanel;
    private readonly Control jsonPanel;
    private static readonly JsonSerializerOptions ExpressionJsonOptions = new()
    {
        WriteIndented = true, MaxDepth = 128, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    public ComparisonFieldMapping? Mapping { get; private set; }

    public ComparisonRuleForm(ComparisonDataSet input, ComparisonDataSet output, int outputFieldOrdinal,
        ComparisonFieldMapping? existing = null, ComparisonMappingSuggestion? suggestion = null)
    {
        this.input = input;
        this.output = output;
        this.outputFieldOrdinal = outputFieldOrdinal;
        if (outputFieldOrdinal < 0 || outputFieldOrdinal >= output.Fields.Count)
            throw new ArgumentOutOfRangeException(nameof(outputFieldOrdinal));
        Text = "比較ルールを設定";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(900, 820);
        MinimumSize = new Size(760, 650);
        Font = SystemFonts.MessageBoxFont;

        AddChoices(operationCombo,
            (RuleOperation.Input, "入力値をそのまま比べる"), (RuleOperation.Literal, "固定値と比べる"),
            (RuleOperation.Slice, "入力を切り取る"), (RuleOperation.Concat, "複数の入力を順番に結合する"),
            (RuleOperation.Pad, "入力の左・右を文字で埋める"), (RuleOperation.Conditional, "条件により値を選ぶ"),
            (RuleOperation.Advanced, "詳細な式（JSON補助）"));
        PopulateFields(inputFieldCombo);
        PopulateFields(trueBranch.Field);
        PopulateFields(falseBranch.Field);
        PopulateSourceKinds(sourceKindCombo, input);
        PopulateSourceKinds(outputSourceCombo, output);
        AddChoices(sliceUnitCombo, (ComparisonSliceUnit.UnicodeCodePoints, "文字（Unicodeコードポイント）"), (ComparisonSliceUnit.Bytes, "バイト"));
        AddChoices(originCombo, (ComparisonIndexOrigin.One, "1始まり"), (ComparisonIndexOrigin.Zero, "0始まり"));
        AddChoices(padSideCombo, (ComparisonPadSide.Left, "左に埋める"), (ComparisonPadSide.Right, "右に埋める"));
        AddChoices(equalityCombo, (ComparisonEqualityMode.StringOrdinal, "文字列の完全一致（空白・先頭ゼロを区別）"),
            (ComparisonEqualityMode.ExactDecimal, "厳密な十進数の一致（空白・指数・桁区切り不可）"));
        operationCombo.SelectedIndex = sliceUnitCombo.SelectedIndex = originCombo.SelectedIndex = padSideCombo.SelectedIndex = equalityCombo.SelectedIndex = 0;
        previewRecordNumber.Maximum = Math.Max(1, input.Records.Count);
        previewRecordNumber.Enabled = input.Records.Count > 0;

        inputPanel = Rows(("入力項目", inputFieldCombo), ("入力の扱い", sourceKindCombo));
        literalPanel = Rows(("固定値", literalText));
        slicePanel = Rows(("切取単位", sliceUnitCombo), ("位置の起点", originCombo),
            ("開始位置", startNumber), ("長さ", lengthNumber));
        var addPart = new Button { Name = "ComparisonAddPart", Text = "選択した入力を末尾へ追加", AutoSize = true };
        var removePart = new Button { Text = "削除", AutoSize = true };
        var upPart = new Button { Text = "上へ", AutoSize = true };
        var downPart = new Button { Text = "下へ", AutoSize = true };
        addPart.Click += (_, _) => TryAddConcatPart();
        removePart.Click += (_, _) => { if (concatParts.SelectedIndex >= 0) concatParts.Items.RemoveAt(concatParts.SelectedIndex); };
        upPart.Click += (_, _) => MovePart(-1);
        downPart.Click += (_, _) => MovePart(1);
        concatPanel = Rows(("結合の追加方法", Flow(concatSliceCheck, addPart)),
            ("結合順（上から）", concatParts), ("順序の変更", Flow(upPart, downPart, removePart)),
            ("空の結合", new Label { AutoSize = true, Text = "結合要素が0個なら、期待値は空文字になります。\r\n空の固定値へは置き換えず、結合ルールとして保存します。" }));
        padPanel = Rows(("埋める方向", padSideCombo), ("目標の文字数", targetNumber), ("埋め文字（1文字）", fillText));
        conditionPanel = Rows(("条件の比較方法", equalityCombo), ("比較する値", compareText),
            ("条件が一致した時", trueBranch.Panel), ("一致しなかった時", falseBranch.Panel));
        var jsonHelp = new Label { AutoSize = true, Text = "通常の操作で作れない多段の式に使います。自由文・C#・SQLなどは実行しません。" };
        jsonPanel = Rows(("詳細の式", Stack(jsonHelp, jsonText)));

        var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, AutoScroll = true, Padding = new Padding(12) };
        body.ColumnStyles.Add(new(SizeType.Percent, 100));
        void AddBody(Control control, bool fill = false)
        {
            control.Dock = DockStyle.Fill;
            var row = body.RowCount++;
            body.RowStyles.Add(new(fill ? SizeType.Percent : SizeType.AutoSize, fill ? 100 : 0));
            body.Controls.Add(control, 0, row);
        }
        var outputField = output.Fields[outputFieldOrdinal];
        AddBody(new Label { AutoSize = true, Text = $"出力No.{outputFieldOrdinal + 1}  {outputField.Name}  / {outputField.DisplayDefinition}" });
        AddBody(new TextBox { Name = "ComparisonOriginalComment", ReadOnly = true, Multiline = true, Height = 52,
            ScrollBars = ScrollBars.Vertical, Text = outputField.Comment, AccessibleName = "元のコメント（変更しません）" });
        if (suggestion is not null)
            AddBody(new Label { AutoSize = true, MaximumSize = new Size(820, 0), Text = "参照候補（確定前）: "
                + (suggestion.Candidates.Count == 0 ? "なし" : string.Join(" / ", suggestion.Candidates.Select(candidate =>
                    $"入力No.{candidate.InputFieldOrdinal + 1} {candidate.InputName} [{string.Join(", ", candidate.Evidence)}]")))
                + (suggestion.Issues.Count > 0 ? Environment.NewLine + string.Join(Environment.NewLine, suggestion.Issues) : "") });
        AddBody(Rows(("比べる方法", operationCombo), ("出力の扱い", outputSourceCombo)));
        AddBody(inputPanel); AddBody(literalPanel); AddBody(slicePanel); AddBody(concatPanel);
        AddBody(padPanel); AddBody(conditionPanel); AddBody(jsonPanel);
        AddBody(Rows(("比較用メモ", noteText)));
        AddBody(new Label { AutoSize = true, MaximumSize = new Size(820, 0),
            Text = "空の固定値は有効です。空白・先頭ゼロは保持します。文字単位はUnicodeコードポイントで、結合文字は別々に数えます。\r\n入力例で計算できることを確認してから確定します。他のレコードの範囲は比較時にも確認します。" });
        var previewButton = new Button { Name = "ComparisonPreviewRule", Text = "期待値を確認", AutoSize = true };
        previewButton.Click += (_, _) => TryBuildAndPreview(commit: false);
        AddBody(Flow(new Label { AutoSize = true, Text = "例示する入力レコード", Padding = new Padding(0, 6, 0, 0) }, previewRecordNumber, previewButton));
        previewText.MinimumSize = new Size(0, 100);
        AddBody(previewText, fill: true);
        AddBody(validationLabel);

        var ok = new Button { Name = "ComparisonConfirmRule", Text = "このルールを使う", AutoSize = true };
        var cancel = new Button { Text = "キャンセル", DialogResult = DialogResult.Cancel, AutoSize = true };
        ok.Click += (_, _) => TryBuildAndPreview(commit: true);
        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(8), WrapContents = false };
        bottom.Controls.AddRange([cancel, ok]);
        Controls.Add(body); Controls.Add(bottom);
        AcceptButton = ok; CancelButton = cancel;
        operationCombo.SelectedIndexChanged += (_, _) => { if (operationCombo.SelectedIndex >= 0) UpdatePanels(); };
        concatSliceCheck.CheckedChanged += (_, _) => UpdatePanels();
        sliceUnitCombo.SelectedIndexChanged += (_, _) =>
        {
            if (sliceUnitCombo.SelectedIndex >= 0 && Selected<ComparisonSliceUnit>(sliceUnitCombo) == ComparisonSliceUnit.Bytes)
                Select(sourceKindCombo, input.Profile.Format == DataFileFormat.Csv
                    ? ComparisonSourceKind.EncodedCsvLogicalValue : ComparisonSourceKind.RawFixedBytes);
        };
        if (existing is not null) LoadMapping(existing);
        else if (suggestion?.Candidates.Count == 1) Select(inputFieldCombo, suggestion.Candidates[0].InputFieldOrdinal);
        UpdatePanels();
    }

    private ComparisonFieldMapping BuildMapping()
    {
        var operation = Selected<RuleOperation>(operationCombo);
        ComparisonExpression expression = operation switch
        {
            RuleOperation.Input => BuildInput(),
            RuleOperation.Literal => new ComparisonLiteralExpression(literalText.Text),
            RuleOperation.Slice => BuildSlice(BuildInput()),
            RuleOperation.Concat => new ComparisonConcatExpression(concatParts.Items.Cast<PartItem>().Select(item => item.Expression).ToArray()),
            RuleOperation.Pad => new ComparisonPadExpression(BuildInput(), (int)targetNumber.Value, fillText.Text, Selected<ComparisonPadSide>(padSideCombo)),
            RuleOperation.Conditional => new ComparisonConditionalExpression(BuildInput(), new ComparisonLiteralExpression(compareText.Text),
                Selected<ComparisonEqualityMode>(equalityCombo), BuildBranch(trueBranch), BuildBranch(falseBranch)),
            RuleOperation.Advanced => ParseExpressionJson(),
            _ => throw new InvalidDataException("比べる方法を選んでください。")
        };
        try { ComparisonExpressionEvaluator.Validate(expression, input.Fields.Count); }
        catch (ComparisonExpressionEvaluator.ComparisonExpressionException ex) { throw new InvalidDataException(ex.Message, ex); }
        var references = ComparisonEngine.GetInputReferences(expression);
        foreach (var reference in references)
        {
            if (reference.SourceKind is ComparisonSourceKind.FixedStorageText or ComparisonSourceKind.RawFixedBytes
                && (input.Profile.Format != DataFileFormat.FixedLength || !input.HasSourceBytes))
                throw new InvalidDataException("元固定長の格納値・バイトがありません。入力を元ファイルから読み込んでください。");
            if (reference.SourceKind == ComparisonSourceKind.EncodedCsvLogicalValue && input.Profile.Format != DataFileFormat.Csv)
                throw new InvalidDataException("CSV論理値の符号化はCSV入力だけで使用できます。");
        }
        var kind = operation == RuleOperation.Input ? ComparisonMappingKind.Input
            : operation == RuleOperation.Literal ? ComparisonMappingKind.Fixed : ComparisonMappingKind.Expression;
        return new(outputFieldOrdinal, kind, references, kind == ComparisonMappingKind.Input ? null : expression,
            Selected<ComparisonSourceKind>(outputSourceCombo), noteText.Text);
    }

    private ComparisonInputExpression BuildInput() =>
        new(Selected<int>(inputFieldCombo), Selected<ComparisonSourceKind>(sourceKindCombo));

    private ComparisonSliceExpression BuildSlice(ComparisonExpression source) => new(source, (int)startNumber.Value,
        (int)lengthNumber.Value, Selected<ComparisonSliceUnit>(sliceUnitCombo), Selected<ComparisonIndexOrigin>(originCombo));

    private ComparisonExpression BuildBranch(BranchControls branch) => Selected<BranchKind>(branch.Kind) switch
    {
        BranchKind.Literal => new ComparisonLiteralExpression(branch.Value.Text),
        BranchKind.SameInput => BuildInput(),
        BranchKind.OtherInput => new ComparisonInputExpression(Selected<int>(branch.Field), Selected<ComparisonSourceKind>(sourceKindCombo)),
        _ => throw new InvalidDataException("条件分岐の値を指定してください。")
    };

    private ComparisonExpression ParseExpressionJson()
    {
        if (jsonText.Text.Length > ComparisonSettingsStore.MaxFileBytes)
            throw new InvalidDataException("詳細な式が長さ上限を超えます。");
        try
        {
            return JsonSerializer.Deserialize<ComparisonExpression>(jsonText.Text, ExpressionJsonOptions)
                ?? throw new InvalidDataException("詳細な式が空です。");
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        { throw new InvalidDataException("詳細な式のJSON・演算の種類が不正です。", ex); }
    }

    private bool TryBuildAndPreview(bool commit)
    {
        Mapping = null;
        try
        {
            var mapping = BuildMapping();
            var expression = mapping.Kind == ComparisonMappingKind.Input
                ? new ComparisonInputExpression(mapping.Inputs[0].FieldOrdinal, mapping.Inputs[0].SourceKind) : mapping.Expression!;
            if (Selected<RuleOperation>(operationCombo) != RuleOperation.Advanced)
                jsonText.Text = JsonSerializer.Serialize(expression, ExpressionJsonOptions);
            var result = ComparisonEngine.Evaluate(input, input.Records.Count == 0 ? null : (int)previewRecordNumber.Value - 1, expression);
            previewText.Text = string.Join(Environment.NewLine, result.Trace.Select(step =>
                $"{step.Operation}: [{step.Value}]  {step.Description}"));
            if (!result.Success) throw new InvalidDataException(result.Reason ?? "期待値を計算できません。");
            validationLabel.Text = "";
            previewText.Text = $"期待値: [{result.ExpectedValue}]  UTF-16長={result.ExpectedValue!.Length}（業務の合否判定ではありません）"
                + Environment.NewLine + previewText.Text;
            if (commit)
            {
                Mapping = mapping;
                DialogResult = DialogResult.OK;
                Close();
            }
            return true;
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException or InvalidOperationException)
        {
            validationLabel.Text = ex.Message;
            return false;
        }
    }

    private void TryAddConcatPart()
    {
        try
        {
            ComparisonExpression part = BuildInput();
            if (concatSliceCheck.Checked) part = BuildSlice(part);
            ComparisonExpressionEvaluator.Validate(part, input.Fields.Count);
            AddPart(part);
            validationLabel.Text = "";
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException
            or ComparisonExpressionEvaluator.ComparisonExpressionException)
        { validationLabel.Text = ex.Message; }
    }

    private void AddPart(ComparisonExpression expression)
    {
        if (concatParts.Items.Count >= ComparisonExpressionEvaluator.MaxReferences)
            throw new InvalidDataException("結合する要素数が上限を超えます。");
        var source = expression is ComparisonSliceExpression slice ? (ComparisonInputExpression)slice.Source : (ComparisonInputExpression)expression;
        var description = $"入力No.{source.FieldOrdinal + 1} {input.Fields[source.FieldOrdinal].Name} / {SourceLabel(source.SourceKind)}";
        if (expression is ComparisonSliceExpression cutting)
            description += $" / 切取 開始{cutting.Start}, 長さ{cutting.Length}, {(int)cutting.Origin}始まり, {cutting.Unit}";
        concatParts.Items.Add(new PartItem(expression, description));
        concatParts.SelectedIndex = concatParts.Items.Count - 1;
    }

    private void MovePart(int delta)
    {
        var from = concatParts.SelectedIndex;
        var to = from + delta;
        if (from < 0 || to < 0 || to >= concatParts.Items.Count) return;
        var item = concatParts.Items[from];
        concatParts.Items.RemoveAt(from); concatParts.Items.Insert(to, item); concatParts.SelectedIndex = to;
    }

    private void LoadMapping(ComparisonFieldMapping mapping)
    {
        noteText.Text = mapping.Note;
        Select(outputSourceCombo, mapping.OutputSourceKind);
        if (mapping.Kind == ComparisonMappingKind.Input && mapping.Inputs.Count == 1)
        {
            LoadInput(new(mapping.Inputs[0].FieldOrdinal, mapping.Inputs[0].SourceKind));
            Select(operationCombo, RuleOperation.Input);
            return;
        }
        var expression = mapping.Expression;
        if (expression is null)
        {
            if (mapping.Inputs.Count == 1) LoadInput(new(mapping.Inputs[0].FieldOrdinal, mapping.Inputs[0].SourceKind));
            return;
        }
        jsonText.Text = JsonSerializer.Serialize(expression, ExpressionJsonOptions);
        switch (expression)
        {
            case ComparisonLiteralExpression literal:
                literalText.Text = literal.Value; Select(operationCombo, RuleOperation.Literal); return;
            case ComparisonInputExpression reference:
                LoadInput(reference); Select(operationCombo, RuleOperation.Input); return;
            case ComparisonSliceExpression { Source: ComparisonInputExpression reference } slice:
                LoadSlice(slice); LoadInput(reference); Select(operationCombo, RuleOperation.Slice); return;
            case ComparisonPadExpression { Source: ComparisonInputExpression reference } pad:
                LoadInput(reference); targetNumber.Value = Math.Clamp(pad.TargetLength, 0, (int)targetNumber.Maximum);
                fillText.Text = pad.Fill; Select(padSideCombo, pad.Side); Select(operationCombo, RuleOperation.Pad); return;
            case ComparisonConcatExpression concat when concat.Parts.All(IsSimplePart):
                foreach (var part in concat.Parts) AddPart(part);
                Select(operationCombo, RuleOperation.Concat); return;
            case ComparisonConditionalExpression { Left: ComparisonInputExpression reference, Right: ComparisonLiteralExpression literal } condition
                when IsSimpleBranch(condition.WhenEqual) && IsSimpleBranch(condition.WhenNotEqual)
                    && BranchSourcesMatch(condition.WhenEqual, reference.SourceKind) && BranchSourcesMatch(condition.WhenNotEqual, reference.SourceKind):
                LoadInput(reference); compareText.Text = literal.Value; Select(equalityCombo, condition.EqualityMode);
                LoadBranch(trueBranch, condition.WhenEqual, reference); LoadBranch(falseBranch, condition.WhenNotEqual, reference);
                Select(operationCombo, RuleOperation.Conditional); return;
            default: Select(operationCombo, RuleOperation.Advanced); return;
        }
    }

    private void LoadInput(ComparisonInputExpression reference)
    { Select(inputFieldCombo, reference.FieldOrdinal); Select(sourceKindCombo, reference.SourceKind); }

    private void LoadSlice(ComparisonSliceExpression slice)
    {
        Select(sliceUnitCombo, slice.Unit); Select(originCombo, slice.Origin);
        startNumber.Value = Math.Clamp(slice.Start, 0, int.MaxValue);
        lengthNumber.Value = Math.Clamp(slice.Length, 0, (int)lengthNumber.Maximum);
    }

    private static bool IsSimplePart(ComparisonExpression part) => part is ComparisonInputExpression
        or ComparisonSliceExpression { Source: ComparisonInputExpression };
    private static bool IsSimpleBranch(ComparisonExpression branch) => branch is ComparisonLiteralExpression or ComparisonInputExpression;
    private static bool BranchSourcesMatch(ComparisonExpression branch, ComparisonSourceKind kind) =>
        branch is not ComparisonInputExpression reference || reference.SourceKind == kind;

    private void LoadBranch(BranchControls controls, ComparisonExpression branch, ComparisonInputExpression conditionInput)
    {
        if (branch is ComparisonLiteralExpression literal)
        { Select(controls.Kind, BranchKind.Literal); controls.Value.Text = literal.Value; }
        else if (branch is ComparisonInputExpression reference)
        {
            Select(controls.Kind, reference == conditionInput ? BranchKind.SameInput : BranchKind.OtherInput);
            Select(controls.Field, reference.FieldOrdinal);
        }
    }

    private void UpdatePanels()
    {
        var operation = Selected<RuleOperation>(operationCombo);
        inputPanel.Visible = operation is RuleOperation.Input or RuleOperation.Slice or RuleOperation.Concat or RuleOperation.Pad or RuleOperation.Conditional;
        literalPanel.Visible = operation == RuleOperation.Literal;
        slicePanel.Visible = operation == RuleOperation.Slice || operation == RuleOperation.Concat && concatSliceCheck.Checked;
        concatPanel.Visible = operation == RuleOperation.Concat;
        padPanel.Visible = operation == RuleOperation.Pad;
        conditionPanel.Visible = operation == RuleOperation.Conditional;
        jsonPanel.Visible = operation == RuleOperation.Advanced;
    }

    private void PopulateFields(ComboBox combo)
    {
        for (var index = 0; index < input.Fields.Count; index++)
            combo.Items.Add(new Choice<int>(index, $"No.{index + 1}  {input.Fields[index].Name} / {input.Fields[index].DisplayDefinition}"));
    }

    private static void PopulateSourceKinds(ComboBox combo, ComparisonDataSet data)
    {
        combo.Items.Add(new Choice<ComparisonSourceKind>(ComparisonSourceKind.LogicalValue, SourceLabel(ComparisonSourceKind.LogicalValue)));
        if (data.Profile.Format == DataFileFormat.Csv)
            combo.Items.Add(new Choice<ComparisonSourceKind>(ComparisonSourceKind.EncodedCsvLogicalValue, SourceLabel(ComparisonSourceKind.EncodedCsvLogicalValue)));
        else if (data.HasSourceBytes)
        {
            combo.Items.Add(new Choice<ComparisonSourceKind>(ComparisonSourceKind.FixedStorageText, SourceLabel(ComparisonSourceKind.FixedStorageText)));
            combo.Items.Add(new Choice<ComparisonSourceKind>(ComparisonSourceKind.RawFixedBytes, SourceLabel(ComparisonSourceKind.RawFixedBytes)));
        }
        combo.SelectedIndex = 0;
    }

    private static string SourceLabel(ComparisonSourceKind kind) => kind switch
    {
        ComparisonSourceKind.LogicalValue => "論理値（固定長は読込時の空白除去・数値整形後／CSVは空白・先頭ゼロ保持）",
        ComparisonSourceKind.FixedStorageText => "元固定長の格納文字列（末尾空白保持・数値化なし）",
        ComparisonSourceKind.RawFixedBytes => "元固定長バイト（選択文字コードで切取／再エンコードではありません）",
        ComparisonSourceKind.EncodedCsvLogicalValue => "CSV論理値を符号化（CSVの引用符構文は含みません）",
        _ => "未対応"
    };

    private static ComboBox DropDown(string name) => new() { Name = name, DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private static NumericUpDown Number(string name, int min, int max, int value) =>
        new() { Name = name, Minimum = min, Maximum = max, Value = value, Width = 120, ThousandsSeparator = true };
    private static T Selected<T>(ComboBox combo) => combo.SelectedItem is Choice<T> choice ? choice.Value
        : throw new InvalidDataException("選択されていない項目があります。入力項目・値の扱いを確認してください。");
    private static void Select<T>(ComboBox combo, T value)
    {
        combo.SelectedIndex = -1;
        for (var index = 0; index < combo.Items.Count; index++)
            if (combo.Items[index] is Choice<T> choice && EqualityComparer<T>.Default.Equals(choice.Value, value))
            { combo.SelectedIndex = index; return; }
    }
    private static void AddChoices<T>(ComboBox combo, params (T Value, string Text)[] choices)
    { foreach (var choice in choices) combo.Items.Add(new Choice<T>(choice.Value, choice.Text)); }
    private static Control Rows(params (string Label, Control Control)[] rows)
    {
        var table = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 2, Margin = new Padding(0, 3, 0, 3) };
        table.ColumnStyles.Add(new(SizeType.Absolute, 170)); table.ColumnStyles.Add(new(SizeType.Percent, 100));
        foreach (var row in rows)
        {
            var index = table.RowCount++;
            table.RowStyles.Add(new(SizeType.AutoSize));
            table.Controls.Add(new Label { AutoSize = true, Text = row.Label, Padding = new Padding(0, 5, 0, 0) }, 0, index);
            row.Control.Dock = DockStyle.Fill; table.Controls.Add(row.Control, 1, index);
        }
        return table;
    }
    private static FlowLayoutPanel Flow(params Control[] controls)
    {
        var panel = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, WrapContents = true, Margin = Padding.Empty };
        panel.Controls.AddRange(controls); return panel;
    }
    private static Control Stack(params Control[] controls)
    {
        var panel = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Dock = DockStyle.Top };
        panel.ColumnStyles.Add(new(SizeType.Percent, 100));
        foreach (var control in controls) { panel.RowStyles.Add(new(SizeType.AutoSize)); panel.Controls.Add(control, 0, panel.RowCount++); control.Dock = DockStyle.Fill; }
        return panel;
    }
    private enum RuleOperation { Input, Literal, Slice, Concat, Pad, Conditional, Advanced }
    private enum BranchKind { Literal, SameInput, OtherInput }
    private sealed record Choice<T>(T Value, string Text) { public override string ToString() => Text; }
    private sealed record PartItem(ComparisonExpression Expression, string Text) { public override string ToString() => Text; }
    private sealed class BranchControls
    {
        public ComboBox Kind { get; }
        public TextBox Value { get; }
        public ComboBox Field { get; }
        public Control Panel { get; }
        public BranchControls(string name)
        {
            Kind = DropDown(name + "Kind"); Kind.Width = 135;
            Value = new() { Name = name + "Value", Width = 200 };
            Field = DropDown(name + "Field"); Field.Width = 300;
            AddChoices(Kind, (BranchKind.Literal, "固定値"), (BranchKind.SameInput, "条件と同じ入力"), (BranchKind.OtherInput, "別の入力項目"));
            Kind.SelectedIndex = 0;
            Panel = Flow(Kind, Value, Field);
            void Update() { Value.Visible = Selected<BranchKind>(Kind) == BranchKind.Literal; Field.Visible = Selected<BranchKind>(Kind) == BranchKind.OtherInput; }
            Kind.SelectedIndexChanged += (_, _) => { if (Kind.SelectedIndex >= 0) Update(); };
            Update();
        }
    }
}
