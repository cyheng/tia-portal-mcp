using ModelContextProtocol.Server;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace TiaMcpServer.ModelContextProtocol
{
    // Default to a small discovery surface. Tool schemas are loaded on demand;
    // lite and full retain the direct-tool interface for existing clients.
    // All tools are static so no DI target is needed.
    public static partial class McpServer
    {
        private static readonly HashSet<string> CompactToolNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "Bootstrap", "FindTools", "GetToolSchema", "CallTool",
        };

        // Explicit allowlist (tool Name, not method name). Kept explicit on purpose:
        // membership must not silently change when a [Lx] description prefix is edited.
        // = all [L0]/[L1] tools + the golden-path tools ServerInstructions/GetAuthoringGuide
        // tell the model to call (previously [L2] and thus missing from lite — a weak
        // model in lite was instructed to call ImportFromDocuments and couldn't see it).
        private static readonly HashSet<string> LiteToolNames = new HashSet<string>(StringComparer.Ordinal)
        {
            // L0 — the bridge to everything not listed here. Without these two, lite is a
            // dead end: the model cannot even discover that the other ~160 tools exist.
            "FindTools", "GetToolSchema", "CallTool",
            // L0 — orientation / diagnostics
            "Bootstrap", "Doctor", "GetState", "GetAuthoringGuide",
            "GenerateAcceptanceReport", "GenerateErrorReport",
            "RunCapabilitySelfTest", "RunOnlineMonitoringSafetySelfTest",
            // L1 — session / project lifecycle
            "Connect", "Disconnect", "ListPortalProcessProjects", "EnsureOpennessUserGroup",
            // 用户在博途界面里开着工程时，Connect 会接管那个实例、OpenProject 又拒绝动它，
            // 整台服务器就用不了了。这条出口必须在默认档里看得见 —— 挡掉它等于让
            // 「用户正在用博途」变成一个无解的死局。
            "ConnectIsolated",
            "OpenProject", "AttachToOpenProject", "CreateProject", "SaveProject", "CloseProject",
            "GetProject", "GetProjectTree", "ValidateAutomationContext",
            // L1 — read / understand
            "GetSoftwareInfo", "GetSoftwareTree", "GetDevices", "DescribeBlockLogic",
            // L1 — build / import / compile
            "ScaffoldProject", "PlcBuildAndImport", "ImportBlock", "ImportType",
            "ImportPlcTagTable", "WritePlcSclSourceFile",
            // 单变量原地重命名：和 ImportPlcTagTable 同级（L1 标签写操作），单变量粒度比整表
            // 导入更轻、更可控，放 lite 让默认档就能改一个变量名，不必 FindTools 绕路。
            "RenamePlcTag",
            "CompileSoftware", "CompileAndDiagnosePlc",
            // The HMI counterpart. Without it a lite session can generate Unified screens but
            // cannot read its own HMI compile errors, so it has to hand the project back to the
            // engineer to compile in the UI (#24).
            "CompileAndDiagnoseHmi",
            // L1 — hardware
            "AddDeviceWithFallback", "SearchHardwareCatalog", "ConnectDeviceNodesToProfinetSubnet",
            // Golden-path tools referenced by ServerInstructions / GetAuthoringGuide
            // (previously [L2]; without them the lite roster contradicts the instructions)
            "ImportFromDocuments", "GenerateBlocksFromExternalSource",
            // Batch SD import/export are the "PREFERRED on V21+" batch path in the same
            // instructions; tag tables and cross-references are what a model needs to read a
            // project it did not write.
            "ImportBlocksFromDocuments", "ExportBlocksAsDocuments",
            "GetPlcTagTables", "GetCrossReferences",
            "GetBlocks", "GetBlocksWithHierarchy", "GetBlockInfo",
            "ExportAsDocuments", "GoOffline",
            // 大响应寄存与分页。**任何档都必须能翻页** —— 超过阈值的响应会被寄存，
            // 挡掉这几个出口等于内容直接丢：真实工程上 GetBlocks 的首页只装得下十几个块，
            // 剩下的拿不回来。它们只碰引擎自己内存里的那份副本，一个都不动 TIA 工程。
            "GetExport", "ListExports", "SaveExport", "DeleteExport", "ClearExports",
        };

        public static IList<McpServerTool> GetLiteTools()
        {
            return GetToolsByName(LiteToolNames);
        }

        public static IList<McpServerTool> GetCompactTools()
        {
            return GetToolsByName(CompactToolNames);
        }

        private static IList<McpServerTool> GetToolsByName(HashSet<string> names)
        {
            var tools = new List<McpServerTool>();
            foreach (var method in typeof(McpServer).GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                var attr = method.GetCustomAttribute<McpServerToolAttribute>();
                if (attr == null) continue;
                var name = attr.Name ?? method.Name;
                if (names.Contains(name))
                {
                    tools.Add(McpServerTool.Create(method));
                }
            }
            return tools;
        }

        /// <summary>The same resolved roster is used by both transports and discovery.</summary>
        public static IList<McpServerTool> GetConfiguredTools()
        {
            switch (ResolvedProfile())
            {
                case "full": return GetAllTools();
                case "lite": return GetLiteTools();
                default: return GetCompactTools();
            }
        }

        public static bool IsToolListed(string name)
        {
            switch (ResolvedProfile())
            {
                case "full": return AllToolMethods().ContainsKey(name);
                case "lite": return LiteToolNames.Contains(name);
                default: return CompactToolNames.Contains(name);
            }
        }

        /// <summary>
        /// 全量工具表。存在的理由是**能拿到列表才能包装它** —— 注册时原来走
        /// `WithToolsFromAssembly()`，那条路直接把工具塞进容器，中间没有一个可以插手的地方，
        /// 于是参数诊断、大响应分页这类「每个工具都该有」的能力根本接不上去。
        /// </summary>
        public static IList<McpServerTool> GetAllTools()
        {
            var tools = new List<McpServerTool>();
            foreach (var method in typeof(McpServer).GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (method.GetCustomAttribute<McpServerToolAttribute>() == null) continue;
                tools.Add(McpServerTool.Create(method));
            }
            return tools;
        }

        // ---- Profile resolution -----------------------------------------------------------------
        // Precedence: --profile flag > TIA_MCP_PROFILE env > compact.
        // Unknown values stay compact so a typo cannot load the full catalog.
        private static string? _profileOverride;

        /// <summary>Applies the CLI --profile flag. Wins over TIA_MCP_PROFILE. Call before building the host.</summary>
        public static void SetProfileOverride(string? profile)
        {
            _profileOverride = string.IsNullOrWhiteSpace(profile) ? null : profile!.Trim();
        }

        /// <summary>Resolved profile name: "compact", "lite" or "full".</summary>
        public static string ResolvedProfile()
        {
            string? p = _profileOverride;
            if (string.IsNullOrEmpty(p)) p = Environment.GetEnvironmentVariable("TIA_MCP_PROFILE");
            p = p?.Trim();
            if (string.Equals(p, "full", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p, "all", StringComparison.OrdinalIgnoreCase)) return "full";
            if (string.Equals(p, "lite", StringComparison.OrdinalIgnoreCase)) return "lite";
            return "compact";
        }

        public static bool IsLiteProfile()
        {
            return ResolvedProfile() == "lite";
        }
    }
}
