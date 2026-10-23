using System.Reflection;
using FixedDataBuilder;

internal static partial class Program
{
    private static void RunDragDropTests(string repo)
    {
        var definition = Path.Combine(repo, "samples", "basic-definition.csv");
        var data = Path.Combine(repo, "samples", "basic-data-sjis-crlf.dat");

        Test("File copy accepted on form, paths, grid, labels and HEX without loading during hover", () =>
        {
            using var form = new MainForm();
            foreach (var control in new[] { form }.Concat(Descendants(form)))
            {
                Equal(true, control.AllowDrop);
                var e = FileDrag(definition);
                RaiseDrag(control, "OnDragEnter", e);
                Equal(DragDropEffects.Copy, e.Effect);
                RaiseDrag(control, "OnDragOver", e);
                Equal(DragDropEffects.Copy, e.Effect);
            }
            Equal(0, Get<List<FieldDefinition>>(form, "fields").Count);
        });
        Test("Directories, missing files and ambiguous multiple drops rejected without changing data", () =>
        {
            using var form = CreateBasicForm(repo);
            var previous = Get<List<List<string>>>(form, "records").ToArray();
            var excel = Path.Combine(temporaryDirectory, "empty.xlsx");
            File.WriteAllText(excel, "");
            foreach (var paths in new[] { Array.Empty<string>(), new[] { repo },
                new[] { Path.Combine(temporaryDirectory, "missing.dat") }, new[] { data, data },
                new[] { definition, definition }, new[] { excel, data }, new[] { definition, data, data } })
            {
                var e = FileDrag(paths);
                RaiseDrag(form, "OnDragEnter", e);
                Equal(DragDropEffects.None, e.Effect);
                RaiseDrag(form, "OnDragDrop", e);
                Equal(DragDropEffects.None, e.Effect);
            }
            Equal(true, previous.SequenceEqual(Get<List<List<string>>>(form, "records")));
        });
        Test("Text and move-only drags rejected", () =>
        {
            using var form = new MainForm();
            var text = new DragEventArgs(new DataObject(DataFormats.Text, definition), 0, 0, 0,
                DragDropEffects.Copy, DragDropEffects.None);
            RaiseDrag(form, "OnDragEnter", text);
            Equal(DragDropEffects.None, text.Effect);
            var move = new DragEventArgs(FileDrag(definition).Data, 0, 0, 0,
                DragDropEffects.Move, DragDropEffects.None);
            RaiseDrag(form, "OnDragEnter", move);
            Equal(DragDropEffects.None, move.Effect);
            RaiseDrag(form, "OnDragDrop", move);
            Equal(0, Get<List<FieldDefinition>>(form, "fields").Count);
        });
        Test("CSV definition drop replaces definition, clears data path and updates history", () =>
        {
            using var form = CreateSampleForm(repo);
            Drop(Get<TextBox>(form, "definitionPathTextBox"), definition);
            Equal(definition, Get<TextBox>(form, "definitionPathTextBox").Text);
            Equal("", Get<TextBox>(form, "dataPathTextBox").Text);
            Equal(1, Get<List<List<string>>>(form, "records").Count);
            Equal(null, Get<string?>(form, "saveDataPath"));
            Equal(definition, Get<List<string>>(form, "recentDefinitionFiles")[0]);
            Equal(DataEncodingProfile.ShiftJis, Get<DataEncodingProfile>(form, "currentDataEncodingProfile"));
        });
        Test("Copybook extensions recognized case-insensitively", () =>
        {
            foreach (var extension in new[] { ".CBL", ".CPY", ".COB" })
            {
                var path = Path.Combine(temporaryDirectory, "definition" + extension);
                File.Copy(Path.Combine(repo, "samples", "copybook-basic-definition.cbl"), path, overwrite: true);
                using var form = new MainForm();
                Drop(Get<DataGridView>(form, "grid"), path);
                Equal(DataEncodingProfile.Utf8WithNationalText, Get<DataEncodingProfile>(form, "currentDataEncodingProfile"));
                Equal(path, Get<TextBox>(form, "definitionPathTextBox").Text);
            }
        });
        Test("Single-layout XML definition uses existing importer", () =>
        {
            var path = Path.Combine(temporaryDirectory, "drop-definition.XML");
            File.WriteAllText(path, "<FileDefinitions><File id='drop' type='SEQ' charSet='UTF-8'><Record>" +
                "<Column index='1' name='コード' type='X' length='3'/></Record></File></FileDefinitions>");
            using var form = new MainForm();
            Drop(form, path);
            Equal(1, Get<List<FieldDefinition>>(form, "fields").Count);
            Equal(DataEncodingProfile.Utf8WithNationalText, Get<DataEncodingProfile>(form, "currentDataEncodingProfile"));
            Equal(true, Get<TextBox>(form, "xmlLayoutTextBox").Text.Contains("drop"));
        });
        Test("Data drop shows read options and loads using current definition", () =>
        {
            using var form = new MainForm(["--definition", definition]);
            WithDialog("データ読込条件", DialogResult.OK, () => Drop(Get<DataGridView>(form, "grid"), data));
            Equal(3, Get<List<List<string>>>(form, "records").Count);
            Equal(data, Get<TextBox>(form, "dataPathTextBox").Text);
            Equal(data, Get<string>(form, "saveDataPath"));
            Equal(data, Get<List<string>>(form, "recentDataFiles")[0]);
        });
        Test("CSV data and arbitrary extensions supported on explicit path targets", () =>
        {
            var csvData = Path.Combine(temporaryDirectory, "records.csv");
            var customDefinition = Path.Combine(temporaryDirectory, "layout.def");
            File.Copy(data, csvData, overwrite: true);
            File.Copy(definition, customDefinition, overwrite: true);
            using var form = new MainForm();
            Drop(Get<TextBox>(form, "definitionPathTextBox"), customDefinition);
            WithDialog("データ読込条件", DialogResult.OK, () => Drop(Get<TextBox>(form, "dataPathTextBox"), csvData),
                dialog => Descendants(dialog).OfType<ComboBox>().Single(control => control.Name == "DataFormat").SelectedIndex = 0);
            Equal(3, Get<List<List<string>>>(form, "records").Count);
            Equal(customDefinition, Get<TextBox>(form, "definitionPathTextBox").Text);
            Equal(csvData, Get<TextBox>(form, "dataPathTextBox").Text);
        });
        Test("Definition and data pair loads in either file order", () =>
        {
            foreach (var paths in new[] { new[] { definition, data }, new[] { data, definition } })
            {
                using var form = new MainForm();
                WithDialog("データ読込条件", DialogResult.OK, () => Drop(form, paths));
                Equal(definition, Get<TextBox>(form, "definitionPathTextBox").Text);
                Equal(data, Get<TextBox>(form, "dataPathTextBox").Text);
                Equal(3, Get<List<List<string>>>(form, "records").Count);
            }
        });
        Test("Copybook pair retains sample national-encoding defaults", () =>
        {
            using var form = new MainForm();
            WithDialog("データ読込条件", DialogResult.OK, () => Drop(form,
                Path.Combine(repo, "samples", "copybook-group-occurs-data-crlf-utf8-n-utf16le.dat"),
                Path.Combine(repo, "samples", "copybook-group-occurs-definition.cbl")));
            Equal(NationalTextEncoding.Utf16, Get<NationalTextEncoding>(form, "currentNationalTextEncoding"));
            Equal(true, Get<List<List<string>>>(form, "records").Count > 0);
        });
        Test("Cancel data options keeps previous records, encoding and file paths", () =>
        {
            using var form = CreateBasicForm(repo);
            var previous = Get<List<List<string>>>(form, "records").ToArray();
            WithDialog("データ読込条件", DialogResult.Cancel, () => Drop(form, data));
            Equal(true, previous.SequenceEqual(Get<List<List<string>>>(form, "records")));
            Equal(data, Get<string>(form, "saveDataPath"));
            Equal(DataEncodingProfile.ShiftJis, Get<DataEncodingProfile>(form, "currentDataEncodingProfile"));
        });
        Test("Cancel layout selection stops paired data load and keeps existing state", () =>
        {
            using var form = CreateBasicForm(repo);
            var previous = Get<List<List<string>>>(form, "records").ToArray();
            WithDialog("汎用XML定義 — レイアウト選択", DialogResult.Cancel, () => Drop(form,
                Path.Combine(repo, "samples", "generic-xml-definition.xml"),
                Path.Combine(repo, "samples", "generic-xml-customer-data-utf8-none.dat")));
            Equal(true, previous.SequenceEqual(Get<List<List<string>>>(form, "records")));
            Equal(definition, Get<TextBox>(form, "definitionPathTextBox").Text);
            Equal(data, Get<TextBox>(form, "dataPathTextBox").Text);
        });
        Test("Failed data read keeps previous records, national widths and save target", () =>
        {
            using var form = CreateBasicForm(repo);
            var previousRecords = Get<List<List<string>>>(form, "records").ToArray();
            var previousFields = Get<List<FieldDefinition>>(form, "fields").ToArray();
            var bad = Path.Combine(temporaryDirectory, "bad.dat");
            File.WriteAllBytes(bad, [0x41]);
            Throws<TargetInvocationException>(() => Invoke(form, "LoadData", bad, RecordSeparatorMode.None, NationalTextEncoding.Utf32));
            Equal(true, previousRecords.SequenceEqual(Get<List<List<string>>>(form, "records")));
            Equal(true, previousFields.SequenceEqual(Get<List<FieldDefinition>>(form, "fields")));
            Equal(NationalTextEncoding.ShiftJis, Get<NationalTextEncoding>(form, "currentNationalTextEncoding"));
            Equal(data, Get<string>(form, "saveDataPath"));
        });
        Test("Excel drops import both layouts including uppercase extension", () =>
        {
            using var source = CreateBasicForm(repo);
            foreach (var layoutName in new[] { "項目縦", "レコード縦" })
            {
                if (layoutName == "レコード縦") Get<ToolStripMenuItem>(source, "recordRowsButton").PerformClick();
                var path = Path.Combine(temporaryDirectory, layoutName + ".XLSX");
                var rows = (IReadOnlyList<IReadOnlyList<ExcelCell>>)Invoke(source, "BuildExcelRows")!;
                ExcelExporter.Write(path, layoutName, rows);
                if (layoutName == "レコード縦")
                {
                    Equal("行", rows[0][0].Text);
                    Equal("1", rows[1][0].Text);
                    var legacyRows = rows.Select(row => row.ToArray()).ToArray();
                    legacyRows[0][0] = new ExcelCell("レコード", ExcelCellKind.Header);
                    for (var index = 1; index < legacyRows.Length; index++)
                        legacyRows[index][0] = new ExcelCell($"Rec {index}", ExcelCellKind.Definition);
                    var legacyPath = Path.Combine(temporaryDirectory, "legacy-record-rows.xlsx");
                    ExcelExporter.Write(legacyPath, layoutName, legacyRows);
                    var legacy = ExcelImporter.Read(legacyPath);
                    Equal(3, legacy.Records.Count);
                    Equal(true, legacy.Records[0].SequenceEqual(Get<List<List<string>>>(source, "records")[0]));
                }
                using var target = new MainForm();
                Drop(Get<TextBox>(target, "dataPathTextBox"), path);
                Equal(3, Get<List<List<string>>>(target, "records").Count);
                Equal(path, Get<TextBox>(target, "dataPathTextBox").Text);
                Equal(null, Get<string?>(target, "saveDataPath"));
            }
        });
        Test("New child controls receive drop handlers without duplicate registration", () =>
        {
            using var form = new MainForm();
            using var child = new TextBox();
            form.Controls.Add(child);
            Invoke(form, "EnableFileDrop", child);
            Equal(true, child.AllowDrop);
            Drop(child, definition);
            WithDialog("データ読込条件", DialogResult.OK, () => Drop(child, data));
            Equal(3, Get<List<List<string>>>(form, "records").Count);
        });
    }

