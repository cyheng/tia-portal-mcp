namespace TiaMcpServer.ModelContextProtocol
{
    public static partial class McpServer
    {
        // The CLI patch command uses the same validation, execution, and save policy as project generation.
        public static ResponseScaffold PatchProject(string spec, bool dryRun = false, bool noOverwrite = false)
            => RunProjectWorkflow(spec, ProjectWorkflowMode.Patch, dryRun, noOverwrite);
    }
}
