using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using TiaMcpServer.ModelContextProtocol;

namespace TiaMcpServer.Tests
{
    internal static class ProjectWorkflowTests
    {
        public static void Run(Action<bool, string> check)
        {
            CheckPreflight(check);
            CheckExecution(check);
            CheckResults(check);
            CheckHmiSelection(check);
        }

        private static void CheckPreflight(Action<bool, string> check)
        {
            var harness = new Harness();
            var dry = harness.Run("{\"projectName\":\"Demo\",\"udt\":[{}]}", true);
            check(dry.Ok && harness.Validations == 1 && harness.Initializations == 0 && harness.Applications == 0 && harness.Saves == 0, "workflow: successful dryRun validates without starting TIA or saving");
            check(dry.Meta?["dryRun"]?.GetValue<bool>() == true && dry.Meta["success"]?.GetValue<bool>() == true, "workflow: dryRun retains its response metadata");

            harness = new Harness { Validator = _ => ProjectStepOutcome.Failed("Invalid generated XML") };
            var failed = harness.Run("{\"projectName\":\"Demo\",\"udt\":[{},{}]}");
            check(!failed.Ok && harness.Validations == 2 && harness.Initializations == 0 && harness.Saves == 0, "workflow: all PLC preflight checks run and a failed build blocks initialization");
            check(failed.Steps.Any(step => step.Detail != null && step.Detail.Contains("$.udt[1]")), "workflow: per-artifact validation failures preserve their JSON paths");

            harness = new Harness();
            var badSpec = harness.Run("{\"projectName\":\"Demo\",\"udt\":[{}],\"hmiTags\":[42]}");
            check(!badSpec.Ok && harness.Validations == 0 && harness.Initializations == 0 && harness.Applications == 0, "workflow: a late structural error is found before any builder or TIA action");
            check(badSpec.Steps[0].Step == "validateSpec" && badSpec.Steps[0].Status == "failed", "workflow: structural errors have a failed response step");
            check(!new Harness().Run("{").Ok, "workflow: malformed JSON produces a failed preflight report");
            harness = new Harness();
            var invalidProperties = harness.Run("""
                {"projectName":"Demo","hmiName":"Panel","hmiScreens":[{"screenName":"Main","designJson":
                  {"screen":{"BackColor":"#FF0000"},"items":[{"name":"R","type":"Rectangle","properties":{"":1}}]}}]}
                """);
            check(!invalidProperties.Ok && harness.Initializations == 0 && harness.Applications == 0 && harness.Saves == 0,
                "workflow: an empty design property name is rejected before screen creation or property writes");

            var directory = Path.Combine(Path.GetTempPath(), "tia_workflow_tests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var project = Path.Combine(directory, "Demo.ap21");
                var source = Path.Combine(directory, "Main.scl");
                File.WriteAllText(project, "offline fixture");
                File.WriteAllText(source, "offline fixture");
                File.WriteAllText(Path.Combine(directory, "Main.s7dcl"), "offline fixture");
                var root = new JsonObject
                {
                    ["projectPath"] = project,
                    ["sclSourceFiles"] = new JsonArray(source),
                    ["ladDocs"] = new JsonArray(new JsonObject { ["importPath"] = directory, ["name"] = "Main" }),
                    ["hmiName"] = "Panel",
                    ["hmiScreens"] = new JsonArray(new JsonObject { ["screenName"] = "Main", ["designJson"] = new JsonObject { ["items"] = new JsonArray() } }),
                    ["hmiTags"] = new JsonArray(new JsonObject { ["tagName"] = "Start" })
                };
                harness = new Harness();
                var valid = harness.Run(root.ToJsonString(), true, ProjectWorkflowMode.Patch);
                check(valid.Ok && valid.ProjectName == "Demo" && valid.DirectoryPath == project, "workflow: patch dryRun validates an existing V21 fixture and keeps response paths");
                check(valid.Steps.Count(step => step.Status == "ok") == 5 && harness.Initializations == 0, "workflow: patch, SCL, LAD, screen, and tag preflight all run offline");
                root["sclSourceFiles"] = new JsonArray(source, Path.Combine(directory, "missing.scl"));
                harness = new Harness();
                var missing = harness.Run(root.ToJsonString(), false, ProjectWorkflowMode.Patch);
                check(!missing.Ok && harness.Initializations == 0 && harness.Applications == 0 && harness.Saves == 0, "workflow: missing final source blocks every TIA action");
                check(missing.Steps.Any(step => step.Status == "failed" && step.Detail!.Contains("$.sclSourceFiles[1]")), "workflow: missing file identifies its spec element");
                root["projectPath"] = Path.Combine(directory, "Demo.ap20");
                check(!new Harness().Run(root.ToJsonString(), true, ProjectWorkflowMode.Patch).Ok, "workflow: patch preflight rejects unsupported project versions");
                root["projectPath"] = Path.Combine(directory, "missing.ap21");
                check(!new Harness().Run(root.ToJsonString(), true, ProjectWorkflowMode.Patch).Ok, "workflow: patch preflight rejects missing project files");
                root["ladDocs"] = new JsonArray(new JsonObject { ["importPath"] = directory, ["name"] = "Missing" });
                check(new Harness().Run(root.ToJsonString(), true, ProjectWorkflowMode.Patch).Steps.Any(step => step.Step == "lad" && step.Status == "failed"), "workflow: missing LAD document has its own failed preflight step");
            }
            finally { Directory.Delete(directory, true); }
        }

