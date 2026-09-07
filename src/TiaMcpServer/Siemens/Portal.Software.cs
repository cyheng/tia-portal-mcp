using Microsoft.Extensions.Logging;
using Siemens.Engineering;
using Siemens.Engineering.Compiler;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.Safety;
using Siemens.Engineering.SW;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security;
using System.Text.Json.Nodes;
using TiaMcpServer.ModelContextProtocol;

namespace TiaMcpServer.Siemens
{
    // PLC 软件定位、工艺对象、外部源、交叉引用与编译。
    public partial class Portal
    {
        public PlcSoftware? GetPlcSoftware(string softwarePath)
        {
            _logger?.LogInformation($"Getting software by path: {softwarePath}");

            if (IsProjectNull())
            {
                return null;
            }

            var softwareContainer = GetSoftwareContainer(softwarePath);

            if (softwareContainer?.Software is PlcSoftware plcSoftware)
            {
                return plcSoftware;
            }

            // Low-barrier fallback: tolerate a sloppy softwarePath (wrong case / extra spaces /
            // a single-PLC project / a unique substring like "PLC" -> "PLC_1"). Exact resolution
            // above is tried first, so this only runs when it misses.
            return ResolvePlcSoftwareFuzzy(softwarePath);
        }

        private PlcSoftware? ResolvePlcSoftwareFuzzy(string softwarePath)
        {
            var all = GetAllPlcSoftware();
            if (all.Count == 0) return null;

            var matched = Guard.MatchPlcName(all.Select(p => p.Name).ToList(), softwarePath);
            if (matched == null) return null;

            return all.FirstOrDefault(p => string.Equals(p.Name, matched, StringComparison.Ordinal))
                ?? all.FirstOrDefault(p => string.Equals(p.Name, matched, StringComparison.OrdinalIgnoreCase));
        }

        // Enumerate every PlcSoftware in the open project (devices + device groups), de-duplicated by
        // name. Used for tolerant softwarePath resolution and "Available PLC paths" error hints.
        public List<PlcSoftware> GetAllPlcSoftware()
        {
            var result = new List<PlcSoftware>();
            if (_project == null) return result;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void Collect(IEnumerable<DeviceItem>? roots)
            {
                if (roots == null) return;
                var stack = new Stack<DeviceItem>(roots.Where(x => x != null));
                while (stack.Count > 0)
                {
                    var it = stack.Pop();
                    if (it == null) continue;
                    try
                    {
                        if (it.GetService<SoftwareContainer>()?.Software is PlcSoftware plc &&
                            !string.IsNullOrEmpty(plc.Name) && seen.Add(plc.Name))
                        {
                            result.Add(plc);
                        }
                    }
                    catch { }
                    try { if (it.DeviceItems != null) foreach (var ch in it.DeviceItems) if (ch != null) stack.Push(ch); } catch { }
                }
            }

            void WalkDevices(DeviceComposition? devices)
            {
                if (devices == null) return;
                foreach (var d in devices) { try { Collect(d.DeviceItems); } catch { } }
            }
            void WalkGroups(DeviceUserGroupComposition? groups)
            {
                if (groups == null) return;
                foreach (var g in groups) { try { WalkDevices(g.Devices); WalkGroups(g.Groups); } catch { } }
            }

            try { WalkDevices(_project.Devices); } catch { }
            try { WalkGroups(_project.DeviceGroups); } catch { }
            return result;
        }

        // " Available PLC paths: a, b, c" suffix for not-found error messages (empty when none).
        public string AvailablePlcPathsSuffix()
        {
            try
            {
                var names = GetAllPlcSoftware().Select(p => p.Name).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().ToList();
                var paths = names.Count > 0 ? " Available PLC paths: " + string.Join(", ", names) : string.Empty;
                // 附上设备树遍历中被吞掉的真因：路径其实是对的、只是遍历半途抛了的那种情况，
                // 光报 "Available PLC paths" 会把用户引去改一个本来就没错的参数。
                // 遍历正常时 DeviceScanErrorSuffix() 返回空串，消息与以前逐字节相同。
                return paths + DeviceScanErrorSuffix();
            }
            catch { return DeviceScanErrorSuffix(); }
        }

