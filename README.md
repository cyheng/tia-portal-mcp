# TiaMcpServer

TiaMcpServer 是一个面向 Siemens TIA Portal 的 Model Context Protocol（MCP）服务。它通过 TIA Portal Openness API 为 AI 客户端提供 PLC、HMI、硬件、程序块、在线诊断和项目文件导入导出能力，也提供一个与 MCP 共用引擎的命令行入口。

项目面向 TIA Portal V21。主服务通过 `TiaMcpServer.csproj` 构建，运行时从本机 V21 安装目录加载 Openness 程序集。

## 功能概览

- 通过 MCP `stdio` 传输接入 Claude、Cursor、VS Code、Codex 等 AI 客户端。
- 通过本地 Streamable HTTP 传输提供 `/mcp` 接口，并支持可选 API Key。
- 连接、打开、创建、保存和关闭 TIA Portal 项目，读取项目树和 PLC/HMI 软件结构。
- 从 JSON/YAML 规格一次性创建或更新 PLC、UDT、DB、标签表、SCL/LAD 程序块和 WinCC Unified HMI。
- 导入、导出、编译和诊断 PLC/HMI 程序；支持 XML、SCL 和 SIMATIC SD 文本（`.s7dcl`、`.s7res`）。
- 处理硬件设备、PROFINET、HMI 连接与标签、报警、交叉引用、全局库和技术对象。
- 读取 S7 或 OPC UA 在线值，检查下载准备状态并执行 PLC 下载。
- 提供 `tia doctor` 环境体检、MCP 客户端配置安装和离线验证套件。



## 技术栈

- C# / .NET SDK-style project
- 主服务：`.NET Framework 4.8`，`x64`
- 离线测试：`.NET 8` 控制台程序
- MCP SDK：`ModelContextProtocol 0.3.0-preview.4`
- TIA Portal：Siemens Openness V21
- 通信：Sharp7、Workstation.UaClient
- 配置与规格解析：YamlDotNet、System.Text.Json

## 环境要求

### 使用发布包

从 GitHub Release 下载 `TiaMcpServer-v21-win-x64.zip`，完整解压并保留 `TiaMcpServer.exe`、.NET/MCP 依赖 DLL 和配置文件。实际项目操作使用本机安装的 TIA Portal V21、Openness 组件及相关授权。

解压后先运行环境检查：

~~~powershell
cd C:\path\to\TiaMcpServer
.\TiaMcpServer.exe doctor
~~~

如果当前用户不在 `Siemens TIA Openness` 用户组，可用管理员 PowerShell 执行发布包中的配置脚本；完成后注销并重新登录 Windows：

~~~powershell
.\Configure-TiaOpenness.ps1
~~~

只检查、不修改用户组：

~~~powershell
.\Configure-TiaOpenness.ps1 -CheckOnly
~~~

浏览器下载的文件若带有网络来源标记，遇到程序集无法加载时，可在解压目录执行 `Get-ChildItem -Recurse | Unblock-File`。

### 配置博途安装路径

建议在 AI 客户端的 MCP 服务配置中显式设置 `TiaPortalLocation`，把博途安装路径直接传给 MCP 子进程，配置示例见下文「接入 AI 客户端」。变量值填写包含 `PublicAPI` 子目录的 TIA Portal V21 安装根目录；自定义安装位置使用本机的实际路径。

| 环境变量 | 默认安装路径示例 |
|---|---|
| `TiaPortalLocation` | `C:/Program Files/Siemens/Automation/Portal V21` |

也可以在 Windows「环境变量 → 用户变量」中添加上述变量，或通过 PowerShell 写入当前用户的环境变量：

```powershell
[Environment]::SetEnvironmentVariable(
    "TiaPortalLocation",
    "C:/Program Files/Siemens/Automation/Portal V21",
    "User"
)
```

保存用户环境变量后，完整退出 AI 客户端及其后台进程，再重新启动客户端，使它和后续启动的 MCP 子进程继承新值。当前 PowerShell 会话可用 `$env:TiaPortalLocation = "C:/Program Files/Siemens/Automation/Portal V21"` 立即设置；该赋值作用于当前终端及其后续子进程。

### 本机开发与离线测试

实际连接或修改 TIA 项目需要 Windows x64、.NET Framework 4.8、TIA Portal V21（包含 Openness）、当前用户加入 `Siemens TIA Openness` 用户组，以及当前用户可访问目标项目。离线测试在 .NET SDK 8 环境中独立运行。

程序优先使用 `--tia-portal-location` 指定的安装目录；自动查找时依次检查 `TiaPortalLocation` 环境变量、V21 注册表信息和默认安装目录，选取包含 V21 Openness API 的目录。标准安装结构中的程序集位于 `PublicAPI\V21\net48`。自定义安装目录也可直接通过参数指定：

~~~powershell
.\TiaMcpServer.exe --tia-portal-location 'D:\TIA21\Portal V21'
~~~

