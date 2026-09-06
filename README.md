# TiaMcpServer

TiaMcpServer 是一个面向 Siemens TIA Portal 的 Model Context Protocol（MCP）服务。它通过 TIA Portal Openness API 为 AI 客户端提供 PLC、HMI、硬件、程序块、在线诊断和项目文件导入导出能力，也提供一个与 MCP 共用引擎的命令行入口。

项目当前包含两个 TIA Portal 目标：默认项目面向 TIA Portal V21，`TiaMcpServer.V20.csproj` 面向 V20。运行时会检测本机 TIA 版本，并在交付目录中尝试路由到匹配版本的引擎。

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
- TIA Portal：Siemens Openness V20/V21
- 通信：Sharp7、Workstation.UaClient
- 配置与规格解析：YamlDotNet、System.Text.Json

## 环境要求

### 使用发布包

正式交付建议使用 GitHub Release 中的 Windows x64 zip 发布包。发布包必须包含 `TiaMcpServer.exe`、.NET/MCP 依赖 DLL 和配置文件；不要只下载 exe。发布包不包含 Siemens TIA Openness API，实际项目操作仍需要本机安装匹配版本的 TIA Portal、Openness 组件及相关授权。

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

### 本机开发与离线测试

实际连接或修改 TIA 项目需要 Windows x64、.NET Framework 4.8、匹配版本的 TIA Portal（包含 Openness）、当前用户加入 `Siemens TIA Openness` 用户组，以及当前用户可访问目标项目。运行离线测试只需要 .NET SDK 8，不需要启动 TIA Portal 或加载 Openness 程序集。

版本匹配关系如下：

| 发布包 | 目标 TIA Portal | Openness API |
|---|---|---|
| V21 | TIA Portal V21 | 本机 `PublicAPI\\V21` |
| V20 | TIA Portal V20 | 本机 `PublicAPI\\V20` |

如果机器上安装了多个 TIA 版本，程序会自动尝试匹配；也可以明确指定：

~~~powershell
.\TiaMcpServer.exe --tia-major-version 21
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
--tia-major-version N       指定 TIA 主版本，例如 20 或 21
--tia-portal-location PATH  指定 TIA Portal 安装目录
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

stdio 配置的基本形态如下，`command` 必须指向构建或交付包中的实际 `TiaMcpServer.exe`：

```json
{
  "mcpServers": {
    "tia-portal": {
      "command": "C:\\path\\to\\TiaMcpServer.exe",
      "args": ["--tia-major-version", "21"]
    }
  }
}
```

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

CLI 支持的退出码为：`0` 成功，`1` 已执行但存在失败步骤，`2` 参数或运行错误。`tia gen` 规格的主要字段包括 `projectName`、`directoryPath`、`plcName`、`plcFamily`、`udt`、`globalDb`、`tagTable`、`sclSourceFiles`、`ladDocs`、`hmiScreens`、`hmiTags`、`compile` 和 `save`。用 `tia schema` 查看完整字段说明。

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

`Bootstrap` 是只读环境和状态检查，不会自动连接 TIA Portal。写入操作前必须先读取实际项目树和软件路径，避免猜测 PLC/HMI 名称。任何写入后都应编译并保存；项目不会自动保存。

编写 PLC 代码前使用 `GetAuthoringGuide` 获取经过验证的 SCL/LAD 语法。LAD 推荐使用带 UTF-8 BOM 的 `.s7dcl` 与 `.s7res` 文本导入；不要手写 SimaticML 的 FlgNet 梯形图 XML。SCL 外部源通过 `ImportPlcExternalSource` 和 `GenerateBlocksFromExternalSource` 导入，编码和现有块覆盖规则应以 `GetAuthoringGuide` 返回的说明为准。

## AI Skill
本项目附带的 [skill/SKILL.md](skill/SKILL.md) 是给 AI 客户端使用的 TIA Portal 操作规范。它说明了 MCP 工具的调用顺序、项目读写规则、SCL/LAD 编码要求、HMI 工作流、在线操作限制以及示例文件的位置。

配置 MCP 客户端时，建议把本文件和 `skill/` 目录一起保留在发布包或项目目录中。完整的 Skill 文件不是 MCP 服务的运行时程序集依赖，但它能让没有内置项目知识的 AI 客户端按照正确流程调用服务；MCP 服务自身也在 `Bootstrap` 响应和 `McpGuides` 中提供了部分同类指引。

## 开发与贡献

开发环境、源码构建、测试命令、GitHub Actions、Self-hosted Runner 和 Release 流程请参阅 [doc/development.md](doc/development.md)。

## 离线测试与验证

测试、CI 和 Release 构建说明请参阅 [doc/development.md](doc/development.md)。离线测试不会启动 TIA Portal，也不替代真实项目的编译、保存、在线监控或下载验证。

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
├── TiaMcpServer.csproj         默认 TIA Portal V21 目标
├── TiaMcpServer.V20.csproj    TIA Portal V20 目标
└── TiaMcpServer.sln           Visual Studio 解决方案

tests/TiaMcpServer.Tests/      .NET 8 离线检查控制台
skill/                         MCP 使用说明和 SCL/LAD 示例资产
.trellis/                      项目开发流程、规范和代理工作区数据
```
