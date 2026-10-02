# 如何管理 .NET 工具

> 来源：[Microsoft Learn — .NET 工具](https://learn.microsoft.com/en-us/dotnet/core/tools/global-tools#install-a-local-tool)
>
> 作者：Microsoft 及贡献者。内容采用 [CC BY 4.0](https://github.com/dotnet/docs/blob/main/LICENSE) 许可。本文已转换为独立 Markdown 文档，移除了网页导航，将链接补全为完整地址，并翻译为中文。
>
> 跳转至：[安装本地工具](#安装本地工具)。

本文适用于：✔️ .NET Core 2.1 SDK 及更高版本。

.NET 工具是一种包含控制台应用程序的特殊 NuGet 包。可以通过以下方式在计算机上安装工具：

- 安装为全局工具。

  工具的二进制文件安装在默认目录中，该目录会添加到 PATH 环境变量。你可以从计算机上的任何目录调用工具，无需指定其位置。计算机上的所有目录使用同一个版本的工具。

- 安装为位于自定义位置的全局工具（也称为 tool-path 工具）。

  工具的二进制文件安装在你指定的位置。你可以从安装目录调用工具，也可以在命令名称前指定目录，或将该目录添加到 PATH 环境变量。计算机上的所有目录使用同一个版本的工具。

- 安装为本地工具（适用于 .NET Core SDK 3.0 及更高版本）。

  工具的二进制文件安装在默认目录中。你可以从安装目录或其任意子目录调用工具。不同目录可以使用同一工具的不同版本。

  .NET CLI 使用清单文件记录针对某个目录安装的本地工具。如果清单文件保存在源代码仓库的根目录中，贡献者可以克隆仓库，然后执行一条 .NET CLI 命令，安装清单文件中列出的所有工具。

> **重要**
>
> .NET 工具以完全信任模式运行。只有在信任工具作者时，才应安装该 .NET 工具。
>
> 如果 .NET 是通过 Snap 安装的，.NET 工具可能无法正常工作。

## 查找工具

可以通过以下方式查找工具：

- 使用 [dotnet tool search](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-tool-search) 命令查找发布到 NuGet.org 的工具。
- 在 [NuGet](https://www.nuget.org) 网站上，使用“.NET tool”包类型筛选器搜索工具。有关详细信息，请参阅[查找和选择包](https://learn.microsoft.com/en-us/nuget/consume-packages/finding-and-choosing-packages)。
- 在 [dotnet/aspnetcore GitHub 仓库的 Tools 目录](https://github.com/dotnet/aspnetcore/tree/main/src/Tools)中，查看 ASP.NET Core 团队创建的工具的源代码。
- 通过 [.NET 诊断工具](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/tools-overview)了解诊断工具。

## 查看作者和统计信息

.NET 工具以完全信任模式运行，而且全局工具会添加到 PATH 环境变量，因此它们具有强大的功能。不要下载不可信作者提供的工具。

如果工具托管在 NuGet 上，可以通过搜索该工具查看作者和统计信息。

## 安装全局工具

要将工具安装为全局工具，请使用 [dotnet tool install](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-tool-install) 的 `-g` 或 `--global` 选项，如下例所示：

```dotnetcli
dotnet tool install -g dotnetsay
```

输出会显示调用工具所需的命令及安装的版本，类似于以下示例：

```text
You can invoke the tool using the following command: dotnetsay
Tool 'dotnetsay' (version '2.1.4') was successfully installed.
```

> **注意**
>
> 默认情况下，要安装的 .NET 二进制文件的体系结构与当前运行的操作系统体系结构一致。要指定其他操作系统体系结构，请参阅 [dotnet tool install 的 --arch 选项](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-tool-install#options)。

工具二进制文件的默认位置取决于操作系统：

| 操作系统 | 路径 |
| --- | --- |
| Linux/macOS | `$HOME/.dotnet/tools` |
| Windows | `%USERPROFILE%\.dotnet\tools` |

首次运行 SDK 时，该位置会添加到用户的 PATH 中。因此，可以从任何目录调用全局工具，无需指定工具位置。

工具的访问权限按用户划分，并非对计算机上的所有用户开放。全局工具仅供安装该工具的用户使用。

### 在自定义位置安装全局工具

要在自定义位置安装全局工具，请使用 [dotnet tool install](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-tool-install) 的 `--tool-path` 选项，如下例所示：

在 Windows 上：

```dotnetcli
dotnet tool install dotnetsay --tool-path c:\dotnet-tools
```

在 Linux 或 macOS 上：

```dotnetcli
dotnet tool install dotnetsay --tool-path ~/bin
```

.NET SDK 不会自动将该位置添加到 PATH 环境变量。要[调用 tool-path 工具](#调用-tool-path-工具)，必须通过以下任一方法确保命令可用：

- 将安装目录添加到 PATH 环境变量。
- 调用工具时指定其完整路径。
- 在安装目录中调用工具。

## 安装本地工具

适用于 .NET Core 3.0 SDK 及更高版本。

如果希望安装的工具仅供本地访问（当前目录及其子目录），必须将工具添加到工具清单文件中。要创建工具清单文件，请运行 `dotnet new tool-manifest` 命令：

```dotnetcli
dotnet new tool-manifest
```

该命令会在 .config 目录下创建名为 dotnet-tools.json 的清单文件。要将本地工具添加到清单文件中，请使用 [dotnet tool install](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-tool-install) 命令，并省略 `--global` 和 `--tool-path` 选项，如下例所示：

> **警告**
>
> 确保工具清单文件存放在受控的位置。.NET CLI 会根据工具清单的内容，通过 `dotnet tool run` 启动本地工具。如果清单被不可信的一方修改，可能导致 CLI 运行恶意代码。

```dotnetcli
dotnet tool install dotnetsay
```

命令输出会显示新安装的工具被添加到哪个清单文件中，类似于以下示例：

```text
You can invoke the tool from this directory using the following command:
dotnet tool run dotnetsay
Tool 'dotnetsay' (version '2.1.4') was successfully installed.
Entry is added to the manifest file /home/name/botsay/.config/dotnet-tools.json.
```

以下示例展示了包含两个已安装本地工具的清单文件：

```json
{
  "version": 1,
  "isRoot": true,
  "tools": {
    "botsay": {
      "version": "1.0.0",
      "commands": [
        "botsay"
      ]
    },
    "dotnetsay": {
      "version": "2.1.3",
      "commands": [
        "dotnetsay"
      ]
    }
  }
}
```

通常会在仓库根目录中添加本地工具。将清单文件提交到仓库后，从仓库检出代码的开发人员会获得最新的清单文件。要安装清单文件中列出的所有工具，他们可以运行 `dotnet tool restore` 命令：

```dotnetcli
dotnet tool restore
```

输出会显示已还原的工具：

```text
Tool 'botsay' (version '1.0.0') was restored. Available commands: botsay
Tool 'dotnetsay' (version '2.1.3') was restored. Available commands: dotnetsay
Restore was successful.
```

## 安装工具的特定版本

要安装工具的预发布版本或特定版本，请使用 `--version` 选项指定版本号，如下例所示：

```dotnetcli
dotnet tool install dotnetsay --version 2.1.3
```

要安装工具的预发布版本而不指定确切版本号，请使用 `--version` 选项并提供通配符，如下例所示：

```dotnetcli
dotnet tool install --global dotnetsay --version "*-rc*"
```

## 使用工具

调用工具所用的命令可能与所安装的包名称不同。要显示计算机上当前用户已安装的所有工具，请使用 [dotnet tool list](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-tool-list) 命令：

```dotnetcli
dotnet tool list
```

输出会显示每个工具的版本和命令，类似于以下示例：

```text
Package Id      Version      Commands       Manifest
-------------------------------------------------------------------------------------------
botsay          1.0.0        botsay         /home/name/repository/.config/dotnet-tools.json
dotnetsay       2.1.3        dotnetsay      /home/name/repository/.config/dotnet-tools.json
```

如上例所示，该列表显示的是本地工具。要查看全局工具，请使用 `--global` 选项。要查看 tool-path 工具，请使用 `--tool-path` 选项。

要列出特定工具，请使用 [dotnet tool list <PACKAGE_ID>](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-tool-list) 命令：

```dotnetcli
dotnet tool list dotnetsay
```

只有在该工具已安装时，输出才会列出它，类似于以下示例：

```text
Package Id      Version      Commands       Manifest
-------------------------------------------------------------------------------------------
dotnetsay       2.1.3        dotnetsay      /home/name/repository/.config/dotnet-tools.json
```

### 调用全局工具

对于全局工具，直接使用工具命令即可。例如，如果命令是 `dotnetsay` 或 `dotnet-doc`，就使用该命令调用全局工具：

```console
dotnetsay
dotnet-doc
```

如果命令以 `dotnet-` 为前缀，也可以使用 `dotnet` 命令并省略工具命令的前缀来调用工具。例如，如果命令是 `dotnet-doc`，以下命令可以调用该工具：

```dotnetcli
dotnet doc
```

但是，在以下情况下，不能使用 `dotnet` 命令调用全局工具：

- 全局工具和本地工具具有相同的命令，且该命令以 `dotnet-` 为前缀。
- 你希望从本地工具作用范围内的目录调用全局工具。

在这种情况下，`dotnet doc` 和 `dotnet dotnet-doc` 都会调用本地工具。要调用全局工具，请直接使用工具命令：

```console
dotnet-doc
```

### 调用 tool-path 工具

要调用使用 `tool-path` 选项安装的全局工具，请按照[本文前面的说明](#在自定义位置安装全局工具)，确保命令可用。

### 调用本地工具

要调用本地工具，必须在安装目录中使用 `dotnet` 命令。可以使用完整形式（`dotnet tool run <COMMAND_NAME>`）或简短形式（`dotnet <COMMAND_NAME>`），如下例所示：

```dotnetcli
dotnet tool run dotnetsay
dotnet dotnetsay
```

如果命令以 `dotnet-` 为前缀，调用工具时可以保留或省略该前缀。例如，如果命令是 `dotnet-doc`，以下任一命令都可以调用该本地工具：

```dotnetcli
dotnet tool run dotnet-doc
dotnet dotnet-doc
dotnet doc
```

## 更新工具

更新工具的过程包括卸载工具，再重新安装最新的稳定版本。要更新工具，请使用 [dotnet tool update](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-tool-update) 命令，并使用安装工具时所用的相同选项：

```dotnetcli
dotnet tool update --global <packagename>
dotnet tool update --tool-path <packagename>
dotnet tool update <packagename>
```

对于本地工具，SDK 会在当前目录及其父目录中查找第一个包含该包 ID 的清单文件。如果所有清单文件中都没有该包 ID，SDK 会将新条目添加到最近的清单文件中。

## 卸载工具

要卸载工具，请使用 [dotnet tool uninstall](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-tool-uninstall) 命令，并使用安装工具时所用的相同选项：

```dotnetcli
dotnet tool uninstall --global <packagename>
dotnet tool uninstall --tool-path <packagename>
dotnet tool uninstall <packagename>
```

对于本地工具，SDK 会在当前目录及其父目录中查找第一个包含该包 ID 的清单文件。

## 获取帮助和排查问题

如果工具无法安装或运行，请参阅[排查 .NET 工具使用问题](https://learn.microsoft.com/en-us/dotnet/core/tools/troubleshoot-usage-issues)。可以使用 `--help` 参数获取可用的 `dotnet tool` 命令和参数列表：

```dotnetcli
dotnet tool --help
```

要获取工具的使用说明，请输入以下任一命令，或查看该工具的网站：

```console
<command> --help
dotnet <command> --help
```

## 另请参阅

- [教程：使用 .NET CLI 创建 .NET 工具](https://learn.microsoft.com/en-us/dotnet/core/tools/global-tools-how-to-create)
- [教程：使用 .NET CLI 安装和使用 .NET 全局工具](https://learn.microsoft.com/en-us/dotnet/core/tools/global-tools-how-to-use)
- [教程：使用 .NET CLI 安装和使用 .NET 本地工具](https://learn.microsoft.com/en-us/dotnet/core/tools/local-tools-how-to-use)
- [创建特定于 RID 的 .NET 工具和 AOT .NET 工具](https://learn.microsoft.com/en-us/dotnet/core/tools/rid-specific-tools)
