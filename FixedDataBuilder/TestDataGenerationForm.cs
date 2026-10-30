using System.Globalization;

namespace FixedDataBuilder;

public sealed class TestDataGenerationForm : Form
{
    private const GenerationRequirement DefaultRequirements = GenerationRequirement.NumericLeadingZero
        | GenerationRequirement.NumericAllZero | GenerationRequirement.NumericFullLength
        | GenerationRequirement.NumericMaximum | GenerationRequirement.TextMaximum;

    private readonly IReadOnlyList<FieldDefinition> fields;
    private readonly IReadOnlyList<List<string>> records;
    private readonly CommittingNumericUpDown baselineRecord = new() { Name = "BaselineRecord", Minimum = 1, Maximum = 1, Width = 90 };
    private readonly CheckedListBox targetFields = new() { Name = "TargetFields", CheckOnClick = true, IntegralHeight = false, Height = 126, Dock = DockStyle.Fill };
    private readonly CommittingNumericUpDown normalCount = new() { Name = "NormalRecordCount", Minimum = 1, Maximum = 10000, Value = 4, Width = 92 };
    private readonly Dictionary<GenerationRequirement, CheckBox> requirementChecks = new();
    private readonly ComboBox format = Choice("DataFormat");
    private readonly ComboBox csvEncoding = Choice("CsvEncoding");
    private readonly ComboBox separator = Choice("RecordSeparator");
    private readonly ComboBox national = Choice("NationalEncoding");
    private readonly ComboBox textEncoding = Choice("TextEncoding");
    private readonly Label countSummary = new() { Name = "GenerationCounts", AutoSize = true, Margin = new Padding(3, 8, 3, 5) };
    private readonly TextBox issues = new() { Name = "GenerationIssues", Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Height = 94, BackColor = SystemColors.Window };
    private readonly Label generationError = new() { Name = "GenerationError", AutoSize = true, ForeColor = Color.Firebrick, MaximumSize = new Size(820, 0), Margin = new Padding(3, 5, 3, 5) };
    private readonly Button execute = new() { Name = "ExecuteGeneration", Text = "実行", Width = 100, Height = 30 };
    private readonly Button cancel = new() { Name = "CancelGeneration", Text = "キャンセル", DialogResult = DialogResult.Cancel, CausesValidation = false, Width = 100, Height = 30 };
    private TestDataGenerationPlan? plan;
    private bool updatingTargets;
    private bool committingNumericInputs;

    public TestDataGenerationResult? GeneratedResult { get; private set; }

