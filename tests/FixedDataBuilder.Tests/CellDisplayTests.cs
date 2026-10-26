using FixedDataBuilder;

internal static partial class Program
{
    private static void RunCellDisplayTests(string repo)
    {
        Test("Space markers and plain text start at the same pixel in both layouts and font sizes", () =>
        {
            foreach (var recordRows in new[] { false, true })
            foreach (var size in new[] { 12f, 18f })
            foreach (var fontName in new[] { "MS Gothic", "Consolas" })
            {
                using var form = CreateCellDisplayForm(recordRows);
                var grid = Get<DataGridView>(form, "grid");
                using var font = new Font(fontName, size);
                grid.Font = font;
                grid.DefaultCellStyle.Font = font;
                grid.ColumnHeadersDefaultCellStyle.Font = font;
                Invoke(form, "RefreshGridSizing");
                using var bitmap = RenderGrid(form);
                var plain = grid.GetCellDisplayRectangle(recordRows ? 1 : 2, recordRows ? 2 : 0, false);
                var spaces = grid.GetCellDisplayRectangle(recordRows ? 1 : 6, recordRows ? 6 : 0, false);
                var plainStart = FirstTextPixel(bitmap, plain, white: false);
                var spacesStart = FirstTextPixel(bitmap, spaces, white: false);
                Equal(plainStart, spacesStart);
            }
        });
        Test("Text, numbers and column headings all use left alignment", () =>
        {
            foreach (var recordRows in new[] { false, true })
            {
                using var form = CreateCellDisplayForm(recordRows);
                var grid = Get<DataGridView>(form, "grid");
                foreach (DataGridViewColumn column in grid.Columns)
                    Equal(DataGridViewContentAlignment.MiddleLeft, column.HeaderCell.InheritedStyle.Alignment);
                foreach (DataGridViewRow row in grid.Rows)
                foreach (DataGridViewCell cell in row.Cells)
                    Equal(DataGridViewContentAlignment.MiddleLeft, cell.InheritedStyle.Alignment);
                if (recordRows)
                {
                    Equal("行", grid.Columns[0].HeaderText);
                    for (var rowIndex = 2; rowIndex < grid.Rows.Count; rowIndex++)
                    {
                        Equal((rowIndex - 1).ToString(), grid[0, rowIndex].Value);
                        Equal(true, grid[0, rowIndex].ReadOnly);
                    }
                    Equal("識別番号", grid.Columns[1].HeaderText);
                    Equal("番号", grid.Columns[2].HeaderText);
                    Equal("X(22)", grid[1, 0].Value);
                    Equal("X(15)", grid[2, 0].Value);
                    Equal("[1-22]", grid[1, 1].Value);
                    Equal("[23-37]", grid[2, 1].Value);
                }
            }
        });
        Test("Columns fit loaded data and complete headings in both layouts", () =>
        {
            foreach (var recordRows in new[] { false, true })
            {
                using var form = CreateBasicForm(repo);
                if (recordRows) Get<ToolStripMenuItem>(form, "recordRowsButton").PerformClick();
                var grid = Get<DataGridView>(form, "grid");
                foreach (DataGridViewColumn column in grid.Columns)
                    Equal(true, column.Width >= column.GetPreferredWidth(DataGridViewAutoSizeColumnMode.AllCells, false));
                if (recordRows)
                {
                    var headerWidth = TextRenderer.MeasureText(grid.Columns[0].HeaderText, grid.ColumnHeadersDefaultCellStyle.Font,
                        Size.Empty, TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine).Width;
                    Equal(true, grid.Columns[0].Width >= headerWidth);
                }
                var cell = grid[recordRows ? 1 : 2, recordRows ? 2 : 0];
                var visibleName = "ジナン" + new string('\u25A1', 7);
                var textWidth = TextRenderer.MeasureText(visibleName, cell.InheritedStyle.Font,
                    Size.Empty, TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine).Width;
                Equal(true, cell.Size.Width >= textWidth + 1);
            }
        });
        Test("Automatic width includes fixed-length space markers even when every value is short", () =>
        {
            foreach (var recordRows in new[] { false, true })
            {
                using var form = CreateCellDisplayForm(recordRows);
                foreach (var record in Get<List<List<string>>>(form, "records")) record[0] = "123";
                Invoke(form, "RefreshGrid");
                var grid = Get<DataGridView>(form, "grid");
                var cell = grid[recordRows ? 1 : 2, recordRows ? 2 : 0];
                var visibleValue = "123" + new string('\uFF65', 19);
                var textWidth = TextRenderer.MeasureText(visibleValue, cell.InheritedStyle.Font,
                    Size.Empty, TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine).Width;
                Equal(true, cell.Size.Width >= textWidth + 1);
                if (recordRows) Equal(true, grid.Columns[1].Width > grid.Columns[4].Width);
            }
        });
        Test("Manual column width remains adjustable and is preserved while editing", () =>
        {
            using var form = CreateCellDisplayForm(recordRows: true);
            var grid = Get<DataGridView>(form, "grid");
            var column = grid.Columns[1];
            column.Width += 51;
            var width = column.Width;
            grid.CurrentCell = grid[1, 6];
            Equal(true, grid.BeginEdit(false));
            ((TextBox)grid.EditingControl!).Text = "111";
            Equal(true, grid.EndEdit());
            Equal(width, column.Width);
            Equal(DataGridViewAutoSizeColumnsMode.None, grid.AutoSizeColumnsMode);
            Equal("111", Get<List<List<string>>>(form, "records")[4][0]);
        });
        Test("Selected space-marker cells retain readable selection foreground and background", () =>
        {
            foreach (var recordRows in new[] { false, true })
            {
                using var form = CreateCellDisplayForm(recordRows);
                var grid = Get<DataGridView>(form, "grid");
                var plainBounds = grid.GetCellDisplayRectangle(recordRows ? 1 : 2, recordRows ? 2 : 0, false);
                using var plainBitmap = RenderGrid(form);
                var expected = FirstTextPixel(plainBitmap, plainBounds, white: false);
                var cell = grid[recordRows ? 1 : 6, recordRows ? 6 : 0];
                grid.CurrentCell = cell;
                cell.Selected = true;
                using var selectedBitmap = RenderGrid(form);
                var selectedBounds = grid.GetCellDisplayRectangle(cell.ColumnIndex, cell.RowIndex, false);
                Equal(expected, FirstTextPixel(selectedBitmap, selectedBounds, white: true));
                var yellow = Color.FromArgb(255, 250, 205).ToArgb();
                for (var y = selectedBounds.Top + 2; y < selectedBounds.Bottom - 2; y++)
                for (var x = selectedBounds.Left + 1; x < Math.Min(selectedBounds.Right - 1, selectedBitmap.Width); x++)
                    Equal(false, selectedBitmap.GetPixel(x, y).ToArgb() == yellow);
            }
        });
    }

