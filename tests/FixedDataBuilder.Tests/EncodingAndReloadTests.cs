using System.Reflection;
using System.Text;
using FixedDataBuilder;

internal static partial class Program
{
    private static void RunEncodingAndReloadTests()
    {
        Test("CSV load dialog selects Shift_JIS or UTF-8, previews Japanese and preserves encoding when saving", () =>
        {
            foreach (var profile in new[] { DataEncodingProfile.ShiftJis, DataEncodingProfile.Utf8WithNationalText })
            {
                var fixture = CreateEncodingFixture(profile);
                using var form = OpenEncodingFixture(fixture, profile);
                Equal("東京", Get<List<List<string>>>(form, "records")[0][0]);
                Equal(profile, Get<DataEncodingProfile>(form, "currentDataEncodingProfile"));
                Equal(true, Get<TextBox>(form, "separatorModeTextBox").Text.Contains(profile == DataEncodingProfile.ShiftJis ? "Shift_JIS" : "UTF-8"));
                var bytes = File.ReadAllBytes(fixture.DataPath);
                Invoke(form, "SaveDataOverwrite");
                Equal(true, bytes.SequenceEqual(File.ReadAllBytes(fixture.DataPath)));
            }
        });
        Test("Cancelling encoding selection preserves loaded records, path and encoding", () =>
        {
            var fixture = CreateEncodingFixture(DataEncodingProfile.ShiftJis);
            using var form = OpenEncodingFixture(fixture, DataEncodingProfile.ShiftJis);
            var previous = Get<List<List<string>>>(form, "records").ToArray();
            SelectEncodingInDialog(DataEncodingProfile.Utf8WithNationalText, NationalTextEncoding.ShiftJis,
                DialogResult.Cancel, () => Invoke(form, "LoadDataFromPath", fixture.DataPath, "read"));
            Equal(DataEncodingProfile.ShiftJis, Get<DataEncodingProfile>(form, "currentDataEncodingProfile"));
            Equal(true, previous.SequenceEqual(Get<List<List<string>>>(form, "records")));
            Equal(fixture.DataPath, Get<string>(form, "saveDataPath"));
            Equal(false, Invoke(form, "HasUnsavedDataChanges"));
        });
        Test("UTF-8 input with or without BOM supports CRLF and no-separator records", () =>
        {
            foreach (var separator in new[] { RecordSeparatorMode.CrLfOrLf, RecordSeparatorMode.None })
            foreach (var bom in new[] { false, true })
            {
                var fixture = CreateEncodingFixture(DataEncodingProfile.Utf8WithNationalText, separator: separator);
                if (bom) File.WriteAllBytes(fixture.DataPath, Encoding.UTF8.GetPreamble().Concat(File.ReadAllBytes(fixture.DataPath)).ToArray());
                var records = FixedLengthDataReader.Read(fixture.DataPath, fixture.Fields, separator,
                    DataEncodingProfile.Utf8WithNationalText, NationalTextEncoding.ShiftJis);
                Equal("東京", records[0][0]);
                Equal("01", records[0][1]);
            }
        });
        Test("UTF-8 X fields validate byte limits and display the remaining byte count as spaces", () =>
        {
            using var form = new MainForm();
            form.GetType().GetField("currentDataEncodingProfile", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(form, DataEncodingProfile.Utf8WithNationalText);
            var field = new FieldDefinition("名称", FieldDataType.HalfWidthText, 5);
            Equal(1, RecordValidator.Validate([field], [new() { "東京" }], DataEncodingProfile.Utf8WithNationalText).Count);
            Equal(true, Invoke(form, "GetValidationError", field, "東京") is string);
            Equal("東京･･", Invoke(form, "GetVisibleTextValue", field with { Length = 8 }, "東京"));
        });
        Test("Invalid UTF-8 leaves current encoding, records and save target intact", () =>
        {
            var fixture = CreateEncodingFixture(DataEncodingProfile.ShiftJis);
            using var form = OpenEncodingFixture(fixture, DataEncodingProfile.ShiftJis);
            var previous = Get<List<List<string>>>(form, "records").ToArray();
            var badPath = Path.Combine(temporaryDirectory, "invalid-utf8.dat");
            File.WriteAllBytes(badPath, new byte[] { 0xff }.Concat(Encoding.ASCII.GetBytes("           01\r\n")).ToArray());
            Throws<TargetInvocationException>(() => Invoke(form, "LoadDataWithEncoding", badPath,
                RecordSeparatorMode.CrLfOrLf, NationalTextEncoding.ShiftJis, DataEncodingProfile.Utf8WithNationalText));
            Equal(DataEncodingProfile.ShiftJis, Get<DataEncodingProfile>(form, "currentDataEncodingProfile"));
            Equal(true, previous.SequenceEqual(Get<List<List<string>>>(form, "records")));
            Equal(fixture.DataPath, Get<string>(form, "saveDataPath"));
        });
        Test("Matching Excel import preserves UTF-8 and independent national encoding", () =>
        {
            var fixture = CreateEncodingFixture(DataEncodingProfile.Utf8WithNationalText, NationalTextEncoding.Utf32);
            using var form = OpenEncodingFixture(fixture, DataEncodingProfile.Utf8WithNationalText, NationalTextEncoding.Utf32);
            var excel = Path.Combine(temporaryDirectory, "utf8-national.xlsx");
            ExcelExporter.Write(excel, "項目縦", (IReadOnlyList<IReadOnlyList<ExcelCell>>)Invoke(form, "BuildExcelRows")!);
            Invoke(form, "LoadExcel", excel);
            Equal(DataEncodingProfile.Utf8WithNationalText, Get<DataEncodingProfile>(form, "currentDataEncodingProfile"));
            Equal(NationalTextEncoding.Utf32, Get<NationalTextEncoding>(form, "currentNationalTextEncoding"));
            Equal(4, Get<List<FieldDefinition>>(form, "fields")[2].NationalByteWidth);
            var saved = Path.Combine(temporaryDirectory, "utf8-national-roundtrip.dat");
            FixedLengthDataWriter.Write(saved, Get<List<FieldDefinition>>(form, "fields"), Get<List<List<string>>>(form, "records"),
                RecordSeparatorMode.CrLfOrLf, DataEncodingProfile.Utf8WithNationalText, NationalTextEncoding.Utf32);
            Equal(true, File.ReadAllBytes(fixture.DataPath).SequenceEqual(File.ReadAllBytes(saved)));
        });
        Test("F5 reload takes external changes using current encoding, separator and view", () =>
        {
            using var empty = new MainForm();
            Equal(false, Get<ToolStripMenuItem>(empty, "reloadDataMenuItem").Enabled);
            var fixture = CreateEncodingFixture(DataEncodingProfile.Utf8WithNationalText, separator: RecordSeparatorMode.None);
            using var form = OpenEncodingFixture(fixture, DataEncodingProfile.Utf8WithNationalText, separator: RecordSeparatorMode.None);
            var menu = Get<ToolStripMenuItem>(form, "reloadDataMenuItem");
            Equal(Keys.F5, menu.ShortcutKeys);
            Equal(true, menu.Enabled);
            Get<ToolStripMenuItem>(form, "recordRowsButton").PerformClick();
            Get<HashSet<int>>(form, "hiddenFieldIndexes").Add(1);
            Invoke(form, "RefreshGrid");
            FixedLengthDataWriter.Write(fixture.DataPath, fixture.Fields, [new() { "大阪", "02" }],
                RecordSeparatorMode.None, DataEncodingProfile.Utf8WithNationalText, NationalTextEncoding.ShiftJis);
            menu.PerformClick();
            Equal("大阪", Get<List<List<string>>>(form, "records")[0][0]);
            Equal(RecordSeparatorMode.None, Get<RecordSeparatorMode>(form, "currentSeparatorMode"));
            Equal(DataEncodingProfile.Utf8WithNationalText, Get<DataEncodingProfile>(form, "currentDataEncodingProfile"));
            Equal(true, Get<ToolStripMenuItem>(form, "recordRowsButton").Checked);
            Equal(true, Get<HashSet<int>>(form, "hiddenFieldIndexes").Contains(1));
            Equal(false, Invoke(form, "HasUnsavedDataChanges"));
        });
        Test("Failed reload preserves existing records and conditions", () =>
        {
            var fixture = CreateEncodingFixture(DataEncodingProfile.Utf8WithNationalText);
            using var form = OpenEncodingFixture(fixture, DataEncodingProfile.Utf8WithNationalText);
            var previous = Get<List<List<string>>>(form, "records").ToArray();
            File.WriteAllBytes(fixture.DataPath, [0xff]);
            Throws<TargetInvocationException>(() => Invoke(form, "ReloadDataFromDisk"));
            Equal(true, previous.SequenceEqual(Get<List<List<string>>>(form, "records")));
            Equal(DataEncodingProfile.Utf8WithNationalText, Get<DataEncodingProfile>(form, "currentDataEncodingProfile"));
            Equal(false, Invoke(form, "HasUnsavedDataChanges"));
        });
        Test("Unsaved-change detection tracks edited and added records and resets after saving", () =>
        {
            var fixture = CreateEncodingFixture(DataEncodingProfile.Utf8WithNationalText);
            using var form = OpenEncodingFixture(fixture, DataEncodingProfile.Utf8WithNationalText);
            var records = Get<List<List<string>>>(form, "records");
            records[0][0] = "大阪";
            Equal(true, Invoke(form, "HasUnsavedDataChanges"));
            records[0][0] = "東京";
            Equal(false, Invoke(form, "HasUnsavedDataChanges"));
            Invoke(form, "AddRecord");
            Equal(true, Invoke(form, "HasUnsavedDataChanges"));
            records.RemoveAt(records.Count - 1);
            records[0][0] = "大阪";
            Invoke(form, "SaveDataOverwrite");
            Equal(false, Invoke(form, "HasUnsavedDataChanges"));
            Equal("大阪", FixedLengthDataReader.Read(fixture.DataPath, fixture.Fields, RecordSeparatorMode.CrLfOrLf,
                DataEncodingProfile.Utf8WithNationalText, NationalTextEncoding.ShiftJis)[0][0]);
        });
        Test("Excel reload uses its source workbook and preserves current layout and visibility", () =>
        {
            var fixture = CreateEncodingFixture(DataEncodingProfile.Utf8WithNationalText);
            using var form = OpenEncodingFixture(fixture, DataEncodingProfile.Utf8WithNationalText);
            var excel = Path.Combine(temporaryDirectory, "reload-source.xlsx");
            var rows = ((IReadOnlyList<IReadOnlyList<ExcelCell>>)Invoke(form, "BuildExcelRows")!)
                .Select(row => row.ToArray()).ToArray();
            ExcelExporter.Write(excel, "項目縦", rows);
            Invoke(form, "LoadExcel", excel);
            Get<ToolStripMenuItem>(form, "recordRowsButton").PerformClick();
            Get<HashSet<int>>(form, "hiddenFieldIndexes").Add(1);
            Invoke(form, "RefreshGrid");
            rows[1][2] = new ExcelCell("大阪");
            ExcelExporter.Write(excel, "項目縦", rows);
            Get<ToolStripMenuItem>(form, "reloadDataMenuItem").PerformClick();
            Equal("大阪", Get<List<List<string>>>(form, "records")[0][0]);
            Equal(true, Get<ToolStripMenuItem>(form, "recordRowsButton").Checked);
            Equal(true, Get<HashSet<int>>(form, "hiddenFieldIndexes").Contains(1));
            Equal(excel, Get<TextBox>(form, "dataPathTextBox").Text);
        });
    }

    private sealed record EncodingFixture(string DefinitionPath, string DataPath, IReadOnlyList<FieldDefinition> Fields);

    private static EncodingFixture CreateEncodingFixture(DataEncodingProfile profile,
        NationalTextEncoding national = NationalTextEncoding.ShiftJis, RecordSeparatorMode separator = RecordSeparatorMode.CrLfOrLf)
    {
        var directory = Path.Combine(temporaryDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var definition = Path.Combine(directory, "definition.csv");
        File.WriteAllText(definition, "項目名,定義\r\n名称,X(12)\r\n番号,9(2)\r\n" +
            (national != NationalTextEncoding.ShiftJis ? "備考,N(3)\r\n" : ""), new UTF8Encoding(false));
        var fields = DefinitionCsvReader.Read(definition).Select(field => field.Type == FieldDataType.FullWidthText
            ? field with { NationalByteWidth = NationalTextEncodingHelper.FixedByteWidth(national) } : field).ToList();
        var data = Path.Combine(directory, "data.dat");
        List<string> record = ["東京", "01"];
        if (fields.Count > 2) record.Add("山川");
        var records = new List<List<string>> { record, record.ToList(), record.ToList() };
        records[1][0] = "大阪";
        records[1][1] = "02";
        records[2][0] = "名古屋";
        records[2][1] = "03";
        FixedLengthDataWriter.Write(data, fields, records, separator, profile, national);
        return new EncodingFixture(definition, data, fields);
    }

    private static MainForm OpenEncodingFixture(EncodingFixture fixture, DataEncodingProfile profile,
        NationalTextEncoding national = NationalTextEncoding.ShiftJis, RecordSeparatorMode separator = RecordSeparatorMode.CrLfOrLf)
    {
        var form = new MainForm(["--definition", fixture.DefinitionPath]);
        SelectEncodingInDialog(profile, national, DialogResult.OK,
            () => Invoke(form, "LoadDataFromPath", fixture.DataPath, "read"), dialog =>
            {
                if (separator == RecordSeparatorMode.None)
                    Descendants(dialog).OfType<RadioButton>().Single(button => button.Text == "改行なし").Checked = true;
                var preview = Descendants(dialog).OfType<DataGridView>().Single();
                Equal("東京", preview[1, 0].Value);
            });
        return form;
    }

    private static void SelectEncodingInDialog(DataEncodingProfile profile, NationalTextEncoding national,
        DialogResult result, Action action, Action<Form>? inspect = null)
    {
        var shown = false;
        Exception? failure = null;
        using var timer = new System.Windows.Forms.Timer { Interval = 20 };
        timer.Tick += (_, _) =>
        {
            var dialog = Application.OpenForms.Cast<Form>().FirstOrDefault(form => form.Text == "データ読込条件");
            if (dialog is null) return;
            timer.Stop();
            shown = true;
            try
            {
                var dataGroup = Descendants(dialog).OfType<GroupBox>().Single(group => group.Text.StartsWith("2."));
                dataGroup.Controls.OfType<RadioButton>().Single(button => button.Text ==
                    (profile == DataEncodingProfile.ShiftJis ? "Shift_JIS" : "UTF-8")).Checked = true;
                Descendants(dialog).OfType<RadioButton>().Single(button => button.Tag is NationalTextEncoding encoding && encoding == national).Checked = true;
                inspect?.Invoke(dialog);
                dialog.DialogResult = result;
            }
            catch (Exception ex) { failure = ex; dialog.DialogResult = DialogResult.Cancel; }
        };
        timer.Start();
        action();
        if (failure is not null) throw failure;
        Equal(true, shown);
    }
}
