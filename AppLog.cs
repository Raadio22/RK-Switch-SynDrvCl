using System.Globalization;
using System.IO;

namespace RKSwitch.SynDrvCl;

internal static class AppLog
{
    private static readonly object Gate = new();
    public static string DirectoryPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RK-Switch-SynDrvCl", "logs");
    public static string FilePath { get; } = Path.Combine(DirectoryPath, "rk-switch.log");

    public static void Info(string message) => Write("INFO", message);
    public static void Error(string message, Exception exception) =>
        Write("ERROR", $"{message} | {exception.GetType().Name}: {exception.Message}");

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(DirectoryPath);
                File.AppendAllText(FilePath,
                    $"{DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture)} [{level}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging must never block the connection workflow.
        }
    }
}
