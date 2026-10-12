using System.Runtime.InteropServices;
using Hermes.Client.Core;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.Shell;

namespace Hermes.Client.WinUI3.Services;

/// <summary>
/// System-wide hotkey registration via Win32 RegisterHotKey. Interop is
/// CsWin32-generated (Microsoft.Windows.CsWin32, MIT) from NativeMethods.txt.
/// The main window's proc is hooked with SetWindowSubclass (the documented
/// chaining API — WinUI's own proc is untouched) to receive WM_HOTKEY; no
/// hidden window and no extra message loop are needed. All calls must run
/// on the UI thread, which owns the window.
/// </summary>
public sealed class GlobalHotkeyService : IHotkeyRegistrar
{
    private const int HotkeyId = 1;
    private const nuint SubclassId = 1;
    private const uint WmHotkey = 0x0312; // documented WM_HOTKEY value

    // Rooted for the process lifetime: native code holds the function pointer.
    private readonly SUBCLASSPROC _subclassProc;
    private HWND _hwnd;
    private bool _disposed;

    // Single instance: the static subclass proc routes to whichever registrar
    // installed it. The app registers exactly one.
    private static GlobalHotkeyService? _current;

    public event EventHandler? Pressed;

    public GlobalHotkeyService()
    {
        _subclassProc = SubclassProc;
        _current = this;
    }

    public bool TryRegister(HotkeyBinding binding, out string? error)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var hwnd = new HWND(MainWindow.WindowHandle);
        if (hwnd.IsNull)
        {
            error = "The main window isn't ready yet — try again in a moment.";
            return false;
        }

        // Install the subclass once; re-registration only swaps the hotkey.
        if (_hwnd.IsNull)
        {
            if (!PInvoke.SetWindowSubclass(hwnd, _subclassProc, SubclassId, 0))
            {
                error = $"Couldn't hook the window proc (Win32 error {Marshal.GetLastPInvokeError()}).";
                return false;
            }

            _hwnd = hwnd;
        }

        // Re-registration replaces the old binding; unregister first so a
        // changed combo never leaves a stale registration behind.
        _ = PInvoke.UnregisterHotKey(_hwnd, HotkeyId);

        var (modifiers, vk) = binding.ToRegisterHotKeyArgs();
        if (PInvoke.RegisterHotKey(_hwnd, HotkeyId, (HOT_KEY_MODIFIERS)modifiers, vk))
        {
            error = null;
            return true;
        }

        var code = Marshal.GetLastPInvokeError();
        error = code == 1409 // ERROR_HOTKEY_ALREADY_REGISTERED
            ? $"{binding} is already registered by another app — pick a different combo in Settings."
            : $"Couldn't register {binding} (Win32 error {code}).";
        return false;
    }

    public void Unregister()
    {
        if (_disposed || _hwnd.IsNull)
        {
            return;
        }

        _ = PInvoke.UnregisterHotKey(_hwnd, HotkeyId);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Unregister();
        if (!_hwnd.IsNull)
        {
            _ = PInvoke.RemoveWindowSubclass(_hwnd, _subclassProc, SubclassId);
            _hwnd = default;
        }

        if (ReferenceEquals(_current, this))
        {
            _current = null;
        }
    }

    private static LRESULT SubclassProc(
        HWND hWnd, uint uMsg, WPARAM wParam, LPARAM lParam, nuint uIdSubclass, nuint dwRefData)
    {
        if (uMsg == WmHotkey)
        {
            _current?.Pressed?.Invoke(_current, EventArgs.Empty);
            return new LRESULT(0);
        }

        return PInvoke.DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }
}
