using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using TiaMcpServer.ModelContextProtocol;
using Server = TiaMcpServer.ModelContextProtocol.McpServer;

namespace TiaMcpServer.Tests
{
    internal static class ToolDiscoveryTests
    {
        public static void Run(Action<bool, string> check)
        {
            string? previousProfile = Environment.GetEnvironmentVariable("TIA_MCP_PROFILE");
            try
            {
                Environment.SetEnvironmentVariable("TIA_MCP_PROFILE", null);
                Server.SetProfileOverride(null);
                CheckProfiles(check);
                Server.SetProfileOverride("compact");
                CheckDiscovery(check);
                CheckSchemas(check);
                CheckDispatch(check).GetAwaiter().GetResult();
                CheckResponseGuard(check).GetAwaiter().GetResult();
            }
            finally
            {
                Environment.SetEnvironmentVariable("TIA_MCP_PROFILE", previousProfile);
                Server.SetProfileOverride(null);
            }
        }

        private static void CheckProfiles(Action<bool, string> check)
        {
            check(Server.ResolvedProfile() == "compact", "profile: absent CLI/environment defaults to compact");
            var compact = Server.GetConfiguredTools().Select(tool => tool.ProtocolTool.Name).OrderBy(name => name).ToArray();
            check(compact.SequenceEqual(new[] { "Bootstrap", "CallTool", "FindTools", "GetToolSchema" }), "profile: compact lists exactly the four discovery tools");
            check(!Server.IsToolListed("SaveProject") && Server.IsToolListed("Bootstrap"), "profile: listed status follows compact membership");

            Environment.SetEnvironmentVariable("TIA_MCP_PROFILE", " FULL ");
            check(Server.ResolvedProfile() == "full" && Server.GetConfiguredTools().Count == Server.GetAllTools().Count, "profile: environment full exposes the complete roster");
            Server.SetProfileOverride("lite");
            check(Server.ResolvedProfile() == "lite" && Server.GetConfiguredTools().Count > compact.Length, "profile: explicit lite overrides the full environment");
            check(Server.IsToolListed("SaveProject") && Server.IsToolListed("GetToolSchema") && !Server.IsToolListed("ExportFixture"), "profile: lite retains its previous allowlist and adds schema discovery");
            Server.SetProfileOverride(" ALL ");
            check(Server.ResolvedProfile() == "full", "profile: historical all alias remains full");
            Server.SetProfileOverride("typo");
            check(Server.ResolvedProfile() == "compact", "profile: an invalid explicit profile falls back to compact");
            Server.SetProfileOverride(null);
            Environment.SetEnvironmentVariable("TIA_MCP_PROFILE", "unknown");
            check(Server.ResolvedProfile() == "compact", "profile: an invalid environment profile falls back to compact");
            Environment.SetEnvironmentVariable("TIA_MCP_PROFILE", " LITE ");
            check(Server.ResolvedProfile() == "lite", "profile: environment values are trimmed and case insensitive");
            Server.SetProfileOverride("compact");
            check(Server.ResolvedProfile() == "compact", "profile: explicit compact overrides the lite environment");
        }

