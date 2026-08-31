using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace RKSwitch.SynDrvCl;

internal sealed class SynologyAutomationService
{
    private static readonly string[] EditConnectionNames = ["Upravit připojení", "Edit Connection"];
    private static readonly string[] ServerFieldNames = ["Adresa serveru", "Server address"];
    private static readonly string[] SettingsTitles = ["Nastavení", "Settings"];
    private static readonly string[] OkNames = ["OK"];
    private static readonly string[] CancelNames = ["Storno", "Cancel"];
    private readonly NetworkSafetyProbe _networkProbe = new();

    public OperationResult Inspect()
    {
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
            return new(current, $"Server z oficiálního nastavení: {current ?? "nelze přečíst"}. Nebyla provedena žádná změna.");
        }
        finally
        {
            if (openedByUs) InvokeNamedButton(settings, CancelNames);
        }
    }

    public OperationResult Switch(ConnectionMode mode, bool dryRun)
    {
        if (mode == ConnectionMode.Company)
            _networkProbe.VerifyCompanyNasAsync().GetAwaiter().GetResult();

        var target = mode == ConnectionMode.Company ? AppConfig.CompanyAddress : AppConfig.QuickConnectId;
        var main = FindMainWindow(startIfMissing: true)
            ?? throw new InvalidOperationException("Nelze nalézt hlavní okno Synology Drive Client.");
        var settings = FindSettingsWindow();
        var openedByUs = settings is null;
        settings ??= OpenSettings(main);
        var current = ReadServer(settings);

        if (dryRun)
        {
            if (openedByUs) InvokeNamedButton(settings, CancelNames);
            var probe = mode == ConnectionMode.Company ? " TCP 6690 i MAC NAS byly ověřeny." : "";
            var dialogText = openedByUs ? " Dialog byl ukončen přes Storno;" : " Již otevřený dialog zůstal beze změny;";
            return new(current, $"Zkušební režim: cíl {target} je připraven.{probe}{dialogText} nic se nezměnilo.");
        }

        if (string.Equals(current?.Trim(), target, StringComparison.OrdinalIgnoreCase))
        {
            if (openedByUs) InvokeNamedButton(settings, CancelNames);
            return new(current, $"Synology Drive již používá {target}; nebyla provedena změna.");
        }

        SetServer(settings, target);
        InvokeNamedButton(settings, OkNames);

        Thread.Sleep(1200);
        var warning = FindCertificateOrWarningWindow();
        if (warning is not null)
            return new(target, "Synology Drive zobrazil varování nebo potvrzovací dialog. Aplikace s ním záměrně nemanipulovala; zkontrolujte jej ručně.", true);

        if (FindSettingsWindow() is not null)
            return new(target, "Oficiální dialog Synology Drive zůstal po stisku OK otevřený. Může vyžadovat heslo, opravu údaje nebo jiné ruční rozhodnutí; aplikace už nic dalšího neprovedla.", true);

        return new(target, $"Adresa byla předána oficiálnímu nastavení Synology Drive: {target}. Heslo, SSL ani synchronizační úlohy nebyly měněny.");
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
        var target = SupportsValue(field) ? field :
            field.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));
        if (target is null || !target.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern))
            throw new InvalidOperationException("Pole Adresa serveru nepodporuje bezpečný zápis přes Windows UI Automation.");
        ((ValuePattern)pattern).SetValue(value);
        if (!string.Equals(ReadValue(target)?.Trim(), value, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Synology Drive nepotvrdil zapsání cílové adresy; tlačítko OK nebylo stisknuto.");
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

    private static T? WaitFor<T>(Func<T?> probe, TimeSpan timeout) where T : class
    {
        var until = DateTime.UtcNow + timeout;
        do { var value = probe(); if (value is not null) return value; Thread.Sleep(150); } while (DateTime.UtcNow < until);
        return null;
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
