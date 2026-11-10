using System;
using System.Windows.Forms;

using CliProgram = ShieldGuard.Cli.Program;

namespace ShieldGuard;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        // --cli-mode launches the CLI host (same engine, console interface).
        if (args.Length > 0 && args[0].Equals("--cli-mode", StringComparison.OrdinalIgnoreCase))
            return CliProgram.Main(args.Skip(1).ToArray()).GetAwaiter().GetResult();

        Application.Run(new MainForm());
        return 0;
    }
}
