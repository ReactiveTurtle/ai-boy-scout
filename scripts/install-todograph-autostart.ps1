$ErrorActionPreference = "Stop"

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$removeScript = Join-Path $PSScriptRoot "remove-todograph-autostart.ps1"
$apiRunner = Join-Path $PSScriptRoot "run-todograph-api-background.ps1"
$webAppRunner = Join-Path $PSScriptRoot "run-todograph-webapp-background.ps1"
$apiProject = Join-Path $repositoryRoot "src\TodoGraph.Api\TodoGraph.Api.csproj"
$apiPublishDirectory = Join-Path $repositoryRoot "artifacts\todo-graph-api"
$webAppDependencies = Join-Path $repositoryRoot "src\TodoGraph.WebApp\node_modules"
$webAppDirectory = Join-Path $repositoryRoot "src\TodoGraph.WebApp"
$webAppCli = Join-Path $webAppDirectory "node_modules\@angular\cli\bin\ng.js"

foreach ($path in @($removeScript, $apiRunner, $webAppRunner, $apiProject, $webAppDependencies, $webAppCli)) {
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Required TodoGraph background-runner path was not found: $path"
    }
}

$powerShell = Join-Path $PSHOME "powershell.exe"
& $removeScript

$apiPort = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 5207)
try { $apiPort.Start(); $apiPort.Stop() } catch { throw "Port 5207 is occupied by another process; it was left untouched." }
$webPort = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 4200)
try { $webPort.Start(); $webPort.Stop() } catch { throw "Port 4200 is occupied by another process; it was left untouched." }

$dotnet = (Get-Command dotnet.exe -ErrorAction Stop).Source
$node = (Get-Command node.exe -ErrorAction Stop).Source
& $dotnet publish $apiProject --configuration Release --output $apiPublishDirectory
if ($LASTEXITCODE -ne 0) { throw "TodoGraph.Api publish failed." }
$apiDll = Join-Path $apiPublishDirectory "TodoGraph.Api.dll"
if (-not (Test-Path -LiteralPath $apiDll)) { throw "Published TodoGraph.Api.dll was not found." }

$currentUser = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
$trigger = New-ScheduledTaskTrigger -AtLogOn -User $currentUser
$principal = New-ScheduledTaskPrincipal -UserId $currentUser -LogonType Interactive -RunLevel Limited
$settings = New-ScheduledTaskSettingsSet `
    -StartWhenAvailable `
    -Hidden `
    -AllowStartIfOnBatteries `
    -DontStopIfGoingOnBatteries `
    -MultipleInstances IgnoreNew `
    -RestartCount 3 `
    -RestartInterval (New-TimeSpan -Minutes 1) `
    -ExecutionTimeLimit ([TimeSpan]::Zero)

$apiAction = New-ScheduledTaskAction `
    -Execute $powerShell `
    -Argument "-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$apiRunner`"" `
    -WorkingDirectory $repositoryRoot
$webAppAction = New-ScheduledTaskAction `
    -Execute $powerShell `
    -Argument "-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$webAppRunner`"" `
    -WorkingDirectory $repositoryRoot

Register-ScheduledTask -TaskName "TodoGraph.Api" -Action $apiAction -Trigger $trigger -Settings $settings -Principal $principal `
    -Description "Start the local TodoGraph API in the background at user logon." -Force | Out-Null
Register-ScheduledTask -TaskName "TodoGraph.WebApp" -Action $webAppAction -Trigger $trigger -Settings $settings -Principal $principal `
    -Description "Start the local TodoGraph Angular WebApp in the background at user logon." -Force | Out-Null

Start-ScheduledTask -TaskName "TodoGraph.Api"
Start-ScheduledTask -TaskName "TodoGraph.WebApp"

"Registered and started TodoGraph.Api and TodoGraph.WebApp for $currentUser."
