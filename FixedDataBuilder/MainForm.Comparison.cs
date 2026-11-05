namespace FixedDataBuilder;

public sealed partial class MainForm
{
    private readonly Button comparisonButton = new() { Name = "OpenVisualComparison", Text = "入力・出力を並べて表示...", AutoSize = true };
    private readonly ToolStripMenuItem comparisonMenu = new("入力・出力を並べて表示...");

    private ComparisonDataSet CreateComparisonOutputSnapshot() => ComparisonDataSet.FromRecords(fields,
        records, new ComparisonDataProfile(currentDataFileFormat, currentCsvEncoding, currentCsvHasHeader,
            currentSeparatorMode, currentDataEncodingProfile, currentNationalTextEncoding),
        GetCurrentDefinitionPath() ?? definitionSaveSuggestionPath, GetCurrentDataPath(), definitionEncoding: appliedDefinitionTextEncoding);

    internal VisualComparisonForm CreateVisualComparisonForm() => new(fields.Count > 0 && records.Count > 0
        ? CreateComparisonOutputSnapshot() : null, displayFont,
        displayWrapOptions.HeaderMinimumCharacters);

    private void OpenComparison()
    {
        if (!grid.EndEdit()) return;
        try
        {
            using var form = CreateVisualComparisonForm();
            form.ShowDialog(this);
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "入力と出力の比較", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    private void UpdateComparisonControls() => comparisonButton.Enabled = comparisonMenu.Enabled = true;
}
