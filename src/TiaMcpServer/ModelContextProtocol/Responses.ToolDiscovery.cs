using System.Text.Json.Nodes;

namespace TiaMcpServer.ModelContextProtocol
{
    /// <summary>One tool's SDK-generated schema, fetched on demand rather than listed in every session.</summary>
    public class ResponseToolSchema : ResponseMessage
    {
        public string? Name { get; set; }
        public string? Description { get; set; }
        public string? Signature { get; set; }
        public JsonNode? InputSchema { get; set; }
    }
}
