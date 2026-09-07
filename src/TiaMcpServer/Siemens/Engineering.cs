using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace TiaMcpServer.Siemens
{
    // Resolves the TIA Portal V21 PublicAPI assemblies used by this process.
    public class Engineering
    {
        private const string BaseAssemblyFileName = "Siemens.Engineering.Base.dll";

        public static int TiaMajorVersion => 21;

        // Explicit installation root from --tia-portal-location, e.g. D:\TIA21\Portal V21.
        public static string? TiaPortalLocationOverride { get; set; }

        // This setting lives here so Program can set it before loading Siemens types.
        public static bool LaunchWithUserInterface { get; set; } = false;

        public static Assembly? Resolver(object sender, ResolveEventArgs args)
        {
            var assemblyName = new AssemblyName(args.Name);
            var name = assemblyName.Name;
            if (name == null || (name != "Siemens.Engineering" && !name.StartsWith("Siemens.Engineering.", StringComparison.Ordinal)))
            {
                return null;
            }

            if (assemblyName.Version != null && assemblyName.Version.Major != TiaMajorVersion)
                throw new FileNotFoundException($"This server requires TIA Portal V21 assemblies. Requested '{args.Name}'.", name + ".dll");

            var probe = ProbeOpennessAssemblies();
            if (!probe.Ok)
                throw new FileNotFoundException(probe.Problem, BaseAssemblyFileName);

            var assemblyPath = FindV21AssemblyPath(probe.InstallPath!, name + ".dll");
            if (assemblyPath == null)
                throw new FileNotFoundException($"TIA Portal V21 assembly '{name}' is required under '{probe.InstallPath}\\PublicAPI\\V21'.", name + ".dll");

            return Assembly.LoadFrom(assemblyPath);
        }

        /// <summary>Returns 21 when the selected installation supplies the V21 Openness API.</summary>
        public static int? DetectTiaMajorVersion()
        {
            return ProbeOpennessAssemblies().Ok ? TiaMajorVersion : (int?)null;
        }

        /// <summary>Checks the same installation and API paths used by Resolver without loading assemblies.</summary>
        public static (bool Ok, string? InstallPath, string? ResolvedDll, string? Problem) ProbeOpennessAssemblies()
        {
            string? installPath = null;
            try
            {
                installPath = GetTiaPortalInstallPath();
                if (installPath == null)
                    return (false, null, null,
                        "TIA Portal V21 with Openness is required. Set --tia-portal-location or TiaPortalLocation to its installation root, " +
                        "or install it in the registered/default location.");

                var assemblyPath = FindV21AssemblyPath(installPath, BaseAssemblyFileName);
                return assemblyPath != null
                    ? (true, installPath, assemblyPath, null)
                    : (false, installPath, null,
                        $"TIA Portal V21 Openness requires '{BaseAssemblyFileName}' under '{installPath}\\PublicAPI\\V21\\net48' or '{installPath}\\PublicAPI\\V21'.");
            }
            catch (Exception ex)
            {
                return (false, installPath, null, "TIA Portal V21 installation lookup failed: " + ex.Message);
            }
        }

        private static string? GetTiaPortalInstallPath()
        {
            return SelectInstallPath(
                TiaPortalLocationOverride,
                Environment.GetEnvironmentVariable("TiaPortalLocation"),
                ReadRegistryInstallPaths(),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Siemens", "Automation", "Portal V21"));
        }

        internal static string? SelectInstallPath(string? overridePath, string? environmentPath,
            IEnumerable<string?> registryPaths, string defaultPath)
        {
            // An explicit path is authoritative; the probe reports any missing API at that path.
            if (!string.IsNullOrWhiteSpace(overridePath)) return Path.GetFullPath(overridePath!.Trim());

            var candidates = new List<string?> { environmentPath };
            candidates.AddRange(registryPaths);
            candidates.Add(defaultPath);
            foreach (var candidate in candidates)
            {
                if (string.IsNullOrWhiteSpace(candidate)) continue;
                try
                {
                    var path = Path.GetFullPath(candidate!.Trim());
                    if (FindV21AssemblyPath(path, BaseAssemblyFileName) != null) return path;
                }
                catch (ArgumentException) { }
                catch (NotSupportedException) { }
                catch (PathTooLongException) { }
            }
            return null;
        }

        internal static string? FindV21AssemblyPath(string installPath, string fileName)
        {
            var apiPath = Path.Combine(installPath, "PublicAPI", "V21");
            var frameworkPath = Path.Combine(apiPath, "net48", fileName);
            if (File.Exists(frameworkPath)) return frameworkPath;
            var directPath = Path.Combine(apiPath, fileName);
            return File.Exists(directPath) ? directPath : null;
        }

        private static List<string?> ReadRegistryInstallPaths()
        {
            var paths = new List<string?>();
#if NET5_0_OR_GREATER
            if (!OperatingSystem.IsWindows()) return paths;
#endif
            try
            {
                using var regBase = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
                using var apiKey = regBase.OpenSubKey(@"SOFTWARE\Siemens\Automation\Openness\21.0\PublicAPI\21.0.0.0\net48");
                if (apiKey?.GetValue("Siemens.Engineering.Base") is string dllPath && !string.IsNullOrWhiteSpace(dllPath))
                    paths.Add(new FileInfo(dllPath).Directory?.Parent?.Parent?.Parent?.FullName);

                using var installKey = regBase.OpenSubKey(@"SOFTWARE\Siemens\Automation\_InstalledSW\TIAP21\TIA_Opns");
                paths.Add(installKey?.GetValue("Path") as string);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException || ex is System.Security.SecurityException
                || ex is IOException || ex is ArgumentException || ex is NotSupportedException) { }
            return paths;
        }
    }
}
