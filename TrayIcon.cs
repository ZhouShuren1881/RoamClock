using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;

namespace RoamClock;

/// <summary>Notification-area entry without a WinForms message loop.</summary>
internal sealed class TrayIcon : IDisposable
{
    private const int Callback = 0x8001;
    private readonly HwndSource _source;
    private readonly Action _open;
    private readonly Action _exit;
    private readonly uint _taskbarCreated;
    private IconData _data;
    private bool _darkTheme;
    private bool _disposed;
    public TrayIcon(Action open, Action exit)
    {
        _open = open; _exit = exit;
        _taskbarCreated = RegisterWindowMessage("TaskbarCreated");
        _source = new HwndSource(new HwndSourceParameters("RoamClock.Tray") { Width = 0, Height = 0, WindowStyle = 0 });
        _source.AddHook(WindowProc);
        try
        {
            _darkTheme = IsDarkSystemTheme();
            _data = new IconData
            {
                Size = Marshal.SizeOf<IconData>(), Window = _source.Handle, Id = 1,
                Flags = 1 | 2 | 4, CallbackMessage = Callback, Icon = LoadAppIcon(_darkTheme),
                Tip = "漫游时钟 · 双击打开设置", Info = "", InfoTitle = ""
            };
            Add();
        }
        catch
        {
            if (_data.Icon != 0) DestroyIcon(_data.Icon);
            _source.Dispose();
            throw;
        }
    }
    private static bool IsDarkSystemTheme() =>
        Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
            "SystemUsesLightTheme", 1) is int value && value == 0;

    private static nint LoadAppIcon(bool darkTheme)
    {
        string asset = darkTheme ? "app-dark.ico" : "app.ico";
        using var stream = Application.GetResourceStream(new Uri($"pack://application:,,,/RoamClock;component/assets/{asset}"))!.Stream;
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        byte[] ico = buffer.ToArray();
        using var reader = new BinaryReader(buffer);
        buffer.Position = 0;
        if (reader.ReadUInt16() != 0 || reader.ReadUInt16() != 1)
            throw new InvalidDataException("无效的 ICO 图标资源。");
        ushort count = reader.ReadUInt16();
        // Convert the ICO file directory to a resource directory so Windows can
        // select the best image for the current notification-area icon size.
        using var directory = new MemoryStream();
        using var writer = new BinaryWriter(directory);
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write(count);
        var images = new (uint Size, uint Offset)[count];
        for (int i = 0; i < count; i++)
        {
            writer.Write(reader.ReadBytes(8));
            uint size = reader.ReadUInt32();
            images[i] = (size, reader.ReadUInt32());
            writer.Write(size);
            writer.Write((ushort)(i + 1));
        }
        int width = GetSystemMetrics(49), height = GetSystemMetrics(50); // SM_CXSMICON / SM_CYSMICON
        int id = LookupIconIdFromDirectoryEx(directory.ToArray(), true, width, height, 0);
        if (id <= 0 || id > count) throw new InvalidDataException("无法选择 ICO 图标图像。");
        var selected = images[id - 1];
        byte[] data = ico.AsSpan(checked((int)selected.Offset), checked((int)selected.Size)).ToArray();
        nint icon = CreateIconFromResourceEx(data, (uint)data.Length, true, 0x00030000,
            width, height, 0);
        if (icon == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "无法加载程序图标。");
        return icon;
    }
    private void Add()
    {
        if (!Shell_NotifyIcon(0, ref _data)) throw new InvalidOperationException("无法创建托盘图标。");
    }
    private void UpdateThemeIcon(bool notify)
    {
        bool darkTheme = IsDarkSystemTheme();
        if (darkTheme == _darkTheme) return;
        nint icon = LoadAppIcon(darkTheme);
        nint previousIcon = _data.Icon;
        _data.Icon = icon;
        if (notify && !Shell_NotifyIcon(1, ref _data)) // NIM_MODIFY
        {
            _data.Icon = previousIcon;
            DestroyIcon(icon);
            throw new InvalidOperationException("无法更新托盘图标。");
        }
        _darkTheme = darkTheme;
        DestroyIcon(previousIcon);
    }
    private nint WindowProc(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if ((uint)message == _taskbarCreated)
        {
            UpdateThemeIcon(false);
            Shell_NotifyIcon(0, ref _data);
            return 0;
        }
        if (message is 0x1A or 0x31A) // WM_SETTINGCHANGE / WM_THEMECHANGED
        {
            // A transient Shell failure must not escape the native window callback.
            try { UpdateThemeIcon(true); }
            catch (Exception ex) { System.Diagnostics.Trace.TraceError("RoamClock tray theme update failed: {0}", ex); }
            return 0;
        }
        if (message != Callback) return 0;
        handled = true;
        if ((int)lParam == 0x203) _open(); // WM_LBUTTONDBLCLK
        if ((int)lParam == 0x205) // WM_RBUTTONUP
        {
            nint menu = CreatePopupMenu();
            try
            {
                AppendMenu(menu, 0, 1, "打开设置");
                AppendMenu(menu, 0, 2, "退出漫游时钟");
                GetCursorPos(out var point);
                SetForegroundWindow(hwnd);
                int selected = TrackPopupMenu(menu, 0x100 | 0x2, point.X, point.Y, 0, hwnd, 0);
                PostMessage(hwnd, 0, 0, 0);
                if (selected == 1) _open();
                if (selected == 2) _exit();
            }
            finally { DestroyMenu(menu); }
        }
        return 0;
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Shell_NotifyIcon(2, ref _data);
        _source.Dispose();
        DestroyIcon(_data.Icon);
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct IconData
    {
        public int Size;
        public nint Window;
        public uint Id, Flags, CallbackMessage;
        public nint Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags;
        public Guid Guid;
        public nint BalloonIcon;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern bool Shell_NotifyIcon(uint action, ref IconData data);
    [DllImport("user32.dll", SetLastError = true)] private static extern nint CreateIconFromResourceEx(byte[] data, uint size,
        [MarshalAs(UnmanagedType.Bool)] bool icon, uint version, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern int LookupIconIdFromDirectoryEx(byte[] directory,
        [MarshalAs(UnmanagedType.Bool)] bool icon, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(nint icon);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll")] private static extern nint CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool AppendMenu(nint menu, uint flags, nuint id, string text);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern int TrackPopupMenu(nint menu, uint flags, int x, int y, int reserved, nint hwnd, nint rect);
    [DllImport("user32.dll")] private static extern bool DestroyMenu(nint menu);
    [DllImport("user32.dll")] private static extern bool PostMessage(nint hwnd, uint message, nint wParam, nint lParam);
}