        private static void CheckDiscovery(Action<bool, string> check)
        {
            var first = Server.FindTools();
            check(first.Meta?["success"]?.GetValue<bool>() == true && Signatures(first).Length == 6, "discovery: the default page contains six tools");
            check(first.Meta?["offset"]?.GetValue<int>() == 0 && first.Meta["nextOffset"]?.GetValue<int>() == 6, "discovery: first-page offsets are actionable");
            var second = Server.FindTools(offset: 6);
            check(!Signatures(first).Intersect(Signatures(second)).Any(), "discovery: consecutive pages have no overlapping tools");
            check(Signatures(first).SequenceEqual(Signatures(Server.FindTools())), "discovery: repeated queries have stable ordering");

            var collected = new List<string>();
            int offset = 0;
            for (int page = 0; page < 100; page++)
            {
                var result = Server.FindTools(offset: offset);
                collected.AddRange(Signatures(result));
                if (result.Meta?["nextOffset"] == null) break;
                int next = result.Meta["nextOffset"]!.GetValue<int>();
                check(next > offset, "discovery: nextOffset strictly advances");
                offset = next;
            }
            check(collected.Count == first.Meta?["matchedCount"]?.GetValue<int>() && collected.Distinct().Count() == collected.Count,
                "discovery: paging retrieves every match exactly once");
            check(Signatures(Server.FindTools(limit: int.MaxValue)).Length == 20, "discovery: oversized pages are capped at twenty tools");
            var clamped = Server.FindTools(limit: 0, offset: -1);
            check(clamped.Meta?["limit"]?.GetValue<int>() == 6 && clamped.Meta["offset"]?.GetValue<int>() == 0, "discovery: nonpositive limits and negative offsets use safe defaults");
            var past = Server.FindTools(offset: int.MaxValue);
            check(Signatures(past).Length == 0 && past.Meta?["nextOffset"] == null && past.Meta?["matchedCount"]?.GetValue<int>() == collected.Count,
                "discovery: an offset beyond the final page retains total matches and reports no next page");
            var none = Server.FindTools("zzzz_does_not_exist");
            check(none.Meta?["success"]?.GetValue<bool>() == true && none.Meta["matchedCount"]?.GetValue<int>() == 0 && none.Meta["nextOffset"] == null,
                "discovery: no matches is a successful empty page");

            var compact = Server.FindTools("exportfixture");
            check(Signatures(compact).FirstOrDefault()?.StartsWith("ExportFixture(softwarePath: string, overwrite?: boolean = false, count?: integer = 3, label?: string = null, format?: string = \"json\")", StringComparison.Ordinal) == true,
                "discovery: an exact case-insensitive name ranks first with all callable defaults");
            check(compact.Items?.FirstOrDefault()?.Contains("[call via CallTool]") == true, "discovery: unlisted tool routing follows compact profile");
            check(compact.Items?.Any(line => line.Contains("Export a fixture.")) == true && compact.Items.All(line => !line.Contains("SECOND_SENTENCE")),
                "discovery: default summaries stop after the first sentence");
            var detailed = Server.FindTools("ExportFixture", includeDetails: true);
            check(detailed.Items?.Any(line => line.Contains("SECOND_SENTENCE")) == true && detailed.Meta?["includeDetails"]?.GetValue<bool>() == true,
                "discovery: details are available explicitly");
            var longSummary = Server.FindTools("FixtureLongDescription").Items?.Skip(1).FirstOrDefault()?.Trim();
            var longDetail = Server.FindTools("FixtureLongDescription", includeDetails: true).Items?.Skip(1).FirstOrDefault()?.Trim();
            check(longSummary?.Length <= 220 && longDetail?.Length > 220 && longSummary.EndsWith("…", StringComparison.Ordinal),
                "discovery: a long first sentence is bounded while explicit details retain its full text");
            check(Server.FindTools("FixtureEscapedDefault").Items?.FirstOrDefault()?.Contains("C:\\\\A\\\"B") == true,
                "discovery: string defaults use valid JSON escapes in the callable signature");
            Server.SetProfileOverride("full");
            check(Server.FindTools("ExportFixture").Items?.FirstOrDefault()?.Contains("[already listed") == true, "discovery: direct-call hint follows a full profile");
            Server.SetProfileOverride("compact");
        }

        private static string[] Signatures(ResponseStringList result) => (result.Items ?? Array.Empty<string>()).Where((_, index) => index % 2 == 0).ToArray();

