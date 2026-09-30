[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ExecutablePath,
    [ValidateSet("compact", "lite", "full")]
    [string]$Profile = "",
    [string]$OutputPath = "",
    [switch]$Smoke
)
$ErrorActionPreference = "Stop"

# Measures actual MCP stdio messages. Character counts are not tokenizer counts;
# some hosts repeat server instructions on every tool and others send them once.
# Smoke reads metadata and guides only; it never connects to or writes a TIA project.

function Invoke-McpRequest {
    param(
        [Diagnostics.Process]$EngineProcess,
        [string]$Method,
        [hashtable]$Parameters
    )
    $script:mcpRequestId++
    $requestId = $script:mcpRequestId
    $request = [ordered]@{
        jsonrpc = "2.0"
        id = $requestId
        method = $Method
        params = $Parameters
    }
    $EngineProcess.StandardInput.WriteLine((ConvertTo-Json -InputObject $request -Depth 100 -Compress))
    $EngineProcess.StandardInput.Flush()
    $deadline = [DateTime]::UtcNow.AddSeconds(25)
    while ([DateTime]::UtcNow -lt $deadline) {
        $remainingMs = [Math]::Max(1, [int]($deadline - [DateTime]::UtcNow).TotalMilliseconds)
        $readTask = $EngineProcess.StandardOutput.ReadLineAsync()
        if (-not $readTask.Wait($remainingMs)) {
            throw "MCP request '$Method' timed out after 25 seconds."
        }
        $line = $readTask.GetAwaiter().GetResult()
        if ($null -eq $line) {
            throw "The MCP process closed stdout while waiting for '$Method'."
        }
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        try { $response = ConvertFrom-Json -InputObject $line }
        catch { throw "The MCP process returned invalid JSON while waiting for '$Method'." }
        if ($response.id -ne $requestId) { continue }
        if ($null -ne $response.error) {
            throw "MCP request '$Method' failed ($($response.error.code)): $($response.error.message)"
        }
        if ($null -eq $response.result) {
            throw "MCP request '$Method' returned no result."
        }
        $script:mcpLastResponseJson = $line
        return $response.result
    }
    throw "MCP request '$Method' timed out after 25 seconds."
}

function Get-McpToolPayload {
    param(
        [Diagnostics.Process]$EngineProcess,
        [string]$Name,
        [hashtable]$Arguments = @{}
    )
    $result = Invoke-McpRequest -EngineProcess $EngineProcess -Method "tools/call" -Parameters @{
        name = $Name
        arguments = $Arguments
    }
    if ($result.isError -eq $true) {
        $message = @($result.content | Where-Object { $_.type -eq "text" } | ForEach-Object { $_.text }) -join " "
        throw "Tool '$Name' failed: $message"
    }
    if ($null -ne $result.structuredContent) { return $result.structuredContent }
    $textBlocks = @($result.content | Where-Object { $_.type -eq "text" })
    if ($textBlocks.Count -ne 1) { throw "Tool '$Name' did not return one JSON text block or structured content." }
    try { return (ConvertFrom-Json -InputObject $textBlocks[0].text) }
    catch { throw "Tool '$Name' returned text that is not structured JSON." }
}

function Assert-ToolSuccess {
    param($Payload, [string]$CheckName)
    if ($null -eq $Payload -or $Payload.meta.success -ne $true) {
        throw "Smoke check '$CheckName' failed: $($Payload.message)"
    }
}

$executable = (Resolve-Path -LiteralPath $ExecutablePath).Path
if (-not [IO.File]::Exists($executable) -or [IO.Path]::GetExtension($executable) -ne ".exe") {
    throw "ExecutablePath must name an existing TiaMcpServer.exe file."
}
$artifactPath = if ([string]::IsNullOrWhiteSpace($OutputPath)) { $null } else { [IO.Path]::GetFullPath($OutputPath) }
if ($null -ne $artifactPath -and [string]::Equals($artifactPath, $executable, [StringComparison]::OrdinalIgnoreCase)) {
    throw "OutputPath cannot overwrite the executable."
}

