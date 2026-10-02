using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace RoamClock.Adpater;

public enum AdapterPhase { Waiting, Observing, Embedded, Disabled, Unsupported, Failed }
public sealed record AdapterStatus(int ExplorerPid, AdapterPhase Phase, int AttachedCount,
    int RejectedCount, int HResult, string? Detail)
{
    /// <summary>Last content revision successfully applied by at least one UI host.</summary>
    public uint AppliedRevision { get; init; }
    public uint RequestedRevision { get; init; }
}

/// <summary>Owns the Explorer connection. Content is native XAML, not a WPF HWND.</summary>
public sealed class TaskbarAdapter : IAsyncDisposable
{
    private static int s_controller;
    private readonly string _dll;
    private readonly string _channelSuffix;
    private readonly CancellationTokenSource _stop = new();
    private readonly object _gate = new();
    private readonly Task _watchdog;
    private Task? _disposeTask;
    private Task<int>? _connection;
    private SharedChannel? _channel;
    private int _pid;
    private bool _enabled = true, _disposed;
    private int _width = 154;
    private ClockContent _content = new("—", "", "", "—", "", "RoamClock 正在启动");
    private AdapterStatus _status = new(0, AdapterPhase.Waiting, 0, 0, 0, null);
    public AdapterStatus Status { get { lock (_gate) return _status; } }

