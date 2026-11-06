using System.Reflection;
using FixedDataBuilder;

internal static partial class Program
{
    private static void RunColumnLifecycleTests()
    {
        Test("Main and column views use standard grid row and text cell templates", () =>
        {
            using var main = new MainForm();
            var mainGrid = Get<WheelZoomDataGridView>(main, "grid");
            Equal(typeof(WheelZoomDataGridView), mainGrid.GetType());
            Equal(typeof(DataGridViewRow), mainGrid.RowTemplate.GetType());
            using var form = new ColumnTextViewForm();
            Invoke(form, "Reflow");
            Equal(true, Get<List<WheelZoomDataGridView>>(form, "panes").All(p => p.GetType() == typeof(WheelZoomDataGridView)
                && p.RowTemplate.GetType() == typeof(DataGridViewRow)
                && p.Columns.Cast<DataGridViewColumn>().All(c => c.CellTemplate!.GetType() == typeof(DataGridViewTextBoxCell))));
        });
        Test("Column page control updates preserve an outer update guard", () =>
        {
            using var form = new ColumnTextViewForm();
            var field = typeof(ColumnTextViewForm).GetField("updating", BindingFlags.Instance | BindingFlags.NonPublic)!;
            field.SetValue(form, true); Invoke(form, "UpdatePageControls"); Equal(true, Get<bool>(form, "updating"));
            field.SetValue(form, false); Invoke(form, "Reflow"); Equal(false, Get<bool>(form, "updating"));
            Equal(false, Get<bool>(form, "reflowing"));
        });
        Test("Canceled column closing permits later reflow", () =>
        {
            using var form = new ColumnTextViewForm();
            form.FormClosing += (_, e) => e.Cancel = true;
            Invoke(form, "OnFormClosing", new FormClosingEventArgs(CloseReason.UserClosing, false));
            Equal(false, Get<bool>(form, "closing"));
            Invoke(form, "Reflow");
            Equal(false, Get<bool>(form, "reflowing"));
            Equal(true, Get<List<WheelZoomDataGridView>>(form, "panes").Count > 0);
        });
    }
}
