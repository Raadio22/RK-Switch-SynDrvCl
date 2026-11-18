using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Security;
using System.Windows.Automation;

namespace RKSwitch.SynDrvCl;

internal sealed class SynologyAutomationService
{
    private static readonly string[] EditConnectionNames = ["Upravit připojení", "Edit Connection"];
    private static readonly string[] ServerFieldNames = ["Adresa serveru", "Server address"];
    private static readonly string[] SettingsTitles = ["Nastavení", "Settings"];
    private static readonly string[] OkNames = ["OK"];
    private static readonly string[] CancelNames = ["Storno", "Cancel"];
    private static readonly string[] NotNowNames = ["Nyní ne", "Not now"];
    private static readonly string[] ProceedAnywayNames = ["Přesto pokračovat", "Proceed anyway", "Continue anyway"];
    private static readonly string[] UnsavedChangesTexts =
    [
        "Změny nejsou uloženy",
        "Changes are not saved",
        "Changes have not been saved"
    ];
    private readonly NetworkSafetyProbe _networkProbe = new();

    public OperationResult Inspect()
    {
        AppLog.Info("Čtení oficiálního nastavení Synology zahájeno.");
        var main = FindMainWindow(startIfMissing: true);
        var settings = FindSettingsWindow(main?.Current.ProcessId);
        var openedByUs = settings is null;
        if (settings is null)
        {
            if (main is null)
                return new(null, "Synology Drive Client neběží nebo nemá otevřené hlavní okno.");
            settings = OpenSettings(main);
        }

        try
        {
            var current = ReadServer(settings);
            AppLog.Info($"Oficiální nastavení přečteno. Server: {current ?? "nezjištěn"}.");
            return new(current, $"Server z oficiálního nastavení: {current ?? "nelze přečíst"}. Nebyla provedena žádná změna.");
        }
        finally
        {
            if (openedByUs) InvokeNamedButton(settings, CancelNames);
        }
    }

    public OperationResult Switch(ConnectionMode mode, bool dryRun, SecureString? password = null)
    {
        AppLog.Info($"Automatizace přepnutí zahájena. Režim: {mode}; testovací režim: {dryRun}.");
        if (mode == ConnectionMode.Company)
        {
            _networkProbe.VerifyCompanyNasAsync().GetAwaiter().GetResult();
            AppLog.Info("Kontrola TCP 6690 a MAC místního NAS prošla.");
        }

        var target = mode == ConnectionMode.Company ? AppConfig.CompanyAddress : AppConfig.QuickConnectId;
        var main = FindMainWindow(startIfMissing: true)
            ?? throw new InvalidOperationException("Nelze nalézt hlavní okno Synology Drive Client.");
        var mainWindow = new IntPtr(main.Current.NativeWindowHandle);
        var processId = main.Current.ProcessId;
        var settings = FindSettingsWindow(processId);
        var openedByUs = settings is null;
        settings ??= OpenSettings(main);
        var submitted = false;
        try
        {
            var current = ReadServer(settings);

            if (dryRun)
            {
                var fieldTest = "";
                if (openedByUs)
                {
                    SetServer(settings, target);
                    fieldTest = " Zápis cílové adresy do oficiálního pole byl ověřen;";
                    CancelAndDiscardChanges(settings);
                }
                var probe = mode == ConnectionMode.Company ? " TCP 6690 i MAC NAS byly ověřeny." : "";
                var dialogText = openedByUs ? " dialog byl ukončen přes Storno;" : " již otevřený dialog zůstal beze změny;";
                return new(current, $"Testovací režim: cíl {target} je připraven.{probe}{fieldTest}{dialogText} nic se nezměnilo.");
            }

            if (string.Equals(current?.Trim(), target, StringComparison.OrdinalIgnoreCase))
            {
                if (openedByUs) InvokeNamedButton(settings, CancelNames);
                return new(current, $"Synology Drive již používá {target}; nebyla provedena změna.");
            }

            SetServer(settings, target);
            AppLog.Info($"Pole Adresa serveru nastaveno na {target}; SSL beze změny.");
            if (password is null || password.Length == 0)
                throw new InvalidOperationException("Pro živé přepnutí je nutné zadat heslo účtu DSM.");
            SetPassword(settings, password);
            AppLog.Info("Heslo bylo jednorázově předáno oficiálnímu poli Synology; hodnota nebyla logována.");
            InvokeNamedButton(settings, OkNames);
            submitted = true;
            AppLog.Info("Oficiální tlačítko OK aktivováno.");

            var confirmationResult = ResolvePostSubmitDialogs(mode, target, processId, mainWindow, TimeSpan.FromSeconds(18));
            if (!confirmationResult.Completed)
            {
                var message = "Synology Drive zatím nedokončil změnu připojení. Zkontrolujte jeho otevřená okna.";
                if (mode == ConnectionMode.Remote)
                {
                    AppLog.Info($"{message} Režim MIMO FIRMU nebude blokován vyskakovacím oknem RK-Switch.");
                    return new(target, "Přepnutí na QuickConnect bylo odesláno; Synology Drive ještě dokončuje připojení.");
                }
                return new(target, message, true);
            }

            var handled = confirmationResult.HandledQuickConnectOffer || confirmationResult.HandledCertificate
                ? " Potřebná potvrzení Synology byla bezpečně vyřízena."
                : "";
            var successMessage = $"Synology Drive byl přepnut na {target}.{handled} Uložené uživatelské jméno a synchronizační úlohy zůstaly beze změny.";
            var windowClosed = TryCloseMainWindow(mainWindow, processId);
            return new(target, windowClosed ? successMessage : $"{successMessage} Okno Synology Drive se nepodařilo zavřít.",
                NeedsUserAttention: !windowClosed, SwitchCompleted: true);
        }
        catch
        {
            if (openedByUs && !submitted) TryCancelAndDiscardChanges(settings);
            throw;
        }
    }

