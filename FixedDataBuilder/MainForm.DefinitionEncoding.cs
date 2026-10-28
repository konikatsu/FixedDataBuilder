namespace FixedDataBuilder;

public sealed partial class MainForm
{
    private DefinitionTextEncoding definitionTextEncoding = DefinitionTextEncoding.Auto;
    private readonly Dictionary<DefinitionTextEncoding, ToolStripMenuItem> definitionEncodingItems = [];

    private ToolStripMenuItem BuildDefinitionEncodingMenu()
    {
        var menu = new ToolStripMenuItem("定義の文字コード（読込）");
        foreach (var value in Enum.GetValues<DefinitionTextEncoding>())
        {
            var item = new ToolStripMenuItem(DefinitionFileText.Name(value))
            {
                Name = $"DefinitionEncoding{value}", Checked = value == definitionTextEncoding
            };
            item.Click += (_, _) => SetDefinitionTextEncoding(value);
            definitionEncodingItems.Add(value, item);
            menu.DropDownItems.Add(item);
        }
        return menu;
    }

    private void SetDefinitionTextEncoding(DefinitionTextEncoding value)
    {
        if (!Enum.IsDefined(value)) throw new ArgumentOutOfRangeException(nameof(value));
        definitionTextEncoding = value;
        foreach (var pair in definitionEncodingItems) pair.Value.Checked = pair.Key == value;
        SetStatus($"定義の文字コード: {DefinitionFileText.Name(value)}。次の読込・定義再読み込みから適用します。");
    }

    private DefinitionTextEncoding InitialDefinitionSaveEncoding(bool editCurrent)
    {
        if (definitionTextEncoding != DefinitionTextEncoding.Auto) return definitionTextEncoding;
        var path = GetCurrentDefinitionPath();
        if (editCurrent && path is not null && File.Exists(path))
        {
            try { return DefinitionFileText.SuggestedSaveEncoding(path, definitionTextEncoding); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return DefinitionTextEncoding.Utf8;
    }
}
