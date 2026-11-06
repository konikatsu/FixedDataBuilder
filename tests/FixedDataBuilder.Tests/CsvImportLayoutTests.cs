using System.Text;
using FixedDataBuilder;

internal static partial class Program
{
    private static void RunCsvImportLayoutTests()
    {
        var path = Path.Combine(temporaryDirectory, "csv-import-layout.csv");
        void WriteUnicode() => File.WriteAllText(path, "コード,変換前,変換後\r\nTEST01,000000,00000\r\nTEST01,001000,01000\r\n", new UnicodeEncoding(false, true));
        CsvDefinitionImportForm Show(float fontPoints = 9, Size? client = null)
        {
            var form = new CsvDefinitionImportForm(path)
            {
                ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(-30000, -30000)
            };
            if (fontPoints != form.Font.SizeInPoints) form.Font = new Font(form.Font.FontFamily, fontPoints);
            form.ClientSize = client ?? new Size(975, 745);
            form.Show(); form.PerformLayout(); Application.DoEvents();
            return form;
        }
        Button Import(Form form) => Descendants(form).OfType<Button>().Single(control => control.Name == "CsvInferenceImport");
        Button Cancel(Form form) => Descendants(form).OfType<Button>().Single(control => control.Name == "CsvInferenceCancel");
        void FullyContained(Control control)
        {
            Equal(true, control.Visible);
            var bounds = control.Bounds;
            for (var parent = control.Parent; parent is not null; parent = parent.Parent)
            {
                Equal(true, parent.ClientRectangle.Contains(bounds));
                bounds.Offset(parent.Left, parent.Top);
            }
        }
        void CheckButtons(CsvDefinitionImportForm form)
        {
            var import = Import(form); var cancel = Cancel(form);
            FullyContained(import); FullyContained(cancel);
            Equal(true, import.Width >= import.GetPreferredSize(Size.Empty).Width);
            Equal(false, import.Bounds.IntersectsWith(cancel.Bounds));
            Equal(import, form.AcceptButton); Equal(cancel, form.CancelButton);
        }
        Test("CSV import and cancel buttons remain fully visible at the reported window size", () =>
        {
            WriteUnicode(); using var form = Show(); CheckButtons(form);
            Equal(true, Import(form).Enabled);
            var preview = Descendants(form).OfType<DataGridView>().Single(control => control.Name == "CsvInferencePreview");
            Equal(3, preview.RowCount);
        });
        Test("CSV import footer survives minimum size and repeated narrow and wide resizes", () =>
        {
            WriteUnicode(); using var form = Show();
            foreach (var size in new[] { new Size(820, 640), new Size(1400, 920), new Size(900, 660), new Size(1000, 760) })
            {
                form.ClientSize = size; form.PerformLayout(); Application.DoEvents(); CheckButtons(form);
            }
        });
        Test("CSV import footer reserves both button labels at larger fonts and scaled layouts", () =>
        {
            foreach (var size in new[] { 8.25f, 12f, 16f })
            {
                WriteUnicode(); using var form = Show(size, new Size(1400, 1000));
                CheckButtons(form);
                form.Scale(new SizeF(1.25f, 1.25f)); form.PerformLayout(); Application.DoEvents(); CheckButtons(form);
            }
        });
        Test("Visible CSV import button commits the UTF16 preview using its actual click handler", () =>
        {
            WriteUnicode(); var original = File.ReadAllBytes(path); using var form = Show();
            Import(form).PerformClick();
            Equal(DialogResult.OK, form.DialogResult);
            var result = form.Inference ?? throw new Exception("Import click did not commit.");
            Equal(CsvTextEncoding.Utf16Le, result.Encoding); Equal(true, result.HasHeader);
            Equal(2, result.Records.Count); Equal("000000", result.Records[0][1]); Equal("00000", result.Records[0][2]);
            Equal(true, original.SequenceEqual(File.ReadAllBytes(path)));
        });
        Test("CSV cancel button closes without committing the inferred definition", () =>
        {
            WriteUnicode(); using var form = Show(); Cancel(form).PerformClick();
            Equal(DialogResult.Cancel, form.DialogResult); Equal(null, form.Inference);
        });
        Test("CSV error state keeps both footer buttons visible while disabling only import", () =>
        {
            File.WriteAllText(path, "コード,内容\r\n1,\"未完", new UTF8Encoding(false));
            using var form = Show(); CheckButtons(form);
            Equal(false, Import(form).Enabled); Equal(true, Cancel(form).Enabled);
            var error = Descendants(form).OfType<Label>().Single(control => control.Name == "CsvInferenceError");
            Equal(Color.Firebrick, error.ForeColor); Equal(false, string.IsNullOrWhiteSpace(error.Text));
        });
    }
}
