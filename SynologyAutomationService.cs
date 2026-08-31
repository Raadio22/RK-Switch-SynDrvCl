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
        var settings = FindSettingsWindow();
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
        var settings = FindSettingsWindow();
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

            Thread.Sleep(1200);
            var warning = FindCertificateOrWarningWindow();
            if (warning is not null)
                return new(target, "Synology Drive zobrazil varování nebo potvrzovací dialog. Aplikace s ním záměrně nemanipulovala; zkontrolujte jej ručně.", true);

            var remainingSettings = WaitForSettingsToClose(TimeSpan.FromSeconds(5));
            if (remainingSettings is not null)
                return new(target, "Oficiální dialog Synology Drive zůstal po stisku OK otevřený. Může vyžadovat heslo, opravu údaje nebo jiné ruční rozhodnutí; aplikace už nic dalšího neprovedla.", true);

            return new(target, $"Synology Drive byl přepnut na {target}. Uložené uživatelské jméno, SSL a synchronizační úlohy zůstaly beze změny.");
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
            !SettingsTitles.Any(t => string.Equals(SafeName(e), t, StringComparison.OrdinalIgnoreCase)));
        if (window is not null || !startIfMissing) return window;

        var exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SynologyDrive", "SynologyDrive.app", "bin", "cloud-drive-ui.exe");
        if (!File.Exists(exe)) throw new InvalidOperationException($"Soubor Synology Drive nebyl nalezen: {exe}");
        Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
        return WaitFor(() => FindMainWindow(false), TimeSpan.FromSeconds(8));
    }

    private static AutomationElement? FindSettingsWindow() =>
        FindTopWindow(e => SettingsTitles.Any(t => string.Equals(SafeName(e), t, StringComparison.OrdinalIgnoreCase)) &&
                           FindNamedElement(e, ServerFieldNames) is not null);

    private static AutomationElement? WaitForSettingsToClose(TimeSpan timeout)
    {
        var until = DateTime.UtcNow + timeout;
        AutomationElement? settings;
        do
        {
            settings = FindSettingsWindow();
            if (settings is null) return null;
            Thread.Sleep(150);
        } while (DateTime.UtcNow < until);
        return settings;
    }

    private static AutomationElement OpenSettings(AutomationElement main)
    {
        SetForegroundWindow(new IntPtr(main.Current.NativeWindowHandle));
        var button = FindNamedElement(main, EditConnectionNames)
            ?? throw new InvalidOperationException("V hlavním okně nebylo nalezeno tlačítko Upravit připojení.");
        Invoke(button);
        return WaitFor(FindSettingsWindow, TimeSpan.FromSeconds(6))
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

    private static AutomationElement? FindCertificateOrWarningWindow() => FindTopWindow(e =>
    {
        var name = SafeName(e);
        if (!name.Contains("Synology", StringComparison.OrdinalIgnoreCase) && !name.Contains("cert", StringComparison.OrdinalIgnoreCase)) return false;
        var allText = string.Join(" ", e.FindAll(TreeScope.Descendants, Condition.TrueCondition).Cast<AutomationElement>().Select(SafeName));
        return new[] { "certifik", "certificate", "varování", "warning", "nedůvěryhod" }
            .Any(w => allText.Contains(w, StringComparison.OrdinalIgnoreCase));
    });

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

    private static T? WaitFor<T>(Func<T?> probe, TimeSpan timeout) where T : class
    {
        var until = DateTime.UtcNow + timeout;
        do { var value = probe(); if (value is not null) return value; Thread.Sleep(150); } while (DateTime.UtcNow < until);
        return null;
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
