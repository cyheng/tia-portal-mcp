[CmdletBinding()]
param(
    [string]$GroupName = "Siemens TIA Openness",
    [string]$UserName = "",
    [switch]$CheckOnly
)
$ErrorActionPreference = "Stop"
function Write-Result {
    param([bool]$Ok, [string]$Message)
    $prefix = if ($Ok) { "[OK]" } else { "[FAIL]" }
    $color = if ($Ok) { "Green" } else { "Red" }
    Write-Host "$prefix $Message" -ForegroundColor $color
}
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $CheckOnly -and -not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Result $false "请使用管理员 PowerShell 重新执行此脚本。"
    exit 2
}
if ([string]::IsNullOrWhiteSpace($UserName)) {
    $UserName = "$env:USERDOMAIN\$env:USERNAME"
}
try {
    Import-Module Microsoft.PowerShell.LocalAccounts -ErrorAction Stop
    $group = Get-LocalGroup -Name $GroupName -ErrorAction Stop
}
catch {
    Write-Result $false "找不到本地用户组 '$GroupName'。请确认 TIA Portal 已安装 Openness 组件。"
    Write-Host $_.Exception.Message -ForegroundColor DarkGray
    exit 1
}
try {
    $targetSid = ([Security.Principal.NTAccount]$UserName).Translate([Security.Principal.SecurityIdentifier]).Value
}
catch {
    Write-Result $false "无法解析 Windows 用户 '$UserName'。可以使用 -UserName 'DOMAIN\User' 指定用户。"
    exit 1
}
$members = @(Get-LocalGroupMember -Group $group.Name -ErrorAction Stop)
$isMember = $members | Where-Object { $_.SID -and $_.SID.Value -eq $targetSid }
if ($isMember) {
    Write-Result $true "用户 $UserName 已属于 '$GroupName'。"
    Write-Host "可以继续运行 TiaMcpServer.exe doctor。"
    exit 0
}
if ($CheckOnly) {
    Write-Result $false "用户 $UserName 尚未加入 '$GroupName'。"
    exit 1
}
try {
    Add-LocalGroupMember -Group $group.Name -Member $UserName -ErrorAction Stop
    Write-Result $true "已将用户 $UserName 加入 '$GroupName'。"
    Write-Host "请注销并重新登录 Windows，或重启后再启动 MCP 服务。"
    exit 0
}
catch {
    Write-Result $false "加入用户组失败：$($_.Exception.Message)"
    exit 1
}