        public void ImportTechnologyObject(string softwarePath, string folderPath, string importPath)
        {
            if (IsProjectNull()) throw new PortalException(PortalErrorCode.InvalidState, "No project is open. If a project is already open in the TIA Portal UI, call AttachToOpenProject(projectName); otherwise call OpenProject(path) for a local .ap21 project, or CreateProject to start a new one. (Connect is attempted automatically.)");

            var plc = GetPlcSoftware(softwarePath);
            if (plc == null) throw new PortalException(PortalErrorCode.NotFound, $"PlcSoftware not found at '{softwarePath}'");

            try
            {
                object root = TryGetPropertyValue(plc, "TechnologyObjectGroup", "TechnologicalObjects", "TechnologyObjects") ?? plc;
                var group = TryResolveChildGroupByPath(root, folderPath) ?? root;

                // collection name varies; try likely ones
                var col = TryGetPropertyValue(group, "TechnologicalObjects", "TechnologyObjects", "Instances", "Objects") ??
                          TryGetPropertyValue(root, "TechnologicalObjects", "TechnologyObjects", "Instances", "Objects");

                if (col == null)
                    throw new PortalException(PortalErrorCode.NotFound, $"TechnologyObjects collection not found. plcType={plc.GetType().FullName} groupType={group.GetType().FullName}");

                if (TryImportEngineeringObjectIntoCollection(col, importPath, out _, out var err)) return;
                throw new PortalException(PortalErrorCode.ImportFailed, err ?? "ImportTechnologyObject failed");
            }
            catch (PortalException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new PortalException(PortalErrorCode.ImportFailed, ex.Message, null, ex);
            }
        }

        // ── Technology Objects (TO) ──────────────────────────────────────────

        private static object? ResolveTechnologyObjectCollection(PlcSoftware plc)
        {
            var group = TryGetPropertyValue(plc,
                "TechnologicalObjectGroup", "TechnologyObjectGroup",
                "TechnologicalObjects", "TechnologyObjects");
            if (group == null) return null;

            // If we landed on a group container, drill into the collection
            var col = TryGetPropertyValue(group,
                "TechnologicalObjects", "TechnologyObjects", "Instances", "Objects");
            return col ?? group; // group itself might already be enumerable
        }

        public List<JsonObject> GetTechnologyObjects(string softwarePath)
        {
            var result = new List<JsonObject>();
            // 这三条原来都返回空列表，工具层于是报「在 'XXX' 里找到 0 个技术对象」——
            // 「没连项目」「路径写错」「枚举炸了」全被说成了「这个 PLC 没有技术对象」。
            // 空列表只有一个合法含义：**解析到了这个 PLC，它确实没有 TO**。
            if (IsProjectNull())
            {
                throw new PortalException(PortalErrorCode.InvalidState,
                    "GetTechnologyObjects: no project is open. Call Connect + OpenProject "
                    + "(or AttachToOpenProject) first.");
            }

            var plc = GetPlcSoftware(softwarePath);
            if (plc == null)
            {
                throw new PortalException(PortalErrorCode.NotFound,
                    $"GetTechnologyObjects: PLC software not found at '{softwarePath}'." + AvailablePlcPathsSuffix());
            }

            try
            {
                var col = ResolveTechnologyObjectCollection(plc);
                if (col is not IEnumerable items || col is string) return result;

                foreach (var item in items)
                {
                    if (item == null) continue;
                    var obj = new JsonObject();
                    foreach (var prop in new[] { "Name", "OfSystemLibElement", "OfSystemLibVersion" })
                    {
                        var val = TryGetPropertyValue(item, prop);
                        if (val != null) obj[prop] = JsonValue.Create(val.ToString());
                    }
                    // Try to get a "type" hint from class name as fallback
                    if (!obj.ContainsKey("OfSystemLibElement"))
                        obj["TypeHint"] = JsonValue.Create(item.GetType().Name);
                    result.Add(obj);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "GetTechnologyObjects failed for {SoftwarePath}", softwarePath);
                // 原来吞掉异常返回已收集的部分 —— 「少了几个 TO」比「一个都没有」更难发现，
                // 因为它看起来完全正常。
                throw new PortalException(PortalErrorCode.OpennessError,
                    $"GetTechnologyObjects failed halfway through '{softwarePath}': {ex.Message}. "
                    + "The list would have been INCOMPLETE, so it is not returned.", null, ex);
            }
            return result;
        }