        private static void CheckExecution(Action<bool, string> check)
        {
            var harness = new Harness();
            var result = harness.Run();
            check(result.Ok && harness.Initializations == 1 && harness.Applications == 1 && harness.Saves == 1, "workflow: successful execution initializes, applies, and saves exactly once");
            check(harness.Events.SequenceEqual(new[] { "initialize", "apply", "save" }), "workflow: saving follows execution");
            harness = new Harness();
            check(harness.Run("{\"projectName\":\"Demo\",\"save\":false}").Ok && harness.Saves == 0, "workflow: explicit save=false retains changes without saving");

            harness = new Harness
            {
                Apply = runner =>
                {
                    var import = SuccessfulImport();
                    import.Failed = new[] { new ImportFailure { Path = "UDT_1.xml", Error = "TIA rejected the type" } };
                    runner.Run("udt", () => ProjectStepOutcome.FromImport(import));
                    runner.Run("compile", () => ProjectStepOutcome.FromCompile(SuccessfulCompile()));
                }
            };
            result = harness.Run();
            check(!result.Ok && harness.Saves == 0 && result.Meta?["success"]?.GetValue<bool>() == false, "workflow: returned import failure stays failed even when later compilation succeeds");
            check(result.Steps.Any(step => step.Step == "udt" && step.Detail!.Contains("TIA rejected the type")), "workflow: native import failure details reach the report");
            check(result.Steps.Last().Step == "save" && result.Steps.Last().Status == "skipped", "workflow: failed execution explicitly reports skipped saving");

            harness = new Harness { Initialize = _ => false };
            result = harness.Run();
            check(!result.Ok && harness.Applications == 0 && harness.Saves == 0, "workflow: unsuccessful initialization blocks all dependent work");
            harness = new Harness { Initialize = _ => throw new InvalidOperationException("initialize failed") };
            check(!harness.Run().Ok && harness.Applications == 0 && harness.Saves == 0, "workflow: initialization exceptions block apply and save");
            harness = new Harness { Apply = _ => throw new InvalidOperationException("apply failed") };
            check(!harness.Run().Ok && harness.Saves == 0, "workflow: apply exceptions are reported without saving");
            harness = new Harness { Save = () => SuccessfulResponse(false) };
            check(!harness.Run().Ok && harness.Saves == 1, "workflow: a failed save response makes the workflow fail");
            harness = new Harness { Save = () => throw new IOException("save failed") };
            check(!harness.Run().Ok && harness.Saves == 1, "workflow: save exceptions are reported as a failed save step");

            var calls = 0;
            var response = new ResponseScaffold { Ok = true };
            var runner = new ProjectStepRunner(response);
            runner.RunSequence("scl", "Main.scl", () => ProjectStepOutcome.Failed("import rejected"),
                () => { calls++; return ProjectStepOutcome.Ok("generated"); });
            check(calls == 0 && !response.Ok && response.Steps.Single().Status == "failed", "workflow: failed import prevents block generation within the same step");
            response = new ResponseScaffold { Ok = true };
            runner = new ProjectStepRunner(response);
            runner.RunSequence("hmiScreen", "Main", () => { calls++; return ProjectStepOutcome.Ok("created"); },
                () => { calls++; return ProjectStepOutcome.Ok("designed"); });
            check(calls == 2 && response.Ok && response.Steps.Single().Status == "ok", "workflow: successful prerequisites run the complete sequence");
            runner.Run("fatal", () => throw new COMException("simulated connection loss"));
            runner.Run("laterWrite", () => { calls++; return ProjectStepOutcome.Ok(); });
            check(runner.Stopped && calls == 2 && response.Steps.Last().Status == "skipped", "workflow: a lost TIA connection stops subsequent write delegates");

            var json = JsonNode.Parse(JsonSerializer.Serialize(result))!.AsObject();
            check(json.Select(item => item.Key).OrderBy(name => name).SequenceEqual(new[]
                { "Ok", "ProjectName", "DirectoryPath", "CompileState", "CompileErrorCount", "CompileWarningCount", "Steps", "Message", "Meta" }.OrderBy(name => name)), "workflow: ResponseScaffold JSON field names stay unchanged");
            check(json["Steps"]![0]!.AsObject().Select(item => item.Key).OrderBy(name => name).SequenceEqual(new[] { "Detail", "Status", "Step" }), "workflow: ScaffoldStep JSON field names stay unchanged");
        }

