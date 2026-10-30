$ErrorActionPreference = "Stop"

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$apiDll = Join-Path $repositoryRoot "artifacts\todo-graph-api\TodoGraph.Api.dll"
$logDirectory = Join-Path $env:LOCALAPPDATA "todo-graph-v2\logs"
$logFile = Join-Path $logDirectory "api.log"

if (-not (Test-Path -LiteralPath $apiDll)) {
    throw "Published TodoGraph.Api.dll was not found: $apiDll"
}

New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
$dotnet = (Get-Command dotnet.exe -ErrorAction Stop).Source
"[$(Get-Date -Format o)] Starting TodoGraph.Api" *>> $logFile
& $dotnet $apiDll --urls "http://127.0.0.1:5207" *>> $logFile
$exitCode = $LASTEXITCODE
"[$(Get-Date -Format o)] TodoGraph.Api stopped with exit code $exitCode" *>> $logFile
exit $exitCode
