namespace RKSwitch.SynDrvCl;

internal enum SynologyDialogKind
{
    None,
    QuickConnectOffer,
    UntrustedCertificate
}

internal static class SynologyDialogPolicy
{
    public static SynologyDialogKind Classify(string? dialogText)
    {
        if (string.IsNullOrWhiteSpace(dialogText)) return SynologyDialogKind.None;

        if (ContainsAll(dialogText, "quickconnect") &&
            (ContainsAny(dialogText, "chcete přejít", "switch to quickconnect", "change to quickconnect")))
            return SynologyDialogKind.QuickConnectOffer;

        if ((ContainsAll(dialogText, "certifikát", "není důvěryhodný") ||
             ContainsAll(dialogText, "certificate", "not trusted") ||
             ContainsAll(dialogText, "certificate", "untrusted")))
            return SynologyDialogKind.UntrustedCertificate;

        return SynologyDialogKind.None;
    }

    private static bool ContainsAll(string text, params string[] fragments) =>
        fragments.All(fragment => text.Contains(fragment, StringComparison.OrdinalIgnoreCase));

    private static bool ContainsAny(string text, params string[] fragments) =>
        fragments.Any(fragment => text.Contains(fragment, StringComparison.OrdinalIgnoreCase));
}
