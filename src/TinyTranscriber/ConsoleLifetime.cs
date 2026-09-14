using System.ComponentModel;
using System.Runtime.InteropServices;

namespace TinyTranscriber;

internal sealed class ConsoleLifetime : IDisposable
{
    private const uint AttachParentProcess = 0xFFFFFFFF;
    private const uint CtrlCEvent = 0;
    private const uint CtrlBreakEvent = 1;
    private const int ErrorAccessDenied = 5;
    private readonly HandlerRoutine? handler;
    private readonly bool attachedConsole;
    private int exitRequested;

    private ConsoleLifetime(Action requestExit)
    {
        if (AttachConsole(AttachParentProcess))
        {
            attachedConsole = true;
        }
        else if (Marshal.GetLastWin32Error() != ErrorAccessDenied)
        {
            return;
        }

        handler = controlType =>
        {
            if (controlType is not CtrlCEvent and not CtrlBreakEvent)
            {
                return false;
            }

            if (Interlocked.Exchange(ref exitRequested, 1) == 0)
            {
                requestExit();
            }

            return true;
        };

        if (!SetConsoleCtrlHandler(handler, true))
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "Could not register the console cancellation handler.");
        }
    }

    public static ConsoleLifetime AttachToParent(Action requestExit)
    {
        return new ConsoleLifetime(requestExit);
    }

    public void Dispose()
    {
        if (handler is not null)
        {
            SetConsoleCtrlHandler(handler, false);
        }

        if (attachedConsole)
        {
            FreeConsole();
        }
    }

    private delegate bool HandlerRoutine(uint controlType);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeConsole();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleCtrlHandler(HandlerRoutine handlerRoutine, bool add);
}
