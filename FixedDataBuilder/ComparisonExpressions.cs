using System.Buffers;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace FixedDataBuilder;

// This is a closed data model, not a language runtime. No user text is compiled or executed.
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$op")]
[JsonDerivedType(typeof(ComparisonInputExpression), "input")]
[JsonDerivedType(typeof(ComparisonLiteralExpression), "literal")]
[JsonDerivedType(typeof(ComparisonSliceExpression), "slice")]
[JsonDerivedType(typeof(ComparisonConcatExpression), "concat")]
[JsonDerivedType(typeof(ComparisonPadExpression), "pad")]
[JsonDerivedType(typeof(ComparisonConditionalExpression), "conditional")]
[JsonDerivedType(typeof(ComparisonUnsupportedExpression), "unsupported")]
public abstract record ComparisonExpression;

public sealed record ComparisonInputExpression(int FieldOrdinal,
    ComparisonSourceKind SourceKind = ComparisonSourceKind.LogicalValue) : ComparisonExpression;
public sealed record ComparisonLiteralExpression(string Value) : ComparisonExpression;
public enum ComparisonSliceUnit { UnicodeCodePoints, Bytes }
public enum ComparisonIndexOrigin { Zero = 0, One = 1 }
public sealed record ComparisonSliceExpression(ComparisonExpression Source, int Start, int Length,
    ComparisonSliceUnit Unit, ComparisonIndexOrigin Origin) : ComparisonExpression;
public sealed record ComparisonConcatExpression(IReadOnlyList<ComparisonExpression> Parts) : ComparisonExpression;
public enum ComparisonPadSide { Left, Right }
/// <summary>Exact Unicode-code-point target length; fill must be one scalar; overlong input fails.</summary>
public sealed record ComparisonPadExpression(ComparisonExpression Source, int TargetLength,
    string Fill, ComparisonPadSide Side) : ComparisonExpression;
public enum ComparisonEqualityMode { StringOrdinal, ExactDecimal }
public sealed record ComparisonConditionalExpression(ComparisonExpression Left, ComparisonExpression Right,
    ComparisonEqualityMode EqualityMode, ComparisonExpression WhenEqual,
    ComparisonExpression WhenNotEqual) : ComparisonExpression;
public sealed record ComparisonUnsupportedExpression(string Description) : ComparisonExpression;

public enum ComparisonEvaluationStatus { Evaluated, CannotEvaluate, Unsupported }
public sealed record ComparisonTraceStep(string Operation, string? Value, string Description);
public sealed record ComparisonEvaluation(ComparisonEvaluationStatus Status, string? ExpectedValue,
    string? Reason, IReadOnlyList<ComparisonTraceStep> Trace)
{
    public bool Success => Status == ComparisonEvaluationStatus.Evaluated;
}

public static class ComparisonExpressionEvaluator
{
    public const int MaxDepth = 32;
    public const int MaxNodes = 256;
    public const int MaxReferences = 128;
    public const int MaxValueLength = 1_000_000;