        private static void CheckSchemas(Action<bool, string> check)
        {
            var schema = Server.GetToolSchema(" exportfixture ");
            check(schema.Meta?["success"]?.GetValue<bool>() == true && schema.Name == "ExportFixture", "schema: requested names are trimmed and returned canonically");
            var method = typeof(Server).GetMethod(nameof(Server.FixtureExport), BindingFlags.Public | BindingFlags.Static)!;
            var sdk = McpServerTool.Create(method).ProtocolTool;
            check(JsonNode.DeepEquals(schema.InputSchema, JsonNode.Parse(sdk.InputSchema.GetRawText())), "schema: response exactly matches the real MCP SDK-generated input schema");
            check(schema.Description == sdk.Description && schema.Description!.Contains("SECOND_SENTENCE"), "schema: full tool description is retained on demand");
            var properties = schema.InputSchema?["properties"];
            check(schema.InputSchema?["required"]?.AsArray().Select(node => node!.GetValue<string>()).SequenceEqual(new[] { "softwarePath" }) == true,
                "schema: only the required softwarePath parameter is marked required");
            check(properties?["overwrite"]?["default"]?.GetValue<bool>() == false && properties["count"]?["default"]?.GetValue<int>() == 3,
                "schema: optional defaults come from the SDK");
            check(properties?["softwarePath"]?["description"]?.GetValue<string>() == "softwarePath: exact software name."
                && properties["overwrite"]?["description"]?.GetValue<string>() == "overwrite: permit replacement only when true.", "schema: full parameter descriptions survive SDK generation");
            check(Server.GetToolSchema("").Meta?["success"]?.GetValue<bool>() == false && Server.GetToolSchema("NoSuchTool").InputSchema == null,
                "schema: empty and unknown names fail without returning a fabricated schema");
            var find = Server.GetToolSchema("FindTools").InputSchema?["properties"];
            check(find?["limit"]?["default"]?.GetValue<int>() == 6 && find["includeDetails"]?["default"]?.GetValue<bool>() == false,
                "schema: discovery advertises its compact defaults through the real SDK");
        }