    public TransferState DetectTransfer()
    {
        var main = FindMainWindow(startIfMissing: false);
        if (main is null) return TransferState.Unknown;
        var text = string.Join(" ", main.FindAll(TreeScope.Descendants, Condition.TrueCondition)
            .Cast<AutomationElement>()
            .Select(e => SafeName(e))
            .Where(s => !string.IsNullOrWhiteSpace(s)));

        var activeWords = new[] { "nahrávání", "stahování", "synchronizuje se", "uploading", "downloading", "syncing" };
        if (activeWords.Any(w => text.Contains(w, StringComparison.OrdinalIgnoreCase))) return TransferState.Active;
        return TransferState.Unknown;
    }

    private static AutomationElement? FindMainWindow(bool startIfMissing)
    {
        var window = FindTopWindow(e =>
            SafeName(e).Contains("Synology Drive Client", StringComparison.OrdinalIgnoreCase) &&
            IsSynologyUi(e));
        if (window is not null || !startIfMissing) return window;

        var exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SynologyDrive", "SynologyDrive.app", "bin", "cloud-drive-ui.exe");
        if (!File.Exists(exe)) throw new InvalidOperationException($"Soubor Synology Drive nebyl nalezen: {exe}");
        Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
        return WaitFor(() => FindMainWindow(false), TimeSpan.FromSeconds(8));
    }

    private static AutomationElement? FindSettingsWindow(int? processId = null) =>
        FindTopWindow(e => SettingsTitles.Any(t => string.Equals(SafeName(e), t, StringComparison.OrdinalIgnoreCase)) &&
                           (processId is null ? IsSynologyUi(e) : e.Current.ProcessId == processId) &&
                           FindNamedElement(e, ServerFieldNames) is not null);

    private static AutomationElement OpenSettings(AutomationElement main)
    {
        SetForegroundWindow(new IntPtr(main.Current.NativeWindowHandle));
        var button = FindNamedElement(main, EditConnectionNames)
            ?? throw new InvalidOperationException("V hlavním okně nebylo nalezeno tlačítko Upravit připojení.");
        Invoke(button);
        var processId = main.Current.ProcessId;
        return WaitFor(() => FindSettingsWindow(processId), TimeSpan.FromSeconds(6))
            ?? throw new InvalidOperationException("Dialog Nastavení se po volbě Upravit připojení neotevřel.");
    }

