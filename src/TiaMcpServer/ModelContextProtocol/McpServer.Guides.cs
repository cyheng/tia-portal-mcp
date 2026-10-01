using ModelContextProtocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json.Nodes;

namespace TiaMcpServer.ModelContextProtocol
{
    // On-demand authoring cheat sheets (see McpGuides). Purpose: models driving this server
    // from hosts that never load SKILL.md (VS Code / Cursor / third-party agents) kept
    // hand-writing fragile FlgNet XML, mixing up BOM rules, and retry-looping on syntax
    // errors — this tool hands them the verified rules right before they write code.
    public static partial class McpServer
    {
        [McpServerTool(Name = "GetAuthoringGuide"), Description("[L0][Guide] Read verified authoring rules before writing SCL/LAD/DB/HMI or adding block comments/titles. Topics: overview, workflow, scl, lad, db, hmi, errors, comments. Read-only.")]
        public static ResponseMessage GetAuthoringGuide(
            [Description("topic: overview | workflow | scl | lad | db | hmi | errors | comments")] string topic)
        {
            var text = McpGuides.Topic(topic);
            if (text == null)
            {
                return new ResponseMessage
                {
                    Message = $"Unknown topic '{topic}'. Available topics: {McpGuides.TopicList}.",
                    Meta = new JsonObject { ["success"] = false, ["topics"] = McpGuides.TopicList },
                };
            }
            return new ResponseMessage
            {
                Message = text,
                Meta = new JsonObject { ["success"] = true, ["topic"] = topic.Trim().ToLowerInvariant() },
            };
        }
    }
}
