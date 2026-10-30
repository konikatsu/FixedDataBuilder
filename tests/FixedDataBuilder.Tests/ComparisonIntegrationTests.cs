using System.Text.Json;
using FixedDataBuilder;

internal static partial class Program
{
    private static void RunComparisonIntegrationTests(string repo)
    {
        Test("Comparison snapshot keeps current unsaved values without aliasing or claiming original bytes", () =>
        {
            using var form = CreateCellDisplayForm(recordRows: true);
            var rows = Get<List<List<string>>>(form, "records");
            rows[0][0] = "edited";
            Invoke(form, "SetUnsavedChanges", true);
            var snapshot = (ComparisonDataSet)Invoke(form, "CreateComparisonOutputSnapshot")!;
            Equal("edited", snapshot.Records[0][0]);
            Equal(false, snapshot.HasSourceBytes);
            rows[0][0] = "later";
            Equal("edited", snapshot.Records[0][0]);
            Equal(true, Get<bool>(form, "hasUnsavedChanges"));
            Equal(5, rows.Count);
        });
        Test("Comparison evidence retains pair indices, formula trace and unevaluated rows separately", () =>
        {
            var input = ComparisonDataSet.FromRecords([new("source", FieldDataType.Text, 5)], new[] { new[] { "wrong" }, new[] { "AB" } });
            var output = ComparisonDataSet.FromRecords([new("mapped", FieldDataType.Text, 5), new("unset", FieldDataType.Text, 5)], new[] { new[] { "AB!", "x" } });
            ComparisonFieldMapping[] mappings = [new(0, ComparisonMappingKind.Expression, [new(0)],
                new ComparisonConcatExpression([new ComparisonInputExpression(0), new ComparisonLiteralExpression("!")]))];
            using var json = JsonDocument.Parse(ComparisonEvidence.BuildJson(input, output, new(1, 0), mappings));
            Equal(2, json.RootElement.GetProperty("inputRecordNumber").GetInt32());
            Equal(1, json.RootElement.GetProperty("outputRecordNumber").GetInt32());
            var results = json.RootElement.GetProperty("results");
            Equal("CalculatedExpectedEqual", results[0].GetProperty("result").GetProperty("Status").GetString());
            Equal("FieldUnmapped", results[1].GetProperty("result").GetProperty("Status").GetString());
            Equal(false, json.RootElement.GetProperty("input").GetProperty("HasSourceBytes").GetBoolean());
        });
        Test("Comparison evidence cannot overwrite a source file", () =>
        {
            var path = Path.Combine(temporaryDirectory, "comparison-evidence-source.csv");
            File.WriteAllText(path, "original");
            var data = ComparisonDataSet.FromRecords([new("x", FieldDataType.Text, 8)], new[] { new[] { "x" } }, dataPath: path);
            Throws<InvalidDataException>(() => ComparisonEvidence.Write(path, data, data, new(0, 0), []));
            Equal("original", File.ReadAllText(path));
        });
        Test("Published comparison samples preserve changed row order and unmatched keys", () =>
        {
            var profile = new ComparisonDataProfile(DataFileFormat.Csv, CsvTextEncoding.Utf8);
            var input = ComparisonDataSet.Load(Path.Combine(repo, "samples/comparison-input-definition.csv"),
                Path.Combine(repo, "samples/comparison-input-data-utf8.csv"), profile);
            var output = ComparisonDataSet.Load(Path.Combine(repo, "samples/comparison-output-definition.csv"),
                Path.Combine(repo, "samples/comparison-output-data-utf8.csv"), profile);
            var pairs = ComparisonEngine.PairByKeys(input, output, [0], [3]);
            Equal(1, pairs[0].InputRecordIndex);
            Equal(0, pairs[1].InputRecordIndex);
            Equal(ComparisonPairKind.OutputOnly, pairs[2].Kind);
            Equal(ComparisonPairKind.InputOnly, pairs[3].Kind);
            var concat = new ComparisonConcatExpression([new ComparisonInputExpression(3), new ComparisonInputExpression(4)]);
            var mapping = new ComparisonFieldMapping(2, ComparisonMappingKind.Expression, [new(3), new(4)], concat);
            Equal(ComparisonResultStatus.CalculatedExpectedEqual, ComparisonEngine.Compare(input, output, pairs[0], mapping).Status);
            Equal("0001", input.Records[0][0]);
        });
    }
}
