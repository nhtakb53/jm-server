using System.Diagnostics;

namespace JmServer.GameIntegration;

public static class D2RGameRunner
{
    public static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(40);

    public static async Task<int> RunAsync(
        string gameDirectory,
        Func<CancellationToken, Task> heartbeat,
        Action<string>? warning = null,
        CancellationToken cancellationToken = default,
        D2RDisplaySettings? displaySettings = null)
    {
        ArgumentNullException.ThrowIfNull(heartbeat);
        D2RLoaderInstaller.EnsurePrivateSessionProcessesAreStopped();
        await D2RSettingsSession.RecoverInterruptedAsync(
            cancellationToken: cancellationToken);
        await D2RModSettings.EnsureInitializedAsync(
            cancellationToken: cancellationToken,
            displaySettings: displaySettings);
        var settingsSession = await D2RSettingsSession.BeginAsync(
            cancellationToken: cancellationToken);

        try
        {
            gameDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(gameDirectory));
            var loaderPath = Path.Combine(gameDirectory, "D2RLoader.exe");
            if (!File.Exists(loaderPath))
            {
                throw new FileNotFoundException("D2RLoader.exe is not installed.", loaderPath);
            }

            using var loader = Process.Start(new ProcessStartInfo
            {
                FileName = loaderPath,
                WorkingDirectory = gameDirectory,
                UseShellExecute = false
            }) ?? throw new InvalidOperationException("D2RLoader did not start.");

            var heartbeatDue = DateTimeOffset.UtcNow.Add(HeartbeatInterval);
            DateTimeOffset? allProcessesExitedAt = null;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var gameProcessRunning = IsGameProcessRunning();
                if (!loader.HasExited || gameProcessRunning)
                {
                    allProcessesExitedAt = null;
                }
                else
                {
                    allProcessesExitedAt ??= DateTimeOffset.UtcNow;
                    if (DateTimeOffset.UtcNow - allProcessesExitedAt >= TimeSpan.FromSeconds(5))
                    {
                        return loader.ExitCode;
                    }
                }

                if (DateTimeOffset.UtcNow >= heartbeatDue)
                {
                    try
                    {
                        await heartbeat(cancellationToken);
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        warning?.Invoke(
                            "Lease heartbeat failed; the local save is being kept for recovery. " +
                            exception.Message);
                    }

                    heartbeatDue = DateTimeOffset.UtcNow.Add(HeartbeatInterval);
                }

                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
        }
        finally
        {
            if (!IsGameProcessRunning())
            {
                await settingsSession.CompleteAsync(CancellationToken.None);
            }
        }
    }

    private static bool IsGameProcessRunning()
    {
        foreach (var processName in new[] { "D2R", "D2RLoader" })
        {
            var processes = Process.GetProcessesByName(processName);
            try
            {
                if (processes.Length != 0)
                {
                    return true;
                }
            }
            finally
            {
                foreach (var process in processes)
                {
                    process.Dispose();
                }
            }
        }

        return false;
    }
}
