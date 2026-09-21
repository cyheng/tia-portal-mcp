using System.Collections.Generic;

namespace TiaMcpServer.ModelContextProtocol
{
    public class Attribute
    {
        public string? Name { get; set; }
        public object? Value { get; set; }
        public string? AccessMode { get; set; }
    }

    public class BlockGroupInfo
    {
        public string? Name { get; set; }
        public IEnumerable<BlockGroupInfo>? Groups { get; set; }
        public IEnumerable<BlockSummary>? Blocks { get; set; }
    }

    /// <summary>BuildBlockHierarchy 限制块明细数量时的计数载体：一次递归共享一份，
    /// 累计总块数与已输出数，调用方据此填 totalCount/truncated。blockLimit &lt;=0 表示不限。</summary>
    public class HierarchyBudget
    {
        public int TotalBlocks;
        public int BlocksEmitted;
        public bool Truncated;
    }

    public class CrossReferenceEntry
    {
        public string? SourceName { get; set; }
        public string? SourcePath { get; set; }
        public string? ReferenceName { get; set; }
        public string? ReferencePath { get; set; }
        public string? LocationName { get; set; }
        public string? ReferenceLocation { get; set; }
        public string? ReferenceType { get; set; }
        public string? Access { get; set; }
    }

    public class NetworkAttribute
    {
        public string? Name { get; set; }
        public string? Value { get; set; }
        public string? DataType { get; set; }
        public bool? IsWritable { get; set; }
        public string? WriteProbeError { get; set; }
    }

    public class ObjectMember
    {
        public string? Name { get; set; }
        public string? Kind { get; set; } // Property/Method
        public string? Type { get; set; }
        public string? Signature { get; set; }
    }

    public class CaxExportResult
    {
        public string? DeviceName { get; set; }
        public string? FilePath { get; set; }
        public bool Success { get; set; }
        public string? State { get; set; }
        public int ErrorCount { get; set; }
        public int WarningCount { get; set; }
        public List<string>? Messages { get; set; }
    }
}
