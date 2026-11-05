using System.Text.Json;
using System.Text.RegularExpressions;

namespace FixedDataBuilder;

public sealed record ComparisonInputReference(int FieldOrdinal,
    ComparisonSourceKind SourceKind = ComparisonSourceKind.LogicalValue);
public enum ComparisonMappingKind { Unmapped, Input, Fixed, Expression, Ambiguous }
/// <summary>All indices/ordinals are zero-based. Suggestions never become confirmed mappings automatically.</summary>
public sealed record ComparisonFieldMapping(int OutputFieldOrdinal, ComparisonMappingKind Kind,
    IReadOnlyList<ComparisonInputReference> Inputs, ComparisonExpression? Expression = null,
    ComparisonSourceKind OutputSourceKind = ComparisonSourceKind.LogicalValue, string Note = "");
public sealed record ComparisonFieldCandidate(int InputFieldOrdinal, string InputName,
    IReadOnlyList<string> Evidence);
public sealed record ComparisonMappingSuggestion(int OutputFieldOrdinal,
    IReadOnlyList<ComparisonFieldCandidate> Candidates, IReadOnlyList<string> Issues)
{
    public bool IsAmbiguous => Candidates.Count > 1;
}

public enum ComparisonPairKind { Manual, KeyUnique, InputOnly, OutputOnly, Ambiguous, Unconfirmed }
public sealed record ComparisonRecordPair(int? InputRecordIndex, int? OutputRecordIndex,
    ComparisonPairKind Kind = ComparisonPairKind.Manual,
    IReadOnlyList<int>? InputCandidates = null, IReadOnlyList<int>? OutputCandidates = null);
public enum ComparisonResultStatus
{
    DirectValueEqual, DirectValueDifferent, CalculatedExpectedEqual, CalculatedExpectedDifferent,
    InputUnmapped, OutputUnmapped, FieldUnmapped, Ambiguous, CannotEvaluate, Unsupported
}
public sealed record ComparisonResult(ComparisonResultStatus Status, string? ExpectedValue,
    string? OutputValue, string? Reason, ComparisonEvaluation? Evaluation = null);

public static partial class ComparisonEngine
{
    /// <summary>
    /// Candidates use only explicit input markers or an exact complete field name. No fuzzy match,
    /// substring search, formula interpretation or automatic confirmation is performed.
    /// </summary>
    public static IReadOnlyList<ComparisonMappingSuggestion> SuggestFieldMappings(ComparisonDataSet input,
        ComparisonDataSet output)
    {
        var result = new List<ComparisonMappingSuggestion>();
        for (var outputOrdinal = 0; outputOrdinal < output.Fields.Count; outputOrdinal++)
        {
            var field = output.Fields[outputOrdinal];
            var found = new Dictionary<int, List<string>>();
            var issues = new List<string>();
            void Add(int ordinal, string evidence)
            {
                if (!found.TryGetValue(ordinal, out var values)) found[ordinal] = values = [];
                if (!values.Contains(evidence)) values.Add(evidence);
            }
            void AddExactName(string name, string evidence)
            {
                var matched = false;
                for (var index = 0; index < input.Fields.Count; index++)
                    if (string.Equals(input.Fields[index].Name, name, StringComparison.Ordinal))
                    { Add(index, evidence); matched = true; }
                if (!matched) issues.Add($"明示入力名に完全一致する項目がありません: {name}");
            }

            // Match the entire output name; duplicate names/OCCURS are all retained as candidates.
            for (var index = 0; index < input.Fields.Count; index++)
                if (string.Equals(input.Fields[index].Name, field.Name, StringComparison.Ordinal))
                    Add(index, "出力項目名と入力正式名の完全一致");
            // A comment that is exactly a full input name is also evidence; prose containing a name is not.
            if (input.Fields.Any(item => string.Equals(item.Name, field.Comment, StringComparison.Ordinal)))
                AddExactName(field.Comment, "コメント全体と入力正式名の完全一致");
            foreach (Match match in InputOrdinalMarker().Matches(field.Comment))
            {
                if (!int.TryParse(match.Groups[1].Value, out var number) || number <= 0 || number > input.Fields.Count)
                    issues.Add($"明示入力番号が範囲外です: {match.Value}");
                else Add(number - 1, $"明示入力番号: {match.Value}");
            }
            foreach (Match match in InputNameMarker().Matches(field.Comment))
                AddExactName(match.Groups[1].Value.Trim(), $"明示入力名: {match.Value.Trim()}");
            result.Add(new(outputOrdinal, Array.AsReadOnly(found.OrderBy(item => item.Key)
                .Select(item => new ComparisonFieldCandidate(item.Key, input.Fields[item.Key].Name,
                    Array.AsReadOnly(item.Value.ToArray()))).ToArray()), Array.AsReadOnly(issues.ToArray())));
        }
        return result.AsReadOnly();
    }

