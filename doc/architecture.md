# 项目架构

## 定位

TiaMcpServer 是一个面向 TIA Portal V21 的 Windows x64 .NET Framework 4.8 控制台服务。它把 MCP 协议层、TIA Portal Openness 工程操作、S7/OPC UA 在线读取和 CLI 规格处理放在同一个进程中，共用项目会话和工具实现。

## 目录分层

~~~text
src/TiaMcpServer/
├── Cli/                       CLI 命令、规格加载、客户端配置安装
├── ModelContextProtocol/      MCP 工具、提示、响应类型、构建器和验证器
├── Runtime/                   环境体检、S7/OPC UA 在线读取
├── Siemens/                   Openness 会话、项目、PLC、HMI、设备和下载逻辑
├── Program*.cs                启动流程、CLI 探针和报告生成
└── TiaMcpServer.csproj         TIA Portal V21 主项目

tests/TiaMcpServer.Tests/      独立的 .NET 8 离线测试入口
skill/                         AI 操作规范和 SCL/LAD 示例资产
doc/                           面向开发者和维护者的项目文档
~~~

## 启动流程

1. Program.Main 初始化 UTF-8 控制台、程序集解析器和 CLI 参数。
2. 使用固定的 TIA 主版本 21，根据安装目录参数、环境变量、注册表和默认路径定位 V21 安装目录。
3. 程序集解析器从 V21 安装目录加载 Openness API，工程操作使用同一套 V21 会话。
4. CLI 动词（如 gen、compile、export）直接调用 McpServer 引擎；没有 CLI 动词时启动 MCP Host。
5. MCP 默认使用 stdio，也可以通过 --transport http 使用本地 HTTP 适配器。
6. Bootstrap 返回环境、连接状态、推荐下一步和工具层信息；实际工程写入应在读取项目树后进行。

## MCP 层

MCP 工具集中在 ModelContextProtocol/，使用 McpServerTool 特性暴露。工具按 [L0]、[L1]、[L2] 分层：

- L0：启动引导和诊断，例如 Bootstrap、Doctor、FindTools、CallTool。
- L1：常用项目生命周期、读取、导入、编译和保存操作。
- L2：硬件、报警、技术对象、在线监控、详细 HMI 和高级导入导出能力。

默认 lite 工具档只列出核心工具，其余工具通过 FindTools 和 CallTool 按需访问；--profile full 才列出完整工具表。

工具入口保留在 `partial McpServer` 中，按职责组织文件：

| 文件 | 职责 |
|---|---|
| `McpServer.PlcSoftware.cs` | PLC 软件、变量表、工艺对象、外部源和编译工具 |
| `McpServer.PlcAuthoring.cs` | PLC XML 构建、SCL 文件生成和构建产物导入 |
| `McpServer.Hmi.cs` | HMI 查询、画面、标签、连接和事件脚本 |
| `McpServer.HmiTemplates.cs` | HMI 模板、库复用和设计预检 |
| `McpServer.Online.cs` | 在线状态、监视、下载及临时离线执行 |
| `McpServer.ReleaseValidation.cs` | 发布验证、诊断报告和交付清单 |
| `McpServer.ProjectWorkflow*.cs` | 工程生成与补丁的初始化、PLC 和 HMI 步骤适配 |

`partial Portal` 按相同业务边界组织 Openness 实现。`Portal.Software.cs` 管理 PLC 软件、工艺对象、外部源和编译；`Portal.PlcTables.cs` 管理 PLC 表；`Portal.SoftwareImportExport.cs` 负责批量导入导出；HMI 查询、画面及脚本、标签及连接分别位于 `Portal.Hmi.cs`、`Portal.UnifiedHmiScreens.cs` 和 `Portal.UnifiedHmiTags.cs`。全局库操作位于 `Portal.GlobalLibraries.cs`，跨软件对象的反射访问集中在 `Portal.SoftwareReflection.cs`。

## TIA 依赖边界

源码中的 Siemens.Engineering.* 类型在编译期来自本机 TIA Portal V21 的 `PublicAPI\V21\net48`。`TiaMcpServer.csproj` 引用 Openness V21 构建包，通过本机安装信息或 `TiaPortalLocation` 属性定位这些程序集。

发布包提供主服务、Siemens.Collaboration.*、MCP SDK 和通信 DLL。运行时从本机 V21 安装目录加载 Openness API，工程操作使用本机安装的 TIA Portal V21、Openness 组件及当前用户的访问权限。自定义安装目录通过 `--tia-portal-location` 参数或 `TiaPortalLocation` 环境变量配置。

## 工程操作边界

核心工程会话由 Siemens/Portal*.cs 管理，MCP 工具负责参数校验和响应格式。推荐流程是：

~~~text
Bootstrap -> Connect -> OpenProject/AttachToOpenProject/CreateProject
         -> GetProjectTree -> read/write -> CompileSoftware -> SaveProject
~~~

SCL/LAD 构建器位于 ModelContextProtocol/，实际 TIA 调用位于 Siemens/。构建、规格解析和步骤判定可以独立执行，工程读写通过 Openness 会话完成。

## 工程规格与执行

`ScaffoldProject` 和 `PatchProject` 是共享流程的两个入口。`ProjectSpecification` 负责解析规格和检查字段类型；`ProjectWorkflow` 负责全量预检、预演结果和保存条件；`McpServer.ProjectWorkflow*.cs` 将实际 PLC/HMI 工具接入该流程。

~~~text
JSON / YAML -> ProjectSpecification -> 全量离线预检
                                     -> dryRun: 返回预检报告
                                     -> 实际执行: 初始化工程 -> PLC 步骤 -> HMI 步骤
                                                                        -> 汇总步骤结果
                                                                        -> 成功且 save=true: 保存
~~~

步骤执行器依据子响应中的成功标记、失败集合及编译结果记录 `ok`、`failed`、`skipped`。同一步骤内的后续写入以前置调用成功为条件。自动保存以全部请求步骤成功为条件；失败报告保留已完成步骤，工程中的部分修改留在当前会话中供检查。

HMI 目标解析使用指定设备及其软件路径，解析错误会进入步骤报告。规格缺省值、参数错误和子操作失败均由共享实现处理。

## 测试边界

tests/TiaMcpServer.Tests 通过项目文件链接独立逻辑源码，覆盖 V21 安装路径、CLI 配置、YAML 类型、项目规格、步骤执行、模板布局、JSON 构建、资源扫描、参数诊断、响应存储和错误分类。工程流程测试通过委托提供子操作结果，验证预检屏障、失败传播、调用顺序和保存条件；脚本检查覆盖 HMI 事件脚本的默认校验策略。

真实 TIA 验证在安装了 TIA Portal V21 的环境中执行，并按 Skill 中的顺序先检查环境、项目树和编译结果。
