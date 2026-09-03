using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using SecScoreLauncher.Services;

namespace SecScoreLauncher.Win32;

/// <summary>
///     Opens or focuses the SecScore main window. Windows-focused: the SecScore window is located by its
///     native title ("SecScore") and raised to the foreground; if it is not running the executable is
///     launched. On non-Windows platforms this is a logged no-op.
/// </summary>
public static class SecScoreWindowFocus
{
    private const string WindowTitle = "SecScore";
    private const string ProcessName = "SecScore";
    private const int SwRestore = 9;
    private const int SwShow = 5;

    public static void OpenOrFocus(SecScoreLauncherConfig config, ILogger logger)
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
            // 1) A window titled "SecScore" already exists (visible or hidden-to-tray) -> focus it.
            var hwnd = NativeMethods.FindWindowW(null, WindowTitle);
            if (hwnd != IntPtr.Zero)
            {
                logger.LogInformation("Found SecScore window (0x{X}), raising to foreground.", hwnd.ToString("X"));
                ForceForeground(hwnd);
                return;
            }

            // 2) No titled window, but the process is running (e.g. window not yet created) -> focus the
            //    process main window, and remember its executable path as an auto-detect hint.
            var running = Process.GetProcessesByName(ProcessName).FirstOrDefault();
            if (running is not null)
            {
                TryRememberExecutable(running, config, logger);
                var handle = running.MainWindowHandle;
                if (handle != IntPtr.Zero)
                {
                    ForceForeground(handle);
                    return;
                }

                if (WaitForMainWindow(running, out handle))
                {
                    ForceForeground(handle);
                    return;
                }

                logger.LogInformation("SecScore is running but has no focusable window yet.");
                return;
            }

            // 3) Not running -> launch it.
            LaunchSecScore(config, logger);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to open or focus the SecScore window.");
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

        logger.LogInformation("Launching SecScore from {Path}.", exe);
        var startInfo = new ProcessStartInfo(exe)
        {
            WorkingDirectory = System.IO.Path.GetDirectoryName(exe) ?? string.Empty,
            UseShellExecute = true
        };
        Process.Start(startInfo);
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
            // MainModule can throw on permission-restricted processes; not fatal.
            logger.LogDebug(exception, "Could not read the SecScore process executable path.");
        }
    }

    private static bool WaitForMainWindow(Process process, out IntPtr handle)
    {
        // Give a just-started process a moment to create its first window.
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

    /// <summary>
    ///     Raises a window to the foreground even from a non-focused process, using the standard
    ///     AttachThreadInput workaround that Windows focus rules would otherwise reject with
    ///     <see cref="SetForegroundWindow"/>.
    /// </summary>
    private static void ForceForeground(IntPtr hwnd)
    {
        NativeMethods.ShowWindow(hwnd, SwRestore); // also restores a minimized window
        NativeMethods.ShowWindow(hwnd, SwShow);

        var foreground = NativeMethods.GetForegroundWindow();
        var foregroundThread = NativeMethods.GetWindowThreadProcessId(foreground, out _);
        var thisThread = NativeMethods.GetCurrentThreadId();
        var targetThread = NativeMethods.GetWindowThreadProcessId(hwnd, out _);

        if (foregroundThread != thisThread)
            NativeMethods.AttachThreadInput(foregroundThread, thisThread, true);
        if (targetThread != thisThread)
            NativeMethods.AttachThreadInput(targetThread, thisThread, true);

        NativeMethods.BringWindowToTop(hwnd);
        NativeMethods.SetForegroundWindow(hwnd);

        if (targetThread != thisThread)
            NativeMethods.AttachThreadInput(targetThread, thisThread, false);
        if (foregroundThread != thisThread)
            NativeMethods.AttachThreadInput(foregroundThread, thisThread, false);
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr FindWindowW(string? lpClassName, string? lpWindowName);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("kernel32.dll")]
        public static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, [MarshalAs(UnmanagedType.Bool)] bool fAttach);
    }
}
