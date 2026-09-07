using System.IO;

namespace TiaMcpServer.ModelContextProtocol
{
    public static partial class McpServer
    {
        private static bool ApplyProjectPlc(ProjectSpecification spec, ProjectStepRunner runner, bool noOverwrite)
        {
            var succeeded = true;
            foreach (var artifact in spec.PlcArtifacts)
                succeeded &= runner.Run(artifact.Kind, () => ProjectStepOutcome.FromImport(
                    PlcBuildAndImport(spec.PlcName, artifact.Kind, artifact.Json, "", "", "", false, false)), artifact.SpecPath);

            foreach (var path in spec.SclSourceFiles)
            {
                var sourceName = Path.GetFileName(path);
                succeeded &= runner.RunSequence("scl", sourceName,
                    () => ProjectStepOutcome.FromResponse(ImportPlcExternalSource(spec.PlcName, "", path)),
                    () => ProjectStepOutcome.FromResponse(GenerateBlocksFromExternalSource(spec.PlcName, sourceName)));
            }

            foreach (var document in spec.LadDocuments)
                succeeded &= runner.Run("lad", () => ProjectStepOutcome.FromResponse(
                    ImportFromDocuments(spec.PlcName, "", document.ImportPath, document.Name, noOverwrite ? "None" : "Override")), document.Name);

            if (spec.Compile)
            {
                succeeded &= runner.Run("compile", () =>
                {
                    var compile = CompileAndDiagnosePlc(spec.PlcName);
                    runner.Response.CompileState = compile.State;
                    runner.Response.CompileErrorCount = compile.ErrorCount;
                    runner.Response.CompileWarningCount = compile.WarningCount;
                    return ProjectStepOutcome.FromCompile(compile);
                });
            }
            else runner.Skip("compile", "compile=false; PLC compilation was skipped.");
            return succeeded;
        }
    }
}
