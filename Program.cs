using System.Runtime.InteropServices;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.Windows.ApplicationModel.DynamicDependency;

namespace Washmachine;

public static class Program
{
    private const uint AttachParentProcess = 0xFFFFFFFF;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(nint hWnd, string text, string caption, uint type);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AllocConsole();

    [DllImport("kernel32.dll")]
    private static extern nint GetStdHandle(int standardHandle);

    [STAThread]
    static void Main(string[] args)
    {
        if (args.Length > 0 && args[0].Equals("--cli-mode", StringComparison.OrdinalIgnoreCase))
        {
            RunCli(args.Skip(1).ToArray());
            return;
        }

        try
        {
            Bootstrap.Initialize(0x00010008); // WindowsAppSDK 1.8
        }
        catch (Exception ex)
        {
            MessageBoxW(0,
                $"Windows App SDK runtime is not installed or failed to initialize.\n\n{ex.Message}",
                "Washmachine – Runtime Error",
                0x00000010); // MB_ICONERROR
            return;
        }

        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(p =>
        {
            var context = new Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(
                Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
            System.Threading.SynchronizationContext.SetSynchronizationContext(context);
            _ = new App();
        });
        Bootstrap.Shutdown();
    }

    private static void RunCli(string[] args)
    {
        const int standardOutputHandle = -11;
        nint outputHandle = GetStdHandle(standardOutputHandle);
        bool hasInheritedOutput = outputHandle != nint.Zero && outputHandle != new nint(-1);
        if (!hasInheritedOutput && !AttachConsole(AttachParentProcess))
            AllocConsole();

        try
        {
            Console.InputEncoding = Encoding.UTF8;
            Console.OutputEncoding = Encoding.UTF8;
            Console.SetIn(new StreamReader(Console.OpenStandardInput(), Encoding.UTF8));
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true });
            Console.SetError(new StreamWriter(Console.OpenStandardError(), new UTF8Encoding(false)) { AutoFlush = true });
            Console.Title = "Washmachine CLI";
        }
        catch
        {
            // A redirected or restricted host may not expose every console property.
        }

        Environment.ExitCode = Washmachine.Cli.Program.Main(args).GetAwaiter().GetResult();
    }
}
