using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using TiaMcpServer.Siemens;

namespace TiaMcpServer.ModelContextProtocol
{
    internal sealed class ProjectStepOutcome
    {
        private ProjectStepOutcome(bool succeeded, string? detail) { Succeeded = succeeded; Detail = detail; }
        public bool Succeeded { get; }
        public string? Detail { get; }
        public static ProjectStepOutcome Ok(string? detail = null) => new ProjectStepOutcome(true, detail);
        public static ProjectStepOutcome Failed(string? detail) => new ProjectStepOutcome(false, detail);

        public static ProjectStepOutcome FromResponse(ResponseMessage? response)
        {
            if (response == null) return Failed("The operation returned no result.");
            if (!IsTrue(response.Meta?["success"]))
                return Failed((response.Message ?? "The operation did not confirm success.") + " (meta.success is not true)");
            foreach (var field in new[] { "failed", "failures" })
                if (response.Meta?[field] is JsonArray failures && failures.Count > 0)
                    return Failed(string.Join("; ", failures.Select(item => item?.ToString() ?? "Unspecified failure")));
            foreach (var field in new[] { "verified", "bindingVerified" })
                if (response.Meta!.ContainsKey(field) && !IsTrue(response.Meta[field]))
                    return Failed((response.Meta["verifyDetail"] ?? response.Meta["bindingGuidance"])?.ToString()
                        ?? (response.Message + " (" + field + " is not true)"));
            return Ok(response.Message);
        }

        public static ProjectStepOutcome FromImport(ResponsePlcProgramImport? response)
        {
            if (response == null) return Failed("The PLC import returned no result.");
            var failures = response.Failed?.ToArray() ?? Array.Empty<ImportFailure>();
            if (failures.Length > 0)
                return Failed(string.Join("; ", failures.Select(f => f == null ? "Unspecified import failure" : f.Path + ": " + f.Error)));
            var result = FromResponse(response);
            if (!result.Succeeded) return result;
            if (response.DryRun != false) return Failed("The PLC import returned a dry-run or unspecified execution result.");
            var imported = new[] { response.ImportedTypes, response.ImportedTagTables, response.ImportedTechnologyObjects, response.ImportedBlocks };
            if (!imported.Any(items => items != null && items.Any(name => !string.IsNullOrWhiteSpace(name))))
                return Failed("The PLC import reported success without any imported objects.");
            if (response.Compile != null)
            {
                var compile = CompileResult(response.Compile, response.Compile.State, response.Compile.ErrorCount, response.Compile.WarningCount);
                if (!compile.Succeeded) return compile;
            }
            return result;
        }

        public static ProjectStepOutcome FromCompile(ResponseCompileDiagnose? response) => response == null
            ? Failed("The compiler returned no result.")
            : CompileResult(response, response.State, response.ErrorCount, response.WarningCount);

        private static ProjectStepOutcome CompileResult(ResponseMessage response, string? state, int? errors, int? warnings)
        {
            var result = FromResponse(response);
            if (!result.Succeeded) return result;
            var detail = $"state={state} errors={errors?.ToString() ?? "unknown"} warnings={warnings?.ToString() ?? "unknown"}";
            return errors == 0 && !string.IsNullOrWhiteSpace(state) && !string.Equals(state, "Error", StringComparison.OrdinalIgnoreCase)
                ? Ok(detail) : Failed(detail);
        }

        internal static bool IsTrue(JsonNode? node) => node is JsonValue value && value.TryGetValue<bool>(out var result) && result;
    }

    internal sealed class ProjectStepRunner
    {
        public ProjectStepRunner(ResponseScaffold response) { Response = response ?? throw new ArgumentNullException(nameof(response)); }
        public ResponseScaffold Response { get; }
        public bool Stopped { get; private set; }

        public bool Run(string name, Func<ProjectStepOutcome> operation, string? context = null)
        {
            if (operation == null) throw new ArgumentNullException(nameof(operation));
            if (Stopped)
            {
                Skip(name, "TIA connection was lost; inspect the project state before continuing.");
                return false;
            }
            try
            {
                var outcome = operation() ?? ProjectStepOutcome.Failed("The operation returned no outcome.");
                var detail = string.IsNullOrWhiteSpace(context) ? outcome.Detail : context + ": " + outcome.Detail;
                Response.Steps.Add(new ScaffoldStep { Step = name, Status = outcome.Succeeded ? "ok" : "failed", Detail = detail });
                if (!outcome.Succeeded) Response.Ok = false;
                return outcome.Succeeded;
            }
            catch (Exception ex)
            {
                Fail(name, ex, context);
                return false;
            }
        }

