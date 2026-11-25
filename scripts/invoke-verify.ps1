param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('\S')]
    [string]$SessionId,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$Command
)

$ErrorActionPreference = 'Stop'
if ($env:BOY_SCOUT_VERIFY_SESSION) {
    throw "Nested verification is not supported. Run ordinary build/test commands inside the existing verification command."
}
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$artifactsDirectory = Join-Path $repositoryRoot 'artifacts'
[System.IO.Directory]::CreateDirectory($artifactsDirectory) | Out-Null
$lockPath = Join-Path $artifactsDirectory 'build-test.lock'
. (Join-Path $PSScriptRoot 'workspace-lock.ps1')
$lock = $null
$exitCode = 1

Write-Host "Verification requested by session '$SessionId' (PID $PID)."
try {
    $lock = Enter-WorkspaceQueueLock -LockPath $lockPath -SessionId $SessionId `
        -Command $Command -Description 'Workspace build/test'
    Push-Location -LiteralPath $repositoryRoot
    $env:BOY_SCOUT_VERIFY_SESSION = $SessionId
    try {
        $global:LASTEXITCODE = 0
        & ([scriptblock]::Create($Command))
        $succeeded = $?
        $exitCode = $LASTEXITCODE
        if (-not $succeeded -and $exitCode -eq 0) {
            $exitCode = 1
        }
    }
    finally {
        Remove-Item Env:BOY_SCOUT_VERIFY_SESSION -ErrorAction SilentlyContinue
        Pop-Location
    }
}
finally {
    if ($null -ne $lock) {
        $lock.Dispose()
        Write-Host "Workspace build/test lock released by session '$SessionId'."
    }
}

exit $exitCode
