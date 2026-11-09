using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using FixedDataBuilder;

internal static partial class Program
{
    private static void RunExcelCaptureTests()
    {
        Test("Excel embeds selected images in rows from the selected cell and preserves the workbook", () =>
        {
            var otherExcelInstances = System.Diagnostics.Process.GetProcessesByName("EXCEL");
            var hadExistingExcel = otherExcelInstances.Length > 0;
            foreach (var process in otherExcelInstances) process.Dispose();
            var excelType = Type.GetTypeFromProgID("Excel.Application")
                ?? throw new InvalidOperationException("Desktop Excel is not registered.");
            dynamic excel = Activator.CreateInstance(excelType)!;
            dynamic? book = null;
            try
            {
                excel.Visible = false;
                excel.DisplayAlerts = false;
                book = excel.Workbooks.Add();
                dynamic sheet = book.Worksheets[1];
                sheet.Activate();
                sheet.Range["C5"].Select();
                var target = ExcelCapturePlacement.ReadTarget((object)excel);
                Equal("C5", target.Address);
                if (!hadExistingExcel) Equal(target.WindowHandle, ExcelCapturePlacement.CurrentTarget().WindowHandle);
                var paths = new List<string>();
                for (var i = 0; i < 5; i++)
                {
                    var path = Path.Combine(temporaryDirectory, $"evidence-{i}.png");
                    using var image = new Bitmap(100 + i * 10, 50 + i * 7);
                    image.Save(path, ImageFormat.Png);
                    paths.Add(path);
                }
                Equal(5, ExcelCapturePlacement.Insert(target, paths, 3));
                Equal(5, (int)sheet.Shapes.Count);
                dynamic first = sheet.Shapes.Item(1);
                dynamic second = sheet.Shapes.Item(2);
                dynamic third = sheet.Shapes.Item(3);
                dynamic fourth = sheet.Shapes.Item(4);
                Equal(true, Math.Abs(Convert.ToDouble(first.Left) - Convert.ToDouble(sheet.Range["C5"].Left)) < 0.1);
                Equal(true, Math.Abs(Convert.ToDouble(first.Top) - Convert.ToDouble(sheet.Range["C5"].Top)) < 0.1);
                Equal(true, Convert.ToDouble(second.Left) >= Convert.ToDouble(first.Left) + Convert.ToDouble(first.Width));
                Equal(true, Convert.ToDouble(third.Left) >= Convert.ToDouble(second.Left) + Convert.ToDouble(second.Width));
                Equal(true, Math.Abs(Convert.ToDouble(fourth.Left) - Convert.ToDouble(first.Left)) < 0.1);
                Equal(true, Convert.ToDouble(fourth.Top) >= Convert.ToDouble(third.Top) + Convert.ToDouble(third.Height));
                var invalidImage = Path.Combine(temporaryDirectory, "invalid-evidence.png");
                File.WriteAllText(invalidImage, "not an image");
                Throws<InvalidOperationException>(() =>
                    ExcelCapturePlacement.Insert(target, [paths[0], invalidImage], 3));
                Equal(5, (int)sheet.Shapes.Count);
                sheet.Range["D5"].Select();
                Throws<InvalidOperationException>(() => ExcelCapturePlacement.Insert(target, paths, 3));
                Equal(5, (int)sheet.Shapes.Count);
                var pathXlsx = Path.Combine(temporaryDirectory, "evidence-test.xlsx");
                book.SaveAs(pathXlsx, 51);
                book.Close(false);
                book = null;
                foreach (var path in paths) File.Delete(path);
                book = excel.Workbooks.Open(pathXlsx);
                Equal(5, (int)book.Worksheets[1].Shapes.Count);
            }
            finally
            {
                if (book is not null) book.Close(false);
                excel.Quit();
                Marshal.FinalReleaseComObject((object)excel);
            }
        });
    }
}
