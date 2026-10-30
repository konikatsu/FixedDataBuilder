using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace FixedDataBuilder;

/// <summary>Builds synthetic cases by cloning one logical record. This class performs no file IO.</summary>
public static class TestDataGenerator
{
    public const int MaximumRecordCount = 10_000;
    public const int MaximumTotalCharacters = 20_000_000;
    public const int MaximumFieldLength = 100_000;
    private const int MaximumCellCount = 1_000_000;
    private const GenerationRequirement NumericRequirements = GenerationRequirement.NumericLeadingZero
        | GenerationRequirement.NumericAllZero | GenerationRequirement.NumericFullLength | GenerationRequirement.NumericMaximum
        | GenerationRequirement.NumericShort | GenerationRequirement.NumericNegativeMaximum | GenerationRequirement.NumericMinimumUnit;
    private const GenerationRequirement TextRequirements = GenerationRequirement.TextMaximum | GenerationRequirement.TextLeadingSpace
        | GenerationRequirement.TextInternalSpace | GenerationRequirement.TextTrailingSpace | GenerationRequirement.TextEmpty
        | GenerationRequirement.TextHalfSpaces | GenerationRequirement.TextFullSpaces;
    private const GenerationRequirement AllRequirements = NumericRequirements | TextRequirements | GenerationRequirement.Identifier
        | GenerationRequirement.InvalidNumeric | GenerationRequirement.Overlength;

