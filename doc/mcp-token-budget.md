# MCP token 开销与本次调整

本项目主要的固定开销来自工具目录和公共指南。MCP 不会自动让这些内容变短；当客户端把全部工具 schema 放进模型上下文时，工具越多、描述越长，每轮输入就越大。调用次数和返回内容则产生额外开销。

## 从 Cloudflare 借鉴的做法

用户指定的 [mcp-server-cloudflare](https://github.com/cloudflare/mcp-server-cloudflare) 提供按产品领域划分的工具。其 Observability 实现只返回需要的字段，部分列表使用紧凑文本，事件查询有小默认页和 offset：[工具输出](https://github.com/cloudflare/mcp-server-cloudflare/blob/main/apps/workers-observability/src/tools/workers-observability.tools.ts)、[查询参数](https://github.com/cloudflare/mcp-server-cloudflare/blob/main/apps/workers-observability/src/types/workers-logs.types.ts)。

该仓库 README 推荐的 Code Mode 位于另一个 [cloudflare/mcp](https://github.com/cloudflare/mcp) 仓库。它用少量入口代替完整 API schema 常驻，先搜索摘要，再按需读取详情：[入口注册](https://github.com/cloudflare/mcp/blob/main/src/server.ts)、[搜索实现](https://github.com/cloudflare/mcp/blob/main/src/tools/search.ts)。它还统一控制响应大小，并标记省略内容：[响应预算](https://github.com/cloudflare/mcp/blob/main/src/truncate.ts)。

本项目采用同样的按需发现思路，通过现有受限工具目录转发调用，继续使用原来的 TIA 方法和参数检查。

## 实现

- 默认 `compact` 的 `tools/list` 只有 `Bootstrap`、`FindTools`、`GetToolSchema`、`CallTool`。完整工程工具和分页工具仍可通过 `CallTool` 调用。`--profile lite`、`--profile full` 保留直调兼容；`config --lite` / `config --full` 会显式写入对应档位。
- 初始化公共指南缩短为 588 字符。完整内容移至 `GetAuthoringGuide(topic="overview")`；语言规则仍按 `scl/lad/db/hmi` 获取。`Bootstrap` 默认返回简短状态和规则，`includeDetails=true` 可读取详细内容。
- `FindTools` 默认 6 个结果、最多 20 个，支持 `offset`。默认只返回准确签名和简短摘要；`includeDetails=true` 可获取完整描述。下一页看 `meta.nextOffset`。
- `GetToolSchema` 使用 MCP SDK 生成的原始 schema，包含参数描述、类型、必填项和默认值，避免另造一份不一致的 schema。
- 大响应默认预算由 20,000 改为 8,000 字符。沿用已有完整响应寄存和分页，超出部分有 `exportId/nextOffset`，需要文件时直接 `SaveExport`。`TIA_MCP_MAX_RESPONSE_CHARS` 仍可覆盖预算；每页上限仍为 20,000。该预算针对原始正文，分页信封另有少量开销；图片、多内容块等保持原状。
- 转发器等待 `Task/Task<T>` 完成后返回实际结果，保留内部失败状态，并在执行前拒绝未知参数。编译、保存等操作继续走原工具。

常用调用：

```text
Bootstrap()
FindTools(query="GetProjectTree")
GetToolSchema(name="GetProjectTree")            # 需要参数细节时
CallTool(name="GetProjectTree", argumentsJson="{}")
CallTool(name="GetAuthoringGuide", argumentsJson="{\"topic\":\"scl\"}")
```

已有调用程序若直接发送 `tools/call(name="GetProjectTree")`，应显式选择 `lite` 或 `full`。默认 compact 的注册表只包含四个入口，隐藏工具经 `CallTool` 转发。

## 实测：2026-09-30

使用修改前后分别构建的 Release 服务，通过真实 stdio `initialize` / `tools/list` 测量。计数来自原始 JSON-RPC 响应行，不含换行。

| 项目 | 调整前默认 lite | 调整后默认 compact |
|---|---:|---:|
| 工具数 | 56 | 4 |
| tools/list 序列化字符 / UTF-8 字节 | 42,198 | 2,458 |
| 解码后的公共初始化指南字符 | 3,230 | 588 |

工具列表序列化体积减少 **94.2%**，公共指南字符减少 **81.8%**。调整后显式 lite 为 57 个工具 / 41,973 字符，full 为 224 个工具 / 178,223 字符；工具总数增加来自 `GetToolSchema`。

部分客户端会把公共初始化指南重复加到每个工具描述前。本次客户端暴露的工具描述存在这种重复。按“工具列表字符 + 指南字符 × 工具数”估算，这部分从 223,078 降至 4,810 字符。该值受客户端行为影响，并不是线上 token 账单。

以上是字符、字节和工具数量测量，未使用模型 tokenizer。不能把 94.2% 当作整个任务的 token 节省：结果大小、调用轮数、历史上下文、客户端工具缓存和模型分词都会影响总量。不要把服务端缓存 schema 本身当成模型输入开销降低。

## 复现与验证

```powershell
dotnet build .\src\TiaMcpServer\TiaMcpServer.csproj -c Release
dotnet run --project .\tests\TiaMcpServer.Tests\TiaMcpServer.Tests.csproj -c Release
.\scripts\Measure-McpContext.ps1 `
  -ExecutablePath .\src\TiaMcpServer\bin\Release\net48\TiaMcpServer.exe `
  -Smoke -OutputPath .\artifacts\mcp-context-default.json
```

测量脚本可用 `-Profile lite` 或 `-Profile full` 比较档位；不传档位会测量程序默认值，并只在子进程中清除继承的档位环境变量。`-Smoke` 检查搜索、准确 schema、同步/异步调用、内部失败保留和未知参数拒绝。它只读环境与指南，不连接或修改 TIA 项目。

离线测试链接实际工具发现、档位、转发和响应护栏源码，使用 MCP SDK，模拟 Siemens 相关工具体；覆盖分页边界、默认参数、异步异常、写工具参数错误无副作用和导出分页恢复。共 453 项通过，0 失败、0 跳过；未统计覆盖率百分比。实际 stdio 默认 compact、lite、full 三档均通过 smoke。构建保留既有的 `OpcUaLiveReader.cs:195` 可空引用警告。

客户端必须启动新构建的 exe 并刷新工具目录后，这些修改才会生效。当前聊天已加载的旧服务和历史上下文不会因源码修改而自动变小。