`doctor` 默认只读；`doctor --fix` 会尝试修复 Openness 用户组成员关系，可能触发 UAC。也可以在 MCP 服务启动后使用 `Doctor` 工具。


## 启动 MCP 服务

直接启动默认的 stdio MCP 服务：

```powershell
dotnet run --project .\src\TiaMcpServer\TiaMcpServer.csproj -c Release
```

常用启动参数：

```text
--tia-portal-location PATH  指定 TIA Portal V21 安装目录
--profile lite|full         工具列表；默认 lite
--with-ui                   使用 TIA Portal 图形界面启动，默认无界面
--logging 0|1|2|3           无日志、stderr、Debug 输出或 Windows Event Log
```

默认 `lite` 只列出核心工具，其他工具可通过 `FindTools` 和 `CallTool` 按需访问。 `full` 会列出全部工具，但可能超过部分 AI 客户端的工具数量限制。

启动本地 HTTP MCP 服务：

```powershell
dotnet run --project .\src\TiaMcpServer\TiaMcpServer.csproj -c Release -- `
  --transport http `
  --http-prefix http://127.0.0.1:8765/ `
  --http-api-key <secret>
```

HTTP 传输提供：

- `GET /`：服务身份信息
- `GET /mcp/health`：健康检查，不要求 API Key
- `POST /mcp`：MCP JSON-RPC 请求
- `DELETE /mcp`：结束 MCP 会话

未设置 `--http-api-key` 时 `/mcp` 未认证，只建议绑定本机地址或在受控内网使用。认证请求使用 `X-API-Key` 或 `Authorization: Bearer ...`。

## 接入 AI 客户端

服务支持通过 CLI 自动写入已检测到的客户端配置：

```powershell
dotnet run --project .\src\TiaMcpServer\TiaMcpServer.csproj -c Release -- config
```

只查看配置片段：

```powershell
dotnet run --project .\src\TiaMcpServer\TiaMcpServer.csproj -c Release -- config --print
```

也可以指定客户端，例如 `config --host cursor`、`config --host vscode` 或 `config --host codex`。配置写入前会保留 `.bak` 备份；写入后需要重启 AI 客户端。

通过 CLI 生成配置后，在 `tia-portal` 服务条目的 `env` 中补充博途安装路径。下面展示采用 `mcpServers` 结构的 stdio 配置：`command` 指向构建或交付包中的 `TiaMcpServer.exe`，`TiaPortalLocation` 指向博途安装根目录。

```json
{
  "mcpServers": {
    "tia-portal": {
      "command": "C:\\path\\to\\TiaMcpServer.exe",
      "args": [],
      "env": {
        "TiaPortalLocation": "C:/Program Files/Siemens/Automation/Portal V21"
      }
    }
  }
}
```

客户端启动 MCP 时会把 `env` 中的值传给服务子进程。保存配置后，重新启动对应 MCP 服务。若 `args` 中同时设置了 `--tia-portal-location`，服务以该参数指定的路径为准。

### MCP 启动时的路径检查

启动日志出现 Siemens DLL 加载失败、`FileNotFoundException` 或 Openness 初始化退出时，先在发布包目录中检查安装路径和 API 文件：

```powershell
$env:TiaPortalLocation = "C:/Program Files/Siemens/Automation/Portal V21"
Test-Path (Join-Path $env:TiaPortalLocation "PublicAPI/V21/net48/Siemens.Engineering.Base.dll")
.\TiaMcpServer.exe doctor
```

标准安装结构下，`Test-Path` 的预期结果为 `True`，`doctor` 会显示所选安装目录和可解析的 V21 Openness DLL 路径。需要补齐 API 文件时，通过 TIA Portal V21 安装程序安装 Openness 组件。

终端中的 `doctor` 反映当前终端的进程环境。AI 客户端启动 MCP 使用该客户端的进程环境及 MCP 配置中的 `env`；将确认过的路径填入该服务条目，可让子进程使用相同的安装目录。进一步排查时查看客户端的 MCP 日志、`%TEMP%/TiaMcpServer.log` 和程序目录中的 `TiaMcpServer.startup.log`。

## CLI 工作流

CLI 使用与 MCP 相同的引擎，适合脚本和一次性操作：

```powershell
# 从 JSON/YAML 规格创建项目；先用 dry-run 检查规格
tia gen .\project-spec.json --dry-run
tia gen .\project-spec.json

# 更新已有项目
tia patch .\project-spec.json --dry-run
tia patch .\project-spec.json --no-overwrite

# 查看项目结构并编译诊断
tia describe .\Demo.ap21 --plc PLC_1
tia compile .\Demo.ap21 --plc PLC_1

# 导出或导入单个程序块/文档
tia export .\Demo.ap21 --plc PLC_1 --out .\exports --block Main
tia export .\Demo.ap21 --plc PLC_1 --out .\exports --block Main --scl
tia import .\Demo.ap21 --plc PLC_1 --from .\exports

