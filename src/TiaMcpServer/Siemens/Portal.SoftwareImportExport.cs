using Siemens.Engineering;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json.Nodes;
using TiaMcpServer.ModelContextProtocol;

namespace TiaMcpServer.Siemens
{
    // PLC 与 HMI 文件的批次导入导出及参考工程导入编排。
    public partial class Portal
    {
        public ResponseImportBatch ImportPlcTagTablesFromDirectory(string softwarePath, string folderPath, string dir, string regexName = "", bool overwrite = true)
        {
            var imported = new List<string>();
            var failed = new List<ImportFailure>();

            try
            {
                if (IsProjectNull())
                {
                    failed.Add(new ImportFailure { Path = dir, Error = "Project is null" });
                    return new ResponseImportBatch { Imported = imported, Failed = failed };
                }

                if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
                {
                    failed.Add(new ImportFailure { Path = dir, Error = "Directory not found" });
                    return new ResponseImportBatch { Imported = imported, Failed = failed };
                }

                Regex? regex = null;
                if (!string.IsNullOrWhiteSpace(regexName))
                {
                    regex = new Regex(regexName, RegexOptions.IgnoreCase);
                }

                foreach (var file in Directory.EnumerateFiles(dir, "*.xml", SearchOption.TopDirectoryOnly))
                {
                    var name = Path.GetFileNameWithoutExtension(file);
                    if (regex != null && !regex.IsMatch(name)) continue;

                    try { ImportPlcTagTable(softwarePath, folderPath, file); imported.Add(name); }
                    catch (PortalException pex) { failed.Add(new ImportFailure { Path = file, Error = pex.Message }); }
                }

                return new ResponseImportBatch { Imported = imported, Failed = failed };
            }
            catch (Exception ex)
            {
                failed.Add(new ImportFailure { Path = dir, Error = ex.ToString() });
                return new ResponseImportBatch { Imported = imported, Failed = failed };
            }
        }

        public ResponseImportBatch ExportPlcWatchTablesToDirectory(string softwarePath, string dir, string regexName = "")
        {
            var exported = new List<string>();
            var failed = new List<ImportFailure>();

            try
            {
                var names = GetPlcWatchTables(softwarePath);
                if (names == null)
                {
                    failed.Add(new ImportFailure { Path = softwarePath, Error = "PLC software not found" });
                    return new ResponseImportBatch { Imported = exported, Failed = failed };
                }

                Directory.CreateDirectory(dir);
                Regex? regex = null;
                if (!string.IsNullOrWhiteSpace(regexName))
                {
                    regex = new Regex(regexName, RegexOptions.IgnoreCase);
                }

                foreach (var name in names)
                {
                    if (regex != null && !regex.IsMatch(name)) continue;
                    var outPath = Path.Combine(dir, MakeSafeFileName(name) + ".xml");
                    if (ExportPlcWatchTable(softwarePath, name, outPath)) exported.Add(outPath);
                    else failed.Add(new ImportFailure { Path = name, Error = "Export failed" });
                }

                return new ResponseImportBatch { Imported = exported, Failed = failed };
            }
            catch (Exception ex)
            {
                failed.Add(new ImportFailure { Path = dir, Error = ex.ToString() });
                return new ResponseImportBatch { Imported = exported, Failed = failed };
            }
        }

        public ResponseImportBatch ImportTechnologyObjectsFromDirectory(string softwarePath, string folderPath, string dir, string regexName = "", bool overwrite = true)
        {
            var imported = new List<string>();
            var failed = new List<ImportFailure>();

            try
            {
                if (IsProjectNull())
                {
                    failed.Add(new ImportFailure { Path = dir, Error = "Project is null" });
                    return new ResponseImportBatch { Imported = imported, Failed = failed };
                }

                if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
                {
                    failed.Add(new ImportFailure { Path = dir, Error = "Directory not found" });
                    return new ResponseImportBatch { Imported = imported, Failed = failed };
                }

                Regex? regex = null;
                if (!string.IsNullOrWhiteSpace(regexName))
                {
                    regex = new Regex(regexName, RegexOptions.IgnoreCase);
                }

                foreach (var file in Directory.EnumerateFiles(dir, "*.xml", SearchOption.TopDirectoryOnly))
                {
                    var name = Path.GetFileNameWithoutExtension(file);
                    if (regex != null && !regex.IsMatch(name)) continue;

                    try { ImportTechnologyObject(softwarePath, folderPath, file); imported.Add(name); }
                    catch (PortalException pex) { failed.Add(new ImportFailure { Path = file, Error = pex.Message }); }
                }

                return new ResponseImportBatch { Imported = imported, Failed = failed };
            }
            catch (Exception ex)
            {
                failed.Add(new ImportFailure { Path = dir, Error = ex.ToString() });
                return new ResponseImportBatch { Imported = imported, Failed = failed };
            }
        }

