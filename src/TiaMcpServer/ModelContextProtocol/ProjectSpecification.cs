using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;

namespace TiaMcpServer.ModelContextProtocol
{
    internal enum ProjectWorkflowMode { Create, Patch }

    internal sealed class ProjectSpecification
    {
        public ProjectWorkflowMode Mode { get; private set; }
        public string ProjectName { get; private set; } = "";
        public string ProjectPath { get; private set; } = "";
        public string DirectoryPath { get; private set; } = "";
        public string PlcName { get; private set; } = "PLC_1";
        public string PlcFamily { get; private set; } = "S7-1500";
        public string PlcMlfb { get; private set; } = "";
        public string HmiName { get; private set; } = "";
        public string HmiFamily { get; private set; } = "WinCCUnifiedPC";
        public string HmiSoftwarePath { get; private set; } = "";
        public string ConnectionName { get; private set; } = "HMI_Connection_1";
        public bool Compile { get; private set; } = true;
        public bool Save { get; private set; } = true;
        public bool HasHmi => !string.IsNullOrWhiteSpace(HmiName);
        public IReadOnlyList<ProjectPlcArtifact> PlcArtifacts { get; private set; } = System.Array.Empty<ProjectPlcArtifact>();
        public IReadOnlyList<string> SclSourceFiles { get; private set; } = System.Array.Empty<string>();
        public IReadOnlyList<ProjectLadDocument> LadDocuments { get; private set; } = System.Array.Empty<ProjectLadDocument>();
        public IReadOnlyList<ProjectHmiScreen> HmiScreens { get; private set; } = System.Array.Empty<ProjectHmiScreen>();
        public IReadOnlyList<ProjectHmiTag> HmiTags { get; private set; } = System.Array.Empty<ProjectHmiTag>();

        public static ProjectSpecification Parse(string json, ProjectWorkflowMode mode)
        {
            var root = Object(JsonNode.Parse(json), "$");
            KnownFields(root, "$", "projectName", "projectPath", "directoryPath", "plcName", "plcFamily", "plcMlfb",
                "hmiName", "hmiFamily", "hmiSoftwarePath", "connectionName", "udt", "globalDb", "tagTable",
                "sclSourceFiles", "ladDocs", "hmiScreens", "hmiTags", "compile", "save");

            var spec = new ProjectSpecification
            {
                Mode = mode,
                ProjectName = String(root, "projectName", "$", "", mode == ProjectWorkflowMode.Create),
                ProjectPath = String(root, "projectPath", "$", "", mode == ProjectWorkflowMode.Patch),
                DirectoryPath = String(root, "directoryPath", "$"),
                PlcName = String(root, "plcName", "$", "PLC_1", true),
                PlcFamily = String(root, "plcFamily", "$", "S7-1500", true),
                PlcMlfb = String(root, "plcMlfb", "$"),
                HmiName = String(root, "hmiName", "$"),
                HmiFamily = String(root, "hmiFamily", "$", "WinCCUnifiedPC", true),
                HmiSoftwarePath = String(root, "hmiSoftwarePath", "$"),
                ConnectionName = String(root, "connectionName", "$", "HMI_Connection_1", true),
                Compile = Boolean(root, "compile", "$", true),
                Save = Boolean(root, "save", "$", true)
            };

            if (mode == ProjectWorkflowMode.Patch)
            {
                spec.ProjectPath = Path.GetFullPath(spec.ProjectPath);
                spec.ProjectName = Path.GetFileNameWithoutExtension(spec.ProjectPath);
                spec.DirectoryPath = spec.ProjectPath;
            }
            else
            {
                FileName(spec.ProjectName, "$.projectName");
                spec.DirectoryPath = string.IsNullOrWhiteSpace(spec.DirectoryPath)
                    ? Path.Combine(Path.GetTempPath(), "tia_mcp_scaffold_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"))
                    : Path.GetFullPath(spec.DirectoryPath);
            }

            var artifacts = new List<ProjectPlcArtifact>();
            foreach (var pair in new[] { (Key: "udt", Kind: "udt"), (Key: "globalDb", Kind: "globaldb"), (Key: "tagTable", Kind: "tagtable") })
            {
                var array = Array(root, pair.Key, "$");
                for (var i = 0; i < array.Count; i++)
                {
                    var path = "$." + pair.Key + "[" + i + "]";
                    var item = Object(array[i], path);
                    artifacts.Add(new ProjectPlcArtifact(pair.Kind, item.ToJsonString(), path));
                }
            }
            spec.PlcArtifacts = artifacts;

