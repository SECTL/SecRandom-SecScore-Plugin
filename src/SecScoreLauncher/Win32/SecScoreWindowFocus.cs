using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SecScoreLauncher.Services;

namespace SecScoreLauncher.Win32;

/// <summary>
///     Opens or focuses SecScore through its REST API, falling back to its process or executable path.
///     On non-Windows platforms this is a logged no-op.
/// </summary>
public static class SecScoreWindowFocus
{
    private const string ProcessName = "SecScore";

    public static async Task OpenOrFocusAsync(
        SecScoreLauncherConfig config,
        HttpClient httpClient,
        ILogger logger)
    {
        if (!OperatingSystem.IsWindows())
        {
            logger.LogWarning(
                "SecScore launcher only supports Windows on this build (OS: {OS}).",
                Environment.OSVersion.Platform);
            return;
        }

        try
        {
            if (await TryShowThroughApiAsync(config, httpClient, logger).ConfigureAwait(false))
                return;

            var configuredPath = config.ExePath.Trim();
            if (configuredPath.Length > 0)
            {
                var configuredExe = config.ResolveExecutable();
                if (configuredExe is null)
                {
                    logger.LogWarning("Configured SecScore executable does not exist: {Path}.", configuredPath);
                    return;
                }

                if (TryFocusProcessAtPath(configuredExe))
                    return;

                LaunchSecScore(configuredExe, logger);
                return;
            }

            var running = Process.GetProcessesByName(ProcessName).FirstOrDefault();
            if (running is not null)
            {
                using (running)
                {
                    TryRememberExecutable(running, config, logger);
                    FocusProcess(running);
                }
                return;
            }

            LaunchSecScore(config, logger);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to open or focus the SecScore window.");
        }
    }

    private static async Task<bool> TryShowThroughApiAsync(
        SecScoreLauncherConfig config,
        HttpClient httpClient,
        ILogger logger)
    {
        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"{config.SecScoreApiUrl.TrimEnd('/')}/api/v1/window/show");
            var token = config.SecScoreApiToken.Trim();
            if (!string.IsNullOrWhiteSpace(token))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            using var response = await httpClient.SendAsync(request, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogDebug("SecScore window API returned HTTP {StatusCode}; using process fallback.", response.StatusCode);
                return false;
            }

            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false));
            var succeeded = body.RootElement.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True;
            if (succeeded)
                logger.LogInformation("Displayed SecScore main window through REST API.");
            return succeeded;
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "SecScore window API is unavailable; using process fallback.");
            return false;
        }
    }

    private static void LaunchSecScore(SecScoreLauncherConfig config, ILogger logger)
    {
        var exe = config.ResolveExecutable();
        if (exe is null)
        {
            logger.LogWarning(
                "SecScore is not running and the executable path could not be auto-detected. " +
                "Set the path in Settings -> SecScore.");
            return;
        }

        LaunchSecScore(exe, logger);
    }

    private static void LaunchSecScore(string exe, ILogger logger)
    {
        logger.LogInformation("Launching SecScore from {Path}.", exe);
        var startInfo = new ProcessStartInfo(exe)
        {
            WorkingDirectory = System.IO.Path.GetDirectoryName(exe) ?? string.Empty,
            UseShellExecute = true
        };
        Process.Start(startInfo);
    }

    private static bool TryFocusProcessAtPath(string exePath)
    {
        var expectedPath = System.IO.Path.GetFullPath(exePath);
        foreach (var process in Process.GetProcessesByName(ProcessName))
        {
            using (process)
            {
                try
                {
                    var runningPath = process.MainModule?.FileName;
                    if (runningPath is not null && string.Equals(
                            System.IO.Path.GetFullPath(runningPath),
                            expectedPath,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        FocusProcess(process);
                        return true;
                    }
                }
                catch
                {
                    // The process may exit while its executable path is being inspected.
                }
            }
        }

        return false;
    }

    private static void FocusProcess(Process process)
    {
        var handle = process.MainWindowHandle;
        if (handle != IntPtr.Zero)
        {
            RaiseWindow(handle);
            return;
        }

        if (WaitForMainWindow(process, out handle))
            RaiseWindow(handle);
    }

    private static void TryRememberExecutable(Process process, SecScoreLauncherConfig config, ILogger logger)
    {
        try
        {
            var path = process.MainModule?.FileName;
            if (!string.IsNullOrWhiteSpace(path))
            {
                var set = config.ExePath.Trim();
                if (set.Length == 0 || !string.Equals(set, path, StringComparison.OrdinalIgnoreCase))
                {
                    config.ExePath = path;
                    config.Save();
                    logger.LogInformation("Auto-detected SecScore executable at {Path}.", path);
                }
            }
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "Could not read the SecScore process executable path.");
        }
    }

    private static bool WaitForMainWindow(Process process, out IntPtr handle)
    {
        handle = IntPtr.Zero;
        for (var i = 0; i < 10; i++)
        {
            Thread.Sleep(150);
            process.Refresh();
            if (process.MainWindowHandle != IntPtr.Zero)
            {
                handle = process.MainWindowHandle;
                return true;
            }
        }

        return false;
    }

    private static void RaiseWindow(IntPtr hwnd)
    {
        NativeMethods.ShowWindow(hwnd, 9);
        NativeMethods.ShowWindow(hwnd, 5);
        NativeMethods.SetForegroundWindow(hwnd);
    }

    private static class NativeMethods
    {
        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        public static extern bool SetForegroundWindow(IntPtr hWnd);
    }
}
