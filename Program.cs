using Microsoft.UI.Xaml;
using WinRT;

namespace Washmachine;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ComWrappersSupport.InitializeComWrappers();
        Application.Start(_ => new App());
    }
}
