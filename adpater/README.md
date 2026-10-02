# RoamClock 原生任务栏适配器

`adpater/` 保留原任务的目录拼写。基于本地 TaskbarWidgets 的进程隔离和 XAML Diagnostics 技术路线，实现独立的 Windows 11 x64 时钟覆盖。

## 组成

- `TaskbarAdapter.cs`：当前会话 Explorer 发现、接入、启停、内容更新、重连和清理。
- `ClockContent.cs`：WPF 与原生宿主共享的格式化显示数据。
- `SharedChannel.cs` / `native/protocol.h`：版本 3 固定 UTF-16 ABI，4464 字节；命名 Mutex 保护读写。
- `native/content.h`：两行原生 XAML 时钟，天气矢量图标、温度和缓存指示；颜色使用系统主题资源。
- `native/adapter.cpp`：COM TAP 观察 `SystemTray.DateTimeIconContent`，在其 Dispatcher 上操作模板和内容。

```text
weather_fetch → ClockFormatter → WPF clock_panel / 原生 ClockContent
                                    ↓
TaskbarAdapter → 命名共享内存 + Mutex → Explorer 内的原生 XAML 时钟
```

WPF 控件无法直接插入 Explorer 的 Windows.UI.Xaml 树，因此两种渲染器共用显示数据。Explorer 内没有网络请求、天气计算或时区计算。

## 模板和生命周期

识别 `ContainerGrid`、`TimeInnerTextBlock`、`DateInnerTextBlock`；校验原时间和日期处于同一 StackPanel。保持这个 StackPanel 及系统按钮原位，原文字透明，自建 Border 作为同一 Grid 的兄弟元素。内容不拦截点击，日历仍由系统按钮处理；其最终交互效果待人工验收。

扩宽时解除时钟祖先过小的 MaxWidth，记录原局部值。停用或控制进程死亡时请求删除自建节点，恢复原文字、尺寸约束、提示和无障碍属性；仅恢复仍等于本适配器写入值的属性。恢复依赖 Explorer UI 线程响应，清理出错时可能无法完成。已识别的宿主模板不匹配时跳过修改并记录不支持；未识别的宿主可能保持观察状态，多个宿主的汇总状态也可能显示已接入。

每个 UI 宿主用 DispatcherTimer 读取参数。新建图标使用保存的 Canvas 引用，避免树尚未加载时读取空 Parent；更新图标移动完整 Path 元素，不共享已经属于另一 Path 的 Geometry。

Explorer 直接加载构建产物 `RoamClock.Native.dll`，不复制 DLL，也不创建缓存目录。控制端与原生端分别读取 DLL 并计算 SHA-256，使用前 16 个十六进制字符作为通道后缀，原生端不依赖父目录名称；加载只需 DLL 的读权限。停用只移除 UI，保留观察器以支持恢复；控制进程退出或释放所有权后，原生线程取消 XAML 订阅，在各 UI 线程恢复原内容、停止定时器并释放宿主，等待 COM 对象与排队回调释放后通过 `FreeLibraryAndExitThread` 卸载 DLL。清理尚未完成时保留模块引用，避免 Explorer 调用已卸载的代码。`DisposeAsync` 最多等待三秒确认 UI 清理及模块卸载；超时原因可从 `Status.Detail` 获取。运行期间 DLL 会被 Explorer 占用，替换或重新构建前需要退出并确认卸载。

退出后的新控制进程会重新加载 DLL。`tests/Program.cs` 的集成模式验证 Explorer 加载指定的原始 DLL、退出后模块移除及文件可独占打开；`tests/Check-CrashCleanup.ps1` 验证控制进程被强制结束后的卸载和再次接入。旧版已经用 PIN 固定在 Explorer 中的 DLL 需要 Explorer 重启后才会释放，修复后的版本不再固定模块。

## 接口与 ABI

```csharp
await using var adapter = new RoamClock.Adpater.TaskbarAdapter();
adapter.SetContent(ClockFormatter.Format(DateTimeOffset.UtcNow,
    "Singapore Standard Time", true, true, weatherService.State), width: 208);
adapter.SetEnabled(false);
adapter.SetEnabled(true);
var status = adapter.Status;
```

共享通道名为 `Local\RoamClock.Adapter.v3.<ExplorerPid>.<buildHash>`，内容不同的 DLL 使用不同哈希通道。时钟宽度 32–480 DIP；产品设置限制为 50–320 DIP。字符串均包含 NUL 终止空间：时间/UTC/温度 32，日期 64，图标路径/提示 1024 个 UTF-16 码元。状态头包含节点数、HRESULT、Phase、RequestedRevision 和 AppliedRevision。UTC 偏移用于悬停信息，不作为任务栏面板的可见标签。

`Embedded` 表示已插入；`AppliedRevision` 表示至少一个 UI 宿主已应用该版本。节点计数和内容确认不能证明点击、字体或多屏布局正确。

## 验证与边界

2026-10-01 移除 DLL 缓存副本后，已在真实 Explorer 上通过 Debug 和 Release 原生 DLL 的直接加载、内容与天气图标更新、停用、恢复、正常退出卸载及原始文件解锁检查；Debug DLL 另通过控制进程强制结束后的清理、卸载、文件解锁和新控制进程再次接入检查。协议、Unicode、所有权与释放检查通过，主项目 Release 构建为零警告、零错误。这些检查不覆盖下列全部人工交互场景。

入口见项目根 README。[开发摘要](../ai_chats/tag_v1.0.2_chat_summary.md) 记载历史阶段曾通过天气图标更新、停用、恢复、正常退出、控制进程强制结束后的恢复及新控制进程接入检查。后续还有仅编译、未执行完整测试的修改；尚无与当前版本对应的完整桌面验证记录。崩溃检查脚本保留 Explorer，仅终止它创建的控制进程；这不等同于当前版本已实际通过检查。

多屏、混合 DPI、自动隐藏、全屏、拥挤布局、主题变化、点击和悬停仍待人工验收。Explorer 重启后的自动重连已实现，但未做最终主动重启测试。

接口参考：[Microsoft InitializeXamlDiagnosticsEx](https://learn.microsoft.com/en-us/windows/win32/api/xamlom/nf-xamlom-initializexamldiagnosticsex)。接口本身有文档，系统任务栏内部模板没有稳定契约。第三方声明见 `THIRD-PARTY-NOTICES.md`。
