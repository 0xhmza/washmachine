using Microsoft.UI.Xaml;
using Microsoft.Windows.ApplicationModel.DynamicDependency;

namespace Washmachine;

public static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        Bootstrap.Initialize(0x00010008); // WindowsAppSDK 1.8
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