        public bool RunSequence(string name, string context, params Func<ProjectStepOutcome>[] operations)
        {
            if (operations == null || operations.Length == 0 || operations.Any(operation => operation == null))
                throw new ArgumentException("At least one operation is required.", nameof(operations));
            return Run(name, () =>
            {
                ProjectStepOutcome result = ProjectStepOutcome.Ok();
                foreach (var operation in operations)
                {
                    result = operation() ?? ProjectStepOutcome.Failed("The operation returned no outcome.");
                    if (!result.Succeeded) return result;
                }
                return result;
            }, context);
        }

        public void Skip(string name, string detail) =>
            Response.Steps.Add(new ScaffoldStep { Step = name, Status = "skipped", Detail = detail });

        internal void Fail(string name, Exception error, string? context = null)
        {
            Response.Ok = false;
            Stopped |= PortalFailureClassifier.IsPortalProcessLost(error);
            Response.Steps.Add(new ScaffoldStep
            {
                Step = name,
                Status = "failed",
                Detail = (string.IsNullOrWhiteSpace(context) ? "" : context + ": ") + error.Message
                    + (Stopped ? " TIA connection was lost; inspect the project state before continuing." : "")
            });
        }
    }

    internal static class ProjectWorkflow
    {
        public static ResponseScaffold Run(string json, ProjectWorkflowMode mode, bool dryRun,
            Func<ProjectPlcArtifact, ProjectStepOutcome> validatePlc,
            Func<ProjectSpecification, ProjectStepRunner, bool> initialize,
            Action<ProjectSpecification, ProjectStepRunner> apply,
            Func<ResponseMessage> save)
        {
            if (validatePlc == null) throw new ArgumentNullException(nameof(validatePlc));
            if (initialize == null) throw new ArgumentNullException(nameof(initialize));
            if (apply == null) throw new ArgumentNullException(nameof(apply));
            if (save == null) throw new ArgumentNullException(nameof(save));
            var response = new ResponseScaffold { Ok = true };
            var runner = new ProjectStepRunner(response);
            ProjectSpecification spec;
            try
            {
                spec = ProjectSpecification.Parse(json, mode);
                response.ProjectName = spec.ProjectName;
                response.DirectoryPath = spec.DirectoryPath;
            }
            catch (Exception ex)
            {
                runner.Fail("validateSpec", ex);
                runner.Skip("save", "Correct the specification before execution.");
                return Complete(response, mode, dryRun, false);
            }

            Validate(spec, runner, validatePlc);
            if (!response.Ok || dryRun)
            {
                if (!response.Ok) runner.Skip("save", "Correct the failed preflight checks before execution.");
                return Complete(response, mode, dryRun, false);
            }

            // Successful preflight details belong to dry-run reports; execution reports keep one summary step.
            response.Steps.Clear();
            runner.Run("validateSpec", () => ProjectStepOutcome.Ok("All offline preflight checks passed."));
            try
            {
                if (!initialize(spec, runner))
                {
                    if (response.Ok) runner.Run("initialize", () => ProjectStepOutcome.Failed("Project initialization did not complete."));
                }
                else if (!runner.Stopped)
                {
                    apply(spec, runner);
                }
            }
            catch (Exception ex)
            {
                runner.Fail("execute", ex);
            }

            if (spec.Save && response.Ok)
                runner.Run("save", () => ProjectStepOutcome.FromResponse(save()));
            else
                runner.Skip("save", spec.Save
                    ? "Execution has failed steps. Review the current in-memory project and the step report before saving."
                    : "save=false; changes remain in the current in-memory project.");
            return Complete(response, mode, dryRun, true);
        }

