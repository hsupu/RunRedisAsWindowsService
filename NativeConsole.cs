using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using Microsoft.Extensions.Logging;

namespace RedisService;

/// <summary>
/// Sends a console Ctrl-C to a child process so it can shut down the way it would on SIGINT (redis persists and exits cleanly).
/// A Windows service has no console of its own, so we temporarily attach to the child's console, tell our own process to ignore the event, broadcast it, then detach.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class NativeConsole
{
    private const uint CTRL_C_EVENT = 0;

    private delegate bool HandlerRoutine(uint dwCtrlType);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachConsole(uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FreeConsole();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GenerateConsoleCtrlEvent(uint dwCtrlEvent, uint dwProcessGroupId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetConsoleCtrlHandler(HandlerRoutine? handlerRoutine, [MarshalAs(UnmanagedType.Bool)] bool add);

    /// <summary>
    /// Attempts to deliver Ctrl-C to the process tree attached to <paramref name="processId"/>'s console.
    /// Returns false (so the caller can fall back to a hard kill) when the child has no console to attach to.
    /// </summary>
    public static bool TrySendCtrlC(int processId, ILogger logger)
    {
        // Detach from any console we might already own so we can borrow the child's.
        FreeConsole();

        if (!AttachConsole((uint)processId))
        {
            logger.LogDebug("AttachConsole({Pid}) failed (win32 error {Error}); graceful stop unavailable.", processId, Marshal.GetLastWin32Error());
            return false;
        }

        try
        {
            // Ignore the Ctrl-C in our own process; passing group 0 broadcasts to everything on this console, including us.
            SetConsoleCtrlHandler(null, add: true);
            return GenerateConsoleCtrlEvent(CTRL_C_EVENT, 0);
        }
        finally
        {
            FreeConsole();
            SetConsoleCtrlHandler(null, add: false);
        }
    }
}
