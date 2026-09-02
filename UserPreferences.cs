using Microsoft.Win32;

namespace RKSwitch.SynDrvCl;

internal static class UserPreferences
{
    private const string KeyPath = @"Software\RK-Switch-SynDrvCl";
    private const string AutomaticSwitchingValue = "AutomaticSwitching";

    public static bool AutomaticSwitchingEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: false);
            return key?.GetValue(AutomaticSwitchingValue) is int value && value == 1;
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(KeyPath, writable: true)
                ?? throw new InvalidOperationException("Nelze otevřít uživatelské nastavení RK-Switch.");
            key.SetValue(AutomaticSwitchingValue, value ? 1 : 0, RegistryValueKind.DWord);
        }
    }
}
