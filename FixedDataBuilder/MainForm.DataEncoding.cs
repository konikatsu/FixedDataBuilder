namespace FixedDataBuilder;

public sealed partial class MainForm
{
    private void LoadAutomaticData(string path, bool isCsv, RecordSeparatorMode separator,
        NationalTextEncoding national, bool hasHeader = false)
    {
        var detection = isCsv ? DataEncodingDetector.Csv(path)
            : DataEncodingDetector.Fixed(path, BuildFieldsForNationalTextEncoding(national), separator);
        if (isCsv) LoadCsvData(path, detection.Encoding!.Value, hasHeader);
        else LoadData(path, separator, national, detection.FixedEncoding(currentDataEncodingProfile));
    }
}