    public static ComparisonEvaluation Evaluate(ComparisonDataSet input, int? inputRecordIndex,
        ComparisonExpression expression)
    {
        ArgumentNullException.ThrowIfNull(input);
        var trace = new List<ComparisonTraceStep>();
        try
        {
            // Validate the WHOLE tree, including unselected branches, before evaluating anything.
            Validate(expression, input.Fields.Count);
            EnsureSupported(expression);
            var value = EvaluateNode(expression);
            return new(ComparisonEvaluationStatus.Evaluated, value, null, trace.AsReadOnly());
        }
        catch (ComparisonExpressionException ex)
        {
            trace.Add(new("未算出", null, ex.Message));
            return new(ex.Unsupported ? ComparisonEvaluationStatus.Unsupported : ComparisonEvaluationStatus.CannotEvaluate,
                null, ex.Message, trace.AsReadOnly());
        }
        catch (Exception ex) when (ComparisonDataSet.IsSourceFailure(ex))
        {
            trace.Add(new("未算出", null, ex.Message));
            return new(ComparisonEvaluationStatus.CannotEvaluate, null, ex.Message, trace.AsReadOnly());
        }

        string Save(string operation, string value, string description)
        {
            if (value.Length > MaxValueLength) throw new ComparisonExpressionException("計算値が長さ上限を超えます。");
            trace.Add(new(operation, value, description));
            return value;
        }

        string EvaluateNode(ComparisonExpression node)
        {
            switch (node)
            {
                case ComparisonInputExpression reference:
                    if (inputRecordIndex is null) throw new ComparisonExpressionException("対応する入力レコードがありません。");
                    var source = input.ReadSource(inputRecordIndex.Value, reference.FieldOrdinal, reference.SourceKind);
                    if (!source.Success) throw new ComparisonExpressionException(source.Reason ?? "入力を取得できません。");
                    return Save("入力", source.Value!, $"入力No.{reference.FieldOrdinal + 1}: {source.Description}");
                case ComparisonLiteralExpression literal:
                    return Save("固定値", literal.Value, "明示された固定文字列（空白・先頭ゼロを保持）");
                case ComparisonSliceExpression slice when slice.Unit == ComparisonSliceUnit.Bytes:
                    // Byte slicing accepts a direct input reference only. Never silently encode a
                    // trimmed fixed display string or an intermediate concatenation as original bytes.
                    var byteReference = (ComparisonInputExpression)slice.Source;
                    if (inputRecordIndex is null) throw new ComparisonExpressionException("対応する入力レコードがありません。");
                    var bytes = input.GetByteSource(inputRecordIndex.Value, byteReference.FieldOrdinal, byteReference.SourceKind);
                    var encoding = input.GetFieldEncoding(byteReference.FieldOrdinal);
                    var byteStart = checked(slice.Start - (int)slice.Origin);
                    RequireRange(byteStart, slice.Length, bytes.Length);
                    var description = input.Profile.Format == DataFileFormat.Csv
                        ? "CSV論理値を選択文字コードで符号化したバイト（CSV引用符構文を含まない）"
                        : "元固定長項目のバイト（末尾空白保持）";
                    Save("入力バイト", Convert.ToHexString(bytes), $"入力No.{byteReference.FieldOrdinal + 1}: {description}");
                    // Decoding only the selected span is insufficient in SJIS: a trail byte can
                    // also be a legal single byte. Check both surrounding boundaries strictly.
                    _ = encoding.GetString(bytes.AsSpan(0, byteStart));
                    var selected = encoding.GetString(bytes.AsSpan(byteStart, slice.Length));
                    _ = encoding.GetString(bytes.AsSpan(byteStart + slice.Length));
                    return Save("バイト切取", selected, $"開始={slice.Start}, 長さ={slice.Length}, 起点={(int)slice.Origin}; {description}");
                case ComparisonSliceExpression slice:
                    var text = EvaluateNode(slice.Source);
                    var scalars = CodePoints(text);
                    var start = checked(slice.Start - (int)slice.Origin);
                    RequireRange(start, slice.Length, scalars.Count);
                    return Save("文字切取", string.Concat(scalars.Skip(start).Take(slice.Length)),
                        $"Unicodeコードポイント単位（結合文字は別々）; 開始={slice.Start}, 長さ={slice.Length}, 起点={(int)slice.Origin}");
                case ComparisonConcatExpression concat:
                    var builder = new StringBuilder();
                    foreach (var part in concat.Parts)
                    {
                        var item = EvaluateNode(part);
                        if ((long)builder.Length + item.Length > MaxValueLength) throw new ComparisonExpressionException("結合結果が長さ上限を超えます。");
                        builder.Append(item);
                    }
                    return Save("結合", builder.ToString(), $"指定順の{concat.Parts.Count}要素を連結（区切り・空白除去なし）");
                case ComparisonPadExpression pad:
                    var unpadded = EvaluateNode(pad.Source);
                    var count = CodePoints(unpadded).Count;
                    if (count > pad.TargetLength) throw new ComparisonExpressionException("入力が埋めの指定長を超えます。切り捨てません。");
                    var addedLength = (long)(pad.TargetLength - count) * pad.Fill.Length;
                    if (unpadded.Length + addedLength > MaxValueLength) throw new ComparisonExpressionException("埋め結果が長さ上限を超えます。");
                    var fill = string.Concat(Enumerable.Repeat(pad.Fill, pad.TargetLength - count));
                    return Save("文字埋め", pad.Side == ComparisonPadSide.Left ? fill + unpadded : unpadded + fill,
                        $"{pad.Side}, Unicodeコードポイント長={pad.TargetLength}, 埋め文字={pad.Fill}（Trimなし）");
                case ComparisonConditionalExpression conditional:
                    var left = EvaluateNode(conditional.Left);
                    var right = EvaluateNode(conditional.Right);
                    var equal = conditional.EqualityMode == ComparisonEqualityMode.StringOrdinal
                        ? string.Equals(left, right, StringComparison.Ordinal)
                        : ExactDecimal(left) == ExactDecimal(right);
                    Save("条件", equal ? "成立" : "不成立", $"{conditional.EqualityMode}: 条件左辺=[{left}], 右辺=[{right}]");
                    var branchValue = EvaluateNode(equal ? conditional.WhenEqual : conditional.WhenNotEqual);
                    return Save("分岐", branchValue, equal ? "一致側を選択" : "不一致側を選択");
                default:
                    throw new ComparisonExpressionException("未対応の演算です。", unsupported: true);
            }
        }
    }

