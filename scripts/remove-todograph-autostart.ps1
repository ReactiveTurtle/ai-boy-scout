$ErrorActionPreference = "Stop"

foreach ($taskName in @("TodoGraph.Api", "TodoGraph.WebApp")) {
    $task = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
    if ($null -eq $task) { continue }

    if ($task.State -eq "Running") {
        Stop-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
    }
    Unregister-ScheduledTask -TaskName $taskName -Confirm:$false
}

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$apiDebugApp = Join-Path $repositoryRoot "src\TodoGraph.Api\bin\Debug\net10.0\TodoGraph.Api.exe"
$apiPublishedDll = Join-Path $repositoryRoot "artifacts\todo-graph-api\TodoGraph.Api.dll"
$apiProject = Join-Path $repositoryRoot "src\TodoGraph.Api\TodoGraph.Api.csproj"
$webAppRoot = Join-Path $repositoryRoot "src\TodoGraph.WebApp\node_modules"
$targetProcessIds = [System.Collections.Generic.List[int]]::new()

foreach ($process in Get-CimInstance Win32_Process) {
    $commandLine = $process.CommandLine
    if ([string]::IsNullOrWhiteSpace($commandLine)) { continue }

    $isTodoGraphApi =
        ($process.Name -ieq "TodoGraph.Api.exe" -and $commandLine.IndexOf($apiDebugApp, [StringComparison]::OrdinalIgnoreCase) -ge 0) -or
        ($process.Name -ieq "dotnet.exe" -and (
            $commandLine.IndexOf($apiPublishedDll, [StringComparison]::OrdinalIgnoreCase) -ge 0 -or
            $commandLine.IndexOf($apiProject, [StringComparison]::OrdinalIgnoreCase) -ge 0))
    $isTodoGraphWebApp = $process.Name -ieq "node.exe" -and
        $commandLine.IndexOf($webAppRoot, [StringComparison]::OrdinalIgnoreCase) -ge 0 -and
        $commandLine.IndexOf("ng.js", [StringComparison]::OrdinalIgnoreCase) -ge 0

    if ($isTodoGraphApi -or $isTodoGraphWebApp) {
        $targetProcessIds.Add([int]$process.ProcessId)
    }
}

if ($targetProcessIds.Count -gt 0) {
    Stop-Process -Id $targetProcessIds.ToArray() -Force -ErrorAction SilentlyContinue
    foreach ($processId in $targetProcessIds) {
        Wait-Process -Id $processId -Timeout 15 -ErrorAction SilentlyContinue
    }

    $remainingProcesses = Get-CimInstance Win32_Process | Where-Object { $targetProcessIds.Contains([int]$_.ProcessId) }
    if ($remainingProcesses) {
        $remainingIds = ($remainingProcesses | ForEach-Object { $_.ProcessId }) -join ", "
        throw "Could not stop the TodoGraph process(es): $remainingIds"
    }
}

"Removed the TodoGraph background startup tasks."
