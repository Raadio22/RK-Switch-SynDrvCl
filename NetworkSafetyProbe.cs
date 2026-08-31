using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace RKSwitch.SynDrvCl;

internal sealed class NetworkSafetyProbe
{
    public async Task VerifyCompanyNasAsync(CancellationToken cancellationToken = default)
    {
        var address = IPAddress.Parse(AppConfig.CompanyAddress);
        if (address.AddressFamily != AddressFamily.InterNetwork)
            throw new InvalidOperationException("Místní adresa NAS není IPv4.");

        using var client = new TcpClient(AddressFamily.InterNetwork);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            await client.ConnectAsync(address, AppConfig.SynologyDrivePort, timeout.Token);
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException)
        {
            throw new InvalidOperationException($"NAS neodpovídá na {AppConfig.CompanyAddress}:{AppConfig.SynologyDrivePort}. Přepnutí na FIRMA bylo zablokováno.");
        }

        var actualMac = ResolveMac(address);
        if (!string.Equals(ModeClassifier.NormalizeMac(actualMac), ModeClassifier.NormalizeMac(AppConfig.NasMac), StringComparison.Ordinal))
            throw new InvalidOperationException($"MAC zařízení na {AppConfig.CompanyAddress} je {actualMac}, očekávána {AppConfig.NasMac}. Přepnutí bylo zablokováno.");
    }

    private static string ResolveMac(IPAddress address)
    {
        var destination = BitConverter.ToInt32(address.GetAddressBytes(), 0);
        var bytes = new byte[6];
        var length = bytes.Length;
        var result = SendARP(destination, 0, bytes, ref length);
        if (result != 0 || length <= 0)
            throw new InvalidOperationException("Nelze bezpečně ověřit MAC místního NAS pomocí ARP. Přepnutí na FIRMA bylo zablokováno.");
        return string.Join("-", bytes.Take(length).Select(b => b.ToString("X2")));
    }

    [DllImport("iphlpapi.dll", ExactSpelling = true)]
    private static extern int SendARP(int destinationIp, int sourceIp, byte[] macAddress, ref int physicalAddressLength);
}
