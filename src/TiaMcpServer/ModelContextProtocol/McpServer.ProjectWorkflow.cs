using System.Xml.Linq;

namespace TiaMcpServer.ModelContextProtocol
{
    public static partial class McpServer
    {
        private static ResponseScaffold RunProjectWorkflow(string json, ProjectWorkflowMode mode, bool dryRun, bool noOverwrite = false)
        {
            var hmiReady = mode == ProjectWorkflowMode.Patch;
            return ProjectWorkflow.Run(json, mode, dryRun, ValidateProjectPlcArtifact,
                (spec, runner) => InitializeProjectWorkflow(spec, runner, out hmiReady),
                (spec, runner) =>
                {
                    var plcOk = ApplyProjectPlc(spec, runner, noOverwrite);
                    if (spec.HasHmi)
                    {
                        if (hmiReady && plcOk) ApplyProjectHmi(spec, runner);
                        else runner.Skip("hmi", "Complete the failed PLC or HMI-device steps before applying HMI changes.");
                    }
                }, () => SaveProject());
        }

        private static ProjectStepOutcome ValidateProjectPlcArtifact(ProjectPlcArtifact artifact)
        {
            var build = BuildPlcArtifact(artifact.Kind, artifact.Json);
            if (!ProjectStepOutcome.IsTrue(build["ok"]))
                return ProjectStepOutcome.Failed(build["error"]?.ToString() ?? "PLC XML validation failed.");
            var xml = build["xml"]?.ToString();
            if (string.IsNullOrWhiteSpace(xml)) return ProjectStepOutcome.Failed("The PLC builder returned no XML.");
            XDocument.Parse(xml!);
            return ProjectStepOutcome.Ok("PLC XML built and validated offline.");
        }

        private static bool InitializeProjectWorkflow(ProjectSpecification spec, ProjectStepRunner runner, out bool hmiReady)
        {
            hmiReady = spec.Mode == ProjectWorkflowMode.Patch;
            if (!runner.Run("connect", () => Portal.IsConnected()
                ? ProjectStepOutcome.Ok("already connected") : ProjectStepOutcome.FromResponse(Connect()))) return false;
            if (spec.Mode == ProjectWorkflowMode.Patch)
                return runner.Run("openProject", () => ProjectStepOutcome.FromResponse(OpenProject(spec.ProjectPath)), spec.ProjectPath);

            if (!runner.Run("createProject", () => ProjectStepOutcome.FromResponse(CreateProject(spec.DirectoryPath, spec.ProjectName)), spec.DirectoryPath)) return false;
            if (!runner.Run("addDevicePlc", () => ProjectDeviceResult(AddDeviceWithFallback(spec.PlcMlfb, "", spec.PlcName, spec.PlcFamily)), spec.PlcName)) return false;
            if (spec.HasHmi)
                hmiReady = runner.Run("addDeviceHmi", () => ProjectDeviceResult(AddDeviceWithFallback("", "", spec.HmiName, spec.HmiFamily)), spec.HmiName);
            return true;
        }

        private static ProjectStepOutcome ProjectDeviceResult(ResponseDeviceProbe response) => response.Ok == true
            ? ProjectStepOutcome.FromResponse(response)
            : ProjectStepOutcome.Failed(response.Error ?? response.Message ?? "Device creation failed.");
    }
}
