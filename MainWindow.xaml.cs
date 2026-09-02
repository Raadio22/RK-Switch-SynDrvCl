using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Security;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace RKSwitch.SynDrvCl;

public partial class MainWindow : Window
{
    internal event EventHandler? InitialLoadCompleted;

    private static readonly Brush Teal = new SolidColorBrush(Color.FromRgb(9, 167, 190));
    private static readonly Brush Amber = new SolidColorBrush(Color.FromRgb(255, 170, 0));
    private static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(148, 163, 184));
    private readonly SynologyAutomationService _synology = new();
    private readonly NetworkSafetyProbe _networkProbe = new();
    private readonly DispatcherTimer _stateTimer;
    private CancellationTokenSource? _networkChangeDebounce;
    private bool _busy;
    private bool _autoSwitchRunning;
    private bool _loadingStartupSetting;
    private bool _loadingAutoSwitchSetting;
    private string? _currentServer;
    private DateTimeOffset _lastRefresh = DateTimeOffset.MinValue;
    private DateTimeOffset _lastAutomaticSwitch = DateTimeOffset.MinValue;

    public MainWindow()
    {
        InitializeComponent();
        _stateTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _stateTimer.Tick += async (_, _) =>
        {
            if (_busy) return;
            if (AutoSwitchCheckBox.IsChecked == true)
                await EvaluateAutomaticSwitchAsync("pravidelná kontrola");
            else if (IsActive)
                await RefreshStateAsync();
        };
        NetworkChange.NetworkAddressChanged += NetworkAddressChanged;
        Loaded += Window_Loaded;
        Closed += (_, _) =>
        {
            _stateTimer.Stop();
            _networkChangeDebounce?.Cancel();
            _networkChangeDebounce?.Dispose();
            NetworkChange.NetworkAddressChanged -= NetworkAddressChanged;
        };
    }

    private bool IsDryRun => TestModeCheckBox.IsChecked == true;

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        AppLog.Info("Aplikace 0.5.0 spuštěna.");
        try
        {
            _loadingStartupSetting = true;
            try { StartupCheckBox.IsChecked = StartupRegistration.IsEnabled(); }
            finally { _loadingStartupSetting = false; }
            _loadingAutoSwitchSetting = true;
            try { AutoSwitchCheckBox.IsChecked = UserPreferences.AutomaticSwitchingEnabled; }
            finally { _loadingAutoSwitchSetting = false; }
            UpdateAutoSwitchStatus();
            UpdateCredentialStatus();
            await RefreshStateAsync();
            _stateTimer.Start();
        }
        finally
        {
            InitialLoadCompleted?.Invoke(this, EventArgs.Empty);
            if (AutoSwitchCheckBox.IsChecked == true)
                _ = Dispatcher.BeginInvoke(new Action(async () => await EvaluateAutomaticSwitchAsync("spuštění aplikace")));
        }
    }

    private async void CompanyButton_Click(object sender, RoutedEventArgs e) =>
        await SwitchAsync(ConnectionMode.Company);

    private async void RemoteButton_Click(object sender, RoutedEventArgs e) =>
        await SwitchAsync(ConnectionMode.Remote);

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) =>
        await RefreshStateAsync();

    private void TestModeChanged(object sender, RoutedEventArgs e)
    {
        var testOnly = TestModeCheckBox.IsChecked == true;
        if (testOnly && AutoSwitchCheckBox.IsChecked == true)
        {
            _loadingAutoSwitchSetting = true;
            try { AutoSwitchCheckBox.IsChecked = false; }
            finally { _loadingAutoSwitchSetting = false; }
            UserPreferences.AutomaticSwitchingEnabled = false;
            UpdateAutoSwitchStatus("vypnutá testovacím režimem");
        }
        DryRunBanner.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(testOnly ? "#FFF7E7" : "#FFF0F0"));
        DryRunBanner.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(testOnly ? "#FFD277" : "#F2A6A6"));
        DryRunText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(testOnly ? "#7A4D00" : "#9F1D1D"));
        DryRunText.Text = testOnly
            ? "TESTOVACÍ REŽIM – připojení nebude změněno"
            : "ŽIVÝ REŽIM – kliknutí na trasu provede přepnutí";
        AppLog.Info(testOnly ? "Zapnut testovací režim bez změn." : "Zapnut živý režim s automatickým přepnutím.");
    }

    private async void AutoSwitchModeChanged(object sender, RoutedEventArgs e)
    {
        if (_loadingAutoSwitchSetting) return;
        var enabled = AutoSwitchCheckBox.IsChecked == true;
        try
        {
            if (enabled && !WindowsCredentialStore.Exists)
            {
                _loadingAutoSwitchSetting = true;
                try { AutoSwitchCheckBox.IsChecked = false; }
                finally { _loadingAutoSwitchSetting = false; }
                UserPreferences.AutomaticSwitchingEnabled = false;
                UpdateAutoSwitchStatus("chybí uložené heslo");
                MessageBox.Show("Nejprve uložte heslo DSM přes tlačítko Správa hesla. Automatika je potřebuje pro oficiální potvrzení změny.",
                    "Automatické přepínání", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (enabled && TestModeCheckBox.IsChecked == true)
                TestModeCheckBox.IsChecked = false;

            UserPreferences.AutomaticSwitchingEnabled = enabled;
            UpdateAutoSwitchStatus(enabled ? "zapnutá · čekám na kontrolu" : "vypnutá");
            AppLog.Info(enabled ? "Automatické přepínání podle sítě zapnuto." : "Automatické přepínání podle sítě vypnuto.");
            if (enabled) await EvaluateAutomaticSwitchAsync("zapnutí automatiky");
        }
        catch (Exception ex)
        {
            AppLog.Error("Změna automatického přepínání selhala", ex);
            _loadingAutoSwitchSetting = true;
            try { AutoSwitchCheckBox.IsChecked = !enabled; }
            finally { _loadingAutoSwitchSetting = false; }
            UpdateAutoSwitchStatus("chyba nastavení");
            MessageBox.Show(ErrorText(ex), "Automatické přepínání", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void StartupModeChanged(object sender, RoutedEventArgs e)
    {
        if (_loadingStartupSetting) return;
        var enabled = StartupCheckBox.IsChecked == true;
        try
        {
            StartupRegistration.SetEnabled(enabled);
            AppLog.Info(enabled ? "Automatické spuštění zapnuto." : "Automatické spuštění vypnuto.");
            StatusText.Text = enabled
                ? "Automatické spuštění po přihlášení do Windows je zapnuté."
                : "Automatické spuštění po přihlášení do Windows je vypnuté.";
        }
        catch (Exception ex)
        {
            AppLog.Error("Změna automatického spuštění selhala", ex);
            _loadingStartupSetting = true;
            try { StartupCheckBox.IsChecked = !enabled; }
            finally { _loadingStartupSetting = false; }
            StatusText.Text = ErrorText(ex);
            MessageBox.Show(StatusText.Text, "Automatické spuštění", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void NetworkAddressChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.HasShutdownStarted) return;
        Dispatcher.BeginInvoke(new Action(ScheduleAutomaticCheckAfterNetworkChange));
    }

    private void ScheduleAutomaticCheckAfterNetworkChange()
    {
        if (AutoSwitchCheckBox.IsChecked != true) return;
        _networkChangeDebounce?.Cancel();
        _networkChangeDebounce?.Dispose();
        _networkChangeDebounce = new CancellationTokenSource();
        _ = RunDebouncedAutomaticCheckAsync(_networkChangeDebounce.Token);
    }

    private async Task RunDebouncedAutomaticCheckAsync(CancellationToken cancellationToken)
    {
        try
        {
            UpdateAutoSwitchStatus("změna sítě · čekám na ustálení");
            await Task.Delay(TimeSpan.FromSeconds(6), cancellationToken);
            await EvaluateAutomaticSwitchAsync("změna síťového připojení");
        }
        catch (OperationCanceledException)
        {
            // Další síťová událost restartovala odpočet.
        }
    }

    private async Task EvaluateAutomaticSwitchAsync(string reason)
    {
        if (AutoSwitchCheckBox.IsChecked != true || _busy || _autoSwitchRunning) return;
        if (IsDryRun)
        {
            UpdateAutoSwitchStatus("pozastavená testovacím režimem");
            return;
        }

        _autoSwitchRunning = true;
        SetBusy(true, "Automatika zjišťuje dostupnou síť…");
        SecureString? password = null;
        try
        {
            AppLog.Info($"Automatická kontrola zahájena. Důvod: {reason}.");
            var network = await _networkProbe.DetectCompanyNetworkAsync();
            var desiredMode = AutomaticSwitchPolicy.DesiredMode(network.IsCompanyNetwork);

            if (desiredMode == ConnectionMode.Remote &&
                string.Equals(_currentServer, AppConfig.CompanyAddress, StringComparison.OrdinalIgnoreCase))
            {
                UpdateAutoSwitchStatus("ověřuji odchod z firemní sítě");
                await Task.Delay(TimeSpan.FromSeconds(4));
                var confirmation = await _networkProbe.DetectCompanyNetworkAsync();
                if (confirmation.IsCompanyNetwork)
                {
                    desiredMode = ConnectionMode.Company;
                    network = confirmation;
                }
            }

            var networkName = desiredMode == ConnectionMode.Company ? "FIRMA" : "MIMO FIRMU";
            if (!AutomaticSwitchPolicy.RequiresSwitch(_currentServer, desiredMode))
            {
                UpdateAutoSwitchStatus($"zapnutá · správně {networkName}");
                StatusText.Text = $"Automatika ověřila síť: režim {networkName} je už správně nastavený.";
                AppLog.Info($"Automatická kontrola dokončena bez změny. Režim {networkName} je správný.");
                return;
            }

            if (DateTimeOffset.Now - _lastAutomaticSwitch < TimeSpan.FromSeconds(30))
            {
                UpdateAutoSwitchStatus("čeká po nedávném přepnutí");
                return;
            }

            if (_synology.DetectTransfer() == TransferState.Active)
            {
                UpdateAutoSwitchStatus("přepnutí odloženo · probíhá přenos");
                StatusText.Text = "Automatické přepnutí bylo odloženo, protože Synology Drive hlásí probíhající přenos.";
                AppLog.Info("Automatické přepnutí odloženo kvůli probíhajícímu přenosu.");
                return;
            }

            if (!WindowsCredentialStore.TryRead(out password))
            {
                UpdateAutoSwitchStatus("pozastavená · chybí heslo");
                StatusText.Text = "Automatika nemůže přepnout: ve Windows není uložené heslo DSM.";
                AppLog.Info("Automatické přepnutí nebylo provedeno, protože chybí uložené heslo.");
                return;
            }

            UpdateAutoSwitchStatus($"přepínám na {networkName}");
            StatusText.Text = desiredMode == ConnectionMode.Company
                ? "Automatika rozpoznala firemní NAS a přepíná na místní síť…"
                : "Automatika potvrdila odchod z firemní sítě a přepíná na QuickConnect…";
            var result = await Task.Run(() => _synology.Switch(desiredMode, dryRun: false, password: password));
            UpdateCurrentState(result.CurrentServer);
            StatusText.Text = $"Automatika: {result.Message}";
            _lastAutomaticSwitch = DateTimeOffset.Now;
            UpdateAutoSwitchStatus(result.NeedsUserAttention
                ? "vyžaduje ruční kontrolu"
                : $"zapnutá · přepnuto na {networkName}");
            AppLog.Info($"Automatické přepnutí dokončeno. {result.Message}");
            if (result.NeedsUserAttention)
            {
                HeaderStateText.Text = "Ruční kontrola";
                HeaderStateDot.Fill = Amber;
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("Automatické přepnutí selhalo", ex);
            UpdateAutoSwitchStatus("chyba · další pokus za minutu");
            StatusText.Text = $"Automatika: {ErrorText(ex)}";
        }
        finally
        {
            password?.Dispose();
            _lastRefresh = DateTimeOffset.Now;
            LastCheckedText.Text = $"Aktualizováno {_lastRefresh:HH:mm:ss}";
            SetBusy(false);
            _autoSwitchRunning = false;
        }
    }

    private void UpdateAutoSwitchStatus(string? detail = null)
    {
        var enabled = AutoSwitchCheckBox.IsChecked == true;
        AutoSwitchStatusText.Text = detail is null
            ? enabled ? "Automatika: zapnutá" : "Automatika: vypnutá"
            : $"Automatika: {detail}";
        AutoSwitchStatusText.Foreground = enabled ? Teal : Muted;
    }

    private async Task RefreshStateAsync()
    {
        if (_busy) return;
        AppLog.Info("Obnova aktuálního stavu zahájena.");
        SetBusy(true, "Zjišťuji režim přes oficiální nastavení Synology Drive…");
        try
        {
            var result = await Task.Run(() => _synology.Inspect());
            UpdateCurrentState(result.CurrentServer);
            StatusText.Text = result.Message;
            AppLog.Info($"Obnova stavu dokončena. Režim: {ModeClassifier.DisplayName(result.CurrentServer)}.");
        }
        catch (Exception ex)
        {
            AppLog.Error("Obnova aktuálního stavu selhala", ex);
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
        SecureString? password = null;
        AppLog.Info($"Požadavek na režim {mode}; cíl {target}; testovací režim: {dryRun}.");

        if (!dryRun)
        {
            var transfer = _synology.DetectTransfer();
            var transferText = transfer switch
            {
                TransferState.Active => "Synology Drive hlásí probíhající přenos. Doporučeno nejprve počkat na dokončení.",
                TransferState.Idle => "Nebyl rozpoznán probíhající přenos.",
                _ => "Probíhající přenos nelze z UI spolehlivě zjistit. Zkontrolujte jej ručně v Synology Drive."
            };

            if (WindowsCredentialStore.TryRead(out password))
            {
                AppLog.Info("Heslo bylo načteno ze Správce přihlašovacích údajů Windows; hodnota nebyla logována.");
                if (transfer == TransferState.Active && MessageBox.Show(
                        $"{transferText}\n\nPřesto pokračovat v přepnutí na {target}?",
                        "Probíhá přenos", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                {
                    password.Dispose();
                    AppLog.Info("Živé přepnutí zrušeno kvůli probíhajícímu přenosu.");
                    StatusText.Text = "Přepnutí zrušeno uživatelem.";
                    return;
                }
            }
            else
            {
                var prompt = new PasswordPromptWindow(target, transferText) { Owner = this };
                if (prompt.ShowDialog() != true)
                {
                    AppLog.Info("Živé přepnutí zrušeno při prvním zadání hesla.");
                    StatusText.Text = "Přepnutí zrušeno uživatelem.";
                    return;
                }
                password = prompt.TakePassword();
                if (prompt.ShouldSavePassword)
                {
                    try
                    {
                        WindowsCredentialStore.Save(password);
                        AppLog.Info("Heslo bylo uloženo do Správce přihlašovacích údajů Windows; hodnota nebyla logována.");
                        UpdateCredentialStatus();
                    }
                    catch (Exception ex)
                    {
                        password.Dispose();
                        AppLog.Error("Uložení hesla do Windows selhalo", ex);
                        StatusText.Text = ErrorText(ex);
                        MessageBox.Show(StatusText.Text, "Heslo nebylo uloženo", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                }
            }
        }

        SetBusy(true, mode == ConnectionMode.Company ? "Bezpečně ověřuji místní NAS…" : "Připravuji QuickConnect…");
        try
        {
            var result = await Task.Run(() => _synology.Switch(mode, dryRun, password));
            UpdateCurrentState(result.CurrentServer);
            StatusText.Text = result.Message;
            AppLog.Info($"Operace dokončena. {result.Message}");
            if (result.NeedsUserAttention)
            {
                HeaderStateText.Text = "Ruční kontrola";
                HeaderStateDot.Fill = Amber;
                MessageBox.Show(result.Message, "Vyžadována ruční kontrola", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            AppLog.Error($"Přepnutí na {target} selhalo", ex);
            StatusText.Text = ErrorText(ex);
            MessageBox.Show(StatusText.Text, "Přepnutí se nezdařilo", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            password?.Dispose();
            _lastRefresh = DateTimeOffset.Now;
            LastCheckedText.Text = $"Aktualizováno {_lastRefresh:HH:mm:ss}";
            SetBusy(false);
        }
    }

    private void UpdateCurrentState(string? server)
    {
        var normalized = server?.Trim();
        _currentServer = normalized;
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
        TestModeCheckBox.IsEnabled = !busy;
        StartupCheckBox.IsEnabled = !busy;
        AutoSwitchCheckBox.IsEnabled = !busy;
        CredentialButton.IsEnabled = !busy;
        if (text is not null) StatusText.Text = text;
    }

    private void UpdateCredentialStatus()
    {
        try
        {
            CredentialStatusText.Text = WindowsCredentialStore.Exists
                ? "Heslo: bezpečně uloženo ve Windows"
                : "Heslo: zatím není uloženo";
        }
        catch (Exception ex)
        {
            CredentialStatusText.Text = "Heslo: stav nelze zjistit";
            AppLog.Error("Kontrola uloženého hesla selhala", ex);
        }
    }

    private void ManageCredential_Click(object sender, RoutedEventArgs e)
    {
        var window = new CredentialSettingsWindow { Owner = this };
        window.ShowDialog();
        UpdateCredentialStatus();
    }

    private static string ErrorText(Exception ex) => $"Chyba: {ex.Message}";

    private void OpenLog_Click(object sender, RoutedEventArgs e)
    {
        AppLog.Info("Uživatel otevřel diagnostický log.");
        try
        {
            Directory.CreateDirectory(AppLog.DirectoryPath);
            if (!File.Exists(AppLog.FilePath)) File.WriteAllText(AppLog.FilePath, "");
            Process.Start(new ProcessStartInfo(AppLog.FilePath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppLog.Error("Otevření diagnostického logu selhalo", ex);
            MessageBox.Show(ErrorText(ex), "Diagnostický log", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