    /// <summary>Validate persisted expressions without executing them.</summary>
    public static void Validate(ComparisonExpression expression, int inputFieldCount)
    {
        var nodes = 0;
        var references = 0;
        Walk(expression, 1);
        void Walk(ComparisonExpression? node, int depth)
        {
            if (node is null) throw new ComparisonExpressionException("式に空の要素があります。");
            if (depth > MaxDepth || ++nodes > MaxNodes) throw new ComparisonExpressionException("式の深さ・要素数が上限を超えます。");
            switch (node)
            {
                case ComparisonInputExpression source:
                    if (source.FieldOrdinal < 0 || source.FieldOrdinal >= inputFieldCount || !Enum.IsDefined(source.SourceKind))
                        throw new ComparisonExpressionException("式の入力項目参照が範囲外、または入力値種別が不正です。");
                    if (++references > MaxReferences) throw new ComparisonExpressionException("式の入力参照数が上限を超えます。");
                    break;
                case ComparisonLiteralExpression literal:
                    ValidateText(literal.Value);
                    break;
                case ComparisonSliceExpression slice:
                    if (!Enum.IsDefined(slice.Unit) || !Enum.IsDefined(slice.Origin) || slice.Start < (int)slice.Origin
                        || slice.Length < 0 || slice.Length > MaxValueLength)
                        throw new ComparisonExpressionException("切取の単位・起点・範囲が不正です。");
                    if (slice.Unit == ComparisonSliceUnit.Bytes && slice.Source is not ComparisonInputExpression)
                        throw new ComparisonExpressionException("バイト切取は直接の入力項目参照だけに対応します。", unsupported: true);
                    Walk(slice.Source, depth + 1);
                    break;
                case ComparisonConcatExpression concat:
                    if (concat.Parts is null || concat.Parts.Count > MaxReferences) throw new ComparisonExpressionException("結合の要素数が不正です。");
                    foreach (var part in concat.Parts) Walk(part, depth + 1);
                    break;
                case ComparisonPadExpression pad:
                    if (!Enum.IsDefined(pad.Side) || pad.TargetLength < 0 || pad.TargetLength > MaxValueLength)
                        throw new ComparisonExpressionException("埋めの方向・長さが不正です。");
                    ValidateText(pad.Fill);
                    if (CodePoints(pad.Fill).Count != 1) throw new ComparisonExpressionException("埋め文字はUnicodeコードポイント1個で指定してください。");
                    Walk(pad.Source, depth + 1);
                    break;
                case ComparisonConditionalExpression conditional:
                    if (!Enum.IsDefined(conditional.EqualityMode)) throw new ComparisonExpressionException("条件の比較方式が未対応です。", unsupported: true);
                    Walk(conditional.Left, depth + 1); Walk(conditional.Right, depth + 1);
                    Walk(conditional.WhenEqual, depth + 1); Walk(conditional.WhenNotEqual, depth + 1);
                    break;
                case ComparisonUnsupportedExpression unsupported:
                    ValidateText(unsupported.Description);
                    // Retain unknown formulas in settings; evaluation still fails the entire tree.
                    break;
                default:
                    throw new ComparisonExpressionException("未対応の演算型です。", unsupported: true);
            }
        }
    }

