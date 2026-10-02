<p align="center">
    <img src="./assets/app.png" alt="Taskbar Widgets logo" width="128" height="128" />
</p>

<h1 align="center">漫游时钟 RoamClock</h1>

<p align="center">
    漂泊在外也要使用家乡的时区
</p>

<p align="center">
  <a href="https://github.com/ZhouShuren1881/RoamClock/releases"><img alt="Total release downloads" src="https://img.shields.io/github/downloads/ZhouShuren1881/RoamClock/total?label=downloads&color=8B5CF6" /></a>
  <img alt="Windows 11 x64" src="https://img.shields.io/badge/Windows%2011-x64-0078D4?logo=windows11&logoColor=white" />
  <img alt="Vite Rate 98" src="https://img.shields.io/badge/Vibe_Rate-98%25-green" />
</p>

<p align="center">
    <a href="https://github.com/chatboxai/chatbox/releases" target="_blank">
        <img alt="Downloads" src="./assets/screenshot.png" />
    </a>
</p>

### 安装使用

仅适配 Windows 11 x64。

- MSI 仅为当前用户安装到 `%LOCALAPPDATA%\Programs\RoamClock`。

- 注册表项位于 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\RoamClock`。

自启动使用了 `--background` 参数实现静默启动，直接双击图标时会显示设置页面。

默认显示**新加坡时间 + 新加坡天气**，时区与天气城市独立配置。天气信息来自 Open-Meteo。

> 重要提示
>
> 如果MSI安装闪退或者程序卸载闪退，请重启后再试。
>
> 安装后，程序会自动提示安装 .NET 8 Desktop Runtime（x64）。

### 编译与发布

需要 .NET 8 SDK 或兼容 SDK、Visual Studio 2022 C++ 桌面工具、Windows SDK、CMake 3.24 或更高版本。上述依赖已安装时，构建无需管理员权限；首次还原依赖或生成 MSI 时可能需要网络获取 NuGet 包、WiX 5.0.2 及其 UI 扩展。

```powershell
# 直接运行（首次运行或需要重新还原依赖时去掉 --no-restore 参数）
dotnet run --project RoamClock.csproj --no-restore

# 发布并生成 MSI
.\build.ps1
```

构建自动编译原生组件。发布使用 x64、PerMonitorV2、依赖运行时的单文件 ReadyToRun 配置；托管程序使用嵌入调试符号。原生 DLL 和声明文件独立随包发布，原生调试 PDB 不包含在三文件发布包中。

MSI 使用依赖运行时的单文件发布，输出到 `bin\RoamClock-<版本>-win-x64.msi`。

### 验证

以下离线用例本身不请求真实天气；首次执行 `dotnet run` 仍可能联网还原依赖。主测试项目会触发原生组件构建。

```powershell
# 离线：天气内存状态、请求失败、取消、城市竞态、跨午夜、夏令时、设置及 WPF 渲染
dotnet run --project tests/RoamClock.Tests.csproj -c Release -- artifacts
# 离线：原生协议字段、Unicode、所有权和释放
dotnet run --project adpater/tests/AdapterChecks.csproj -c Debug
# 用户桌面：任务栏天气图标、内容确认、停用、恢复和清理
dotnet run --project adpater/tests/AdapterChecks.csproj -c Debug -- .\bin\msi-publish\RoamClock.Native.dll
# 单独运行崩溃检查时，也需先构建 Debug 检查程序
dotnet build adpater/tests/AdapterChecks.csproj -c Debug
# 只终止本次测试的控制进程，检查异常退出恢复与新控制进程再次接入
powershell -File adpater/tests/Check-CrashCleanup.ps1 -NativeDll .\bin\msi-publish\RoamClock.Native.dll
```

原生接口与实现细节见 [adpater/README.md](adpater/README.md)。

### 鸣谢

任务栏嵌入实现参考了 [TaskbarWidgets - Github](https://github.com/pfcdev/TaskbarWidgets)

Icon 是C娘。
