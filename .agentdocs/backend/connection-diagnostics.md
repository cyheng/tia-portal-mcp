# TIA 连接诊断约束

## 读取场景

排查 `Bootstrap` 就绪但 `Connect`、`AttachToOpenProject` 失败，或修改 `Engineering.cs` 程序集解析逻辑时读取。

## 分层判断

- MCP 握手与工具调用成功，仅说明客户端已连到 MCP 服务。
- `Bootstrap` 的安装检测检查 `Siemens.Engineering.Base.dll` 等环境条件，未实际证明 Openness 附加成功。
- `TiaPortal.GetProcesses()` 返回目标 PID，只证明进程发现成功。实际连接还依赖 `TiaPortalProcess.Attach()` 初始化通信与适配器。
- 已打开 UI 工程是 `Connect` 优先选择的对象，不应据此推断进程状态不兼容。
- `ConnectIsolated` 创建另一实例，适用于独立工程流程；不能作为接管当前 UI 工程的默认恢复方案。
- compact 工具档只列出四个发现入口，其余工具通过 `FindTools`、`GetToolSchema`、`CallTool` 访问。工具数量与连接成功无直接关系。

## 已确认的解析回归（修复前）

2026-10-01 对当前构建及参考项目 `D:\code\ai\TIA_Portal_Openness_MCP` 的源码与已编译解析器进行对照。当前查找范围由提交 `673e30b`（`refactor: standardize TIA Portal V21 runtime and documentation`）引入：

| 项目 | Siemens DLL 搜索范围 | 离线加载连接适配器结果 |
|---|---|---|
| 当前项目修复前 | `PublicAPI/V21/net48`、`PublicAPI/V21` | `FileNotFoundException` |
| 参考项目 | `PublicAPI/V21` 与 `Bin/PublicAPI` 的递归搜索 | 成功 |

测试使用两份已编译 `TiaMcpServer.exe` 中实际的 `Engineering.Resolver`，只调用解析方法，不初始化 Openness、不枚举或附加 TIA。两者都能加载 Base DLL，只有参考构建能加载 `Siemens.Engineering.ClientAdapter.MarshallerHook.dll`。

本机适配器实际位于 `E:\TIA-PORTAL-V21\Portal V21\Bin\PublicAPI\Client`，程序集版本为 `21.0.0.0`。修复前解析器找不到文件时抛出异常。不能假定所有 `Siemens.Engineering.*` DLL 均位于公开 API 目录。

对本机 Base DLL 的 ReflectionOnly 方法体检查显示：`TiaPortal` 构造器调用 `ApplicationEntryPoint.Initialize`，后者创建 `Session`；`Session` 使用 MarshallerHook 类型。构造器将一般异常交给 `NonRecoverableException.CreateInstance`，生成 `Connection to TiaPortal failed.`。该报错不能单独区分 DLL 加载失败、通信失败或 TIA 退出。

实际 MCP 已复现：发现 PID 30276，但 `Attach()` 返回上述 NonRecoverableException；随后复核该 PID 仍存在、启动时间未变，且 `Responding=true`。进程存活情况应单独检查，不能用异常名称替代证据。

结论边界：解析回归已独立证实，与连接失败链路一致；修复后真实 DLL 加载通过，尚未完成真实附加验证，不能声称已经恢复工程连接。

## 当前实现与回归检查

- `FindV21AssemblyPath` 仅定位公开 API；安装检测和版本识别继续要求公共 API 目录下存在 Base DLL。
- `FindV21DependencyPath` 按顺序检查公共 API、`Bin/PublicAPI` 根目录和 `Bin/PublicAPI/Client`。不递归搜索其他版本、框架、服务端或 AddIn 目录。
- `Resolver` 在加载前核对实际程序集名称、V21 主版本及请求指定的版本；身份不符抛出带文件路径的 `FileLoadException`。
- `V21EngineeringTests.cs` 覆盖两个适配器、通信契约、公共 API 优先级、受限搜索、错误身份以及运行时目录不能冒充公共 API 安装。
- `scripts/Test-OpennessAssemblyResolution.ps1` 编译临时 net48 探针，调用真实构建的解析器，加载 Base、Contract、ClientAdapter.Interfaces、MarshallerHook 和 MarshallerHook.Hmi 五个 DLL。不初始化 Openness，不枚举、附加或修改工程。
- 2026-10-01 验证：旧构建的两个 MarshallerHook 加载失败；修复构建五项均通过；离线套件 460 通过、0 失败、0 跳过，V21 Release 构建通过。
- 更新磁盘上的 EXE 后，已经启动的 MCP 进程仍执行旧程序集，必须重启客户端中的 MCP 服务才生效。

## 修复与测试边界

- 保留 V21 和 net48 约束，同时恢复对同一安装根目录中连接运行时依赖的受控查找；避免混入 V18/V19/V20 或其他 .NET 运行时目录。
- 环境体检应区分公共 API 存在与连接依赖可加载。
- 离线回归测试须持续保留公共 API 与运行时依赖分开存放的用例，避免后续精简路径再次破坏连接。
- `AttachToOpenProject` 的空 catch 会隐藏原始附加异常；诊断应保留失败阶段、PID 和底层异常，而不是仅返回无法附加工程。
- `ConnectPortal` 的附加失败后启动新实例策略及 30 秒等待超时不是本次独有改动，但会扩大故障表现；超时也不等于后台 Attach 已被取消。
- 实际工程验证应在工程已保存且明确允许连接测试后执行，不自动关闭工程、清理锁文件或重启 TIA。

## 证据位置

- 当前实现：`../../src/TiaMcpServer/Siemens/Engineering.cs`、`../../src/TiaMcpServer/Siemens/Portal.cs`。
- 离线临时探针及输出位于 Git 忽略目录 `artifacts/connection-diagnosis/`，可用于本次诊断复核，不作为部署依赖。
- Siemens V21 异常说明：[Handling exceptions](https://docs.tia.siemens.cloud/r/en-us/v21/tia-portal-openness-api-for-automation-of-engineering-workflows/tia-portal-openness-api/exceptions/handling-exceptions)。