    public static TestDataGenerationPlan Analyze(IReadOnlyList<FieldDefinition> fields, IReadOnlyList<string> baseline,
        TestDataGenerationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(settings);
        var issues = new List<GenerationIssue>();
        var cases = new Dictionary<int, IReadOnlyList<GenerationCellCase>>();
        void Block(string message) => issues.Add(new(-1, "Settings", message, true));
        TestDataGenerationPlan Plan(int required = 0, int invalid = 0) => new(required, invalid, cases, issues);

        if (fields.Count == 0 || fields.Count > 10_000) Block("項目数は1～10,000にしてください。");
        if (baseline.Count != fields.Count) Block("基準レコードの項目数が定義と一致しません。");
        if (settings.BaselineRecordIndex < 0) Block("基準レコード番号が不正です。");
        if (settings.NormalRecordCount is < 1 or > MaximumRecordCount) Block($"正常レコード数は1～{MaximumRecordCount:N0}にしてください。");
        if (settings.Requirements == GenerationRequirement.None || (settings.Requirements & ~AllRequirements) != 0)
            Block("生成要件を1つ以上選択してください（未対応の要件値は指定できません）。");
        if (settings.TargetFieldIndexes is null || settings.TargetFieldIndexes.Count == 0 || settings.TargetFieldIndexes.Count > 10_000)
            Block("生成対象の項目を1～10,000個選択してください。");
        if (settings.OutputOptions is null) Block("出力条件がありません。");
        else if (!Enum.IsDefined(settings.OutputOptions.Format) || !Enum.IsDefined(settings.OutputOptions.CsvEncoding)
            || !Enum.IsDefined(settings.OutputOptions.SeparatorMode) || !Enum.IsDefined(settings.OutputOptions.NationalEncoding)
            || (settings.OutputOptions.TextEncoding.HasValue && !Enum.IsDefined(settings.OutputOptions.TextEncoding.Value)))
            Block("出力条件の文字コードまたは形式が不正です。");
        if (issues.Any(i => i.IsBlocking)) return Plan();

        var targets = settings.TargetFieldIndexes!.Distinct().Order().ToArray();
        if (targets.Any(index => index < 0 || index >= fields.Count)) Block("生成対象の項目番号が範囲外です。");
        for (var index = 0; index < fields.Count; index++)
        {
            var field = fields[index];
            if (field is null || field.Name is null || !Enum.IsDefined(field.Type) || field.Length is < 1 or > MaximumFieldLength
                || field.DecimalScale < 0 || field.DecimalScale >= field.Length
                || (field.Type == FieldDataType.FullWidthText && field.NationalByteWidth is not (2 or 4)))
                issues.Add(new(index, "Definition", $"項目定義が不正、または長さが上限 {MaximumFieldLength:N0} を超えています。", true));
            if (baseline[index] is null) issues.Add(new(index, "Baseline", "基準値がnullです。空文字を指定してください。", true));
        }
        if (issues.Any(i => i.IsBlocking)) return Plan();
        targets = targets.Where(index =>
        {
            if (!fields[index].IsRedefines) return true;
            issues.Add(new(index, "REDEFINES", "REDEFINES項目は物理列を持たないため生成対象から除外し、基準値を保持します。"));
            return false;
        }).ToArray();
        if (targets.Length == 0) { Block("生成可能な物理項目が選択されていません。"); return Plan(); }

        var csv = settings.OutputOptions!.Format == DataFileFormat.Csv;
        var invalidCount = csv ? targets.Sum(index =>
            (IsNumeric(fields[index]) && Has(settings, GenerationRequirement.InvalidNumeric) ? 1 : 0)
            + (Has(settings, GenerationRequirement.Overlength) ? 1 : 0)) : 0;
        var rowCount = (long)settings.NormalRecordCount + invalidCount;
        var targetSet = targets.ToHashSet();
        long maximumRowChars = 0, maximumCaseChars = 0;
        for (var index = 0; index < fields.Count; index++)
        {
            var maximum = targetSet.Contains(index) ? Math.Max(baseline[index].Length, fields[index].Length + 3L) : baseline[index].Length;
            maximumRowChars += maximum;
            if (targetSet.Contains(index)) maximumCaseChars += (fields[index].Length + 3L) * 16;
        }
        // Bounds precede the creation of repeated strings, case collections and cloned rows.
        if (rowCount > MaximumRecordCount || rowCount * fields.Count > MaximumCellCount
            || maximumRowChars * rowCount + maximumCaseChars > MaximumTotalCharacters)
        {
            Block($"生成量が上限（合計{MaximumRecordCount:N0}レコード、{MaximumCellCount:N0}セル、{MaximumTotalCharacters:N0}文字）を超えます。");
            return Plan(0, invalidCount);
        }

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var outputEncoding = csv ? JapaneseEncodings.ForCsv(settings.OutputOptions.CsvEncoding)
            : StrictEncoding(JapaneseEncodings.ForData(settings.OutputOptions.TextEncoding ?? DataEncodingProfile.ShiftJis));
        foreach (var index in targets)
        {
            var field = fields[index];
            var fieldCases = new List<GenerationCellCase>();
            void Unavailable(GenerationRequirement requirement, string reason) => issues.Add(new(index, requirement.ToString(), reason));
            void Add(string id, string goal, string value) => fieldCases.Add(new(index, CellId(index, id), goal, value));
            foreach (var requirement in IndividualRequirements(settings.Requirements))
            {
                if (IsNumeric(field) && (requirement & TextRequirements) != 0)
                    Unavailable(requirement, "文字項目向けの要件のため、この数値項目には適用しません。");
                if (!IsNumeric(field) && ((requirement & NumericRequirements) != 0 || requirement == GenerationRequirement.InvalidNumeric))
                    Unavailable(requirement, "数値項目向けの要件のため、この文字項目には適用しません。");
            }
            if (IsNumeric(field))
            {
                if (Has(settings, GenerationRequirement.NumericLeadingZero))
                {
                    if (field.Length == 1) Unavailable(GenerationRequirement.NumericLeadingZero, "1桁では、先頭ゼロかつゼロ以外の値を桁あふれなく作れません。");
                    else Add("numeric.leading-zero", "先頭ゼロ・非ゼロ値", DecimalValue("0" + CyclicDigits(field.Length - 1), field));
                }
                if (Has(settings, GenerationRequirement.NumericAllZero)) Add("numeric.all-zero", "全桁ゼロ", DecimalValue(new('0', field.Length), field));
                if (Has(settings, GenerationRequirement.NumericFullLength)) Add("numeric.full-length", "全桁・非ゼロ開始の循環数字", DecimalValue(CyclicDigits(field.Length), field));
                if (Has(settings, GenerationRequirement.NumericMaximum)) Add("numeric.maximum", "最大値（全桁9）", DecimalValue(new('9', field.Length), field));
                if (Has(settings, GenerationRequirement.NumericShort)) Add("numeric.short", "短い数値表記（7）", "7");
                if (Has(settings, GenerationRequirement.NumericNegativeMaximum))
                {
                    if (IsSigned(field)) Add("numeric.negative-maximum", "負の最大絶対値", "-" + DecimalValue(new('9', field.Length), field));
                    else Unavailable(GenerationRequirement.NumericNegativeMaximum, "符号なし数値には負数を適用しません。");
                }
                if (Has(settings, GenerationRequirement.NumericMinimumUnit))
                {
                    if (field.DecimalScale > 0) Add("numeric.minimum-unit", "小数の最小単位", "0." + new string('0', field.DecimalScale - 1) + "1");
                    else Unavailable(GenerationRequirement.NumericMinimumUnit, "小数桁を持たない項目には小数の最小単位を適用しません。");
                }
            }
            else
            {
                var marker = field.Type == FieldDataType.FullWidthText ? '漢' : 'X';
                if (Has(settings, GenerationRequirement.TextMaximum)) Add("text.maximum", "文字の最大長", new(marker, field.Length));
                var positions = 0;
                foreach (var (requirement, mask) in new[] { (GenerationRequirement.TextLeadingSpace, 1), (GenerationRequirement.TextInternalSpace, 2), (GenerationRequirement.TextTrailingSpace, 4) })
                {
                    if (!Has(settings, requirement)) continue;
                    if (!csv && mask != 2) Unavailable(requirement, "固定長保存では前後の空白が除かれるため、この空白位置を保持できません。CSVを選択してください。");
                    else if (field.Length < (mask == 2 ? 3 : 2)) Unavailable(requirement, mask == 2
                        ? "内部空白には空白の前後に文字が必要です（最低3文字）。" : "端の空白と文字を区別するには最低2文字必要です。");
                    else positions |= mask;
                }
                while (positions != 0)
                {
                    var chosen = Enumerable.Range(1, 7).Where(mask => (mask & positions) == mask && MinimumSpaceLength(mask) <= field.Length)
                        .OrderByDescending(mask => System.Numerics.BitOperations.PopCount((uint)mask)).ThenBy(mask => mask).First();
                    var chars = new string(marker, field.Length).ToCharArray();
                    var space = field.Type == FieldDataType.FullWidthText ? '\u3000' : ' ';
                    if ((chosen & 1) != 0) chars[0] = space;
                    if ((chosen & 2) != 0) chars[field.Length / 2] = space;
                    if ((chosen & 4) != 0) chars[^1] = space;
                    Add($"text.spaces-{chosen}", SpaceGoal(chosen), new string(chars));
                    positions &= ~chosen;
                }
                foreach (var requirement in new[] { GenerationRequirement.TextEmpty, GenerationRequirement.TextHalfSpaces, GenerationRequirement.TextFullSpaces })
                {
                    if (!Has(settings, requirement)) continue;
                    if (!csv) { Unavailable(requirement, "固定長保存では空白の除去と埋め文字により、空文字・空白だけの値の区別を保持できません。CSVを選択してください。"); continue; }
                    if (requirement == GenerationRequirement.TextEmpty) Add("text.empty", "空文字", "");
                    else if (requirement == GenerationRequirement.TextHalfSpaces)
                    {
                        if (field.Type == FieldDataType.FullWidthText) Unavailable(requirement, "型Nの全角文字要件に半角空白は適用しません。");
                        else Add("text.half-spaces", "半角空白のみ", new(' ', field.Length));
                    }
                    else if (field.Type == FieldDataType.HalfWidthText) Unavailable(requirement, "半角文字項目には全角空白を適用しません。");
                    else
                    {
                        var count = field.Type == FieldDataType.FullWidthText ? field.Length : field.Length / outputEncoding.GetByteCount("\u3000");
                        if (count == 0) Unavailable(requirement, "出力文字コードの全角空白1文字が項目のバイト長に収まりません。");
                        else Add("text.full-spaces", "全角空白のみ", new('\u3000', count));
                    }
                }
            }
            if (Has(settings, GenerationRequirement.Identifier))
            {
                Add("identifier.column", "列識別子（桁数制約による重複あり）", IdentifierValue(field, index + 1, 'C'));
                Add("identifier.row", "行識別子（他の列と同じ行番号）", IdentifierValue(field, fieldCases.Count + 1, 'R'));
            }
            if (!csv)
                foreach (var requirement in new[] { GenerationRequirement.InvalidNumeric, GenerationRequirement.Overlength })
                    if (Has(settings, requirement) && (requirement != GenerationRequirement.InvalidNumeric || IsNumeric(field)))
                        Unavailable(requirement, "意図的な不正値はCSV専用です。固定長では正常ケースのみ生成します。");

            cases[index] = fieldCases.AsReadOnly();
            foreach (var item in fieldCases)
                ValidateValue(field, index, item.Value, item.Goal, generated: true, settings.OutputOptions, outputEncoding, issues);
        }

        var requiredCount = cases.Values.Max(list => list.Count);
        if (settings.NormalRecordCount < requiredCount)
            issues.Add(new(-1, "NormalRecordCount", $"選択した要件には正常レコードが最低{requiredCount}件必要です。指定は{settings.NormalRecordCount}件です。", true));
        // A selected field can still use the baseline in slots after its last case.
        for (var index = 0; index < fields.Count; index++)
            if (!fields[index].IsRedefines && (!cases.TryGetValue(index, out var values) || values.Count < settings.NormalRecordCount || values.Count == 0))
                ValidateValue(fields[index], index, baseline[index], "基準値の出力可否", generated: false, settings.OutputOptions, outputEncoding, issues);
        if (csv && settings.OutputOptions.CsvHasHeader)
            foreach (var (field, index) in fields.Select((field, index) => (field, index)).Where(item => !item.field.IsRedefines))
                CheckEncoding(outputEncoding, field.Name, index, "CSVヘッダー", issues);
        if (Has(settings, GenerationRequirement.Identifier))
        {
            var signatures = cases.Select(pair => (Index: pair.Key, Case: pair.Value.FirstOrDefault(item => item.CaseId.EndsWith(".identifier.column", StringComparison.Ordinal))))
                .Where(item => item.Case is not null).GroupBy(item => item.Case!.Value, StringComparer.Ordinal);
            foreach (var group in signatures.Where(group => group.Count() > 1))
                foreach (var item in group) issues.Add(new(item.Index, "Identifier", "項目長の制約により列識別子が他の対象列と重複します。全列・全レコードの一意性は保証しません。"));
            issues.Add(new(-1, "Identifier", "列識別と行識別は別の正常ケースです。行識別子は同じ行の各列で共有し、短い項目では識別子が切り詰められます。"));
        }
        return Plan(requiredCount, invalidCount);
    }

