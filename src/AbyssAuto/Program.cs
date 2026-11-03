namespace AbyssAuto;
internal static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if(args.Length==3 && args[0]=="--validate")
            return OfflineValidator.Run(Path.GetFullPath(args[1]),Path.GetFullPath(args[2])).GetAwaiter().GetResult();
        Application.Run(new MainForm());
        return 0;
    }
}
