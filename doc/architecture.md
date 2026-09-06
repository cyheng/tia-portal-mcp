# 项目架构

## 定位

TiaMcpServer 是一个 Windows x64 的 .NET Framework 4.8 控制台服务。它把 MCP 协议层、TIA Portal Openness 工程操作、S7/OPC UA 在线读取和 CLI 规格处理放在同一个进程中，共用项目会话和工具实现。

## 目录分层

~~~text
src/TiaMcpServer/
├── Cli/                       CLI 命令、规格加载、客户端配置安装
├── ModelContextProtocol/      MCP 工具、提示、响应类型、构建器和验证器
├── Runtime/                   环境体检、S7/OPC UA 在线读取
├── Siemens/                   Openness 会话、项目、PLC、HMI、设备和下载逻辑
├── Program*.cs                启动流程、CLI 探针和报告生成
├── TiaMcpServer.csproj        TIA Portal V21 目标
└── TiaMcpServer.V20.csproj    TIA Portal V20 目标

tests/TiaMcpServer.Tests/      独立的 .NET 8 离线测试入口
skill/                         AI 操作规范和 SCL/LAD 示例资产
doc/                           面向开发者和维护者的项目文档
~~~

## 启动流程

1. Program.Main 初始化 UTF-8 控制台、程序集解析器和 CLI 参数。
2. 根据 CLI 参数、环境变量或注册表检测 TIA 主版本，并设置 Engineering.TiaMajorVersion。
3. 如果检测到的 TIA 版本和当前 exe 不匹配，EngineRouter 尝试启动同目录的匹配版本 exe。
4. CLI 动词（如 gen、compile、export）直接调用 McpServer 引擎；没有 CLI 动词时启动 MCP Host。
5. MCP 默认使用 stdio，也可以通过 --transport http 使用本地 HTTP 适配器。
6. Bootstrap 返回环境、连接状态、推荐下一步和工具层信息；实际工程写入应在读取项目树后进行。

## MCP 层

MCP 工具集中在 ModelContextProtocol/，使用 McpServerTool 特性暴露。工具按 [L0]、[L1]、[L2] 分层：

- L0：启动引导和诊断，例如 Bootstrap、Doctor、FindTools、CallTool。
- L1：常用项目生命周期、读取、导入、编译和保存操作。
- L2：硬件、报警、技术对象、在线监控、详细 HMI 和高级导入导出能力。

默认 lite 工具档只列出核心工具，其余工具通过 FindTools 和 CallTool 按需访问；--profile full 才列出完整工具表。

## TIA 依赖边界

源码中的 Siemens.Engineering.* 类型在编译期来自本机 TIA Portal Openness PublicAPI。发布包中的 Siemens.Collaboration.*、MCP SDK 和通信 DLL 不能替代这些 API。运行时也需要本机 TIA Portal、Openness 组件、用户组权限和匹配的版本。

V20 与 V21 使用不同的项目文件和 Openness 程序集版本。不要使用 V21 exe 去加载 V20 API，也不要把一个版本的 PublicAPI DLL 混入另一个版本的构建目录。

## 工程操作边界

核心工程会话由 Siemens/Portal*.cs 管理，MCP 工具负责参数校验和响应格式。推荐流程是：

~~~text
Bootstrap -> Connect -> OpenProject/AttachToOpenProject/CreateProject
         -> GetProjectTree -> read/write -> CompileSoftware -> SaveProject
~~~

SCL/LAD 构建器位于 ModelContextProtocol/，实际 TIA 调用位于 Siemens/。这样离线验证器可以链接不依赖 Siemens 程序集的构建逻辑，而真实工程操作仍通过 Openness 会话完成。

## 测试边界

tests/TiaMcpServer.Tests 通过项目文件链接少量零依赖源码，测试模板布局、JSON 构建、资源扫描、参数诊断、响应存储、错误分类和脚本检查。它不会启动 TIA Portal，也不会验证真实 PLC 编译、下载或在线通信。

真实 TIA 验证需要安装对应版本的 TIA Portal，并按 Skill 中的顺序先检查环境、项目树和编译结果。
