using ModelContextProtocol;
using ModelContextProtocol.Server;
using System;
using System.ComponentModel;
using System.IO;
using System.Text.Json.Nodes;

namespace TiaMcpServer.ModelContextProtocol
{
    // Offline release validation and handoff artifacts.
    public static partial class McpServer
    {

        [McpServerTool(Name = "RunOfflineReleaseValidationSuite"), Description("[L2][Validation]Offline-only helper: run the release smoke suite covering PLC Builder, Classic HMI, PLC symbol extraction, Unified HMI template layout, HMI action recipes, and online-monitoring safety guardrails. It does not connect to TIA Portal or modify projects.")]
        public static ResponseJsonReport RunOfflineReleaseValidationSuite(
            [Description("workspaceRoot: repository/workspace root containing TMP_EXPORT, docs, and tools.")] string workspaceRoot,
            [Description("reportDirectory: directory where suite files and reports will be written.")] string reportDirectory)
        {
            try
            {
                var data = OfflineReleaseValidationSuite.Run(workspaceRoot, reportDirectory);
                var ok = data["ok"]?.GetValue<bool>() == true;
                return new ResponseJsonReport
                {
                    Ok = ok,
                    Message = ok ? "Offline release validation suite passed" : "Offline release validation suite found issues",
                    Data = data,
                    Meta = new JsonObject
                    {
                        ["timestamp"] = DateTime.Now,
                        ["success"] = ok,
                        ["offlineOnly"] = true
                    }
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error running offline release validation suite: {ex.Message}{McpHints.Recovery(ex)}", ex, McpErrorCode.InternalError);
            }
        }

        [McpServerTool(Name = "RunV2PlanCompletionAudit"), Description("[L2][Validation]Offline-only strict audit for docs/TIA_MCP_常见操作全覆盖方案_V2_二次优化计划.md. It reports verified hard-gate percentage and blocks 100% claims when real TIA/online evidence is missing.")]
        public static ResponseJsonReport RunV2PlanCompletionAudit(
            [Description("workspaceRoot: repository/workspace root containing docs, tools, and reports.")] string workspaceRoot,
            [Description("reportDirectory: directory where V2 audit reports will be written.")] string reportDirectory)
        {
            try
            {
                var data = V2PlanCompletionAuditor.Run(workspaceRoot, reportDirectory);
                var ok = data["ok"]?.GetValue<bool>() == true;
                return new ResponseJsonReport
                {
                    Ok = ok,
                    Message = "V2 plan completion audit finished",
                    Data = data,
                    Meta = new JsonObject
                    {
                        ["timestamp"] = DateTime.Now,
                        ["success"] = ok,
                        ["offlineOnly"] = true
                    }
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error running V2 plan completion audit: {ex.Message}{McpHints.Recovery(ex)}", ex, McpErrorCode.InternalError);
            }
        }

        [McpServerTool(Name = "BuildReleaseDiagnosticReport"), Description("[L2][Reports]Build an offline diagnostic report from a previously generated OfflineReleaseValidationSuite JSON report. It does not connect to TIA Portal or modify projects.")]
        public static ResponseJsonReport BuildReleaseDiagnosticReport(
            [Description("offlineReleaseSuiteJsonPath: path to offline_release_validation_suite_*.json.")] string offlineReleaseSuiteJsonPath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(offlineReleaseSuiteJsonPath) || !File.Exists(offlineReleaseSuiteJsonPath))
                    throw new FileNotFoundException("Offline release suite JSON report not found.", offlineReleaseSuiteJsonPath);
                var root = JsonNode.Parse(File.ReadAllText(offlineReleaseSuiteJsonPath)) as JsonObject
                    ?? throw new InvalidOperationException("Offline release suite JSON root must be an object.");
                var data = ReleaseDiagnosticReportBuilder.Build(root);
                return new ResponseJsonReport
                {
                    Ok = data["ok"]?.GetValue<bool>() == true,
                    Message = "Release diagnostic report built.",
                    Data = data,
                    Meta = new JsonObject
                    {
                        ["timestamp"] = DateTime.Now,
                        ["success"] = true,
                        ["offlineOnly"] = true
                    }
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error building release diagnostic report: {ex.Message}{McpHints.Recovery(ex)}", ex, McpErrorCode.InternalError);
            }
        }

        [McpServerTool(Name = "BuildReleaseRunbook"), Description("[L2][Reports]Build an offline first-user runbook from a previously generated OfflineReleaseValidationSuite JSON report. It does not connect to TIA Portal or modify projects.")]
        public static ResponseJsonReport BuildReleaseRunbook(
            [Description("offlineReleaseSuiteJsonPath: path to offline_release_validation_suite_*.json.")] string offlineReleaseSuiteJsonPath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(offlineReleaseSuiteJsonPath) || !File.Exists(offlineReleaseSuiteJsonPath))
                    throw new FileNotFoundException("Offline release suite JSON report not found.", offlineReleaseSuiteJsonPath);
                var root = JsonNode.Parse(File.ReadAllText(offlineReleaseSuiteJsonPath)) as JsonObject
                    ?? throw new InvalidOperationException("Offline release suite JSON root must be an object.");
                var diagnostics = root["diagnostics"] as JsonObject ?? ReleaseDiagnosticReportBuilder.Build(root);
                var data = ReleaseRunbookBuilder.Build(root, diagnostics);
                return new ResponseJsonReport
                {
                    Ok = data["ok"]?.GetValue<bool>() == true,
                    Message = "Release runbook built.",
                    Data = data,
                    Meta = new JsonObject
                    {
                        ["timestamp"] = DateTime.Now,
                        ["success"] = true,
                        ["offlineOnly"] = true
                    }
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error building release runbook: {ex.Message}{McpHints.Recovery(ex)}", ex, McpErrorCode.InternalError);
            }
        }

