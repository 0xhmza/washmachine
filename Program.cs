using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.Windows.ApplicationModel.DynamicDependency;

namespace Washmachine;

public static class Program
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(nint hWnd, string text, string caption, uint type);

    [STAThread]
    static void Main(string[] args)
    {
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
}