        public ResponseImportBatch ExportTechnologyObjectsToDirectory(
            string softwarePath, string exportDir, string regexName = "")
        {
            var exported = new List<string>();
            var failed = new List<ImportFailure>();

            if (IsProjectNull())
            {
                failed.Add(new ImportFailure { Path = softwarePath, Error = "No project open." });
                return new ResponseImportBatch { Imported = exported, Failed = failed };
            }

            var plc = GetPlcSoftware(softwarePath);
            if (plc == null)
            {
                failed.Add(new ImportFailure { Path = softwarePath, Error = "PLC software not found." });
                return new ResponseImportBatch { Imported = exported, Failed = failed };
            }

            try
            {
                Directory.CreateDirectory(exportDir);
                Regex? regex = null;
                if (!string.IsNullOrWhiteSpace(regexName))
                    regex = new Regex(regexName, RegexOptions.IgnoreCase);

                var col = ResolveTechnologyObjectCollection(plc);
                if (col is not IEnumerable items || col is string)
                {
                    failed.Add(new ImportFailure { Path = softwarePath, Error = "Technology object collection not accessible." });
                    return new ResponseImportBatch { Imported = exported, Failed = failed };
                }

                foreach (var item in items)
                {
                    if (item == null) continue;
                    var name = TryGetPropertyValue(item, "Name")?.ToString() ?? string.Empty;
                    if (string.IsNullOrEmpty(name)) continue;
                    if (regex != null && !regex.IsMatch(name)) continue;

                    var path = Path.Combine(exportDir, name + ".xml");
                    TryExportEngineeringObject(item, path, out var err);
                    if (err == null) exported.Add(name);
                    else failed.Add(new ImportFailure { Path = name, Error = err });
                }
            }
            catch (Exception ex)
            {
                failed.Add(new ImportFailure { Path = exportDir, Error = ex.ToString() });
            }

            return new ResponseImportBatch { Imported = exported, Failed = failed };
        }

        public (List<string> Exported, List<string> Failed)? ExportHmiProgram(string softwarePath, string exportDir, bool exportScreens = true, bool exportTagTables = true)
        {
            if (IsProjectNull()) return null;

            var exported = new List<string>();
            var failed = new List<string>();

            Directory.CreateDirectory(exportDir);

            if (exportScreens)
            {
                var screens = GetHmiScreens(softwarePath) ?? new List<string>();
                foreach (var s in screens)
                {
                    var safe = MakeSafeFileName(s);
                    var outPath = Path.Combine(exportDir, $"screen_{safe}.xml");
                    try { ExportHmiScreen(softwarePath, s, outPath); exported.Add(outPath); }
                    catch (PortalException) { failed.Add($"screen:{s}"); }
                }
            }

            if (exportTagTables)
            {
                var tables = GetHmiTagTables(softwarePath) ?? new List<string>();
                foreach (var t in tables)
                {
                    var safe = MakeSafeFileName(t);
                    var outPath = Path.Combine(exportDir, $"tagtable_{safe}.xml");
                    try { ExportHmiTagTable(softwarePath, t, outPath); exported.Add(outPath); }
                    catch (PortalException) { failed.Add($"tagtable:{t}"); }
                }
            }

            return (exported, failed);
        }

        public ResponseImportBatch ImportHmiScreensFromDirectory(string softwarePath, string folderPath, string dir, string regexName = "", bool overwrite = true)
        {
            var imported = new List<string>();
            var failed = new List<ImportFailure>();

            try
            {
                if (IsProjectNull())
                {
                    failed.Add(new ImportFailure { Path = dir, Error = "Project is null" });
                    return new ResponseImportBatch { Imported = imported, Failed = failed };
                }

                if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
                {
                    failed.Add(new ImportFailure { Path = dir, Error = "Directory not found" });
                    return new ResponseImportBatch { Imported = imported, Failed = failed };
                }

                Regex? regex = null;
                if (!string.IsNullOrWhiteSpace(regexName))
                {
                    regex = new Regex(regexName, RegexOptions.IgnoreCase);
                }

                foreach (var file in Directory.EnumerateFiles(dir, "*.xml", SearchOption.TopDirectoryOnly))
                {
                    var name = Path.GetFileNameWithoutExtension(file);
                    if (regex != null && !regex.IsMatch(name)) continue;

                    try
                    {
                        ImportHmiScreen(softwarePath, folderPath, file);
                        imported.Add(name);
                    }
                    catch (Exception ex)
                    {
                        failed.Add(new ImportFailure { Path = file, Error = ex.ToString() });
                    }
                }

                return new ResponseImportBatch { Imported = imported, Failed = failed };
            }
            catch (Exception ex)
            {
                failed.Add(new ImportFailure { Path = dir, Error = ex.ToString() });
                return new ResponseImportBatch { Imported = imported, Failed = failed };
            }
        }

