#requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ServerPath,
    [Parameter(Mandatory = $true)][string]$TiaPortalLocation
)

# 使用真实 net48 构建验证依赖加载；不初始化 Openness、不枚举进程、不附加或修改工程。
$ErrorActionPreference = 'Stop'
$serverFile = (Resolve-Path -LiteralPath $ServerPath).Path
$installRoot = (Resolve-Path -LiteralPath $TiaPortalLocation).Path
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw '需要 Windows x64 .NET Framework C# 编译器。' }
$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$probeRoot = Join-Path $tempRoot ('tia_resolver_test_' + [Guid]::NewGuid().ToString('N'))
$source = @'
using System;
using System.IO;
using System.Reflection;

internal static class OpennessAssemblyResolutionTest
{
    private static int Main(string[] args)
    {
        int failed = 0;
        string serverDirectory = Path.GetDirectoryName(args[0]);
        AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs request)
        {
            string path = Path.Combine(serverDirectory, new AssemblyName(request.Name).Name + ".dll");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };
        try
        {
            Assembly server = Assembly.LoadFrom(args[0]);
            Type engineering = server.GetType("TiaMcpServer.Siemens.Engineering", true);
            engineering.GetProperty("TiaPortalLocationOverride").SetValue(null, args[1], null);
            ResolveEventHandler resolver = (ResolveEventHandler)Delegate.CreateDelegate(
                typeof(ResolveEventHandler), engineering.GetMethod("Resolver"));
            AppDomain.CurrentDomain.AssemblyResolve += resolver;
            string installPrefix = Path.GetFullPath(args[1]).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            foreach (string suffix in new[] { "Base", "Contract", "ClientAdapter.Interfaces", "ClientAdapter.MarshallerHook", "ClientAdapter.MarshallerHook.Hmi" })
            {
                string name = "Siemens.Engineering." + suffix;
                try
                {
                    Assembly loaded = resolver(null, new ResolveEventArgs(name + ", Version=21.0.0.0, Culture=neutral, PublicKeyToken=d29ec89bac048f84"));
                    if (loaded == null || loaded.GetName().Name != name || loaded.GetName().Version.Major != 21
                        || !Path.GetFullPath(loaded.Location).StartsWith(installPrefix, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Loaded assembly identity or installation path is incorrect.");
                    Console.WriteLine("PASS " + name + " => " + loaded.Location);
                }
                catch (Exception error)
                {
                    failed++;
                    Console.WriteLine("FAIL " + name + ": " + error);
                }
            }
        }
        catch (Exception error)
        {
            failed++;
            Console.WriteLine("FAIL " + error);
        }
        return failed == 0 ? 0 : 1;
    }
}
'@

try {
    New-Item -ItemType Directory -Path $probeRoot | Out-Null
    $sourcePath = Join-Path $probeRoot 'OpennessAssemblyResolutionTest.cs'
    $probeExe = Join-Path $probeRoot 'OpennessAssemblyResolutionTest.exe'
    [IO.File]::WriteAllText($sourcePath, $source, [Text.UTF8Encoding]::new($false))
    & $compiler /nologo /platform:x64 /target:exe "/out:$probeExe" $sourcePath
    if ($LASTEXITCODE -ne 0) { throw '离线集成探针编译失败。' }
    & $probeExe $serverFile $installRoot
    if ($LASTEXITCODE -ne 0) { throw 'Openness 运行时依赖加载检查失败。' }
} finally {
    # 仅删除本次生成的临时目录，先核对绝对路径边界。
    $fullProbeRoot = [IO.Path]::GetFullPath($probeRoot)
    $tempPrefix = $tempRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if ($fullProbeRoot.StartsWith($tempPrefix, [StringComparison]::OrdinalIgnoreCase) -and
        [IO.Path]::GetFileName($fullProbeRoot).StartsWith('tia_resolver_test_', [StringComparison]::Ordinal) -and
        (Test-Path -LiteralPath $fullProbeRoot)) {
        Remove-Item -LiteralPath $fullProbeRoot -Recurse -Force
    }
}
