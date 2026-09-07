using ModelContextProtocol;
using ModelContextProtocol.Server;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using TiaMcpServer.Siemens;

namespace TiaMcpServer.ModelContextProtocol
{
    // PLC online state, monitoring, diagnostics and downloads.
    public static partial class McpServer
    {

        [McpServerTool(Name = "GetPlcWatchTables"), Description("[L2][PLC-Software]List PLC watch/monitor table names (PlcWatchTable). Read-only.")]
        public static ResponseStringList GetPlcWatchTables(
            [Description("softwarePath: path in the project structure to the PLC software")] string softwarePath)
        {
            try
            {
                var items = Portal.GetPlcWatchTables(softwarePath);
                if (items != null)
                {
                    return new ResponseStringList
                    {
                        Message = $"PLC watch tables listed for '{softwarePath}'",
                        Items = items,
                        Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = true }
                    };
                }

                throw new McpException($"PLC software not found at '{softwarePath}'.{Portal.AvailablePlcPathsSuffix()}", McpErrorCode.InternalError);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error listing PLC watch tables: {ex.Message}{McpHints.Recovery(ex)}", ex, McpErrorCode.InternalError);
            }
        }

        [McpServerTool(Name = "GetPlcForceTables"), Description(
            "[L2][Category:PLC-Online][PreCondition:Connect+OpenProject]" +
            " List all force table names in the PLC software." +
            " Force tables configure which variables are continuously forced to specific values while the CPU is online." +
            " Read-only: this server exposes NO tool for creating or editing force entries — forcing overrides live PLC logic" +
            " (a forced output stays forced regardless of what the program writes) and is deliberately kept out of the AI tool surface." +
            " Create or edit force entries in the TIA Portal UI. For a one-shot value written from a watch table instead," +
            " use SetWatchTableModifyValue (the variable reverts to PLC logic afterwards).")]
        public static ResponseStringList GetPlcForceTables(
            [Description("softwarePath: path to the PLC software, e.g. 'PLC_1'")] string softwarePath)
        {
            try
            {
                var names = Portal.GetPlcForceTables(softwarePath);
                if (names == null)
                {
                    // 原来这里返回 Items=[] + 一句「not found」的**正常**响应。
                    // 只读 Items 的调用方看到的是「这台 PLC 没有强制表」——和「你路径写错了」
                    // 是完全不同的结论，而它分辨不出来。
                    throw new McpException(
                        $"GetPlcForceTables: PLC software not found at '{softwarePath}'. "
                        + "Use GetProjectTree to get the exact PLC path.",
                        McpErrorCode.InvalidParams);
                }

                return new ResponseStringList
                {
                    Items = names,
                    Message = $"{names.Count} force table(s) found.",
                    Meta = new JsonObject { ["softwarePath"] = softwarePath, ["timestamp"] = DateTime.Now }
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error listing force tables for '{softwarePath}': {ex.Message}{McpHints.Recovery(ex)}", ex, McpErrorCode.InternalError);
            }
        }

        [McpServerTool(Name = "SetWatchTableModifyValue"), Description(
            "[L2][Category:PLC-Online][ONLINE-WRITE][PreCondition:Connect+OpenProject+GoOnline]" +
            " Configure a watch table entry to write a value to a PLC variable once (or on a trigger)." +
            " This is an OFFLINE CONFIGURATION step — the value is written to the PLC only when TIA Portal is online and the trigger fires." +
            " Trigger options: Permanent (every cycle), PermanentAtStart (every cycle, at scan start), OnceOnlyAtStart (single write at scan start), PermanentAtEnd, OnceOnlyAtEnd, OnceOnlyAtStop." +
            " Use GoOnline before calling this for the write to reach the PLC." +
            " SAFETY: the target is a variable in the physical CPU, not a simulation. The moment the trigger fires the value" +
            " lands on the real address — if that address is a coil, a valve, a contactor or a drive enable, the machine moves" +
            " at that instant, with no acknowledgement step. Before calling, know exactly what the address drives and confirm" +
            " nobody is at or inside the machine. The resulting physical motion is NOT undone by calling this tool again with" +
            " another value; the equipment stays wherever it moved to." +
            " Not a Force: this writes the value once per trigger event and the variable then follows PLC logic again" +
            " (a force would keep overriding the program continuously). This server exposes no tool for force entries —" +
            " GetPlcForceTables only lists them; use TIA Portal directly to force a value." +
            " Example: SetWatchTableModifyValue('PLC_1', 'Debug_WT', 'DB1.DBX0.0', 'TRUE', 'OnceOnlyAtStart')")]
        public static ResponseMessage SetWatchTableModifyValue(
            [Description("softwarePath: path to the PLC software, e.g. 'PLC_1'")] string softwarePath,
            [Description("tableName: name of the watch table to configure (created if not existing)")] string tableName,
            [Description("address: variable address, e.g. 'DB1.DBX0.0', '%M0.0', 'MyTag'")] string address,
            [Description("modifyValue: value to write, e.g. 'TRUE', '42', '3.14'")] string modifyValue,
            [Description("trigger: when to apply the write — Permanent | PermanentAtStart | OnceOnlyAtStart | PermanentAtEnd | OnceOnlyAtEnd | OnceOnlyAtStop (default: Permanent)")] string trigger = "Permanent")
        {
            try
            {
                return Portal.EnsureWatchTableEntry(softwarePath, tableName, address, modifyValue, trigger);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error setting watch table entry: {ex.Message}{McpHints.Recovery(ex)}", ex, McpErrorCode.InternalError);
            }
        }

        // Force-write capability retained in the Portal layer but intentionally NOT exposed as an MCP tool:
        // forcing overrides live PLC logic and must not be AI-invocable. Online monitoring stays read-only
        // (see RunOnlineMonitoringSafetySelfTest / Test_OnlineMonitoringNoUnsafeToolNames). Use TIA Portal
        // directly for commissioning forces. Removed from the tool surface in 0.0.38.
        public static ResponseMessage SetForceTableEntry(
            [Description("softwarePath: path to the PLC software, e.g. 'PLC_1'")] string softwarePath,
            [Description("tableName: name of the force table to configure (created if not existing)")] string tableName,
            [Description("address: variable address to force, e.g. 'DB1.DBX0.0', '%M0.0'")] string address,
            [Description("forceValue: value to force, e.g. 'TRUE', '42'")] string forceValue)
        {
            try
            {
                return Portal.EnsureForceTableEntry(softwarePath, tableName, address, forceValue);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error setting force table entry: {ex.Message}{McpHints.Recovery(ex)}", ex, McpErrorCode.InternalError);
            }
        }

        [McpServerTool(Name = "ExportPlcWatchTable"), Description("[L2][PLC-Software]Export one PLC watch/monitor table (PlcWatchTable) to XML file. Read-only against the TIA project.")]
        public static ResponseExportFile ExportPlcWatchTable(
            [Description("softwarePath: path in the project structure to the PLC software")] string softwarePath,
            [Description("watchTableName: PLC watch table name")] string watchTableName,
            [Description("exportPath: full file path to write to")] string exportPath)
        {
            try
            {
                var ok = Portal.ExportPlcWatchTable(softwarePath, watchTableName, exportPath);
                if (ok)
                {
                    return new ResponseExportFile
                    {
                        Message = $"PLC watch table '{watchTableName}' exported",
                        ExportPath = exportPath,
                        Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = true }
                    };
                }

                throw new McpException($"Failed exporting PLC watch table '{watchTableName}' from '{softwarePath}'", McpErrorCode.InternalError);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error exporting PLC watch table: {ex.Message}{McpHints.Recovery(ex)}", ex, McpErrorCode.InternalError);
            }
        }

