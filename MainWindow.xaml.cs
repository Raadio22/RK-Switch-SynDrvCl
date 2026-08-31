using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace RKSwitch.SynDrvCl;

public partial class MainWindow : Window
{
    private static readonly Brush Teal = new SolidColorBrush(Color.FromRgb(9, 167, 190));
    private static readonly Brush Amber = new SolidColorBrush(Color.FromRgb(255, 170, 0));
    private static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(148, 163, 184));
    private readonly SynologyAutomationService _synology = new();
    private readonly DispatcherTimer _stateTimer;
    private bool _busy;
    private bool _loadingStartupSetting;
    private DateTimeOffset _lastRefresh = DateTimeOffset.MinValue;

    public MainWindow()
    {
        InitializeComponent();
        _stateTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _stateTimer.Tick += async (_, _) =>
        {
            if (IsActive && !_busy) await RefreshStateAsync();
        };
        Loaded += Window_Loaded;
        Closed += (_, _) => _stateTimer.Stop();
    }

    private bool IsDryRun => LiveModeCheckBox.IsChecked != true;

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _loadingStartupSetting = true;
        try { StartupCheckBox.IsChecked = StartupRegistration.IsEnabled(); }
        finally { _loadingStartupSetting = false; }
        await RefreshStateAsync();
        _stateTimer.Start();
    }

    private async void CompanyButton_Click(object sender, RoutedEventArgs e) =>
        await SwitchAsync(ConnectionMode.Company);

    private async void RemoteButton_Click(object sender, RoutedEventArgs e) =>
        await SwitchAsync(ConnectionMode.Remote);

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) =>
        await RefreshStateAsync();

    private void LiveModeChanged(object sender, RoutedEventArgs e)
    {
        var live = LiveModeCheckBox.IsChecked == true;
        DryRunBanner.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(live ? "#FFF0F0" : "#FFF7E7"));
        DryRunBanner.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(live ? "#F2A6A6" : "#FFD277"));
        DryRunText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(live ? "#9F1D1D" : "#7A4D00"));
        DryRunText.Text = live
            ? "ŽIVÉ ZMĚNY POVOLENY – před každým přepnutím bude potvrzení"
            : "ZKUŠEBNÍ REŽIM – připojení nebude změněno";
    }

    private void StartupModeChanged(object sender, RoutedEventArgs e)
    {
        if (_loadingStartupSetting) return;
        var enabled = StartupCheckBox.IsChecked == true;
        try
        {
            StartupRegistration.SetEnabled(enabled);
            StatusText.Text = enabled
                ? "Automatické spuštění po přihlášení do Windows je zapnuté."
                : "Automatické spuštění po přihlášení do Windows je vypnuté.";
        }
        catch (Exception ex)
        {
            _loadingStartupSetting = true;
            try { StartupCheckBox.IsChecked = !enabled; }
            finally { _loadingStartupSetting = false; }
            StatusText.Text = ErrorText(ex);
            MessageBox.Show(StatusText.Text, "Automatické spuštění", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task RefreshStateAsync()
    {
        if (_busy) return;
        SetBusy(true, "Zjišťuji režim přes oficiální nastavení Synology Drive…");
        try
        {
            var result = await Task.Run(() => _synology.Inspect());
            UpdateCurrentState(result.CurrentServer);
            StatusText.Text = result.Message;
        }
        catch (Exception ex)
        {
            UpdateCurrentState(null);
            StatusText.Text = ErrorText(ex);
        }
        finally
        {
            _lastRefresh = DateTimeOffset.Now;
            LastCheckedText.Text = $"Aktualizováno {_lastRefresh:HH:mm:ss}";
            SetBusy(false);
        }
    }

    private async Task SwitchAsync(ConnectionMode mode)
    {
        if (_busy) return;
        var target = mode == ConnectionMode.Company ? AppConfig.CompanyAddress : AppConfig.QuickConnectId;
        var dryRun = IsDryRun;

        if (!dryRun)
        {
            var transfer = _synology.DetectTransfer();
            var transferText = transfer switch
            {
                TransferState.Active => "Synology Drive hlásí probíhající přenos. Doporučeno nejprve počkat na dokončení.",
                TransferState.Idle => "Nebyl rozpoznán probíhající přenos.",
                _ => "Probíhající přenos nelze z UI spolehlivě zjistit. Zkontrolujte jej ručně v Synology Drive."
            };
            var answer = MessageBox.Show(
                $"Chystáte se živě změnit adresu serveru na:\n\n{target}\n\n{transferText}\n\n" +
                "Účet, heslo, SSL a synchronizační úlohy aplikace nemění. Po potvrzení Synology může zobrazit vlastní dialog nebo varování certifikátu; to musíte posoudit ručně.\n\nPokračovat?",
                "Potvrzení živého přepnutí", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes)
            {
                StatusText.Text = "Přepnutí zrušeno uživatelem.";
                return;
            }
        }

        SetBusy(true, mode == ConnectionMode.Company ? "Bezpečně ověřuji místní NAS…" : "Připravuji QuickConnect…");
        try
        {
            var result = await Task.Run(() => _synology.Switch(mode, dryRun));
            UpdateCurrentState(result.CurrentServer);
            StatusText.Text = result.Message;
            if (result.NeedsUserAttention)
            {
                HeaderStateText.Text = "Ruční kontrola";
                HeaderStateDot.Fill = Amber;
                MessageBox.Show(result.Message, "Vyžadována ruční kontrola", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = ErrorText(ex);
            MessageBox.Show(StatusText.Text, "Přepnutí se nezdařilo", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _lastRefresh = DateTimeOffset.Now;
            LastCheckedText.Text = $"Aktualizováno {_lastRefresh:HH:mm:ss}";
            SetBusy(false);
        }
    }

    private void UpdateCurrentState(string? server)
    {
        var normalized = server?.Trim();
        var clientRunning = Process.GetProcessesByName("cloud-drive-ui").Length > 0;
        ClientStateText.Text = clientRunning ? "Synology Drive: spuštěn" : "Synology Drive: není spuštěn";

        if (string.Equals(normalized, AppConfig.CompanyAddress, StringComparison.OrdinalIgnoreCase))
        {
            ModeText.Text = "FIRMA";
            EndpointText.Text = $"Místní NAS · {AppConfig.CompanyAddress}:{AppConfig.SynologyDrivePort}";
            HeaderStateText.Text = "FIRMA";
            StateDot.Fill = HeaderStateDot.Fill = Teal;
            return;
        }

        if (string.Equals(normalized, AppConfig.QuickConnectId, StringComparison.OrdinalIgnoreCase))
        {
            ModeText.Text = "MIMO FIRMU";
            EndpointText.Text = $"QuickConnect · {AppConfig.QuickConnectId}";
            HeaderStateText.Text = "MIMO FIRMU";
            StateDot.Fill = HeaderStateDot.Fill = Amber;
            return;
        }

        ModeText.Text = string.IsNullOrWhiteSpace(normalized) ? "Nelze zjistit" : "Jiný server";
        EndpointText.Text = normalized ?? "Oficiální nastavení není dostupné";
        HeaderStateText.Text = "Neznámý stav";
        StateDot.Fill = HeaderStateDot.Fill = Muted;
    }

    private void SetBusy(bool busy, string? text = null)
    {
        _busy = busy;
        CompanyButton.IsEnabled = !busy;
        RemoteButton.IsEnabled = !busy;
        LiveModeCheckBox.IsEnabled = !busy;
        StartupCheckBox.IsEnabled = !busy;
        if (text is not null) StatusText.Text = text;
    }

    private static string ErrorText(Exception ex) => $"Chyba: {ex.Message}";
}
