namespace FixedDataBuilder;

public class WheelZoomDataGridView : DataGridView
{
    public event EventHandler<MouseEventArgs>? ZoomWheel;
    protected virtual Keys WheelModifiers => ModifierKeys;

    internal void ScrollHorizontallyTo(int offset)
    {
        // The scrollbar range can still be smaller than the content immediately after a layout change.
        HorizontalScrollingOffset = Math.Clamp(offset, HorizontalScrollBar.Minimum, HorizontalScrollBar.Maximum);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if ((WheelModifiers & Keys.Control) != 0) ZoomWheel?.Invoke(this, e);
        else base.OnMouseWheel(e);
    }
}

public class WheelZoomTextBox : TextBox
{
    public event EventHandler<MouseEventArgs>? ZoomWheel;
    protected virtual Keys WheelModifiers => ModifierKeys;
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if ((WheelModifiers & Keys.Control) != 0) ZoomWheel?.Invoke(this, e);
        else base.OnMouseWheel(e);
    }
}
