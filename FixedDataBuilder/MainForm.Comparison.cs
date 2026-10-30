namespace FixedDataBuilder;

public sealed partial class MainForm
{
    private readonly Button comparisonButton = new() { Text = "入力・出力を並べて表示...", AutoSize = true, Enabled = false };
    private readonly ToolStripMenuItem comparisonMenu = new("入力・出力を並べて表示...") { Enabled = false };

    private ComparisonDataSet CreateComparisonOutputSnapshot() => ComparisonDataSet.FromRecords(fields,
        records, new ComparisonDataProfile(currentDataFileFormat, currentCsvEncoding, currentCsvHasHeader,
            currentSeparatorMode, currentDataEncodingProfile, currentNationalTextEncoding),
        GetCurrentDefinitionPath(), GetCurrentDataPath(), definitionEncoding: appliedDefinitionTextEncoding);

    private VisualComparisonForm CreateVisualComparisonForm() => new(CreateComparisonOutputSnapshot(), displayFont);

    private void OpenComparison()
    {
        if (!grid.EndEdit() || records.Count == 0 || fields.Count == 0) return;
        try
        {
            using var form = CreateVisualComparisonForm();
            form.ShowDialog(this);
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "入力と出力の比較", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    private void UpdateComparisonControls() => comparisonButton.Enabled = comparisonMenu.Enabled = records.Count > 0 && fields.Count > 0;
}
