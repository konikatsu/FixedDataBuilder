using System.Reflection;
using FixedDataBuilder;

internal static partial class Program
{
    private static void RunDirectComparisonTests(string repo)
    {
        Form Startup(params string[] args) => (Form)typeof(MainForm).Assembly.GetType("FixedDataBuilder.Program")!
            .GetMethod("CreateStartupForm", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [args])!;

        Test("Comparison is available on a newly opened main window before files are loaded", () =>
        {
            using var main = new MainForm();
            Equal(true, Get<Button>(main, "comparisonButton").Enabled);
            Equal(true, Get<ToolStripMenuItem>(main, "comparisonMenu").Enabled);
            using var form = (VisualComparisonForm)Invoke(main, "CreateVisualComparisonForm")!;
            Equal(0, VisualGrid(VisualPane(form, true)).RowCount);
            Equal(0, VisualGrid(VisualPane(form, false)).RowCount);
            Equal(true, Descendants(form).OfType<Button>().Single(button => button.Name == "OpenInputRecords").Enabled);
            Equal(true, Descendants(form).OfType<Button>().Single(button => button.Name == "OpenOutputRecords").Enabled);
        });
        Test("The compare command line opens the comparison form directly with empty independent panes", () =>
        {
            using var startup = Startup("--compare");
            Equal(true, startup is VisualComparisonForm);
            var form = (VisualComparisonForm)startup;
            Equal(0, VisualGrid(VisualPane(form, true)).RowCount);
            Equal(0, VisualGrid(VisualPane(form, false)).RowCount);
        });
        Test("Ordinary startup still opens the main form and comparison flag matching ignores case", () =>
        {
            using var normal = Startup(); using var compare = Startup("--COMPARE");
            Equal(true, normal is MainForm); Equal(true, compare is VisualComparisonForm);
        });
        Test("Direct comparison startup accepts existing definition and data command-line arguments as output", () =>
        {
            using var startup = Startup("--compare", "--definition", Path.Combine(repo, "samples", "comment-definition.csv"),
                "--data", Path.Combine(repo, "samples", "comment-data-utf8.csv"), "--csv-header", "present", "--csv-encoding", "utf8");
            var form = (VisualComparisonForm)startup; var output = VisualPane(form, false);
            Equal("0001", VisualGrid(output)[1, Get<int>(output, "metadataRows")].Value);
            Equal(0, VisualGrid(VisualPane(form, true)).RowCount);
        });
        Test("A saved comparison set can restore data and human review marks into a directly opened empty form", () =>
        {
            using var source = new VisualComparisonForm(VisualData("SAVED", 2, 2));
            var pane = VisualPane(source, false);
            ReviewInvoke(pane, "SelectReviewCell", 1, 1); ReviewInvoke(pane, "MarkSelected", true);
            var set = (ComparisonSetDocument)Invoke(source, "CaptureComparisonSet")!;
            using var target = new VisualComparisonForm();
            Invoke(target, "RestoreComparisonSet", set);
            var output = VisualPane(target, false);
            Equal(true, ReviewInvoke(output, "IsReviewed", 1, 1));
            Equal("SAVED_1_1", VisualGrid(output)[1, Get<int>(output, "metadataRows")].Value);
            Equal(0, VisualGrid(VisualPane(target, true)).RowCount);
        });
        Test("An input-only comparison set can be saved and reopened from an empty comparison window", () =>
        {
            using var source = new VisualComparisonForm();
            Invoke(source, "SetInputDataSet", VisualData("INPUT", 3, 2));
            var path = Path.Combine(temporaryDirectory, "input-only.fdbcompare.json");
            Invoke(source, "SaveComparisonSetToPath", path);
            var saved = ComparisonSetStore.Read(path);
            Equal(null, saved.Output); Equal(true, saved.Input is not null);
            using var target = new VisualComparisonForm();
            Invoke(target, "RestoreComparisonSet", saved);
            Equal(0, VisualGrid(VisualPane(target, false)).RowCount);
            var input = VisualPane(target, true);
            Equal("INPUT_1_1", VisualGrid(input)[1, Get<int>(input, "metadataRows")].Value);
        });
        Test("Saving a completely empty comparison set reports the missing data and protects existing files", () =>
        {
            using var form = new VisualComparisonForm();
            var path = Path.Combine(temporaryDirectory, "empty-comparison.fdbcompare.json");
            File.WriteAllText(path, "existing");
            var bytes = File.ReadAllBytes(path);
            Throws<TargetInvocationException>(() => Invoke(form, "SaveComparisonSetToPath", path));
            Equal(true, bytes.SequenceEqual(File.ReadAllBytes(path)));
            Equal(0, VisualGrid(VisualPane(form, true)).RowCount);
            Equal(0, VisualGrid(VisualPane(form, false)).RowCount);
        });
        Test("An invalid saved set leaves a directly opened comparison window empty and usable", () =>
        {
            using var form = new VisualComparisonForm();
            Throws<TargetInvocationException>(() => Invoke(form, "RestoreComparisonSet", new ComparisonSetDocument(999, true, null, null)));
            Equal(0, VisualGrid(VisualPane(form, true)).RowCount);
            Equal(0, VisualGrid(VisualPane(form, false)).RowCount);
        });
    }
}
