using System;
using System.Collections.Generic;
using System.Windows.Input;
using System.Windows.Interop;

namespace Noted.Interop;

// janela message-only que recebe os hotkeys globais (custo zero quando inactiva)
public sealed class HotKeyManager : IDisposable
{
    private readonly HwndSource _source;
    private readonly Dictionary<int, Action> _handlers = new();
    private int _nextId = 1;

    public HotKeyManager()
    {
        var p = new HwndSourceParameters("Noted.HotKeySink")
        {
            WindowStyle = 0,
            ExtendedWindowStyle = 0,
            ParentWindow = new IntPtr(-3) // HWND_MESSAGE
        };
        _source = new HwndSource(p);
        _source.AddHook(WndProc);
    }

    public bool Register(int modifiers, Key key, Action handler)
    {
        int vk = KeyInterop.VirtualKeyFromKey(key);
        int id = _nextId++;
        if (!Native.RegisterHotKey(_source.Handle, id, modifiers | Native.MOD_NOREPEAT, vk))
            return false;
        _handlers[id] = handler;
        return true;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Native.WM_HOTKEY && _handlers.TryGetValue(wParam.ToInt32(), out var h))
        {
            handled = true;
            h();
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        foreach (var id in _handlers.Keys) Native.UnregisterHotKey(_source.Handle, id);
        _handlers.Clear();
        _source.Dispose();
    }
}