    private static string? ReadServer(AutomationElement settings)
    {
        var field = FindNamedElement(settings, ServerFieldNames)
            ?? throw new InvalidOperationException("Pole Adresa serveru nebylo nalezeno.");
        var value = ReadValue(field);
        if (value is not null) return value;
        var edit = field.FindFirst(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));
        return edit is null ? null : ReadValue(edit);
    }

    private static void SetServer(AutomationElement settings, string value)
    {
        var field = FindNamedElement(settings, ServerFieldNames)
            ?? throw new InvalidOperationException("Pole Adresa serveru nebylo nalezeno.");
        var edit = field.FindFirst(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));
        var target = edit is not null && SupportsValue(edit) ? edit : SupportsValue(field) ? field : null;
        if (target is null || !target.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern))
            throw new InvalidOperationException("Pole Adresa serveru nepodporuje bezpečný zápis přes Windows UI Automation.");
        ((ValuePattern)pattern).SetValue(value);
        if (!string.Equals(ReadValue(target)?.Trim(), value, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Synology Drive nepotvrdil zapsání cílové adresy; tlačítko OK nebylo stisknuto.");
    }

    private static void SetPassword(AutomationElement settings, SecureString password)
    {
        var field = FindNamedElement(settings, ["Heslo", "Password"])
            ?? throw new InvalidOperationException("Pole Heslo nebylo v oficiálním dialogu Synology nalezeno.");
        var edit = field.Current.ControlType == ControlType.Edit ? field : field.FindFirst(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));
        var target = edit is not null && SupportsValue(edit) ? edit : SupportsValue(field) ? field : null;
        if (target is null || !target.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern))
            throw new InvalidOperationException("Pole Heslo nepodporuje bezpečné jednorázové vyplnění přes Windows UI Automation.");

        var plainText = new NetworkCredential(string.Empty, password).Password;
        try { ((ValuePattern)pattern).SetValue(plainText); }
        finally { plainText = string.Empty; }
    }

    private PostSubmitResult ResolvePostSubmitDialogs(
        ConnectionMode mode, string target, int processId, IntPtr mainWindow, TimeSpan timeout)
    {
        var elapsed = Stopwatch.StartNew();
        TimeSpan? clearSince = null;
        var handledQuickConnectOffer = false;
        var handledCertificate = false;

        do
        {
            var quickConnectOffer = FindDialog(processId, SynologyDialogKind.QuickConnectOffer, NotNowNames);
            if (mode == ConnectionMode.Company && !handledQuickConnectOffer)
            {
                if (quickConnectOffer is not null)
                {
                    InvokeNamedButton(quickConnectOffer, NotNowNames);
                    handledQuickConnectOffer = true;
                    clearSince = null;
                    AppLog.Info("Dotaz Synology na přechod k QuickConnect vyřízen volbou Nyní ne.");
                    Thread.Sleep(250);
                    continue;
                }
            }

            var certificate = FindDialog(processId, SynologyDialogKind.UntrustedCertificate, ProceedAnywayNames);
            if (mode == ConnectionMode.Company && !handledCertificate)
            {
                if (certificate is not null)
                {
                    var settings = FindSettingsWindow(processId);
                    if (settings is not null &&
                        !string.Equals(ReadServer(settings)?.Trim(), target, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Certifikátové potvrzení neodpovídá cílové adrese firemního NASu.");

                    _networkProbe.VerifyCompanyNasAsync().GetAwaiter().GetResult();
                    InvokeNamedButton(certificate, ProceedAnywayNames);
                    handledCertificate = true;
                    clearSince = null;
                    AppLog.Info("Nedůvěryhodný SSL certifikát místního NASu potvrzen až po opakované kontrole TCP a MAC.");
                    Thread.Sleep(250);
                    continue;
                }
            }

            if (quickConnectOffer is null && certificate is null && FindSettingsWindow(processId) is null &&
                (!IsVisibleWindowOfProcess(mainWindow, processId) || IsWindowEnabled(mainWindow)))
            {
                clearSince ??= elapsed.Elapsed;
                if (elapsed.Elapsed - clearSince.Value >= TimeSpan.FromSeconds(1.2))
                    return new(true, handledQuickConnectOffer, handledCertificate);
            }
            else
            {
                clearSince = null;
            }

            Thread.Sleep(150);
        } while (elapsed.Elapsed < timeout);

        return new(false, handledQuickConnectOffer, handledCertificate);
    }

    private static AutomationElement? FindDialog(int processId, SynologyDialogKind kind, IEnumerable<string> buttonNames)
    {
        var windows = AutomationElement.RootElement.FindAll(TreeScope.Children,
            new AndCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window),
                new PropertyCondition(AutomationElement.ProcessIdProperty, processId)));
        foreach (AutomationElement window in windows)
        {
            if (DialogMatches(window, kind, buttonNames)) return window;

            var sheets = window.FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Custom));
            foreach (AutomationElement sheet in sheets)
                if (DialogMatches(sheet, kind, buttonNames)) return sheet;
        }
        return null;
    }

    private static bool DialogMatches(AutomationElement element, SynologyDialogKind kind, IEnumerable<string> buttonNames)
    {
        try
        {
            if (FindNamedElement(element, buttonNames) is null) return false;
            return SynologyDialogPolicy.Classify(AllText(element)) == kind;
        }
        catch (ElementNotAvailableException)
        {
            return false;
        }
    }

    private static string AllText(AutomationElement element) => string.Join(" ",
        new[] { SafeName(element) }.Concat(
            element.FindAll(TreeScope.Descendants, Condition.TrueCondition)
                .Cast<AutomationElement>()
                .Select(SafeName)));

    private static AutomationElement? FindTopWindow(Func<AutomationElement, bool> predicate) =>
        AutomationElement.RootElement.FindAll(TreeScope.Children,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window))
            .Cast<AutomationElement>().FirstOrDefault(predicate);

    private static AutomationElement? FindNamedElement(AutomationElement root, IEnumerable<string> names) =>
        root.FindAll(TreeScope.Descendants, Condition.TrueCondition).Cast<AutomationElement>()
            .FirstOrDefault(e => names.Any(n => string.Equals(SafeName(e), n, StringComparison.OrdinalIgnoreCase)));

    private static void InvokeNamedButton(AutomationElement root, IEnumerable<string> names)
    {
        var button = FindNamedElement(root, names)
            ?? throw new InvalidOperationException($"Tlačítko {string.Join('/', names)} nebylo nalezeno.");
        Invoke(button);
    }

    private static void CancelAndDiscardChanges(AutomationElement settings)
    {
        InvokeNamedButton(settings, CancelNames);

        var confirmation = WaitFor(
            () => FindUnsavedChangesConfirmation(settings),
            TimeSpan.FromSeconds(3));
        if (confirmation is null)
            throw new InvalidOperationException(
                "Synology Drive nepotvrdil bezpečné zahození testovací změny; dialog byl ponechán otevřený pro ruční kontrolu.");

        InvokeNamedButton(confirmation, OkNames);
        AppLog.Info("Synology potvrdil zahození neuložené testovací změny.");
    }

    private static AutomationElement? FindUnsavedChangesConfirmation(AutomationElement settings)
    {
        try
        {
            return settings.FindAll(TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Custom))
                .Cast<AutomationElement>()
                .Where(element => string.Equals(SafeClassName(element), "SynoMessageSheet", StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault(sheet =>
                {
                    var text = string.Join(" ",
                        sheet.FindAll(TreeScope.Descendants, Condition.TrueCondition)
                            .Cast<AutomationElement>()
                            .Select(SafeName));
                    return UnsavedChangesTexts.Any(expected =>
                        text.Contains(expected, StringComparison.OrdinalIgnoreCase));
                });
        }
        catch (ElementNotAvailableException)
        {
            return null;
        }
    }

    private static void TryCancelAndDiscardChanges(AutomationElement settings)
    {
        try { CancelAndDiscardChanges(settings); }
        catch { /* Best-effort cleanup after a pre-submit failure. */ }
    }

    private static void Invoke(AutomationElement element)
    {
        if (!element.TryGetCurrentPattern(InvokePattern.Pattern, out var pattern))
            throw new InvalidOperationException($"Prvek {SafeName(element)} nelze bezpečně aktivovat přes UI Automation.");
        ((InvokePattern)pattern).Invoke();
    }

    private static string? ReadValue(AutomationElement element) =>
        element.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern) ? ((ValuePattern)pattern).Current.Value : null;

    private static bool SupportsValue(AutomationElement element) => element.TryGetCurrentPattern(ValuePattern.Pattern, out _);
    private static string SafeName(AutomationElement element) { try { return element.Current.Name ?? ""; } catch { return ""; } }
    private static string SafeClassName(AutomationElement element) { try { return element.Current.ClassName ?? ""; } catch { return ""; } }

    private static bool IsSynologyUi(AutomationElement element)
    {
        try
        {
            using var process = Process.GetProcessById(element.Current.ProcessId);
            return string.Equals(process.ProcessName, "cloud-drive-ui", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception or ElementNotAvailableException)
        {
            return false;
        }
    }

    private static bool TryCloseMainWindow(IntPtr window, int processId)
    {
        const uint wmClose = 0x0010;
        if (window == IntPtr.Zero) return false;
        if (!IsVisibleWindowOfProcess(window, processId)) return true;
        if (!IsWindowEnabled(window) || !PostMessage(window, wmClose, IntPtr.Zero, IntPtr.Zero))
        {
            AppLog.Info("Okno Synology Drive nepřijalo požadavek na zavření.");
            return false;
        }

        var elapsed = Stopwatch.StartNew();
        while (IsVisibleWindowOfProcess(window, processId) && elapsed.Elapsed < TimeSpan.FromSeconds(3))
            Thread.Sleep(150);

        var closed = !IsVisibleWindowOfProcess(window, processId);
        AppLog.Info(closed ? "Hlavní okno Synology Drive bylo zavřeno." : "Hlavní okno Synology Drive zůstalo otevřené.");
        return closed;
    }

    private static bool IsVisibleWindowOfProcess(IntPtr window, int processId) =>
        IsWindowVisible(window) && GetWindowThreadProcessId(window, out var owner) != 0 && owner == (uint)processId;

    private static T? WaitFor<T>(Func<T?> probe, TimeSpan timeout) where T : class
    {
        var until = DateTime.UtcNow + timeout;
        do { var value = probe(); if (value is not null) return value; Thread.Sleep(150); } while (DateTime.UtcNow < until);
        return null;
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", EntryPoint = "PostMessageW")]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool IsWindowEnabled(IntPtr window);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    private sealed record PostSubmitResult(
        bool Completed,
        bool HandledQuickConnectOffer,
        bool HandledCertificate);
}