$startInfo = [Diagnostics.ProcessStartInfo]::new()
$startInfo.FileName = $executable
$startInfo.WorkingDirectory = [IO.Path]::GetDirectoryName($executable)
$startInfo.Arguments = "--transport stdio"
if (-not [string]::IsNullOrWhiteSpace($Profile)) { $startInfo.Arguments += " --profile $Profile" }
# Make an omitted profile measure the application default, independent of the
# caller's profile environment setting. Only the owned child environment changes.
$startInfo.EnvironmentVariables.Remove("TIA_MCP_PROFILE")
$startInfo.UseShellExecute = $false
$startInfo.CreateNoWindow = $true
$startInfo.RedirectStandardInput = $true
$startInfo.RedirectStandardOutput = $true
$startInfo.RedirectStandardError = $true
$startInfo.StandardOutputEncoding = [Text.UTF8Encoding]::new($false)
$startInfo.StandardErrorEncoding = [Text.UTF8Encoding]::new($false)
$engineProcess = [Diagnostics.Process]::new()
$engineProcess.StartInfo = $startInfo
$engineStarted = $false
$stderrTask = $null
$script:mcpRequestId = 0
$script:mcpLastResponseJson = ""

try {
    $engineStarted = $engineProcess.Start()
    if (-not $engineStarted) { throw "Could not start the MCP executable." }
    # Drain stderr concurrently so diagnostics cannot fill the pipe and stall RPC.
    $stderrTask = $engineProcess.StandardError.ReadToEndAsync()
    $initialized = Invoke-McpRequest -EngineProcess $engineProcess -Method "initialize" -Parameters @{
        protocolVersion = "2025-03-26"
        capabilities = @{}
        clientInfo = @{ name = "tia-mcp-context-measurement"; version = "1.0" }
    }
    $engineProcess.StandardInput.WriteLine('{"jsonrpc":"2.0","method":"notifications/initialized"}')
    $engineProcess.StandardInput.Flush()
    $toolList = Invoke-McpRequest -EngineProcess $engineProcess -Method "tools/list" -Parameters @{}
    if ($null -ne $toolList.nextCursor) { throw "tools/list is paginated; this measurement requires the complete roster." }
    $tools = @($toolList.tools)
    if ($tools.Count -eq 0 -or $null -eq $tools[0]) { throw "tools/list returned no tools." }
    $instructions = [string]$initialized.instructions
    # Count the original wire JSON, not PowerShell's differently escaped reserialization.
    $toolsJson = $script:mcpLastResponseJson
    $report = [ordered]@{
        profile = if ([string]::IsNullOrWhiteSpace($Profile)) { "default" } else { $Profile }
        protocolVersion = $initialized.protocolVersion
        toolCount = $tools.Count
        instructionChars = $instructions.Length
        instructionUtf8Bytes = [Text.Encoding]::UTF8.GetByteCount($instructions)
        toolsListChars = $toolsJson.Length
        toolsListUtf8Bytes = [Text.Encoding]::UTF8.GetByteCount($toolsJson)
        measurementNote = "toolsList counts measure the actual JSON-RPC response line, excluding its newline."
        estimatedInstructionRepeatedChars = [long]$instructions.Length * $tools.Count + $toolsJson.Length
        estimateNote = "Host-dependent character estimate: tools/list chars + instruction chars * tool count. Not a tokenizer count; hosts may send instructions once or repeat them."
    }

    if ($Smoke) {
        $checks = [Collections.Generic.List[string]]::new()
        $found = Get-McpToolPayload -EngineProcess $engineProcess -Name "FindTools" -Arguments @{ query = "GetAuthoringGuide"; limit = 1 }
        Assert-ToolSuccess -Payload $found -CheckName "FindTools"
        if (@($found.items | Where-Object { $_ -match '^GetAuthoringGuide\(' }).Count -eq 0) {
            throw "FindTools did not return the exact GetAuthoringGuide signature."
        }
        $checks.Add("FindTools exact-name search")

        $schema = Get-McpToolPayload -EngineProcess $engineProcess -Name "GetToolSchema" -Arguments @{ name = "GetAuthoringGuide" }
        Assert-ToolSuccess -Payload $schema -CheckName "GetToolSchema"
        if ($schema.name -ne "GetAuthoringGuide" -or @($schema.inputSchema.required) -notcontains "topic" -or
            [string]::IsNullOrWhiteSpace([string]$schema.inputSchema.properties.topic.description)) {
            throw "GetToolSchema omitted the required topic parameter or its description."
        }
        $checks.Add("GetToolSchema required topic and description")

        $guide = Get-McpToolPayload -EngineProcess $engineProcess -Name "CallTool" -Arguments @{
            name = "GetAuthoringGuide"; argumentsJson = '{"topic":"workflow"}'
        }
        Assert-ToolSuccess -Payload $guide -CheckName "CallTool guide"
        Assert-ToolSuccess -Payload $guide.result -CheckName "Inner guide"
        if ($guide.result.meta.topic -ne "workflow" -or [string]::IsNullOrWhiteSpace([string]$guide.result.message)) {
            throw "CallTool did not preserve the workflow guide result."
        }
        $checks.Add("CallTool synchronous workflow guide")

        $bootstrap = Get-McpToolPayload -EngineProcess $engineProcess -Name "CallTool" -Arguments @{ name = "Bootstrap" }
        Assert-ToolSuccess -Payload $bootstrap -CheckName "CallTool Bootstrap"
        Assert-ToolSuccess -Payload $bootstrap.result -CheckName "Inner Bootstrap"
        if ($null -eq $bootstrap.result.environment -or $null -eq $bootstrap.result.portal -or
            [string]::IsNullOrWhiteSpace([string]$bootstrap.result.recommendedNextTool)) {
            throw "CallTool did not await and preserve the Bootstrap result."
        }
        $checks.Add("CallTool asynchronous read-only Bootstrap")

        $failure = Get-McpToolPayload -EngineProcess $engineProcess -Name "CallTool" -Arguments @{
            name = "GetAuthoringGuide"; argumentsJson = '{"topic":"__invalid_measurement_topic__"}'
        }
        if ($failure.meta.success -ne $false -or $failure.result.meta.success -ne $false -or
            [string]::IsNullOrWhiteSpace([string]$failure.result.message)) {
            throw "CallTool did not preserve the inner tool's failure."
        }
        $checks.Add("CallTool preserves inner failure")

        $badArgument = Get-McpToolPayload -EngineProcess $engineProcess -Name "CallTool" -Arguments @{
            name = "GetAuthoringGuide"; argumentsJson = '{"topic":"workflow","unknown":true}'
        }
        if ($badArgument.meta.success -ne $false -or $null -ne $badArgument.result -or
            $badArgument.message -notmatch 'unknown') {
            throw "CallTool did not reject the unknown argument before invocation."
        }
        $checks.Add("CallTool rejects unknown arguments")
        $report.smoke = [ordered]@{ passed = $true; checks = $checks.ToArray() }
    }

    $reportJson = ConvertTo-Json -InputObject $report -Depth 100
    if ($null -ne $artifactPath) {
        $artifactDirectory = [IO.Path]::GetDirectoryName($artifactPath)
        if (-not [string]::IsNullOrEmpty($artifactDirectory)) {
            [IO.Directory]::CreateDirectory($artifactDirectory) | Out-Null
        }
        [IO.File]::WriteAllText($artifactPath, $reportJson + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
    }
    Write-Output $reportJson
}
finally {
    # This Process object owns exactly the child launched above. Never kill by name.
    if ($engineStarted) {
        if (-not $engineProcess.HasExited) {
            $engineProcess.StandardInput.Close()
            if (-not $engineProcess.WaitForExit(1500)) {
                $engineProcess.Kill()
                $engineProcess.WaitForExit(1500) | Out-Null
            }
        }
        if ($null -ne $stderrTask -and $stderrTask.Wait(1500)) {
            $diagnostics = $stderrTask.GetAwaiter().GetResult()
            if (-not [string]::IsNullOrWhiteSpace($diagnostics)) {
                $tail = $diagnostics.Substring([Math]::Max(0, $diagnostics.Length - 600))
                Write-Verbose "MCP stderr tail: $tail"
            }
        }
    }
    $engineProcess.Dispose()
}