    /// <summary>Exact tuples of parsed logical strings. Fixed reader trimming still applies. Duplicate keys never auto-pair.</summary>
    public static IReadOnlyList<ComparisonRecordPair> PairByKeys(ComparisonDataSet input, ComparisonDataSet output,
        IReadOnlyList<int> inputKeyOrdinals, IReadOnlyList<int> outputKeyOrdinals)
    {
        if (inputKeyOrdinals.Count == 0 || inputKeyOrdinals.Count != outputKeyOrdinals.Count)
            throw new ArgumentException("両側のキー項目を同じ個数で1個以上指定してください。");
        if (inputKeyOrdinals.Any(index => index < 0 || index >= input.Fields.Count)
            || outputKeyOrdinals.Any(index => index < 0 || index >= output.Fields.Count))
            throw new ArgumentOutOfRangeException(nameof(inputKeyOrdinals), "キー項目番号が範囲外です。");
        if (input.Profile.Format == DataFileFormat.Csv && inputKeyOrdinals.Any(index => input.Fields[index].IsRedefines)
            || output.Profile.Format == DataFileFormat.Csv && outputKeyOrdinals.Any(index => output.Fields[index].IsRedefines))
            throw new ArgumentException("CSVに物理列を持たないREDEFINESはキーに使用できません。内部初期値を元CSVの値として照合しません。");
        var inputGroups = Group(input, inputKeyOrdinals);
        var outputGroups = Group(output, outputKeyOrdinals);
        var pairs = new List<ComparisonRecordPair>();
        // Output order is stable. Input-only rows follow, retaining their original input indices.
        for (var index = 0; index < output.Records.Count; index++)
        {
            var key = Key(output.Records[index], outputKeyOrdinals);
            var outputs = outputGroups[key];
            if (!inputGroups.TryGetValue(key, out var inputs))
                pairs.Add(new(null, index, ComparisonPairKind.OutputOnly));
            else if (inputs.Count == 1 && outputs.Count == 1)
                pairs.Add(new(inputs[0], index, ComparisonPairKind.KeyUnique));
            else pairs.Add(new(null, index, ComparisonPairKind.Ambiguous, inputs, outputs));
        }
        for (var index = 0; index < input.Records.Count; index++)
            if (!outputGroups.ContainsKey(Key(input.Records[index], inputKeyOrdinals)))
                pairs.Add(new(index, null, ComparisonPairKind.InputOnly));
        return pairs.AsReadOnly();

        static string Key(IReadOnlyList<string> row, IReadOnlyList<int> ordinals) =>
            JsonSerializer.Serialize(ordinals.Select(ordinal => row[ordinal]).ToArray());
        static Dictionary<string, IReadOnlyList<int>> Group(ComparisonDataSet data, IReadOnlyList<int> ordinals)
        {
            var groups = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            for (var index = 0; index < data.Records.Count; index++)
            {
                var key = Key(data.Records[index], ordinals);
                if (!groups.TryGetValue(key, out var rows)) groups[key] = rows = [];
                rows.Add(index);
            }
            // Freeze once per key. Thousands of repeated rows share these immutable candidates
            // instead of each pair allocating another copy of the entire duplicate group.
            return groups.ToDictionary(group => group.Key,
                group => (IReadOnlyList<int>)Array.AsReadOnly(group.Value.ToArray()), StringComparer.Ordinal);
        }
    }

