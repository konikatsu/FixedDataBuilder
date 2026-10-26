namespace FixedDataBuilder;

public sealed class XmlLayoutSelectionForm : Form
{
    public XmlLayoutDefinition? SelectedLayout { get; private set; }

    public XmlLayoutSelectionForm(IReadOnlyList<XmlLayoutDefinition> layouts)
    {
        Text = "レイアウト選択";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(680, 360);
        ClientSize = new Size(900, 400);
        MinimizeBox = false;
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), RowCount = 4, ColumnCount = 1 };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        panel.Controls.Add(new Label { Text = "取り込む固定長レイアウトを選択してください。", AutoSize = true }, 0, 0);
        var list = new ListBox { Dock = DockStyle.Fill, HorizontalScrollbar = true, IntegralHeight = false };
        foreach (var layout in layouts) list.Items.Add(layout);
        panel.Controls.Add(list, 0, 1);
        var details = new Label { Dock = DockStyle.Fill, AutoEllipsis = true, Padding = new Padding(0, 8, 0, 0) };
        panel.Controls.Add(details, 0, 2);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var cancel = new Button { Text = "キャンセル", DialogResult = DialogResult.Cancel, AutoSize = true };
        var ok = new Button { Text = "取り込む", AutoSize = true };
        buttons.Controls.AddRange([cancel, ok]);
        panel.Controls.Add(buttons, 0, 3);
        Controls.Add(panel);
        AcceptButton = ok;
        CancelButton = cancel;
        list.SelectedIndexChanged += (_, _) =>
        {
            SelectedLayout = list.SelectedItem as XmlLayoutDefinition;
            ok.Enabled = SelectedLayout?.IsSupported == true;
            details.Text = SelectedLayout is { IsSupported: true } selected
                ? $"{selected.Fields.Count} 項目 / {selected.RecordLength} バイト / {selected.EncodingName} / " +
                    (selected.SeparatorMode == RecordSeparatorMode.None ? "改行なし" : "改行あり (CRLF)")
                : SelectedLayout?.UnsupportedReason ?? "";
        };
        ok.Click += (_, _) => { if (SelectedLayout?.IsSupported == true) DialogResult = DialogResult.OK; };
        list.DoubleClick += (_, _) => { if (ok.Enabled) ok.PerformClick(); };
        list.SelectedIndex = layouts.ToList().FindIndex(layout => layout.IsSupported);
    }
}
