[CmdletBinding()]
param(
    [ValidateSet("20", "21")]
    [string]$TiaVersion = "21",
    [string]$RepositoryRoot = "",
    [string]$OutputDirectory = ""
)
$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
}
else {
    $RepositoryRoot = (Resolve-Path $RepositoryRoot).Path
}
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $RepositoryRoot "release-assets"
}
else {
    $OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
}
$buildFolder = if ($TiaVersion -eq "20") { "bin-v20" } else { "bin" }
$buildOutput = Join-Path $RepositoryRoot "src/TiaMcpServer/$buildFolder/Release/net48"
$packageName = "TiaMcpServer-v$TiaVersion-win-x64"
$stageDirectory = Join-Path $OutputDirectory $packageName
$zipPath = Join-Path $OutputDirectory "$packageName.zip"
if (-not (Test-Path (Join-Path $buildOutput "TiaMcpServer.exe"))) {
    throw "未找到构建产物：$buildOutput/TiaMcpServer.exe。请先在安装了 TIA Portal V$TiaVersion Openness 的 Runner 上构建项目。"
}
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
if (Test-Path $stageDirectory) { Remove-Item -LiteralPath $stageDirectory -Recurse -Force }
if (Test-Path $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
New-Item -ItemType Directory -Path $stageDirectory -Force | Out-Null
Copy-Item -Path (Join-Path $buildOutput "*") -Destination $stageDirectory -Recurse -Force
Copy-Item -Path (Join-Path $RepositoryRoot "README.md") -Destination $stageDirectory -Force
Copy-Item -Path (Join-Path $RepositoryRoot "skill") -Destination $stageDirectory -Recurse -Force
Copy-Item -Path (Join-Path $RepositoryRoot "doc") -Destination $stageDirectory -Recurse -Force
Copy-Item -Path (Join-Path $RepositoryRoot "scripts/Configure-TiaOpenness.ps1") -Destination $stageDirectory -Force
@(
    "TiaMcpServer release package"
    "TIA Portal: V$TiaVersion"
    "Platform: win-x64"
    "Built: $(Get-Date -Format o)"
) | Set-Content -Path (Join-Path $stageDirectory "RELEASE-VERSION.txt") -Encoding UTF8
Compress-Archive -Path (Join-Path $stageDirectory "*") -DestinationPath $zipPath -CompressionLevel Optimal
Write-Host "已生成：$zipPath"