            var sources = Array(root, "sclSourceFiles", "$");
            spec.SclSourceFiles = sources.Select((item, i) =>
                Path.GetFullPath(StringValue(item, "$.sclSourceFiles[" + i + "]", true))).ToArray();
            spec.LadDocuments = Array(root, "ladDocs", "$").Select((item, i) =>
            {
                var path = "$.ladDocs[" + i + "]";
                var obj = Object(item, path);
                KnownFields(obj, path, "importPath", "name");
                var name = String(obj, "name", path, "", true);
                FileName(name, path + ".name");
                return new ProjectLadDocument(Path.GetFullPath(String(obj, "importPath", path, "", true)), name, path);
            }).ToArray();
            spec.HmiScreens = Array(root, "hmiScreens", "$").Select((item, i) =>
            {
                var path = "$.hmiScreens[" + i + "]";
                var obj = Object(item, path);
                KnownFields(obj, path, "screenName", "width", "height", "designJson");
                var design = Object(obj["designJson"], path + ".designJson");
                ValidateHmiDesign(design, path + ".designJson");
                return new ProjectHmiScreen(String(obj, "screenName", path, "", true),
                    UnsignedInteger(obj, "width", path), UnsignedInteger(obj, "height", path), design.ToJsonString());
            }).ToArray();
            spec.HmiTags = Array(root, "hmiTags", "$").Select((item, i) =>
            {
                var path = "$.hmiTags[" + i + "]";
                var obj = Object(item, path);
                KnownFields(obj, path, "tagTableName", "tagName", "hmiDataType", "plcTag", "address");
                return new ProjectHmiTag(String(obj, "tagTableName", path, "Default tag table", true),
                    String(obj, "tagName", path, "", true), String(obj, "hmiDataType", path, "Bool", true),
                    String(obj, "plcTag", path), String(obj, "address", path));
            }).ToArray();

            if (!spec.HasHmi && (spec.HmiScreens.Count > 0 || spec.HmiTags.Count > 0 || spec.HmiSoftwarePath.Length > 0))
                throw new ArgumentException("$.hmiName is required when HMI screens, tags, or a software path are supplied.");
            return spec;
        }