    private static MainForm CreateBasicForm(string repo) => new([
        "--definition", Path.Combine(repo, "samples", "basic-definition.csv"),
        "--data", Path.Combine(repo, "samples", "basic-data-sjis-crlf.dat")]);

    private static DragEventArgs FileDrag(params string[] paths) => new(
        new DataObject(DataFormats.FileDrop, paths), 0, 0, 0, DragDropEffects.Copy, DragDropEffects.None);

    private static void Drop(Control target, params string[] paths)
    {
        var e = FileDrag(paths);
        RaiseDrag(target, "OnDragDrop", e);
        Equal(DragDropEffects.Copy, e.Effect);
    }

    private static void RaiseDrag(Control control, string method, DragEventArgs e) =>
        typeof(Control).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(control, [e]);

    private static void WithDialog(string title, DialogResult result, Action action, Action<Form>? inspect = null)
    {
        var shown = 0;
        using var timer = new System.Windows.Forms.Timer { Interval = 20 };
        timer.Tick += (_, _) =>
        {
            var dialog = Application.OpenForms.Cast<Form>().FirstOrDefault(form => form.Text == title);
            if (dialog is null) return;
            timer.Stop();
            shown++;
            inspect?.Invoke(dialog);
            dialog.DialogResult = result;
        };
        timer.Start();
        action();
        timer.Stop();
        Equal(1, shown);
    }
}