        private static void CheckResults(Action<bool, string> check)
        {
            check(ProjectStepOutcome.FromResponse(SuccessfulResponse()).Succeeded, "workflow result: confirmed success is accepted");
            check(!ProjectStepOutcome.FromResponse(null).Succeeded && !ProjectStepOutcome.FromResponse(new ResponseMessage()).Succeeded, "workflow result: missing responses or metadata are failures");
            check(!ProjectStepOutcome.FromResponse(new ResponseMessage { Meta = new JsonObject { ["success"] = "true" } }).Succeeded, "workflow result: string success does not count as a boolean confirmation");
            foreach (var field in new[] { "failed", "failures" })
            {
                var response = SuccessfulResponse();
                response.Meta![field] = new JsonArray("write rejected");
                check(!ProjectStepOutcome.FromResponse(response).Succeeded, "workflow result: " + field + " cannot be hidden by success=true");
            }
            foreach (var field in new[] { "verified", "bindingVerified" })
            {
                var response = SuccessfulResponse();
                response.Meta![field] = false;
                check(!ProjectStepOutcome.FromResponse(response).Succeeded, "workflow result: unverified " + field + " prevents automatic success");
            }
            check(ProjectStepOutcome.FromImport(SuccessfulImport()).Succeeded, "workflow result: imported object with confirmed success is accepted");
            check(!ProjectStepOutcome.FromImport(null).Succeeded, "workflow result: missing import response is rejected");
            var import = SuccessfulImport();
            import.ImportedTypes = Array.Empty<string>();
            check(!ProjectStepOutcome.FromImport(import).Succeeded, "workflow result: success without imported objects is rejected");
            import = SuccessfulImport();
            import.DryRun = true;
            check(!ProjectStepOutcome.FromImport(import).Succeeded, "workflow result: dryRun cannot satisfy a requested import");
            import = SuccessfulImport();
            import.Meta!["success"] = false;
            check(!ProjectStepOutcome.FromImport(import).Succeeded, "workflow result: reported import failure is preserved");
            import = SuccessfulImport();
            import.Compile = new ResponseCompile { State = "Error", ErrorCount = 1, Meta = new JsonObject { ["success"] = false } };
            check(!ProjectStepOutcome.FromImport(import).Succeeded, "workflow result: a nested failed compilation is preserved");
            check(ProjectStepOutcome.FromCompile(SuccessfulCompile()).Succeeded, "workflow result: zero errors with warnings remains successful");
            check(!ProjectStepOutcome.FromCompile(null).Succeeded, "workflow result: missing compile response is rejected");
            var compile = SuccessfulCompile();
            compile.ErrorCount = null;
            check(!ProjectStepOutcome.FromCompile(compile).Succeeded, "workflow result: unreadable error count cannot mean zero errors");
            compile = SuccessfulCompile();
            compile.State = "Error";
            check(!ProjectStepOutcome.FromCompile(compile).Succeeded, "workflow result: explicit Error state takes precedence over a zero count");
        }