        public ResponseImportBatch ImportHmiTagTablesFromDirectory(string softwarePath, string folderPath, string dir, string regexName = "", bool overwrite = true)
        {
            var imported = new List<string>();
            var failed = new List<ImportFailure>();

            try
            {
                if (IsProjectNull())
                {
                    failed.Add(new ImportFailure { Path = dir, Error = "Project is null" });
                    return new ResponseImportBatch { Imported = imported, Failed = failed };
                }

                if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
                {
                    failed.Add(new ImportFailure { Path = dir, Error = "Directory not found" });
                    return new ResponseImportBatch { Imported = imported, Failed = failed };
                }

                Regex? regex = null;
                if (!string.IsNullOrWhiteSpace(regexName))
                {
                    regex = new Regex(regexName, RegexOptions.IgnoreCase);
                }

                foreach (var file in Directory.EnumerateFiles(dir, "*.xml", SearchOption.TopDirectoryOnly))
                {
                    var name = Path.GetFileNameWithoutExtension(file);
                    if (regex != null && !regex.IsMatch(name)) continue;

                    try
                    {
                        ImportHmiTagTable(softwarePath, folderPath, file);
                        imported.Add(name);
                    }
                    catch (Exception ex)
                    {
                        failed.Add(new ImportFailure { Path = file, Error = ex.ToString() });
                    }
                }

                return new ResponseImportBatch { Imported = imported, Failed = failed };
            }
            catch (Exception ex)
            {
                failed.Add(new ImportFailure { Path = dir, Error = ex.ToString() });
                return new ResponseImportBatch { Imported = imported, Failed = failed };
            }
        }