    public TaskbarAdapter(string? nativeDllPath = null)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) ||
            RuntimeInformation.ProcessArchitecture != Architecture.X64 ||
            RuntimeInformation.OSArchitecture != Architecture.X64)
            throw new PlatformNotSupportedException("任务栏适配器支持 Windows 11 x64。");
        _dll = Path.GetFullPath(nativeDllPath ?? Path.Combine(AppContext.BaseDirectory, "RoamClock.Native.dll"));
        if (!File.Exists(_dll)) throw new FileNotFoundException("请先编译原生适配器。", _dll);
        using (var file = File.OpenRead(_dll))
            _channelSuffix = Convert.ToHexString(SHA256.HashData(file))[..16];
        if (Interlocked.CompareExchange(ref s_controller, 1, 0) != 0)
            throw new InvalidOperationException("当前进程已有任务栏适配器。");
        _watchdog = Task.Run(WatchAsync);
    }

    public void SetContent(string label, int width = 120)
    {
        SetContent(new ClockContent(label, "", "", "", "", label, false), width);
    }

    public void SetContent(ClockContent content, int width = 154)
    {
        ArgumentNullException.ThrowIfNull(content);
        content.Validate();
        if (width is < 32 or > 480) throw new ArgumentOutOfRangeException(nameof(width));
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_content == content && _width == width) return;
            _content = content; _width = width;
            _channel?.Configure(_enabled, _width, _content);
        }
    }
    public void SetEnabled(bool enabled)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _enabled = enabled;
            _channel?.Configure(enabled, _width, _content);
        }
    }

    private async Task WatchAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                try
                {
                    int pid = FindShellProcess();
                    bool connect = false;
                    lock (_gate)
                    {
                        if (_disposed) break;
                        if (_pid != pid)
                        {
                            Disconnect(); _pid = pid;
                        }
                        if (pid == 0)
                            _status = new(0, AdapterPhase.Waiting, 0, 0, 0, "等待 Explorer 任务栏");
                        else
                        {
                            if (_channel is null)
                            {
                                _channel = new SharedChannel(pid, _channelSuffix);
                                _channel.Configure(_enabled, _width, _content);
                            }
                            _status = _channel.Read(pid);
                            // Only a channel without native acknowledgement needs initialization.
                            if (_status.Phase == AdapterPhase.Waiting)
                            {
                                connect = true;
                            }
                        }
                    }
                    if (connect)
                    {
                        // A diagnostics call is external code and cannot be
                        // cancelled. Do not hold the public API lock while it runs.
                        // Retain one pending call instead of starting duplicates.
                        _connection ??= Task.Run(() => Connect(pid, _dll));
                        int hr = await _connection.WaitAsync(TimeSpan.FromSeconds(4), _stop.Token).ConfigureAwait(false);
                        _connection = null;
                        if (hr < 0)
                            lock (_gate) _status = new(pid, AdapterPhase.Failed, 0, 0, hr,
                                $"XAML Diagnostics 接入失败：0x{hr:X8}");
                    }
                }
                catch (OperationCanceledException) when (_stop.IsCancellationRequested) { break; }
                catch (TimeoutException)
                {
                    lock (_gate) _status = new(_pid, AdapterPhase.Waiting, 0, 0, 0, "等待 XAML Diagnostics 接入返回");
                }
                catch (Exception ex)
                {
                    if (_connection?.IsCompleted == true) _connection = null;
                    lock (_gate) _status = new(_pid, AdapterPhase.Failed, 0, 0, ex.HResult, ex.Message);
                }
                await Task.Delay(1000, _stop.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }

    private void Disconnect()
    {
        if (_channel is null) return;
        try { _channel.ReleaseOwner(); }
        finally { _channel.Dispose(); _channel = null; }
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate) return new ValueTask(_disposeTask ??= DisposeCoreAsync());
    }

    private async Task DisposeCoreAsync()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _channel?.Configure(false, _width, _content);
            _channel?.ReleaseOwner();
        }
        _stop.Cancel();
        try
        {
            await _watchdog.ConfigureAwait(false);
            // A connection already in progress may still inject the DLL. Give
            // it a chance to finish while the shutdown channel remains alive.
            if (_connection is not null)
            {
                try { await _connection.WaitAsync(TimeSpan.FromSeconds(4)).ConfigureAwait(false); }
                catch (TimeoutException) { }
            }
            // Wait for native UI-thread cleanup acknowledgement, bounded if
            // Explorer has exited or its UI thread is unresponsive.
            var deadline = Stopwatch.StartNew();
            bool confirmed = false;
            while (deadline.Elapsed < TimeSpan.FromSeconds(3))
            {
                lock (_gate)
                {
                    if (_channel is null) { confirmed = true; break; }
                    _status = _channel.Read(_pid);
                    if (_status.AttachedCount == 0 && _status.Phase == AdapterPhase.Disabled && !IsModuleLoaded())
                    { confirmed = true; break; }
                }
                await Task.Delay(100).ConfigureAwait(false);
            }
            if (!confirmed)
                lock (_gate) _status = _status with { Detail = "已请求退出，但未在三秒内确认原生 DLL 卸载。" };
        }
        finally
        {
            lock (_gate) Disconnect();
            _stop.Dispose(); Interlocked.Exchange(ref s_controller, 0);
        }
    }

    private bool IsModuleLoaded()
    {
        try
        {
            using var process = Process.GetProcessById(_pid);
            foreach (ProcessModule module in process.Modules)
                if (string.Equals(module.FileName, _dll, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
        catch (System.ComponentModel.Win32Exception) { return true; }
    }

    private static int FindShellProcess()
    {
        nint taskbar = FindWindow("Shell_TrayWnd", null);
        if (taskbar == 0) return 0;
        GetWindowThreadProcessId(taskbar, out uint pid);
        using var shell = Process.GetProcessById((int)pid);
        using var self = Process.GetCurrentProcess();
        if (!shell.ProcessName.Equals("explorer", StringComparison.OrdinalIgnoreCase) || shell.SessionId != self.SessionId)
            throw new InvalidOperationException("任务栏不属于当前会话的 Explorer。");
        return (int)pid;
    }

    // Unlike TaskbarWidgets' bootstrap, Diagnostics performs the DLL load itself.
    private static int Connect(int pid, string dll)
    {
        nint library = LoadLibraryEx("Windows.UI.Xaml.dll", 0, 0x800); // SYSTEM32
        if (library == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            nint address = GetProcAddress(library, "InitializeXamlDiagnosticsEx");
            if (address == 0) throw new EntryPointNotFoundException("InitializeXamlDiagnosticsEx");
            var initialize = Marshal.GetDelegateForFunctionPointer<InitializeDiagnostics>(address);
            Guid clsid = new("d86bb1b7-8e52-4e70-b4d6-50eb61e87031");
            int hr = unchecked((int)0x80070490);
            for (int i = 1; i <= 256 && hr == unchecked((int)0x80070490); i++)
                hr = initialize($"VisualDiagConnection{i}", (uint)pid, "", dll, clsid, null);
            return hr;
        }
        finally { FreeLibrary(library); }
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    private delegate int InitializeDiagnostics(string endpoint, uint pid, string diagnosticsDll,
        string tapDll, Guid clsid, string? data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint FindWindow(string name, string? title);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint LoadLibraryEx(string path, nint file, uint flags);
    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true)] private static extern nint GetProcAddress(nint library, string name);
    [DllImport("kernel32.dll")] private static extern bool FreeLibrary(nint library);
}
