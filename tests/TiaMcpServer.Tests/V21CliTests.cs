using System;
using System.IO;
using System.Text.Json.Nodes;
using TiaMcpServer.Cli;

namespace TiaMcpServer.Tests
{
    internal static class V21CliTests
    {
        internal static void Run(Action<bool, string> check)
        {
            RunArgumentTests(check);
            RunConfigTests(check);
        }

        private static void RunArgumentTests(Action<bool, string> check)
        {
            var defaults = CliOptions.ParseArgs(Array.Empty<string>());
            check(defaults.Profile == null && !defaults.PortalWithUserInterface,
                "V21 CLI: default startup needs no version argument");

            var compatible = CliOptions.ParseArgs(new[]
            {
                "--tia-major-version", "21", "--profile", "full", "--with-ui"
            });
            check(compatible.Profile == "full" && compatible.PortalWithUserInterface,
                "V21 CLI: the legacy fixed value preserves the following options");

            var singleDash = CliOptions.ParseArgs(new[]
            {
                "-TIA-MAJOR-VERSION", "21", "--tia-portal-location", @"D:\TIA21\Portal V21"
            });
            check(singleDash.TiaPortalLocation == @"D:\TIA21\Portal V21",
                "V21 CLI: the legacy single-dash option remains case-insensitive");

            foreach (var value in new[] { "20", "22", "0", "-1", "abc", "" })
                check(RejectsVersion(new[] { "--tia-major-version", value }),
                    "V21 CLI: reject unsupported or invalid version value '" + value + "'");

            check(RejectsVersion(new[] { "--tia-major-version" }),
                "V21 CLI: a missing legacy version value is an argument error");
            check(RejectsVersion(new[] { "--tia-major-version", "--with-ui" }),
                "V21 CLI: a following flag is not a version value");
            check(RejectsVersion(new[] { "--tia-major-version", "20", "--tia-major-version", "21" }),
                "V21 CLI: a later supported value cannot hide an unsupported one");
        }

        private static bool RejectsVersion(string[] args)
        {
            try
            {
                CliOptions.ParseArgs(args);
                return false;
            }
            catch (ArgumentException ex)
            {
                return ex.Message.Contains("V21");
            }
        }

        private static void RunConfigTests(Action<bool, string> check)
        {
            var ownExe = McpConfigInstaller.OwnExePath();
            check(Path.IsPathRooted(ownExe) && File.Exists(ownExe),
                "V21 config: the current executable path is nonempty, absolute and readable");

            const string exe = @"C:\TIA 工具\TiaMcpServer.exe";
            foreach (var style in new[] { McpConfigInstaller.HostStyle.McpServers, McpConfigInstaller.HostStyle.VsCode })
            {
                var entry = McpConfigInstaller.BuildServerEntry(exe, style);
                check(entry["command"]?.GetValue<string>() == exe && entry["args"] is JsonArray args && args.Count == 0,
                    "V21 config: " + style + " launches the supplied executable without a version argument");
                check(!entry.ContainsKey("env"),
                    "V21 config: " + style + " keeps the default profile implicit");
                check(style != McpConfigInstaller.HostStyle.VsCode || entry["type"]?.GetValue<string>() == "stdio",
                    "V21 config: VS Code keeps its stdio transport declaration");

                var fullEntry = McpConfigInstaller.BuildServerEntry(exe, style, full: true);
                check(fullEntry["env"]?["TIA_MCP_PROFILE"]?.GetValue<string>() == "full",
                    "V21 config: " + style + " preserves the explicit full profile");

                var rootKey = style == McpConfigInstaller.HostStyle.VsCode ? "servers" : "mcpServers";
                var snippet = JsonNode.Parse(McpConfigInstaller.Snippet(exe, style, full: true));
                check(snippet?[rootKey]?[McpConfigInstaller.ServerKey]?["command"]?.GetValue<string>() == exe,
                    "V21 config: " + style + " snippet uses the expected host schema");
            }

            var codex = McpConfigInstaller.Snippet(exe, McpConfigInstaller.HostStyle.CodexToml);
            check(codex.Contains("args = []") && !codex.Contains("tia-major-version") && codex.Contains("startup_timeout_sec = 120"),
                "V21 config: Codex has no version argument and keeps the startup timeout");
            check(!codex.Contains("TIA_MCP_PROFILE"),
                "V21 config: Codex keeps the default profile implicit");
            var fullCodex = McpConfigInstaller.Snippet(exe, McpConfigInstaller.HostStyle.CodexToml, full: true);
            check(fullCodex.Contains("TIA_MCP_PROFILE = \"full\""),
                "V21 config: Codex preserves the explicit full profile");

            RunConfigWriteTests(check, exe);
        }

