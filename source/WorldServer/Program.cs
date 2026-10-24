using System;
using System.Runtime.InteropServices;
using WorldServer.core;

namespace WorldServer
{
    public sealed class Program
    {
        private static void Main(string[] args)
        {
            if (OperatingSystem.IsWindows())
            {
                // World threads sleep until the next tick (RootWorldThread), so ask Windows for 1 ms
                // timer precision: ticks then stay 100 ms apart instead of slipping by up to 15 ms.
                _ = timeBeginPeriod(1);
                DisableQuickEdit();
            }

            GcPauseLog.Start();
            new GameServer(args).Run();
        }

        // Clicking in a console window with Quick Edit on starts a selection, and every console
        // write then blocks until the selection ends, freezing the world thread that wrote.
        private static void DisableQuickEdit()
        {
            const uint ENABLE_QUICK_EDIT_MODE = 0x0040;
            const uint ENABLE_EXTENDED_FLAGS = 0x0080;
            var handle = GetStdHandle(-10); // STD_INPUT_HANDLE
            if (GetConsoleMode(handle, out var mode))
                _ = SetConsoleMode(handle, (mode & ~ENABLE_QUICK_EDIT_MODE) | ENABLE_EXTENDED_FLAGS);
        }

        [DllImport("winmm.dll")]
        private static extern uint timeBeginPeriod(uint period);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetStdHandle(int handle);

        [DllImport("kernel32.dll")]
        private static extern bool GetConsoleMode(IntPtr handle, out uint mode);

        [DllImport("kernel32.dll")]
        private static extern bool SetConsoleMode(IntPtr handle, uint mode);
    }
}
