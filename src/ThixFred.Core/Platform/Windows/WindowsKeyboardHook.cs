using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using ThixFred.Core.Infrastructure;

namespace ThixFred.Core.Platform.Windows;

/// <summary>
/// The global shortcut on Windows: a low-level keyboard hook on its own thread with a message loop. It sees a key before
/// Windows and every other program does, so ThixFred's shortcut wins even over combinations the shell or other programs
/// have taken (Alt+Space opens the window menu of the app in front): the key press is swallowed and never reaches anyone else.
/// When the combination contains the Windows or the Alt key, an unassigned key is sent in between, so releasing them does not open the
/// Start menu or the menu bar of the app in front (the same trick AutoHotkey uses).
/// The hook procedure only posts: Windows drops a hook that takes too long.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class WindowsKeyboardHook
{
    private static readonly object Gate = new();
    private static readonly ManualResetEventSlim Ready = new();
    private static Thread? _thread;
    private static uint _threadId;
    private static IntPtr _hook;

    private static Win32.LowLevelKeyboardProc? _callback;

    private static volatile HookRegistration? _registration;
    private static volatile bool _suspended;
    private static uint _swallowedKey;

    /// <summary>Watches for the combination; <paramref name="pressed"/> runs on a thread-pool thread.</summary>
    public static void Register(int virtualKey, bool control, bool alt, bool shift, bool win, Action pressed)
    {
        EnsureStarted();
        _registration = new HookRegistration((uint)virtualKey, control, alt, shift, win, pressed);
    }

    public static void Unregister() => _registration = null;

    /// <summary>While the settings record a new shortcut the old one must not fire.</summary>
    public static void Suspend(bool suspended) => _suspended = suspended;

    public static void Stop()
    {
        lock (Gate)
        {
            if (_thread is null)
            {
                return;
            }

            Win32.PostThreadMessage(_threadId, Win32.WmQuit, IntPtr.Zero, IntPtr.Zero);
            _thread.Join(TimeSpan.FromSeconds(2));
            _thread = null;
            Ready.Reset();
        }
    }

    private static void EnsureStarted()
    {
        lock (Gate)
        {
            if (_thread is null)
            {
                _thread = new Thread(Run) { IsBackground = true, Name = "ThixFred keyboard hook" };
                _thread.Start();
            }
        }

        Ready.Wait(TimeSpan.FromSeconds(5));
    }

    private static void Run()
    {
        _threadId = Win32.GetCurrentThreadId();
        _callback = HookProcedure;
        _hook = Win32.SetWindowsHookEx(Win32.WhKeyboardLl, Marshal.GetFunctionPointerForDelegate(_callback), Win32.GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero)
        {
            Log.Error($"The keyboard hook could not be installed (error {Marshal.GetLastWin32Error()})");
        }

        Ready.Set();

        while (Win32.GetMessage(out _, IntPtr.Zero, 0, 0) > 0)
        {
            // The hook needs a message loop on its thread; there is nothing else to dispatch.
        }

        if (_hook != IntPtr.Zero)
        {
            Win32.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }

    private static IntPtr HookProcedure(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code < 0)
        {
            return Win32.CallNextHookEx(_hook, code, wParam, lParam);
        }

        var data = Marshal.PtrToStructure<Win32.KbdLlHookStruct>(lParam);
        var message = (int)wParam;
        var isDown = message is Win32.WmKeyDown or Win32.WmSysKeyDown;
        var isUp = message is Win32.WmKeyUp or Win32.WmSysKeyUp;

        // The release of a swallowed key is swallowed too, so no program sees half a key press.
        if (isUp && _swallowedKey != 0 && data.VkCode == _swallowedKey)
        {
            _swallowedKey = 0;
            return 1;
        }

        var registration = _registration;
        // Keys ThixFred sends itself are marked; keys of other tools (remapping, automation) count like typed ones.
        if (!isDown || registration is null || _suspended || data.ExtraInfo == Win32.OwnInputMarker || data.VkCode != registration.VirtualKey)
        {
            return Win32.CallNextHookEx(_hook, code, wParam, lParam);
        }

        if (!registration.Matches())
        {
            return Win32.CallNextHookEx(_hook, code, wParam, lParam);
        }

        if (_swallowedKey == data.VkCode)
        {
            return 1; // auto-repeat while the key is held: fire once
        }

        _swallowedKey = data.VkCode;
        // Releasing Win alone opens the Start menu, releasing Alt alone the menu bar of the app in front (Alt+Space is the
        // shortcut of most launchers): a key in between makes it a combination again.
        if (registration.Win || registration.Alt)
        {
            SendMaskKey();
        }

        ThreadPool.QueueUserWorkItem(_ => registration.Pressed());
        return 1;
    }

    private static void SendMaskKey()
    {
        var inputs = new[]
        {
            new Win32.Input { Type = Win32.InputKeyboard, Data = new Win32.InputUnion { Keyboard = new Win32.KeyboardInput { Vk = Win32.VkMask, ExtraInfo = Win32.OwnInputMarker } } },
            new Win32.Input { Type = Win32.InputKeyboard, Data = new Win32.InputUnion { Keyboard = new Win32.KeyboardInput { Vk = Win32.VkMask, Flags = Win32.KeyEventFKeyUp, ExtraInfo = Win32.OwnInputMarker } } },
        };
        Win32.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Win32.Input>());
    }

    private sealed record HookRegistration(uint VirtualKey, bool Control, bool Alt, bool Shift, bool Win, Action Pressed)
    {
        /// <summary>Exactly these modifiers: Ctrl+Win+V does not fire on Ctrl+Shift+Win+V.</summary>
        public bool Matches() =>
            Win32.IsKeyDown(Win32.VkControl) == Control &&
            Win32.IsKeyDown(Win32.VkMenu) == Alt &&
            Win32.IsKeyDown(Win32.VkShift) == Shift &&
            (Win32.IsKeyDown(Win32.VkLWin) || Win32.IsKeyDown(Win32.VkRWin)) == Win;
    }
}
