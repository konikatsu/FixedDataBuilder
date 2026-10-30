namespace FixedDataBuilder;

[Flags]
public enum GenerationRequirement
{
    None = 0,
    NumericLeadingZero = 1 << 0,
    NumericAllZero = 1 << 1,
    NumericFullLength = 1 << 2,
    NumericMaximum = 1 << 3,
    NumericShort = 1 << 4,
    NumericNegativeMaximum = 1 << 5,
    NumericMinimumUnit = 1 << 6,
    TextMaximum = 1 << 7,
    TextLeadingSpace = 1 << 8,
    TextInternalSpace = 1 << 9,
    TextTrailingSpace = 1 << 10,
    TextEmpty = 1 << 11,
    TextHalfSpaces = 1 << 12,
    TextFullSpaces = 1 << 13,
    Identifier = 1 << 14,
    InvalidNumeric = 1 << 15,
    Overlength = 1 << 16
}

public sealed record TestDataGenerationSettings(
    int BaselineRecordIndex,
    IReadOnlyList<int> TargetFieldIndexes,
    GenerationRequirement Requirements,
    int NormalRecordCount,
    DataSaveOptions OutputOptions);

public sealed record GenerationIssue(int FieldIndex, string Requirement, string Message, bool IsBlocking = false);

public sealed record GenerationCellCase(int FieldIndex, string CaseId, string Goal, string Value, bool IntentionalInvalid = false);

public sealed record TestDataGenerationPlan(
    int RequiredNormalCount,
    int InvalidRecordCount,
    IReadOnlyDictionary<int, IReadOnlyList<GenerationCellCase>> CasesByField,
    IReadOnlyList<GenerationIssue> Issues)
{
    public bool HasBlockingIssues => Issues.Any(issue => issue.IsBlocking);
}

public sealed record GeneratedRecordCase(int RecordNumber, string CaseId, string Goal,
    bool IntentionalInvalid, IReadOnlyList<GenerationCellCase> Cells);

public sealed record TestDataGenerationResult(
    TestDataGenerationSettings Settings,
    DateTimeOffset GeneratedAt,
    string DefinitionFingerprint,
    IReadOnlyList<List<string>> Records,
    IReadOnlyList<GeneratedRecordCase> Cases,
    IReadOnlyList<GenerationIssue> Issues);
