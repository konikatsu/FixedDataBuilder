using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace FixedDataBuilder;

public sealed record XmlLayoutDefinition(
    string Id,
    string Name,
    IReadOnlyList<FieldDefinition> Fields,
    DataEncodingProfile EncodingProfile,
    RecordSeparatorMode SeparatorMode,
    string? UnsupportedReason = null)
{
    public bool IsSupported => UnsupportedReason is null;
    public int RecordLength => Fields.Sum(field => field.StorageByteLength);
    public string EncodingName => EncodingProfile == DataEncodingProfile.Utf8WithNationalText ? "UTF-8" : "Shift_JIS";
    public override string ToString() => IsSupported
        ? $"{Name} [{Id}] / {RecordLength} バイト / {EncodingName}"
        : $"{Name} [{Id}] / 対象外: {UnsupportedReason}";
}

/// <summary>FileDefinitions / File / Record / Column 形式の汎用XML定義を読み込む。</summary>
public static class XmlDefinitionReader
{
    public static IReadOnlyList<XmlLayoutDefinition> Read(string path)
    {
        using var reader = XmlReader.Create(path, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = 10_000_000
        });
        var root = XDocument.Load(reader).Root;
        if (root?.Name.LocalName != "FileDefinitions")
        {
            throw new InvalidDataException("汎用XML定義のルート要素は FileDefinitions にしてください。");
        }

        var files = Children(root, "File").ToList();
        if (files.Count == 0)
        {
            throw new InvalidDataException("汎用XML定義に File 要素がありません。");
        }

        var layouts = new List<XmlLayoutDefinition>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            var id = Attribute(file, "id") ?? $"layout-{layouts.Count + 1}";
            var name = Attribute(file, "name") ?? id;
            if (!ids.Add(id))
            {
                throw new InvalidDataException($"XMLレイアウトIDが重複しています: {id}");
            }

            try
            {
                layouts.Add(ReadLayout(file, id, name));
            }
            catch (InvalidDataException ex)
            {
                // 可変長などの対象外レイアウトが併記されていても、固定長レイアウトは選べる。
                layouts.Add(new(id, name, [], DataEncodingProfile.ShiftJis, RecordSeparatorMode.None, ex.Message));
            }
        }
        return layouts;
    }

    private static XmlLayoutDefinition ReadLayout(XElement file, string id, string name)
    {
        if (!string.Equals(Attribute(file, "type"), "SEQ", StringComparison.OrdinalIgnoreCase)
            || BooleanAttribute(file, "isVariable"))
        {
            throw new InvalidDataException("固定長SEQのみ取り込めます。");
        }
        if (BooleanAttribute(file, "existBOM") || BooleanAttribute(file, "existShiftCode"))
        {
            throw new InvalidDataException("BOM・シフトコード付きの定義には対応していません。");
        }

        var encoding = (Attribute(file, "charSet") ?? "").ToUpperInvariant().Replace("-", "").Replace("_", "") switch
        {
            "UTF8" => DataEncodingProfile.Utf8WithNationalText,
            "SHIFTJIS" or "SJIS" or "MS932" or "WINDOWS31J" or "CP932" => DataEncodingProfile.ShiftJis,
            _ => throw new InvalidDataException("charSet は UTF-8 または Shift_JIS を指定してください。")
        };
        var separator = (Attribute(file, "lfType") ?? "").ToUpperInvariant() switch
        {
            "" or "NONE" => RecordSeparatorMode.None,
            "CRLF" => RecordSeparatorMode.CrLfOrLf,
            _ => throw new InvalidDataException("lfType は CRLF または改行なし（省略／NONE）に対応しています。")
        };
        var dataRecords = Children(file, "Record")
            .Where(record => Attribute(record, "type") is null or "data").ToList();
        if (dataRecords.Count != 1 || Children(file, "Record").Count() != 1)
        {
            throw new InvalidDataException("1つの File に1つのデータ用 Record を指定してください。複数レコード様式・ヘッダー等は未対応です。");
        }

        var record = dataRecords[0];
        var columns = Children(record, "Column").ToList();
        if (columns.Count == 0)
        {
            throw new InvalidDataException("Record に Column がありません。");
        }
        var indexed = columns.Select(column => (Column: column, Index: PositiveInteger(column, "index")))
            .OrderBy(item => item.Index).ToList();
        var fields = new List<FieldDefinition>();
        foreach (var (column, index) in indexed)
        {
            if (index != fields.Count + 1)
            {
                throw new InvalidDataException("Column の index は1から連続する、重複しない番号にしてください。");
            }
            var fieldName = Attribute(column, "name")
                ?? throw new InvalidDataException($"Column {index}: name がありません。");
            var length = PositiveInteger(column, "length");
            var type = Attribute(column, "type");
            if (type is not ("X" or "9"))
            {
                throw new InvalidDataException($"{fieldName}: 型 {type ?? "(未指定)"} は未対応です。X / 9 に対応しています。");
            }
            if (type == "9" && Attribute(column, "size") is not null && PositiveInteger(column, "size") != length)
            {
                throw new InvalidDataException($"{fieldName}: 数字の size と length が異なる定義は未対応です。");
            }
            fields.Add(new(fieldName, type == "9" ? FieldDataType.PlainNumber : FieldDataType.Text,
                length, $"{type}({length})"));
        }
        if (Attribute(record, "columnCount") is not null && PositiveInteger(record, "columnCount") != fields.Count)
        {
            throw new InvalidDataException("Record の columnCount と項目数が一致しません。");
        }
        var actualLength = fields.Sum(field => (long)field.StorageByteLength);
        if (actualLength > int.MaxValue)
        {
            throw new InvalidDataException("レコード長が大きすぎます。");
        }
        if (Attribute(record, "length") is not null && PositiveInteger(record, "length") != actualLength)
        {
            throw new InvalidDataException($"Record の length と項目の合計バイト長 {actualLength} が一致しません。");
        }
        return new(id, name, fields, encoding, separator);
    }

    private static IEnumerable<XElement> Children(XElement element, string name) =>
        element.Elements().Where(child => child.Name.LocalName == name);

    private static string? Attribute(XElement element, string name) =>
        string.IsNullOrWhiteSpace((string?)element.Attribute(name)) ? null : ((string)element.Attribute(name)!).Trim();

    private static int PositiveInteger(XElement element, string name) =>
        int.TryParse(Attribute(element, name), NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value > 0
            ? value
            : throw new InvalidDataException($"{element.Name.LocalName} の {name} は正の整数で指定してください。");

    private static bool BooleanAttribute(XElement element, string name)
    {
        var text = Attribute(element, name);
        if (text is null) return false;
        return bool.TryParse(text, out var value) ? value
            : throw new InvalidDataException($"{name} は true / false で指定してください。");
    }
}