        public ResponseSeed SeedProjectFromReference(
            string plcSoftwarePath,
            string hmiSoftwarePath,
            string referenceDir,
            JsonObject? placeholders = null)
        {
            var imported = new List<string>();
            var failed = new List<ImportFailure>();

            placeholders ??= new JsonObject();

            try
            {
                if (IsProjectNull())
                {
                    failed.Add(new ImportFailure { Path = referenceDir, Error = "Project is null" });
                    return new ResponseSeed { Imported = imported, Failed = failed, Placeholders = placeholders };
                }

                if (string.IsNullOrWhiteSpace(referenceDir) || !Directory.Exists(referenceDir))
                {
                    failed.Add(new ImportFailure { Path = referenceDir, Error = "Reference directory not found" });
                    return new ResponseSeed { Imported = imported, Failed = failed, Placeholders = placeholders };
                }

                var manifestPath = Path.Combine(referenceDir, "manifest.json");
                JsonObject? manifest = null;
                if (File.Exists(manifestPath))
                {
                    try
                    {
                        manifest = JsonNode.Parse(File.ReadAllText(manifestPath)) as JsonObject;
                    }
                    catch (Exception ex)
                    {
                        failed.Add(new ImportFailure { Path = manifestPath, Error = $"Failed to parse manifest.json: {ex.Message}" });
                    }
                }

                string plcBlocksDir = Path.Combine(referenceDir, "plc", "blocks");
                string plcTypesDir = Path.Combine(referenceDir, "plc", "types");
                string hmiScreensDir = Path.Combine(referenceDir, "hmi", "screens");
                string hmiTagsDir = Path.Combine(referenceDir, "hmi", "tags");

                var plcBlockGroupPath = manifest?["plcBlockGroupPath"]?.ToString() ?? "";
                var plcTypeGroupPath = manifest?["plcTypeGroupPath"]?.ToString() ?? "";
                var hmiScreenFolderPath = manifest?["hmiScreenFolderPath"]?.ToString() ?? "";
                var hmiTagTableFolderPath = manifest?["hmiTagTableFolderPath"]?.ToString() ?? "";

                if (manifest?["plcBlocksDir"] != null) plcBlocksDir = Path.Combine(referenceDir, manifest["plcBlocksDir"]!.ToString());
                if (manifest?["plcTypesDir"] != null) plcTypesDir = Path.Combine(referenceDir, manifest["plcTypesDir"]!.ToString());
                if (manifest?["hmiScreensDir"] != null) hmiScreensDir = Path.Combine(referenceDir, manifest["hmiScreensDir"]!.ToString());
                if (manifest?["hmiTagTablesDir"] != null) hmiTagsDir = Path.Combine(referenceDir, manifest["hmiTagTablesDir"]!.ToString());

                var tempDir = Path.Combine(Path.GetTempPath(), "tia-seed-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempDir);

                void CopyDirWithReplace(string srcDir, string dstDir)
                {
                    if (!Directory.Exists(srcDir)) return;
                    Directory.CreateDirectory(dstDir);

                    foreach (var file in Directory.EnumerateFiles(srcDir, "*.xml", SearchOption.TopDirectoryOnly))
                    {
                        var text = File.ReadAllText(file, Encoding.UTF8);
                        foreach (var kv in placeholders)
                        {
                            var k = kv.Key;
                            var v = kv.Value?.ToString() ?? "";
                            text = text.Replace("{{" + k + "}}", v);
                        }
                        var outPath = Path.Combine(dstDir, Path.GetFileName(file));
                        File.WriteAllText(outPath, text, Encoding.UTF8);
                    }
                }

                var tempPlcBlocks = Path.Combine(tempDir, "plc", "blocks");
                var tempPlcTypes = Path.Combine(tempDir, "plc", "types");
                var tempHmiScreens = Path.Combine(tempDir, "hmi", "screens");
                var tempHmiTags = Path.Combine(tempDir, "hmi", "tags");

                CopyDirWithReplace(plcBlocksDir, tempPlcBlocks);
                CopyDirWithReplace(plcTypesDir, tempPlcTypes);
                CopyDirWithReplace(hmiScreensDir, tempHmiScreens);
                CopyDirWithReplace(hmiTagsDir, tempHmiTags);

                // PLC blocks
                if (Directory.Exists(tempPlcBlocks))
                {
                    var r = ImportBlocksFromDirectory(plcSoftwarePath, plcBlockGroupPath, tempPlcBlocks, "", overwrite: true);
                    imported.AddRange(r.Imported?.Select(x => "plc:block:" + x) ?? Array.Empty<string>());
                    failed.AddRange(r.Failed?.Select(x => new ImportFailure { Path = x.Path, Error = "plc:block:" + x.Error }) ?? Array.Empty<ImportFailure>());
                }

                // PLC types (UDT)
                if (Directory.Exists(tempPlcTypes))
                {
                    foreach (var file in Directory.EnumerateFiles(tempPlcTypes, "*.xml", SearchOption.TopDirectoryOnly))
                    {
                        var ok = ImportType(plcSoftwarePath, plcTypeGroupPath, file);
                        var name = Path.GetFileNameWithoutExtension(file);
                        if (ok) imported.Add("plc:type:" + name);
                        else failed.Add(new ImportFailure { Path = file, Error = "plc:type:Import failed" });
                    }
                }

                // HMI tag tables then screens
                if (Directory.Exists(tempHmiTags))
                {
                    var r = ImportHmiTagTablesFromDirectory(hmiSoftwarePath, hmiTagTableFolderPath, tempHmiTags);
                    imported.AddRange(r.Imported?.Select(x => "hmi:tagtable:" + x) ?? Array.Empty<string>());
                    failed.AddRange(r.Failed?.Select(x => new ImportFailure { Path = x.Path, Error = "hmi:tagtable:" + x.Error }) ?? Array.Empty<ImportFailure>());
                }

                if (Directory.Exists(tempHmiScreens))
                {
                    var r = ImportHmiScreensFromDirectory(hmiSoftwarePath, hmiScreenFolderPath, tempHmiScreens);
                    imported.AddRange(r.Imported?.Select(x => "hmi:screen:" + x) ?? Array.Empty<string>());
                    failed.AddRange(r.Failed?.Select(x => new ImportFailure { Path = x.Path, Error = "hmi:screen:" + x.Error }) ?? Array.Empty<ImportFailure>());
                }

                return new ResponseSeed
                {
                    Message = $"Seed applied from '{referenceDir}'",
                    Imported = imported,
                    Failed = failed,
                    Placeholders = placeholders,
                    TempDir = tempDir,
                };
            }
            catch (Exception ex)
            {
                failed.Add(new ImportFailure { Path = referenceDir, Error = ex.ToString() });
                return new ResponseSeed { Imported = imported, Failed = failed, Placeholders = placeholders };
            }
        }

        private static string MakeSafeFileName(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }
            return name;
        }