    private static MainForm CreateCellDisplayForm(bool recordRows)
    {
        var form = new MainForm();
        Get<List<FieldDefinition>>(form, "fields").AddRange([
            new("識別番号", FieldDataType.HalfWidthText, 22, "X(22)"),
            new("番号", FieldDataType.HalfWidthText, 15, "X(15)"),
            new("宛名番号", FieldDataType.HalfWidthText, 10, "X(10)"),
            new("区分", FieldDataType.PlainNumber, 2, "9(2)")]);
        Get<List<List<string>>>(form, "records").AddRange([
            new() { "1234567890123456789012", "123456789012345", "1234567890", "01" },
            new() { "2234567890123456789012", "223456789012345", "2234567890", "02" },
            new() { "3234567890123456789012", "323456789012345", "3234567890", "03" },
            new() { "4234567890123456789012", "423456789012345", "4234567890", "04" },
            new() { "123       9012", "123", "123", "05" }]);
        if (recordRows) Get<ToolStripMenuItem>(form, "recordRowsButton").PerformClick();
        else Invoke(form, "RefreshGrid");
        form.ShowInTaskbar = false;
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(-30000, -30000);
        form.Show();
        Get<DataGridView>(form, "grid").ClearSelection();
        Application.DoEvents();
        return form;
    }

    private static Bitmap RenderGrid(MainForm form)
    {
        var grid = Get<DataGridView>(form, "grid");
        var bitmap = new Bitmap(grid.Width, grid.Height);
        grid.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
        return bitmap;
    }

    private static Point FirstTextPixel(Bitmap bitmap, Rectangle cell, bool white)
    {
        var minX = int.MaxValue;
        var minY = int.MaxValue;
        for (var y = cell.Top + 2; y < Math.Min(cell.Bottom - 2, bitmap.Height); y++)
        for (var x = cell.Left + 1; x < Math.Min(cell.Left + 30, bitmap.Width); x++)
        {
            var color = bitmap.GetPixel(x, y);
            var isInk = white ? color.R > 240 && color.G > 240 && color.B > 240
                : color.R < 80 && color.G < 80 && color.B < 80;
            if (!isInk) continue;
            minX = Math.Min(minX, x - cell.Left);
            minY = Math.Min(minY, y - cell.Top);
        }
        if (minX == int.MaxValue) throw new Exception("No text pixels found in comparison cell");
        return new Point(minX, minY);
    }
}
