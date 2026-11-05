namespace FixedDataBuilder;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        ApplicationConfiguration.Initialize();
        Application.Run(CreateStartupForm(args));
    }

    private static Form CreateStartupForm(string[] args)
    {
        if (!args.Contains("--compare", StringComparer.OrdinalIgnoreCase))
        {
            return new MainForm(args);
        }

        using var source = new MainForm(args);
        return source.CreateVisualComparisonForm();
    }
}
