using System.Runtime.InteropServices;

namespace FixedDataBuilder;

public sealed record ExcelCaptureTarget(object Application, string Workbook, string Sheet, string Address, int WindowHandle);

public static class ExcelCapturePlacement
{
    private static readonly Guid ExcelClass = new("00024500-0000-0000-C000-000000000046");

    [DllImport("oleaut32.dll", PreserveSig = false)]
    private static extern void GetActiveObject(ref Guid clsid, IntPtr reserved,
        [MarshalAs(UnmanagedType.IUnknown)] out object activeObject);

    public static ExcelCaptureTarget CurrentTarget()
    {
        try
        {
            var clsid = ExcelClass;
            GetActiveObject(ref clsid, IntPtr.Zero, out var application);
            return ReadTarget(application);
        }
        catch (COMException ex)
        {
            throw new InvalidOperationException("開いているExcelを取得できません。デスクトップ版Excelでブックを開き、セルを選択してください。", ex);
        }
    }

    public static ExcelCaptureTarget ReadTarget(object application)
    {
        try
        {
            dynamic excel = application;
            dynamic selection = excel.Selection;
            // Access Cells to reject selected shapes and chart objects.
            object selectedCells = selection.Cells;
            dynamic cell = excel.ActiveCell;
            dynamic sheet = excel.ActiveSheet;
            dynamic workbook = excel.ActiveWorkbook;
            if (cell is null || sheet is null || workbook is null || selectedCells is null)
                throw new InvalidOperationException("Excelで貼り付け先のセルを選択してください。");
            string address = cell.Address[false, false];
            return new ExcelCaptureTarget(application, (string)workbook.Name, (string)sheet.Name, address, (int)excel.Hwnd);
        }
        catch (Exception ex) when (ex is COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException or NullReferenceException)
        {
            throw new InvalidOperationException("Excelでワークシートを開き、貼り付け先のセルを選択してください。", ex);
        }
    }

    public static int Insert(ExcelCaptureTarget target, IReadOnlyList<string> imagePaths, int columns)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(imagePaths);
        if (imagePaths.Count == 0) throw new ArgumentException("貼り付ける画像がありません。", nameof(imagePaths));
        if (columns < 1) throw new ArgumentOutOfRangeException(nameof(columns));
        foreach (var path in imagePaths)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("画像ファイルが見つかりません。", path);
            try
            {
                using var image = Image.FromFile(path);
                if (image.Width < 1 || image.Height < 1) throw new InvalidOperationException("画像の大きさが不正です。");
            }
            catch (Exception ex) when (ex is ArgumentException or OutOfMemoryException)
            {
                throw new InvalidOperationException($"画像ファイルを読み込めません: {Path.GetFileName(path)}", ex);
            }
        }

        var current = ReadTarget(target.Application);
        if (current != target)
            throw new InvalidOperationException("Excelの選択先が変わりました。「選択位置を取得」をもう一度押してください。");
        dynamic excel = target.Application;
        dynamic sheet = excel.ActiveSheet;
        if ((bool)sheet.ProtectDrawingObjects)
            throw new InvalidOperationException("シートが保護されているため、画像を貼り付けられません。");
        dynamic cell = excel.ActiveCell;
        dynamic shapes = sheet.Shapes;
        double startLeft = Convert.ToDouble(cell.Left);
        double left = startLeft;
        double top = Convert.ToDouble(cell.Top);
        double rowHeight = 0;
        const double gap = 8;
        var inserted = new List<object>();
        try
        {
            for (var index = 0; index < imagePaths.Count; index++)
            {
                if (index > 0 && index % columns == 0)
                {
                    left = startLeft;
                    top += rowHeight + gap;
                    rowHeight = 0;
                }
                // LinkToFile=false and SaveWithDocument=true embed the PNG in the workbook.
                dynamic shape = shapes.AddPicture(Path.GetFullPath(imagePaths[index]), 0, -1,
                    (float)left, (float)top, -1f, -1f);
                inserted.Add(shape);
                left += Convert.ToDouble(shape.Width) + gap;
                rowHeight = Math.Max(rowHeight, Convert.ToDouble(shape.Height));
            }
            return inserted.Count;
        }
        catch (Exception ex)
        {
            var remaining = 0;
            foreach (dynamic shape in inserted)
            {
                try { shape.Delete(); }
                catch (Exception) { remaining++; }
            }
            var rollback = remaining == 0 ? "追加済みの画像は取り消しました。"
                : $"追加済みの画像のうち{remaining}枚を取り消せませんでした。Excelを確認してください。";
            throw new InvalidOperationException("Excelへ画像を貼り付けられませんでした。" + rollback, ex);
        }
    }
}
