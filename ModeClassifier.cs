namespace RKSwitch.SynDrvCl;

internal static class ModeClassifier
{
    public static string DisplayName(string? server)
    {
        var value = server?.Trim();
        if (string.Equals(value, AppConfig.CompanyAddress, StringComparison.OrdinalIgnoreCase))
            return $"FIRMA ({AppConfig.CompanyAddress})";
        if (string.Equals(value, AppConfig.QuickConnectId, StringComparison.OrdinalIgnoreCase))
            return $"MIMO FIRMU ({AppConfig.QuickConnectId})";
        return string.IsNullOrWhiteSpace(value) ? "neznámý" : $"jiný server ({value})";
    }

    public static string NormalizeMac(string? mac) =>
        new((mac ?? "").Where(Uri.IsHexDigit).Select(char.ToUpperInvariant).ToArray());
}