    public TestDataGenerationForm(IReadOnlyList<FieldDefinition> fields, IReadOnlyList<List<string>> records,
        int initialRecordIndex, int initialFieldIndex, DataSaveOptions initialOptions)
    {
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(initialOptions);
        this.fields = fields.ToArray();
        this.records = records.Select(record => record.ToList()).ToArray();
        Text = "テストデータ作成条件";
        Name = "TestDataGenerationForm";
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false;
        ClientSize = new Size(920, 640);
        MinimumSize = new Size(790, 520);
        AutoScaleMode = AutoScaleMode.Font;

        baselineRecord.Maximum = Math.Max(1, this.records.Count);
        baselineRecord.Value = this.records.Count == 0 ? 1 : Math.Clamp(initialRecordIndex, 0, this.records.Count - 1) + 1;
        baselineRecord.Enabled = this.records.Count > 0;
        for (var index = 0; index < this.fields.Count; index++)
            if (!this.fields[index].IsRedefines)
                targetFields.Items.Add(new TargetField(index, $"{index + 1}. {this.fields[index].Name}  [{this.fields[index].DisplayDefinition}]"), index == initialFieldIndex);

        format.Items.AddRange(["固定長", "CSV（ヘッダーなし）", "CSV（ヘッダーあり）"]);
        csvEncoding.Items.AddRange(JapaneseEncodings.CsvNames);
        separator.Items.AddRange(["改行あり (CRLF)", "改行なし"]);
        national.Items.AddRange(["MS932 (CP932)", "UTF-16LE", "UTF-32LE", "SJIS (JIS X 0208)"]);
        textEncoding.Items.AddRange(JapaneseEncodings.TextNames);
        format.SelectedIndex = initialOptions.Format == DataFileFormat.Csv ? (initialOptions.CsvHasHeader ? 2 : 1) : 0;
        csvEncoding.SelectedIndex = (int)initialOptions.CsvEncoding;
        separator.SelectedIndex = initialOptions.SeparatorMode == RecordSeparatorMode.None ? 1 : 0;
        national.SelectedIndex = (int)initialOptions.NationalEncoding;
        textEncoding.SelectedIndex = (int)(initialOptions.TextEncoding ?? DataEncodingProfile.ShiftJis);

        var shell = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        var scroll = new Panel { Name = "GenerationBody", Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(12) };
        var body = Stack();
        body.Dock = DockStyle.Top;
        body.MinimumSize = new Size(730, 0);
        body.Controls.Add(Note("基準行をコピーし、チェックした対象項目だけに選択した値を入れます。対象外の項目は基準行の値を保持します。\r\n日付・業務コードの妥当性は判断しません。変更したくない項目は対象から外してください。"));
        var columns = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 2, Margin = new Padding(0, 6, 0, 0) };
        columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        columns.Controls.Add(BuildSourceAndOptions(), 0, 0);
        columns.Controls.Add(BuildRequirements(), 1, 0);
        body.Controls.Add(columns);
        body.Controls.Add(Note("条件の確認（対応できない条件や不足も表示します）"));
        body.Controls.Add(issues);
        body.Controls.Add(generationError);
        body.Controls.Add(Note("実行すると、新しい編集用の一覧に作成結果を表示します。確認・編集後に「名前を付けて保存」で保存してください。"));
        scroll.Controls.Add(body);

