# 代理文档索引

## 产品与架构文档

`../doc/architecture.md` - 服务架构、模块边界与 TIA Openness 依赖。

## 开发文档

`../doc/development.md` - C# 构建、离线测试与本地验证要求。

## 后端文档

`backend/connection-diagnostics.md` - 连接分层判断、V21 运行时依赖解析回归证据，以及修复和验证边界。

## 当前任务文档

暂无。

## 已归档任务文档

`workflow/done/261001-diagnose-tia-connection.md` - Connect 连接故障分析的验证范围与完成情况；继续修复时读取。

`workflow/done/261001-fix-openness-resolver.md` - 连接运行时 DLL 解析修复、回归验证和默认 EXE 更新记录。

## 全局重要记忆

- 真实工程操作必须在 TIA Portal V21、Openness 组件和相应授权可用的环境中验证。
- MCP 客户端配置优先使用 CLI 的 `config --host <host>` 命令生成，并显式设置 `TiaPortalLocation`。

## 文档治理

- 文档与代码注释使用中文，必要时保留英文专业名词并在首次出现时说明。
- 新增长期约束、架构决策或跨模块约定时，优先更新已有文档并同步维护本索引。
