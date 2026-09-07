using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using TiaMcpServer.Cli;
using YamlDotNet.Core;

namespace TiaMcpServer.Tests
{
    internal static class SpecLoaderTests
    {
        internal static void Run(Action<bool, string> check)
        {
            RunStringTests(check);
            RunScalarTests(check);
            RunNestedTests(check);
            RunFileTests(check);
        }

        private static void RunStringTests(Action<bool, string> check)
        {
            var root = JsonNode.Parse(SpecLoader.YamlToJson(@"
projectName: ""001""
plcName: 'true'
screenName: ""007""
falseText: 'false'
nullText: ""null""
tildeText: '~'
fractionText: '1.25'
exponentText: ""1e3""
empty: """"
plain: PLC_1
address: '%M0.0'
taggedNumber: !!str 001
taggedBool: !!str true
literal: |-
  123
folded: >-
  false
"));

            foreach (var (key, expected) in new[]
            {
                ("projectName", "001"), ("plcName", "true"), ("screenName", "007"),
                ("falseText", "false"), ("nullText", "null"), ("tildeText", "~"),
                ("fractionText", "1.25"), ("exponentText", "1e3"), ("empty", ""),
                ("plain", "PLC_1"), ("address", "%M0.0"), ("taggedNumber", "001"),
                ("taggedBool", "true"), ("literal", "123"), ("folded", "false")
            })
                check(StringIs(root?[key], expected), "YAML preserves string value and type: " + key);
        }

        private static void RunScalarTests(Action<bool, string> check)
        {
            var originalCulture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
                var root = JsonNode.Parse(SpecLoader.YamlToJson(@"
integer: 21
leadingZero: 001
negative: -3
largeInteger: 9007199254740993
fraction: 1.25
preciseFraction: 12345678.901234
exponent: 1.2345678901234e-9
taggedFraction: !!float 12345678.901234
compile: false
save: true
nullValue: null
tilde: ~
emptyValue:
"));
                check(root?["integer"]?.GetValue<int>() == 21 && root?["leadingZero"]?.GetValue<int>() == 1 &&
                      root?["negative"]?.GetValue<int>() == -3,
                    "YAML plain integers retain numeric types");
                check(root?["largeInteger"]?.GetValue<long>() == 9007199254740993L,
                    "YAML integers retain precision beyond the exact double range");
                check(root?["fraction"]?.GetValue<double>() == 1.25,
                    "YAML decimal syntax is independent of the current culture");
                check(root?["preciseFraction"]?.GetValue<double>() == 12345678.901234 &&
                      root?["taggedFraction"]?.GetValue<double>() == 12345678.901234,
                    "YAML fractions preserve double precision without narrowing to float");
                check(root?["exponent"]?.GetValue<double>() == 1.2345678901234e-9,
                    "YAML scientific notation preserves double precision");
                check(root?["compile"]?.GetValue<bool>() == false && root?["save"]?.GetValue<bool>() == true,
                    "YAML plain booleans retain boolean types");
                check(root is JsonObject map && map.ContainsKey("nullValue") && map["nullValue"] == null &&
                      map.ContainsKey("tilde") && map["tilde"] == null &&
                      map.ContainsKey("emptyValue") && map["emptyValue"] == null,
                    "YAML null, tilde and empty values remain explicit JSON nulls");
            }
            finally
            {
                CultureInfo.CurrentCulture = originalCulture;
            }

            check(SpecLoader.YamlToJson("") == "{}" && SpecLoader.YamlToJson("null") == "{}",
                "YAML empty documents preserve the existing empty-object result");
            check(Throws<YamlException>(() => SpecLoader.YamlToJson("screens: [")),
                "Malformed YAML reports a parsing error");
        }

        private static void RunNestedTests(Action<bool, string> check)
        {
            var root = JsonNode.Parse(SpecLoader.YamlToJson(@"
defaults: &screen
  screenName: ""007""
  width: 800
hmiScreens:
  - *screen
  - screenName: '008'
    values: [true, false, 1.25, '001', null, {name: 'false'}]
"));
            var screens = root?["hmiScreens"] as JsonArray;
            check(screens?.Count == 2 && StringIs(screens[0]?["screenName"], "007") &&
                  screens[0]?["width"]?.GetValue<int>() == 800,
                "YAML aliases preserve nested mappings and scalar types");
            var values = screens?[1]?["values"] as JsonArray;
            check(values?.Count == 6 && values[0]?.GetValue<bool>() == true && values[1]?.GetValue<bool>() == false &&
                  values[2]?.GetValue<double>() == 1.25 && StringIs(values[3], "001") && values[4] == null &&
                  StringIs(values[5]?["name"], "false"),
                "YAML flow arrays preserve nested objects, strings, numbers, booleans and nulls");
            check(JsonNode.Parse(SpecLoader.YamlToJson("[1, '001', false, null]")) is JsonArray array &&
                  array.Count == 4 && StringIs(array[1], "001"),
                "YAML root sequences remain JSON arrays for the spec validator to inspect");
        }

        private static void RunFileTests(Action<bool, string> check)
        {
            var directory = Path.Combine(Path.GetTempPath(), "tia_spec_loader_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                const string json = "  {\"projectName\":\"001\",\"compile\":false,\"value\":1.2345678901234}\n";
                var jsonPath = Path.Combine(directory, "spec.json");
                File.WriteAllText(jsonPath, json, new UTF8Encoding(true));
                check(SpecLoader.LoadAsJson(jsonPath) == json,
                    "JSON input passes through unchanged after its UTF-8 BOM is removed");

                var sniffPath = Path.Combine(directory, "spec.data");
                File.WriteAllText(sniffPath, json);
                check(SpecLoader.LoadAsJson(sniffPath) == json,
                    "JSON objects with unknown extensions pass through unchanged");
                File.WriteAllText(sniffPath, "  [1, \"001\", false]\n");
                check(SpecLoader.LoadAsJson(sniffPath) == "  [1, \"001\", false]\n",
                    "JSON arrays with unknown extensions pass through unchanged");

                foreach (var extension in new[] { ".yaml", ".YML", ".data" })
                {
                    var path = Path.Combine(directory, "quoted" + extension);
                    File.WriteAllText(path, "projectName: '001'\ncompile: false\n");
                    var parsed = JsonNode.Parse(SpecLoader.LoadAsJson(path));
                    check(StringIs(parsed?["projectName"], "001") && parsed?["compile"]?.GetValue<bool>() == false,
                        "YAML file detection preserves scalar types: " + extension);
                }
                check(Throws<FileNotFoundException>(() => SpecLoader.LoadAsJson(Path.Combine(directory, "missing.yaml"))),
                    "Missing spec files report FileNotFoundException");

                RunBundleTests(directory, check);
            }
            finally
            {
                var expectedPrefix = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                var fullDirectory = Path.GetFullPath(directory);
                if (fullDirectory.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase) &&
                    Path.GetFileName(fullDirectory).StartsWith("tia_spec_loader_test_", StringComparison.Ordinal))
                    Directory.Delete(fullDirectory, true);
            }
        }

        private static void RunBundleTests(string directory, Action<bool, string> check)
        {
            var originalBaseDirectory = AppContext.GetData("APP_CONTEXT_BASE_DIRECTORY");
            var bundle = Path.Combine(directory, "bundle");
            Directory.CreateDirectory(Path.Combine(bundle, "templates"));
            Directory.CreateDirectory(Path.Combine(bundle, "tools"));
            var runtime = Path.Combine(bundle, "runtime", "engine");
            Directory.CreateDirectory(runtime);
            try
            {
                AppContext.SetData("APP_CONTEXT_BASE_DIRECTORY", runtime);
                var jsonPath = Path.Combine(directory, "bundle.json");
                File.WriteAllText(jsonPath, @"{""sclSourceFiles"":[""__BUNDLE__/templates/Flow.scl""],""ladDocs"":[{""importPath"":""__BUNDLE__\\templates\\lad"",""name"":""Main""}],""bundleRoot"":""__BUNDLE__""}");
                var json = JsonNode.Parse(SpecLoader.LoadAsJson(jsonPath));
                var normalizedBundle = bundle.Replace('\\', '/');
                check(StringIs(json?["sclSourceFiles"]?[0], normalizedBundle + "/templates/Flow.scl") &&
                      StringIs(json?["ladDocs"]?[0]?["importPath"], normalizedBundle + "/templates\\lad") &&
                      StringIs(json?["bundleRoot"], normalizedBundle),
                    "Bundle substitution preserves JSON forward-slash, escaped-backslash and root-only forms");

                var yamlPath = Path.Combine(directory, "bundle.yaml");
                File.WriteAllText(yamlPath, "projectName: '001'\nsclSourceFiles:\n  - '__BUNDLE__/templates/Flow.scl'\n");
                var yaml = JsonNode.Parse(SpecLoader.LoadAsJson(yamlPath));
                check(StringIs(yaml?["projectName"], "001") &&
                      StringIs(yaml?["sclSourceFiles"]?[0], normalizedBundle + "/templates/Flow.scl"),
                    "Bundle substitution preserves quoted YAML names and file paths");

                var isolated = Path.Combine(directory, "isolated");
                Directory.CreateDirectory(isolated);
                AppContext.SetData("APP_CONTEXT_BASE_DIRECTORY", isolated);
                var unresolved = JsonNode.Parse(SpecLoader.LoadAsJson(yamlPath));
                check(StringIs(unresolved?["sclSourceFiles"]?[0], "__BUNDLE__/templates/Flow.scl"),
                    "Bundle tokens remain intact when no bundle root is available");
            }
            finally
            {
                AppContext.SetData("APP_CONTEXT_BASE_DIRECTORY", originalBaseDirectory);
            }
        }

        private static bool StringIs(JsonNode? node, string expected) =>
            node is JsonValue value && value.TryGetValue<string>(out var actual) && actual == expected;

        private static bool Throws<T>(Action action) where T : Exception
        {
            try { action(); return false; }
            catch (T) { return true; }
        }
    }
}