        private static void Validate(ProjectSpecification spec, ProjectStepRunner runner, Func<ProjectPlcArtifact, ProjectStepOutcome> validatePlc)
        {
            if (spec.Mode == ProjectWorkflowMode.Patch)
                runner.Run("projectPath", () => ProjectStepOutcome.Ok(ProjectFilePath.Resolve(spec.ProjectPath)));
            foreach (var artifact in spec.PlcArtifacts)
                runner.Run(artifact.Kind, () => validatePlc(artifact), artifact.SpecPath);
            for (var i = 0; i < spec.SclSourceFiles.Count; i++)
            {
                var path = spec.SclSourceFiles[i];
                runner.Run("scl", () => ExistingFile(path), "$.sclSourceFiles[" + i + "]");
            }
            foreach (var document in spec.LadDocuments)
                runner.Run("lad", () => ExistingFile(Path.Combine(document.ImportPath, document.Name + ".s7dcl")), document.SpecPath);
            foreach (var screen in spec.HmiScreens)
                runner.Run("hmiScreen", () => ProjectStepOutcome.Ok(screen.Name + ": design JSON validated."));
            foreach (var tag in spec.HmiTags)
                runner.Run("hmiTag", () => ProjectStepOutcome.Ok(tag.TableName + "/" + tag.Name));
        }

        private static ProjectStepOutcome ExistingFile(string path) => File.Exists(path)
            ? ProjectStepOutcome.Ok("exists: " + path) : ProjectStepOutcome.Failed("File not found: " + path);

        private static ResponseScaffold Complete(ResponseScaffold response, ProjectWorkflowMode mode, bool dryRun, bool executionStarted)
        {
            var tool = mode == ProjectWorkflowMode.Create ? "ScaffoldProject" : "PatchProject";
            var ok = response.Steps.Count(step => step.Status == "ok");
            var failed = response.Steps.Count(step => step.Status == "failed");
            response.Message = $"{tool}{(dryRun ? " dryRun" : "")} '{response.ProjectName}': {ok} ok, {failed} failed. "
                + (executionStarted ? $"Compile state={response.CompileState ?? "(skipped)"} errors={response.CompileErrorCount}."
                    : "Offline validation completed; TIA execution starts after all preflight checks pass.");
            response.Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = response.Ok };
            if (dryRun) response.Meta["dryRun"] = true;
            return response;
        }

        public static string SelectHmiPath(ProjectSpecification spec, IEnumerable<ProjectHmiTarget> targets)
        {
            if (spec == null) throw new ArgumentNullException(nameof(spec));
            if (targets == null) throw new ArgumentNullException(nameof(targets));
            var explicitPath = !string.IsNullOrWhiteSpace(spec.HmiSoftwarePath);
            var requested = explicitPath ? spec.HmiSoftwarePath : spec.HmiName;
            if (string.IsNullOrWhiteSpace(requested)) throw new ArgumentException("An HMI device or software path is required.");
            bool Same(string value) => string.Equals(value, requested, StringComparison.OrdinalIgnoreCase);
            var scoped = targets.Where(target => string.Equals(target.DevicePath, spec.HmiName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(target.DeviceName, spec.HmiName, StringComparison.OrdinalIgnoreCase));
            var matches = scoped.Where(target => explicitPath
                ? Same(target.SoftwarePath) || Same(target.SoftwareName) || Same(target.DevicePath) || Same(target.DeviceName)
                : Same(target.DevicePath) || Same(target.DeviceName)).ToArray();
            if (matches.Length == 0)
                throw new InvalidOperationException("HMI target '" + requested + "' was not found in device '" + spec.HmiName
                    + "'. Use that device's qualified hmiSoftwarePath from the project tree.");
            if (matches.Length > 1)
                throw new InvalidOperationException("HMI target '" + requested + "' is ambiguous: "
                    + string.Join(", ", matches.Select(target => target.SoftwarePath)) + ". Set an explicit device-qualified hmiSoftwarePath.");
            return matches[0].SoftwarePath;
        }
    }

    internal sealed class ProjectHmiTarget
    {
        public ProjectHmiTarget(string deviceName, string devicePath, string softwareName, string softwarePath)
        { DeviceName = deviceName; DevicePath = devicePath; SoftwareName = softwareName; SoftwarePath = softwarePath; }
        public string DeviceName { get; }
        public string DevicePath { get; }
        public string SoftwareName { get; }
        public string SoftwarePath { get; }
    }
}
