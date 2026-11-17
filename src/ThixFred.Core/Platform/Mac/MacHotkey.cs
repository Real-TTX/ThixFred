using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using ThixFred.Core.Infrastructure;

namespace ThixFred.Core.Platform.Mac;

/// <summary>
/// The global shortcut on macOS: a Carbon hot key (RegisterEventHotKey). It needs no accessibility permission, the system
/// hands the key press to ThixFred alone, and it works in every app. It has to be registered on the main thread (the UI
/// thread of Avalonia); the handler is called there too.
/// </summary>
[SupportedOSPlatform("macos")]
internal static class MacHotkey
{
    private const string Carbon = "/System/Library/Frameworks/Carbon.framework/Carbon";
    private const uint KeyboardEventClass = 0x6B657962; // 'keyb'
    private const uint HotKeyPressedKind = 5; // kEventHotKeyPressed
    private const uint Signature = 0x54434C50; // 'TCLP'

    public const uint CommandModifier = 0x0100;
    public const uint ShiftModifier = 0x0200;
    public const uint OptionModifier = 0x0800;
    public const uint ControlModifier = 0x1000;

    private static IntPtr _handlerRef;
    private static IntPtr _hotKeyRef;
    private static EventHandlerProc? _handler;
    private static Action? _pressed;
    private static (uint KeyCode, uint Modifiers)? _registered;

    private delegate int EventHandlerProc(IntPtr callRef, IntPtr eventRef, IntPtr userData);

    [StructLayout(LayoutKind.Sequential)]
    private struct EventTypeSpec
    {
        public uint EventClass;
        public uint EventKind;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct EventHotKeyId
    {
        public uint Signature;
        public uint Id;
    }

    [DllImport(Carbon)]
    private static extern IntPtr GetApplicationEventTarget();

    [DllImport(Carbon)]
    private static extern int InstallEventHandler(IntPtr target, IntPtr handler, uint numberOfTypes, EventTypeSpec[] types, IntPtr userData, out IntPtr handlerRef);

    [DllImport(Carbon)]
    private static extern int RegisterEventHotKey(uint keyCode, uint modifiers, EventHotKeyId id, IntPtr target, uint options, out IntPtr hotKeyRef);

    [DllImport(Carbon)]
    private static extern int UnregisterEventHotKey(IntPtr hotKeyRef);

    /// <summary>Registers the shortcut (main thread); <paramref name="pressed"/> runs on the main thread.</summary>
    public static bool Register(uint keyCode, uint modifiers, Action pressed)
    {
        Unregister();
        _pressed = pressed;
        if (_handlerRef == IntPtr.Zero)
        {
            _handler = OnHotKey;
            var types = new[] { new EventTypeSpec { EventClass = KeyboardEventClass, EventKind = HotKeyPressedKind } };
            var status = InstallEventHandler(GetApplicationEventTarget(), Marshal.GetFunctionPointerForDelegate(_handler), 1, types, IntPtr.Zero, out _handlerRef);
            if (status != 0)
            {
                Log.Error($"InstallEventHandler failed ({status})");
                return false;
            }
        }

        var result = RegisterEventHotKey(keyCode, modifiers, new EventHotKeyId { Signature = Signature, Id = 1 }, GetApplicationEventTarget(), 0, out _hotKeyRef);
        if (result != 0)
        {
            Log.Error($"RegisterEventHotKey failed ({result}): the shortcut is taken by another app");
            _hotKeyRef = IntPtr.Zero;
            return false;
        }

        _registered = (keyCode, modifiers);
        return true;
    }

    public static void Unregister()
    {
        if (_hotKeyRef != IntPtr.Zero)
        {
            UnregisterEventHotKey(_hotKeyRef);
            _hotKeyRef = IntPtr.Zero;
        }
    }

    /// <summary>While the settings record a new shortcut the old one must not fire.</summary>
    public static void Suspend(bool suspended)
    {
        if (suspended)
        {
            Unregister();
        }
        else if (_registered is { } registered && _pressed is { } pressed)
        {
            Register(registered.KeyCode, registered.Modifiers, pressed);
        }
    }

    private static int OnHotKey(IntPtr callRef, IntPtr eventRef, IntPtr userData)
    {
        try
        {
            _pressed?.Invoke();
        }
        catch (Exception exception)
        {
            Log.Error("Hot key handler failed", exception);
        }

        return 0; // noErr: handled
    }
}
