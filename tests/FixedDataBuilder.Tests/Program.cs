using System.Drawing.Imaging;
using System.Reflection;
using System.Text;
using System.Xml;
using FixedDataBuilder;

internal static partial class Program
{
    private static int passed;
    private static string temporaryDirectory = "";

    [STAThread]
    private static int Main(string[] args)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Application.EnableVisualStyles();
        temporaryDirectory = Path.Combine(Path.GetTempPath(), $"FixedDataBuilder-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);
        // UI確認で利用者の履歴を変更しないよう、終了時に元のバイト列へ戻す。
        var recentPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FixedDataBuilder", "recent-files.txt");
        var previousHistory = File.Exists(recentPath) ? File.ReadAllBytes(recentPath) : null;
        try
        {
            var repo = Path.GetFullPath(args[0]);
            var sample = Path.Combine(repo, "samples", "generic-xml-definition.xml");
            var layouts = XmlDefinitionReader.Read(sample);
            var customer = layouts.Single(layout => layout.Id == "customer-fixed");
            var product = layouts.Single(layout => layout.Id == "product-fixed");

            Test("Multiple layouts stay separate; CSV excluded", () =>
            {
                Equal(3, layouts.Count);
                Equal(false, layouts[0].IsSupported);
                Equal(4, customer.Fields.Count);
                Equal(40, customer.RecordLength);
                Equal(RecordSeparatorMode.None, customer.SeparatorMode);
                Equal(DataEncodingProfile.Utf8WithNationalText, customer.EncodingProfile);
                Equal(16, product.RecordLength);
                Equal(RecordSeparatorMode.CrLfOrLf, product.SeparatorMode);
            });
            Test("Index orders columns rather than XML element order", () =>
            {
                var layout = Parse("<Column index='2' name='数量' type='9' length='2'/><Column index='1' name='コード' type='X' length='3'/>");
                Equal(true, layout.IsSupported);
                Equal("コード", layout.Fields[0].Name);
                Equal(5, layout.RecordLength);
            });
            Test("Length and column count checked", () =>
            {
                Equal(false, Parse(Column(), "length='9'").IsSupported);
                Equal(false, Parse(Column(), "columnCount='2'").IsSupported);
                Equal(true, Parse(Column(), "columnCount='1' length='3'").IsSupported);
            });
            Test("Duplicate, gapped and invalid indices rejected", () =>
            {
                foreach (var columns in new[] { Column() + Column(), Column().Replace("index='1'", "index='2'"), Column().Replace("index='1'", "index='0'") })
                    Equal(false, Parse(columns).IsSupported);
            });
            Test("Invalid lengths, names and unsupported types rejected", () =>
            {
                foreach (var column in new[] { Column().Replace("length='3'", "length='0'"), Column().Replace("length='3'", "length='bad'"),
                    Column().Replace("name='コード'", ""), Column().Replace("type='X'", "type='N'") })
                    Equal(false, Parse(column).IsSupported);
                Equal(false, Parse("<Column index='1' name='数量' type='9' length='5' size='3'/>").IsSupported);
            });
            Test("Unsupported encoding, BOM and delimiters rejected", () =>
            {
                foreach (var attributes in new[] { "charSet='UTF-16'", "charSet='UTF-8' existBOM='true'", "charSet='UTF-8' existShiftCode='true'",
                    "charSet='UTF-8' lfType='LF'", "charSet='UTF-8' existBOM='unknown'" })
                    Equal(false, Parse(Column(), fileAttributes: attributes).IsSupported);
            });
            Test("Wrong root, duplicate IDs and external entities rejected", () =>
            {
                Throws<InvalidDataException>(() => Read("<other/>"));
                Throws<InvalidDataException>(() => Read("<FileDefinitions><File id='same'/><File id='same'/></FileDefinitions>"));
                Throws<XmlException>(() => Read("<!DOCTYPE FileDefinitions [<!ENTITY e SYSTEM 'file:///no-such-file'>]><FileDefinitions>&e;</FileDefinitions>"));
            });
            Test("Multiple record formats rejected", () =>
                Equal(false, Read("<FileDefinitions><File type='SEQ' charSet='UTF-8'><Record type='data'/><Record type='header'/></File></FileDefinitions>")[0].IsSupported));
            Test("UTF-8 byte length validation matches writer", () =>
            {
                var field = new FieldDefinition("氏名", FieldDataType.Text, 4);
                var records = new List<List<string>> { new() { "東京" } };
                Equal(0, RecordValidator.Validate([field], records).Count);
                Equal(1, RecordValidator.Validate([field], records, DataEncodingProfile.Utf8WithNationalText).Count);
                Throws<InvalidDataException>(() => FixedLengthDataWriter.EncodeField(field, "東京", DataEncodingProfile.Utf8WithNationalText, NationalTextEncoding.ShiftJis));
            });
            Test("UTF-8 fixed records round trip with Japanese and padding", () =>
            {
                var path = Path.Combine(repo, "samples", "generic-xml-customer-data-utf8-none.dat");
                var bytes = File.ReadAllBytes(path);
                Equal(80, bytes.Length);
                Equal(false, bytes.AsSpan().StartsWith(Encoding.UTF8.GetPreamble()));
                var records = FixedLengthDataReader.Read(path, customer.Fields, customer.SeparatorMode, customer.EncodingProfile, NationalTextEncoding.ShiftJis);
                Equal(2, records.Count);
                Equal("山田 太郎", records[0][1]);
                Equal("00000012", records[0][3]);
                Equal(0, RecordValidator.Validate(customer.Fields, records, customer.EncodingProfile).Count);
                RoundTrip(path, customer, records);
            });
            Test("Shift_JIS CRLF records round trip", () =>
            {
                var path = Path.Combine(repo, "samples", "generic-xml-product-data-sjis-crlf.dat");
                var records = FixedLengthDataReader.Read(path, product.Fields, product.SeparatorMode, product.EncodingProfile, NationalTextEncoding.ShiftJis);
                Equal(2, records.Count);
                Equal("商品A", records[0][0]);
                Equal(36, new FileInfo(path).Length);
                RoundTrip(path, product, records);
            });
            Test("Legacy CSV and copybook still load", () =>
            {
                var csvFields = DefinitionCsvReader.Read(Path.Combine(repo, "samples", "basic-definition.csv"));
                Equal(true, csvFields.Count > 0);
                var csvRecords = FixedLengthDataReader.Read(Path.Combine(repo, "samples", "basic-data-sjis-crlf.dat"), csvFields, RecordSeparatorMode.CrLfOrLf);
                Equal(3, csvRecords.Count);
                Equal(true, CopybookDefinitionReader.Read(Path.Combine(repo, "samples", "copybook-basic-definition.cbl")).Count > 0);
            });
            Test("UI uses selected XML metadata, then resets on CSV", () =>
            {
                using var form = CreateSampleForm(repo);
                Equal(4, Get<List<FieldDefinition>>(form, "fields").Count);
                Equal(2, Get<List<List<string>>>(form, "records").Count);
                Equal(DataEncodingProfile.Utf8WithNationalText, Get<DataEncodingProfile>(form, "currentDataEncodingProfile"));
                Equal(RecordSeparatorMode.None, Get<RecordSeparatorMode>(form, "currentSeparatorMode"));
                Equal(true, Get<TextBox>(form, "xmlLayoutTextBox").Text.Contains("customer-fixed"));
                Equal(true, Invoke(form, "GetValidationError", new FieldDefinition("氏名", FieldDataType.Text, 4), "東京") is string);
                Equal(true, Invoke(form, "LoadDefinition", Path.Combine(repo, "samples", "basic-definition.csv"), null));
                Equal(DataEncodingProfile.ShiftJis, Get<DataEncodingProfile>(form, "currentDataEncodingProfile"));
                Equal("—", Get<TextBox>(form, "xmlLayoutTextBox").Text);
            });
            Test("Failed read and cancelled selection keep existing data", () =>
            {
                using var form = CreateSampleForm(repo);
                var previousFields = Get<List<FieldDefinition>>(form, "fields").ToArray();
                var bad = Path.Combine(temporaryDirectory, "bad.xml");
                File.WriteAllText(bad, "<other/>");
                Throws<TargetInvocationException>(() => Invoke(form, "LoadDefinition", bad, null));
                Equal(true, previousFields.SequenceEqual(Get<List<FieldDefinition>>(form, "fields")));
                using var timer = new System.Windows.Forms.Timer { Interval = 50 };
                timer.Tick += (_, _) =>
                {
                    var dialog = Application.OpenForms.OfType<XmlLayoutSelectionForm>().FirstOrDefault();
                    if (dialog is not null) { timer.Stop(); dialog.DialogResult = DialogResult.Cancel; }
                };
                timer.Start();
                Equal(false, Invoke(form, "LoadDefinition", sample, null));
                Equal(2, Get<List<List<string>>>(form, "records").Count);
                Equal(true, previousFields.SequenceEqual(Get<List<FieldDefinition>>(form, "fields")));
            });
            Test("XML encoding survives Excel export and import", () =>
            {
                using var form = CreateSampleForm(repo);
                var path = Path.Combine(temporaryDirectory, "data.xlsx");
                var rows = (IReadOnlyList<IReadOnlyList<ExcelCell>>)Invoke(form, "BuildExcelRows")!;
                ExcelExporter.Write(path, "項目縦", rows);
                Invoke(form, "LoadExcel", path);
                Equal(DataEncodingProfile.Utf8WithNationalText, Get<DataEncodingProfile>(form, "currentDataEncodingProfile"));
                Equal(RecordSeparatorMode.None, Get<RecordSeparatorMode>(form, "currentSeparatorMode"));
                RoundTrip(Path.Combine(repo, "samples", "generic-xml-customer-data-utf8-none.dat"), customer, Get<List<List<string>>>(form, "records"));
            });
            Test("Selection dialog disables unsupported layouts", () =>
            {
                using var dialog = new XmlLayoutSelectionForm(layouts);
                var list = Descendants(dialog).OfType<ListBox>().Single();
                var ok = Descendants(dialog).OfType<Button>().Single(button => button.Text == "取り込む");
                Equal(true, ok.Enabled);
                list.SelectedIndex = 0;
                Equal(false, ok.Enabled);
                list.SelectedIndex = 2;
                Equal(true, ok.Enabled);
                Equal("product-fixed", dialog.SelectedLayout!.Id);
            });

            RunCellDisplayTests(repo);
            RunDragDropTests(repo);
            RunEncodingAndReloadTests();

            if (args.Length >= 3 && args[1] == "--capture-encoding")
            {
                var fixture = CreateEncodingFixture(DataEncodingProfile.Utf8WithNationalText);
                using var form = new MainForm(["--definition", fixture.DefinitionPath]);
                SelectEncodingInDialog(DataEncodingProfile.Utf8WithNationalText, NationalTextEncoding.ShiftJis,
                    DialogResult.OK, () => Invoke(form, "LoadDataFromPath", fixture.DataPath, "UTF-8データを読み込みました。"),
                    dialog =>
                    {
                        using var bitmap = new Bitmap(dialog.Width, dialog.Height);
                        dialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                        bitmap.Save(args[2], ImageFormat.Png);
                    });
                if (args.Length >= 4)
                {
                    Get<TextBox>(form, "definitionPathTextBox").Text = "（UTF-8・表示確認用CSV）";
                    Get<TextBox>(form, "dataPathTextBox").Text = "（架空のUTF-8データ）";
                    Get<ToolStripMenuItem>(form, "recordRowsButton").PerformClick();
                    Capture(form, args[3], expectedHex: null);
                }
            }

            if (args.Length >= 3 && args[1] == "--capture-display")
            {
                using var form = CreateCellDisplayForm(recordRows: true);
                Get<TextBox>(form, "definitionPathTextBox").Text = "（表示確認用サンプル）";
                Get<TextBox>(form, "dataPathTextBox").Text = "（架空データ・5レコード）";
                Capture(form, args[2], expectedHex: null);
            }

            if (args.Length >= 3 && args[1] == "--capture-drop")
            {
                using var form = new MainForm();
                WithDialog("データ読込条件", DialogResult.OK, () => Drop(form,
                    Path.Combine(repo, "samples", "copybook-group-occurs-definition.cbl"),
                    Path.Combine(repo, "samples", "copybook-group-occurs-data-crlf-utf8-n-utf16le.dat")));
                Capture(form, args[2], expectedHex: null);
            }
            if (args.Length >= 3 && args[1] == "--capture")
            {
                using var form = CreateSampleForm(repo);
                Capture(form, args[2]);
                using var chooser = new XmlLayoutSelectionForm(layouts);
                Capture(chooser, Path.Combine(Path.GetDirectoryName(args[2])!, "screen-image-xml-layout-selection-v0.1.34.png"));
            }
            if (args.Length >= 5 && args[3] == "--integration")
            {
                Test("Provided fixed layout integration", () =>
                {
                    var provided = XmlDefinitionReader.Read(args[4]).Single(layout => layout.IsSupported);
                    Equal(13, provided.Fields.Count);
                    Equal(109, provided.RecordLength);
                    if (args.Length >= 6)
                    {
                        var records = FixedLengthDataReader.Read(args[5], provided.Fields, provided.SeparatorMode, provided.EncodingProfile, NationalTextEncoding.ShiftJis);
                        Equal(10, records.Count);
                        Equal(0, RecordValidator.Validate(provided.Fields, records, provided.EncodingProfile).Count);
                        RoundTrip(args[5], provided, records);
                    }
                });
            }
            Console.WriteLine($"PASS: {passed} tests");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally
        {
            if (previousHistory is not null) File.WriteAllBytes(recentPath, previousHistory);
            else if (File.Exists(recentPath)) File.Delete(recentPath);
            // 一意の専用一時ディレクトリのみを削除する。
            Directory.Delete(temporaryDirectory, true);
        }
    }

    private static MainForm CreateSampleForm(string repo) => new([
        "--definition", Path.Combine(repo, "samples", "generic-xml-definition.xml"), "--xml-layout", "customer-fixed",
        "--data", Path.Combine(repo, "samples", "generic-xml-customer-data-utf8-none.dat"), "--layout", "field"]);
    private static void Capture(Form form, string path, string? expectedHex = "E5 B1 B1")
    {
        if (form is MainForm mainForm)
        {
            form.Text = $"FixedDataBuilder v{typeof(MainForm).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion}";
            var grid = Get<DataGridView>(mainForm, "grid");
            grid.CurrentCell = grid[2, expectedHex is null ? 0 : 1];
        }
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(-30000, -30000);
        form.ShowInTaskbar = false;
        form.Show();
        Application.DoEvents();
        if (form is MainForm displayedForm)
        {
            Invoke(displayedForm, "UpdateHexView");
            if (expectedHex is not null)
                Equal(true, Get<TextBox>(displayedForm, "hexTextBox").Text.Contains(expectedHex));
        }
        using var bitmap = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
        bitmap.Save(path, ImageFormat.Png);
        form.Hide();
    }
    private static void RoundTrip(string source, XmlLayoutDefinition layout, IReadOnlyList<List<string>> records)
    {
        var path = Path.Combine(temporaryDirectory, "roundtrip.dat");
        FixedLengthDataWriter.Write(path, layout.Fields, records, layout.SeparatorMode, layout.EncodingProfile, NationalTextEncoding.ShiftJis);
        Equal(true, File.ReadAllBytes(source).SequenceEqual(File.ReadAllBytes(path)));
    }
    private static XmlLayoutDefinition Parse(string columns, string recordAttributes = "", string fileAttributes = "charSet='UTF-8'") =>
        Read($"<FileDefinitions><File id='test' type='SEQ' {fileAttributes}><Record {recordAttributes}>{columns}</Record></File></FileDefinitions>")[0];
    private static string Column() => "<Column index='1' name='コード' type='X' length='3'/>";
    private static IReadOnlyList<XmlLayoutDefinition> Read(string xml)
    {
        var path = Path.Combine(temporaryDirectory, "definition.xml");
        File.WriteAllText(path, xml, new UTF8Encoding(false));
        return XmlDefinitionReader.Read(path);
    }
    private static T Get<T>(object instance, string field) => (T)instance.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance)!;
    private static object? Invoke(object instance, string method, params object?[] arguments) =>
        instance.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(instance, arguments);
    private static IEnumerable<Control> Descendants(Control control) => control.Controls.Cast<Control>().SelectMany(child => new[] { child }.Concat(Descendants(child)));
    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}, actual {actual}");
    }
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new Exception($"Expected {typeof(T).Name}");
    }
    private static void Test(string name, Action action)
    {
        action(); passed++; Console.WriteLine($"PASS {name}");
    }
}
