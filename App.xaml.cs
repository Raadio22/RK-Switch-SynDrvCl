using System.Threading;
using System.Windows;

namespace RKSwitch.SynDrvCl;

public partial class App : Application
{
    internal const string InstanceMutexName = "RKSwitchSynDrvCl.App";
    private Mutex? _instanceMutex;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var mutex = new Mutex(initiallyOwned: true, InstanceMutexName, out var createdNew);
        if (!createdNew)
        {
            mutex.Dispose();
            MessageBox.Show("RK-Switch SynDrvCl už je spuštěný.", "RK-Switch SynDrvCl",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }
        _instanceMutex = mutex;

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

    protected override void OnExit(ExitEventArgs e)
    {
        _instanceMutex?.ReleaseMutex();
        _instanceMutex?.Dispose();
        _instanceMutex = null;
        base.OnExit(e);
    }
}