    public static ComparisonEvaluation Evaluate(ComparisonDataSet input, int? inputRecordIndex,
        ComparisonExpression expression) => ComparisonExpressionEvaluator.Evaluate(input, inputRecordIndex, expression);

    /// <summary>Distinct references in first-use order, including conditions and both branches.</summary>
    public static IReadOnlyList<ComparisonInputReference> GetInputReferences(ComparisonExpression expression)
    {
        ComparisonExpressionEvaluator.Validate(expression, int.MaxValue);
        var references = new List<ComparisonInputReference>();
        var seen = new HashSet<ComparisonInputReference>();
        Walk(expression);
        return references.AsReadOnly();
        void Walk(ComparisonExpression node)
        {
            switch (node)
            {
                case ComparisonInputExpression input:
                    var reference = new ComparisonInputReference(input.FieldOrdinal, input.SourceKind);
                    if (seen.Add(reference)) references.Add(reference);
                    break;
                case ComparisonSliceExpression slice: Walk(slice.Source); break;
                case ComparisonConcatExpression concat:
                    foreach (var part in concat.Parts) Walk(part);
                    break;
                case ComparisonPadExpression pad: Walk(pad.Source); break;
                case ComparisonConditionalExpression conditional:
                    Walk(conditional.Left); Walk(conditional.Right); Walk(conditional.WhenEqual); Walk(conditional.WhenNotEqual);
                    break;
            }
        }
    }

