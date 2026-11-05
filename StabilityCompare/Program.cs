namespace StabilityCompare;
internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        string fonts = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        ScottPlot.Fonts.FontResolvers.Insert(0, new ScottPlot.FontResolvers.FileFontResolver("CompareChinese", Path.Combine(fonts, "msyh.ttc"), false, false));
        ScottPlot.Fonts.FontResolvers.Insert(0, new ScottPlot.FontResolvers.FileFontResolver("CompareChinese", Path.Combine(fonts, "msyhbd.ttc"), true, false));
        ScottPlot.Fonts.Default = "CompareChinese";
        Application.Run(new MainForm());
    }
}
