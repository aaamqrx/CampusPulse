using System.Windows;

namespace CampusPulse.App;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var isDemo = e.Args.Contains("--smoke-test", StringComparer.Ordinal);
        var window = new MainWindow(isDemo);
        MainWindow = window;
        window.Show();
    }
}