        private static void ValidateHmiDesign(JsonObject design, string path)
        {
            KnownFields(design, path, "screen", "items", "width", "height");
            foreach (var field in new[] { "width", "height" })
                if (design.ContainsKey(field)) UnsignedInteger(design, field, path);
            if (design.ContainsKey("screen")) PropertyBag(design["screen"], path + ".screen");
            if (!design.ContainsKey("items")) throw new ArgumentException(path + ".items must be an array.");
            var items = Array(design, "items", path);
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < items.Count; i++)
            {
                var itemPath = path + ".items[" + i + "]";
                var item = Object(items[i], itemPath);
                KnownFields(item, itemPath, "name", "type", "left", "top", "width", "height", "text", "textProperty",
                    "culture", "properties", "font", "content", "padding");
                var name = String(item, "name", itemPath, "", true);
                if (!names.Add(name)) throw new ArgumentException(itemPath + ".name duplicates '" + name + "'.");
                foreach (var field in new[] { "type", "text", "textProperty", "culture" })
                    if (item.ContainsKey(field)) String(item, field, itemPath);
                foreach (var field in new[] { "left", "top" })
                    if (item.ContainsKey(field)) Integer(item, field, itemPath);
                foreach (var field in new[] { "width", "height" })
                    if (item.ContainsKey(field)) UnsignedInteger(item, field, itemPath);
                foreach (var field in new[] { "properties", "font", "content", "padding" })
                    if (item.ContainsKey(field)) PropertyBag(item[field], itemPath + "." + field);
            }
        }

        private static void PropertyBag(JsonNode? node, string path)
        {
            foreach (var property in Object(node, path))
                if (string.IsNullOrWhiteSpace(property.Key))
                    throw new ArgumentException(path + "[" + JsonValue.Create(property.Key)!.ToJsonString() + "] has an empty property name.");
        }

        private static JsonObject Object(JsonNode? node, string path) =>
            node as JsonObject ?? throw new ArgumentException(path + " must be an object.");

        private static JsonArray Array(JsonObject obj, string name, string path)
        {
            if (!obj.TryGetPropertyValue(name, out var node)) return new JsonArray();
            return node as JsonArray ?? throw new ArgumentException(path + "." + name + " must be an array.");
        }

        private static string String(JsonObject obj, string name, string path, string fallback = "", bool nonEmpty = false)
        {
            if (!obj.TryGetPropertyValue(name, out var node))
            {
                if (nonEmpty && string.IsNullOrWhiteSpace(fallback)) throw new ArgumentException(path + "." + name + " is required.");
                return fallback;
            }
            return StringValue(node, path + "." + name, nonEmpty);
        }

        private static string StringValue(JsonNode? node, string path, bool nonEmpty)
        {
            if (node is not JsonValue value || !value.TryGetValue<string>(out var result) || result == null)
                throw new ArgumentException(path + " must be a string.");
            if (nonEmpty && string.IsNullOrWhiteSpace(result)) throw new ArgumentException(path + " must be a non-empty string.");
            return result;
        }

        private static bool Boolean(JsonObject obj, string name, string path, bool fallback)
        {
            if (!obj.TryGetPropertyValue(name, out var node)) return fallback;
            if (node is JsonValue value && value.TryGetValue<bool>(out var result)) return result;
            throw new ArgumentException(path + "." + name + " must be a boolean.");
        }

        private static int Integer(JsonObject obj, string name, string path)
        {
            if (WholeNumber(obj[name], out var result) && result >= int.MinValue && result <= int.MaxValue) return (int)result;
            throw new ArgumentException(path + "." + name + " must be a 32-bit integer.");
        }

        private static uint UnsignedInteger(JsonObject obj, string name, string path)
        {
            if (!obj.TryGetPropertyValue(name, out var node)) return 0;
            if (WholeNumber(node, out var result) && result >= 0 && result <= uint.MaxValue) return (uint)result;
            throw new ArgumentException(path + "." + name + " must be a non-negative 32-bit integer.");
        }

        private static bool WholeNumber(JsonNode? node, out decimal result)
        {
            result = 0;
            return node is JsonValue value && value.TryGetValue<decimal>(out result) && decimal.Truncate(result) == result;
        }

        private static void FileName(string value, string path)
        {
            if (value == "." || value == ".." || value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || value.IndexOf('/') >= 0 || value.IndexOf('\\') >= 0)
                throw new ArgumentException(path + " must be a file name without directory separators.");
        }

        private static void KnownFields(JsonObject obj, string path, params string[] names)
        {
            var allowed = new HashSet<string>(names, StringComparer.Ordinal);
            foreach (var property in obj)
                if (!allowed.Contains(property.Key)) throw new ArgumentException(path + "." + property.Key + " is not a supported field.");
        }
    }

    internal sealed class ProjectPlcArtifact
    {
        public ProjectPlcArtifact(string kind, string json, string specPath) { Kind = kind; Json = json; SpecPath = specPath; }
        public string Kind { get; }
        public string Json { get; }
        public string SpecPath { get; }
    }

    internal sealed class ProjectLadDocument
    {
        public ProjectLadDocument(string importPath, string name, string specPath) { ImportPath = importPath; Name = name; SpecPath = specPath; }
        public string ImportPath { get; }
        public string Name { get; }
        public string SpecPath { get; }
    }

    internal sealed class ProjectHmiScreen
    {
        public ProjectHmiScreen(string name, uint width, uint height, string designJson) { Name = name; Width = width; Height = height; DesignJson = designJson; }
        public string Name { get; }
        public uint Width { get; }
        public uint Height { get; }
        public string DesignJson { get; }
    }

    internal sealed class ProjectHmiTag
    {
        public ProjectHmiTag(string tableName, string name, string dataType, string plcTag, string address)
        { TableName = tableName; Name = name; DataType = dataType; PlcTag = plcTag; Address = address; }
        public string TableName { get; }
        public string Name { get; }
        public string DataType { get; }
        public string PlcTag { get; }
        public string Address { get; }
    }
}
