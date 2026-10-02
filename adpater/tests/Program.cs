using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using RoamClock.Adpater;
using RoamClock.WeatherFetch;

try { await RunAsync(args); }
catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }

static async Task RunAsync(string[] args)
{
// Default: protocol checks without Explorer load.
if (args.Length == 0)
{
    int id = Random.Shared.Next(100_000_000, int.MaxValue);
    using var channel = new SharedChannel(id);
    var clock = new ClockContent("23:59:59", "09/30 周三", "UTC+08:00", "-12°C", "M0,0 L1,1", "杭州 · 缓存天气", true, true);
    channel.Configure(true, 208, clock);
    using var mapping = MemoryMappedFile.OpenExisting($@"Local\RoamClock.Adapter.v3.{id}.checks");
    using var view = mapping.CreateViewAccessor();
    Check(view.ReadUInt32(0) == 0x44414352 && view.ReadUInt32(4) == 3, "ABI header");
    Check(view.ReadInt32(8) == Environment.ProcessId && view.ReadInt32(20) == 208, "owner and DIP width");
    Check(view.ReadUInt32(44) == 3 && ReadText(48, 32) == clock.Time && ReadText(112, 64) == clock.Date &&
        ReadText(240, 32) == clock.Zone && ReadText(304, 32) == clock.Temperature &&
        ReadText(368, 1024) == clock.WeatherPath && ReadText(2416, 1024) == clock.Tooltip, "all clock fields match native offsets");
    channel.Configure(false, 176, clock with { Time = "1", Tooltip = "短", ShowWeather = false, IsStale = false });
    Check(ReadText(48, 32) == "1" && ReadText(2416, 1024) == "短" && view.ReadUInt32(44) == 0, "shorter updates clear stale text and flags");
    try { (clock with { Tooltip = new string('x', 1024) }).Validate(); throw new Exception("Oversized content accepted"); }
    catch (ArgumentException) { }
    try { using var duplicate = new SharedChannel(id); throw new Exception("Duplicate owner accepted"); }
    catch (InvalidOperationException) { }
    view.Write(24, 2); view.Write(32, (uint)AdapterPhase.Embedded); view.Write(40, 7u);
    Check(channel.Read(id).AttachedCount == 2, "native acknowledgement");
    Check(channel.Read(id).AppliedRevision == 7, "content acknowledgement");
    channel.ReleaseOwner();
    Check(view.ReadUInt32(8) == 0 && view.ReadUInt32(12) == 0, "release disables content");
    Console.WriteLine("PASS: protocol, Unicode, ownership and release");
    return;

    string ReadText(int offset, int capacity)
    {
        var bytes = new byte[capacity * 2];
        view.ReadArray(offset, bytes, 0, bytes.Length);
        return System.Text.Encoding.Unicode.GetString(bytes).TrimEnd('\0');
    }
}

bool hold = args[0] == "--hold";
string sourcePath = Path.GetFullPath(args[hold ? 1 : 0]);
await using var adapter = new TaskbarAdapter(sourcePath);
adapter.SetContent(new ClockContent("23:59:59", "09/30 周三", "UTC+08:00", "-12°C",
    WeatherConditions.IconPath(2, true), "杭州 · 整合测试", true, true), 208);
await WaitFor(s => s.Phase == AdapterPhase.Embedded && s.AttachedCount > 0);
using (var explorer = Process.GetProcessById(adapter.Status.ExplorerPid))
    Check(explorer.Modules.Cast<ProcessModule>().Any(m =>
        string.Equals(m.FileName, sourcePath, StringComparison.OrdinalIgnoreCase)), "Explorer loads the original native DLL");
Console.WriteLine($"ATTACHED: {adapter.Status}");
if (hold) { await Task.Delay(Timeout.Infinite); return; }
uint previousRevision = adapter.Status.AppliedRevision;
adapter.SetContent(new ClockContent("23:59:59", "09/30 周三", "UTC+08:00", "-12°C", "M2,13 L24,13", "杭州 · 整合测试", true, true), 208);
await WaitFor(s => s.AppliedRevision != previousRevision &&
    s.AppliedRevision == s.RequestedRevision && s.Phase == AdapterPhase.Embedded);
Console.WriteLine("UPDATED: content revision acknowledged by a UI host");
foreach (var code in new[] { 0, 2, 3, 45, 51, 73, 95, -1 })
{
    previousRevision = adapter.Status.AppliedRevision;
    adapter.SetContent(new ClockContent("00:00:00", "10/01 周四", "UTC+08:00", "24°C",
        WeatherConditions.IconPath(code, code != 0), "杭州 · " + WeatherConditions.Describe(code)));
    await WaitFor(s => s.AppliedRevision != previousRevision && s.AppliedRevision == s.RequestedRevision && s.Phase == AdapterPhase.Embedded);
}
Console.WriteLine("ICONS: all weather geometries applied");
adapter.SetEnabled(false);
await WaitFor(s => s.Phase == AdapterPhase.Disabled && s.AttachedCount == 0);
Console.WriteLine("DETACHED: no native roots");
adapter.SetEnabled(true);
await WaitFor(s => s.Phase == AdapterPhase.Embedded && s.AttachedCount > 0);
Console.WriteLine($"REATTACHED: {adapter.Status}");
await adapter.DisposeAsync();
Check(adapter.Status.AttachedCount == 0, "dispose cleanup acknowledgement");
Check(adapter.Status.Detail is null, "dispose unload acknowledgement: " + adapter.Status.Detail);
using (var explorer = Process.GetProcessById(adapter.Status.ExplorerPid))
    Check(!explorer.Modules.Cast<ProcessModule>().Any(m =>
        string.Equals(m.FileName, sourcePath, StringComparison.OrdinalIgnoreCase)), "native module removed from Explorer");
using (File.Open(sourcePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
Console.WriteLine("PASS: direct DLL load, embed, update, disable, resume, module unload and DLL file unlocked");

async Task WaitFor(Func<AdapterStatus, bool> predicate)
{
    var timer = Stopwatch.StartNew();
    while (timer.Elapsed < TimeSpan.FromSeconds(12))
    {
        if (predicate(adapter.Status)) return;
        await Task.Delay(200);
    }
    throw new Exception($"Timeout: {adapter.Status}");
}
static void Check(bool value, string name) { if (!value) throw new Exception(name); }

}
