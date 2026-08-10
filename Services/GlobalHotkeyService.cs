using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Sonja.ReadAloud.Models;

namespace Sonja.ReadAloud.Services;

public sealed class GlobalHotkeyService : IDisposable
{
    private const int HotkeyId = 0x534F;
    private const int WmHotkey = 0x0312;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;
    private const uint ModNoRepeat = 0x4000;

    private readonly IntPtr _windowHandle;
    private readonly HwndSource _source;
    private HotkeyDefinition? _registeredHotkey;
    private bool _disposed;

    public GlobalHotkeyService(Window window)
    {
        _windowHandle = new WindowInteropHelper(window).Handle;
        _source = HwndSource.FromHwnd(_windowHandle)
            ?? throw new InvalidOperationException("The settings window handle is unavailable.");
        _source.AddHook(WindowMessageHook);
    }

    public event EventHandler? HotkeyPressed;

    public OperationResult TryRegister(HotkeyDefinition hotkey)
    {
        if (!hotkey.IsValid)
        {
            return OperationResult.Fail("Choose a shortcut with at least one modifier key.");
        }

        var previous = _registeredHotkey?.Clone();
        Unregister();

        var modifiers = ModNoRepeat;
        if (hotkey.Alt) modifiers |= ModAlt;
        if (hotkey.Control) modifiers |= ModControl;
        if (hotkey.Shift) modifiers |= ModShift;
        if (hotkey.Windows) modifiers |= ModWin;

        var virtualKey = (uint)KeyInterop.VirtualKeyFromKey(hotkey.Key);
        if (RegisterHotKey(_windowHandle, HotkeyId, modifiers, virtualKey))
        {
            _registeredHotkey = hotkey.Clone();
            return OperationResult.Ok($"{hotkey.DisplayName} is active.");
        }

        var error = new Win32Exception(Marshal.GetLastWin32Error());
        if (previous is not null)
        {
            RegisterWithoutFallback(previous);
        }

        return OperationResult.Fail(
            $"Windows could not register {hotkey.DisplayName}. It may already be used by another app. ({error.Message})");
    }

    private void RegisterWithoutFallback(HotkeyDefinition hotkey)
    {
        var modifiers = ModNoRepeat;
        if (hotkey.Alt) modifiers |= ModAlt;
        if (hotkey.Control) modifiers |= ModControl;
        if (hotkey.Shift) modifiers |= ModShift;
        if (hotkey.Windows) modifiers |= ModWin;

        if (RegisterHotKey(_windowHandle, HotkeyId, modifiers, (uint)KeyInterop.VirtualKeyFromKey(hotkey.Key)))
        {
            _registeredHotkey = hotkey.Clone();
        }
    }

    private IntPtr WindowMessageHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            HotkeyPressed?.Invoke(this, EventArgs.Empty);
        }

        return IntPtr.Zero;
    }

    private void Unregister()
    {
        if (_registeredHotkey is not null)
        {
            UnregisterHotKey(_windowHandle, HotkeyId);
            _registeredHotkey = null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Unregister();
        _source.RemoveHook(WindowMessageHook);
        _disposed = true;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr windowHandle, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr windowHandle, int id);
}