    internal static void EnsureSupported(ComparisonExpression expression)
    {
        switch (expression)
        {
            case ComparisonUnsupportedExpression unsupported:
                throw new ComparisonExpressionException($"未対応の演算: {unsupported.Description}", unsupported: true);
            case ComparisonSliceExpression slice: EnsureSupported(slice.Source); break;
            case ComparisonConcatExpression concat:
                foreach (var part in concat.Parts) EnsureSupported(part);
                break;
            case ComparisonPadExpression pad: EnsureSupported(pad.Source); break;
            case ComparisonConditionalExpression conditional:
                EnsureSupported(conditional.Left); EnsureSupported(conditional.Right);
                EnsureSupported(conditional.WhenEqual); EnsureSupported(conditional.WhenNotEqual);
                break;
        }
    }

    private static void ValidateText(string? value)
    {
        if (value is null || value.Length > MaxValueLength) throw new ComparisonExpressionException("式の文字列が空参照、または長さ上限を超えます。");
        _ = CodePoints(value);
    }

    private static List<string> CodePoints(string value)
    {
        if (value.Length > MaxValueLength) throw new ComparisonExpressionException("文字列が長さ上限を超えます。");
        var result = new List<string>();
        var span = value.AsSpan();
        while (!span.IsEmpty)
        {
            if (Rune.DecodeFromUtf16(span, out var rune, out var used) != OperationStatus.Done)
                throw new ComparisonExpressionException("不正なUTF-16文字列です。");
            result.Add(rune.ToString()); span = span[used..];
        }
        return result;
    }

    private static void RequireRange(int start, int length, int available)
    {
        if (start < 0 || length < 0 || (long)start + length > available)
            throw new ComparisonExpressionException("切取範囲が入力の長さを超えます。");
    }

    // Exact decimal equality without floating point, rounding, whitespace, grouping or exponents.
    private static string ExactDecimal(string value)
    {
        if (value.Length > 4096 || !Regex.IsMatch(value, @"\A[+-]?[0-9]+(?:\.[0-9]+)?\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
            throw new ComparisonExpressionException("厳密十進数として比較できません。空白・指数・桁区切りは対応しません。");
        var negative = value.StartsWith('-');
        var unsigned = value.TrimStart('+', '-');
        var parts = unsigned.Split('.');
        var integer = parts[0].TrimStart('0');
        if (integer.Length == 0) integer = "0";
        var fraction = parts.Length == 2 ? parts[1].TrimEnd('0') : "";
        var zero = integer == "0" && fraction.Length == 0;
        return (negative && !zero ? "-" : "") + integer + (fraction.Length > 0 ? "." + fraction : "");
    }

    internal sealed class ComparisonExpressionException(string message, bool unsupported = false) : Exception(message)
    {
        public bool Unsupported { get; } = unsupported;
    }
}