        public ResponseMessage ExportTechnologyObject(string softwarePath, string toName, string exportPath)
        {
            if (IsProjectNull()) return new ResponseMessage { Message = "No project open." };
            var plc = GetPlcSoftware(softwarePath);
            if (plc == null) return new ResponseMessage { Message = $"PLC software not found: '{softwarePath}'." };

            try
            {
                var col = ResolveTechnologyObjectCollection(plc);
                var to = FindByName(col, toName);
                if (to == null)
                    return new ResponseMessage { Message = $"Technology object '{toName}' not found in '{softwarePath}'." };

                Directory.CreateDirectory(Path.GetDirectoryName(exportPath) ?? ".");
                TryExportEngineeringObject(to, exportPath, out var err);
                if (err != null)
                    return new ResponseMessage { Message = $"Export error: {err}" };

                return new ResponseMessage
                {
                    Message = $"Technology object '{toName}' exported to '{exportPath}'.",
                    Meta = new JsonObject { ["exportPath"] = exportPath, ["toName"] = toName }
                };
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "ExportTechnologyObject failed");
                return new ResponseMessage { Message = $"Export failed: {ex.Message}" };
            }
        }

        public List<ModelContextProtocol.CrossReferenceEntry>? GetCrossReferences(string softwarePath, string objectPath, string objectKind = "Block", string filter = "AllObjects")
        {
            if (IsProjectNull()) return null;

            object? target = null;
            if (string.Equals(objectKind, "Type", StringComparison.OrdinalIgnoreCase))
            {
                target = GetType(softwarePath, objectPath);
            }
            else
            {
                target = GetBlock(softwarePath, objectPath);
            }

            if (target == null) return null;

            var crossReferenceService = TryGetServiceByTypeSuffix(target, "CrossReferenceService");
            if (crossReferenceService == null) return null;

            var result = TryInvokeGetCrossReferences(crossReferenceService, filter);
            if (result == null) return null;

            return TryFlattenCrossReferenceResult(result, objectPath);
        }

        private static object? TryInvokeGetCrossReferences(object crossReferenceService, string filterName)
        {
            try
            {
                var svcType = crossReferenceService.GetType();
                var filterType = svcType.Assembly.GetTypes()
                    .FirstOrDefault(t => t.IsEnum && t.Name.Equals("CrossReferenceFilter", StringComparison.OrdinalIgnoreCase));
                if (filterType == null) return null;

                var filterValue = Enum.Parse(filterType, filterName, ignoreCase: true);
                var m = svcType.GetMethod("GetCrossReferences", new[] { filterType });
                if (m == null) return null;

                return m.Invoke(crossReferenceService, new[] { filterValue });
            }
            catch
            {
                return null;
            }
        }

        private static List<ModelContextProtocol.CrossReferenceEntry> TryFlattenCrossReferenceResult(object crossReferenceResult, string sourcePathFallback)
        {
            var items = new List<ModelContextProtocol.CrossReferenceEntry>();

            try
            {
                var sources = crossReferenceResult.GetType().GetProperty("Sources")?.GetValue(crossReferenceResult) as IEnumerable;
                if (sources == null) return items;

                foreach (var src in sources)
                {
                    if (src == null) continue;
                    var srcName = src.GetType().GetProperty("Name")?.GetValue(src)?.ToString();
                    var srcPath = src.GetType().GetProperty("Path")?.GetValue(src)?.ToString() ?? sourcePathFallback;

                    var refs = src.GetType().GetProperty("References")?.GetValue(src) as IEnumerable;
                    if (refs == null) continue;

                    foreach (var rf in refs)
                    {
                        if (rf == null) continue;
                        var refName = rf.GetType().GetProperty("Name")?.GetValue(rf)?.ToString();
                        var refPath = rf.GetType().GetProperty("Path")?.GetValue(rf)?.ToString();

                        var locations = rf.GetType().GetProperty("Locations")?.GetValue(rf) as IEnumerable;
                        if (locations == null)
                        {
                            items.Add(new ModelContextProtocol.CrossReferenceEntry
                            {
                                SourceName = srcName,
                                SourcePath = srcPath,
                                ReferenceName = refName,
                                ReferencePath = refPath
                            });
                            continue;
                        }

                        foreach (var loc in locations)
                        {
                            if (loc == null) continue;
                            items.Add(new ModelContextProtocol.CrossReferenceEntry
                            {
                                SourceName = srcName,
                                SourcePath = srcPath,
                                ReferenceName = refName,
                                ReferencePath = refPath,
                                LocationName = loc.GetType().GetProperty("Name")?.GetValue(loc)?.ToString(),
                                ReferenceLocation = loc.GetType().GetProperty("ReferenceLocation")?.GetValue(loc)?.ToString(),
                                ReferenceType = loc.GetType().GetProperty("ReferenceType")?.GetValue(loc)?.ToString(),
                                Access = loc.GetType().GetProperty("Access")?.GetValue(loc)?.ToString()
                            });
                        }
                    }
                }
            }
            catch
            {
                // best-effort
            }

            return items;
        }

