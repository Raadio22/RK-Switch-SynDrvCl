namespace RKSwitch.SynDrvCl;

internal static class AutomaticSwitchPolicy
{
    public static ConnectionMode DesiredMode(bool companyNasAvailable) =>
        companyNasAvailable ? ConnectionMode.Company : ConnectionMode.Remote;

    public static bool RequiresSwitch(string? currentServer, ConnectionMode desiredMode)
    {
        var expected = desiredMode == ConnectionMode.Company
            ? AppConfig.CompanyAddress
            : AppConfig.QuickConnectId;
        return !string.Equals(currentServer?.Trim(), expected, StringComparison.OrdinalIgnoreCase);
    }
}
