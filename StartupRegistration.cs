using Microsoft.Win32;

namespace RKSwitch.SynDrvCl;

internal static class StartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "RK-Switch SynDrvCl";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        var registered = key?.GetValue(ValueName) as string;
        var expectedPath = Environment.ProcessPath;
        return !string.IsNullOrWhiteSpace(registered) && !string.IsNullOrWhiteSpace(expectedPath) &&
               registered.Contains(expectedPath, StringComparison.OrdinalIgnoreCase);
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException("Nelze otevřít uživatelské nastavení automatického spuštění.");
        if (!enabled)
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
            return;
        }

        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable))
            throw new InvalidOperationException("Nelze zjistit cestu k aplikaci.");
        key.SetValue(ValueName, $"\"{executable}\" --startup", RegistryValueKind.String);
    }
}
