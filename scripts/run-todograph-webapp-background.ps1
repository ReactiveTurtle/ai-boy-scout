$ErrorActionPreference = "Stop"

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$webAppDirectory = Join-Path $repositoryRoot "src\TodoGraph.WebApp"
$webAppCli = Join-Path $webAppDirectory "node_modules\@angular\cli\bin\ng.js"
$logDirectory = Join-Path $env:LOCALAPPDATA "todo-graph-v2\logs"
$logFile = Join-Path $logDirectory "webapp.log"

if (-not (Test-Path -LiteralPath $webAppCli)) {
    throw "Angular CLI entry point was not found: $webAppCli"
}

New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
$node = (Get-Command node.exe -ErrorAction Stop).Source
"[$(Get-Date -Format o)] Starting TodoGraph.WebApp" *>> $logFile
& $node $webAppCli serve --host 127.0.0.1 --port 4200 *>> $logFile
$exitCode = $LASTEXITCODE
"[$(Get-Date -Format o)] TodoGraph.WebApp stopped with exit code $exitCode" *>> $logFile
exit $exitCode