        private static bool TryExportEngineeringObject(object engineeringObject, string exportPath, out string? error)
        {
            error = null;
            try
            {
                var fi = new FileInfo(exportPath);
                fi.Directory?.Create();
                if (fi.Exists) fi.Delete();

                var t = engineeringObject.GetType();

                // Prefer Export(FileInfo, ExportOptions)
                var m2 = t.GetMethod("Export", new[] { typeof(FileInfo), typeof(ExportOptions) });
                if (m2 != null)
                {
                    m2.Invoke(engineeringObject, new object[] { fi, ExportOptions.None });
                    return true;
                }

                // Some engineering objects (notably HMI Unified) use Export(FileInfo, <OtherOptionsEnum>)
                // We best-effort call the first Export overload whose first parameter is FileInfo.
                var any = t.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .Where(m => string.Equals(m.Name, "Export", StringComparison.OrdinalIgnoreCase))
                    .Select(m => new { Method = m, Params = m.GetParameters() })
                    .FirstOrDefault(x => x.Params.Length == 2 && x.Params[0].ParameterType == typeof(FileInfo));

                if (any != null)
                {
                    var p2 = any.Params[1].ParameterType;
                    object? arg2 = null;

                    if (p2.IsEnum)
                    {
                        // use default enum value (0) or first defined value
                        arg2 = Enum.ToObject(p2, 0);
                    }
                    else if (p2 == typeof(bool))
                    {
                        arg2 = false;
                    }
                    else if (p2 == typeof(int))
                    {
                        arg2 = 0;
                    }
                    else
                    {
                        // unknown option type; try null if allowed
                        if (!p2.IsValueType) arg2 = null;
                        else arg2 = Activator.CreateInstance(p2);
                    }

                    any.Method.Invoke(engineeringObject, new[] { (object)fi, arg2! });
                    return true;
                }

                // Export(FileInfo)
                var m1 = t.GetMethod("Export", new[] { typeof(FileInfo) });
                if (m1 != null)
                {
                    m1.Invoke(engineeringObject, new object[] { fi });
                    return true;
                }
            }
            catch (TargetInvocationException tie) when (tie.InnerException != null)
            {
                error = $"{tie.InnerException.GetType().FullName}: {tie.InnerException.Message}";
            }
            catch (Exception ex)
            {
                error = ex.ToString();
            }

            return false;
        }

        private static bool TryImportEngineeringObjectIntoCollection(object collection, string importPath, out string? importedName, out string? error)
        {
            importedName = null;
            error = null;

            try
            {
                var fi = new FileInfo(importPath);
                if (!fi.Exists)
                {
                    error = "File not found";
                    return false;
                }

                var t = collection.GetType();

                // Prefer Import(FileInfo, ImportOptions)
                var m2 = t.GetMethod("Import", new[] { typeof(FileInfo), typeof(ImportOptions) });
                if (m2 != null)
                {
                    var list = m2.Invoke(collection, new object[] { fi, ImportOptions.Override });
                    importedName = BestEffortExtractFirstName(list) ?? Path.GetFileNameWithoutExtension(importPath);
                    return true;
                }

                // Import(FileInfo)
                var m1 = t.GetMethod("Import", new[] { typeof(FileInfo) });
                if (m1 != null)
                {
                    var list = m1.Invoke(collection, new object[] { fi });
                    importedName = BestEffortExtractFirstName(list) ?? Path.GetFileNameWithoutExtension(importPath);
                    return true;
                }

                error = $"No Import method found on collection type {t.FullName}";
                return false;
            }
            catch (TargetInvocationException tie) when (tie.InnerException != null)
            {
                error = $"{tie.InnerException.GetType().FullName}: {tie.InnerException.Message}";
                return false;
            }
            catch (Exception ex)
            {
                error = ex.ToString();
                return false;
            }
        }

        private static string? BestEffortExtractFirstName(object? importReturnValue)
        {
            try
            {
                if (importReturnValue is IEnumerable enumerable)
                {
                    foreach (var item in enumerable)
                    {
                        if (item == null) continue;
                        var name = item.GetType().GetProperty("Name")?.GetValue(item)?.ToString();
                        if (!string.IsNullOrWhiteSpace(name)) return name;
                    }
                }
            }
            catch { }
            return null;
        }
    }
}
