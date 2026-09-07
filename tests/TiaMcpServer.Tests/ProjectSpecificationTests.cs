using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using TiaMcpServer.ModelContextProtocol;

namespace TiaMcpServer.Tests
{
    internal static class ProjectSpecificationTests
    {
        public static void Run(Action<bool, string> check)
        {
            var minimal = ProjectSpecification.Parse("{\"projectName\":\"Demo\"}", ProjectWorkflowMode.Create);
            check(minimal.ProjectName == "Demo" && minimal.PlcName == "PLC_1" && minimal.PlcFamily == "S7-1500", "spec: default PLC settings survive parsing");
            check(minimal.Compile && minimal.Save && !minimal.HasHmi && minimal.PlcArtifacts.Count == 0, "spec: omitted options keep documented defaults");
            check(Path.IsPathRooted(minimal.DirectoryPath), "spec: default project directory is absolute");

            var complete = ProjectSpecification.Parse("""
                {"projectName":"Demo","directoryPath":".","plcName":"CPU","plcMlfb":"order",
                 "compile":false,"save":false,"hmiName":"Panel","connectionName":"Connection",
                 "udt":[{"name":"UDT_1","members":[{"name":"Value","datatype":"Bool","externalWritable":false}]}],
                 "globalDb":[{"dbName":"DB_1","number":1,"staticMembers":[{"name":"Value","datatype":"Int","startValue":42}]}],
                 "tagTable":[{"tableName":"Tags","tags":[{"name":"Start","dataTypeName":"Bool","logicalAddress":"%M0.0"}]}],
                 "sclSourceFiles":["source.scl"],"ladDocs":[{"importPath":".","name":"Main"}],
                 "hmiScreens":[{"screenName":"Main","width":1024,"height":600,"designJson":
                    {"screen":{"Width":1024},"items":[{"name":"Title","type":"Text","left":-1,"top":2,"width":200,"height":30,
                       "text":"001","textProperty":"Text","culture":"zh-CN","properties":{},"font":{"Size":12},"content":{},"padding":{}}]}}],
                 "hmiTags":[{"tagName":"Start","plcTag":"Start","address":"%M0.0"}]}
                """, ProjectWorkflowMode.Create);
            check(!complete.Compile && !complete.Save && complete.HasHmi, "spec: explicit false and HMI selection are preserved");
            check(complete.PlcArtifacts.Select(item => item.Kind).SequenceEqual(new[] { "udt", "globaldb", "tagtable" }), "spec: PLC artifacts remain in dependency order");
            check(complete.PlcArtifacts[2].SpecPath == "$.tagTable[0]" && complete.PlcArtifacts[0].Json.Contains("externalWritable"), "spec: PLC payload and diagnostic path survive parsing");
            check(complete.PlcArtifacts[1].Json.Contains("\"startValue\":42"), "spec: numeric PLC start values remain available to the existing DSL builder");
            check(complete.HmiScreens[0].Width == 1024 && complete.HmiScreens[0].Height == 600 && complete.HmiScreens[0].DesignJson.Contains("001"), "spec: HMI dimensions and text remain intact");
            check(complete.HmiTags[0].TableName == "Default tag table" && complete.HmiTags[0].DataType == "Bool", "spec: HMI tag defaults remain intact");
            check(complete.LadDocuments[0].SpecPath == "$.ladDocs[0]" && Path.IsPathRooted(complete.SclSourceFiles[0]), "spec: source and document paths are normalized once");

            var maximum = ProjectSpecification.Parse("""
                {"projectName":"Demo","hmiName":"Panel","hmiScreens":[{"screenName":"Main","width":4294967295,"designJson":{"items":[]}}]}
                """, ProjectWorkflowMode.Create);
            check(maximum.HmiScreens[0].Width == uint.MaxValue && maximum.HmiScreens[0].Height == 0, "spec: uint width boundary and omitted height are supported");
            var decimalDimensions = ProjectSpecification.Parse("""
                {"projectName":"Demo","hmiName":"Panel","hmiScreens":[{"screenName":"Main","width":120.0,"height":12.0,
                 "designJson":{"width":120.0,"height":12.0,"items":[{"name":"Title","left":12.0,"top":-12.0,"width":120.0,"height":12.0}]}}]}
                """, ProjectWorkflowMode.Create);
            check(decimalDimensions.HmiScreens[0].Width == 120 && decimalDimensions.HmiScreens[0].Height == 12,
                "spec: mathematically integral JSON dimensions and coordinates retain compatibility");
            var builtDesign = HmiTemplateDesignJsonBuilder.BuildApplyDesign(new JsonObject
            {
                ["Screen"] = new JsonObject { ["Width"] = 800, ["Height"] = 480 },
                ["Items"] = new JsonArray(new JsonObject { ["Name"] = "Title", ["Type"] = "Text", ["Text"] = "Title" })
            }, 800, 480);
            var builtSpec = new JsonObject
            {
                ["projectName"] = "Demo", ["hmiName"] = "Panel",
                ["hmiScreens"] = new JsonArray(new JsonObject { ["screenName"] = "Main", ["designJson"] = builtDesign })
            };
            check(ProjectSpecification.Parse(builtSpec.ToJsonString(), ProjectWorkflowMode.Create).HmiScreens[0].DesignJson == builtDesign.ToJsonString(),
                "spec: real HMI template builder output including top-level dimensions is accepted unchanged");
            var patch = ProjectSpecification.Parse("{\"projectPath\":\"Existing.ap21\"}", ProjectWorkflowMode.Patch);
            check(patch.ProjectName == "Existing" && patch.DirectoryPath == patch.ProjectPath && Path.IsPathRooted(patch.ProjectPath), "spec: patch preserves project-path response semantics");

            var invalid = new[]
            {
                ("[]", "$"),
                ("null", "$"),
                ("{}", "$.projectName"),
                ("{\"projectName\":\" \"}", "$.projectName"),
                ("{\"projectName\":42}", "$.projectName"),
                ("{\"projectName\":\"../Demo\"}", "$.projectName"),
                ("{\"projectName\":\"Demo\",\"plcName\":\"\"}", "$.plcName"),
                ("{\"projectName\":\"Demo\",\"compile\":\"false\"}", "$.compile"),
                ("{\"projectName\":\"Demo\",\"save\":null}", "$.save"),
                ("{\"projectName\":\"Demo\",\"sav\":false}", "$.sav"),
                ("{\"projectName\":\"Demo\",\"udt\":{}}", "$.udt"),
                ("{\"projectName\":\"Demo\",\"udt\":null}", "$.udt"),
                ("{\"projectName\":\"Demo\",\"udt\":[null]}", "$.udt[0]"),
                ("{\"projectName\":\"Demo\",\"sclSourceFiles\":[42]}", "$.sclSourceFiles[0]"),
                ("{\"projectName\":\"Demo\",\"sclSourceFiles\":[\"\"]}", "$.sclSourceFiles[0]"),
                ("{\"projectName\":\"Demo\",\"ladDocs\":[{\"name\":\"Main\"}]}", "$.ladDocs[0].importPath"),
                ("{\"projectName\":\"Demo\",\"ladDocs\":[{\"name\":\"../Main\",\"importPath\":\".\"}]}", "$.ladDocs[0].name"),
                ("{\"projectName\":\"Demo\",\"hmiTags\":[{\"tagName\":\"Start\"}]}", "$.hmiName"),
                ("{\"projectName\":\"Demo\",\"hmiName\":\"Panel\",\"hmiTags\":[{\"tagName\":false}]}", "$.hmiTags[0].tagName")
            };
            foreach (var example in invalid) Reject(example.Item1, example.Item2, check);
            Reject("{}", "$.projectPath", check, ProjectWorkflowMode.Patch);

            foreach (var field in new[] { "\"width\":-1", "\"width\":1.5", "\"width\":4294967296", "\"height\":\"600\"" })
                Reject("{\"projectName\":\"Demo\",\"hmiName\":\"Panel\",\"hmiScreens\":[{\"screenName\":\"Main\","
                    + field + ",\"designJson\":{\"items\":[]}}]}", "$.hmiScreens[0]", check);
            foreach (var design in new[] { "42", "null", "[]", "{}", "{\"items\":{}}", "{\"width\":-1,\"items\":[]}", "{\"height\":\"12\",\"items\":[]}", "{\"screen\":42,\"items\":[]}", "{\"items\":[null]}",
                "{\"items\":[{}]}", "{\"items\":[{\"name\":\"A\",\"properties\":[]}]}",
                "{\"items\":[{\"name\":\"A\"},{\"name\":\"a\"}]}" })
                Reject("{\"projectName\":\"Demo\",\"hmiName\":\"Panel\",\"hmiScreens\":[{\"screenName\":\"Main\",\"designJson\":"
                    + design + "}]}", "$.hmiScreens[0].designJson", check);
            foreach (var field in new[] { "screen", "properties", "font", "content", "padding" })
            {
                var item = new JsonObject { ["name"] = "Title", ["type"] = "Text" };
                var design = new JsonObject { ["items"] = new JsonArray(item) };
                var bag = new JsonObject { [field == "screen" ? " " : ""] = 1 };
                if (field == "screen") design[field] = bag;
                else item[field] = bag;
                var root = new JsonObject
                {
                    ["projectName"] = "Demo", ["hmiName"] = "Panel",
                    ["hmiScreens"] = new JsonArray(new JsonObject { ["screenName"] = "Main", ["designJson"] = design })
                };
                Reject(root.ToJsonString(), "$.hmiScreens[0].designJson." + (field == "screen" ? "screen" : "items[0]." + field), check);
            }
        }

        private static void Reject(string json, string path, Action<bool, string> check, ProjectWorkflowMode mode = ProjectWorkflowMode.Create)
        {
            try
            {
                ProjectSpecification.Parse(json, mode);
                check(false, "spec: reject invalid field " + path + " in " + json);
            }
            catch (ArgumentException ex)
            {
                check(ex.Message.Contains(path), "spec: validation identifies " + path);
            }
        }
    }
}
