using System.Reflection;
using System.Text;
using FixedDataBuilder;

internal static partial class Program
{
    private static void RunUnsavedAndSettingsTests(string repo)
    {
        (string Definition, string Data, string Settings) Inputs(string name)
        {
            var definition = Path.Combine(temporaryDirectory, name + "-definition.csv");
            var data = Path.Combine(temporaryDirectory, name + "-data.csv");
            File.WriteAllText(definition, "項目名,定義\r\nコード,X(6)\r\n名称,X(20)\r\n", new UTF8Encoding(false));
            File.WriteAllText(data, "\"000001\",\"alpha\"\r\n", new UTF8Encoding(false));
            return (definition, data, Path.Combine(temporaryDirectory, name + "-settings.json"));
        }
        void EditCode(MainForm form, string value)
        {
            var grid = Get<DataGridView>(form, "grid");
            if (Get<ToolStripMenuItem>(form, "recordRowsButton").Checked)
            {
                var row = (int)Invoke(form, "RecordRowsDataStartIndex")!;
                grid[1, row].Value = value;
                Invoke(form, "UpdateRecordValue", row, 1);
            }
            else
            {
                grid[2, 0].Value = value;
                Invoke(form, "UpdateRecordValue", 0, 2);
            }
        }
        MainForm Open((string Definition, string Data, string Settings) input, string layout = "record") => new(
            ["--definition", input.Definition, "--data", input.Data, "--format", "csv", "--layout", layout],
            new DataLoadSettingsStore(input.Settings));

        Test("Unsaved title changes only on committed edits and clears after successful save in both layouts", () =>
        {
            foreach (var layout in new[] { "record", "field" })
            {
                var input = Inputs("dirty-" + layout);
                using var form = Open(input, layout);
                Equal(false, Get<bool>(form, "hasUnsavedChanges"));
                EditCode(form, "000001");
                Equal(false, Get<bool>(form, "hasUnsavedChanges"));
                EditCode(form, "000009");
                Equal(true, Get<bool>(form, "hasUnsavedChanges"));
                Equal(true, form.Text.Contains("未保存"));
                Invoke(form, "WriteData", input.Data, Invoke(form, "CurrentSaveOptions"));
                Equal(false, Get<bool>(form, "hasUnsavedChanges"));
                Equal(false, form.Text.Contains("未保存"));
                Equal("000009", CsvDataReader.Read(input.Data, Get<List<FieldDefinition>>(form, "fields"))[0][0]);
            }
        });
        Test("Definition reload button is visible and does not act without a definition file", () =>
        {
            using var form = new MainForm();
            form.ShowInTaskbar = false;
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-30000, -30000);
            form.Show();
            var button = Descendants(form).OfType<Button>().Single(item => item.Name == "ReloadDefinition");
            button.PerformClick();
            Equal(true, Get<ToolStripStatusLabel>(form, "statusLabel").Text!.Contains("先に定義ファイル"));
            Equal(0, Get<List<FieldDefinition>>(form, "fields").Count);
        });
        Test("Definition-only reload updates names while preserving edited data, paths and unsaved state", () =>
        {
            var input = Inputs("definition-only");
            using var form = Open(input);
            EditCode(form, "000055");
            Get<HashSet<int>>(form, "hiddenFieldIndexes").Add(1);
            File.WriteAllText(input.Definition, "項目名,定義\r\n更新コード,X(6)\r\n名称,X(20)\r\n", new UTF8Encoding(false));
            Equal(true, Invoke(form, "ReloadDefinitionCore"));
            Equal("更新コード", Get<List<FieldDefinition>>(form, "fields")[0].Name);
            Equal("000055", Get<List<List<string>>>(form, "records")[0][0]);
            Equal(true, Get<bool>(form, "hasUnsavedChanges"));
            Equal(input.Data, Get<string>(form, "saveDataPath"));
            Equal(DataFileFormat.Csv, Get<DataFileFormat>(form, "currentDataFileFormat"));
            Equal(true, Get<HashSet<int>>(form, "hiddenFieldIndexes").Contains(1));
        });
        Test("Incompatible or broken definition reload preserves the original definition and data", () =>
        {
            var input = Inputs("incompatible-definition");
            using var form = Open(input);
            EditCode(form, "000044");
            foreach (var definition in new[] { "項目名,定義\r\nコード,X(7)\r\n名称,X(20)\r\n", "broken" })
            {
                File.WriteAllText(input.Definition, definition, new UTF8Encoding(false));
                Throws<TargetInvocationException>(() => Invoke(form, "ReloadDefinitionCore"));
                Equal(6, Get<List<FieldDefinition>>(form, "fields")[0].Length);
                Equal("000044", Get<List<List<string>>>(form, "records")[0][0]);
                Equal(true, Get<bool>(form, "hasUnsavedChanges"));
            }
        });
        Test("Definition-only workspace accepts structural updates without changing the data file", () =>
        {
            var input = Inputs("empty-definition");
            using var form = new MainForm(["--definition", input.Definition], new DataLoadSettingsStore(input.Settings));
            File.AppendAllText(input.Definition, "追加,X(3)\r\n", new UTF8Encoding(false));
            Equal(true, Invoke(form, "ReloadDefinitionCore"));
            Equal(3, Get<List<FieldDefinition>>(form, "fields").Count);
            Equal(3, Get<List<List<string>>>(form, "records")[0].Count);
            Equal(false, Get<bool>(form, "hasUnsavedChanges"));
        });
        Test("Record changes mark unsaved and failed reload preserves that state while successful reload clears it", () =>
        {
            var input = Inputs("dirty-actions");
            using var form = Open(input);
            Invoke(form, "SelectRecord", 0);
            Invoke(form, "DuplicateRecord");
            Equal(true, Get<bool>(form, "hasUnsavedChanges"));
            Invoke(form, "ReloadCurrentFilesCore");
            Equal(false, Get<bool>(form, "hasUnsavedChanges"));
            Invoke(form, "AddRecord");
            Equal(true, Get<bool>(form, "hasUnsavedChanges"));
            Invoke(form, "WriteData", input.Data, Invoke(form, "CurrentSaveOptions"));
            Invoke(form, "DeleteRecord");
            Equal(true, Get<bool>(form, "hasUnsavedChanges"));
            File.WriteAllText(input.Data, "\"broken", new UTF8Encoding(false));
            Throws<TargetInvocationException>(() => Invoke(form, "ReloadCurrentFilesCore"));
            Equal(true, Get<bool>(form, "hasUnsavedChanges"));
        });
        Test("Unsaved confirmation supports cancel, discard and saving the current file", () =>
        {
            var input = Inputs("confirm");
            using var form = Open(input);
            EditCode(form, "000099");
            var proceed = true;
            WithDialog("未保存の変更", DialogResult.Cancel,
                () => proceed = (bool)Invoke(form, "ConfirmDiscardChanges", "終了")!,
                dialog =>
                {
                    Equal(DialogResult.Cancel, ((Button)dialog.AcceptButton!).DialogResult);
                    Equal(DialogResult.Cancel, ((Button)dialog.ActiveControl!).DialogResult);
                });
            Equal(false, proceed);
            Equal(true, Get<bool>(form, "hasUnsavedChanges"));
            WithDialog("未保存の変更", DialogResult.No, () => proceed = (bool)Invoke(form, "ConfirmDiscardChanges", "データを開く")!);
            Equal(true, proceed);
            Equal(true, Get<bool>(form, "hasUnsavedChanges"));
            WithDialog("未保存の変更", DialogResult.Yes, () => proceed = (bool)Invoke(form, "ConfirmDiscardChanges", "再読み込み")!);
            Equal(true, proceed);
            Equal(false, Get<bool>(form, "hasUnsavedChanges"));
            Equal(true, File.ReadAllText(input.Data).Contains("000099"));
        });
        Test("Closing an unsaved window can be cancelled or explicitly discarded", () =>
        {
            using var form = Open(Inputs("closing"));
            form.ShowInTaskbar = false;
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-30000, -30000);
            form.Show();
            EditCode(form, "000088");
            WithDialog("未保存の変更", DialogResult.Cancel, () => form.Close());
            Equal(false, form.IsDisposed);
            Equal(true, Get<bool>(form, "hasUnsavedChanges"));
            WithDialog("未保存の変更", DialogResult.No, () => form.Close());
            Equal(true, form.IsDisposed);
        });
        Test("Cancelling Save As from unsaved confirmation keeps the original edits", () =>
        {
            var input = Inputs("cancel-save-as");
            using var form = new MainForm(["--definition", input.Definition], new DataLoadSettingsStore(input.Settings));
            EditCode(form, "000077");
            var proceed = true;
            WithDialog("データ保存条件", DialogResult.Cancel, () =>
                WithDialog("未保存の変更", DialogResult.Yes, () => proceed = (bool)Invoke(form, "ConfirmDiscardChanges", "終了")!));
            Equal(false, proceed);
            Equal(true, Get<bool>(form, "hasUnsavedChanges"));
        });
        Test("Failed CSV save keeps unsaved marker and the destination bytes intact", () =>
        {
            var input = Inputs("failed-save");
            using var form = Open(input);
            EditCode(form, "000066");
            Get<List<List<string>>>(form, "records")[0][1] = "😀";
            var before = File.ReadAllBytes(input.Data);
            Throws<TargetInvocationException>(() => Invoke(form, "WriteData", input.Data,
                new DataSaveOptions(DataFileFormat.Csv, CsvTextEncoding.ShiftJis, RecordSeparatorMode.None, NationalTextEncoding.ShiftJis)));
            Equal(true, Get<bool>(form, "hasUnsavedChanges"));
            Equal(true, before.SequenceEqual(File.ReadAllBytes(input.Data)));
        });
        Test("Load settings persist by definition, data path and layout ID, and recover from corrupt JSON", () =>
        {
            var input = Inputs("settings-store");
            var settings = new DataLoadSettings(DataFileFormat.Csv, CsvTextEncoding.ShiftJis, RecordSeparatorMode.None, NationalTextEncoding.Utf32);
            var store = new DataLoadSettingsStore(input.Settings);
            Equal(true, store.Remember(input.Definition, input.Data, settings, "one"));
            var reopened = new DataLoadSettingsStore(input.Settings);
            Equal(settings, reopened.Find(input.Definition.ToUpperInvariant(), input.Data.ToUpperInvariant(), "one"));
            Equal(null, reopened.Find(input.Definition, input.Data, "two"));
            Equal(null, reopened.Find(input.Definition + ".other", input.Data, "one"));
            File.WriteAllText(input.Settings, "{broken");
            Equal(null, reopened.Find(input.Definition, input.Data, "one"));
            Equal(true, reopened.Remember(input.Definition, input.Data, settings));
            Equal(settings, new DataLoadSettingsStore(input.Settings).Find(input.Definition, input.Data));
        });
        Test("CSV format and Shift_JIS are restored after reopening the app even with a .dat extension", () =>
        {
            var input = Inputs("remember-csv");
            var data = Path.ChangeExtension(input.Data, ".dat");
            File.WriteAllText(data, "\"000001\",\"東京\"\r\n", Encoding.GetEncoding(932));
            var store = new DataLoadSettingsStore(input.Settings);
            using (var form = new MainForm(["--definition", input.Definition], store))
            {
                WithDialog("データ読込条件", DialogResult.OK, () => Invoke(form, "LoadDataFromPath", data, "読込完了"), dialog =>
                {
                    Descendants(dialog).OfType<ComboBox>().Single(combo => combo.Name == "DataFormat").SelectedIndex = 1;
                    Descendants(dialog).OfType<ComboBox>().Single(combo => combo.Name == "CsvEncoding").SelectedIndex = 1;
                    Equal(true, dialog.Controls.OfType<Button>().Single(button => button.Text == "OK").Enabled);
                });
                Equal("東京", Get<List<List<string>>>(form, "records")[0][1]);
            }
            using var next = new MainForm(["--definition", input.Definition], new DataLoadSettingsStore(input.Settings));
            WithDialog("データ読込条件", DialogResult.Cancel, () => Invoke(next, "LoadDataFromPath", data, "読込完了"), dialog =>
            {
                Equal(1, Descendants(dialog).OfType<ComboBox>().Single(combo => combo.Name == "DataFormat").SelectedIndex);
                Equal(1, Descendants(dialog).OfType<ComboBox>().Single(combo => combo.Name == "CsvEncoding").SelectedIndex);
                Descendants(dialog).OfType<ComboBox>().Single(combo => combo.Name == "CsvEncoding").SelectedIndex = 0;
            });
            Equal(CsvTextEncoding.ShiftJis, store.Find(input.Definition, data)!.CsvEncoding);
        });
        Test("Fixed-length separator and national encoding are restored in a fresh form", () =>
        {
            var input = Inputs("remember-fixed");
            File.WriteAllText(input.Definition, "項目名,定義\r\nコード,X(6)\r\n名称,N(2)\r\n", new UTF8Encoding(false));
            var data = Path.ChangeExtension(input.Data, ".dat");
            var fields = DefinitionCsvReader.Read(input.Definition).Select(field => field.Type == FieldDataType.FullWidthText
                ? field with { NationalByteWidth = 4 } : field).ToList();
            FixedLengthDataWriter.Write(data, fields, new List<List<string>> { new() { "000001", "東京" } },
                RecordSeparatorMode.None, DataEncodingProfile.ShiftJis, NationalTextEncoding.Utf32);
            var store = new DataLoadSettingsStore(input.Settings);
            using (var form = new MainForm(["--definition", input.Definition], store))
            {
                WithDialog("データ読込条件", DialogResult.OK, () => Invoke(form, "LoadDataFromPath", data, "読込完了"), dialog =>
                {
                    Descendants(dialog).OfType<RadioButton>().Single(button => button.Text == "改行なし").Checked = true;
                    Descendants(dialog).OfType<RadioButton>().Single(button => button.Text == "UTF-32LE").Checked = true;
                });
                Equal("東京", Get<List<List<string>>>(form, "records")[0][1]);
            }
            using var next = new MainForm(["--definition", input.Definition], new DataLoadSettingsStore(input.Settings));
            WithDialog("データ読込条件", DialogResult.Cancel, () => Invoke(next, "LoadDataFromPath", data, "読込完了"), dialog =>
            {
                Equal(true, Descendants(dialog).OfType<RadioButton>().Single(button => button.Text == "改行なし").Checked);
                Equal(true, Descendants(dialog).OfType<RadioButton>().Single(button => button.Text == "UTF-32LE").Checked);
            });
        });
        Test("Failure to write remembered conditions does not fail an otherwise successful data read", () =>
        {
            var input = Inputs("unwritable-settings");
            var store = new DataLoadSettingsStore(temporaryDirectory);
            using var form = new MainForm(["--definition", input.Definition], store);
            WithDialog("データ読込条件", DialogResult.OK, () => Invoke(form, "LoadDataFromPath", input.Data, "読込完了"));
            Equal("000001", Get<List<List<string>>>(form, "records")[0][0]);
            Equal(false, Get<bool>(form, "hasUnsavedChanges"));
            Equal(true, Get<ToolStripStatusLabel>(form, "statusLabel").Text!.Contains("記憶できませんでした"));
        });
    }
}