    public static ComparisonResult Compare(ComparisonDataSet input, ComparisonDataSet output,
        ComparisonRecordPair pair, ComparisonFieldMapping mapping)
    {
        if (!Enum.IsDefined(pair.Kind) || !Enum.IsDefined(mapping.Kind))
            return new(ComparisonResultStatus.Unsupported, null, null, "未対応のレコード対応・項目対応の種類です。");
        if (pair.OutputRecordIndex is null)
            return new(ComparisonResultStatus.OutputUnmapped, null, null, "対応出力なし（フィルター除外・読込異常などの理由は未確認）");
        var actual = output.ReadSource(pair.OutputRecordIndex.Value, mapping.OutputFieldOrdinal, mapping.OutputSourceKind);
        if (!actual.Success) return new(ComparisonResultStatus.CannotEvaluate, null, null, actual.Reason);
        if (mapping.Kind == ComparisonMappingKind.Ambiguous)
            return new(ComparisonResultStatus.Ambiguous, null, actual.Value, "対応候補が複数あり、確定していません。");
        if (mapping.Kind == ComparisonMappingKind.Unmapped)
            return new(ComparisonResultStatus.FieldUnmapped, null, actual.Value, "入力項目・加工式が未対応です。");
        ComparisonExpression? expression = mapping.Expression;
        if (mapping.Kind == ComparisonMappingKind.Input)
        {
            if (mapping.Inputs is null || mapping.Inputs.Count != 1 || mapping.Inputs[0] is null || mapping.Expression is not null)
                return new(ComparisonResultStatus.CannotEvaluate, null, actual.Value, "直接比較には入力参照を1個指定します。複数入力には明示的な加工式が必要です。");
            expression = new ComparisonInputExpression(mapping.Inputs[0].FieldOrdinal, mapping.Inputs[0].SourceKind);
        }
        if (expression is null)
            return new(ComparisonResultStatus.CannotEvaluate, null, actual.Value, "評価する式が指定されていません。");
        if (!Enum.IsDefined(mapping.Kind))
            return new(ComparisonResultStatus.Unsupported, null, actual.Value, "未対応の対応種別です。");
        try { ComparisonExpressionEvaluator.Validate(expression, input.Fields.Count); }
        catch (ComparisonExpressionEvaluator.ComparisonExpressionException ex)
        {
            return new(ex.Unsupported ? ComparisonResultStatus.Unsupported : ComparisonResultStatus.CannotEvaluate,
                null, actual.Value, ex.Message);
        }
        if (mapping.Kind == ComparisonMappingKind.Expression && (mapping.Inputs is null
            || !mapping.Inputs.ToHashSet().SetEquals(GetInputReferences(expression))))
            return new(ComparisonResultStatus.CannotEvaluate, null, actual.Value, "表示用の入力参照と加工式の入力参照が一致しません。");
        // Only inspect references after the complete tree has passed depth/node/reference validation.
        var needsInput = ContainsInput(expression);
        if (mapping.Kind == ComparisonMappingKind.Fixed && (mapping.Inputs is null || mapping.Inputs.Count != 0 || needsInput))
            return new(ComparisonResultStatus.CannotEvaluate, null, actual.Value, "固定値の対応に入力参照が含まれています。");
        if (pair.Kind == ComparisonPairKind.Ambiguous && needsInput)
            return new(ComparisonResultStatus.Ambiguous, null, actual.Value, "入力行の対応候補が複数あり、確定していません。");
        if (pair.Kind == ComparisonPairKind.Unconfirmed && needsInput)
            return new(ComparisonResultStatus.InputUnmapped, null, actual.Value, "入力行と出力行の組が未確定です。");
        if (pair.InputRecordIndex is null && needsInput)
            return new(ComparisonResultStatus.InputUnmapped, null, actual.Value, "対応する入力レコードがありません。");
        var evaluation = Evaluate(input, pair.InputRecordIndex, expression);
        if (!evaluation.Success)
            return new(evaluation.Status == ComparisonEvaluationStatus.Unsupported ? ComparisonResultStatus.Unsupported
                : ComparisonResultStatus.CannotEvaluate, null, actual.Value, evaluation.Reason, evaluation);
        var same = string.Equals(evaluation.ExpectedValue, actual.Value, StringComparison.Ordinal);
        var direct = mapping.Kind == ComparisonMappingKind.Input;
        return new(direct ? same ? ComparisonResultStatus.DirectValueEqual : ComparisonResultStatus.DirectValueDifferent
                : same ? ComparisonResultStatus.CalculatedExpectedEqual : ComparisonResultStatus.CalculatedExpectedDifferent,
            evaluation.ExpectedValue, actual.Value, direct ? "指定された元値との直接比較（業務の合否判定ではありません）"
                : "明示された式の期待値との比較（業務仕様自体の正しさは別途確認）", evaluation);
    }

    internal static bool ContainsInput(ComparisonExpression expression) => expression switch
    {
        ComparisonInputExpression => true,
        ComparisonSliceExpression slice => ContainsInput(slice.Source),
        ComparisonConcatExpression concat => concat.Parts.Any(ContainsInput),
        ComparisonPadExpression pad => ContainsInput(pad.Source),
        ComparisonConditionalExpression conditional => ContainsInput(conditional.Left) || ContainsInput(conditional.Right)
            || ContainsInput(conditional.WhenEqual) || ContainsInput(conditional.WhenNotEqual),
        _ => false
    };

    [GeneratedRegex(@"(?<![\p{L}\p{N}_])入力\s*(?:参照\s*[=＝]\s*)?No\s*[.．]\s*([0-9]+)(?![0-9])", RegexOptions.CultureInvariant)]
    private static partial Regex InputOrdinalMarker();
    [GeneratedRegex(@"(?:\A|[\r\n;；])\s*入力\s*[:：]\s*([^\r\n;；]+)", RegexOptions.CultureInvariant)]
    private static partial Regex InputNameMarker();
}
