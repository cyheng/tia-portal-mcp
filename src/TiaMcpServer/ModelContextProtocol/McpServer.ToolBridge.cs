using ModelContextProtocol.Server;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace TiaMcpServer.ModelContextProtocol
{
    // Discover a small page first, fetch one authoritative schema only when needed,
    // then invoke the original tool with its existing checks and structured response.
    public static partial class McpServer
    {
        // name -> the static method carrying [McpServerTool]. Built once; ~212 entries.
        private static Dictionary<string, MethodInfo>? _allToolMethods;

        private static Dictionary<string, MethodInfo> AllToolMethods()
        {
            if (_allToolMethods != null) return _allToolMethods;
            var map = new Dictionary<string, MethodInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var m in typeof(McpServer).GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                var attr = m.GetCustomAttribute<McpServerToolAttribute>();
                if (attr == null) continue;
                map[attr.Name ?? m.Name] = m;
            }
            _allToolMethods = map;
            return map;
        }

        private static string ToolDescription(MethodInfo m)
        {
            var d = m.GetCustomAttribute<DescriptionAttribute>();
            return d == null ? "" : d.Description;
        }

        /// <summary>Renders one tool's signature the way the model needs to call it through CallTool.</summary>
        private static string RenderSignature(string name, MethodInfo m)
        {
            var parts = new List<string>();
            foreach (var p in m.GetParameters())
            {
                string t = FriendlyTypeName(p.ParameterType);
                // Optional params are what a model most often gets wrong, so show the actual
                // default rather than a bare "?".
                if (!p.HasDefaultValue) { parts.Add(p.Name + ": " + t); continue; }
                string def;
                if (p.DefaultValue == null) def = "null";
                else if (p.DefaultValue is bool) def = ((bool)p.DefaultValue) ? "true" : "false";
                else if (p.DefaultValue is string) def = JsonSerializer.Serialize((string)p.DefaultValue, BridgeJson);
                else def = Convert.ToString(p.DefaultValue, System.Globalization.CultureInfo.InvariantCulture) ?? "null";
                parts.Add(p.Name + "?: " + t + " = " + def);
            }
            return name + "(" + string.Join(", ", parts) + ")";
        }

        private static string FriendlyTypeName(Type t)
        {
            var u = Nullable.GetUnderlyingType(t) ?? t;
            if (u == typeof(string)) return "string";
            if (u == typeof(bool)) return "boolean";
            if (u == typeof(int) || u == typeof(long)) return "integer";
            if (u == typeof(double) || u == typeof(float) || u == typeof(decimal)) return "number";
            if (u.IsArray) return FriendlyTypeName(u.GetElementType()!) + "[]";
            return u.Name;
        }

        [McpServerTool(Name = "FindTools"), Description(
            "[L0][Meta] Search all tools by capability words or exact name. Returns a small page of exact signatures and short summaries. " +
            "Use GetToolSchema(name) for full parameter details, then CallTool(name, argumentsJson). Follow meta.nextOffset for more matches.")]
        public static ResponseStringList FindTools(
            [Description("query: space-separated words matched against tool names and descriptions, e.g. 'export watch table'. Empty lists the whole roster.")] string query = "",
            [Description("limit: page size, default 6 and maximum 20.")] int limit = 6,
            [Description("offset: zero-based match offset, default 0. Use meta.nextOffset for the next page.")] int offset = 0,
            [Description("includeDetails: false returns short summaries; true includes full tool descriptions.")] bool includeDetails = false)
        {
            try
            {
                var all = AllToolMethods();
                limit = limit <= 0 ? 6 : Math.Min(limit, 20);
                offset = Math.Max(0, offset);
                string search = (query ?? "").Trim();

                var terms = search
                    .Split(new[] { ' ', ',', ';', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(t => t.Trim().ToLowerInvariant())
                    .Where(t => t.Length > 0)
                    .ToArray();

                var scored = new List<KeyValuePair<int, string>>();
                foreach (var kv in all)
                {
                    string lname = kv.Key.ToLowerInvariant();
                    string desc = ToolDescription(kv.Value).ToLowerInvariant();
                    int score = 0;
                    if (terms.Length == 0) score = 1;
                    foreach (var t in terms)
                    {
                        // Name hits outrank description hits: a model searching "watch table"
                        // wants ExportPlcWatchTable ahead of every tool that merely mentions it.
                        if (lname == t) score += 100;
                        else if (lname.Contains(t)) score += 20;
                        if (desc.Contains(t)) score += 3;
                    }
                    if (score > 0) scored.Add(new KeyValuePair<int, string>(score, kv.Key));
                }

                if (scored.Count == 0)
                {
                    return new ResponseStringList
                    {
                        Message = "No tool matches '" + query + "'. Try fewer or more general words " +
                                  "(e.g. 'watch table' instead of 'ExportPlcWatchTableToCsv'), " +
                                  "or call FindTools with an empty query to list everything.",
                        Meta = DiscoveryMeta(0, 0, offset, limit, includeDetails),
                    };
                }

                var hits = scored
                    .OrderByDescending(x => string.Equals(x.Value, search, StringComparison.OrdinalIgnoreCase))
                    .ThenByDescending(x => x.Key).ThenBy(x => x.Value, StringComparer.Ordinal)
                    .Skip(offset)
                    .Take(limit).ToList();

                var lines = new List<string>();
                foreach (var h in hits)
                {
                    var m = all[h.Value];
                    bool listed = IsToolListed(h.Value);
                    lines.Add(RenderSignature(h.Value, m)
                              + (listed ? "  [already listed - call it directly]" : "  [call via CallTool]"));
                    lines.Add("    " + (includeDetails ? ToolDescription(m) : ToolSummary(m)));
                }

                return new ResponseStringList
                {
                    Message = hits.Count + " of " + scored.Count + " matches. Use GetToolSchema(name) for full details, " +
                              "CallTool(name, argumentsJson) to invoke, and meta.nextOffset for more.",
                    Items = lines,
                    Meta = DiscoveryMeta(scored.Count, hits.Count, offset, limit, includeDetails),
                };
            }
            catch (Exception ex)
            {
                return new ResponseStringList { Message = "FindTools failed: " + ex.Message, Meta = BridgeMeta(false) };
            }
        }

        private static string ToolSummary(MethodInfo method)
        {
            string description = ToolDescription(method).Trim();
            int end = description.Length;
            for (int i = 0; i < description.Length; i++)
            {
                char c = description[i];
                if (c == '\r' || c == '\n') { end = i; break; }
                if ((c == '.' || c == '!' || c == '?' || c == '。') &&
                    (i + 1 == description.Length || char.IsWhiteSpace(description[i + 1]) || c == '。'))
                { end = i + 1; break; }
            }
            string summary = description.Substring(0, end).Trim();
            const int maxChars = 220;
            if (summary.Length <= maxChars) return summary;
            int length = maxChars - 1;
            if (char.IsHighSurrogate(summary[length - 1])) length--;
            return summary.Substring(0, length) + "…";
        }

        private static JsonObject DiscoveryMeta(int matchedCount, int returnedCount, int offset, int limit, bool includeDetails)
        {
            var meta = BridgeMeta(true);
            meta["matchedCount"] = matchedCount;
            meta["returnedCount"] = returnedCount;
            meta["offset"] = offset;
            meta["limit"] = limit;
            meta["nextOffset"] = returnedCount > 0 && offset + returnedCount < matchedCount
                ? (JsonNode)JsonValue.Create(offset + returnedCount)!
                : null;
            meta["includeDetails"] = includeDetails;
            return meta;
        }

        [McpServerTool(Name = "GetToolSchema"), Description(
            "[L0][Meta] Get one tool's exact name, full description, signature with defaults, and authoritative input JSON schema. " +
            "Use after FindTools when parameter details are needed, then invoke with CallTool.")]
        public static ResponseToolSchema GetToolSchema(
            [Description("name: exact tool name from FindTools.")] string name)
        {
            string target = (name ?? "").Trim();
            try
            {
                if (target.Length == 0)
                    return new ResponseToolSchema { Message = "GetToolSchema: 'name' is required. Use FindTools to look up a tool.", Meta = BridgeMeta(false) };
                if (!AllToolMethods().TryGetValue(target, out var method))
                    return new ResponseToolSchema { Message = "No tool named '" + target + "'. Use FindTools to find its exact name.", Meta = BridgeMeta(false) };

                // Use the same SDK factory as tools/list, including required flags, defaults,
                // nested types and parameter descriptions. Reflection-built schemas drift.
                var tool = McpServerTool.Create(method).ProtocolTool;
                return new ResponseToolSchema
                {
                    Name = tool.Name,
                    Description = tool.Description,
                    Signature = RenderSignature(tool.Name, method),
                    InputSchema = JsonNode.Parse(tool.InputSchema.GetRawText()),
                    Meta = BridgeMeta(true),
                };
            }
            catch (Exception ex)
            {
                return new ResponseToolSchema { Message = "GetToolSchema failed: " + ex.Message, Meta = BridgeMeta(false) };
            }
        }

        [McpServerTool(Name = "CallTool"), Description(
            "[L0][Meta] Invoke any discovered tool by exact name, with its existing safety checks. " +
            "Use FindTools or GetToolSchema first. The original response is returned as structured JSON in result; meta.success preserves inner failure.")]
        public static async Task<ResponseCallTool> CallTool(
            [Description("name: exact tool name from FindTools, e.g. 'ExportPlcWatchTable'.")] string name,
            [Description("argumentsJson: JSON object of the tool's arguments, e.g. '{\"softwarePath\":\"PLC_1\"}'. Omit or '{}' for a no-argument tool.")] string argumentsJson = "")
        {
            string target = (name ?? "").Trim();
            try
            {
                if (target.Length == 0)
                    return new ResponseCallTool { Message = "CallTool: 'name' is required. Call FindTools to look up a tool name.", Meta = BridgeMeta(false) };

                // Self-recursion would be a loop with no purpose; refuse it explicitly.
                if (string.Equals(target, "CallTool", StringComparison.OrdinalIgnoreCase))
                    return new ResponseCallTool { Message = "CallTool cannot invoke itself. Pass the target tool's own name.", Meta = BridgeMeta(false) };

                var all = AllToolMethods();
                MethodInfo? method;
                if (!all.TryGetValue(target, out method))
                {
                    // A wrong name is the likeliest failure, so spend the message on the fix
                    // rather than on restating the problem.
                    var near = all.Keys
                        .Where(k => k.IndexOf(target, StringComparison.OrdinalIgnoreCase) >= 0
                                 || target.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0)
                        .OrderBy(k => k, StringComparer.Ordinal).Take(8).ToList();
                    // Containment misses the commonest case of all - a typo in the middle of an
                    // otherwise correct name ("ExportPlcWatchTabel"). Fall back to shared prefix.
                    if (near.Count == 0)
                        near = all.Keys
                            .Select(k => new KeyValuePair<int, string>(CommonPrefixLength(k, target), k))
                            .Where(x => x.Key >= 6)
                            .OrderByDescending(x => x.Key).ThenBy(x => x.Value, StringComparer.Ordinal)
                            .Take(5).Select(x => x.Value).ToList();
                    return new ResponseCallTool
                    {
                        Message = "No tool named '" + target + "'." + (near.Count > 0
                            ? " Did you mean: " + string.Join(", ", near) + "?"
                            : " Call FindTools with a capability keyword to find the right name."),
                        Meta = BridgeMeta(false),
                    };
                }

                JsonObject args;
                if (string.IsNullOrWhiteSpace(argumentsJson) || argumentsJson.Trim() == "{}")
                {
                    args = new JsonObject();
                }
                else
                {
                    JsonNode? parsed;
                    try { parsed = JsonNode.Parse(argumentsJson); }
                    catch (JsonException jx)
                    {
                        return new ResponseCallTool
                        {
                            Message = "argumentsJson is not valid JSON (" + jx.Message + "). It must be a JSON OBJECT of the " +
                                      "tool's parameters, e.g. {\"softwarePath\":\"PLC_1\"} - not a bare value, not the tool name.",
                            Meta = BridgeMeta(false),
                        };
                    }
                    JsonObject? obj = parsed as JsonObject;
                    if (obj == null)
                        return new ResponseCallTool
                        {
                            Message = "argumentsJson must be a JSON object, e.g. {\"softwarePath\":\"PLC_1\"}. " +
                                      "Expected signature: " + RenderSignature(target, method!),
                            Meta = BridgeMeta(false),
                        };
                    args = obj;
                }

                var ps = method.GetParameters();
                var known = ps.Select(p => p.Name!).ToArray();
                var supplied = args.Select(kv => kv.Key).ToArray();
                string problem = known.Length == 0 && supplied.Length > 0
                    ? target + " takes no arguments, but got: " + string.Join(", ", supplied) + ". Expected signature: " + RenderSignature(target, method) + " (nothing was executed)."
                    : ArgDiagnostics.Check(target, known, ps.Where(p => !p.HasDefaultValue).Select(p => p.Name!).ToArray(), supplied,
                        ps.ToDictionary(p => p.Name!, p => FriendlyTypeName(p.ParameterType), StringComparer.OrdinalIgnoreCase));
                if (problem.Length > 0)
                    return new ResponseCallTool { Message = problem, Meta = BridgeMeta(false) };

                var duplicate = supplied.GroupBy(k => k, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
                if (duplicate != null)
                    return new ResponseCallTool { Message = "Duplicate argument '" + duplicate.Key + "'. Supply each parameter once (nothing was executed).", Meta = BridgeMeta(false) };

                var call = new object?[ps.Length];
                var missing = new List<string>();
                for (int i = 0; i < ps.Length; i++)
                {
                    var p = ps[i];
                    // Match case-insensitively: models routinely send PascalCase for a camelCase param.
                    JsonNode? value = null;
                    bool found = false;
                    foreach (var kv in args)
                    {
                        if (!string.Equals(kv.Key, p.Name, StringComparison.OrdinalIgnoreCase)) continue;
                        value = kv.Value; found = kv.Value != null; break;
                    }
                    if (!found)
                    {
                        if (p.HasDefaultValue) { call[i] = p.DefaultValue; continue; }
                        missing.Add(p.Name!);
                        call[i] = p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null;
                        continue;
                    }
                    try { call[i] = value!.Deserialize(p.ParameterType, BridgeJson); }
                    catch (Exception cx)
                    {
                        return new ResponseCallTool
                        {
                            Message = "Argument '" + p.Name + "' of " + target + " could not be read as " +
                                      FriendlyTypeName(p.ParameterType) + ": " + cx.Message +
                                      ". Expected signature: " + RenderSignature(target, method!),
                            Meta = BridgeMeta(false),
                        };
                    }
                }

                if (missing.Count > 0)
                {
                    return new ResponseCallTool
                    {
                        Message = target + " is missing required argument(s): " + string.Join(", ", missing) +
                                  ". Expected signature: " + RenderSignature(target, method!),
                        Meta = BridgeMeta(false),
                    };
                }

                object? result = method!.Invoke(null, call);
                if (result is Task task)
                {
                    await task.ConfigureAwait(false);
                    result = method.ReturnType.GetProperty("Result")?.GetValue(task);
                }
                // Tools return their own strongly-typed response objects; hand that through as
                // structured JSON in Result, not an escaped string in Message. Putting the inner
                // JSON into Message serialized every quote to \" — inflating it and losing the
                // structured parsing a direct call gives. The model reads Result directly.
                JsonNode? resultNode = result == null
                    ? null
                    : JsonNode.Parse(JsonSerializer.Serialize(result, result.GetType(), BridgeJson));

                return new ResponseCallTool
                {
                    Message = "Called " + target + (result == null ? " (null result)" : ""),
                    Result = resultNode,
                    Meta = BridgeMeta(InnerToolSucceeded(resultNode)),
                };
            }
            catch (TargetInvocationException tie)
            {
                var inner = tie.InnerException ?? tie;
                return new ResponseCallTool { Message = target + " failed: " + inner.Message, Meta = BridgeMeta(false) };
            }
            catch (Exception ex)
            {
                return new ResponseCallTool { Message = "CallTool('" + target + "') failed: " + ex.Message, Meta = BridgeMeta(false) };
            }
        }

        private static bool InnerToolSucceeded(JsonNode? result)
        {
            if (!(result is JsonObject obj)) return true;
            foreach (var field in obj)
            {
                if (!string.Equals(field.Key, "meta", StringComparison.OrdinalIgnoreCase) || !(field.Value is JsonObject meta)) continue;
                foreach (var entry in meta)
                    if (string.Equals(entry.Key, "success", StringComparison.OrdinalIgnoreCase) && entry.Value is JsonValue value
                        && value.TryGetValue<bool>(out var success)) return success;
            }
            return true;
        }

        private static int CommonPrefixLength(string a, string b)
        {
            int n = Math.Min(a.Length, b.Length), i = 0;
            while (i < n && char.ToLowerInvariant(a[i]) == char.ToLowerInvariant(b[i])) i++;
            return i;
        }

        private static JsonObject BridgeMeta(bool success)
        {
            return new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = success };
        }

        private static readonly JsonSerializerOptions BridgeJson = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            // Chinese project/block names must survive the round trip unescaped.
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
    }
}
