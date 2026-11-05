namespace FixedDataBuilder;

public sealed partial class VisualComparisonForm
{
    private RecordListPane? activeReviewPane;
    private bool comparisonSetDirty;
    private bool restoringComparisonSet;
    private string? comparisonSetPath;
    private readonly Label comparisonStatus = new() { Name = "ComparisonSetStatus", AutoSize = true, Margin = new Padding(8, 8, 0, 0) };

    private void InitializeComparisonSetTools(FlowLayoutPanel toolbar)
    {
        var save = new Button { Name = "SaveComparisonSet", Text = "比較セットを保存...", AutoSize = true };
        var load = new Button { Name = "LoadComparisonSet", Text = "比較セットを開く...", AutoSize = true };
        var peer = new Button { Name = "FindPeerField", Text = "他方の同名項目を表示", AutoSize = true };
        save.Click += (_, _) => SaveComparisonSetDialog();
        load.Click += (_, _) => OpenComparisonSetDialog();
        peer.Click += (_, _) => ShowPeerField();
        toolbar.Controls.AddRange([save, load, peer, comparisonStatus]);
        inputPane.FieldSelected += (_, _) => activeReviewPane = inputPane;
        outputPane.FieldSelected += (_, _) => activeReviewPane = outputPane;
        inputPane.ReviewChanged += (_, _) => MarkComparisonSetChanged();
        outputPane.ReviewChanged += (_, _) => MarkComparisonSetChanged();
    }

    private void MarkComparisonSetChanged()
    {
        if (restoringComparisonSet) return;
        comparisonSetDirty = true;
        Text = "入力・出力を並べて表示（目視確認） 【比較セット未保存】";
    }

    private void ShowPeerField()
    {
        var source = activeReviewPane ?? outputPane;
        var target = ReferenceEquals(source, inputPane) ? outputPane : inputPane;
        var field = source.SelectedField;
        if (source.DataSet is not { } data || field < 0 || field >= data.Fields.Count)
        { comparisonStatus.Text = "まず項目のセルを選択してください。"; return; }
        comparisonStatus.Text = target.FindPeerField(data.Fields[field].Name)
            ? $"{target.Side}の同名項目を表示しました。表示する行は各側で選べます。"
            : "同名項目を一つに絞れません。各表の項目検索かNo.で選んでください。";
    }

    private ComparisonSetDocument CaptureComparisonSet() => new(1, inputOnTop,
        inputPane.CaptureSetSide(), outputPane.CaptureSetSide());

    private void RestoreComparisonSet(ComparisonSetDocument document)
    {
        ComparisonSetStore.Validate(document);
        // Decode and validate both snapshots before changing either visible side.
        var input = document.Input is null ? null : ComparisonDataSet.FromSnapshot(document.Input.Data);
        var output = document.Output is null ? null : ComparisonDataSet.FromSnapshot(document.Output.Data);
        var previous = CaptureComparisonSet();
        var previousInput = inputPane.DataSet; var previousOutput = outputPane.DataSet;
        restoringComparisonSet = true;
        try
        {
            inputPane.RestoreSetSide(document.Input, input);
            outputPane.RestoreSetSide(document.Output, output);
            if (inputOnTop != document.InputOnTop) SwapLists();
        }
        catch
        {
            inputPane.RestoreSetSide(previous.Input, previousInput);
            outputPane.RestoreSetSide(previous.Output, previousOutput);
            if (inputOnTop != previous.InputOnTop) SwapLists();
            throw;
        }
        finally { restoringComparisonSet = false; }
        comparisonSetDirty = false;
        Text = "入力・出力を並べて表示（目視確認）";
        comparisonStatus.Text = "保存時のデータ・確認済み印を復元しました。元ファイルは再読み込みしていません。";
    }

    private void SaveComparisonSetToPath(string path)
    {
        ComparisonSetStore.Write(path, CaptureComparisonSet());
        comparisonSetPath = path;
        comparisonSetDirty = false;
        Text = "入力・出力を並べて表示（目視確認）";
        comparisonStatus.Text = "読込データ・定義・確認済み印・表示位置を保存しました。";
    }

    private bool SaveComparisonSetDialog()
    {
        using var dialog = new SaveFileDialog
        {
            Title = "比較セットを保存（読込データ・定義・確認済み印を含みます）",
            Filter = "比較セット (*.fdbcompare.json)|*.fdbcompare.json|JSON (*.json)|*.json",
            FileName = comparisonSetPath is null ? "comparison-set.fdbcompare.json" : Path.GetFileName(comparisonSetPath),
            AddExtension = true, DefaultExt = "fdbcompare.json"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return false;
        try { SaveComparisonSetToPath(dialog.FileName); return true; }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "比較セット保存エラー", MessageBoxButtons.OK, MessageBoxIcon.Error); return false; }
    }

    private bool ConfirmComparisonReplacement()
    {
        if (!comparisonSetDirty) return true;
        var result = MessageBox.Show(this, "現在の比較セットを保存しますか？", "比較セットの変更が未保存です",
            MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button3);
        return result == DialogResult.No || result == DialogResult.Yes && SaveComparisonSetDialog();
    }

    private void OpenComparisonSetDialog()
    {
        using var dialog = new OpenFileDialog { Title = "比較セットを開く", Filter = "比較セット (*.fdbcompare.json;*.json)|*.fdbcompare.json;*.json" };
        if (dialog.ShowDialog(this) != DialogResult.OK || !ConfirmComparisonReplacement()) return;
        try
        {
            var document = ComparisonSetStore.Read(dialog.FileName);
            RestoreComparisonSet(document);
            comparisonSetPath = dialog.FileName;
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "比較セット読込エラー", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!e.Cancel && !ConfirmComparisonReplacement()) e.Cancel = true;
        base.OnFormClosing(e);
    }
}
