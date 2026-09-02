using RKSwitch.SynDrvCl;

static void Equal(string expected, string actual, string name)
{
    if (!string.Equals(expected, actual, StringComparison.Ordinal))
        throw new Exception($"{name}: očekáváno '{expected}', získáno '{actual}'");
}

Equal("FIRMA (192.168.1.2)", ModeClassifier.DisplayName(" 192.168.1.2 "), "Klasifikace FIRMA");
Equal("MIMO FIRMU (NAS-ZemOlsar)", ModeClassifier.DisplayName("nas-zemolsar"), "Klasifikace QuickConnect");
Equal("9009D090167D", ModeClassifier.NormalizeMac("90-09-d0-90-16-7d"), "Normalizace MAC");

if (AutomaticSwitchPolicy.DesiredMode(companyNasAvailable: true) != ConnectionMode.Company)
    throw new Exception("Automatika: dostupný firemní NAS musí zvolit režim FIRMA.");
if (AutomaticSwitchPolicy.DesiredMode(companyNasAvailable: false) != ConnectionMode.Remote)
    throw new Exception("Automatika: nedostupný firemní NAS musí zvolit režim MIMO FIRMU.");
if (!AutomaticSwitchPolicy.RequiresSwitch(AppConfig.QuickConnectId, ConnectionMode.Company))
    throw new Exception("Automatika: přechod z QuickConnect na FIRMA musí vyžadovat přepnutí.");
if (AutomaticSwitchPolicy.RequiresSwitch(AppConfig.CompanyAddress, ConnectionMode.Company))
    throw new Exception("Automatika: správně nastavená FIRMA se nesmí přepínat znovu.");

await new NetworkSafetyProbe().VerifyCompanyNasAsync();

Console.WriteLine("PASS: klasifikace režimů, automatická volba, normalizace MAC, TCP 192.168.1.2:6690 a ARP MAC.");
