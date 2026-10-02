using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Text;

namespace RoamClock.Adpater;

internal sealed class SharedChannel : IDisposable
{
    internal const int Size = 4464;
    private const uint Magic = 0x44414352;
    private readonly MemoryMappedFile _mapping;
    private readonly MemoryMappedViewAccessor _view;
    private readonly Mutex _mutex;

    public SharedChannel(int explorerPid, string suffix = "checks")
    {
        string name = $@"Local\RoamClock.Adapter.v3.{explorerPid}.{suffix}";
        _mutex = new Mutex(false, name + ".Lock");
        _mapping = MemoryMappedFile.CreateOrOpen(name, Size, MemoryMappedFileAccess.ReadWrite);
        _view = _mapping.CreateViewAccessor(0, Size, MemoryMappedFileAccess.ReadWrite);
        try
        {
            Locked(() =>
            {
                int owner = _view.ReadInt32(8);
                if (owner != 0 && IsAlive(owner))
                    throw new InvalidOperationException($"Explorer {explorerPid} 已由 RoamClock 进程 {owner} 管理。");
                if (_view.ReadUInt32(0) is not (0 or Magic) || _view.ReadUInt32(4) is not (0 or 3))
                    throw new InvalidOperationException("不兼容的适配器协议。");
                _view.Write(0, Magic);
                _view.Write(4, 3u);
                _view.Write(8, Environment.ProcessId);
                _view.Write(32, (uint)AdapterPhase.Waiting);
            });
        }
        catch { Dispose(); throw; }
    }

    public void Configure(bool enabled, int width, ClockContent content) => Locked(() =>
    {
        _view.Write(12, enabled ? 1u : 0u);
        _view.Write(16, unchecked(_view.ReadUInt32(16) + 1));
        _view.Write(20, (uint)width);
        _view.Write(44, (content.ShowWeather ? 1u : 0u) | (content.IsStale ? 2u : 0u));
        WriteText(48, 32, content.Time);
        WriteText(112, 64, content.Date);
        WriteText(240, 32, content.Zone);
        WriteText(304, 32, content.Temperature);
        WriteText(368, 1024, content.WeatherPath);
        WriteText(2416, 1024, content.Tooltip);
    });

    private void WriteText(int offset, int capacity, string value)
    {
        var bytes = new byte[capacity * 2];
        Encoding.Unicode.GetBytes(value).CopyTo(bytes, 0);
        _view.WriteArray(offset, bytes, 0, bytes.Length);
    }

    public AdapterStatus Read(int pid) => Locked(() => new AdapterStatus(pid,
        (AdapterPhase)_view.ReadUInt32(32), _view.ReadInt32(24),
        _view.ReadInt32(36), _view.ReadInt32(28), null)
        { AppliedRevision = _view.ReadUInt32(40), RequestedRevision = _view.ReadUInt32(16) });

    public void ReleaseOwner() => Locked(() =>
    {
        _view.Write(12, 0u);
        _view.Write(8, 0u);
    });

    private T Locked<T>(Func<T> action)
    {
        bool held;
        try { held = _mutex.WaitOne(TimeSpan.FromSeconds(1)); }
        catch (AbandonedMutexException) { held = true; }
        if (!held) throw new TimeoutException("适配器参数通道超时。");
        try { return action(); }
        finally { _mutex.ReleaseMutex(); }
    }
    private void Locked(Action action) => Locked(() => { action(); return 0; });
    private static bool IsAlive(int pid)
    {
        try { using var process = Process.GetProcessById(pid); return !process.HasExited; }
        catch (ArgumentException) { return false; }
    }
    public void Dispose() { _view.Dispose(); _mapping.Dispose(); _mutex.Dispose(); }
}