        [McpServerTool(Name = "ExportPlcWatchTablesToDirectory"), Description("[L2][PLC-Software]Export all PLC watch/monitor tables to XML files. Read-only against the TIA project.")]
        public static ResponseImportBatch ExportPlcWatchTablesToDirectory(
            [Description("softwarePath: path in the project structure to the PLC software")] string softwarePath,
            [Description("dir: output directory")] string dir,
            [Description("regexName: optional regex filter applied to table name")] string regexName = "")
        {
            try
            {
                var result = Portal.ExportPlcWatchTablesToDirectory(softwarePath, dir, regexName);
                return new ResponseImportBatch
                {
                    Message = $"Exported {result.Imported?.Count() ?? 0} PLC watch tables to '{dir}'. Failed={result.Failed?.Count() ?? 0}",
                    Imported = result.Imported,
                    Failed = result.Failed,
                    Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = (result.Failed == null || !result.Failed.Any()) }
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error exporting PLC watch tables: {ex.Message}{McpHints.Recovery(ex)}", ex, McpErrorCode.InternalError);
            }
        }

        [McpServerTool(Name = "ProbePlcMonitorOnlineCapabilities"), Description("[L2][Online-Monitoring]Read-only probe for PLC online/offline/watch/monitor API surfaces. It does not go online/offline, change watch tables, write values, or touch restricted safety APIs.")]
        public static ResponseJsonReport ProbePlcMonitorOnlineCapabilities(
            [Description("softwarePath: path in the project structure to the PLC software")] string softwarePath)
        {
            try
            {
                var result = Portal.ProbePlcMonitorOnlineCapabilities(softwarePath);
                result.Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = result.Ok == true };
                return result;
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error probing PLC monitor/online capabilities: {ex.Message}{McpHints.Recovery(ex)}", ex, McpErrorCode.InternalError);
            }
        }

        [McpServerTool(Name = "ReadPlcWatchTableCurrentValuesReadOnly"), Description("[L2][Online-Monitoring] Read current/monitor value properties from an existing PLC watch table only. It does not create/modify watch tables, write PLC values, go offline, or use force operations.")]
        public static ResponseJsonReport ReadPlcWatchTableCurrentValuesReadOnly(
            [Description("softwarePath: PLC software path resolved from GetProjectTree/ValidateAutomationContext.")] string softwarePath,
            [Description("watchTableName: existing PLC watch table path/name returned by GetPlcWatchTables.")] string watchTableName,
            [Description("maxEntries: maximum entries to inspect.")] int maxEntries = 50)
        {
            try
            {
                var result = Portal.ReadPlcWatchTableCurrentValuesReadOnly(softwarePath, watchTableName, maxEntries);
                result.Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = result.Ok == true };
                return result;
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error reading PLC watch table values read-only: {ex.Message}{McpHints.Recovery(ex)}", ex, McpErrorCode.InternalError);
            }
        }

        [McpServerTool(Name = "PlanOnlineReadOnlyMonitoring"), Description("[L2][Online-Monitoring] Validate an online-monitoring request shape without connecting to TIA Portal. Read-only preflight only: no go-online/offline, no watch-table modification, no value write, and no force operation.")]
        public static ResponseJsonReport PlanOnlineReadOnlyMonitoring(
            [Description("softwarePath: PLC software path resolved from GetProjectTree/ValidateAutomationContext.")] string softwarePath,
            [Description("tagPathsJson: JSON array of symbolic PLC tag/member paths, for example [\"DB_HMI.MotorRun\",\"DB_HMI.SpeedSet\"]. Do not pass guessed M bits.")] string tagPathsJson,
            [Description("mode: current-values or watch-table-export-plan. Both are read-only planning modes.")] string mode = "current-values")
        {
            try
            {
                var warnings = new JsonArray();
                var acceptedTags = new JsonArray();
                var rejectedTags = new JsonArray();
                var policy = new JsonArray();
                foreach (var policyLine in GetOnlineMonitoringSafetyPolicy())
                {
                    policy.Add(policyLine);
                }
                var normalizedMode = (mode ?? string.Empty).Trim();
                var allowedModes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "current-values",
                    "watch-table-export-plan"
                };

                if (!allowedModes.Contains(normalizedMode))
                {
                    return BuildOnlineMonitoringPlanResponse(false, softwarePath, normalizedMode, acceptedTags, rejectedTags, warnings, policy, $"Unsupported mode '{mode}'. Supported values: current-values, watch-table-export-plan.");
                }

                if (string.IsNullOrWhiteSpace(softwarePath))
                {
                    warnings.Add("softwarePath is empty. Resolve the PLC software path from GetProjectTree before real online monitoring.");
                }

                JsonNode? parsed;
                try
                {
                    parsed = JsonNode.Parse(tagPathsJson);
                }
                catch (Exception ex)
                {
                    return BuildOnlineMonitoringPlanResponse(false, softwarePath, normalizedMode, acceptedTags, rejectedTags, warnings, policy, "tagPathsJson must be a JSON array of symbolic PLC paths. Parse error: " + ex.Message);
                }

                if (parsed is not JsonArray tagArray)
                {
                    return BuildOnlineMonitoringPlanResponse(false, softwarePath, normalizedMode, acceptedTags, rejectedTags, warnings, policy, "tagPathsJson must be a JSON array.");
                }

                foreach (var item in tagArray)
                {
                    var tag = item?.GetValue<string>()?.Trim() ?? string.Empty;
                    var rejectReason = GetOnlineMonitoringTagRejectReason(tag);
                    if (rejectReason == null)
                    {
                        acceptedTags.Add(tag);
                    }
                    else
                    {
                        rejectedTags.Add(new JsonObject
                        {
                            ["tagPath"] = tag,
                            ["reason"] = rejectReason
                        });
                    }
                }

                if (acceptedTags.Count == 0)
                {
                    warnings.Add("No accepted tag paths. Real online monitoring requires at least one declared PLC symbol or DB member.");
                }

                var ok = rejectedTags.Count == 0 && acceptedTags.Count > 0;
                var message = ok
                    ? "Online read-only monitoring plan validated. This preflight did not connect to TIA Portal."
                    : "Online read-only monitoring plan rejected. Fix rejected tag paths before any real online workflow.";

                return BuildOnlineMonitoringPlanResponse(ok, softwarePath, normalizedMode, acceptedTags, rejectedTags, warnings, policy, message);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error planning online read-only monitoring: {ex.Message}{McpHints.Recovery(ex)}", ex, McpErrorCode.InternalError);
            }
        }

        private static ResponseJsonReport BuildOnlineMonitoringPlanResponse(bool ok, string softwarePath, string mode, JsonArray acceptedTags, JsonArray rejectedTags, JsonArray warnings, JsonArray policy, string message)
        {
            return new ResponseJsonReport
            {
                Ok = ok,
                Message = message,
                Data = new JsonObject
                {
                    ["softwarePath"] = softwarePath,
                    ["mode"] = mode,
                    ["readOnly"] = true,
                    ["connectsToTia"] = false,
                    ["goesOnlineOrOffline"] = false,
                    ["modifiesWatchTables"] = false,
                    ["writesPlcValues"] = false,
                    ["usesForce"] = false,
                    ["acceptedTags"] = acceptedTags,
                    ["rejectedTags"] = rejectedTags,
                    ["warnings"] = warnings,
                    ["policy"] = policy
                },
                Meta = new JsonObject
                {
                    ["timestamp"] = DateTime.Now,
                    ["success"] = ok
                }
            };
        }

        private static string? GetOnlineMonitoringTagRejectReason(string tagPath)
        {
            if (string.IsNullOrWhiteSpace(tagPath))
            {
                return "Tag path is empty.";
            }

            var forbiddenIntent = new[]
            {
                "force", "write", "modify", "update", "create",
                "delete", "remove", "import", "insert", "download", "activate", "start", "stop",
                "goonline", "gooffline", "watchtable", "forcetable"
            };
            var compact = Regex.Replace(tagPath, @"[\s_\-\.]+", string.Empty);
            var segments = Regex.Split(tagPath, @"[\.\s_\-]+").Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
            var forbidden = forbiddenIntent.FirstOrDefault(x =>
                compact.Equals(x, StringComparison.OrdinalIgnoreCase) ||
                segments.Any(segment => segment.StartsWith(x, StringComparison.OrdinalIgnoreCase)));
            if (forbidden != null)
            {
                return $"Tag path contains unsafe online/write/force/watch-table intent keyword '{forbidden}'.";
            }

            if (Regex.IsMatch(tagPath, @"^%?[MIQ][BWD]?\d+(\.\d+)?$", RegexOptions.IgnoreCase))
            {
                return "Absolute I/Q/M address is not accepted for HMI/online planning. Use a declared PLC symbol or DB member read back from the project.";
            }

            if (!Regex.IsMatch(tagPath, @"^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)+$"))
            {
                return "Use a symbolic PLC path with at least one member separator, for example DB_HMI.MotorRun.";
            }

            return null;
        }

        [McpServerTool(Name = "PlanOnlineReadOnlyDataProvider"), Description("[L2][Online-Monitoring] Plan the commercial current-value path through an external read-only data provider such as opcua or s7-readonly. This is a preflight only: it does not connect, write PLC values, modify watch tables, go online/offline through TIA, or use force operations.")]
        public static ResponseJsonReport PlanOnlineReadOnlyDataProvider(
            [Description("provider: opcua or s7-readonly. opcua is preferred for commercial symbolic readback.")] string provider,
            [Description("endpoint: OPC UA endpoint URL or PLC endpoint/IP. It is validated only for shape and is not opened.")] string endpoint,
            [Description("tagPathsJson: JSON array of declared symbolic PLC tags/DB members. Guessed M bits and unsafe intent names are rejected.")] string tagPathsJson,
            [Description("optionsJson: optional JSON object such as {\"pollMs\":1000,\"source\":\"watch-table-export\"}.")] string optionsJson = "{}")
        {
            try
            {
                var normalizedProvider = (provider ?? "").Trim().ToLowerInvariant();
                var allowedProviders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "opcua",
                    "s7-readonly"
                };

                var policy = new JsonArray(GetOnlineMonitoringSafetyPolicy().Select(x => JsonValue.Create(x)).ToArray());
                var warnings = new JsonArray();
                var acceptedTags = new JsonArray();
                var rejectedTags = new JsonArray();
                var options = ParseJsonObjectOrEmpty(optionsJson, "optionsJson");

                if (!allowedProviders.Contains(normalizedProvider))
                {
                    return BuildReadOnlyProviderPlan(false, normalizedProvider, endpoint, acceptedTags, rejectedTags, warnings, policy, options, $"Unsupported provider '{provider}'. Supported providers: opcua, s7-readonly.");
                }

                if (string.IsNullOrWhiteSpace(endpoint))
                {
                    warnings.Add("endpoint is empty. Real read-only providers require an OPC UA endpoint URL or PLC endpoint/IP before execution.");
                }

                JsonNode? parsed;
                try
                {
                    parsed = JsonNode.Parse(tagPathsJson);
                }
                catch (Exception ex)
                {
                    return BuildReadOnlyProviderPlan(false, normalizedProvider, endpoint, acceptedTags, rejectedTags, warnings, policy, options, "tagPathsJson must be a JSON array. Parse error: " + ex.Message);
                }

                if (parsed is not JsonArray tagArray)
                {
                    return BuildReadOnlyProviderPlan(false, normalizedProvider, endpoint, acceptedTags, rejectedTags, warnings, policy, options, "tagPathsJson must be a JSON array.");
                }

                foreach (var item in tagArray)
                {
                    var tag = item?.GetValue<string>()?.Trim() ?? "";
                    var rejectReason = GetOnlineMonitoringTagRejectReason(tag);
                    if (rejectReason == null)
                    {
                        acceptedTags.Add(tag);
                    }
                    else
                    {
                        rejectedTags.Add(new JsonObject
                        {
                            ["tagPath"] = tag,
                            ["reason"] = rejectReason
                        });
                    }
                }

                if (normalizedProvider == "s7-readonly")
                {
                    warnings.Add("s7-readonly must be implemented as a read-only adapter with no Write/Force API surface exposed by MCP.");
                }

                var ok = acceptedTags.Count > 0 && rejectedTags.Count == 0;
                return BuildReadOnlyProviderPlan(
                    ok,
                    normalizedProvider,
                    endpoint,
                    acceptedTags,
                    rejectedTags,
                    warnings,
                    policy,
                    options,
                    ok
                        ? "Read-only data provider plan validated. This preflight did not open a network connection."
                        : "Read-only data provider plan rejected. Fix rejected tags/provider settings before any real read workflow.");
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error planning read-only data provider: {ex.Message}{McpHints.Recovery(ex)}", ex, McpErrorCode.InternalError);
            }
        }

        private static ResponseJsonReport BuildReadOnlyProviderPlan(bool ok, string provider, string endpoint, JsonArray acceptedTags, JsonArray rejectedTags, JsonArray warnings, JsonArray policy, JsonObject options, string message)
        {
            return new ResponseJsonReport
            {
                Ok = ok,
                Message = message,
                Data = new JsonObject
                {
                    ["provider"] = provider,
                    ["endpoint"] = endpoint ?? "",
                    ["implementationPath"] = provider.Equals("opcua", StringComparison.OrdinalIgnoreCase)
                        ? "Use OPC UA read/subscribe as the preferred commercial current-value channel."
                        : "Use a strictly read-only S7 adapter for address/symbol reads when OPC UA is unavailable.",
                    ["status"] = "planned-read-only-provider",
                    ["usesTiaOpennessForCurrentValues"] = false,
                    ["usesTiaOpennessForTagDiscovery"] = true,
                    ["readOnly"] = true,
                    ["connectsNow"] = false,
                    ["writesPlcValues"] = false,
                    ["modifiesWatchTables"] = false,
                    ["usesForce"] = false,
                    ["acceptedTags"] = acceptedTags,
                    ["rejectedTags"] = rejectedTags,
                    ["warnings"] = warnings,
                    ["policy"] = policy,
                    ["options"] = options
                },
                Meta = new JsonObject
                {
                    ["timestamp"] = DateTime.Now,
                    ["success"] = ok
                }
            };
        }

        private static JsonObject ParseJsonObjectOrEmpty(string json, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(json)) return new JsonObject();
            try
            {
                return JsonNode.Parse(json) as JsonObject ?? new JsonObject();
            }
            catch (Exception ex)
            {
                throw new McpException(parameterName + " must be a JSON object. Parse error: " + ex.Message, ex, McpErrorCode.InvalidParams);
            }
        }

        [McpServerTool(Name = "GetOnlineState"), Description(
            "[L1][Category:PLC-Online][PreCondition:Connect+OpenProject]" +
            " Read the current online connection state of a PLC (Offline/Connecting/Online/Incompatible/NotReachable/Protected/Disconnecting)." +
            " Does NOT change state — purely a read operation." +
            " Use before GoOnline to check current state, or after DownloadToPlc to verify the CPU is reachable." +
            " State=Online means the PC is communicating with the physical CPU." +
            " State=Incompatible means online but firmware/config mismatch — download required." +
            " State=NotReachable means network or IP configuration issue." +
            " NOTE: This reports Openness connection state, NOT the CPU operating mode (RUN/STOP)." +
            " The TIA Portal public API does not expose CPU operating mode — check the CPU front panel LEDs or HMI for RUN/STOP status.")]
        public static ResponseOnlineState GetOnlineState(
            [Description("softwarePath: path to the PLC software, e.g. 'PLC_1'")] string softwarePath)
        {
            try
            {
                return Portal.GetOnlineState(softwarePath);
            }
            catch (PortalException pex)
            {
                // 路径解析不到是调用方的参数问题，不是服务器内部意外错误。
                throw new McpException(pex.Message, pex,
                    pex.Code == PortalErrorCode.NotFound ? McpErrorCode.InvalidParams : McpErrorCode.InternalError);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error reading online state for '{softwarePath}': {ex.Message}{McpHints.Recovery(ex)}", ex, McpErrorCode.InternalError);
            }
        }

        [McpServerTool(Name = "GoOnline"), Description(
            "[L1][Category:PLC-Online][ONLINE-CONNECT][PreCondition:Connect+OpenProject]" +
            " Establish an online connection from TIA Portal to the physical PLC." +
            " Required before DownloadToPlc to confirm reachability, or for future online monitoring tools." +
            " Returns State=Online on success." +
            " If ipAddress is omitted, uses the IP address configured in the project's hardware configuration." +
            " If ipAddress is provided, overrides the configured IP for this session (useful for commissioning with a different IP)." +
            " Common failures: NotReachable (wrong IP / no cable), Protected (CPU requires authentication — supply password), Incompatible (firmware mismatch).")]
        public static ResponseOnlineState GoOnline(
            [Description("softwarePath: path to the PLC software, e.g. 'PLC_1'")] string softwarePath,
            [Description("ipAddress: optional IP address override, e.g. '192.168.1.10'. Leave empty to use the project's configured IP.")] string ipAddress = "",
            [Description("password: optional CPU access password. Required when the CPU has read/write protection configured. Leave empty for unprotected CPUs.")] string password = "")
        {
            try
            {
                return Portal.GoOnline(
                    softwarePath,
                    string.IsNullOrWhiteSpace(ipAddress) ? null : ipAddress,
                    string.IsNullOrWhiteSpace(password) ? null : password);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error going online for '{softwarePath}': {ex.Message}{McpHints.Recovery(ex)}", ex, McpErrorCode.InternalError);
            }
        }

        [McpServerTool(Name = "GoOffline"), Description(
            "[L1][Category:PLC-Online][PreCondition:Connect+OpenProject]" +
            " Disconnect the online session between TIA Portal and the physical PLC." +
            " Safe to call even if not currently online. Always go offline when monitoring or download is complete.")]
        public static ResponseMessage GoOffline(
            [Description("softwarePath: path to the PLC software, e.g. 'PLC_1'")] string softwarePath)
        {
            try
            {
                return Portal.GoOffline(softwarePath);
            }
            catch (PortalException pex)
            {
                throw new McpException(pex.Message, pex,
                    pex.Code == PortalErrorCode.NotFound ? McpErrorCode.InvalidParams : McpErrorCode.InternalError);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error going offline for '{softwarePath}': {ex.Message}{McpHints.Recovery(ex)}", ex, McpErrorCode.InternalError);
            }
        }

        [McpServerTool(Name = "GoOfflineAll"), Description(
            "[L1][Category:PLC-Online][PreCondition:Connect+OpenProject]" +
            " Take EVERY PLC in the open project offline in one call and report each PLC's before/after online state." +
            " Use this whenever CompileSoftware/Export*/Import* is blocked by 'operation not permitted in online mode':" +
            " a UI-initiated online session or a second online PLC is NOT released by GoOffline on a single softwarePath." +
            " Fully autonomous — never ask the user to toggle online/offline in the TIA UI, and never OCR the toolbar.")]
        public static ResponseJsonReport GoOfflineAll()
        {
            try
            {
                var data = Portal.GoOfflineAll();
                bool all = data["allOffline"]?.GetValue<bool>() ?? false;
                return new ResponseJsonReport
                {
                    Ok = all,
                    Message = data["message"]?.ToString() ?? "GoOfflineAll completed.",
                    Data = data,
                    Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = all }
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"GoOfflineAll failed: {ex.Message}{McpHints.Recovery(ex)}", ex, McpErrorCode.InternalError);
            }
        }

        [McpServerTool(Name = "GetDeviceIpAddress"), Description(
            "[L1][Category:Hardware][PreCondition:Connect+OpenProject]" +
            " Read a device's configured IP address straight from the TIA project (Openness PROFINET node) —" +
            " NOT by probing the CPU over S7 and NOT by exporting/parsing AML. Returns the primary IE IP plus all network nodes" +
            " (address, subnet, type). This is the correct, fast way to discover a PLC's IP before GoOnline/ReadPlcLiveValuesS7.")]
        public static ResponseJsonReport GetDeviceIpAddress(
            [Description("devicePath: device name from GetProjectTree, e.g. 'PLC_1'.")] string devicePath)
        {
            try
            {
                var data = Portal.GetDeviceIpAddress(devicePath);
                bool found = data["found"]?.GetValue<bool>() ?? false;
                var ip = data["ipAddress"]?.ToString() ?? string.Empty;
                return new ResponseJsonReport
                {
                    Ok = found,
                    Message = found
                        ? $"{devicePath} IP: {(string.IsNullOrEmpty(ip) ? "(no address configured on any node)" : ip)}"
                        : (data["message"]?.ToString() ?? "Device not found."),
                    Data = data,
                    Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = found }
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"GetDeviceIpAddress failed for '{devicePath}': {ex.Message}{McpHints.Recovery(ex)}", ex, McpErrorCode.InternalError);
            }
        }

        [McpServerTool(Name = "GetProjectTopology"), Description(
            "[L1][Category:Hardware][PreCondition:Connect+OpenProject]" +
            " One-shot, read-only project topology from Openness: every device with its network nodes (IP, subnet, node type)." +
            " Call this early to understand the project's devices and subnets at a glance, instead of probing S7 or parsing AML.")]
        public static ResponseJsonReport GetProjectTopology()
        {
            try
            {
                var data = Portal.GetProjectTopology();
                int count = data["deviceCount"]?.GetValue<int>() ?? 0;
                return new ResponseJsonReport
                {
                    Ok = count > 0,
                    Message = $"Project topology: {count} device(s).",
                    Data = data,
                    Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = count > 0 }
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"GetProjectTopology failed: {ex.Message}{McpHints.Recovery(ex)}", ex, McpErrorCode.InternalError);
            }
        }

        [McpServerTool(Name = "DumpDeviceAttributes"), Description(
            "[L2][Category:Hardware][PreCondition:Connect+OpenProject]" +
            " Read-only inventory of EVERY Openness attribute exposed on a device's items (CPU, modules, interfaces, ports):" +
            " name, access mode (read-only vs read/write), current value, value type." +
            " Run this ONCE per CPU/firmware to learn what is actually exposed, then drive hardware reads/writes from that" +
            " ground truth instead of guessing attribute names. Optional nameFilter narrows to attributes whose name contains" +
            " a substring (e.g. 'protection', 'putget', 'ip'). NOTE: GetAttributeInfos() does not enumerate every gettable" +
            " attribute on all CPUs, so absence here means 'not enumerated', not a guaranteed 'no interface'.")]
        public static ResponseJsonReport DumpDeviceAttributes(
            [Description("devicePath: device name, CPU/program name, or full name (e.g. 'S7-1200 station_3', '安全PLC', 'S7-1500/ET200MP station_1').")] string devicePath,
            [Description("nameFilter: optional case-insensitive substring to narrow attribute names (e.g. 'protection'). Empty = all.")] string? nameFilter = null)
        {
            try
            {
                var data = Portal.DumpDeviceAttributes(devicePath, nameFilter);
                bool found = data["found"]?.GetValue<bool>() ?? false;
                int items = data["itemCount"]?.GetValue<int>() ?? 0;
                int attrs = data["totalAttributes"]?.GetValue<int>() ?? 0;
                int writable = data["writableAttributes"]?.GetValue<int>() ?? 0;
                return new ResponseJsonReport
                {
                    Ok = found,
                    Message = found
                        ? $"{devicePath}: {attrs} attribute(s) across {items} item(s) ({writable} writable)."
                        : (data["message"]?.ToString() ?? "Not found."),
                    Data = data,
                    Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = found }
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"DumpDeviceAttributes failed for '{devicePath}': {ex.Message}{McpHints.Recovery(ex)}", ex, McpErrorCode.InternalError);
            }
        }

        [McpServerTool(Name = "GetPutGetAccess"), Description(
            "[L2][Category:Hardware][PreCondition:Connect+OpenProject]" +
            " Read whether a CPU permits remote PUT/GET access — the precondition for ReadPlcLiveValuesS7 on DB areas." +
            " If enabled=false, S7 absolute reads of DBs will fail; enable with SetPutGetAccess (then hardware DownloadToPlc).")]
        public static ResponseJsonReport GetPutGetAccess(
            [Description("devicePath: device name from GetProjectTree, e.g. 'PLC_1'.")] string devicePath)
        {
            try
            {
                var data = Portal.GetPutGetAccess(devicePath);
                bool found = data["found"]?.GetValue<bool>() ?? false;
                bool enabled = data["enabled"]?.GetValue<bool>() ?? false;
                return new ResponseJsonReport
                {
                    Ok = found,
                    Message = found
                        ? $"PUT/GET access on {devicePath}: {(enabled ? "ENABLED" : "DISABLED")} (attribute '{data["attributeName"]}')."
                        : (data["message"]?.ToString() ?? "Not found."),
                    Data = data,
                    Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = found }
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"GetPutGetAccess failed for '{devicePath}': {ex.Message}{McpHints.Recovery(ex)}", ex, McpErrorCode.InternalError);
            }
        }

        [McpServerTool(Name = "SetPutGetAccess"), Description(
            "[L2][Category:Hardware][CONFIG-WRITE][PreCondition:Connect+OpenProject]" +
            " Enable or disable remote PUT/GET access on a CPU (the precondition for S7 DB reads)." +
            " This is a hardware-configuration change — you must run DownloadToPlc afterwards for it to take effect on the live CPU." +
            " Returns before/after readback evidence.")]
        public static ResponseJsonReport SetPutGetAccess(
            [Description("devicePath: device name from GetProjectTree, e.g. 'PLC_1'.")] string devicePath,
            [Description("enable: true to permit remote PUT/GET access, false to forbid it.")] bool enable = true)
        {
            try
            {
                var data = Portal.SetPutGetAccess(devicePath, enable);
                bool ok = data["ok"]?.GetValue<bool>() ?? false;
                return new ResponseJsonReport
                {
                    Ok = ok,
                    Message = ok
                        ? $"PUT/GET access on {devicePath} set to {enable}. Download hardware config to apply."
                        : (data["message"]?.ToString() ?? "SetPutGetAccess failed."),
                    Data = data,
                    Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = ok }
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"SetPutGetAccess failed for '{devicePath}': {ex.Message}{McpHints.Recovery(ex)}", ex, McpErrorCode.InternalError);
            }
        }

        // Autonomy helper: when a compile/export/import is blocked because TIA is in online mode,
        // take ALL PLCs offline via Openness and retry once — never hand the toggle back to the user.
        internal static bool IsOnlineModeError(Exception ex)
        {
            for (Exception? e = ex; e != null; e = e.InnerException)
            {
                var m = e.Message ?? string.Empty;
                if (m.IndexOf("online mode", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                if (m.IndexOf("not permitted", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    m.IndexOf("online", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        internal static T WithAutoOffline<T>(Func<T> op)
        {
            try { return op(); }
            catch (Exception ex) when (IsOnlineModeError(ex))
            {
                try { Portal.GoOfflineAll(); } catch { }
                return op(); // retry once, now fully offline
            }
        }

        [McpServerTool(Name = "CompareSoftwareToOnline"), Description(
            "[L2][Category:PLC-Online][PreCondition:Connect+OpenProject+GoOnline]" +
            " Compare the offline PLC software in the project against the program currently running on the physical CPU." +
            " Use after editing blocks to confirm what differs from the live CPU before downloading," +
            " or after a download to verify offline/online consistency." +
            " Returns a tree-walked list of differences (only entries where ComparisonResult is not 'Equal' are reported)." +
            " Requires GoOnline to be called first; will return IsOnline=false with guidance otherwise.")]
        public static ResponseCompare CompareSoftwareToOnline(
            [Description("softwarePath: path to the PLC software, e.g. 'PLC_1'")] string softwarePath,
            [Description("maxDepth: maximum tree depth to walk (default 4). Lower = faster but less detail.")] int maxDepth = 4,
            [Description("maxEntries: cap on differences returned (default 200). Truncated=true in response if reached.")] int maxEntries = 200)
        {
            try
            {
                return Portal.CompareSoftwareToOnline(softwarePath, maxDepth, maxEntries);
            }
            catch (PortalException pex)
            {
                throw new McpException(pex.Message, pex,
                    pex.Code == PortalErrorCode.NotFound ? McpErrorCode.InvalidParams : McpErrorCode.InternalError);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error comparing '{softwarePath}' to online: {ex.Message}{McpHints.Recovery(ex)}", ex, McpErrorCode.InternalError);
            }
        }

        [McpServerTool(Name = "CheckDownloadReadiness"), Description(
            "[L1][Category:PLC-Online][PreCondition:Connect+OpenProject+CompileSoftware]" +
            " Check whether a PLC is ready to receive a program download WITHOUT actually downloading." +
            " Verifies: DownloadProvider service is available, a network/IP configuration exists in the hardware config." +
            " Returns Ready=true only when all checks pass." +
            " Use this before DownloadToPlc to surface problems early (missing IP, no hardware config, etc.)." +
            " Meta.downloadRoutes lists every PG/PC interface -> CPU route (best-ranked first, preferred=true when the" +
            " adapter shares a subnet with the CPU) — check it on a multi-NIC PC (WLAN/VPN/PLCSIM) before downloading." +
            " Does NOT compile — run CompileSoftware first to ensure blocks are consistent.")]
        public static ResponseCheckDownload CheckDownloadReadiness(
            [Description("softwarePath: path to the PLC software in the project tree, e.g. 'PLC_1'")] string softwarePath)
        {
            try
            {
                return Portal.CheckDownloadReadiness(softwarePath);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error checking download readiness for '{softwarePath}': {ex.Message}{McpHints.Recovery(ex)}", ex, McpErrorCode.InternalError);
            }
        }

        [McpServerTool(Name = "DownloadToPlc"), Description(
            "[L1][Category:PLC-Online][ONLINE-WRITE][PreCondition:Connect+OpenProject+CompileSoftware+CheckDownloadReadiness]" +
            " Download the compiled PLC program to the physical CPU over the network." +
            " The CPU will stop briefly during download and restart automatically (controlled by startAfterDownload)." +
            " SAFETY: Verify no personnel are near the machine before downloading. This changes live PLC behavior." +
            " Workflow: Connect → OpenProject → CompileSoftware → CheckDownloadReadiness → DownloadToPlc → GetOnlineState." +
            " On success State=Success or Warning. On Error check Errors[] for details." +
            " Default options (keepActualValues=true, consistentBlocksOnly=true) are safe for most scenarios." +
            " Set keepActualValues=false only when DB initial values must be reset — this is irreversible." +
            " On a multi-NIC PC the PG/PC interface is picked automatically (the adapter sharing a subnet with the CPU);" +
            " Meta.pgPcRoute reports which one was used. Override with pgPcInterface / targetIpAddress when the pick is wrong.")]
        public static ResponseDownload DownloadToPlc(
            [Description("softwarePath: path to the PLC software, e.g. 'PLC_1'")] string softwarePath,
            [Description("consistentBlocksOnly: true=download only consistent blocks (safe default), false=download all blocks even inconsistent ones")] bool consistentBlocksOnly = true,
            [Description("keepActualValues: true=preserve current DB actual values (safe default), false=reset all DB values to initial values (irreversible)")] bool keepActualValues = true,
            [Description("startAfterDownload: true=automatically set CPU to RUN after download (default), false=leave CPU in STOP")] bool startAfterDownload = true,
            [Description("stopBeforeDownload: true=automatically stop CPU before download (required for most downloads), false=attempt online download without stopping")] bool stopBeforeDownload = true,
            [Description("password: optional CPU access password. Required when the CPU has download protection configured. Leave empty for unprotected CPUs.")] string password = "",
            [Description("pgPcInterface: optional PG/PC interface name (substring, case-insensitive), e.g. 'PLCSIM' or 'Realtek'. Leave empty to auto-pick the adapter that shares a subnet with the CPU. Run CheckDownloadReadiness to see the available names.")] string pgPcInterface = "",
            [Description("targetIpAddress: optional CPU IP to download to, e.g. '192.168.0.1'. Disambiguates which route to use when the project has several CPU interfaces. Leave empty to auto-pick.")] string targetIpAddress = "")
        {
            try
            {
                var result = Portal.DownloadToPlc(
                    softwarePath,
                    consistentBlocksOnly,
                    keepActualValues,
                    startAfterDownload,
                    stopBeforeDownload,
                    string.IsNullOrWhiteSpace(password) ? null : password,
                    string.IsNullOrWhiteSpace(pgPcInterface) ? null : pgPcInterface,
                    string.IsNullOrWhiteSpace(targetIpAddress) ? null : targetIpAddress);

                if (result.Ok == false && result.Errors != null && result.Errors.Length > 0)
                    throw new McpException(
                        $"Download to '{softwarePath}' failed: {result.Message}",
                        McpErrorCode.InternalError);

                return result;
            }
            catch (McpException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new McpException($"Unexpected error downloading to '{softwarePath}': {ex.Message}{McpHints.Recovery(ex)}", ex, McpErrorCode.InternalError);
            }
        }
    }
}
