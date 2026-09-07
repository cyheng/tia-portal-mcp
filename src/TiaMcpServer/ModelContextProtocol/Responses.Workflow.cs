using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace TiaMcpServer.ModelContextProtocol
{
    // Shared workflow contracts are independent of the Siemens SDK so their behavior can be tested offline.
    public class ResponseMessage
    {
        public string? Message { get; set; }
        public JsonObject? Meta { get; set; }
    }

    public class ImportFailure
    {
        public string? Path { get; set; }
        public string? Error { get; set; }
    }

    public class ResponseCompile : ResponseMessage
    {
        public string? State { get; set; }
        public int? ErrorCount { get; set; }
        public int? WarningCount { get; set; }
        public IEnumerable<string>? Messages { get; set; }
    }

    public class ResponsePlcProgramImport : ResponseMessage
    {
        public bool? DryRun { get; set; }
        public string? BuildKind { get; set; }
        public string? GeneratedDirectory { get; set; }
        public IEnumerable<string>? WrittenFiles { get; set; }
        public IEnumerable<string>? DiscoveredTypes { get; set; }
        public IEnumerable<string>? DiscoveredTagTables { get; set; }
        public IEnumerable<string>? DiscoveredTechnologyObjects { get; set; }
        public IEnumerable<string>? DiscoveredBlocks { get; set; }
        public IEnumerable<string>? ImportedTypes { get; set; }
        public IEnumerable<string>? ImportedTagTables { get; set; }
        public IEnumerable<string>? ImportedTechnologyObjects { get; set; }
        public IEnumerable<string>? ImportedBlocks { get; set; }
        public IEnumerable<ImportFailure>? Failed { get; set; }
        public ResponseCompile? Compile { get; set; }
        public string? CapabilityDecision { get; set; }
        public IEnumerable<string>? CapabilityWarnings { get; set; }
        public IEnumerable<string>? RecommendedNextActions { get; set; }
    }

    public class ResponseCompileDiagnose : ResponseMessage
    {
        public string? State { get; set; }
        public int? ErrorCount { get; set; }
        public int? WarningCount { get; set; }
        public IEnumerable<string>? Errors { get; set; }
        public IEnumerable<string>? Warnings { get; set; }
        public IEnumerable<string>? Info { get; set; }
        public IEnumerable<string>? RawMessages { get; set; }
    }

    public class ScaffoldStep
    {
        public string? Step { get; set; }
        public string? Status { get; set; } // ok | failed | skipped
        public string? Detail { get; set; }
    }

    public class ResponseScaffold : ResponseMessage
    {
        public bool Ok { get; set; }
        public string? ProjectName { get; set; }
        public string? DirectoryPath { get; set; }
        public string? CompileState { get; set; }
        public int? CompileErrorCount { get; set; }
        public int? CompileWarningCount { get; set; }
        public List<ScaffoldStep> Steps { get; set; } = new List<ScaffoldStep>();
    }
}