        public List<string>? GetPlcExternalSources(string softwarePath)
        {
            if (IsProjectNull()) return null;
            var softwareContainer = GetSoftwareContainer(softwarePath);
            if (softwareContainer?.Software is not PlcSoftware plcSoftware) return null;

            var sources = TryGetExternalSourcesCollection(plcSoftware);
            if (sources == null) return new List<string>();

            var names = new List<string>();
            foreach (var item in sources)
            {
                if (item == null) continue;
                var name = item.GetType().GetProperty("Name")?.GetValue(item)?.ToString();
                if (!string.IsNullOrWhiteSpace(name)) names.Add(name!);
            }
            return names;
        }

        /// <summary>
        /// Removes a PLC external source by name (e.g. <c>Ramp.scl</c> or <c>Ramp</c>) so a subsequent
        /// <see cref="ImportPlcExternalSource"/> can recreate it. Returns true if deleted or if no matching source exists.
        /// </summary>
        public void DeletePlcExternalSource(string softwarePath, string externalSourceName)
        {
            if (IsProjectNull())
                throw new PortalException(PortalErrorCode.InvalidState, "DeletePlcExternalSource: project is null");

            if (string.IsNullOrWhiteSpace(externalSourceName))
                throw new PortalException(PortalErrorCode.InvalidParams, "DeletePlcExternalSource: externalSourceName is empty");

            var softwareContainer = GetSoftwareContainer(softwarePath);
            if (softwareContainer?.Software is not PlcSoftware plcSoftware)
                throw new PortalException(PortalErrorCode.NotFound, $"DeletePlcExternalSource: PlcSoftware not found at '{softwarePath}'");

            var sources = TryGetExternalSourcesCollection(plcSoftware);
            if (sources == null)
                throw new PortalException(PortalErrorCode.OpennessError, "DeletePlcExternalSource: ExternalSources collection not available");

            foreach (var item in sources)
            {
                if (item == null) continue;
                var name = item.GetType().GetProperty("Name")?.GetValue(item)?.ToString();
                if (string.IsNullOrWhiteSpace(name)) continue;
                if (!ExternalSourceNameMatches(name!, externalSourceName)) continue;

                try
                {
                    var del = item.GetType().GetMethod("Delete", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                    if (del != null)
                    {
                        del.Invoke(item, null);
                        return;
                    }

                    throw new PortalException(PortalErrorCode.OpennessError, $"DeletePlcExternalSource: no parameterless Delete() on {item.GetType().Name}");
                }
                catch (PortalException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    throw new PortalException(PortalErrorCode.OpennessError, $"DeletePlcExternalSource: {ex.Message}", null, ex);
                }
            }

            // source not present = idempotent no-op success
        }

        public void ImportPlcExternalSource(string softwarePath, string groupPath, string filePath)
        {
            if (IsProjectNull())
                throw new PortalException(PortalErrorCode.InvalidState, "ImportPlcExternalSource: project is null");
            var softwareContainer = GetSoftwareContainer(softwarePath);
            if (softwareContainer?.Software is not PlcSoftware plcSoftware)
                throw new PortalException(PortalErrorCode.NotFound, $"ImportPlcExternalSource: PlcSoftware not found at '{softwarePath}'");

            var group = TryGetExternalSourceGroupByPath(plcSoftware, groupPath);
            if (group == null)
                throw new PortalException(PortalErrorCode.NotFound, $"ImportPlcExternalSource: ExternalSourceGroup not found (groupPath='{groupPath}')");

            // Openness API for external sources differs across TIA versions:
            // some expose Import(FileInfo,...), others expose Add/Create/ImportFromFile(FileInfo,...).
            // Prefer the ExternalSources composition first — CreateFromFile lives there in V21.
            var targets = new List<object>();
            try
            {
                var extSourcesObj = group.GetType().GetProperty("ExternalSources")?.GetValue(group);
                if (extSourcesObj != null) targets.Add(extSourcesObj);
            }
            catch { }
            targets.Add(group);

            var fi = new FileInfo(filePath);
            if (!fi.Exists)
                throw new PortalException(PortalErrorCode.InvalidParams, $"ImportPlcExternalSource: file not found '{filePath}'");

            var candidates = new List<(object Target, MethodInfo Method)>();
            foreach (var tgt in targets)
            {
                var methods = tgt.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance);
                candidates.AddRange(methods
                    .Where(m =>
                    {
                        var ps = m.GetParameters();
                        if (ps.Length < 1) return false;
                        if (ps[0].ParameterType != typeof(FileInfo) && ps[0].ParameterType != typeof(string)) return false;
                        var n = m.Name ?? "";
                        return n.StartsWith("Import", StringComparison.OrdinalIgnoreCase)
                               || n.StartsWith("Add", StringComparison.OrdinalIgnoreCase)
                               || n.StartsWith("Create", StringComparison.OrdinalIgnoreCase);
                    })
                    .Select(m => (Target: tgt, Method: m)));
            }

            candidates = candidates
                .OrderBy(c => c.Method.GetParameters()[0].ParameterType == typeof(FileInfo) ? 0 : 1)
                .ThenBy(c => c.Method.Name.StartsWith("Import", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(c => c.Method.GetParameters().Length)
                .ToList();

            if (candidates.Count == 0)
            {
                string Dump(object tgt)
                {
                    try
                    {
                        var ms = tgt.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                            .Where(m => (m.Name ?? "").IndexOf("Import", StringComparison.OrdinalIgnoreCase) >= 0
                                     || (m.Name ?? "").IndexOf("Create", StringComparison.OrdinalIgnoreCase) >= 0
                                     || (m.Name ?? "").IndexOf("Add", StringComparison.OrdinalIgnoreCase) >= 0)
                            .Select(m => $"{m.Name}({string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name))})")
                            .Take(10);
                        return string.Join("; ", ms);
                    }
                    catch { return ""; }
                }

                var extDump = targets.Count > 1 ? Dump(targets[1]) : "";
                throw new PortalException(PortalErrorCode.OpennessError,
                    $"ImportPlcExternalSource: No import-like method found. group={group.GetType().FullName} methods=[{Dump(group)}] extSources=[{extDump}]");
            }

            var failures = new List<string>();
            foreach (var candidate in candidates)
            {
                var importMethod = candidate.Method;
                var parms = importMethod.GetParameters();
                var argLists = BuildExternalSourceImportArguments(parms, fi);
                if (argLists.Count == 0) continue;

                foreach (var args in argLists)
                {
                    var sig = $"{importMethod.Name}({string.Join(", ", parms.Select(p => p.ParameterType.Name))})";
                    try
                    {
                        var result = importMethod.Invoke(candidate.Target, args);
                        if (importMethod.ReturnType == typeof(void) || result != null)
                        {
                            return;
                        }

                        failures.Add($"{sig} returned null");
                    }
                    catch (Exception ex)
                    {
                        var inner = (ex is TargetInvocationException tie && tie.InnerException != null) ? tie.InnerException : ex;
                        failures.Add($"{sig} threw {inner.GetType().FullName}: {inner.Message}");
                    }
                }
            }

            throw new PortalException(PortalErrorCode.OpennessError, "ImportPlcExternalSource: all import-like methods failed: " + string.Join(" | ", failures.Take(12)));
        }

        private static bool ExternalSourceNameMatches(string actualName, string requested)
        {
            if (string.IsNullOrWhiteSpace(actualName)) return false;
            if (string.Equals(actualName, requested, StringComparison.OrdinalIgnoreCase)) return true;
            var req = (requested ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(req)) return false;
            var reqNoExt = Path.GetFileNameWithoutExtension(req);
            var actNoExt = Path.GetFileNameWithoutExtension(actualName);
            if (string.Equals(actNoExt, reqNoExt, StringComparison.OrdinalIgnoreCase)) return true;
            if (req.IndexOf('.') < 0 &&
                string.Equals(actualName, req + ".scl", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            return false;
        }

        private static List<object?[]> BuildExternalSourceImportArguments(ParameterInfo[] parms, FileInfo fi)
        {
            var result = new List<object?[]>();
            if (parms.Length < 1) return result;
            if (parms[0].ParameterType != typeof(FileInfo) && parms[0].ParameterType != typeof(string)) return result;

            object firstArg = parms[0].ParameterType == typeof(FileInfo) ? fi : fi.FullName;
            var sourceName = Path.GetFileNameWithoutExtension(fi.Name);

            if (parms.Length == 1)
            {
                result.Add(new object?[] { firstArg });
                return result;
            }

            // Siemens.Openness: PlcExternalSourceComposition.CreateFromFile(string name, string path)
            // Manual 5.11.3.x — first arg is the external-source *name* (often "Block_1.scl"), second is full path.
            // Older reflection code wrongly passed (FullPath, fileTitleWithoutExtension).
            if (parms.Length == 2 && parms[0].ParameterType == typeof(string) && parms[1].ParameterType == typeof(string))
            {
                result.Add(new object?[] { fi.Name, fi.FullName });
                if (!string.IsNullOrEmpty(sourceName) &&
                    !string.Equals(sourceName, fi.Name, StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(new object?[] { sourceName, fi.FullName });
                }
                return result;
            }

            if (parms.Length == 2 && parms[0].ParameterType == typeof(FileInfo) && parms[1].ParameterType == typeof(string))
            {
                result.Add(new object?[] { fi, sourceName });
                return result;
            }

            if (parms.Length == 2 && parms[1].ParameterType.IsEnum)
            {
                foreach (var preferred in new[] { "Override", "Overwrite", "Replace", "None" })
                {
                    try
                    {
                        result.Add(new object?[] { firstArg, Enum.Parse(parms[1].ParameterType, preferred, ignoreCase: true) });
                    }
                    catch { }
                }
                foreach (var value in Enum.GetValues(parms[1].ParameterType))
                {
                    if (!result.Any(args => Equals(args[1], value))) result.Add(new object?[] { firstArg, value });
                }
                return result;
            }

            if (parms.Skip(1).All(p => p.IsOptional))
            {
                result.Add(new[] { firstArg }.Concat(parms.Skip(1).Select(p => p.DefaultValue)).ToArray());
            }

            return result;
        }

        public void GenerateBlocksFromExternalSource(string softwarePath, string externalSourceName)
        {
            if (IsProjectNull()) throw new PortalException(PortalErrorCode.InvalidState, "GenerateBlocksFromExternalSource: project is null");
            var softwareContainer = GetSoftwareContainer(softwarePath);
            if (softwareContainer?.Software is not PlcSoftware plcSoftware) throw new PortalException(PortalErrorCode.NotFound, $"GenerateBlocksFromExternalSource: PlcSoftware not found at '{softwarePath}'");

            var sources = TryGetExternalSourcesCollection(plcSoftware);
            if (sources == null) throw new PortalException(PortalErrorCode.OpennessError, "GenerateBlocksFromExternalSource: ExternalSources collection not available");

            object? src = null;
            foreach (var item in sources)
            {
                if (item == null) continue;
                var name = item.GetType().GetProperty("Name")?.GetValue(item)?.ToString();
                if (string.IsNullOrWhiteSpace(name)) continue;
                if (ExternalSourceNameMatches(name!, externalSourceName))
                {
                    src = item;
                    break;
                }
            }
            if (src == null) throw new PortalException(PortalErrorCode.NotFound, $"GenerateBlocksFromExternalSource: external source not found: {externalSourceName}");

            // Resolve GenerateBlocksFromSource(PlcBlockUserGroup, GenerateBlockOption);
            // parameterless GenerateBlocks() may not exist.
            var t = src.GetType();
            var methods = t.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m =>
                {
                    var n = m.Name ?? "";
                    return n.Equals("GenerateBlocks", StringComparison.OrdinalIgnoreCase)
                           || n.Equals("GenerateBlocksFromSource", StringComparison.OrdinalIgnoreCase)
                           || n.Equals("GenerateBlocksFromExternalSource", StringComparison.OrdinalIgnoreCase);
                })
                .OrderBy(m => m.GetParameters().Length)
                .ToList();

            var failures = new List<string>();
            foreach (var gen in methods)
            {
                var ps = gen.GetParameters();
                try
                {
                    if (ps.Length == 0)
                    {
                        gen.Invoke(src, Array.Empty<object>());
                        return;
                    }

                    if (ps.Length == 2 && ps[1].ParameterType.IsEnum)
                    {
                        var folderType = ps[0].ParameterType;
                        var blockRoot = plcSoftware.BlockGroup;
                        if (blockRoot == null)
                        {
                            failures.Add($"{gen.Name}: BlockGroup is null");
                            continue;
                        }

                        if (!folderType.IsAssignableFrom(blockRoot.GetType()))
                        {
                            failures.Add($"{gen.Name}: BlockGroup type {blockRoot.GetType().Name} not assignable to {folderType.Name}");
                            continue;
                        }

                        object optionVal;
                        try
                        {
                            optionVal = Enum.Parse(ps[1].ParameterType, "None", ignoreCase: true);
                        }
                        catch
                        {
                            var vals = Enum.GetValues(ps[1].ParameterType);
                            if (vals.Length == 0)
                            {
                                failures.Add($"{gen.Name}: GenerateBlockOption enum empty");
                                continue;
                            }
                            optionVal = vals.GetValue(0)!;
                        }

                        gen.Invoke(src, new[] { blockRoot, optionVal });
                        return;
                    }
                }
                catch (Exception ex)
                {
                    var inner = (ex is TargetInvocationException tie && tie.InnerException != null) ? tie.InnerException : ex;
                    failures.Add($"{gen.Name}({ps.Length}): {inner.Message}");
                }
            }

            throw new PortalException(PortalErrorCode.OpennessError, "GenerateBlocksFromExternalSource: " + string.Join(" | ", failures.Take(10)));
        }

        private static IEnumerable<object?>? TryGetExternalSourcesCollection(PlcSoftware plcSoftware)
        {
            try
            {
                var group = plcSoftware.GetType().GetProperty("ExternalSourceGroup")?.GetValue(plcSoftware)
                           ?? plcSoftware.GetType().GetProperty("ExternalSources")?.GetValue(plcSoftware);
                if (group == null) return null;

                var sources = group.GetType().GetProperty("ExternalSources")?.GetValue(group) ?? group;
                return sources as IEnumerable<object?>;
            }
            catch
            {
                return null;
            }
        }

        private static object? TryGetExternalSourceGroupByPath(PlcSoftware plcSoftware, string groupPath)
        {
            try
            {
                var root = plcSoftware.GetType().GetProperty("ExternalSourceGroup")?.GetValue(plcSoftware);
                if (root == null) return null;

                if (string.IsNullOrWhiteSpace(groupPath) || groupPath == "/")
                {
                    return root;
                }

                var segments = groupPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                object current = root;
                foreach (var seg in segments)
                {
                    var groups = current.GetType().GetProperty("Groups")?.GetValue(current) as IEnumerable;
                    if (groups == null) return null;

                    object? next = null;
                    foreach (var g in groups)
                    {
                        if (g == null) continue;
                        var name = g.GetType().GetProperty("Name")?.GetValue(g)?.ToString();
                        if (string.Equals(name, seg, StringComparison.OrdinalIgnoreCase))
                        {
                            next = g;
                            break;
                        }
                    }
                    if (next == null) return null;
                    current = next;
                }

                return current;
            }
            catch
            {
                return null;
            }
        }

        public CompilerResult CompileSoftware(string softwarePath, string password = "")
        {
            _logger?.LogInformation($"Compiling software by path: {softwarePath}");

            if (IsProjectNull())
                throw new PortalException(PortalErrorCode.InvalidState, "Project is null");

            var softwareContainer = GetSoftwareContainer(softwarePath);
            if (softwareContainer?.Software == null)
                throw new PortalException(PortalErrorCode.NotFound, $"SoftwareContainer or Software not found for path '{softwarePath}'");

            if (!string.IsNullOrEmpty(password))
            {
                var deviceItem = softwareContainer?.Parent as DeviceItem;

                var admin = deviceItem?.GetService<SafetyAdministration>();
                if (admin != null)
                {
                    if (!admin.IsLoggedOnToSafetyOfflineProgram)
                    {
                        SecureString secString = new NetworkCredential("", password).SecurePassword;
                        try
                        {
                            admin.LoginToSafetyOfflineProgram(secString);
                        }
                        catch (Exception ex)
                        {
                            throw new PortalException(PortalErrorCode.OpennessError, $"Safety login failed: {ex.Message}", null, ex);
                        }
                    }
                }
            }

            // PlcSoftware and classic WinCC (HmiTarget) are themselves service providers, so the
            // compiler service comes off the software object. WinCC Unified's HmiSoftware is NOT
            // an IEngineeringServiceProvider (verified against the V21 PublicAPI) and exposes no
            // Compile of its own — for Unified the compilable object is the owning device item,
            // which is what the TIA UI compiles as well.
            ICompilable compileService = ResolveCompileService(softwareContainer, softwarePath, out var targetKind);

            try
            {
                CompilerResult result = compileService.Compile();

                if (result == null)
                    throw new PortalException(PortalErrorCode.OpennessError, "ICompilable.Compile() returned null");

                return result;
            }
            catch (PortalException)
            {
                throw;
            }
            catch (TargetInvocationException tie) when (tie.InnerException != null)
            {
                throw new PortalException(PortalErrorCode.OpennessError, $"{tie.InnerException.GetType().FullName}: {tie.InnerException.Message}", null, tie.InnerException);
            }
            catch (Exception ex)
            {
                throw new PortalException(PortalErrorCode.OpennessError, $"{ex.GetType().FullName}: {ex.Message}", null, ex);
            }
        }

        /// <summary>
        /// Find the object that actually carries the ICompilable service for a software path,
        /// and return that service.
        ///
        /// PlcSoftware and classic WinCC (HmiTarget) carry it themselves. WinCC Unified's
        /// HmiSoftware does not — for Unified the compilable object sits further up the
        /// ownership chain (the HMI device), which is what the TIA UI compiles too.
        ///
        /// The judgement must be "does this object actually hand out ICompilable", NOT
        /// "is this object an IEngineeringServiceProvider" — in Openness practically
        /// everything implements that interface, while GetService&lt;T&gt;() just returns
        /// **null** when the service is absent instead of throwing. Testing the interface
        /// therefore picks the first ancestor unconditionally and then fails with a null
        /// service. Real-machine 2026-08-31, MTP700 Unified Basic on V21: the software's
        /// immediate parent DeviceItem passed the interface test and yielded a null
        /// service, so Unified compiles failed with
        /// "HmiSoftware via DeviceItemImpl.GetService&lt;ICompilable&gt;() returned null"
        /// — i.e. exactly the panel type this tool was added for.
        ///
        /// So: walk up from the software and take the first level that really provides
        /// the service. Walking is also depth-proof — Unified PC stations nest
        /// Device → DeviceItem → DeviceItem, and that nesting is not ours to predict.
        /// </summary>
        private ICompilable ResolveCompileService(SoftwareContainer? softwareContainer, string softwarePath, out string targetKind)
        {
            var software = softwareContainer?.Software;
            if (software == null)
                throw new PortalException(PortalErrorCode.NotFound, $"SoftwareContainer or Software not found for path '{softwarePath}'");

            // 走过的每一层都记下来：找不到时把这串报出去，下一个人不用再猜层级。
            var probed = new List<string>();

            ICompilable? Probe(object? candidate, string kind)
            {
                if (candidate is not IEngineeringServiceProvider provider) return null;
                ICompilable? service;
                try
                {
                    service = provider.GetService<ICompilable>();
                }
                catch (Exception ex)
                {
                    // 代理对象可能已失效；这一层探不了不代表上一层探不了，记下继续往上。
                    probed.Add($"{kind}(threw {ex.GetType().Name})");
                    return null;
                }
                probed.Add($"{kind}{(service == null ? "(no ICompilable)" : "(OK)")}");
                return service;
            }

            var direct = Probe(software, software.GetType().Name);
            if (direct != null)
            {
                targetKind = software.GetType().Name;
                return direct;
            }

            // 从软件容器往上爬。上限 8 层纯属防御：真实层级是 3~4 层，
            // 加个上限只是不想在代理对象出怪时把自己转死在循环里。
            object? node = softwareContainer;
            for (int depth = 0; node != null && depth < 8; depth++)
            {
                var kind = $"{software.GetType().Name} via {node.GetType().Name}";
                var service = Probe(node, kind);
                if (service != null)
                {
                    targetKind = kind;
                    return service;
                }
                node = (node as IEngineeringObject)?.Parent;
            }

            throw new PortalException(
                PortalErrorCode.InvalidState,
                $"Software at '{softwarePath}' ({software.GetType().FullName}) is not compilable: " +
                $"neither it nor any owner up to 8 levels provides ICompilable. Probed: {string.Join(" -> ", probed)}");
        }
    }
}