        private static async Task CheckDispatch(Action<bool, string> check)
        {
            Server.BridgeFixtureInvocations = 0;
            var sync = await Server.CallTool("exportfixture", "{\"SoftwarePath\":\"中文PLC\"}");
            check(sync.Meta?["success"]?.GetValue<bool>() == true && sync.Result?["Message"]?.GetValue<string>() == "中文PLC:False:3:null:json",
                "dispatch: synchronous calls preserve case-insensitive arguments, defaults and Chinese structured output");
            check(Server.BridgeFixtureInvocations == 1 && sync.Result is JsonObject, "dispatch: a valid synchronous call executes once and returns an object rather than escaped JSON");
            var asyncResult = await Server.CallTool("Bootstrap");
            check(asyncResult.Meta?["success"]?.GetValue<bool>() == true && asyncResult.Result?["Message"]?.GetValue<string>() == "bootstrap completed"
                && Server.BridgeFixtureInvocations == 2, "dispatch: Task<T> is awaited and its actual response is serialized");
            var asyncVoid = await Server.CallTool("FixtureAsyncVoid");
            check(asyncVoid.Meta?["success"]?.GetValue<bool>() == true && asyncVoid.Result == null && Server.BridgeFixtureInvocations == 3,
                "dispatch: Task is awaited before returning a null result");
            var returnedFailure = await Server.CallTool("FixtureAsyncFailure");
            check(returnedFailure.Meta?["success"]?.GetValue<bool>() == false && returnedFailure.Result?["Message"]?.GetValue<string>() == "inner failure",
                "dispatch: returned inner failure stays failed while retaining the inner response");
            check((await Server.CallTool("FixtureJsonFailure")).Meta?["success"]?.GetValue<bool>() == false,
                "dispatch: lowercase JSON meta.success is also respected");
            check((await Server.CallTool("FixtureThrows")).Message?.Contains("synchronous fixture failure") == true,
                "dispatch: synchronous invocation errors expose the actionable inner message");
            var thrown = await Server.CallTool("FixtureAsyncThrows");
            check(thrown.Meta?["success"]?.GetValue<bool>() == false && thrown.Message?.Contains("asynchronous fixture failure") == true,
                "dispatch: awaited errors are reported instead of serializing a failed task");

            foreach (var arguments in new[] { "{}", "[]", "{", "{\"softwarePath\":\"PLC\",\"overwrit\":true}", "{\"softwarePath\":\"PLC\",\"count\":\"bad\"}", "{\"softwarePath\":null}", "{\"softwarePath\":\"A\",\"SoftwarePath\":\"B\"}" })
            {
                var rejected = await Server.CallTool("ExportFixture", arguments);
                check(rejected.Meta?["success"]?.GetValue<bool>() == false && Server.BridgeFixtureInvocations == 3,
                    "dispatch: invalid arguments are rejected before any fixture side effect: " + arguments);
            }
            var typo = await Server.CallTool("ExportFixture", "{\"softwarePath\":\"PLC\",\"overwrit\":true}");
            check(typo.Message?.Contains("overwrit -> overwrite") == true && typo.Message.Contains("nothing was executed"),
                "dispatch: unknown argument errors suggest the real parameter and state no execution");
            var noArgs = await Server.CallTool("SaveProject", "{\"overwrite\":true}");
            check(noArgs.Meta?["success"]?.GetValue<bool>() == false && noArgs.Message?.Contains("takes no arguments") == true && Server.BridgeFixtureInvocations == 3,
                "dispatch: unknown arguments cannot trigger a no-argument write tool");
            check((await Server.CallTool("CallTool")).Meta?["success"]?.GetValue<bool>() == false
                && (await Server.CallTool("")).Meta?["success"]?.GetValue<bool>() == false
                && (await Server.CallTool("NoSuchTool")).Meta?["success"]?.GetValue<bool>() == false,
                "dispatch: recursive, empty and unknown targets fail safely");

            ExportStore.ResetForTests();
            try
            {
                string text = "完整分页响应" + new string('x', 5000);
                string id = ExportStore.Put("ExportFixture", "", text);
                int offset = 0;
                var collected = new StringBuilder();
                for (int page = 0; page < 100; page++)
                {
                    var slice = await Server.CallTool("getexport", new JsonObject { ["exportId"] = id, ["offset"] = offset, ["length"] = 500 }.ToJsonString());
                    check(slice.Meta?["success"]?.GetValue<bool>() == true, "dispatch: export pages remain callable through the compact bridge");
                    collected.Append(slice.Result?["Message"]?.GetValue<string>());
                    if (slice.Result?["Meta"]?["eof"]?.GetValue<bool>() == true) break;
                    offset = slice.Result!["Meta"]!["nextOffset"]!.GetValue<int>();
                }
                check(collected.ToString() == text, "dispatch: export paging through CallTool reconstructs the entire original response");
                check(Server.IsExportTool("getexport"), "dispatch: lowercase export targets retain the response-guard bypass");
            }
            finally { ExportStore.ResetForTests(); }
        }

