using ModelContextProtocol.Server;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace TiaMcpServer.ModelContextProtocol
{
    // Only the Siemens-dependent tool bodies are fixtures. Profile selection, discovery,
    // dispatch, SDK schemas, argument guards and response paging are production sources.
    public class ResponseStringList : ResponseMessage
    {
        public IEnumerable<string>? Items { get; set; }
    }

    public static partial class McpServer
    {
        internal static int BridgeFixtureInvocations;

        [McpServerTool(Name = "Bootstrap"), Description("Inspect the environment. Full bootstrap operating details follow.")]
        public static async Task<ResponseMessage> Bootstrap()
        {
            await Task.Yield();
            BridgeFixtureInvocations++;
            return FixtureResponse("bootstrap completed");
        }

        [McpServerTool(Name = "Doctor"), Description("Diagnose the environment.")]
        public static ResponseMessage Doctor() => FixtureResponse("doctor completed");

        [McpServerTool(Name = "GetState"), Description("Read the connection state.")]
        public static ResponseMessage GetState() => FixtureResponse("state completed");

        [McpServerTool(Name = "SaveProject"), Description("Save the project.")]
        public static ResponseMessage SaveProject()
        {
            BridgeFixtureInvocations++;
            return FixtureResponse("saved");
        }

        [McpServerTool(Name = "GetAuthoringGuide"), Description("Read one authoring topic.")]
        public static ResponseMessage GetAuthoringGuide(
            [Description("topic: the requested syntax or workflow topic.")] string topic = "workflow") =>
            FixtureResponse(McpGuides.Topic(topic) ?? "Unknown guide topic");

        [McpServerTool(Name = "ExportFixture"), Description("Export a fixture. SECOND_SENTENCE contains the detailed safety and format rules.")]
        public static ResponseMessage FixtureExport(
            [Description("softwarePath: exact software name.")] string softwarePath,
            [Description("overwrite: permit replacement only when true.")] bool overwrite = false,
            [Description("count: maximum fixture records.")] int count = 3,
            [Description("label: optional output label.")] string? label = null,
            [Description("format: the selected format.")] string format = "json")
        {
            BridgeFixtureInvocations++;
            return FixtureResponse(softwarePath + ":" + overwrite + ":" + count + ":" + (label ?? "null") + ":" + format);
        }

        [McpServerTool(Name = "FixtureAsyncFailure"), Description("Simulate a returned async failure.")]
        public static async Task<ResponseMessage> FixtureAsyncFailure()
        {
            await Task.Yield();
            return FixtureResponse("inner failure", false);
        }

        [McpServerTool(Name = "FixtureAsyncVoid"), Description("Complete an async operation without a response.")]
        public static async Task FixtureAsyncVoid()
        {
            await Task.Yield();
            BridgeFixtureInvocations++;
        }

        [McpServerTool(Name = "FixtureThrows"), Description("Throw a synchronous failure.")]
        public static ResponseMessage FixtureThrows() => throw new InvalidOperationException("synchronous fixture failure");

        [McpServerTool(Name = "FixtureAsyncThrows"), Description("Throw an asynchronous failure.")]
        public static async Task<ResponseMessage> FixtureAsyncThrows()
        {
            await Task.Yield();
            throw new InvalidOperationException("asynchronous fixture failure");
        }

        [McpServerTool(Name = "FixtureJsonFailure"), Description("Return a JSON failure using lowercase response fields.")]
        public static JsonObject FixtureJsonFailure() => new JsonObject
        {
            ["message"] = "json failure",
            ["meta"] = new JsonObject { ["success"] = false }
        };

        [McpServerTool(Name = "Connect"), Description("Connect to a session.")]
        public static ResponseMessage Connect() => FixtureResponse("connected");

        [McpServerTool(Name = "FixtureLongDescription"), Description("Detailed export guidance Detailed export guidance Detailed export guidance Detailed export guidance Detailed export guidance Detailed export guidance Detailed export guidance Detailed export guidance Detailed export guidance Detailed export guidance Detailed export guidance Detailed export guidance Detailed export guidance Detailed export guidance Detailed export guidance Detailed export guidance Detailed export guidance Detailed export guidance Detailed export guidance Detailed export guidance Detailed export guidance Detailed export guidance Detailed export guidance Detailed export guidance ")]
        public static ResponseMessage FixtureLongDescription() => FixtureResponse("long description");

        [McpServerTool(Name = "FixtureEscapedDefault"), Description("Inspect an escaped path default.")]
        public static ResponseMessage FixtureEscapedDefault(string path = "C:\\A\"B") => FixtureResponse(path);

        [McpServerTool(Name = "FixtureLargeResult"), Description("Read a large export response.")]
        public static ResponseMessage FixtureLargeResult(int length = 20000) => FixtureResponse(new string('x', length));

        [McpServerTool(Name = "Disconnect"), Description("Disconnect from a session.")]
        public static ResponseMessage Disconnect() => FixtureResponse("disconnected");

        [McpServerTool(Name = "GetProjectTree"), Description("Read the project tree.")]
        public static ResponseMessage GetProjectTree() => FixtureResponse("tree");

        [McpServerTool(Name = "GetBlocks"), Description("Read the block list.")]
        public static ResponseMessage GetBlocks() => FixtureResponse("blocks");

        [McpServerTool(Name = "CompileSoftware"), Description("Compile the selected software.")]
        public static ResponseMessage CompileSoftware() => FixtureResponse("compiled");

        private static ResponseMessage FixtureResponse(string message, bool success = true) =>
            new ResponseMessage { Message = message, Meta = new JsonObject { ["success"] = success } };
    }
}