        [McpServerTool(Name = "BuildReleaseManifest"), Description("[L2][Reports]Build an offline machine-readable release manifest from a previously generated OfflineReleaseValidationSuite JSON report. It does not connect to TIA Portal or modify projects.")]
        public static ResponseJsonReport BuildReleaseManifest(
            [Description("offlineReleaseSuiteJsonPath: path to offline_release_validation_suite_*.json.")] string offlineReleaseSuiteJsonPath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(offlineReleaseSuiteJsonPath) || !File.Exists(offlineReleaseSuiteJsonPath))
                    throw new FileNotFoundException("Offline release suite JSON report not found.", offlineReleaseSuiteJsonPath);
                var root = JsonNode.Parse(File.ReadAllText(offlineReleaseSuiteJsonPath)) as JsonObject
                    ?? throw new InvalidOperationException("Offline release suite JSON root must be an object.");
                var diagnostics = root["diagnostics"] as JsonObject ?? ReleaseDiagnosticReportBuilder.Build(root);
                var runbook = root["runbook"] as JsonObject ?? ReleaseRunbookBuilder.Build(root, diagnostics);
                var data = ReleaseManifestBuilder.Build(root, diagnostics, runbook);
                return new ResponseJsonReport
                {
                    Ok = data["ok"]?.GetValue<bool>() == true,
                    Message = "Release manifest built.",
                    Data = data,
                    Meta = new JsonObject
                    {
                        ["timestamp"] = DateTime.Now,
                        ["success"] = true,
                        ["offlineOnly"] = true
                    }
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error building release manifest: {ex.Message}{McpHints.Recovery(ex)}", ex, McpErrorCode.InternalError);
            }
        }

        [McpServerTool(Name = "RebuildReleaseHandoffArtifacts"), Description("[L2][Reports]Rebuild diagnostics, runbook, and manifest files from an existing OfflineReleaseValidationSuite JSON report. Offline-only and does not connect to TIA Portal.")]
        public static ResponseJsonReport RebuildReleaseHandoffArtifacts(
            [Description("offlineReleaseSuiteJsonPath: path to offline_release_validation_suite_*.json.")] string offlineReleaseSuiteJsonPath,
            [Description("outputDirectory: directory where rebuilt handoff artifacts will be written.")] string outputDirectory)
        {
            try
            {
                var data = ReleaseHandoffArtifactBuilder.RebuildFromSuiteJson(offlineReleaseSuiteJsonPath, outputDirectory);
                return new ResponseJsonReport
                {
                    Ok = data["ok"]?.GetValue<bool>() == true,
                    Message = "Release handoff artifacts rebuilt.",
                    Data = data,
                    Meta = new JsonObject
                    {
                        ["timestamp"] = DateTime.Now,
                        ["success"] = true,
                        ["offlineOnly"] = true
                    }
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error rebuilding release handoff artifacts: {ex.Message}{McpHints.Recovery(ex)}", ex, McpErrorCode.InternalError);
            }
        }
    }
}
