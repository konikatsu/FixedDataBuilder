using System.Drawing;
using System.IO;

namespace FixedDataBuilder;

internal static class AppIcon
{
    internal static Icon Load()
    {
        using Stream stream = typeof(AppIcon).Assembly.GetManifestResourceStream("FixedDataBuilder.AppIcon.ico")
            ?? throw new InvalidOperationException("The embedded application icon is missing.");
        using var icon = new Icon(stream);
        return (Icon)icon.Clone();
    }
}
