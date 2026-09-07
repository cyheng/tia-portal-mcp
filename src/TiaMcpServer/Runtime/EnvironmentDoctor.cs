using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;

namespace TiaMcpServer.Runtime
{
    /// <summary>
    /// Shared CLI and MCP checks for TIA Portal V21, its Openness API, .NET Framework 4.8,
    /// and Windows file blocking. Each check supplies English and Chinese diagnostics.
    /// </summary>
    public static class EnvironmentDoctor
    {
        public sealed class Check
        {
            public string Id = "";
            public bool Ok;
            /// <summary>Informational checks never gate readiness.</summary>
            public bool Gating = true;
            public string NameEn = "", NameZh = "";
            public string DetailEn = "", DetailZh = "";
            public string? FixEn, FixZh;

            public string Name(bool zh) => zh ? NameZh : NameEn;
            public string Detail(bool zh) => zh ? DetailZh : DetailEn;
            public string? Fix(bool zh) => zh ? FixZh : FixEn;
        }

        public static bool PreferChinese =>
            CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("zh", StringComparison.OrdinalIgnoreCase);

        public static List<Check> Run()
        {
            var probe = Siemens.Engineering.ProbeOpennessAssemblies();
            return new List<Check>
            {
                TiaInstall(probe.InstallPath),
                OpennessAssemblies(probe),
                DotNetFramework48(),
                FilesNotBlocked(),
            };
        }

        private static Check TiaInstall(string? installPath)
        {
            bool ok = installPath != null && Directory.Exists(Path.Combine(installPath, "PublicAPI", "V21"));
            return new Check
            {
                Id = "tia-install",
                Ok = ok,
                NameEn = "TIA Portal V21 installation",
                NameZh = "TIA Portal V21 安装",
                DetailEn = ok ? "V21 installation: " + installPath : "TIA Portal V21 installation is required.",
                DetailZh = ok ? "V21 安装目录：" + installPath : "需要安装 TIA Portal V21。",
                FixEn = ok ? null : "Install TIA Portal V21 with Openness, or set --tia-portal-location / TiaPortalLocation to its installation root (e.g. D:\\TIA21\\Portal V21).",
                FixZh = ok ? null : "安装 TIA Portal V21 并勾选 Openness 组件，或用 --tia-portal-location / TiaPortalLocation 指定安装根目录（例如 D:\\TIA21\\Portal V21）。",
            };
        }

        private static Check OpennessAssemblies((bool Ok, string? InstallPath, string? ResolvedDll, string? Problem) probe)
        {
            return new Check
            {
                Id = "openness-dll",
                Ok = probe.Ok,
                NameEn = "V21 Openness API assemblies",
                NameZh = "V21 Openness 编程接口 DLL",
                DetailEn = probe.Ok ? "resolvable: " + probe.ResolvedDll : (probe.Problem ?? "not resolvable"),
                DetailZh = probe.Ok ? "可解析：" + probe.ResolvedDll : ("无法解析——" + (probe.Problem ?? "原因未知")),
                FixEn = probe.Ok ? null : "Run the TIA Portal V21 setup and install the Openness component. Confirm Siemens.Engineering.Base.dll exists under <install>\\PublicAPI\\V21\\net48.",
                FixZh = probe.Ok ? null : "运行 TIA Portal V21 安装程序并安装 Openness 组件，确认 <安装目录>\\PublicAPI\\V21\\net48 下存在 Siemens.Engineering.Base.dll。",
            };
        }

        private static Check DotNetFramework48()
        {
            int release = 0;
            try
            {
                using var key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                    .OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full");
                release = (int)(key?.GetValue("Release") ?? 0);
            }
            catch { }

            // 528040 = .NET Framework 4.8 RTM; anything at or above it satisfies net48.
            bool ok = release >= 528040;
            return new Check
            {
                Id = "dotnet48",
                Ok = ok,
                NameEn = ".NET Framework 4.8",
                NameZh = ".NET Framework 4.8",
                DetailEn = ok ? $"present (release {release})" : (release > 0 ? $"too old (release {release}, need >= 528040)" : "not detected"),
                DetailZh = ok ? $"已安装（release {release}）" : (release > 0 ? $"版本过低（release {release}，需要 >= 528040）" : "未检测到"),
                FixEn = ok ? null : "Install the .NET Framework 4.8 runtime (Windows 10 1903+ and Windows 11 ship it built in).",
                FixZh = ok ? null : "安装 .NET Framework 4.8 运行时（Windows 10 1903 及以上、Windows 11 自带）。",
            };
        }

        /// <summary>
        /// Windows tags every file extracted from a downloaded .zip with a Zone.Identifier stream;
        /// .NET then refuses to load the assemblies and the engine fails in ways that look nothing
        /// like "your download is blocked".
        /// </summary>
        private static Check FilesNotBlocked()
        {
            var blocked = new List<string>();
            string dir = "";
            try
            {
                dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";
                if (!string.IsNullOrEmpty(dir))
                {
                    foreach (var f in Directory.GetFiles(dir, "*.dll").Concat(Directory.GetFiles(dir, "*.exe")))
                    {
                        if (HasZoneIdentifier(f)) blocked.Add(Path.GetFileName(f));
                        if (blocked.Count >= 5) break;
                    }
                }
            }
            catch { }

            bool ok = blocked.Count == 0;
            string list = string.Join(", ", blocked);
            return new Check
            {
                Id = "motw",
                Ok = ok,
                NameEn = "Files not blocked by Windows (MOTW)",
                NameZh = "文件未被 Windows 标记为网络来源 (MOTW)",
                DetailEn = ok ? "no zone identifier on the engine files" : $"blocked files present: {list}{(blocked.Count >= 5 ? ", ..." : "")}",
                DetailZh = ok ? "引擎目录下的文件没有网络来源标记" : $"存在被阻止的文件：{list}{(blocked.Count >= 5 ? " …" : "")}",
                FixEn = ok ? null : $"Unblock the delivery folder in PowerShell:  Get-ChildItem -Recurse '{dir}' | Unblock-File",
                FixZh = ok ? null : $"用 PowerShell 解除阻止：  Get-ChildItem -Recurse '{dir}' | Unblock-File",
            };
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode,
            SetLastError = true)]
        private static extern IntPtr CreateFileW(string lpFileName, uint dwDesiredAccess, uint dwShareMode,
            IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        /// <summary>
        /// Alternate data streams have to be opened through the Win32 API: File.Exists() on an
        /// "file.dll:Zone.Identifier" path returns false even when the stream is right there, so a
        /// check written with it silently never fires.
        /// </summary>
        private static bool HasZoneIdentifier(string path)
        {
            const uint GENERIC_READ = 0x80000000;
            const uint FILE_SHARE_READWRITE = 0x00000003;
            const uint OPEN_EXISTING = 3;
            var invalid = new IntPtr(-1);
            IntPtr h = invalid;
            try
            {
                h = CreateFileW(path + ":Zone.Identifier", GENERIC_READ, FILE_SHARE_READWRITE,
                                IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
                return h != invalid;
            }
            catch { return false; }
            finally { if (h != invalid && h != IntPtr.Zero) CloseHandle(h); }
        }
    }
}
