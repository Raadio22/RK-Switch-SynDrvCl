using System.Windows;

namespace RKSwitch.SynDrvCl;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var splash = new SplashWindow();
        splash.Show();

        var minimumDisplayTime = Task.Delay(1200);
        var initialLoad = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var main = new MainWindow();
        main.InitialLoadCompleted += (_, _) => initialLoad.TrySetResult();
        MainWindow = main;
        main.Show();
        splash.Activate();

        await Task.WhenAll(minimumDisplayTime, initialLoad.Task);
        splash.Close();
        main.Activate();
        ShutdownMode = ShutdownMode.OnMainWindowClose;
    }
}