        private static void RunConfigWriteTests(Action<bool, string> check, string exe)
        {
            var directory = Path.Combine(Path.GetTempPath(), "tia_mcp_v21_cli_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var originalDirectory = Directory.GetCurrentDirectory();
            try
            {
                var jsonPath = Path.Combine(directory, "mcp.json");
                var original = "{\"keep\":true,\"mcpServers\":{\"other\":{\"command\":\"other.exe\"},\"tia-portal\":{\"command\":\"old.exe\",\"args\":[\"--tia-major-version\",\"21\"]}}}";
                File.WriteAllText(jsonPath, original);
                McpConfigInstaller.Apply(jsonPath, exe);
                var written = JsonNode.Parse(File.ReadAllText(jsonPath));
                check(written?["mcpServers"]?[McpConfigInstaller.ServerKey]?["args"] is JsonArray args && args.Count == 0,
                    "V21 config: updating a JSON config removes the obsolete version argument");
                check(written?["keep"]?.GetValue<bool>() == true &&
                      written?["mcpServers"]?["other"]?["command"]?.GetValue<string>() == "other.exe" &&
                      File.ReadAllText(jsonPath + ".bak") == original,
                    "V21 config: updating JSON preserves unrelated settings and backs up the original");

                var tomlPath = Path.Combine(directory, "config.toml");
                File.WriteAllText(tomlPath, "model = 'keep'\n[mcp_servers.tia-portal]\ncommand = 'old.exe'\nargs = ['--tia-major-version', '21']\n");
                McpConfigInstaller.Apply(tomlPath, exe, McpConfigInstaller.HostStyle.CodexToml, full: true);
                var toml = File.ReadAllText(tomlPath);
                check(toml.Contains("model = 'keep'") && toml.Contains("args = []") && !toml.Contains("tia-major-version") &&
                      toml.Contains("TIA_MCP_PROFILE = \"full\""),
                    "V21 config: updating TOML removes the version argument and preserves other settings");

                Directory.SetCurrentDirectory(directory);
                var jsonStatus = McpConfigInstaller.Apply("relative.json", exe);
                var relativeJson = JsonNode.Parse(File.ReadAllText(Path.Combine(directory, "relative.json")));
                check(relativeJson?["mcpServers"]?[McpConfigInstaller.ServerKey]?["command"]?.GetValue<string>() == exe &&
                      jsonStatus.Contains(Path.Combine(directory, "relative.json")),
                    "V21 config: a bare JSON filename resolves against the current directory");

                var tomlStatus = McpConfigInstaller.Apply("relative.toml", exe, McpConfigInstaller.HostStyle.CodexToml);
                var relativeToml = File.ReadAllText(Path.Combine(directory, "relative.toml"));
                check(relativeToml.Contains("args = []") && tomlStatus.Contains(Path.Combine(directory, "relative.toml")),
                    "V21 config: a bare TOML filename resolves against the current directory");
            }
            finally
            {
                Directory.SetCurrentDirectory(originalDirectory);
                foreach (var file in Directory.GetFiles(directory)) File.Delete(file);
                Directory.Delete(directory);
            }
        }
    }
}
