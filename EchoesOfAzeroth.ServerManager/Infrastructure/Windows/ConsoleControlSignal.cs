using System.ComponentModel;
using System.Runtime.InteropServices;

namespace EchoesOfAzeroth.ServerManager.Infrastructure.Windows;

internal static class ConsoleControlSignal
{
    private const uint CtrlBreakEvent = 1;
    private static readonly object Sync = new();

    public static bool TrySendCtrlBreak(int processId, out string? error)
    {
        lock (Sync)
        {
            error = null;
            FreeConsole();

            if (!AttachConsole((uint)processId))
            {
                error = new Win32Exception(Marshal.GetLastWin32Error()).Message;
                return false;
            }

            try
            {
                if (!SetConsoleCtrlHandler(nint.Zero, true))
                {
                    error = new Win32Exception(Marshal.GetLastWin32Error()).Message;
                    return false;
                }

                if (!GenerateConsoleCtrlEvent(CtrlBreakEvent, 0))
                {
                    error = new Win32Exception(Marshal.GetLastWin32Error()).Message;
                    return false;
                }

                return true;
            }
            finally
            {
                FreeConsole();
                SetConsoleCtrlHandler(nint.Zero, false);
            }
        }
    }

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
    private static extern bool SetConsoleCtrlHandler(nint handlerRoutine, [MarshalAs(UnmanagedType.Bool)] bool add);
}