    public static TestDataGenerationResult Generate(IReadOnlyList<FieldDefinition> fields, IReadOnlyList<string> baseline,
        TestDataGenerationSettings settings)
    {
        var plan = Analyze(fields, baseline, settings);
        if (plan.HasBlockingIssues) throw new InvalidDataException(string.Join(Environment.NewLine, plan.Issues.Where(issue => issue.IsBlocking).Select(issue => issue.Message)));
        var records = new List<List<string>>(settings.NormalRecordCount + plan.InvalidRecordCount);
        var traces = new List<GeneratedRecordCase>(records.Capacity);
        var baselineCells = fields.Select((field, index) => new GenerationCellCase(index, CellId(index, "baseline"),
            field.IsRedefines ? "基準値保持（REDEFINES除外）" : "基準値保持", baseline[index])).ToArray();
        for (var rowIndex = 0; rowIndex < settings.NormalRecordCount; rowIndex++)
        {
            var row = baseline.ToList();
            var cells = new List<GenerationCellCase>(fields.Count);
            for (var index = 0; index < fields.Count; index++)
            {
                if (plan.CasesByField.TryGetValue(index, out var cases) && rowIndex < cases.Count)
                {
                    row[index] = cases[rowIndex].Value;
                    cells.Add(cases[rowIndex]);
                }
                else cells.Add(baselineCells[index]);
            }
            records.Add(row);
            traces.Add(new(rowIndex + 1, $"N{rowIndex + 1:D5}", rowIndex < plan.RequiredNormalCount ? "正常要件ケース" : "基準値のみ（追加行）", false, cells.AsReadOnly()));
        }
        if (plan.InvalidRecordCount > 0)
            foreach (var index in plan.CasesByField.Keys.Order())
                foreach (var requirement in new[] { GenerationRequirement.InvalidNumeric, GenerationRequirement.Overlength })
                {
                    if (!Has(settings, requirement) || (requirement == GenerationRequirement.InvalidNumeric && !IsNumeric(fields[index]))) continue;
                    var value = requirement == GenerationRequirement.InvalidNumeric ? "A" : IsNumeric(fields[index])
                        ? new string('9', fields[index].IntegerDigitLength + 1)
                        : new string(fields[index].Type == FieldDataType.FullWidthText ? '漢' : 'X', fields[index].Length + 1);
                    var row = records[0].ToList();
                    row[index] = value;
                    var cells = traces[0].Cells.ToList();
                    cells[index] = new(index, CellId(index, requirement == GenerationRequirement.InvalidNumeric ? "invalid.numeric" : "invalid.overlength"),
                        requirement == GenerationRequirement.InvalidNumeric ? "意図的エラー：数値以外" : "意図的エラー：桁あふれ", value, true);
                    records.Add(row);
                    traces.Add(new(records.Count, $"I{records.Count - settings.NormalRecordCount:D5}-F{index + 1:D5}-{requirement}", cells[index].Goal, true, cells.AsReadOnly()));
                }
        // Snapshot mutable collections supplied by the caller; generated output never aliases the baseline.
        var snapshotSettings = settings with { TargetFieldIndexes = Array.AsReadOnly(settings.TargetFieldIndexes.ToArray()) };
        return new(snapshotSettings, DateTimeOffset.UtcNow, DefinitionFingerprint(fields), records.AsReadOnly(), traces.AsReadOnly(), plan.Issues);
    }

