namespace RKSwitch.SynDrvCl;

internal static class AppConfig
{
    public const string CompanyAddress = "192.168.1.2";
    public const int SynologyDrivePort = 6690;
    public const string NasMac = "90-09-D0-90-16-7D";
    public const string QuickConnectId = "NAS-ZemOlsar";
}

internal enum ConnectionMode { Company, Remote }
internal enum TransferState { Unknown, Idle, Active }

internal sealed record OperationResult(string? CurrentServer, string Message, bool NeedsUserAttention = false);
