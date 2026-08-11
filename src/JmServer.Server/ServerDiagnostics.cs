using System.Text;

namespace JmServer.Server;

internal static class ServerDiagnostics
{
    private const long MaximumLogLength = 4 * 1024 * 1024;
    private static readonly object Gate = new();

    public static string LogDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "JM Server",
        "logs");

    public static void WriteException(
        Exception exception,
        JmServer.Contracts.MessageType messageType,
        string? sessionId)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(LogDirectory);
                var currentPath = Path.Combine(LogDirectory, "server.log");
                if (File.Exists(currentPath) && new FileInfo(currentPath).Length >= MaximumLogLength)
                {
                    var previousPath = Path.Combine(LogDirectory, "server.previous.log");
                    File.Delete(previousPath);
                    File.Move(currentPath, previousPath);
                }

                var line = new StringBuilder()
                    .Append(DateTimeOffset.UtcNow.ToString("O"))
                    .Append(" | ")
                    .Append(messageType)
                    .Append(" | session=")
                    .Append(sessionId ?? "unknown")
                    .AppendLine()
                    .AppendLine(exception.ToString())
                    .AppendLine("---")
                    .ToString();
                File.AppendAllText(currentPath, line, Encoding.UTF8);
            }
        }
        catch
        {
            // Diagnostics must never replace the original server error.
        }
    }
}
