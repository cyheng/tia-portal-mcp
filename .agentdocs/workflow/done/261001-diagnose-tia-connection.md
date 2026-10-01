# TIA MCP 连接故障诊断（分析完成）

## 范围

对比当前项目与 `D:\code\ai\TIA_Portal_Openness_MCP`，分析 Claude Code 调用 `Connect` 出现 `NonRecoverableException` 的原因。用户提供的 FC 备注请求是复现背景，本任务不修改工程程序块。

## 阶段与验证

- [x] 提取日志异常链、实际服务路径与环境状态。
- [x] 对比启动、程序集解析、实例附加、超时和错误传播；完成 MCP 连接失败复现及离线解析对照。
- [x] 区分已证实事实与待验证因素，整理修复优先级及验证边界。

## 约束

- 不关闭用户工程、不重启用户的 TIA 进程、不保存或写入工程。
- 不把 Bootstrap 就绪、工具数量或通用异常名称直接等同于连接结果。
- 当前工作区已有未跟踪的 `.agentdocs/`、`.serena/`，保持既有内容。

## 已验证证据

- 运行服务位于当前项目 `src/TiaMcpServer/bin/Release/net48/TiaMcpServer.exe`。
- Bootstrap 返回 V21、Openness 组通过；compact 列出 4 个入口，完整目录含 224 个工具。
- Connect 及 ListPortalProcessProjects 均复现 `NonRecoverableException: Connection to TiaPortal failed`；后者明确发现 PID 30276，失败发生在 `TiaPortalProcess.Attach()`。
- 两份 ConnectPortal 主流程一致；当前 Engineering 解析器取消了旧项目对 `Bin/PublicAPI` 的递归后备查找。
- 离线调用两份已编译解析器：Base 均加载成功，MarshallerHook 仅参考构建成功。ReflectionOnly 元数据检查确认连接初始化依赖该适配器。
- 实际附加对照被自动审批拒绝，理由为可能造成 TIA 退出及未保存工程损失；改为纯离线检查后已完成本次分析，未进行生产代码修复或宣称连接恢复。
- 可复用诊断约束整理于 `.agentdocs/backend/connection-diagnostics.md`。
