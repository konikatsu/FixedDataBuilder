using System.Drawing.Imaging;
using System.Reflection;
using System.Text;
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
        var preferencesPath = DataLoadSettingsStore.DefaultPath;
        var previousPreferences = File.Exists(preferencesPath) ? File.ReadAllBytes(preferencesPath) : null;
        var fontSettingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FixedDataBuilder", "settings.txt");
        var previousFontSettings = File.Exists(fontSettingsPath) ? File.ReadAllBytes(fontSettingsPath) : null;
        try
        {
            var repo = Path.GetFullPath(args[0]);
            if (args.Length == 3 && args[1] == "--verify-column-view")
            {
                RunColumnViewTests();
                RunColumnSearchTests();
                RunColumnLifecycleTests();
                Test("Provided CSV fills column-view pages without clipping or losing lines",
                    () => VerifyProvidedColumnView(args[2]));
                Console.WriteLine($"PASS: {passed} tests");
                return 0;
            }
            if (args.Length >= 4 && args[1] == "--capture-unsaved")
            {
                using var form = CreateCellDisplayForm(recordRows: true);
                Get<TextBox>(form, "definitionPathTextBox").Text = "（表示確認用サンプル）";
                Get<TextBox>(form, "dataPathTextBox").Text = "（架空データ・5行）";
                var row = (int)Invoke(form, "RecordRowsDataStartIndex")!;
                Get<DataGridView>(form, "grid")[1, row].Value = "9999999999999999999999";
                Invoke(form, "UpdateRecordValue", row, 1);
                Capture(form, args[2], expectedHex: null);
                WithDialog("未保存の変更", DialogResult.Cancel,
                    () => Invoke(form, "ConfirmDiscardChanges", "終了"), dialog => Capture(dialog, args[3], expectedHex: null));
                return 0;
            }
            if (args.Length >= 3 && args[1] == "--capture-remembered")
            {
                var definition = Path.Combine(repo, "samples", "csv-import-definition.csv");
                var data = Path.Combine(repo, "samples", "csv-import-data-utf8.csv");
                var store = new DataLoadSettingsStore(Path.Combine(temporaryDirectory, "capture-load-settings.json"));
                store.Remember(definition, data, new DataLoadSettings(DataFileFormat.Csv, CsvTextEncoding.Utf8,
                    RecordSeparatorMode.CrLfOrLf, NationalTextEncoding.ShiftJis));
                using var form = new MainForm(["--definition", definition], store);
                WithDialog("データ読込条件", DialogResult.Cancel,
                    () => Invoke(form, "LoadDataFromPath", data, "読込完了"), dialog => Capture(dialog, args[2], expectedHex: null));
                return 0;
            }
            if (args.Length >= 4 && args[1] == "--capture-excel")
            {
                using var exportForm = new MainForm([
                    "--definition", Path.Combine(repo, "samples", "csv-import-definition.csv"),
                    "--data", Path.Combine(repo, "samples", "csv-import-data-utf8.csv"), "--layout", "record"]);
                ExcelExporter.Write(args[2], "レコード縦", (IReadOnlyList<IReadOnlyList<ExcelCell>>)Invoke(exportForm, "BuildExcelRows")!);
                Get<ToolStripMenuItem>(exportForm, "fieldRowsButton").PerformClick();
                ExcelExporter.Write(args[3], "項目縦", (IReadOnlyList<IReadOnlyList<ExcelCell>>)Invoke(exportForm, "BuildExcelRows")!);
                return 0;
            }
            if (args.Length >= 5 && args[1] == "--capture-csv-only")
            {
                CaptureCsv(repo, args[2], args[3]);
                using var displayForm = CreateCellDisplayForm(recordRows: true);
                Get<TextBox>(displayForm, "definitionPathTextBox").Text = "（表示確認用サンプル）";
                Get<TextBox>(displayForm, "dataPathTextBox").Text = "（架空データ・5レコード）";
                Capture(displayForm, args[4], expectedHex: null);
                return 0;
            }
            if (args.Length >= 5 && args[1] == "--capture-encodings")
            {
                var definition = Path.Combine(repo, "samples", "encoding-comparison-definition.csv");
                var data = Path.Combine(repo, "samples", "encoding-sjis.csv");
                var store = new DataLoadSettingsStore(Path.Combine(temporaryDirectory, "capture-encoding-settings.json"));
                store.Remember(definition, data, new DataLoadSettings(DataFileFormat.Csv, CsvTextEncoding.StrictShiftJis,
                    RecordSeparatorMode.CrLfOrLf, NationalTextEncoding.StrictShiftJis, DataEncodingProfile.StrictShiftJis));
                using var form = new MainForm(["--definition", definition, "--data", data, "--format", "csv", "--csv-encoding", "sjis", "--layout", "record"], store);
                Get<TextBox>(form, "definitionPathTextBox").Text = "（文字コード比較用サンプル定義）";
                Get<TextBox>(form, "dataPathTextBox").Text = "encoding-sjis.csv（架空データ3行）";
                Capture(form, args[2], expectedHex: null);
                using var load = new MainForm(["--definition", definition], store);
                WithDialog("データ読込条件", DialogResult.Cancel, () => Invoke(load, "LoadDataFromPath", data, "読込完了"),
                    dialog => Capture(dialog, args[3], expectedHex: null));
                using var save = new DataSaveOptionsForm(new DataSaveOptions(DataFileFormat.FixedLength, CsvTextEncoding.StrictShiftJis,
                    RecordSeparatorMode.CrLfOrLf, NationalTextEncoding.StrictShiftJis, DataEncodingProfile.StrictShiftJis));
                Capture(save, args[4], expectedHex: null);
                return 0;
            }
            if (args.Length >= 4 && args[1] == "--capture-single-record")
            {
                using var form = CreateCellDisplayForm(recordRows: false);
                Get<TextBox>(form, "definitionPathTextBox").Text = "（表示確認用サンプル）";
                Get<TextBox>(form, "dataPathTextBox").Text = "（架空データ・5行）";
                Capture(form, args[2], expectedHex: null);
                Invoke(form, "SetSingleRecordView", true);
                Capture(form, args[3], expectedHex: null);
                if (args.Length >= 5)
                {
                    using var csvForm = new MainForm(["--definition", Path.Combine(repo, "samples", "csv-header-wrap-definition.csv"),
                        "--data", Path.Combine(repo, "samples", "space-count-data-utf8.csv"), "--csv-header", "present", "--layout", "field"]);
                    Get<TextBox>(csvForm, "definitionPathTextBox").Text = "（表示確認用CSV定義）";
                    Get<TextBox>(csvForm, "dataPathTextBox").Text = "（公開サンプルCSV）";
                    Invoke(csvForm, "SetSingleRecordView", true);
                    Capture(csvForm, args[4], expectedHex: null);
                }
                return 0;
            }
            if (args.Length >= 6 && args[1] == "--capture-manual-hex")
            {
                using var form = CreateCellDisplayForm(recordRows: false);
                Get<TextBox>(form, "definitionPathTextBox").Text = "（表示確認用サンプル）";
                Get<TextBox>(form, "dataPathTextBox").Text = "（架空データ・5行）";
                Capture(form, args[2], expectedHex: null);
                Invoke(form, "SetHexAreaHeight", 200);
                Capture(form, args[3], expectedHex: null);
                Invoke(form, "ApplyDisplayWrapOptions", new DisplayWrapOptions(true, 20, 10, true, 12));
                Capture(form, args[4], expectedHex: null);
                using var dialog = new DisplayWrapOptionsForm(new DisplayWrapOptions(true, 20, 10, true, 12));
                Capture(dialog, args[5], expectedHex: null);
                return 0;
            }
            if (args.Length >= 5 && args[1] == "--capture-field-numbers")
            {
                using var form = CreateCellDisplayForm(recordRows: false);
                Get<TextBox>(form, "definitionPathTextBox").Text = "（表示確認用サンプル）";
                Get<TextBox>(form, "dataPathTextBox").Text = "（架空データ・5行）";
                Capture(form, args[2], expectedHex: null);
                Get<HashSet<int>>(form, "hiddenFieldIndexes").Add(1);
                Invoke(form, "RefreshGrid");
                Capture(form, args[3], expectedHex: null);
                Get<HashSet<int>>(form, "hiddenFieldIndexes").Clear();
                Get<ToolStripMenuItem>(form, "recordRowsButton").PerformClick();
                Capture(form, args[4], expectedHex: null);
                return 0;
            }
            if (args.Length >= 5 && args[1] == "--capture-display-limit")
            {
                using var form = CreateCellDisplayForm(recordRows: true);
                Get<TextBox>(form, "definitionPathTextBox").Text = "（表示確認用サンプル）";
                Get<TextBox>(form, "dataPathTextBox").Text = "（架空データ・5行）";
                Capture(form, args[2], expectedHex: null);
                Invoke(form, "ApplyDisplayWrapOptions", new DisplayWrapOptions(true, 20, 10, true, 12));
                Capture(form, args[3], expectedHex: null);
                using var dialog = new DisplayWrapOptionsForm(new DisplayWrapOptions(true, 20, 10, true, 12));
                Capture(dialog, args[4], expectedHex: null);
                return 0;
            }
            Test("UTF-8 byte length validation matches writer", () =>
            {
                var field = new FieldDefinition("氏名", FieldDataType.Text, 4);
                var records = new List<List<string>> { new() { "東京" } };
                Equal(0, RecordValidator.Validate([field], records).Count);
                Equal(1, RecordValidator.Validate([field], records, DataEncodingProfile.Utf8WithNationalText).Count);
                Throws<InvalidDataException>(() => FixedLengthDataWriter.EncodeField(field, "東京", DataEncodingProfile.Utf8WithNationalText, NationalTextEncoding.ShiftJis));
            });
            Test("Legacy CSV and copybook still load", () =>
            {
                var csvFields = DefinitionCsvReader.Read(Path.Combine(repo, "samples", "basic-definition.csv"));
                Equal(true, csvFields.Count > 0);
                var csvRecords = FixedLengthDataReader.Read(Path.Combine(repo, "samples", "basic-data-sjis-crlf.dat"), csvFields, RecordSeparatorMode.CrLfOrLf);
                Equal(3, csvRecords.Count);
                Equal(true, CopybookDefinitionReader.Read(Path.Combine(repo, "samples", "copybook-basic-definition.cbl")).Count > 0);
            });
            RunCellDisplayTests(repo);
            RunDragDropTests(repo);
            RunCsvDataTests(repo);
            RunCsvUnicodeTests();
            RunCsvDefinitionInferenceTests(repo);
            RunCsvImportLayoutTests();
            RunFlexibleDefinitionTests(repo);
            RunLiveCommentReviewTests(repo);
            RunComparisonSetTests(repo);
            RunDirectComparisonTests(repo);
            RunColumnViewTests();
            RunColumnSearchTests();
            RunColumnLifecycleTests();
            RunReloadTests(repo);
            RunHeaderRowsTests(repo);
            RunUnsavedAndSettingsTests(repo);
            RunJapaneseEncodingTests(repo);
            RunCsvHeaderTests(repo);
            RunDisplayWrapTests(repo);
            RunFixedPaddingWrapTests();
            RunSpaceAndZoomTests(repo);
            RunDisplayLimitTests(repo);
            RunFieldNumberTests(repo);
            RunManualHexResizeTests(repo);
            RunSingleRecordViewTests(repo);
            RunFieldCommentTests(repo);
            RunDefinitionEncodingTests(repo);
            RunDataEncodingDetectionTests(repo);
            RunHorizontalHeaderTests(repo);
            RunExcelHorizontalExportTests(repo);
            RunByteRulerTests(repo);
            RunGridCaptureTests(repo);
            RunImageClipboardTests(repo);
            RunRecordNameWrappingTests(repo);
            RunTestDataGeneratorTests(repo);
            RunTestDataGenerationFormTests(repo);
            RunTestDataGenerationPreferencesTests(repo);
            RunRowDeletionTests(repo);
            RunFixedTextSpaceTests(repo);
            RunTestDataGenerationIntegrationTests(repo);
            RunComparisonEngineTests(repo);
            RunComparisonRuleFormTests(repo);
            RunComparisonFormTests(repo);
            RunComparisonIntegrationTests(repo);
            RunComparisonCsvAliasTests(repo);
            RunVisualComparisonTests(repo);
            RunVisualComparisonRulerTests(repo);

            if (args.Length >= 4 && args[1] == "--capture-csv")
                CaptureCsv(repo, args[2], args[3]);

            if (args.Length >= 4 && args[1] == "--verify-csv")
            {
                Test("Provided CSV layout and 10 records read through UI with all 29 columns", () =>
                {
                    var providedFields = DefinitionCsvReader.Read(args[2]);
                    var sourceBytes = File.ReadAllBytes(args[3]);
                    var providedRows = CsvDataReader.Read(args[3], providedFields);
                    Equal(29, providedFields.Count);
                    Equal(10, providedRows.Count);
                    Equal(true, providedRows.All(row => row.Count == 29));
                    using var form = new MainForm(["--definition", args[2], "--data", args[3], "--layout", "record"]);
                    Equal(true, providedRows.SelectMany(row => row).SequenceEqual(Get<List<List<string>>>(form, "records").SelectMany(row => row)));
                    Equal(args[3], Get<string?>(form, "saveDataPath"));
                    var csvCopy = Path.Combine(temporaryDirectory, "provided-roundtrip.csv");
                    Invoke(form, "WriteData", csvCopy, new DataSaveOptions(DataFileFormat.Csv, CsvTextEncoding.Utf8,
                        RecordSeparatorMode.CrLfOrLf, NationalTextEncoding.ShiftJis));
                    Equal(true, providedRows.SelectMany(row => row).SequenceEqual(CsvDataReader.Read(csvCopy, providedFields).SelectMany(row => row)));
                    Equal(true, sourceBytes.SequenceEqual(File.ReadAllBytes(args[3])));
                });
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
            Console.WriteLine($"PASS: {passed} tests");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally
        {
            if (previousHistory is not null) File.WriteAllBytes(recentPath, previousHistory);
            else if (File.Exists(recentPath)) File.Delete(recentPath);
            if (previousPreferences is not null) File.WriteAllBytes(preferencesPath, previousPreferences);
            else if (File.Exists(preferencesPath)) File.Delete(preferencesPath);
            if (previousFontSettings is not null) File.WriteAllBytes(fontSettingsPath, previousFontSettings);
            else if (File.Exists(fontSettingsPath)) File.Delete(fontSettingsPath);
            // 一意の専用一時ディレクトリのみを削除する。
            Directory.Delete(temporaryDirectory, true);
        }
    }

    private static void Capture(Form form, string path, string? expectedHex = "E5 B1 B1")
    {
        if (form is MainForm mainForm)
        {
            form.Text = $"FixedDataBuilder v{typeof(MainForm).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion}"
                + (Get<bool>(mainForm, "hasUnsavedChanges") ? " 【未保存】" : string.Empty);
            var grid = Get<DataGridView>(mainForm, "grid");
            var dataRow = Get<ToolStripMenuItem>(mainForm, "recordRowsButton").Checked
                ? (int)Invoke(mainForm, "RecordRowsDataStartIndex")! : expectedHex is null ? 0 : 1;
            grid.CurrentCell = grid[Get<bool>(mainForm, "isSingleRecordView") ? grid.Columns["SingleRecordValue"].Index : 2, dataRow];
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