        private static async Task CheckResponseGuard(Action<bool, string> check)
        {
            string? previousBudget = Environment.GetEnvironmentVariable("TIA_MCP_MAX_RESPONSE_CHARS");
            ExportStore.ResetForTests();
            try
            {
                Environment.SetEnvironmentVariable("TIA_MCP_MAX_RESPONSE_CHARS", null);
                check(Server.ResolvedMaxResponseChars() == 8000, "response guard: default per-response budget is eight thousand characters");
                Environment.SetEnvironmentVariable("TIA_MCP_MAX_RESPONSE_CHARS", "bad");
                check(Server.ResolvedMaxResponseChars() == 8000, "response guard: malformed budget falls back to eight thousand");
                Environment.SetEnvironmentVariable("TIA_MCP_MAX_RESPONSE_CHARS", "1200");
                check(Server.ResolvedMaxResponseChars() == 1200, "response guard: explicit response budgets remain supported");
                Environment.SetEnvironmentVariable("TIA_MCP_MAX_RESPONSE_CHARS", null);

                var small = new CallToolResult
                {
                    Content = new List<ContentBlock> { new TextContentBlock { Text = "small" } },
                    StructuredContent = new JsonObject { ["value"] = "small" }
                };
                check(ReferenceEquals(ResponseGuardTool.Shrink(small, "Fixture", ""), small),
                    "response guard: a small response retains the original protocol result and structured content");
                var failure = new CallToolResult
                {
                    IsError = true,
                    Content = new List<ContentBlock> { new TextContentBlock { Text = new string('e', 20000) } }
                };
                check(ReferenceEquals(ResponseGuardTool.Shrink(failure, "Fixture", ""), failure) && failure.IsError == true && ExportStore.Stats().count == 0,
                    "response guard: protocol errors preserve IsError and full diagnostic text without being parked");

                // A real SDK server supplies request context; no transport is run and no
                // Siemens session is needed. Invoke the production wrappers and SDK tool.
                await using var server = McpServerFactory.Create(
                    new StreamServerTransport(Stream.Null, Stream.Null), new McpServerOptions());
                var bridge = Server.WrapTools(new List<McpServerTool>
                {
                    McpServerTool.Create(typeof(Server).GetMethod(nameof(Server.CallTool))!)
                }).Single();
                var request = new RequestContext<CallToolRequestParams>(server)
                {
                    Params = new CallToolRequestParams
                    {
                        Name = "CallTool",
                        Arguments = new Dictionary<string, JsonElement>
                        {
                            ["name"] = JsonSerializer.SerializeToElement("FixtureLargeResult"),
                            ["argumentsJson"] = JsonSerializer.SerializeToElement("{}")
                        }
                    }
                };
                var result = await bridge.InvokeAsync(request);
                var stub = JsonNode.Parse(((TextContentBlock)result.Content[0]).Text!);
                string id = stub!["meta"]!["exportId"]!.GetValue<string>();
                check(stub["meta"]?["truncated"]?.GetValue<bool>() == true && stub["message"]?.GetValue<string>().Length == 8000,
                    "response guard: large bridge responses return an eight thousand character head plus a paging handle");
                check(JsonNode.DeepEquals(stub, result.StructuredContent), "response guard: text and structured content both contain the reduced response");
                check(ExportStore.Get(id)?.Content.Length == stub["meta"]?["totalLength"]?.GetValue<int>(),
                    "response guard: the full original SDK response remains in the export store");

                var collected = new StringBuilder();
                int offset = 0;
                for (int page = 0; page < 100; page++)
                {
                    request.Params = new CallToolRequestParams
                    {
                        Name = "CallTool",
                        Arguments = new Dictionary<string, JsonElement>
                        {
                            ["name"] = JsonSerializer.SerializeToElement("getexport"),
                            ["argumentsJson"] = JsonSerializer.SerializeToElement(
                                new JsonObject { ["exportId"] = id, ["offset"] = offset, ["length"] = 8000 }.ToJsonString())
                        }
                    };
                    var pageResult = await bridge.InvokeAsync(request);
                    var envelope = JsonNode.Parse(((TextContentBlock)pageResult.Content[0]).Text!);
                    var inner = Property(envelope, "result");
                    var meta = Property(inner, "meta");
                    check(envelope?["meta"]?["truncated"] == null && inner != null,
                        "response guard: forwarded lowercase GetExport bypasses shrinking even when its JSON envelope exceeds the budget");
                    collected.Append(Property(inner, "message")?.GetValue<string>());
                    if (meta?["eof"]?.GetValue<bool>() == true) break;
                    offset = meta!["nextOffset"]!.GetValue<int>();
                }
                check(collected.ToString() == ExportStore.Get(id)?.Content && ExportStore.Stats().count == 1,
                    "response guard: compact-mode SDK paging reconstructs the original response without creating nested export handles");
            }
            finally
            {
                Environment.SetEnvironmentVariable("TIA_MCP_MAX_RESPONSE_CHARS", previousBudget);
                ExportStore.ResetForTests();
            }
        }

        private static JsonNode? Property(JsonNode? node, string name) => node is JsonObject obj
            ? obj.FirstOrDefault(field => string.Equals(field.Key, name, StringComparison.OrdinalIgnoreCase)).Value
            : null;
    }
}
