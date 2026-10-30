using System.Text;
using FixedDataBuilder;

internal static partial class Program
{
    private static void RunComparisonCsvAliasTests(string repo)
    {
        Test("Comparison CSV COPY aliases do not impose fixed byte ranges or expose synthetic alias values", () =>
        {
            var definitionPath = Path.Combine(temporaryDirectory, "comparison-csv-long-alias.cbl");
            var dataPath = Path.Combine(temporaryDirectory, "comparison-csv-long-alias.csv");
            File.WriteAllText(definitionPath,
                "       01 SYNTHETIC-RECORD.\r\n" +
                "          05 BASE-FIELD PIC X(1).\r\n" +
                "          05 ALIAS-FIELD REDEFINES BASE-FIELD PIC X(2).\r\n", new UTF8Encoding(false));
            File.WriteAllText(dataPath, "A\r\nB\r\n", new UTF8Encoding(false));
            var fields = CopybookDefinitionReader.Read(definitionPath, DefinitionTextEncoding.Utf8);
            Equal(2, fields.Count);
            Equal(true, fields[1].IsRedefines);
            Equal(true, fields[1].PhysicalByteLength > fields[0].PhysicalByteLength);
            var existingRecords = CsvDataReader.Read(dataPath, fields, CsvTextEncoding.Utf8);
            var profile = new ComparisonDataProfile(DataFileFormat.Csv, CsvTextEncoding.Utf8);
            var loaded = ComparisonDataSet.Load(definitionPath, dataPath, profile, DefinitionTextEncoding.Utf8);
            Equal(existingRecords.Count, loaded.Records.Count);
            Equal("A|B", string.Join("|", loaded.Records.Select(row => row[0])));
            Equal(true, loaded.HasSourceBytes);
            Equal(0, loaded.RawRecords.Count);
            Equal("A\r\nB\r\n", Encoding.UTF8.GetString(loaded.CopySourceBytes()!));
            Equal("A", loaded.ReadSource(0, 0).Value);
            var alias = loaded.ReadSource(0, 1);
            Equal(false, alias.Success);
            Equal(true, alias.Reason!.Contains("CSVに物理列を持たないREDEFINES"));
            Equal(false, loaded.ReadSource(0, 1, ComparisonSourceKind.EncodedCsvLogicalValue).Success);
            Throws<InvalidOperationException>(() => loaded.CopyRawFieldBytes(0, 0));
            Throws<InvalidOperationException>(() => loaded.CopyRawRecordBytes(0));
            var snapshot = ComparisonDataSet.FromRecords(fields, existingRecords, profile);
            Equal(2, snapshot.Records.Count);
            Equal(false, snapshot.ReadSource(0, 1).Success);
            // The same alias still cannot overrun a fixed-length record, even in a logical snapshot.
            Throws<InvalidDataException>(() => ComparisonDataSet.FromRecords(fields, existingRecords,
                new ComparisonDataProfile(DataFileFormat.FixedLength)));
        });
    }
}
