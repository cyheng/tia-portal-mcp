using System;
using System.IO;
using System.Linq;
using TiaMcpServer.Siemens;

internal static class V21EngineeringTests
{
    private const string BaseAssembly = "Siemens.Engineering.Base.dll";

    public static void Run(Action<bool, string> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "tia_v21_engineering_" + Guid.NewGuid().ToString("N"));
        var previousOverride = Engineering.TiaPortalLocationOverride;
        Directory.CreateDirectory(root);
        try
        {
            var explicitInstall = CreateInstall(root, "explicit", "V21", "net48");
            var environmentInstall = CreateInstall(root, "environment", "V21", "net48");
            var registryInstall = CreateInstall(root, "registry", "V21", "net48");
            var defaultInstall = CreateInstall(root, "default", "V21", "net48");
            var otherInstall = CreateInstall(root, "other", "V20", "net48");
            var missingInstall = Path.Combine(root, "missing");

            check(Engineering.TiaMajorVersion == 21, "engineering: target version is V21");
            check(typeof(Engineering).GetProperty(nameof(Engineering.TiaMajorVersion))?.CanWrite == false,
                "engineering: target version is read-only");

            check(Engineering.SelectInstallPath(explicitInstall, environmentInstall, new[] { registryInstall }, defaultInstall) == explicitInstall,
                "engineering: explicit installation has priority");
            check(Engineering.SelectInstallPath(null, environmentInstall, new[] { registryInstall }, defaultInstall) == environmentInstall,
                "engineering: environment installation precedes registry and default");
            check(Engineering.SelectInstallPath(null, otherInstall, new[] { registryInstall }, defaultInstall) == registryInstall,
                "engineering: V21 registry installation follows an environment path without V21 API");
            check(Engineering.SelectInstallPath(null, missingInstall, new[] { otherInstall }, defaultInstall) == defaultInstall,
                "engineering: default V21 installation is selected when earlier candidates lack V21 API");
            check(Engineering.SelectInstallPath(null, "\0", Array.Empty<string?>(), defaultInstall) == defaultInstall,
                "engineering: malformed automatic candidate allows the valid default installation");
            check(Engineering.SelectInstallPath(null, otherInstall, new[] { missingInstall }, missingInstall) == null,
                "engineering: installation discovery requires a V21 API");
            check(Engineering.SelectInstallPath(missingInstall, environmentInstall, new[] { registryInstall }, defaultInstall) == missingInstall,
                "engineering: explicit missing installation remains the path to diagnose");

            var net48Assembly = Path.Combine(explicitInstall, "PublicAPI", "V21", "net48", BaseAssembly);
            WriteMarker(Path.Combine(explicitInstall, "PublicAPI", "V21", BaseAssembly));
            check(Engineering.FindV21AssemblyPath(explicitInstall, BaseAssembly) == net48Assembly,
                "engineering: net48 API is selected before the version directory");

            var directInstall = CreateInstall(root, "direct", "V21", "");
            check(Engineering.FindV21AssemblyPath(directInstall, BaseAssembly) == Path.Combine(directInstall, "PublicAPI", "V21", BaseAssembly),
                "engineering: direct V21 API layout is supported");
            var differentFramework = CreateInstall(root, "different-framework", "V21", "net8.0");
            WriteMarker(Path.Combine(otherInstall, "Bin", "PublicAPI", BaseAssembly));
            check(Engineering.FindV21AssemblyPath(differentFramework, BaseAssembly) == null,
                "engineering: net48 runtime selects a compatible API directory");
            check(Engineering.FindV21AssemblyPath(otherInstall, BaseAssembly) == null,
                "engineering: lookup stays within PublicAPI/V21");

            Engineering.TiaPortalLocationOverride = explicitInstall;
            var available = Engineering.ProbeOpennessAssemblies();
            check(available.Ok && available.InstallPath == explicitInstall && available.ResolvedDll == net48Assembly && available.Problem == null,
                "engineering: probe reports the exact V21 assembly path");
            check(Engineering.DetectTiaMajorVersion() == 21,
                "engineering: detection agrees with the available V21 API");
            check(Engineering.Resolver(new object(), new ResolveEventArgs("Unrelated.Library, Version=1.0.0.0")) == null,
                "engineering: unrelated assemblies retain their own resolver");
            check(ThrowsFileNotFound(() => Engineering.Resolver(new object(), new ResolveEventArgs("Siemens.Engineering.Base, Version=20.0.0.0"))),
                "engineering: assembly requests require the V21 identity");
            check(ThrowsFileNotFound(() => Engineering.Resolver(new object(), new ResolveEventArgs("Siemens.Engineering.Missing, Version=21.0.0.0"))),
                "engineering: absent V21 dependency reports a load error");

            Engineering.TiaPortalLocationOverride = otherInstall;
            var unavailable = Engineering.ProbeOpennessAssemblies();
            check(!unavailable.Ok && unavailable.InstallPath == otherInstall && unavailable.ResolvedDll == null
                    && unavailable.Problem != null && unavailable.Problem.Contains("V21"),
                "engineering: explicit directory without V21 reports its required API");
            check(Engineering.DetectTiaMajorVersion() == null,
                "engineering: detection agrees with an unavailable V21 API");
            check(ThrowsFileNotFound(() => Engineering.Resolver(new object(), new ResolveEventArgs("Siemens.Engineering.Base, Version=21.0.0.0"))),
                "engineering: missing V21 installation stops assembly loading");

            Engineering.TiaPortalLocationOverride = "\0";
            var malformed = Engineering.ProbeOpennessAssemblies();
            check(!malformed.Ok && malformed.Problem != null,
                "engineering: malformed explicit path produces a diagnostic");

            var capabilities = Capability.Snapshot();
            check(capabilities.Count == 2 && capabilities.All(c => c.Supported && c.MinVersion == 21 && c.Note.Contains("V21")),
                "engineering: Bootstrap advertises V21 capability requirements");
            check(capabilities.Any(c => c.Feature == nameof(TiaFeature.HardwareHmiConnection))
                    && capabilities.Any(c => c.Feature == nameof(TiaFeature.DocumentExport)),
                "engineering: Bootstrap includes hardware HMI connections and document export");
        }
        finally
        {
            Engineering.TiaPortalLocationOverride = previousOverride;
            var expectedPrefix = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var fullRoot = Path.GetFullPath(root);
            if (fullRoot.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase)
                && Path.GetFileName(fullRoot).StartsWith("tia_v21_engineering_", StringComparison.Ordinal))
                Directory.Delete(fullRoot, true);
        }
    }

    private static string CreateInstall(string root, string name, string version, string framework)
    {
        var install = Path.Combine(root, name);
        WriteMarker(Path.Combine(install, "PublicAPI", version, framework, BaseAssembly));
        return install;
    }

    private static void WriteMarker(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "API path fixture");
    }

    private static bool ThrowsFileNotFound(Action action)
    {
        try { action(); return false; }
        catch (FileNotFoundException) { return true; }
    }
}
