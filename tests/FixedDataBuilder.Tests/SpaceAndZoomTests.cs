using System.Reflection;
using System.Text;
using FixedDataBuilder;

internal static partial class Program
{
    private static void RunSpaceAndZoomTests(string repo)
    {
        var settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FixedDataBuilder", "settings.txt");
        var oldSettings = File.Exists(settingsPath) ? File.ReadAllBytes(settingsPath) : null;
        try
        {
            Test("Space count distinguishes literal half/full-width spaces and excludes tabs and newlines in CSV", () =>
            {
                var summary = SelectionSpaceSummary.Count(" A  \u3000B\u3000\t\r\n ", new FieldDefinition("文字", FieldDataType.Text, 100), true, Encoding.UTF8);
                Equal(new SelectionSpaceSummary(4, 2, 0, 0), summary);
                Equal(new SelectionSpaceSummary(0, 0, 0, 0), SelectionSpaceSummary.Count("", new FieldDefinition("空欄", FieldDataType.Text, 100), true, Encoding.UTF8));
            });
            Test("Fixed-length space counts include the same inferred padding shown by the grid", () =>
            {
                Equal(new SelectionSpaceSummary(7, 0, 5, 0), SelectionSpaceSummary.Count("A  BC", new FieldDefinition("X", FieldDataType.Text, 10), false, Encoding.GetEncoding(932)));
                Equal(new SelectionSpaceSummary(1, 2, 1, 0), SelectionSpaceSummary.Count("　A　", new FieldDefinition("X", FieldDataType.Text, 6), false, Encoding.GetEncoding(932)));
                Equal(new SelectionSpaceSummary(0, 3, 0, 3), SelectionSpaceSummary.Count("日", new FieldDefinition("N", FieldDataType.FullWidthText, 4), false, Encoding.Unicode));
                Equal(new SelectionSpaceSummary(0, 0, 0, 0), SelectionSpaceSummary.Count("1", new FieldDefinition("9", FieldDataType.PlainNumber, 8), false, Encoding.UTF8));
            });
            Test("Byte-based fixed X padding uses selected encoding and invalid SJIS retains actual space counts", () =>
            {
                var field = new FieldDefinition("X", FieldDataType.Text, 8);
                Equal(2, SelectionSpaceSummary.Count("東京", field, false, Encoding.UTF8).PaddingHalfWidth);
                Equal(4, SelectionSpaceSummary.Count("東京", field, false, Encoding.GetEncoding(932)).PaddingHalfWidth);
                Equal(new SelectionSpaceSummary(1, 1, 0, 0), SelectionSpaceSummary.Count("① 　", field, false, StrictShiftJisEncoding.Instance));
            });
            Test("Selected space summary follows data cells and clears for metadata in both grid layouts", () =>
            {
                foreach (var recordRows in new[] { true, false })
                {
                    using var form = CreateCellDisplayForm(recordRows);
                    var grid = Get<DataGridView>(form, "grid");
                    var row = recordRows ? (int)Invoke(form, "RecordRowsDataStartIndex")! + 4 : 0;
                    var column = recordRows ? 1 : 6;
                    grid.CurrentCell = grid[column, row];
                    Invoke(form, "UpdateHexView");
                    Equal(true, Get<Label>(form, "selectionSpaceLabel").Text.Contains("半角スペース: 15文字"));
                    Equal(true, Get<Label>(form, "selectionSpaceLabel").Text.Contains("補完: 半角8"));
                    grid.CurrentCell = grid[0, 0];
                    Invoke(form, "UpdateHexView");
                    Equal("半角スペース: — / 全角スペース: —", Get<Label>(form, "selectionSpaceLabel").Text);
                }
            });
            Test("CSV space counts use raw values after editing and do not count display markers or padding", () =>
            {
                using var form = new MainForm(["--definition", Path.Combine(repo, "samples", "csv-import-definition.csv"),
                    "--data", Path.Combine(repo, "samples", "csv-import-data-utf8.csv"), "--layout", "record"]);
                var grid = Get<DataGridView>(form, "grid");
                var row = (int)Invoke(form, "RecordRowsDataStartIndex")!;
                grid.CurrentCell = grid[2, row];
                grid[2, row].Value = " A　 ";
                Invoke(form, "UpdateRecordValue", row, 2);
                Equal("半角スペース: 2文字 / 全角スペース: 1文字", Get<Label>(form, "selectionSpaceLabel").Text);
            });
            Test("Wheel zoom changes grid and HEX fonts together, preserves data, selection and dirty state", () =>
            {
                using var form = CreateKnownZoomForm();
                var grid = Get<DataGridView>(form, "grid");
                Invoke(form, "SelectRecord", 0);
                var selected = grid.CurrentCell;
                var values = Get<List<List<string>>>(form, "records").SelectMany(r => r).ToArray();
                Invoke(form, "ChangeZoomFromWheel", 120);
                Equal(13f, grid.Font.SizeInPoints);
                Equal(13f, Get<TextBox>(form, "hexTextBox").Font.SizeInPoints);
                Equal(selected, grid.CurrentCell);
                Equal(false, Get<bool>(form, "hasUnsavedChanges"));
                Equal(true, values.SequenceEqual(Get<List<List<string>>>(form, "records").SelectMany(r => r)));
                Invoke(form, "ChangeZoomFromWheel", -120);
                Equal(12f, grid.Font.SizeInPoints);
                using var reopened = new MainForm();
                Equal(12f, Get<DataGridView>(reopened, "grid").Font.SizeInPoints);
            });
            Test("Wheel zoom accumulates partial notches and clamps size without losing remainder", () =>
            {
                using var form = CreateKnownZoomForm();
                var grid = Get<DataGridView>(form, "grid");
                Invoke(form, "ChangeZoomFromWheel", 60);
                Equal(12f, grid.Font.SizeInPoints);
                Invoke(form, "ChangeZoomFromWheel", 60);
                Equal(13f, grid.Font.SizeInPoints);
                Invoke(form, "ChangeZoomFromWheel", 120000);
                Equal(48f, grid.Font.SizeInPoints);
                Invoke(form, "ChangeZoomFromWheel", -120000);
                Equal(6f, grid.Font.SizeInPoints);
            });
            Test("Wheel zoom recalculates wrapped row heights and retains the selected space summary", () =>
            {
                using var form = CreateKnownZoomForm();
                var grid = Get<DataGridView>(form, "grid");
                Invoke(form, "ApplyDisplayWrapOptions", new DisplayWrapOptions(true, 20, 10));
                Invoke(form, "SelectRecord", 0);
                var row = (int)Invoke(form, "RecordRowsDataStartIndex")!;
                var before = grid.Rows[row].Height;
                var count = Get<Label>(form, "selectionSpaceLabel").Text;
                Invoke(form, "ChangeZoomFromWheel", 120 * 6);
                Equal(true, grid.Rows[row].Height > before);
                Equal(count, Get<Label>(form, "selectionSpaceLabel").Text);
                Equal(true, Get<DisplayWrapOptions>(form, "displayWrapOptions").Enabled);
            });
            Test("Ctrl wheel is consumed by both controls while ordinary wheel reaches normal scrolling", () =>
            {
                using var grid = new ProbeZoomGrid();
                using var text = new ProbeZoomTextBox();
                var gridZoom = 0; var gridNormal = 0; var textZoom = 0; var textNormal = 0;
                grid.ZoomWheel += (_, e) => gridZoom += e.Delta;
                grid.MouseWheel += (_, e) => gridNormal += e.Delta;
                text.ZoomWheel += (_, e) => textZoom += e.Delta;
                text.MouseWheel += (_, e) => textNormal += e.Delta;
                grid.SendWheel(Keys.Control, 120); text.SendWheel(Keys.Control, -120);
                Equal(120, gridZoom); Equal(-120, textZoom); Equal(0, gridNormal); Equal(0, textNormal);
                grid.SendWheel(Keys.None, 120); text.SendWheel(Keys.None, -120);
                Equal(120, gridNormal); Equal(-120, textNormal);
            });
        }
        finally
        {
            if (oldSettings is not null) File.WriteAllBytes(settingsPath, oldSettings);
            else if (File.Exists(settingsPath)) File.Delete(settingsPath);
        }
    }

    private static MainForm CreateKnownZoomForm()
    {
        var form = CreateCellDisplayForm(recordRows: true);
        var previous = Get<Font>(form, "displayFont");
        typeof(MainForm).GetField("displayFont", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(form, new Font(previous.FontFamily, 12f, previous.Style, GraphicsUnit.Point));
        Invoke(form, "ApplyDisplayFont");
        previous.Dispose();
        return form;
    }

    private sealed class ProbeZoomGrid : WheelZoomDataGridView
    {
        private Keys modifiers;
        protected override Keys WheelModifiers => modifiers;
        public void SendWheel(Keys keys, int delta) { modifiers = keys; OnMouseWheel(new MouseEventArgs(MouseButtons.None, 0, 0, 0, delta)); }
    }
    private sealed class ProbeZoomTextBox : WheelZoomTextBox
    {
        private Keys modifiers;
        protected override Keys WheelModifiers => modifiers;
        public void SendWheel(Keys keys, int delta) { modifiers = keys; OnMouseWheel(new MouseEventArgs(MouseButtons.None, 0, 0, 0, delta)); }
    }
}