    public static string DefinitionFingerprint(IReadOnlyList<FieldDefinition> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        void Number(int value) { Span<byte> bytes = stackalloc byte[4]; BinaryPrimitives.WriteInt32LittleEndian(bytes, value); hash.AppendData(bytes); }
        void Text(string? value) { if (value is null) { Number(-1); return; } Number(value.Length); hash.AppendData(Encoding.UTF8.GetBytes(value)); }
        Number(fields.Count);
        foreach (var field in fields)
        {
            ArgumentNullException.ThrowIfNull(field);
            Text(field.Name); Number((int)field.Type); Number(field.Length); Text(field.DefinitionText);
            Number(field.DecimalScale); Number(field.NationalByteWidth); Text(field.RedefinesName); Text(field.Comment);
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static bool IsNumeric(FieldDefinition field) => field.Type is FieldDataType.PlainNumber or FieldDataType.SignedNumber or FieldDataType.PackedUnsigned or FieldDataType.PackedSigned;
    private static bool IsSigned(FieldDefinition field) => field.Type is FieldDataType.SignedNumber or FieldDataType.PackedSigned;
    private static bool Has(TestDataGenerationSettings settings, GenerationRequirement requirement) => (settings.Requirements & requirement) != 0;
    private static IEnumerable<GenerationRequirement> IndividualRequirements(GenerationRequirement requirements)
        => Enum.GetValues<GenerationRequirement>().Where(value => value != GenerationRequirement.None && (requirements & value) != 0);
    private static string CellId(int index, string suffix) => $"F{index + 1:D5}.{suffix}";
    private static string CyclicDigits(int length) => string.Create(length, 0, (chars, _) => { for (var index = 0; index < chars.Length; index++) chars[index] = "1234567890"[index % 10]; });
    private static string DecimalValue(string digits, FieldDefinition field) => field.DecimalScale == 0 ? digits : digits.Insert(field.IntegerDigitLength, ".");
    private static int MinimumSpaceLength(int mask) => mask switch { 1 or 4 => 2, 2 or 5 => 3, 3 or 6 => 4, 7 => 5, _ => int.MaxValue };
    private static string SpaceGoal(int mask) => "空白位置：" + string.Join("・", new[] { (Mask: 1, Name: "先頭"), (Mask: 2, Name: "内部"), (Mask: 4, Name: "末尾") }.Where(item => (mask & item.Mask) != 0).Select(item => item.Name));
    private static string IdentifierValue(FieldDefinition field, int number, char prefix)
    {
        var digits = number.ToString(CultureInfo.InvariantCulture);
        if (IsNumeric(field)) return DecimalValue(digits.Length > field.Length ? digits[^field.Length..] : digits.PadLeft(field.Length, '0'), field);
        var digitWidth = field.Length - 1;
        var marker = digitWidth == 0 ? prefix.ToString() : prefix + (digits.Length > digitWidth ? digits[^digitWidth..] : digits.PadLeft(digitWidth, '0'));
        return field.Type == FieldDataType.FullWidthText ? new string(marker.Select(value => (char)(value + 0xfee0)).ToArray()) : marker;
    }
    private static Encoding StrictEncoding(Encoding encoding) => encoding is StrictShiftJisEncoding ? encoding
        : Encoding.GetEncoding(encoding.CodePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
    private static void CheckEncoding(Encoding encoding, string value, int index, string goal, List<GenerationIssue> issues)
    {
        try { encoding.GetByteCount(value); }
        catch (EncoderFallbackException exception) { issues.Add(new(index, goal, "出力文字コードで表現できません：" + exception.Message, true)); }
    }
    private static void ValidateValue(FieldDefinition field, int index, string value, string goal, bool generated,
        DataSaveOptions options, Encoding outputEncoding, List<GenerationIssue> issues)
    {
        try
        {
            if (options.Format == DataFileFormat.Csv)
            {
                outputEncoding.GetByteCount(value);
                if (!generated) return; // Untouched baseline values are not business-rule validation targets.
                if (IsNumeric(field))
                {
                    if (!NumericValueFormatter.TryFormatDigits(value, field, IsSigned(field), out _, out _, out var error)) throw new InvalidDataException(error);
                }
                else if (field.Type == FieldDataType.FullWidthText ? value.Length > field.Length : outputEncoding.GetByteCount(value) > field.Length)
                    throw new InvalidDataException("生成値が項目長を超えています。");
            }
            else
            {
                var encoding = field.Type == FieldDataType.FullWidthText ? StrictEncoding(NationalTextEncodingHelper.GetEncoding(options.NationalEncoding)) : outputEncoding;
                encoding.GetByteCount(value);
                // The existing writer defines fixed serialization, including number normalization and padding.
                var outputField = field.Type == FieldDataType.FullWidthText
                    ? field with { NationalByteWidth = NationalTextEncodingHelper.FixedByteWidth(options.NationalEncoding) } : field;
                FixedLengthDataWriter.EncodeField(outputField, value, options.TextEncoding ?? DataEncodingProfile.ShiftJis, options.NationalEncoding);
            }
        }
        catch (Exception exception) when (exception is EncoderFallbackException or InvalidDataException or ArgumentException)
        {
            issues.Add(new(index, goal, "出力できない値です：" + exception.Message, true));
        }
    }
}