# 查看规格字段、版本和环境
tia schema
tia version
tia doctor
```

CLI 支持的退出码为：`0` 成功，`1` 流程包含失败步骤，`2` 参数或运行错误。`tia gen` 规格的主要字段包括 `projectName`、`directoryPath`、`plcName`、`plcFamily`、`udt`、`globalDb`、`tagTable`、`sclSourceFiles`、`ladDocs`、`hmiScreens`、`hmiTags`、`compile` 和 `save`。用 `tia schema` 查看完整字段说明。

`gen` 和 `patch` 共用规格校验与执行流程。每次执行先检查字段类型、PLC 构建内容、SCL/LAD 文件和 HMI 设计结构，全部预检通过后再连接并打开或创建工程。`--dry-run` 返回预检报告；实际执行返回各步骤的结果和错误信息。

规格字段按声明的类型填写：`compile`、`save` 使用布尔值，`udt`、`globalDb`、`tagTable` 使用数组，`designJson` 使用对象。YAML 引号保留字符串语义，例如 `projectName: "001"` 的工程名为 `001`。字段缺省时采用默认值，字段类型错误时报告具体 JSON 路径。

HMI 写入通过 `hmiName` 指定设备；`hmiSoftwarePath` 可进一步指定软件路径。解析范围限定在该设备内，路径歧义会作为步骤错误返回。

`save` 默认为 `true`，全部请求步骤成功后自动保存一次。步骤失败时，工程保留当前内存状态，报告列出失败项；检查和修正后可显式调用 `SaveProject`。`save: false` 用于保留待检查的内存修改，`compile: false` 用于跳过本次 PLC 编译步骤。

## MCP 使用顺序

通过 MCP 操作真实工程时，建议遵循下面的顺序：

```text
Bootstrap
  -> Connect
  -> OpenProject / AttachToOpenProject / CreateProject
  -> GetProjectTree
  -> 读取或写入工程
  -> CompileSoftware 或 CompileAndDiagnosePlc
  -> SaveProject
```

`Bootstrap` 提供只读环境和状态检查。通过 `Connect` 建立会话后，先读取实际项目树和软件路径，再操作其中的 PLC/HMI 对象。单项工具写入后应调用编译和保存工具；`ScaffoldProject` 与 `PatchProject` 按规格中的 `compile`、`save` 字段完成相应步骤。

编写 PLC 代码前使用 `GetAuthoringGuide` 获取经过验证的 SCL/LAD 语法。LAD 推荐使用带 UTF-8 BOM 的 `.s7dcl` 与 `.s7res` 文本导入；不要手写 SimaticML 的 FlgNet 梯形图 XML。SCL 外部源通过 `ImportPlcExternalSource` 和 `GenerateBlocksFromExternalSource` 导入，编码和现有块覆盖规则应以 `GetAuthoringGuide` 返回的说明为准。

## AI Skill
本项目附带的 [skill/SKILL.md](skill/SKILL.md) 是给 AI 客户端使用的 TIA Portal 操作规范。它说明了 MCP 工具的调用顺序、项目读写规则、SCL/LAD 编码要求、HMI 工作流、在线操作限制以及示例文件的位置。

配置 MCP 客户端时，建议把本文件和 `skill/` 目录一起保留在发布包或项目目录中。完整的 Skill 文件不是 MCP 服务的运行时程序集依赖，但它能让没有内置项目知识的 AI 客户端按照正确流程调用服务；MCP 服务自身也在 `Bootstrap` 响应和 `McpGuides` 中提供了部分同类指引。

## 开发与贡献

开发环境、源码构建、测试命令、GitHub Actions、Self-hosted Runner 和 Release 流程请参阅 [doc/development.md](doc/development.md)。

## 离线测试与验证

测试、CI 和 Release 构建说明请参阅 [doc/development.md](doc/development.md)。离线检查覆盖规格校验、步骤编排及独立构建逻辑；真实项目的编译、保存、在线监控和下载验证在 TIA Portal V21 环境中完成。

## 架构

模块职责、启动流程、MCP 工具分层、TIA Openness 依赖边界和测试边界请参阅 [doc/architecture.md](doc/architecture.md)。

## 目录结构

```text
src/TiaMcpServer/
├── Cli/                       CLI 命令、规格加载和 AI 客户端配置安装
├── ModelContextProtocol/      MCP 工具、提示、响应类型和离线构建器
├── Runtime/                   在线读取和环境体检
├── Siemens/                   TIA Openness、项目、PLC、HMI、设备和下载逻辑
├── Program*.cs                启动流程、探针和报告生成入口
├── TiaMcpServer.csproj         TIA Portal V21 主项目
└── TiaMcpServer.sln           Visual Studio 解决方案

tests/TiaMcpServer.Tests/      .NET 8 离线检查控制台
skill/                         MCP 使用说明和 SCL/LAD 示例资产
.trellis/                      项目开发流程、规范和代理工作区数据
```