        private static void CheckHmiSelection(Action<bool, string> check)
        {
            var targets = new[]
            {
                new ProjectHmiTarget("A", "A", "HMI_RT_1", "A/HMI_RT_1"),
                new ProjectHmiTarget("B", "B", "HMI_RT_1", "B/HMI_RT_1")
            };
            ProjectSpecification Spec(string path = "") => ProjectSpecification.Parse(new JsonObject
                { ["projectName"] = "Demo", ["hmiName"] = "B", ["hmiSoftwarePath"] = path }.ToJsonString(), ProjectWorkflowMode.Create);
            check(ProjectWorkflow.SelectHmiPath(Spec(), targets) == "B/HMI_RT_1", "workflow HMI: the selected device wins over a global HMI_RT_1 name");
            check(ProjectWorkflow.SelectHmiPath(Spec("HMI_RT_1"), targets) == "B/HMI_RT_1", "workflow HMI: explicit short software name stays inside the selected device");
            check(ProjectWorkflow.SelectHmiPath(Spec("B/HMI_RT_1"), targets) == "B/HMI_RT_1", "workflow HMI: qualified explicit software path resolves exactly");
            RejectHmi(() => ProjectWorkflow.SelectHmiPath(Spec("A/HMI_RT_1"), targets), "not found in device 'B'", check);
            RejectHmi(() => ProjectWorkflow.SelectHmiPath(Spec("Missing"), targets), "Missing", check);
            RejectHmi(() => ProjectWorkflow.SelectHmiPath(Spec(), Array.Empty<ProjectHmiTarget>()), "not found", check);
            var ambiguous = targets.Concat(new[] { new ProjectHmiTarget("B", "B", "Second", "B/Second") }).ToArray();
            RejectHmi(() => ProjectWorkflow.SelectHmiPath(Spec(), ambiguous), "ambiguous", check);
            check(ProjectWorkflow.SelectHmiPath(Spec("B/Second"), ambiguous) == "B/Second", "workflow HMI: explicit software path resolves ambiguity inside one device");
        }

        private static void RejectHmi(Func<string> select, string message, Action<bool, string> check)
        {
            try { select(); check(false, "workflow HMI: expected rejection containing " + message); }
            catch (InvalidOperationException ex) { check(ex.Message.Contains(message), "workflow HMI: reports " + message); }
        }

        private static ResponseMessage SuccessfulResponse(bool success = true) =>
            new ResponseMessage { Message = "completed", Meta = new JsonObject { ["success"] = success } };
        private static ResponsePlcProgramImport SuccessfulImport() => new ResponsePlcProgramImport
            { DryRun = false, ImportedTypes = new[] { "UDT_1" }, Failed = Array.Empty<ImportFailure>(), Meta = new JsonObject { ["success"] = true } };
        private static ResponseCompileDiagnose SuccessfulCompile() => new ResponseCompileDiagnose
            { State = "Warning", ErrorCount = 0, WarningCount = 1, Meta = new JsonObject { ["success"] = true } };

        private sealed class Harness
        {
            public int Validations;
            public int Initializations;
            public int Applications;
            public int Saves;
            public List<string> Events = new List<string>();
            public Func<ProjectPlcArtifact, ProjectStepOutcome> Validator = _ => ProjectStepOutcome.Ok("validated");
            public Func<ProjectStepRunner, bool> Initialize = _ => true;
            public Action<ProjectStepRunner> Apply = runner => runner.Run("plc", () => ProjectStepOutcome.Ok("applied"));
            public Func<ResponseMessage> Save = () => SuccessfulResponse();

            public ResponseScaffold Run(string json = "{\"projectName\":\"Demo\"}", bool dryRun = false, ProjectWorkflowMode mode = ProjectWorkflowMode.Create) =>
                ProjectWorkflow.Run(json, mode, dryRun,
                    artifact => { Validations++; Events.Add("validate"); return Validator(artifact); },
                    (_, runner) => { Initializations++; Events.Add("initialize"); return Initialize(runner); },
                    (_, runner) => { Applications++; Events.Add("apply"); Apply(runner); },
                    () => { Saves++; Events.Add("save"); return Save(); });
        }
    }
}
