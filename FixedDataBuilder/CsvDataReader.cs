using System.Text;

namespace FixedDataBuilder;

public enum CsvTextEncoding { Utf8, ShiftJis }

/// <summary>定義の物理項目順に、CSVの各列を画面の値として取り込む。</summary>
public static class CsvDataReader
{
    public static IReadOnlyList<List<string>> Read(string path, IReadOnlyList<FieldDefinition> fields,
        CsvTextEncoding textEncoding = CsvTextEncoding.Utf8)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var encoding = textEncoding switch
        {
            CsvTextEncoding.Utf8 => new UTF8Encoding(true, true),
            CsvTextEncoding.ShiftJis => Encoding.GetEncoding(932, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback),
            _ => throw new ArgumentOutOfRangeException(nameof(textEncoding))
        };
        var columnCount = fields.Count(field => !field.IsRedefines);
        if (columnCount == 0) throw new InvalidDataException("定義書に項目がありません。");

        using var reader = new StreamReader(path, encoding, detectEncodingFromByteOrderMarks: false);
        var records = new List<List<string>>();
        var cells = new List<string>();
        var cell = new StringBuilder();
        var state = State.Start;
        var started = false;
        var line = 1;
        var recordStartLine = 1;

        InvalidDataException Error(string message) =>
            new($"CSV {records.Count + 1} 行目（ファイル {line} 行目）: {message}");

        void FinishRecord()
        {
            cells.Add(cell.ToString());
            if (cells.Count != columnCount)
                throw new InvalidDataException($"CSV {records.Count + 1} 行目（ファイル {recordStartLine} 行目）: "
                    + $"列数が定義と一致しません。必要 {columnCount} 列 / 実際 {cells.Count} 列。");
            var column = 0;
            // CSV列を持たない数値REDEFINESには、従来の固定長検証で有効な内部初期値を置く。
            records.Add(fields.Select(field => field.IsRedefines
                ? field.Type is FieldDataType.PlainNumber or FieldDataType.SignedNumber
                    or FieldDataType.PackedUnsigned or FieldDataType.PackedSigned ? "0" : string.Empty
                : cells[column++]).ToList());
            cells.Clear();
            cell.Clear();
            state = State.Start;
            started = false;
        }

        while (reader.Read() is var value && value >= 0)
        {
            var ch = (char)value;
            if (state == State.Quoted)
            {
                if (ch == '"')
                {
                    if (reader.Peek() == '"') { reader.Read(); cell.Append('"'); }
                    else state = State.Closed;
                }
                else
                {
                    cell.Append(ch);
                    if (ch == '\r')
                    {
                        if (reader.Peek() == '\n') { reader.Read(); cell.Append('\n'); }
                        line++;
                    }
                    else if (ch == '\n') line++;
                }
                continue;
            }
            if (ch == ',')
            {
                cells.Add(cell.ToString()); cell.Clear(); state = State.Start; started = true;
            }
            else if (ch is '\r' or '\n')
            {
                FinishRecord();
                if (ch == '\r' && reader.Peek() == '\n') reader.Read();
                line++;
                recordStartLine = line;
            }
            else if (state == State.Closed)
                throw Error("閉じ引用符の後にはカンマまたは改行が必要です。");
            else if (ch == '"')
            {
                if (state != State.Start) throw Error("引用符は列の先頭で使用してください。");
                state = State.Quoted; started = true;
            }
            else
            {
                cell.Append(ch); state = State.Unquoted; started = true;
            }
        }
        if (state == State.Quoted) throw Error("引用符が閉じられていません。");
        if (started || cells.Count > 0) FinishRecord();
        if (records.Count == 0) throw new InvalidDataException("CSVデータに行がありません。");
        return records;
    }

    private enum State { Start, Unquoted, Quoted, Closed }
}
