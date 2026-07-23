using System.Windows;
using AppBase = System.Windows.Application;

namespace TenzoraX;

public partial class App : AppBase
{
    protected override void OnStartup(StartupEventArgs e)
    {
        SplashWindow.ShowSplash();
        base.OnStartup(e);
    }
}