        var footer = new TableLayoutPanel { Name = "GenerationFooter", Dock = DockStyle.Fill, ColumnCount = 3, Padding = new Padding(12, 8, 12, 8) };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        footer.Controls.Add(new Label { Text = "ファイルへの自動保存は行いません。", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        footer.Controls.Add(execute, 1, 0);
        footer.Controls.Add(cancel, 2, 0);
        shell.Controls.Add(scroll, 0, 0);
        shell.Controls.Add(footer, 0, 1);
        Controls.Add(shell);
        AcceptButton = execute;
        CancelButton = cancel;

        foreach (var numeric in new[] { baselineRecord, normalCount })
        {
            // ValueChanged can precede the spinner's Text update. Read the edit text only
            // after TextChanged, including direct keyboard input that has not lost focus.
            numeric.TextChanged += (_, _) => { if (!committingNumericInputs) RefreshAnalysis(); };
            numeric.Validated += (_, _) => { if (!committingNumericInputs) RefreshAnalysis(); };
        }
        targetFields.ItemCheck += (_, e) => { if (!updatingTargets) UpdateAnalysis(e.Index, e.NewValue); };
        foreach (var check in requirementChecks.Values) check.CheckedChanged += (_, _) => RefreshAnalysis();
        foreach (var choice in new[] { format, csvEncoding, separator, national, textEncoding })
            choice.SelectedIndexChanged += (_, _) => { UpdateOptionControls(); RefreshAnalysis(); };
        execute.Click += (_, _) => ExecuteGeneration();
        FormClosing += (_, _) => { if (DialogResult != DialogResult.OK) GeneratedResult = null; };
        UpdateOptionControls();
        RefreshAnalysis();
    }

    private Control BuildSourceAndOptions()
    {
        var left = Stack();
        left.Dock = DockStyle.Fill;
        var baseline = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, WrapContents = true };
        baseline.Controls.Add(new Label { Text = "基準行（1から）", AutoSize = true, Margin = new Padding(3, 6, 3, 3) });
        baseline.Controls.Add(baselineRecord);
        baseline.Controls.Add(new Label { Text = $"/ {records.Count} 行", AutoSize = true, Margin = new Padding(3, 6, 3, 3) });
        left.Controls.Add(baseline);
        var target = Stack();
        target.Controls.Add(targetFields);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true };
        var selectAll = new Button { Name = "SelectAllTargets", Text = "すべて選択", AutoSize = true };
        var clear = new Button { Name = "ClearTargets", Text = "選択を解除", AutoSize = true };
        selectAll.Click += (_, _) => SetAllTargets(true);
        clear.Click += (_, _) => SetAllTargets(false);
        buttons.Controls.AddRange([selectAll, clear]);
        target.Controls.Add(buttons);
        var redefines = fields.Count(field => field.IsRedefines);
        target.Controls.Add(Note(redefines > 0 ? $"REDEFINES {redefines} 項目は対象にできません。" : "対象は実体のある項目だけです。"));
        left.Controls.Add(Group("変更する対象項目", target));
        var choices = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 2, Padding = new Padding(3) };
        choices.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        choices.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var controls = new[] { format, csvEncoding, textEncoding, national, separator };
        var labels = new[] { "出力形式", "CSV文字コード", "固定長 X文字", "固定長 N文字", "固定長の改行" };
        for (var index = 0; index < controls.Length; index++)
        {
            choices.Controls.Add(new Label { Text = labels[index], AutoSize = true, Anchor = AnchorStyles.Left }, 0, index);
            choices.Controls.Add(controls[index], 1, index);
        }
        left.Controls.Add(Group("出力条件", choices));
        return left;
    }

    private Control BuildRequirements()
    {
        var right = Stack();
        right.Dock = DockStyle.Fill;
        var checks = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 2, Padding = new Padding(3) };
        checks.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        checks.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        var numeric = new (GenerationRequirement Requirement, string Text)[]
        {
            (GenerationRequirement.NumericLeadingZero, "数値：先頭ゼロ"),
            (GenerationRequirement.NumericAllZero, "数値：すべてゼロ"),
            (GenerationRequirement.NumericFullLength, "数値：定義桁いっぱい"),
            (GenerationRequirement.NumericMaximum, "数値：最大値"),
            (GenerationRequirement.NumericShort, "数値：短い桁数"),
            (GenerationRequirement.NumericNegativeMaximum, "数値：負の最大値（符号あり）"),
            (GenerationRequirement.NumericMinimumUnit, "数値：最小単位（小数あり）")
        };
        var text = new (GenerationRequirement Requirement, string Text)[]
        {
            (GenerationRequirement.TextMaximum, "文字：最大長"),
            (GenerationRequirement.TextLeadingSpace, "文字：先頭に空白"),
            (GenerationRequirement.TextInternalSpace, "文字：途中に空白"),
            (GenerationRequirement.TextTrailingSpace, "文字：末尾に空白"),
            (GenerationRequirement.TextEmpty, "文字：空文字"),
            (GenerationRequirement.TextHalfSpaces, "文字：半角空白だけ"),
            (GenerationRequirement.TextFullSpaces, "文字：全角空白だけ")
        };
        for (var index = 0; index < numeric.Length; index++)
        {
            checks.Controls.Add(Requirement(numeric[index].Requirement, numeric[index].Text), 0, index);
            checks.Controls.Add(Requirement(text[index].Requirement, text[index].Text), 1, index);
        }
        checks.Controls.Add(Requirement(GenerationRequirement.Identifier, "行・項目を識別する値"), 0, 7);
        checks.SetColumnSpan(checks.GetControlFromPosition(0, 7)!, 2);
        checks.Controls.Add(Requirement(GenerationRequirement.InvalidNumeric, "CSV不正値：数値に文字"), 0, 8);
        checks.Controls.Add(Requirement(GenerationRequirement.Overlength, "CSV不正値：長さ超過"), 1, 8);
        right.Controls.Add(Group("作成する値（項目の型・桁数に応じて判定）", checks));
        right.Controls.Add(Note("9(8) の例：先頭ゼロ 01234567 ／ 全ゼロ 00000000\r\n定義桁いっぱい 12345678 ／ 最大値 99999999\r\n固定長で保持できない空白・不正値は、CSVを選んでください。"));
        var countRow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, Margin = new Padding(0, 8, 0, 0) };
        countRow.Controls.Add(new Label { Text = "通常行数（1～10000）", AutoSize = true, Margin = new Padding(3, 6, 3, 3) });
        countRow.Controls.Add(normalCount);
        right.Controls.Add(countRow);
        right.Controls.Add(countSummary);
        return right;
    }

    private CheckBox Requirement(GenerationRequirement requirement, string text)
    {
        var check = new CheckBox { Name = $"Requirement{requirement}", Text = text, AutoSize = true,
            Checked = DefaultRequirements.HasFlag(requirement), Margin = new Padding(3, 5, 3, 5) };
        requirementChecks.Add(requirement, check);
        return check;
    }

    private static ComboBox Choice(string name) => new() { Name = name, DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, Width = 180 };
    private static TableLayoutPanel Stack()
    {
        var stack = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, Margin = new Padding(0) };
        stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        stack.SizeChanged += (_, _) =>
        {
            foreach (var label in stack.Controls.OfType<Label>())
                label.MaximumSize = new Size(Math.Max(1, stack.ClientSize.Width - label.Margin.Horizontal), 0);
        };
        return stack;
    }
    private static Label Note(string text) => new() { Text = text, AutoSize = true, Dock = DockStyle.Top, Margin = new Padding(3, 5, 3, 5) };
    private static GroupBox Group(string text, Control content)
    {
        var group = new GroupBox { Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top, Padding = new Padding(6, 18, 6, 6), Margin = new Padding(3, 5, 3, 5) };
        content.Dock = DockStyle.Top;
        group.Controls.Add(content);
        return group;
    }

    private void SetAllTargets(bool selected)
    {
        updatingTargets = true;
        try { for (var index = 0; index < targetFields.Items.Count; index++) targetFields.SetItemChecked(index, selected); }
        finally { updatingTargets = false; }
        RefreshAnalysis();
    }

    private void UpdateOptionControls()
    {
        var isCsv = format.SelectedIndex > 0;
        csvEncoding.Enabled = isCsv;
        separator.Enabled = national.Enabled = textEncoding.Enabled = !isCsv;
    }

    private DataSaveOptions BuildOutputOptions() => new(format.SelectedIndex > 0 ? DataFileFormat.Csv : DataFileFormat.FixedLength,
        (CsvTextEncoding)csvEncoding.SelectedIndex,
        separator.SelectedIndex == 1 ? RecordSeparatorMode.None : RecordSeparatorMode.CrLfOrLf,
        (NationalTextEncoding)national.SelectedIndex, (DataEncodingProfile)textEncoding.SelectedIndex, format.SelectedIndex == 2);

    private TestDataGenerationSettings BuildSettings() => CreateSettings(-1, CheckState.Unchecked);

    private TestDataGenerationSettings CreateSettings(int pendingIndex, CheckState pendingState)
    {
        var selected = Enumerable.Range(0, targetFields.Items.Count)
            .Where(index => index == pendingIndex ? pendingState == CheckState.Checked : targetFields.GetItemChecked(index))
            .Select(index => ((TargetField)targetFields.Items[index]).Index).ToArray();
        var requirements = requirementChecks.Where(pair => pair.Value.Checked).Aggregate(GenerationRequirement.None, (value, pair) => value | pair.Key);
        return new TestDataGenerationSettings((int)baselineRecord.Value - 1, selected, requirements, (int)normalCount.Value, BuildOutputOptions());
    }

    private void RefreshAnalysis() => UpdateAnalysis(-1, CheckState.Unchecked);

    private bool CommitNumericInputs()
    {
        foreach (var (control, name) in new[] { (baselineRecord, "基準行"), (normalCount, "通常行数") })
        {
            // Empty/partial/out-of-range text remains editable. Do not read Value here:
            // its getter may commit or clamp the unfinished edit before it is valid.
            if (!decimal.TryParse(control.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out var value)
                || decimal.Truncate(value) != value || value < control.Minimum || value > control.Maximum)
            {
                execute.Enabled = false;
                generationError.Text = $"{name}は {control.Minimum:0}～{control.Maximum:0} の整数で指定してください。";
                return false;
            }
        }
        committingNumericInputs = true;
        try
        {
            baselineRecord.CommitEditText();
            normalCount.CommitEditText();
        }
        finally { committingNumericInputs = false; }
        return true;
    }

    private void UpdateAnalysis(int pendingIndex, CheckState pendingState)
    {
        if (!CommitNumericInputs()) return;
        generationError.Text = "";
        execute.Enabled = false;
        plan = null;
        var settings = CreateSettings(pendingIndex, pendingState);
        var messages = new List<string>();
        if (records.Count == 0) messages.Add("基準行がありません。元の一覧にデータを用意してください。");
        if (settings.TargetFieldIndexes.Count == 0) messages.Add("変更する対象項目を選択してください。");
        if (settings.Requirements == GenerationRequirement.None) messages.Add("作成する値を1つ以上チェックしてください。");
        if (records.Count == 0)
        {
            countSummary.Text = "必要な通常行数：未計算\r\n不正値の追加・合計：未計算";
            issues.Text = string.Join(Environment.NewLine, messages);
            return;
        }
        try
        {
            plan = TestDataGenerator.Analyze(fields, records[settings.BaselineRecordIndex], settings);
            countSummary.Text = $"必要な通常行数：{plan.RequiredNormalCount} 行 ／ 指定：{settings.NormalRecordCount} 行\r\n不正値の追加：{plan.InvalidRecordCount} 行 ／ 合計：{settings.NormalRecordCount + plan.InvalidRecordCount} 行";
            if (settings.NormalRecordCount < plan.RequiredNormalCount)
                messages.Add($"通常行数が不足しています。{plan.RequiredNormalCount} 行以上を指定してください（現在 {settings.NormalRecordCount} 行）。");
            foreach (var issue in plan.Issues)
            {
                var field = issue.FieldIndex >= 0 && issue.FieldIndex < fields.Count ? $"{issue.FieldIndex + 1}. {fields[issue.FieldIndex].Name}：" : "";
                var requirement = Enum.TryParse<GenerationRequirement>(issue.Requirement, out var key) && requirementChecks.TryGetValue(key, out var check)
                    ? $"（{check.Text}）" : "";
                messages.Add($"{(issue.IsBlocking ? "要変更" : "確認事項")}{requirement}：{field}{issue.Message}");
            }
            execute.Enabled = settings.TargetFieldIndexes.Count > 0 && settings.Requirements != GenerationRequirement.None
                && settings.NormalRecordCount >= plan.RequiredNormalCount && !plan.HasBlockingIssues;
            if (messages.Count == 0) messages.Add("選択した条件は、対象項目の型・桁数で作成できます。");
            issues.Text = string.Join(Environment.NewLine, messages.Distinct());
        }
        catch (Exception error)
        {
            countSummary.Text = "必要な通常行数：確認できません\r\n不正値の追加・合計：確認できません";
            issues.Text = string.Join(Environment.NewLine, messages);
            generationError.Text = $"条件を確認できませんでした。{error.Message}";
        }
    }

    private void ExecuteGeneration()
    {
        if (!CommitNumericInputs() || !ValidateChildren()) return;
        RefreshAnalysis();
        if (!execute.Enabled) return;
        var settings = BuildSettings();
        TryGenerate(() => TestDataGenerator.Generate(fields, records[settings.BaselineRecordIndex], settings));
    }

    private bool TryGenerate(Func<TestDataGenerationResult> generate)
    {
        GeneratedResult = null;
        DialogResult = DialogResult.None;
        UseWaitCursor = true;
        try
        {
            GeneratedResult = generate();
            DialogResult = DialogResult.OK;
            return true;
        }
        catch (Exception error)
        {
            generationError.Text = $"作成できませんでした。条件を確認してから、もう一度実行してください。\r\n{error.Message}";
            return false;
        }
        finally { UseWaitCursor = false; }
    }

    private sealed record TargetField(int Index, string Text)
    {
        public override string ToString() => Text;
    }

    private sealed class CommittingNumericUpDown : NumericUpDown
    {
        public void CommitEditText() => ValidateEditText();
    }
}
